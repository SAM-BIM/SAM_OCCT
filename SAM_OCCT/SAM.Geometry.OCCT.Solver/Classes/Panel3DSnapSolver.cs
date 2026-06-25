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

        /// <summary>Re-attach the (sloped) roof to the resolved output. MakerVolume cells cap at the top
        /// ceiling and drop the roof lid above; this adds the clean roof slopes back. Default true.</summary>
        public bool RetainRoof { get; set; } = true;

        /// <summary>Step 2: after the resolve, build a Face3D over each residual naked-boundary loop (air-panel
        /// candidate) so every space is fully enclosed. Default true. (Step 1 strips input holes outright.)</summary>
        public bool FillHoles { get; set; } = true;

        /// <summary>Angle within which a panel's normal counts as horizontal, so the panel is "vertical" (a wall).</summary>
        public double VerticalAngleTolerance { get; set; } = 20 * (System.Math.PI / 180);

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
            CleanFace3Ds = CleanBucket(SnappedPanels, ToleranceAngle, ToleranceArcAngle, ToleranceDistance);

            if (StopAfterClean)
            {
                ResolvedFace3Ds = CleanFace3Ds;
                return;
            }

            // ---- Step 2: extend + resolve ----
            // Re-wrap the clean panels: Step 1 merged/removed panels, so the per-source weights no longer
            // apply, and bucket/weight are re-derived from geometry. The per-panel MaxExtend IS carried
            // forward (positionally), so a wall the caller marked to extend further keeps that reach.
            SnappedPanels = Register(
                CleanFace3Ds,
                AdjustListLength(null, CleanFace3Ds.Count, DEFAULT_BucketSize),
                AdjustListLength(null, CleanFace3Ds.Count, DEFAULT_Weight),
                AdjustListLength(maxExtensions, CleanFace3Ds.Count, DEFAULT_MaxExtension));

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
                Fill(SnappedPanels, VerticalAngleTolerance, FillMargin, ToleranceDistance);
            }

            // Plan-closure diagnostic: which wall ends are STILL open after the managed extend? These are the
            // panels to upgrade (raise MaxExtend / bucket) before the floors/roofs can fill a closed polysurface.
            OpenWallEndPoint3Ds = OpenWallEnds(SnappedPanels, VerticalAngleTolerance, ConnectionTolerance, ToleranceDistance, out List<Face3D> openWallFace3Ds);
            OpenWallFace3Ds = openWallFace3Ds;

            List<Face3D> snappedFace3Ds = SnappedPanels.Select(x => x.Face3D).Where(x => x != null && x.IsValid()).ToList();
            ResolvedFace3Ds = snappedFace3Ds;

            // Stop before the native resolve: the split (MakerVolume trim) stays in Solve3D. The output here
            // is the filled caps + extended (overshooting) walls, for reviewing the pre-resolve geometry.
            if (StopAfterExtend)
            {
                return;
            }

            Resolve(snappedFace3Ds, options);

            // The native cells enclose the rooms (floors + walls) but cap at the top ceiling, dropping the
            // sloped roof above. Re-attach the clean roof slopes so the building envelope is complete.
            if (RetainRoof && NativeResolved && ResolvedFace3Ds != null)
            {
                List<Face3D> roofSlopes = CleanFace3Ds.Where(IsSlopedRoof).ToList();
                if (roofSlopes.Count != 0)
                {
                    ResolvedFace3Ds = ResolvedFace3Ds.Concat(roofSlopes).ToList();
                }
            }
        }

        /// <summary>True when the face is a sloped roof - normal neither (near) vertical nor (near) horizontal.</summary>
        private static bool IsSlopedRoof(Face3D face3D)
        {
            Vector3D normal = face3D?.GetPlane()?.Normal.Unit;
            if (normal == null)
            {
                return false;
            }

            double absZ = System.Math.Abs(normal.Z);
            return absZ > 0.1 && absZ < 0.95;
        }

        /// <summary>
        /// Step 1 - clean bucket (managed, native-free). Produces clean single panels: each panel is reduced to
        /// its external shape (internal openings stripped), within-bucket near-parallel lower-weight panels are
        /// projected onto their backer plane, then coplanar faces - including a smaller panel contained in a
        /// larger one - are merged via the managed union. The output feeds Step 2 (fill/extend), or is returned
        /// as-is when only cleaning is wanted (<see cref="StopAfterClean"/>).
        /// </summary>
        public static List<Face3D> CleanBucket(List<SnappedPanel> panels, double toleranceAngle, double toleranceArcAngle, double toleranceDistance)
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

            // 2. Bucket snap - bring within-bucket near-parallel, in-plane-overlapping panels onto one
            //    backer plane (now coplanar). Non-overlapping parallels (separate bays) are left put.
            Snap(panels, toleranceAngle, toleranceArcAngle, toleranceDistance);

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

            for (int i = 0; i < walls.Count; i++)
            {
                Segment3D foot = feet[i];
                Point3D start = foot.GetStart();
                Point3D end = foot.GetEnd();
                double length = foot.GetLength();

                // This wall's own reach budget (Solver MaxExtend), capped at a fraction of the wall's own
                // length so a short stub (e.g. a pier between two door openings) cannot shoot out unrealistically
                // far. Mirrors the 2D ExtensionSolver's min(MaxExtension, length * ExtensionLimitLengthRatio).
                // Non-positive => the wall stays put.
                double maxReach = System.Math.Min(walls[i].MaxExtension, length * EXTENSION_LIMIT_LENGTH_RATIO);
                if (maxReach <= toleranceDistance)
                {
                    continue;
                }

                double dx = (end.X - start.X) / length;
                double dy = (end.Y - start.Y) / length;

                // Each end runs along its own outward direction; the reach is whatever it takes to meet the
                // nearest wall in that direction (or 0 when none is within this wall's MaxExtend).
                double endReach = NearestWallReach(end.X, end.Y, dx, dy, i, feet, maxReach, toleranceDistance);
                double startReach = NearestWallReach(start.X, start.Y, -dx, -dy, i, feet, maxReach, toleranceDistance);

                if (endReach > toleranceDistance)
                {
                    endReach += overshoot;
                }

                if (startReach > toleranceDistance)
                {
                    startReach += overshoot;
                }

                if (startReach > toleranceDistance || endReach > toleranceDistance)
                {
                    walls[i].ExtendHorizontal(startReach, endReach, toleranceDistance);
                }
            }
        }

        /// <summary>
        /// Distance from <c>(px, py)</c> travelling along the unit plan direction <c>(dx, dy)</c> to the
        /// nearest other wall foot it crosses, within <paramref name="maxReach"/>; 0 when no wall is hit.
        /// The crossing must lie within the other wall's plan extent (not just on its infinite line), and a
        /// wall parallel to the ray is skipped. Plan (XY) only.
        /// </summary>
        private static double NearestWallReach(double px, double py, double dx, double dy, int self, List<Segment3D> feet, double maxReach, double tolerance)
        {
            double best = double.MaxValue;
            for (int k = 0; k < feet.Count; k++)
            {
                if (k == self)
                {
                    continue;
                }

                Point3D a = feet[k].GetStart();
                Point3D b = feet[k].GetEnd();
                double ex = b.X - a.X;
                double ey = b.Y - a.Y;

                // Ray (p + t*d) vs segment (a + s*e), solved in plan. denom = cross(d, e).
                double denom = dx * ey - dy * ex;
                if (System.Math.Abs(denom) < 1e-9)
                {
                    continue; // parallel - a wall never closes a corner onto a parallel wall
                }

                double rx = a.X - px;
                double ry = a.Y - py;
                double t = (rx * ey - ry * ex) / denom; // distance along the ray to the crossing
                double s = (rx * dy - ry * dx) / denom; // parameter along the other wall's foot

                if (t <= tolerance || t > maxReach + tolerance)
                {
                    continue; // behind this end, or beyond the search reach
                }

                if (s < -tolerance || s > 1 + tolerance)
                {
                    continue; // crosses the wall's line outside the wall's own extent
                }

                if (t < best)
                {
                    best = t;
                }
            }

            return best == double.MaxValue ? 0 : best;
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
            List<BoundingBox3D> caps = new List<BoundingBox3D>();
            double roofMaxZ = double.NaN; // the highest point of the roof system (the ridge)
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
                    caps.Add(boundingBox3D);
                }

                if (!horizontal && (double.IsNaN(roofMaxZ) || boundingBox3D.Max.Z > roofMaxZ))
                {
                    roofMaxZ = boundingBox3D.Max.Z;
                }
            }

            foreach (SnappedPanel wall in walls)
            {
                BoundingBox3D wallBox = wall.GetBoundingBox();
                if (wallBox == null)
                {
                    continue;
                }

                double wallTopZ = wallBox.Max.Z;

                // The nearest cap that starts above the wall top and covers it in plan.
                BoundingBox3D nearestCap = null;
                double nearestStartZ = double.MaxValue;
                foreach (BoundingBox3D cap in caps)
                {
                    if (cap.Min.Z < wallTopZ - toleranceDistance)
                    {
                        continue; // not above the wall
                    }

                    if (!OverlapsInPlan(cap, wallBox, toleranceDistance))
                    {
                        continue;
                    }

                    if (cap.Min.Z < nearestStartZ)
                    {
                        nearestStartZ = cap.Min.Z;
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
                // room can close at the bottom (the "between floors" case).
                double wallBottomZ = wallBox.Min.Z;
                BoundingBox3D nearestBelow = null;
                double nearestEndZ = double.MinValue;
                foreach (BoundingBox3D cap in caps)
                {
                    if (cap.Max.Z > wallBottomZ + toleranceDistance)
                    {
                        continue; // not below the wall
                    }

                    if (!OverlapsInPlan(cap, wallBox, toleranceDistance))
                    {
                        continue;
                    }

                    if (cap.Max.Z > nearestEndZ)
                    {
                        nearestEndZ = cap.Max.Z;
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
        /// Step 2 - fill floors/roofs to walls: grow each (non-vertical) cap outward in its plane so it
        /// overshoots the surrounding walls, closing the floor/roof-to-wall gaps that otherwise leave naked
        /// edges and prevent any cell from closing. The native resolve trims the overshoot back at the walls.
        /// </summary>
        public static void Fill(List<SnappedPanel> panels, double verticalAngleTolerance, double margin, double toleranceDistance)
        {
            if (panels == null || panels.Count == 0 || margin <= toleranceDistance)
            {
                return;
            }

            foreach (SnappedPanel panel in panels)
            {
                if (!panel.IsVertical(verticalAngleTolerance)) // floors and roofs are the caps
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
        /// Managed snap: sort by <c>Weight</c> descending so backers are processed first, then
        /// project each not-yet-snapped lower-weight panel that lies within a backer's slab, is
        /// near-parallel to it AND actually overlaps it in-plane (a genuine double-wall) onto the
        /// backer plane. A near-parallel panel that merely passes through the slab but covers a
        /// different part of the plane (the next bay's wall, colinear but offset along its run) is a
        /// distinct wall and is left where it is - no move is needed. Equal-weight near-coincident
        /// panels are absorbed.
        /// </summary>
        public static void Snap(List<SnappedPanel> panels, double toleranceAngle, double toleranceArcAngle, double toleranceDistance = Tolerance.Distance)
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

                    if (!backer.BucketContains(candidate, out bool fully))
                    {
                        continue;
                    }

                    // A fully captured neighbour tolerates a larger angle (it is clearly the same
                    // surface); a partially captured one must be near-parallel. Mirrors the 2D solver.
                    double angleTolerance = fully ? toleranceAngle : toleranceArcAngle;
                    if (!backer.IsParallelWith(candidate, angleTolerance))
                    {
                        continue;
                    }

                    // Only collapse a candidate that genuinely shares surface with the backer in-plane
                    // (a real double-wall). A near-parallel wall that sits over a different part of the
                    // plane - e.g. the top wall of the adjacent bay - is a separate wall: snapping it
                    // would move it onto its neighbour for no reason. Keep it where it is.
                    if (!backer.OverlapsInPlane(candidate, toleranceDistance))
                    {
                        continue;
                    }

                    candidate.SnapToBacker(backer.Plane);
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
