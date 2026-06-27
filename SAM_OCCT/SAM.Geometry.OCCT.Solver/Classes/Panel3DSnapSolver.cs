// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

// SAM.Core.OCCT and SAM.Geometry.OCCT both declare a static Query class.
using GeometryQuery = SAM.Geometry.OCCT.Query;
using GeometryCreate = SAM.Geometry.OCCT.Create;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// True 3D analogue of <c>SAM.Geometry.Solver.SnapSolver</c>. Reuses the proven
    /// backer (<c>Weight</c>) + width (<c>BucketSize</c>) semantics, but operates on panels
    /// as faces in full 3D space instead of per-level 2D slices.
    /// </summary>
    /// <remarks>
    /// Pipeline:
    /// <list type="number">
    /// <item>Register panels as <see cref="SnappedPanel"/>s (pad parameter lists).</item>
    /// <item><b>Snap (managed):</b> within near-parallel clusters, project each lower-<c>Weight</c>
    /// panel that lies inside a higher-<c>Weight</c> backer's <c>BucketSize</c> slab onto the backer plane.</item>
    /// <item><b>Resolve (native):</b> hand the snapped face set to the OCCT kernel - MakerVolume
    /// (<c>Create.Shells</c>) splits mutual intersections and resolves junctions, then
    /// <c>MergeCoplanarFace3Ds</c> unifies resolved coplanar faces.</item>
    /// <item><b>Report:</b> <c>Validate</c> flags naked (free) boundary edges.</item>
    /// </list>
    /// When the native kernel is unavailable the managed snap result is returned unresolved and
    /// <see cref="NativeResolved"/> is false.
    /// </remarks>
    public class Panel3DSnapSolver
    {
        public const double DEFAULT_BucketSize = 0.3;
        public const double DEFAULT_Weight = 1.0;

        /// <summary>Default lateral reach a wall may grow along its axis to meet the next wall (metres) - the
        /// 3D analogue of the Solver's <c>SolverParameter.MaxExtend</c>. 0.4 m so a short wall between two
        /// door openings can still reach its neighbour. Override per panel via the <c>maxExtensions</c> input.</summary>
        public const double DEFAULT_MaxExtension = 0.4;

        /// <summary>A wall's lateral reach is additionally capped at this fraction of its own length, so a short
        /// stub cannot extend unrealistically far. Mirrors the 2D <c>SnappedWall.ExtensionLimitLengthRatio</c>.</summary>
        public const double EXTENSION_LIMIT_LENGTH_RATIO = 0.49;

        private readonly List<Face3D> face3Ds;
        private readonly List<double> bucketSizes;
        private readonly List<double> weights;
        private readonly List<double> maxExtensions;

        public double ToleranceAngle { get; set; } = 5 * (System.Math.PI / 180);
        public double ToleranceArcAngle { get; set; } = 0.3 * (System.Math.PI / 180);
        public double ToleranceDistance { get; set; } = Tolerance.Distance;

        /// <summary>
        /// When true, walls that stop short of the floor/roof above are extended up to that cap before
        /// the native resolve, so the kernel can trim them (e.g. split a gable wall at the roof pitch)
        /// and close the under-roof volume. The floor/roof elevations a wall is extended to become the
        /// implicit levels. Default true.
        /// </summary>
        public bool ExtendToCaps { get; set; } = true;

        /// <summary>How far past a flat floor cap a wall is over-extended so the native trim cuts cleanly (metres).</summary>
        public double ExtendOvershoot { get; set; } = 0.05;

        /// <summary>
        /// Walls first: before the caps are touched, grow each wall sideways along its own axis until its end
        /// runs into the next wall it points at, so the plan loop closes (the X/Y gap, not just the up/down
        /// one). Each end is extended a different distance - only as far as the wall it finds - so corners
        /// meet without distorting the layout. The reach per wall is that wall's <c>MaxExtension</c> (the
        /// 3D analogue of the Solver's <c>SolverParameter.MaxExtend</c>); set it per panel to control which
        /// walls may extend and how far. Default true.
        /// </summary>
        public bool ExtendWallsToWalls { get; set; } = true;

        /// <summary>How far past the wall it meets a wall end is over-extended, so the native trim cuts the
        /// corner cleanly (metres).</summary>
        public double WallExtendOvershoot { get; set; } = 0.05;

        /// <summary>How far past a sloped roof's ridge an under-roof wall is over-extended, so it clears the
        /// highest point of the roof and the kernel can cut it along the full pitch (metres).</summary>
        public double RoofOvershoot { get; set; } = 0.5;

        /// <summary>Stop after Step 1 (clean bucket): return the clean single panels without fill/extend/resolve.
        /// Lets bucket values be tuned and reviewed in isolation. Default false.</summary>
        public bool StopAfterClean { get; set; } = false;

        /// <summary>Stop after Step 2's managed fill + extend, before the native resolve (the split): return the
        /// filled floors/roofs and the walls extended up to their caps (overshooting), untrimmed. Lets the
        /// pre-resolve geometry be reviewed before <c>Solve3D</c> runs the native MakerVolume split. Default false.</summary>
        public bool StopAfterExtend { get; set; } = false;

        /// <summary>Step 2: grow floors/roofs out to the surrounding walls (close floor-to-wall gaps). Default true.</summary>
        public bool FillCapsToWalls { get; set; } = true;

        /// <summary>How far a floor/roof is grown outward so it overshoots the walls (and a roof reaches the
        /// ridge / the next roof slope) and trims cleanly (metres).</summary>
        public double FillMargin { get; set; } = 0.5;

        /// <summary>How far past the wall plane a cap is grown once the measured gap is closed - a small hedge
        /// so the floor genuinely crosses the wall (which MakerVolume can cut) rather than touching it
        /// tangentially. Independent of <see cref="ExtendOvershoot"/> (the wall→cap reach). Set to 0 to grow
        /// each cap *exactly* to the wall plane and let the post-resolve sew bond the coincident edges
        /// instead (the grow-to-plane-and-sew alternative). Default 0.05 m.</summary>
        public double FillOvershoot { get; set; } = 0.05;

        /// <summary>
        /// Re-attach any face the native MakerVolume dropped - walls AND caps (floors/roofs) alike. The
        /// kernel returns only faces that bound a closed cell, so a face whose cell fails to form (e.g. a
        /// stepped/tilted region the kernel cannot close, or a roof lid the cells cap off) is silently
        /// discarded, leaving a hole. This re-adds every face that went into the volume build but has no
        /// representation in the resolved output, using its extended geometry (the exact face the kernel
        /// saw, grown/overshooting) so the re-added face reaches its neighbours and closes the gap. Subsumes
        /// the old separate roof-lid re-attach: a dropped sloped roof is just another dropped face. Default true.
        /// </summary>
        public bool RetainDropped { get; set; } = true;

        /// <summary>Step 2: after the resolve, re-sew the resolved faces at an expanded tolerance to stitch the
        /// floor/wall slot gaps that survive the volume build, instead of patching them with fabricated faces.
        /// The sewn result is kept only when it strictly reduces the naked-edge count. Default true.</summary>
        public bool SewResidualGaps { get; set; } = true;

        /// <summary>Upper bound (metres) on the post-resolve sew tolerance - how wide a residual floor/wall slot
        /// the sew may bridge. Larger than the pre-build <c>SewingTolerance</c> (which only closes sub-cm gaps),
        /// but clamped (≤ 0.3 m) so unrelated near edges are not over-merged. Default 0.1 m.</summary>
        public double SewExpandTolerance { get; set; } = 0.1;

        /// <summary>Step 2: after the resolve (and the sew pass), build a Face3D over each residual naked-boundary
        /// loop (air-panel candidate) so every space is fully enclosed. Default true. (Step 1 strips input holes
        /// outright.)</summary>
        public bool FillHoles { get; set; } = true;

        /// <summary>Angle within which a panel's normal counts as horizontal, so the panel is "vertical" (a wall).</summary>
        public double VerticalAngleTolerance { get; set; } = 20 * (System.Math.PI / 180);

        /// <summary>
        /// Max perpendicular offset (metres) at which two consecutive segments of one vertical wall run -
        /// abutting/overlapping along the run, heights overlapping - are aligned onto a single plane. Closes
        /// the small Y-jog where an imported side wall steps from one segment to the next. Kept below the
        /// gap between genuinely separate parallel walls (e.g. adjacent rooms) so those are not merged.
        /// Default 0.3 m; raise to align larger jogs, set to 0 to disable colinear alignment.
        /// </summary>
        public double AlignColinearOffset { get; set; } = 0.3;

        /// <summary>
        /// Max perpendicular offset (metres) within which the floor/roof caps of one level are normalized
        /// onto a single plane. After the bucket snap, near-parallel caps whose planes lie within this band
        /// of one another are all projected onto the dominant (largest-area) cap's plane - the "level plane".
        /// This collapses the small plane differences left when several separately-imported floor/roof tiles
        /// covering one space were merged at slightly different tilts/elevations; those differences otherwise
        /// stop the native kernel closing the cell. Floors and roofs separate automatically (a floor and the
        /// roof above are parallel but far more than this offset apart). Default 0.3 m; set to 0 to disable.
        /// </summary>
        public double NormalizeCapOffset { get; set; } = 0.3;

        /// <summary>Also extend walls up to a sloped roof above (not just horizontal floor caps), so the kernel
        /// can cut them at the pitch and enclose the under-roof space. Default true.</summary>
        public bool ExtendToRoofs { get; set; } = true;

        /// <summary>The registered panels after the managed snap stage. Carries source mapping.</summary>
        public List<SnappedPanel> SnappedPanels { get; private set; } = new List<SnappedPanel>();

        /// <summary>The resolved faces. After native resolve these are split/merged; otherwise the snapped faces.</summary>
        public List<Face3D> ResolvedFace3Ds { get; private set; } = new List<Face3D>();

        /// <summary>Locations of naked (free) boundary edges reported by the native validator.</summary>
        public List<Point3D> NakedEdgePoint3Ds { get; private set; } = new List<Point3D>();

        /// <summary>True when the native OCCT kernel ran the resolve stage; false for a managed-only result.</summary>
        public bool NativeResolved { get; private set; }

        /// <summary>Number of closed cells (rooms/levels) the native MakerVolume formed. 1 = single space; 0 = none.</summary>
        public int ResolvedCellCount { get; private set; }

        /// <summary>Step 1 output: clean single panels - external shape only, within-bucket parallels snapped
        /// onto one backer, contained/overlapping coplanar faces merged. The input to Step 2 (fill/extend).</summary>
        public List<Face3D> CleanFace3Ds { get; private set; } = new List<Face3D>();

        /// <summary>The face set fed to the native MakerVolume - after the clean bucket, fill, extend and the
        /// coplanar pre-merge ("after bucket merge"). Exposed for visual debugging of the pre-resolve state.</summary>
        public List<Face3D> BucketMergedFace3Ds { get; private set; } = new List<Face3D>();

        /// <summary>New faces created to close holes (internal openings) in the input panels. These are the
        /// air-panel candidates: the analytical wrapper turns them into <c>PanelType.Air</c> panels.</summary>
        public List<Face3D> HoleFillFace3Ds { get; private set; } = new List<Face3D>();

        /// <summary>
        /// Plan-closure diagnostic, measured on the walls AFTER the managed extend/fill but BEFORE the native
        /// resolve: the locations of wall-foot endpoints that no other wall meets in plan (XY). A naked end
        /// here means the wall loop is still open at that corner, so floors/roofs cannot fill into a closed
        /// polysurface. Empty means every wall end is met by another wall (the loops close). These are the
        /// spots to upgrade extend or bucket size. See also <see cref="OpenWallFace3Ds"/>.
        /// </summary>
        public List<Point3D> OpenWallEndPoint3Ds { get; private set; } = new List<Point3D>();

        /// <summary>The wall faces that still have at least one open (naked-in-plan) end after the managed
        /// extend/fill - the panels to upgrade (raise MaxExtend / bucket) so their loop closes.</summary>
        public List<Face3D> OpenWallFace3Ds { get; private set; } = new List<Face3D>();

        /// <summary>How close (in plan) another wall foot must come to a wall end for that end to count as
        /// "met" (closed). Above the extend overshoot so a wall extended up to its neighbour reads as
        /// connected; tight enough to flag a real gap. Default 0.1 m.</summary>
        public double ConnectionTolerance { get; set; } = 0.1;

        /// <summary>
        /// The building/level "up" axis. The Step-2 extend logic (walls vertical, caps above/below, plan =
        /// XY) is expressed in world Z; when a whole level is tilted - its floors/roofs (and the walls that
        /// run across the slope) are not aligned with world Z - that logic must run in the level's own frame.
        /// Setting <see cref="Up"/> to the level normal makes Step 2 rotate the clean faces so this axis maps
        /// to world Z, extend there, then rotate back. Null or world Z = no rotation (the ordinary case).
        /// Step 1 (clean bucket) and the native resolve are orientation-agnostic and are unaffected.
        /// </summary>
        public Vector3D Up { get; set; }

        public Panel3DSnapSolver(
            IEnumerable<Face3D> face3Ds,
            IEnumerable<double> bucketSizes = null,
            IEnumerable<double> weights = null,
            IEnumerable<double> maxExtensions = null)
        {
            this.face3Ds = face3Ds == null ? new List<Face3D>() : face3Ds.ToList();
            this.bucketSizes = bucketSizes?.ToList();
            this.weights = weights?.ToList();
            this.maxExtensions = maxExtensions?.ToList();
        }

        public void Execute(OcctBuildOptions options = null)
        {
            SnappedPanels = new List<SnappedPanel>();
            CleanFace3Ds = new List<Face3D>();
            ResolvedFace3Ds = new List<Face3D>();
            NakedEdgePoint3Ds = new List<Point3D>();
            HoleFillFace3Ds = new List<Face3D>();
            BucketMergedFace3Ds = new List<Face3D>();
            OpenWallEndPoint3Ds = new List<Point3D>();
            OpenWallFace3Ds = new List<Face3D>();
            NativeResolved = false;
            ResolvedCellCount = 0;

            if (face3Ds == null || face3Ds.Count == 0)
            {
                return;
            }

            List<double> bucketSizes_Adjusted = AdjustListLength(bucketSizes, face3Ds.Count, DEFAULT_BucketSize);
            List<double> weights_Adjusted = AdjustListLength(weights, face3Ds.Count, DEFAULT_Weight);
            List<double> maxExtensions_Adjusted = AdjustListLength(maxExtensions, face3Ds.Count, DEFAULT_MaxExtension);

            SnappedPanels = Register(face3Ds, bucketSizes_Adjusted, weights_Adjusted, maxExtensions_Adjusted);

            // ---- Step 1: clean bucket (managed, native-free) ----
            // Strip holes -> bucket-snap within-bucket parallels onto one backer -> merge coplanar (contained/overlap).
            CleanFace3Ds = CleanBucket(SnappedPanels, ToleranceAngle, ToleranceArcAngle, ToleranceDistance, VerticalAngleTolerance, AlignColinearOffset, NormalizeCapOffset);

            if (StopAfterClean)
            {
                ResolvedFace3Ds = CleanFace3Ds;
                return;
            }

            // ---- Step 2: extend + resolve ----
            // Step 2's extend logic is written for a world-Z-up building (walls vertical, caps above/below
            // in Z, plan = XY). When the whole level is tilted, rotate the clean faces into a canonical
            // Z-up frame (mapping the level Up axis onto world Z), run the extend there, then rotate the
            // result back. For the ordinary upright case (Up null or already Z) no rotation happens.
            Vector3D up = (Up == null || Up.Length <= ToleranceDistance) ? new Vector3D(0, 0, 1) : Up.Unit;
            if (up.Z < 0)
            {
                up = up.GetNegated(); // axis only: pick the +Z hemisphere so the tilt angle stays below 90 deg
            }

            Transform3D toCanonical = null;
            Transform3D fromCanonical = null;
            double tiltAngle = up.SmallestAngle(new Vector3D(0, 0, 1));
            if (tiltAngle > ToleranceAngle)
            {
                // The level plane (normal = up) at the world origin. GetOriginToPlane expresses a world
                // vector in that plane's frame, so it maps up -> world Z (and the level plane -> XY);
                // GetPlaneToOrigin is its inverse, rotating the extended result back.
                Plane levelPlane = new Plane(new Point3D(0, 0, 0), up);
                toCanonical = Transform3D.GetOriginToPlane(levelPlane);
                fromCanonical = Transform3D.GetPlaneToOrigin(levelPlane);
            }

            // Re-wrap the clean panels: Step 1 merged/removed panels, so the per-source weights no longer
            // apply, and bucket/weight are re-derived from geometry. The per-panel MaxExtend IS carried
            // forward (positionally), so a wall the caller marked to extend further keeps that reach. The
            // rotation preserves order and validity, so the maxExtensions stay index-aligned.
            List<Face3D> step2Face3Ds = toCanonical == null
                ? CleanFace3Ds
                : CleanFace3Ds.Select(x => x.Transform(toCanonical)).ToList();

            SnappedPanels = Register(
                step2Face3Ds,
                AdjustListLength(null, step2Face3Ds.Count, DEFAULT_BucketSize),
                AdjustListLength(null, step2Face3Ds.Count, DEFAULT_Weight),
                AdjustListLength(maxExtensions, step2Face3Ds.Count, DEFAULT_MaxExtension));

            // ---- Walls first ----
            // Close the plan loop: grow each wall sideways along its axis (up to its own MaxExtend) until its
            // end meets the next wall, so the X/Y gaps (the ones the up/down extend below cannot touch) close
            // into corners.
            if (ExtendWallsToWalls)
            {
                ExtendWalls(SnappedPanels, VerticalAngleTolerance, WallExtendOvershoot, ToleranceDistance);
            }

            // Extend walls up to the floor/roof above and down to the floor below (the "between floors" case).
            if (ExtendToCaps)
            {
                Extend(SnappedPanels, VerticalAngleTolerance, ExtendOvershoot, ToleranceDistance, RoofOvershoot, ExtendToRoofs);
            }

            // ---- Then floors and roofs ----
            // Grow the caps out to the now-closed walls so the floor/roof-to-wall gaps close.
            if (FillCapsToWalls)
            {
                Fill(SnappedPanels, VerticalAngleTolerance, FillMargin, ToleranceDistance, FillOvershoot);
            }

            // Plan-closure diagnostic: which wall ends are STILL open after the managed extend? These are the
            // panels to upgrade (raise MaxExtend / bucket) before the floors/roofs can fill a closed polysurface.
            OpenWallEndPoint3Ds = OpenWallEnds(SnappedPanels, VerticalAngleTolerance, ConnectionTolerance, ToleranceDistance, out List<Face3D> openWallFace3Ds);
            OpenWallFace3Ds = openWallFace3Ds;

            List<Face3D> snappedFace3Ds = SnappedPanels.Select(x => x.Face3D).Where(x => x != null && x.IsValid()).ToList();

            // Back to the world frame: the extend ran in the canonical Z-up frame, so rotate the extended
            // faces and the plan-closure diagnostics back to where the input lives before resolving/output.
            if (fromCanonical != null)
            {
                snappedFace3Ds = snappedFace3Ds.Select(x => x.Transform(fromCanonical)).Where(x => x != null && x.IsValid()).ToList();
                OpenWallEndPoint3Ds = OpenWallEndPoint3Ds?.Where(x => x != null).Select(x => x.Transform(fromCanonical)).ToList() ?? new List<Point3D>();
                OpenWallFace3Ds = OpenWallFace3Ds?.Where(x => x != null && x.IsValid()).Select(x => x.Transform(fromCanonical)).Where(x => x != null && x.IsValid()).ToList() ?? new List<Face3D>();
            }

            ResolvedFace3Ds = snappedFace3Ds;

            // Stop before the native resolve: the split (MakerVolume trim) stays in Solve3D. The output here
            // is the filled caps + extended (overshooting) walls, for reviewing the pre-resolve geometry.
            if (StopAfterExtend)
            {
                return;
            }

            Resolve(snappedFace3Ds, options);

            // MakerVolume returns only faces that bound a closed cell, so any face whose cell does not form
            // - a wall or a cap (floor/roof) in a stepped/tilted region the kernel cannot close, or a roof
            // lid the cells cap off - is dropped, leaving a hole. Re-add every face that went into the
            // volume build but has no representation in the resolved output, using its extended geometry so
            // the re-added face overshoots its neighbours and closes the gap. Walls and caps are treated the
            // same: a dropped cap comes back grown (not the ungrown clean slab), so it reaches its walls.
            if (RetainDropped && NativeResolved && ResolvedFace3Ds != null)
            {
                List<Face3D> dropped = new List<Face3D>();
                foreach (Face3D face3D in snappedFace3Ds)
                {
                    if (face3D != null && face3D.IsValid() && !IsRepresented(face3D, ResolvedFace3Ds))
                    {
                        dropped.Add(face3D);
                    }
                }

                if (dropped.Count != 0)
                {
                    ResolvedFace3Ds = ResolvedFace3Ds.Concat(dropped).ToList();
                }
            }
        }

        /// <summary>
        /// True when some resolved face actually covers <paramref name="face3D"/> (a wall or a cap): lies on
        /// the same plane (parallel normal, near-coincident) AND its boundary contains the face's centre. A
        /// face the native resolve kept (whole or split into sub-faces) is represented; a face it dropped is
        /// not. The centre-inside-boundary test (not a bbox test) is what distinguishes faces sharing one
        /// infinite plane: the stepped ramp's slabs all lie on one plane and only touch at their edges, and
        /// a tiny wall's centre can fall inside a coplanar neighbour's bounding box without being inside the
        /// neighbour's actual face - either would be wrongly called "represented" by a looser test. Used by
        /// RetainDropped.
        /// </summary>
        private static bool IsRepresented(Face3D face3D, List<Face3D> resolvedFace3Ds)
        {
            Plane plane = face3D?.GetPlane();
            BoundingBox3D box = face3D?.GetBoundingBox();
            if (plane == null || box == null)
            {
                return true; // cannot test - do not re-add an untestable face
            }

            Point3D centre = box.GetCentroid();
            if (centre == null)
            {
                return true;
            }

            Vector3D normal = plane.Normal.Unit;
            foreach (Face3D resolved in resolvedFace3Ds)
            {
                Plane resolvedPlane = resolved?.GetPlane();
                if (resolvedPlane == null)
                {
                    continue;
                }

                if (System.Math.Abs(normal.DotProduct(resolvedPlane.Normal.Unit)) < 0.99)
                {
                    continue; // not parallel - a different orientation
                }

                if (System.Math.Abs(plane.Distance(resolvedPlane.Origin)) > 0.05)
                {
                    continue; // parallel but a different (offset) plane
                }

                if (!ContainsPoint(resolved.GetBoundingBox(), centre, 0.05))
                {
                    continue; // cheap reject before the planar containment test
                }

                // Accurate: is the centre actually inside this resolved face's boundary (not just its box)?
                Geometry.Planar.Face2D resolved2D = resolvedPlane.Convert(resolved);
                Geometry.Planar.Point2D centre2D = resolvedPlane.Convert(centre);
                if (resolved2D != null && centre2D != null
                    && (Geometry.Planar.Query.Inside(resolved2D, centre2D, 0.01) || resolved2D.On(centre2D, 0.01)))
                {
                    return true; // a resolved sub-face on this plane genuinely covers this face's centre
                }
            }

            return false;
        }

        /// <summary>True when <paramref name="point3D"/> lies inside the box, grown by <paramref name="tolerance"/>.</summary>
        private static bool ContainsPoint(BoundingBox3D boundingBox3D, Point3D point3D, double tolerance)
        {
            if (boundingBox3D == null || point3D == null)
            {
                return false;
            }

            Point3D min = boundingBox3D.Min;
            Point3D max = boundingBox3D.Max;
            return point3D.X >= min.X - tolerance && point3D.X <= max.X + tolerance
                && point3D.Y >= min.Y - tolerance && point3D.Y <= max.Y + tolerance
                && point3D.Z >= min.Z - tolerance && point3D.Z <= max.Z + tolerance;
        }

        /// <summary>
        /// Step 1 - clean bucket (managed, native-free). Produces clean single panels: each panel is reduced to
        /// its external shape (internal openings stripped), within-bucket near-parallel lower-weight panels are
        /// projected onto their backer plane, then coplanar faces - including a smaller panel contained in a
        /// larger one - are merged via the managed union. The output feeds Step 2 (fill/extend), or is returned
        /// as-is when only cleaning is wanted (<see cref="StopAfterClean"/>).
        /// </summary>
        public static List<Face3D> CleanBucket(List<SnappedPanel> panels, double toleranceAngle, double toleranceArcAngle, double toleranceDistance, double verticalAngleTolerance = 20 * (System.Math.PI / 180), double alignColinearOffset = 0.3, double normalizeCapOffset = 0.3)
        {
            if (panels == null || panels.Count == 0)
            {
                return new List<Face3D>();
            }

            // 1. External shape only - drop window/door openings.
            foreach (SnappedPanel panel in panels)
            {
                panel.StripInternalEdges();
            }

            // 1b. Collapse back-to-back partitions BEFORE the weighted bucket snap can pull them onto one
            //     side. Two near-coincident, in-plane-overlapping faces with OPPOSING (anti-parallel) normals
            //     AND (near) equal area are the two room-facing skins of one shared partition - one wall per
            //     room. The weighted snap below projects the lighter skin onto the heavier backer, which puts the
            //     partition ~a wall-thickness off the lighter room's cap edges and detaches it, so that room
            //     cannot close and the two adjacent rooms merge into a single cell; collapsing both skins onto
            //     the smaller (fragile) room's plane closes both rooms instead (see SnapOpposedPartitions). The
            //     equal-area gate is essential: without it a long shared wall caught against a short partition
            //     skin, or the differently-sized walls of two adjacent grid rooms, are mis-collapsed and merge
            //     rooms. Same-facing duplicates (a wall imported twice) keep parallel normals and are left to the
            //     weighted snap. Runs in the clean (world) frame, so it is keyed on the opposing-normal geometry,
            //     not the IsVertical test (which a tilted wall fails here).
            SnapOpposedPartitions(panels, toleranceAngle, toleranceDistance);

            // 2. Bucket snap - bring within-bucket near-parallel, in-plane-overlapping panels onto one
            //    backer plane (now coplanar), and align consecutive vertical wall segments offset by a small
            //    step jog. Non-overlapping, non-colinear parallels (separate bays) are left put.
            Snap(panels, toleranceAngle, toleranceArcAngle, toleranceDistance, verticalAngleTolerance, alignColinearOffset);

            // 2b. Normalize caps onto one level plane - project the near-parallel, within-offset floor/roof
            //     tiles of a level onto the dominant cap's plane. Unlike the snap above (which needs an
            //     in-plane overlap), this groups purely by perpendicular nearness, so adjacent (edge-touching)
            //     tiles of one slab - merged at slightly different tilts/elevations - collapse onto a single
            //     plane and the coplanar merge below can fuse them, letting the kernel close the cell.
            NormalizeCaps(panels, toleranceAngle, normalizeCapOffset, toleranceDistance, verticalAngleTolerance);

            // 3. Coplanar merge - union coplanar/overlapping faces so a contained smaller panel collapses into one.
            List<Face3D> face3Ds = panels.Select(x => x.Face3D).Where(x => x != null && x.IsValid()).ToList();
            List<Face3D> merged = Geometry.Spatial.Query.Union(face3Ds, toleranceDistance);
            if (merged == null || merged.Count == 0)
            {
                merged = face3Ds;
            }

            return merged.Where(x => x != null && x.IsValid()).ToList();
        }

        /// <summary>
        /// Walls first - close the plan loop. Grow each (vertical) wall sideways along its own axis until
        /// each end runs into the next wall it points at, so the X/Y gaps between wall ends close into
        /// corners (the up/down <see cref="Extend"/> cannot touch these - it only moves a wall's top and
        /// base). Each end is handled independently: it extends only as far as the nearest other wall its
        /// axis crosses within that wall's own <c>MaxExtension</c> reach (the Solver's
        /// <c>SolverParameter.MaxExtend</c> - a different reach per direction, and per panel), plus a small
        /// <paramref name="overshoot"/> so the native trim cuts the corner cleanly. A wall whose
        /// <c>MaxExtension</c> is non-positive is not extended at all; an end with no wall in reach, and a
        /// wall parallel to its neighbour, are left where they are. Walls are matched in plan (XY) only -
        /// their elevations are irrelevant to whether they meet at a corner.
        /// </summary>
        public static void ExtendWalls(List<SnappedPanel> panels, double verticalAngleTolerance, double overshoot, double toleranceDistance)
        {
            if (panels == null || panels.Count < 2)
            {
                return;
            }

            // Collect the walls and their foot segments (axis + plan footprint) once, up front, so every
            // reach is measured against the original wall lines (deterministic, order-independent).
            List<SnappedPanel> walls = new List<SnappedPanel>();
            List<Segment3D> feet = new List<Segment3D>();
            foreach (SnappedPanel panel in panels)
            {
                if (!panel.IsVertical(verticalAngleTolerance))
                {
                    continue; // only walls run along the plan; floors/roofs are the caps
                }

                Segment3D foot = panel.GetBaseSegment(toleranceDistance);
                if (foot == null)
                {
                    continue;
                }

                walls.Add(panel);
                feet.Add(foot);
            }

            if (walls.Count < 2)
            {
                return;
            }

            // Hand the wall foot-lines (in plan) to the proven 2D ExtensionSolver: it builds each end's
            // extension reach (capped at min(MaxExtension, length * ExtensionLimitLengthRatio)), registers
            // the intersections of every extended end against every other wall, and greedily resolves each
            // naked end to the cheapest junction - extending BOTH walls to a shared corner where neither
            // currently reaches the other, and trimming overshoots. This closes L-corners and T-junctions
            // the old per-end ray scan (which only reached a wall it already crossed) left open.
            List<Geometry.Planar.Segment2D> lines = new List<Geometry.Planar.Segment2D>(walls.Count);
            List<double> maxExtensions = new List<double>(walls.Count);
            foreach (Segment3D foot in feet)
            {
                Point3D s = foot.GetStart();
                Point3D e = foot.GetEnd();
                lines.Add(new Geometry.Planar.Segment2D(new Geometry.Planar.Point2D(s.X, s.Y), new Geometry.Planar.Point2D(e.X, e.Y)));
            }

            foreach (SnappedPanel wall in walls)
            {
                maxExtensions.Add(System.Math.Max(0, wall.MaxExtension));
            }

            List<Geometry.Planar.Segment2D> resolved;
            try
            {
                resolved = new global::SAM.Geometry.Solver.ExtensionSolver(lines, maxExtensions, toleranceDistance).Solve();
            }
            catch
            {
                return; // never let the plan-loop close abort the solve
            }

            if (resolved == null || resolved.Count != walls.Count)
            {
                return;
            }

            for (int i = 0; i < walls.Count; i++)
            {
                Geometry.Planar.Segment2D original = lines[i];
                Geometry.Planar.Segment2D result = resolved[i];
                if (result == null)
                {
                    continue;
                }

                Geometry.Planar.Point2D oStart = original.GetStart();
                Geometry.Planar.Point2D oEnd = original.GetEnd();
                Geometry.Planar.Point2D rStart = result.GetStart();
                Geometry.Planar.Point2D rEnd = result.GetEnd();

                // Unchanged within tolerance -> leave the wall exactly where it was (no needless move).
                if (rStart.Distance(oStart) <= toleranceDistance && rEnd.Distance(oEnd) <= toleranceDistance)
                {
                    continue;
                }

                // Over-extend the ends that grew (not the trimmed ones) by the overshoot, so the native
                // MakerVolume gets a clean crossing at the corner rather than an exact touch.
                double length = original.GetLength();
                if (length <= toleranceDistance)
                {
                    continue;
                }

                double ux = (oEnd.X - oStart.X) / length;
                double uy = (oEnd.Y - oStart.Y) / length;
                double startParam = (rStart.X - oStart.X) * ux + (rStart.Y - oStart.Y) * uy; // <0 => start end extended
                double endParam = (rEnd.X - oStart.X) * ux + (rEnd.Y - oStart.Y) * uy;       // >length => end end extended

                double nsX = rStart.X, nsY = rStart.Y, neX = rEnd.X, neY = rEnd.Y;
                if (overshoot > toleranceDistance && startParam < -toleranceDistance)
                {
                    nsX -= ux * overshoot;
                    nsY -= uy * overshoot;
                }

                if (overshoot > toleranceDistance && endParam > length + toleranceDistance)
                {
                    neX += ux * overshoot;
                    neY += uy * overshoot;
                }

                walls[i].SetVerticalFootprint(new Geometry.Planar.Point2D(nsX, nsY), new Geometry.Planar.Point2D(neX, neY), toleranceDistance);
            }
        }

        /// <summary>
        /// Plan-closure diagnostic. For each wall (vertical panel), checks whether each of its two foot
        /// endpoints is met by another wall in plan (XY) - i.e. some other wall's foot passes within
        /// <paramref name="connectionTolerance"/> of the endpoint (an L-corner, a T-junction, or a crossing).
        /// An endpoint no wall meets is "open": the wall loop does not close there, so a floor/roof cannot
        /// fill into a closed polysurface around it. Returns the open endpoint locations (the corners to fix)
        /// and, via <paramref name="openWallFace3Ds"/>, the wall faces that own at least one open end (the
        /// panels to upgrade - raise MaxExtend or bucket size). This is the managed, native-free analogue of
        /// the 2D solver's naked-node marking, and the signal a future auto-tune would escalate on.
        /// </summary>
        public static List<Point3D> OpenWallEnds(List<SnappedPanel> panels, double verticalAngleTolerance, double connectionTolerance, double toleranceDistance, out List<Face3D> openWallFace3Ds)
        {
            List<Point3D> openEnds = new List<Point3D>();
            openWallFace3Ds = new List<Face3D>();
            if (panels == null || panels.Count == 0)
            {
                return openEnds;
            }

            // Collect the walls and their plan feet once.
            List<SnappedPanel> walls = new List<SnappedPanel>();
            List<Segment3D> feet = new List<Segment3D>();
            foreach (SnappedPanel panel in panels)
            {
                if (!panel.IsVertical(verticalAngleTolerance))
                {
                    continue;
                }

                Segment3D foot = panel.GetBaseSegment(toleranceDistance);
                if (foot == null)
                {
                    continue;
                }

                walls.Add(panel);
                feet.Add(foot);
            }

            for (int i = 0; i < walls.Count; i++)
            {
                Point3D start = feet[i].GetStart();
                Point3D end = feet[i].GetEnd();

                bool startOpen = !EndMetByAnotherWall(start, i, feet, connectionTolerance);
                bool endOpen = !EndMetByAnotherWall(end, i, feet, connectionTolerance);

                if (startOpen)
                {
                    openEnds.Add(start);
                }

                if (endOpen)
                {
                    openEnds.Add(end);
                }

                if ((startOpen || endOpen) && walls[i].Face3D != null)
                {
                    openWallFace3Ds.Add(walls[i].Face3D);
                }
            }

            return openEnds;
        }

        /// <summary>True when some wall other than <paramref name="self"/> passes within
        /// <paramref name="connectionTolerance"/> of the endpoint in plan (XY), so the end is met (closed).</summary>
        private static bool EndMetByAnotherWall(Point3D endpoint, int self, List<Segment3D> feet, double connectionTolerance)
        {
            for (int k = 0; k < feet.Count; k++)
            {
                if (k == self)
                {
                    continue;
                }

                Point3D a = feet[k].GetStart();
                Point3D b = feet[k].GetEnd();
                if (PlanDistancePointToSegment(endpoint.X, endpoint.Y, a.X, a.Y, b.X, b.Y) <= connectionTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Shortest distance in the XY plane from point (px, py) to the segment (ax, ay)-(bx, by).</summary>
        private static double PlanDistancePointToSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            double ex = bx - ax;
            double ey = by - ay;
            double lengthSquared = ex * ex + ey * ey;
            double t = lengthSquared <= 1e-18 ? 0 : ((px - ax) * ex + (py - ay) * ey) / lengthSquared;
            t = System.Math.Max(0, System.Math.Min(1, t));
            double cx = ax + t * ex;
            double cy = ay + t * ey;
            double dx = px - cx;
            double dy = py - cy;
            return System.Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Managed extend: grow each (vertical) wall up to the nearest cap - the floor or roof that
        /// sits above it and covers it in plan - so the native resolve can trim the wall against that
        /// cap and close the volume. The cap a wall reaches defines its implicit upper level; a wall
        /// under a pitched roof is over-extended past the ridge so the roof faces split it at the pitch.
        /// Walls with no cap above (true parapets/outer tops) are left untouched.
        /// </summary>
        public static void Extend(List<SnappedPanel> panels, double verticalAngleTolerance, double overshoot, double toleranceDistance, double roofOvershoot = 0.5, bool includeRoofs = true)
        {
            if (panels == null || panels.Count < 2)
            {
                return;
            }

            List<SnappedPanel> walls = new List<SnappedPanel>();
            List<BoundingBox3D> capBoxes = new List<BoundingBox3D>();
            List<Plane> capPlanes = new List<Plane>();
            foreach (SnappedPanel panel in panels)
            {
                BoundingBox3D boundingBox3D = panel.GetBoundingBox();
                if (boundingBox3D == null)
                {
                    continue;
                }

                if (panel.IsVertical(verticalAngleTolerance))
                {
                    walls.Add(panel);
                    continue;
                }

                // Floors/flat ceilings are always caps. Sloped roofs are caps only when includeRoofs:
                // a wall under the roof is over-extended past the ridge so the kernel cuts it at the pitch
                // and encloses the under-roof space. With it off, a roof would bury the wall in a tall box.
                bool horizontal = boundingBox3D.Max.Z - boundingBox3D.Min.Z <= toleranceDistance + 0.1;
                if (horizontal || includeRoofs)
                {
                    capBoxes.Add(boundingBox3D);
                    capPlanes.Add(panel.Plane);
                }
            }

            foreach (SnappedPanel wall in walls)
            {
                BoundingBox3D wallBox = wall.GetBoundingBox();
                if (wallBox == null)
                {
                    continue;
                }

                // Whether a cap sits above (or below) a wall is decided by the cap surface DIRECTLY ABOVE the
                // wall - the cap plane evaluated at the wall's plan centre - not by the cap's bounding-box
                // Min/Max Z. For a flat floor the two are identical; for a SLOPED roof they diverge: the roof's
                // eave (bbox Min.Z) can sit below the wall top while the roof surface over the wall is well
                // above it (a large space, where the slope spans a wide Z range). Gating on bbox Min.Z then
                // wrongly rejects that roof as "not above the wall" and leaves the wall short of it - the
                // reported tilted-roof gap. Evaluating the cap over the wall closes it.
                double wallPlanX = 0.5 * (wallBox.Min.X + wallBox.Max.X);
                double wallPlanY = 0.5 * (wallBox.Min.Y + wallBox.Max.Y);
                double wallTopZ = wallBox.Max.Z;

                // The nearest cap whose surface above the wall sits above the wall top and covers it in plan.
                BoundingBox3D nearestCap = null;
                double nearestCapZ = double.MaxValue;
                for (int i = 0; i < capBoxes.Count; i++)
                {
                    BoundingBox3D cap = capBoxes[i];
                    if (!OverlapsInPlan(cap, wallBox, toleranceDistance))
                    {
                        continue;
                    }

                    double capZ = CapZAtPlan(capPlanes[i], wallPlanX, wallPlanY, cap.Max.Z);
                    if (capZ < wallTopZ - toleranceDistance)
                    {
                        continue; // the cap surface above the wall is below the wall top - not a cap above
                    }

                    if (capZ < nearestCapZ)
                    {
                        nearestCapZ = capZ;
                        nearestCap = cap;
                    }
                }

                if (nearestCap != null)
                {
                    // Extend to the top of the covering cap (a roof slope's ridge, or a flat floor) plus an
                    // overshoot so the kernel trims the wall cleanly along the cap. A roof gets a larger
                    // overshoot so the under-roof wall clears the pitch.
                    bool capIsRoof = nearestCap.Max.Z - nearestCap.Min.Z > toleranceDistance + 0.1;
                    double os = capIsRoof ? roofOvershoot : overshoot;
                    wall.ExtendTopTo(nearestCap.Max.Z + os, toleranceDistance);
                }

                // ...and down to the nearest cap below, so the wall reaches the floor of its level and the
                // room can close at the bottom (the "between floors" case). Same surface-above-the-wall
                // measure, mirrored: the cap whose surface directly under the wall is highest, yet still
                // below the wall base.
                double wallBottomZ = wallBox.Min.Z;
                BoundingBox3D nearestBelow = null;
                double nearestBelowZ = double.MinValue;
                for (int i = 0; i < capBoxes.Count; i++)
                {
                    BoundingBox3D cap = capBoxes[i];
                    if (!OverlapsInPlan(cap, wallBox, toleranceDistance))
                    {
                        continue;
                    }

                    double capZ = CapZAtPlan(capPlanes[i], wallPlanX, wallPlanY, cap.Min.Z);
                    if (capZ > wallBottomZ + toleranceDistance)
                    {
                        continue; // the cap surface under the wall is above the wall base - not a cap below
                    }

                    if (capZ > nearestBelowZ)
                    {
                        nearestBelowZ = capZ;
                        nearestBelow = cap;
                    }
                }

                if (nearestBelow != null)
                {
                    wall.ExtendBottomTo(nearestBelow.Min.Z - overshoot, toleranceDistance);
                }
            }
        }

        /// <summary>
        /// Elevation of a (non-vertical) cap's plane directly above/below the plan point (<paramref name="x"/>,
        /// <paramref name="y"/>) - the Z at which the cap surface crosses the vertical line through that point.
        /// For a flat cap this is just the cap elevation; for a sloped roof it is the roof height at that plan
        /// location, which is what decides whether the roof sits above a given wall (its bounding-box Min/Max Z
        /// does not). Falls back to <paramref name="fallback"/> when the plane is (near) vertical, so its Z over
        /// a plan point is undefined.
        /// </summary>
        private static double CapZAtPlan(Plane capPlane, double x, double y, double fallback)
        {
            if (capPlane == null)
            {
                return fallback;
            }

            Vector3D normal = capPlane.Normal?.Unit;
            Point3D origin = capPlane.Origin;
            if (normal == null || origin == null || System.Math.Abs(normal.Z) <= 1e-9)
            {
                return fallback;
            }

            return origin.Z - (normal.X * (x - origin.X) + normal.Y * (y - origin.Y)) / normal.Z;
        }

        /// <summary>
        /// Step 2 - fill floors/roofs to walls: grow each (non-vertical) cap outward in its plane so it
        /// overshoots the surrounding walls, closing the floor/roof-to-wall gaps that otherwise leave naked
        /// edges and prevent any cell from closing. The native resolve trims the overshoot back at the walls.
        /// </summary>
        public static void Fill(List<SnappedPanel> panels, double verticalAngleTolerance, double margin, double toleranceDistance, double overshoot = 0.05)
        {
            if (panels == null || panels.Count == 0 || margin <= toleranceDistance)
            {
                return;
            }

            // The walls each cap grows toward. Measuring the gap to these (rather than blindly offsetting by
            // the full margin) lets a cap reach exactly the walls it is short of and no further - the kernel
            // trims the small overshoot. A cap with no wall in reach falls back to the fixed-margin grow.
            List<SnappedPanel> walls = panels.Where(x => x.IsVertical(verticalAngleTolerance)).ToList();

            foreach (SnappedPanel panel in panels)
            {
                if (panel.IsVertical(verticalAngleTolerance)) // floors and roofs are the caps
                {
                    continue;
                }

                if (!panel.GrowOutwardTo(walls, margin, overshoot, toleranceDistance))
                {
                    panel.GrowOutward(margin, toleranceDistance);
                }
            }
        }

        /// <summary>True when the two boxes overlap in the XY (plan) projection within a tolerance.</summary>
        private static bool OverlapsInPlan(BoundingBox3D a, BoundingBox3D b, double tolerance)
        {
            return a.Min.X <= b.Max.X + tolerance && a.Max.X >= b.Min.X - tolerance
                && a.Min.Y <= b.Max.Y + tolerance && a.Max.Y >= b.Min.Y - tolerance;
        }

        /// <summary>
        /// Collapse each back-to-back partition pair onto the <em>smaller</em> skin's plane. Two faces are a
        /// partition pair when their supporting planes are <em>anti-parallel</em> (normals oppose, within
        /// <paramref name="toleranceAngle"/>), each lies inside the other's capture slab, and they share
        /// surface in-plane - i.e. the two room-facing skins of one shared wall, one per room.
        /// <para>
        /// The general <see cref="Snap"/> projects the lighter-weight skin onto the heavier backer, which on a
        /// default (length-weighted) solve is the LARGER room's skin. That lands the partition ~a wall-thickness
        /// off the SMALLER room's cap edges; the native resolve only closes a room when the partition meets its
        /// caps exactly, and the post-resolve fill reliably re-grows the larger room's caps over such a gap but
        /// not the smaller (more fragile) room's - so the smaller room fails to close and the two rooms merge
        /// into one cell. Snapping the pair onto the smaller skin instead makes the fragile room close exactly
        /// and leaves the recoverable gap on the larger room, which the fill closes - so both rooms form their
        /// own cell. (The midplane was tried and is worse: it leaves an unmet gap on BOTH rooms, closing
        /// neither.) Same-facing duplicates (parallel, not anti-parallel) are left to the weighted bucket snap.
        /// </para>
        /// Largest-area first so each larger skin is projected onto its smaller partner; each face is consumed once.
        /// </summary>
        /// <summary>
        /// The two skins of a real back-to-back partition are the SAME wall seen from each room, so they are
        /// congruent - equal area. A within-bucket, anti-parallel, in-plane-overlapping pair whose areas differ
        /// by more than this fraction is therefore NOT one partition's two skins but two distinct walls (e.g. a
        /// long shared wall caught against a short partition skin, or the differently-sized walls of two adjacent
        /// rooms in a grid). Collapsing such a mis-pair drags one wall off its room's cap edges, which both opens
        /// naked edges and merges the two rooms into a single cell. Observed genuine pairs sit at ratio 1.000 and
        /// every observed mis-pair at <= 0.93, so this 0.97 floor cleanly separates them.
        /// </summary>
        public const double OPPOSED_PARTITION_MIN_AREA_RATIO = 0.97;

        public static void SnapOpposedPartitions(List<SnappedPanel> panels, double toleranceAngle, double toleranceDistance)
        {
            if (panels == null || panels.Count < 2)
            {
                return;
            }

            double minDot = System.Math.Cos(toleranceAngle);

            // Largest area first: the outer (larger) skin a is projected onto the inner (smaller) skin b's plane.
            List<SnappedPanel> ordered = panels
                .Where(x => x != null && x.Plane != null)
                .OrderByDescending(x => x.GetArea())
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                SnappedPanel a = ordered[i];
                if (a.Snapped || a.Plane == null)
                {
                    continue;
                }

                for (int j = i + 1; j < ordered.Count; j++)
                {
                    SnappedPanel b = ordered[j];
                    if (b.Snapped || b.Plane == null)
                    {
                        continue;
                    }

                    // Opposing (anti-parallel) normals only: the two skins face opposite rooms. A same-facing
                    // pair (dot > 0) is a genuine double-wall - leave it to the weighted bucket snap.
                    if (a.Plane.Normal.Unit.DotProduct(b.Plane.Normal.Unit) > -minDot)
                    {
                        continue;
                    }

                    // Near-coincident (within the capture slab) AND sharing surface in-plane: a real
                    // back-to-back partition, not two distinct parallel walls a room apart.
                    if (!a.BucketContains(b, out bool _) || !a.OverlapsInPlane(b, toleranceDistance))
                    {
                        continue;
                    }

                    // Congruent skins only: the two room-facing skins of one partition are the same wall and so
                    // have (near) equal area. A pair whose areas differ by more is a mis-pair of two distinct
                    // walls; collapsing it drags one wall off its room and merges the rooms (see the constant).
                    double areaA = a.GetArea();
                    double areaB = b.GetArea();
                    double larger = System.Math.Max(areaA, areaB);
                    if (larger <= 0 || System.Math.Min(areaA, areaB) / larger < OPPOSED_PARTITION_MIN_AREA_RATIO)
                    {
                        continue;
                    }

                    // Project the larger skin onto the smaller skin's plane (the fragile room's side), and mark
                    // the smaller skin consumed (a no-op self-projection) so the weighted snap leaves it put.
                    a.SnapToBacker(b.Plane);
                    b.SnapToBacker(b.Plane);
                    break; // a is consumed; move to the next a
                }
            }
        }

        /// <summary>
        /// Managed snap: sort by <c>Weight</c> descending so backers are processed first, then project each
        /// not-yet-snapped lower-weight, near-parallel panel onto the backer plane when either: (a) it lies
        /// within the backer's bucket slab AND overlaps it in-plane (a genuine double-wall); or (b) backer
        /// and candidate are both (near) vertical walls that are consecutive segments of one run - abutting
        /// or overlapping along the run, heights overlapping - offset by no more than
        /// <paramref name="alignColinearOffset"/> (a small Y-jog at a step). A near-parallel panel that
        /// merely passes through the slab but covers a different part of the plane (the next bay's wall),
        /// or is offset by more than the align distance, is a distinct wall and is left where it is.
        /// </summary>
        public static void Snap(List<SnappedPanel> panels, double toleranceAngle, double toleranceArcAngle, double toleranceDistance = Tolerance.Distance, double verticalAngleTolerance = 20 * (System.Math.PI / 180), double alignColinearOffset = 0.3)
        {
            if (panels == null || panels.Count < 2)
            {
                return;
            }

            List<SnappedPanel> ordered = panels.OrderByDescending(x => x.Weight).ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                SnappedPanel backer = ordered[i];
                if (backer.Plane == null)
                {
                    continue;
                }

                for (int j = i + 1; j < ordered.Count; j++)
                {
                    SnappedPanel candidate = ordered[j];
                    if (candidate.Snapped || candidate.Plane == null)
                    {
                        continue;
                    }

                    // Within-bucket near-parallel panels snap onto the backer. Equal-weight neighbours
                    // are absorbed too - the descending sort makes the earlier panel the backer, so
                    // coincident/offset "double-wall" pairs of the same weight collapse onto one plane
                    // (and then merge as coplanar). This matches the 2D TryBucketSnap and this method's
                    // own contract; the sort guarantees candidate.Weight <= backer.Weight, so only a
                    // strictly higher-weight candidate (never produced by the sort) is skipped.
                    if (candidate.Weight > backer.Weight)
                    {
                        continue;
                    }

                    // A fully captured neighbour tolerates a larger angle (it is clearly the same
                    // surface); a partially captured one must be near-parallel. Mirrors the 2D solver.
                    bool withinBucket = backer.BucketContains(candidate, out bool fully);
                    double angleTolerance = fully ? toleranceAngle : toleranceArcAngle;
                    if (!backer.IsParallelWith(candidate, angleTolerance))
                    {
                        continue;
                    }

                    // (a) A genuine double-wall: within the bucket slab AND sharing surface in-plane. A
                    // near-parallel wall that sits over a different part of the plane (the next bay's wall)
                    // is a separate wall and must not be dragged onto its neighbour.
                    bool overlap = withinBucket && backer.OverlapsInPlane(candidate, toleranceDistance);

                    // (b) Consecutive segments of one vertical wall run with a small perpendicular jog at a
                    // step: abutting/overlapping along the run, heights overlapping, offset within the align
                    // distance. Aligns the jog onto one plane. Restricted to walls so stacked floor/roof
                    // tiles at different levels are never merged; independent of the bucket so a jog wider
                    // than the bucket still aligns.
                    bool abut = alignColinearOffset > toleranceDistance
                        && backer.IsVertical(verticalAngleTolerance) && candidate.IsVertical(verticalAngleTolerance)
                        && backer.AbutsColinearWithin(candidate, alignColinearOffset, toleranceDistance);

                    if (!overlap && !abut)
                    {
                        continue;
                    }

                    candidate.SnapToBacker(backer.Plane);
                }
            }
        }

        /// <summary>
        /// Normalize a level's caps onto one plane. Groups the (non-vertical) floor/roof panels that are
        /// near-parallel and whose planes sit within <paramref name="normalizeCapOffset"/> perpendicular of
        /// one another, and projects every cap in a group onto the dominant (largest-area) cap's plane - the
        /// level plane. This collapses the small plane differences left when several separately-imported
        /// floor/roof tiles covering one space were merged at slightly different tilts/elevations; the kernel
        /// cannot close a cell whose lid is several barely-offset planes. Unlike <see cref="Snap"/> (which
        /// requires an in-plane overlap so it never drags a separate parallel wall onto its neighbour), this
        /// groups purely by perpendicular nearness, so adjacent edge-touching (non-overlapping) tiles of one
        /// slab are still brought onto a single plane. Floors and roofs separate out automatically: a floor
        /// and the roof above are parallel but far more than the offset apart, so they never merge; two roof
        /// slopes that meet at a ridge are not parallel, so each keeps its own pitch. Restricted to caps so
        /// vertical walls (handled by the bucket snap) are untouched.
        /// </summary>
        public static void NormalizeCaps(List<SnappedPanel> panels, double toleranceAngle, double normalizeCapOffset, double toleranceDistance, double verticalAngleTolerance = 20 * (System.Math.PI / 180))
        {
            if (panels == null || panels.Count < 2 || normalizeCapOffset <= toleranceDistance)
            {
                return;
            }

            // Caps only, largest area first so the dominant slab is the backer each group snaps onto.
            List<SnappedPanel> caps = panels
                .Where(x => x != null && x.Plane != null && !x.IsVertical(verticalAngleTolerance))
                .OrderByDescending(x => x.GetArea())
                .ToList();

            if (caps.Count < 2)
            {
                return;
            }

            bool[] grouped = new bool[caps.Count];
            for (int i = 0; i < caps.Count; i++)
            {
                if (grouped[i])
                {
                    continue;
                }

                SnappedPanel backer = caps[i];
                grouped[i] = true;

                for (int j = i + 1; j < caps.Count; j++)
                {
                    if (grouped[j])
                    {
                        continue;
                    }

                    SnappedPanel candidate = caps[j];
                    if (!backer.IsParallelWith(candidate, toleranceAngle))
                    {
                        continue;
                    }

                    // Perpendicular nearness to the backer level plane (no in-plane overlap required): a
                    // candidate whose centre lies within the offset band of the backer plane is the same level.
                    Point3D centre = candidate.GetBoundingBox()?.GetCentroid();
                    if (centre == null || System.Math.Abs(backer.Plane.Distance(centre)) > normalizeCapOffset)
                    {
                        continue;
                    }

                    if (candidate.SnapToBacker(backer.Plane))
                    {
                        grouped[j] = true;
                    }
                }
            }
        }

        private void Resolve(List<Face3D> snappedFace3Ds, OcctBuildOptions options)
        {
            if (snappedFace3Ds == null || snappedFace3Ds.Count == 0)
            {
                return;
            }

            // Healing defaults for the solver use-case: sew near-touching faces before the volume build
            // (bridges residual sub-mm gaps the managed fill leaves) and keep internal floors/partitions as
            // shared cell faces (a zoned complex, not just the outer envelope). Callers can override.
            if (options == null)
            {
                options = new OcctBuildOptions
                {
                    AvoidInternalShapes = false,
                    SewBeforeBuild = true,
                    SewingTolerance = 0.01 // 1 cm: bridges the cm-scale floor/wall gaps typical of Revit exports
                };
            }

            // Native coplanar pre-merge BEFORE the volume build. After Step 2's fill/extend, extended walls
            // and grown caps overlap coplanar neighbours; collapsing those overlaps (the share of
            // self-intersections MakerVolume cannot otherwise digest) is what lets the kernel form a zoned
            // cell complex instead of a single envelope cell.
            List<Face3D> buildFace3Ds = snappedFace3Ds;
            List<Face3D> preMerged = GeometryQuery.MergeCoplanarFace3Ds(snappedFace3Ds, out OcctCellComplexResult preMergeResult, ToleranceAngle, options);
            preMergeResult?.Dispose();
            if (preMerged != null && preMerged.Count != 0)
            {
                buildFace3Ds = preMerged;
            }

            BucketMergedFace3Ds = buildFace3Ds; // expose the MakerVolume input for debugging

            // MakerVolume: split panels at mutual intersections and resolve 3-way junctions.
            List<Shell> shells = GeometryCreate.Shells(buildFace3Ds, out OcctCellComplexResult cellResult, options);
            if (cellResult == null || !cellResult.NativeAvailable)
            {
                // Native kernel not present (e.g. non-Windows agent): keep the managed snap result.
                cellResult?.Dispose();
                return;
            }

            NativeResolved = true;
            ResolvedCellCount = cellResult.Cells?.Count ?? 0;

            List<Face3D> resolved = shells == null
                ? new List<Face3D>()
                : shells.Where(x => x != null).SelectMany(x => x.Face3Ds ?? new List<Face3D>()).Where(x => x != null).ToList();
            cellResult.Dispose();

            if (resolved.Count == 0)
            {
                // No closed cells formed (open wall soup): fall back to the pre-merged faces.
                resolved = buildFace3Ds;
            }

            // Merge resolved coplanar neighbours (the colinear-merge analogue).
            List<Face3D> merged = GeometryQuery.MergeCoplanarFace3Ds(resolved, out OcctCellComplexResult mergeResult, ToleranceAngle, options);
            mergeResult?.Dispose();
            if (merged != null && merged.Count != 0)
            {
                resolved = merged;
            }

            // ---- Adaptive native sew pass ----
            // The pre-build sew (SewingTolerance ~1 cm) only bridges sub-cm gaps; the floor/wall slot gaps
            // that survive into the resolved faces are wider. Re-sew the resolved faces at an expanded
            // tolerance to stitch the two free edges of each slot directly - no fabricated air face - and keep
            // the sewn result only when it strictly reduces the naked-edge count, so over-merging unrelated
            // near edges is rejected. GapFill below then handles only what sewing could not close.
            if (SewResidualGaps)
            {
                int nakedBefore = NakedEdgeCount(resolved, options);
                if (nakedBefore > 0)
                {
                    double sewTolerance = System.Math.Min(System.Math.Max(SewExpandTolerance, options.SewingTolerance), 0.3);
                    OcctBuildOptions sewOptions = new OcctBuildOptions(options)
                    {
                        SewBeforeBuild = true,
                        SewingTolerance = sewTolerance
                    };

                    List<Shell> sewnShells = GeometryQuery.Sew(resolved, out OcctCellComplexResult sewResult, sewOptions, false);
                    sewResult?.Dispose();

                    List<Face3D> sewn = sewnShells == null
                        ? null
                        : sewnShells.Where(x => x != null).SelectMany(x => x.Face3Ds ?? new List<Face3D>()).Where(x => x != null && x.IsValid()).ToList();

                    if (sewn != null && sewn.Count != 0 && NakedEdgeCount(sewn, options) < nakedBefore)
                    {
                        resolved = sewn;
                    }
                }
            }

            ResolvedFace3Ds = resolved;

            // Report naked (free) boundary edges - true boundaries vs unresolved gaps.
            GeometryQuery.Validate(resolved, out OcctValidationReport report, out OcctCellComplexResult validateResult, options, false);
            validateResult?.Dispose();
            if (report != null)
            {
                NakedEdgePoint3Ds = report
                    .IssuesOf(OcctValidationIssueCategory.NakedEdge)
                    .Where(x => x?.Location != null)
                    .Select(x => x.Location)
                    .ToList();
            }

            // Close the residual holes: build a Face3D over each naked-boundary loop. These are added to the
            // air-panel candidates so every space is fully enclosed.
            if (FillHoles && NakedEdgePoint3Ds.Count != 0)
            {
                List<Face3D> gapFace3Ds = GapFill.NakedLoopFace3Ds(resolved, NakedEdgePoint3Ds, 0.01);
                if (gapFace3Ds.Count != 0)
                {
                    HoleFillFace3Ds = (HoleFillFace3Ds ?? new List<Face3D>()).Concat(gapFace3Ds).ToList();
                }
            }
        }

        /// <summary>
        /// Counts the naked (free) boundary edges in a resolved face set via the native validator. Used by the
        /// adaptive sew pass to accept a re-sew only when it strictly reduces the count. Returns
        /// <see cref="int.MaxValue"/> when the validator is unavailable, so a sew is never accepted on a
        /// count it could not measure.
        /// </summary>
        private static int NakedEdgeCount(List<Face3D> face3Ds, OcctBuildOptions options)
        {
            if (face3Ds == null || face3Ds.Count == 0)
            {
                return 0;
            }

            GeometryQuery.Validate(face3Ds, out OcctValidationReport report, out OcctCellComplexResult result, options, false);
            result?.Dispose();
            return report?.CountOf(OcctValidationIssueCategory.NakedEdge) ?? int.MaxValue;
        }

        private static List<SnappedPanel> Register(List<Face3D> face3Ds, List<double> bucketSizes, List<double> weights, List<double> maxExtensions)
        {
            List<SnappedPanel> panels = new List<SnappedPanel>();
            for (int i = 0; i < face3Ds.Count; i++)
            {
                Face3D face3D = face3Ds[i];
                if (face3D == null || !face3D.IsValid() || face3D.GetPlane() == null)
                {
                    continue;
                }

                panels.Add(new SnappedPanel(i, face3D, weights[i], bucketSizes[i], maxExtensions[i]));
            }

            return panels;
        }

        /// <summary>
        /// Pads or trims <paramref name="values"/> to <paramref name="targetCount"/>, filling the
        /// shortfall with <paramref name="defaultValue"/>. Same pad pattern as the 2D <c>SnapSolver</c>.
        /// </summary>
        public static List<double> AdjustListLength(List<double> values, int targetCount, double defaultValue)
        {
            List<double> adjusted = new List<double>(targetCount);
            int existing = values?.Count ?? 0;
            for (int i = 0; i < targetCount; i++)
            {
                adjusted.Add(i < existing ? values[i] : defaultValue);
            }

            return adjusted;
        }
    }
}
