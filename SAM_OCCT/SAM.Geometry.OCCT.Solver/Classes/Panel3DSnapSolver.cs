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

        /// <summary>
        /// Diagnostic-only override: skip the raw-first attempt (<see cref="TryRawResolve"/>) and always run
        /// the managed clean/extend/resolve pipeline (Steps 1-2 + native resolve), even on an input the raw
        /// solve would otherwise adopt watertight. Lets golden-master tests capture the managed-pipeline
        /// signature on the SAME fixtures the raw-first optimization exists to bypass (see
        /// docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md, Phase 0). Default false preserves the existing
        /// raw-first-then-managed-fallback behaviour for every other caller.
        /// </summary>
        public bool ForceManagedPipeline { get; set; } = false;

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

        /// <summary>
        /// Minimum cell volume (cubic metres) for the raw-first adoption gate (<see cref="TryRawResolve"/>): a
        /// resolved cell smaller than this is a sliver artifact (e.g. a thin void where two modelled faces
        /// leave a hair's-width gap), not a genuine room, and its presence means the raw solve is not trusted
        /// as-is - the managed clean/extend pipeline runs instead. Default 0.05 m3.
        /// </summary>
        public double MinCellVolume { get; set; } = 0.05;

        /// <summary>
        /// Maximum fraction of input faces the raw-first adoption gate (<see cref="TryRawResolve"/>) tolerates
        /// having no surviving representation in the resolved output. A face gets dropped when it bounds no
        /// closed cell - a common symptom of a modelling defect (e.g. a partition that stops short of the
        /// ceiling and so cannot split the room it was meant to divide), which is exactly the
        /// "watertight-but-wrong" case a plain naked-edge check misses (two rooms silently merge into one
        /// cell while the outer envelope stays watertight). Exceeding this ratio rejects the raw adoption so
        /// the managed pipeline gets a chance to close it properly.
        /// <para>
        /// Default 0.30 (30%), calibrated against the 5 golden-master fixtures (docs/
        /// TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md Phase 0): a correctly-adopted, watertight raw solve
        /// naturally drops 0-25% of its input faces on real models (e.g. a back-to-back partition's two
        /// coincident room-facing skins, one of which is absorbed into the other during the coplanar merge) -
        /// this is the existing <see cref="RetainDropped"/> recovery path working as intended, not a defect.
        /// The plan's originally-proposed 0.10 default rejected 3 of the 5 real fixtures outright and is only
        /// meaningful as an explicit, tighter override on a specific model (or in a unit/integration test that
        /// sets it directly) - never as the global default.
        /// </para>
        /// </summary>
        public double MaxDroppedRatio { get; set; } = 0.30;

        /// <summary>Machine-readable events from every stage of this solve (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md
        /// §N) - every gate rejection carries its reason here. Reset at the start of each <see cref="Execute"/> call.</summary>
        public SolverDiagnostics Diagnostics { get; private set; } = new SolverDiagnostics();

        /// <summary>Provenance of the managed pipeline's output faces: which source panel(s) each
        /// <see cref="ResolvedFace3Ds"/> entry came from, and how (<see cref="Provenance"/>). Populated on the
        /// managed path (Phase 2, managed-only); the native resolve/heal record a coarse per-output mapping.
        /// Null-safe: always non-null after <see cref="Execute"/>.</summary>
        public SourceMap SourceMap { get; private set; } = new SourceMap();

        /// <summary>
        /// The exact native-history resolve map (Phase 3): source panel -> resolved output ordinal,
        /// composed from <c>BRepTools_History</c> across the adopted resolve hops. Non-null ONLY when
        /// native history was available for every load-bearing hop; null in the geometric-fallback path
        /// (pre-v4 native, the sew-before-build path, or an adopted residual sew - the §7.1 scope cut).
        /// Consumers prefer it and fall back to the geometric heuristic face-by-face, which is what
        /// demotes <c>NearestSourceIndex</c> to a fallback without perturbing the fallback path.
        /// </summary>
        public SourceMap ResolveHistorySourceMap { get; private set; }

        /// <summary>Free-boundary (naked) wires of the resolved output as ordered polylines (Phase 3, native
        /// ABI v4). Empty on a pre-v4 native build; the <see cref="NakedEdgePoint3Ds"/> remain the
        /// always-available naked-edge signal. Reset at the start of each <see cref="Execute"/> call.</summary>
        public List<OcctNakedWire> NakedWires { get; private set; } = new List<OcctNakedWire>();

        /// <summary>The closure signature of the ADOPTED result (raw-first, when it was adopted). Null when the
        /// managed pipeline ran instead (Phase 2+ populates it for the managed levels too).</summary>
        public ClosureSignature3D Signature { get; private set; }

        /// <summary>The closure signature <see cref="TryRawResolve"/> measured for the raw (L0) attempt,
        /// whether or not it was adopted - lets a rejected raw attempt still be inspected/diagnosed.</summary>
        public ClosureSignature3D RawAttemptSignature { get; private set; }

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
            Diagnostics = new SolverDiagnostics();
            Signature = null;
            RawAttemptSignature = null;

            if (face3Ds == null || face3Ds.Count == 0)
            {
                return;
            }

            // ---- Raw-first ----
            // A well-modelled export resolves directly through the kernel; the managed clean+extend below is
            // built to REPAIR gappy exports and only degrades an already-watertight solve (it merges/normalizes
            // caps and extends walls, which on good input loses room separations). Hand the kernel the raw input
            // faces first and keep that result when it is watertight (no naked edges); only fall through to the
            // managed pipeline when the raw solve leaves gaps. Skipped for the diagnostic StopAfter* modes, which
            // exist to inspect the managed clean/extend geometry itself.
            if (!ForceManagedPipeline && !StopAfterClean && !StopAfterExtend && TryRawResolve(options))
            {
                return;
            }

            List<double> bucketSizes_Adjusted = AdjustListLength(bucketSizes, face3Ds.Count, DEFAULT_BucketSize);
            List<double> weights_Adjusted = AdjustListLength(weights, face3Ds.Count, DEFAULT_Weight);
            List<double> maxExtensions_Adjusted = AdjustListLength(maxExtensions, face3Ds.Count, DEFAULT_MaxExtension);

            SnappedPanels = Register(face3Ds, bucketSizes_Adjusted, weights_Adjusted, maxExtensions_Adjusted);

            ToleranceBudget tolerances = new ToleranceBudget
            {
                Angle = ToleranceAngle,
                ArcAngle = ToleranceArcAngle,
                Distance = ToleranceDistance,
                VerticalAngle = VerticalAngleTolerance
            };

            // ---- Stage A: SNAP (managed, native-free) ----
            // Strip holes -> collapse opposed partitions -> bucket-snap -> normalize caps -> merge coplanar.
            // SnapStage additionally attributes each clean face to the source panels that merged into it, so
            // its MaxExtend is carried by source identity (snapResult.CleanMaxExtensions), not list position -
            // the fix for the positional-MaxExtend bug in the pre-Phase-2 re-Register below.
            SourceMap snapSourceMap = new SourceMap();
            SnapStage.Result snapResult = SnapStage.Clean(SnappedPanels, tolerances, AlignColinearOffset, NormalizeCapOffset, Diagnostics, snapSourceMap);
            CleanFace3Ds = snapResult.CleanFace3Ds;

            if (StopAfterClean)
            {
                ResolvedFace3Ds = CleanFace3Ds;
                SourceMap = snapSourceMap; // source -> clean face (exact: output == clean faces)
                return;
            }

            // ---- Stage B/C: condition + resolve ----
            // The conditioning logic is written for a world-Z-up building (walls vertical, caps above/below
            // in Z, plan = XY). When the whole level is tilted, rotate the clean faces into a canonical
            // Z-up frame (mapping the level Up axis onto world Z), run the conditioning there, then rotate the
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

            // Re-wrap the clean panels for conditioning. Weight/bucket are re-derived from geometry (defaults),
            // but the per-panel MaxExtend is now carried by SOURCE IDENTITY via SnapStage's attribution
            // (snapResult.CleanMaxExtensions, index-aligned to CleanFace3Ds) - so a wall the caller marked to
            // extend further keeps that reach even though Step 1 merged/reordered panels. The rotation preserves
            // order and validity, so the carried MaxExtend stays index-aligned. (Pre-Phase-2 this list was the
            // ORIGINAL input maxExtensions applied positionally, landing the wrong reach on the wrong panel.)
            List<Face3D> step2Face3Ds = toCanonical == null
                ? CleanFace3Ds
                : CleanFace3Ds.Select(x => x.Transform(toCanonical)).ToList();

            SnappedPanels = Register(
                step2Face3Ds,
                AdjustListLength(null, step2Face3Ds.Count, DEFAULT_BucketSize),
                AdjustListLength(null, step2Face3Ds.Count, DEFAULT_Weight),
                AdjustListLength(snapResult.CleanMaxExtensions, step2Face3Ds.Count, DEFAULT_MaxExtension));

            ConditionStage.Condition(
                SnappedPanels,
                new ConditionStage.Settings
                {
                    ExtendWallsToWalls = ExtendWallsToWalls,
                    WallExtendOvershoot = WallExtendOvershoot,
                    ExtendToCaps = ExtendToCaps,
                    ExtendOvershoot = ExtendOvershoot,
                    RoofOvershoot = RoofOvershoot,
                    ExtendToRoofs = ExtendToRoofs,
                    FillCapsToWalls = FillCapsToWalls,
                    FillMargin = FillMargin,
                    FillOvershoot = FillOvershoot
                },
                tolerances);

            // Plan-closure diagnostic: which wall ends are STILL open after conditioning? These are the panels
            // to upgrade (raise MaxExtend / bucket) before the floors/roofs can fill a closed polysurface.
            OpenWallEndPoint3Ds = OpenWallEnds(SnappedPanels, VerticalAngleTolerance, ConnectionTolerance, ToleranceDistance, out List<Face3D> openWallFace3Ds);
            OpenWallFace3Ds = openWallFace3Ds;

            List<Face3D> snappedFace3Ds = SnappedPanels.Select(x => x.Face3D).Where(x => x != null && x.IsValid()).ToList();

            // Back to the world frame: conditioning ran in the canonical Z-up frame, so rotate the conditioned
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
                SourceMap = BuildResolvedSourceMap(ResolvedFace3Ds, face3Ds);
                return;
            }

            ResolveStage.Result resolveResult = ResolveStage.Resolve(snappedFace3Ds, options, ToleranceAngle, SewResidualGaps, SewExpandTolerance, FillHoles, Diagnostics);
            BucketMergedFace3Ds = resolveResult.BucketMergedFace3Ds;
            NativeResolved = resolveResult.NativeResolved;
            NakedWires = resolveResult.NakedWires ?? new List<OcctNakedWire>();
            if (resolveResult.NativeResolved)
            {
                ResolvedCellCount = resolveResult.ResolvedCellCount;
                ResolvedFace3Ds = resolveResult.ResolvedFace3Ds;
                NakedEdgePoint3Ds = resolveResult.NakedEdgePoint3Ds;
                HoleFillFace3Ds = resolveResult.HoleFillFace3Ds;
            }

            // Stage C - HEAL: re-add every conditioned face the native MakerVolume dropped (bounds no closed
            // cell), using its extended geometry so the re-added face overshoots its neighbours and closes the
            // gap. Walls and caps alike: a dropped cap comes back grown (not the ungrown clean slab).
            if (RetainDropped && NativeResolved && ResolvedFace3Ds != null)
            {
                HealStage.Result heal = HealStage.RetainDropped(ResolvedFace3Ds, snappedFace3Ds);
                ResolvedFace3Ds = heal.ResolvedFace3Ds;
            }

            // Source mapping over the final output faces. Phase 3: when the native resolve supplied a
            // composed BRepTools_History (input snapped face -> resolved output ordinal), bridge it back
            // to the original sources through the geometric snapped->source attribution and use it - the
            // resolve leg (splits/merges) is then exact. When history was unavailable (pre-v4 native, the
            // sew-before-build path, or an adopted residual sew) it degrades to the fully geometric
            // Phase-2 map, so no output face is ever left source-orphaned.
            SourceMap resolveHistoryMap = resolveResult.SourceMap;
            if (resolveHistoryMap != null)
            {
                // Bridge the exact resolve history (snapped input -> resolved ordinal) back to the
                // original sources through the geometric snapped->source attribution. ResolveHistorySourceMap
                // carries ONLY the history-resolved faces (null in the geometric-fallback path), so a
                // consumer can safely prefer it and fall back to NearestSourceIndex face-by-face.
                SourceMap snappedToSource = BuildResolvedSourceMap(snappedFace3Ds, face3Ds);
                ResolveHistorySourceMap = snappedToSource.Compose(resolveHistoryMap);
                SourceMap = BackfillGeometric(CloneSourceMap(ResolveHistorySourceMap), ResolvedFace3Ds, face3Ds);
            }
            else
            {
                ResolveHistorySourceMap = null;
                SourceMap = BuildResolvedSourceMap(ResolvedFace3Ds, face3Ds);
            }
        }

        /// <summary>Shallow copy of a <see cref="SourceMap"/>'s records, so backfilling the solver's public
        /// <see cref="SourceMap"/> does not mutate the exact <see cref="ResolveHistorySourceMap"/>.</summary>
        private static SourceMap CloneSourceMap(SourceMap source)
        {
            SourceMap copy = new SourceMap();
            if (source == null)
            {
                return copy;
            }

            foreach (int sourceIndex in source.Sources)
            {
                foreach (FaceKey key in source.FacesOf(sourceIndex))
                {
                    copy.Record(sourceIndex, key, Provenance.Resolved);
                }
            }

            return copy;
        }

        /// <summary>
        /// Fills the gaps a native-history composition leaves: any output face the composed
        /// <paramref name="composed"/> map has no source for (an adopted-sew face, a heal-appended
        /// face, or a reverse-gap ordinal) is attributed geometrically so it is never source-orphaned.
        /// Faces the history did resolve keep their exact composed sources.
        /// </summary>
        private static SourceMap BackfillGeometric(SourceMap composed, List<Face3D> outputFace3Ds, List<Face3D> inputFace3Ds)
        {
            if (outputFace3Ds == null)
            {
                return composed ?? new SourceMap();
            }

            SourceMap geometric = BuildResolvedSourceMap(outputFace3Ds, inputFace3Ds);
            for (int k = 0; k < outputFace3Ds.Count; k++)
            {
                FaceKey key = new FaceKey(k);
                if (composed.SourcesOf(key).Count != 0)
                {
                    continue;
                }

                foreach (int source in geometric.SourcesOf(key))
                {
                    composed.Record(source, key, Provenance.Resolved);
                }
            }

            return composed;
        }

        /// <summary>
        /// Builds a coarse source mapping over <paramref name="outputFace3Ds"/>: each output face is attributed
        /// to the input source(s) it is coplanar with and overlaps (bbox), falling back to the single nearest
        /// input so every output keeps at least one source (never orphaned). This is the managed-only stand-in
        /// (Phase 2) for the precise native <c>BRepTools_History</c> composed in Phase 3; the
        /// <see cref="NearestSourceIndex"/>-style heuristic is honest about being coarse.
        /// </summary>
        private static SourceMap BuildResolvedSourceMap(List<Face3D> outputFace3Ds, List<Face3D> inputFace3Ds)
        {
            SourceMap map = new SourceMap();
            if (outputFace3Ds == null)
            {
                return map;
            }

            List<Face3D> inputs = inputFace3Ds ?? new List<Face3D>();
            for (int k = 0; k < outputFace3Ds.Count; k++)
            {
                Face3D output = outputFace3Ds[k];
                if (output == null || !output.IsValid())
                {
                    continue;
                }

                FaceKey key = new FaceKey(k);
                List<int> sources = new List<int>();
                for (int i = 0; i < inputs.Count; i++)
                {
                    if (CoplanarBoxOverlap(output, inputs[i]))
                    {
                        sources.Add(i);
                    }
                }

                if (sources.Count != 0)
                {
                    map.RecordMerge(sources, key, Provenance.Resolved);
                    continue;
                }

                int nearest = NearestCoplanarInputIndex(output, inputs);
                if (nearest >= 0)
                {
                    map.Record(nearest, key, Provenance.Resolved);
                }
                else
                {
                    map.RecordFabricated(key, Provenance.Resolved);
                }
            }

            return map;
        }

        /// <summary>Coarse coplanar-and-overlapping test: parallel normals, near-coincident planes, and 3D bounding
        /// boxes overlapping (grown by a small tolerance). Used only for the Phase-2 coarse source mapping.</summary>
        private static bool CoplanarBoxOverlap(Face3D a, Face3D b)
        {
            Plane planeA = a?.GetPlane();
            Plane planeB = b?.GetPlane();
            if (planeA == null || planeB == null)
            {
                return false;
            }

            if (System.Math.Abs(planeA.Normal.Unit.DotProduct(planeB.Normal.Unit)) < 0.99)
            {
                return false;
            }

            if (System.Math.Abs(planeA.Distance(planeB.Origin)) > 0.05)
            {
                return false;
            }

            BoundingBox3D boxA = a.GetBoundingBox();
            BoundingBox3D boxB = b.GetBoundingBox();
            if (boxA == null || boxB == null)
            {
                return false;
            }

            const double tolerance = 0.05;
            return boxA.Min.X - tolerance <= boxB.Max.X && boxA.Max.X + tolerance >= boxB.Min.X
                && boxA.Min.Y - tolerance <= boxB.Max.Y && boxA.Max.Y + tolerance >= boxB.Min.Y
                && boxA.Min.Z - tolerance <= boxB.Max.Z && boxA.Max.Z + tolerance >= boxB.Min.Z;
        }

        /// <summary>Index of the input whose centroid is nearest <paramref name="output"/>'s centroid, preferring a
        /// coplanar input; -1 when there are no inputs. The nearest-source fallback for the coarse mapping.</summary>
        private static int NearestCoplanarInputIndex(Face3D output, List<Face3D> inputs)
        {
            Point3D outputCentre = output?.GetBoundingBox()?.GetCentroid();
            Plane outputPlane = output?.GetPlane();
            if (outputCentre == null || inputs == null || inputs.Count == 0)
            {
                return inputs != null && inputs.Count != 0 ? 0 : -1;
            }

            int best = -1;
            double bestDistance = double.MaxValue;
            int bestAny = -1;
            double bestAnyDistance = double.MaxValue;
            for (int i = 0; i < inputs.Count; i++)
            {
                Point3D centre = inputs[i]?.GetBoundingBox()?.GetCentroid();
                if (centre == null)
                {
                    continue;
                }

                double distance = outputCentre.Distance(centre);
                if (distance < bestAnyDistance)
                {
                    bestAnyDistance = distance;
                    bestAny = i;
                }

                Plane inputPlane = inputs[i]?.GetPlane();
                bool coplanar = inputPlane != null && outputPlane != null
                    && System.Math.Abs(outputPlane.Normal.Unit.DotProduct(inputPlane.Normal.Unit)) >= 0.99;
                if (coplanar && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best >= 0 ? best : bestAny;
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
        internal static bool IsRepresented(Face3D face3D, List<Face3D> resolvedFace3Ds)
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
        /// <para>
        /// As of Phase 2 the body lives in <see cref="SnapStage"/> (which additionally attributes each clean
        /// face to its source panels so <c>MaxExtend</c> is carried by identity); this static remains as the
        /// public, geometry-only entry point and delegates there, returning the same faces it always did.
        /// </para>
        /// </summary>
        public static List<Face3D> CleanBucket(List<SnappedPanel> panels, double toleranceAngle, double toleranceArcAngle, double toleranceDistance, double verticalAngleTolerance = 20 * (System.Math.PI / 180), double alignColinearOffset = 0.3, double normalizeCapOffset = 0.3)
        {
            if (panels == null || panels.Count == 0)
            {
                return new List<Face3D>();
            }

            ToleranceBudget tolerances = new ToleranceBudget
            {
                Angle = toleranceAngle,
                ArcAngle = toleranceArcAngle,
                Distance = toleranceDistance,
                VerticalAngle = verticalAngleTolerance
            };

            return SnapStage.Clean(panels, tolerances, alignColinearOffset, normalizeCapOffset).CleanFace3Ds;
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
        /// Minimum in-plane <em>overlap</em> ratio (<see cref="SnappedPanel.InPlaneOverlapRatio"/>) for a pair to
        /// count as one partition's two skins. The two skins of a real back-to-back partition occupy the SAME
        /// footprint, so their overlap-to-larger ratio is ~1.0 - even when one skin carries a door notch that
        /// cuts its <em>area</em> (the old full-area gate wrongly rejected such a door-cut skin at ~0.68 and left
        /// the partition split). A pair whose footprints differ by more - a long shared wall caught against a
        /// short partition skin, or the differently-sized walls of two adjacent grid rooms - overlaps only
        /// partially and is a mis-pair whose collapse would drag one wall off its room and merge the two rooms
        /// into one cell. Observed genuine pairs sit at 1.000 and mis-pairs well below, so this 0.97 floor
        /// separates them. (Replaces the pre-Phase-2 full-area ratio, which a door cut defeated.)
        /// </summary>
        public const double OPPOSED_PARTITION_MIN_OVERLAP_RATIO = 0.97;

        /// <summary>
        /// Minimum in-plane overlap-to-larger-footprint ratio for a CO-parallel pair to snap together in
        /// <see cref="Snap"/> (Phase 2b distinct-wall guard). Below this the two share a supporting plane but
        /// sit at different in-plane locations - two distinct walls, not one double-wall - and collapsing them
        /// would merge two cells (observed on whole-level-tilted.sam, where the Phase-2b ordering/midpoint
        /// change first exposed such a mis-pair at ratio ~0.10). Set well below
        /// <see cref="OPPOSED_PARTITION_MIN_OVERLAP_RATIO"/> because a genuine co-parallel double-wall (skins
        /// offset along the run, or of unequal size) overlaps less fully than a coincident opposed partition:
        /// observed genuine co-parallel snaps sit at 0.78-0.97, mis-pairs at or below 0.30.
        /// </summary>
        public const double COPARALLEL_SNAP_MIN_OVERLAP_RATIO = 0.5;

        /// <summary>
        /// Ceiling (metres) on the perpendicular separation between two skins for them to count as one
        /// back-to-back partition rather than a genuine void. A partition's two room-facing skins sit within a
        /// wall thickness of one another (typically 0.1-0.3 m); a shaft/void gap is wider. Gating on this
        /// thickness scale - rather than the <c>BucketSize</c> slab, which the analytical wrapper floors at 0.4 m
        /// (§I) - is what lets a real 0.3-0.4 m void survive Stage A while a thin partition still collapses.
        /// 0.3 m is a generous maximum wall thickness; a pair separated by more is not one wall's two skins.
        /// </summary>
        public const double OPPOSED_PARTITION_MAX_SEPARATION = 0.3;

        /// <summary>
        /// Collapse each back-to-back partition pair onto the smaller skin's plane, with gates that distinguish a
        /// real shared partition from geometry that merely looks like one:
        /// <list type="number">
        /// <item><b>Anti-parallel normals</b> - the two skins face opposite rooms (a same-facing duplicate is
        /// left to the weighted bucket snap).</item>
        /// <item><b>Within-bucket and in-plane overlap</b> - near-coincident and sharing surface, not two walls
        /// a room apart.</item>
        /// <item><b>Thickness-scale separation</b> (Phase 2, <see cref="OPPOSED_PARTITION_MAX_SEPARATION"/>,
        /// <see cref="SnappedPanel.PerpendicularSeparation"/>) - the skins must sit within a wall thickness, not
        /// the 0.4 m-floored bucket slab. A wider gap is a genuine void (a shaft) and must survive Stage A.</item>
        /// <item><b>In-plane overlap ratio</b> (Phase 2, <see cref="OPPOSED_PARTITION_MIN_OVERLAP_RATIO"/>) - gated
        /// on the overlap <em>footprint</em>, not full face area, so a door-cut skin (same footprint, smaller area)
        /// collapses while a partial-overlap mis-pair (a long wall vs a short partition) does not.</item>
        /// </list>
        /// <para>
        /// The plan (§E Phase 2) proposed a <em>normal-sign</em> separation test (collapse only "facing-away"
        /// skins). That proved unreliable on the real fixtures: SAM/Revit import winding orients a real
        /// partition's skin normals <em>toward</em> each other (into the wall core), the opposite of a clean
        /// synthetic model, so the sign test mis-classified genuine partitions as voids and regressed
        /// <c>whole-level-tilted</c> from 22 to 20 cells. The winding-independent thickness-separation gate
        /// achieves the same goal (keep a wide void, collapse a thin partition) without depending on normal
        /// orientation - the empirical-recalibration methodology of §A.
        /// </para>
        /// Rejections are recorded as <see cref="DiagnosticCode.RejectedCollapse"/> so every kept pair carries its
        /// reason. Largest-area first, so the outer (larger) skin is projected onto its smaller partner's plane
        /// (the fragile room's side, which the post-resolve fill cannot re-grow); each face is consumed once.
        /// </summary>
        public static void SnapOpposedPartitions(List<SnappedPanel> panels, double toleranceAngle, double toleranceDistance, SolverDiagnostics diagnostics = null)
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

                    // Thickness-scale separation gate (Phase 2): the two skins must sit within a wall thickness,
                    // not the 0.4 m-floored bucket. A wider anti-parallel, overlapping pair bounds a genuine void
                    // (a shaft) and must survive Stage A - the shaft-void fix.
                    if (a.PerpendicularSeparation(b) > OPPOSED_PARTITION_MAX_SEPARATION)
                    {
                        diagnostics?.Add(SolverStage.Snap, DiagnosticCode.RejectedCollapse, OcctDiagnosticSeverity.Info,
                            string.Format("Opposed pair is {0:0.###} m apart (> {1} m wall thickness) - a real void (e.g. a shaft), not collapsed.",
                                a.PerpendicularSeparation(b), OPPOSED_PARTITION_MAX_SEPARATION),
                            face3D: a.Face3D, toleranceUsed: OPPOSED_PARTITION_MAX_SEPARATION);
                        continue;
                    }

                    // Overlap-footprint gate (Phase 2): the two skins must occupy (near) the same footprint.
                    // Gated on the overlap region, not full face area, so a door-cut skin (same footprint,
                    // smaller area) still collapses while a partial-overlap mis-pair (a long shared wall caught
                    // against a short partition) is left put.
                    double overlapRatio = a.InPlaneOverlapRatio(b);
                    if (overlapRatio < OPPOSED_PARTITION_MIN_OVERLAP_RATIO)
                    {
                        diagnostics?.Add(SolverStage.Snap, DiagnosticCode.RejectedCollapse, OcctDiagnosticSeverity.Info,
                            string.Format("Opposed pair overlaps only {0:P0} of the larger footprint (< {1:P0}) - distinct walls, not one partition.",
                                overlapRatio, OPPOSED_PARTITION_MIN_OVERLAP_RATIO),
                            face3D: a.Face3D, toleranceUsed: toleranceDistance);
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
        /// Sorts <paramref name="panels"/> by the snap-priority law (Phase 2b - the 2D
        /// <c>SnapAndAdjustWalls</c> ordering, SAM_Solver <c>SnapSolver.cs:962-967</c>, lifted to 3D): backers
        /// are processed before subordinates, and a tie at one key is broken by the next. <b>Weight</b>
        /// descending (primary - who dominates), then <b>BucketSize</b> descending (secondary - a wider
        /// capture reach outranks a narrower one at equal weight, so the panel that can actually reach a
        /// distant equal-weight partner is the one whose bucket does the reaching), then <b>Area</b>
        /// descending (tertiary - the larger surface anchors the smaller, mirroring the 2D law's Length key).
        /// Exposed (not inlined into <see cref="Snap"/>) so the ordering law itself is unit-testable.
        /// </summary>
        public static List<SnappedPanel> OrderForSnap(IEnumerable<SnappedPanel> panels)
        {
            return (panels ?? Enumerable.Empty<SnappedPanel>())
                .OrderByDescending(x => x.Weight)
                .ThenByDescending(x => x.BucketSize)
                .ThenByDescending(x => x.GetArea())
                .ToList();
        }

        /// <summary>
        /// Cheap bounding-box pre-filter for <see cref="Snap"/>'s candidate scan (Phase 2b, "shared with
        /// Phase 9"): only panels whose 3D bounding box lies within <paramref name="margin"/> of
        /// <paramref name="backer"/>'s box can possibly satisfy <c>BucketContains</c>/<c>AbutsColinearWithin</c>,
        /// so this rejects the rest before the more expensive plane/footprint predicates run. A superset, never
        /// a false negative: <paramref name="margin"/> should be at least as large as the reach any downstream
        /// test can accept (the caller passes backer's bucket size plus the colinear-align offset).
        /// </summary>
        public static IEnumerable<SnappedPanel> CandidatePanelsNear(SnappedPanel backer, IEnumerable<SnappedPanel> panels, double margin)
        {
            BoundingBox3D backerBox = backer?.GetBoundingBox();
            if (backerBox == null || panels == null)
            {
                yield break;
            }

            foreach (SnappedPanel candidate in panels)
            {
                BoundingBox3D box = candidate?.GetBoundingBox();
                if (box == null)
                {
                    continue;
                }

                if (backerBox.Min.X - margin <= box.Max.X && backerBox.Max.X + margin >= box.Min.X
                    && backerBox.Min.Y - margin <= box.Max.Y && backerBox.Max.Y + margin >= box.Min.Y
                    && backerBox.Min.Z - margin <= box.Max.Z && backerBox.Max.Z + margin >= box.Min.Z)
                {
                    yield return candidate;
                }
            }
        }

        /// <summary>
        /// Managed snap - ONE pass (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §F "the live one-shot
        /// greedy pass"; <see cref="SnapStage.SnapToFixedPoint"/> is what iterates this to convergence,
        /// Phase 2b). Sorts by <see cref="OrderForSnap"/> so backers are processed first, then projects each
        /// not-yet-snapped, near-parallel panel within reach onto the backer plane when either: (a) it lies
        /// within the backer's bucket slab AND overlaps it in-plane (a genuine double-wall); or (b) backer
        /// and candidate are both (near) vertical walls that are consecutive segments of one run - abutting
        /// or overlapping along the run, heights overlapping - offset by no more than
        /// <paramref name="alignColinearOffset"/> (a small Y-jog at a step). A near-parallel panel that
        /// merely passes through the slab but covers a different part of the plane (the next bay's wall),
        /// or is offset by more than the align distance, is a distinct wall and is left where it is.
        /// <para>
        /// Equal-weight (within 1%) pairs use the midpoint rule (Phase 2b, mirrors the 2D solver's tie-break,
        /// <see cref="SnappedPanel.MoveToMidplaneWith"/>): BOTH panels move to the midplane and BOTH buckets
        /// grow by the distance moved, rather than the earlier-sorted panel staying an unconditional backer.
        /// </para>
        /// </summary>
        /// <returns>True when at least one panel moved this pass - the fixed-point loop's convergence signal.</returns>
        public static bool Snap(List<SnappedPanel> panels, double toleranceAngle, double toleranceArcAngle, double toleranceDistance = Tolerance.Distance, double verticalAngleTolerance = 20 * (System.Math.PI / 180), double alignColinearOffset = 0.3)
        {
            if (panels == null || panels.Count < 2)
            {
                return false;
            }

            List<SnappedPanel> ordered = OrderForSnap(panels);
            bool anyChanged = false;

            for (int i = 0; i < ordered.Count; i++)
            {
                SnappedPanel backer = ordered[i];
                if (backer.Plane == null)
                {
                    continue;
                }

                double margin = backer.BucketSize + alignColinearOffset;
                foreach (SnappedPanel candidate in CandidatePanelsNear(backer, ordered.Skip(i + 1), margin).ToList())
                {
                    if (candidate.Snapped || candidate.Plane == null)
                    {
                        continue;
                    }

                    // Near-parallel panels snap onto the backer. Equal-weight neighbours are captured too
                    // (see the midpoint rule below); the sort guarantees candidate.Weight <= backer.Weight,
                    // so only a strictly higher-weight candidate (never produced by the sort) is skipped.
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
                    // is a separate wall and must not be dragged onto its neighbour. The in-plane overlap must
                    // also cover a real fraction of the larger footprint (Phase 2b): two distinct walls that
                    // merely share a supporting plane - e.g. two perimeter walls of a tilted level at a similar
                    // plane offset but metres apart in-plane - touch only at a corner (ratio well below the
                    // floor) and must NOT collapse together, which would drag one wall off its room and merge
                    // two cells. The co-parallel analogue of SnapOpposedPartitions' overlap-ratio gate, but at
                    // a lower floor because a genuine co-parallel double-wall (offset along the run, or unequal
                    // skins) legitimately overlaps less fully than a coincident opposed partition.
                    bool overlap = withinBucket
                        && backer.OverlapsInPlane(candidate, toleranceDistance)
                        && backer.InPlaneOverlapRatio(candidate) >= COPARALLEL_SNAP_MIN_OVERLAP_RATIO;

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

                    // Void guard (Phase 2): never collapse an OPPOSING (anti-parallel) pair separated by more
                    // than a wall thickness - that is a genuine void (a shaft), not a double-wall. Thin opposing
                    // partitions were already consumed by SnapOpposedPartitions (marked Snapped, skipped above),
                    // so any opposing pair reaching here beyond the thickness ceiling bounds real space and must
                    // survive Stage A. IsParallelWith treats anti-parallel as parallel, so without this the
                    // weighted snap would delete the void just like the pre-Phase-2 opposed-collapse did.
                    if (backer.Plane.Normal.Unit.DotProduct(candidate.Plane.Normal.Unit) < 0
                        && backer.PerpendicularSeparation(candidate) > OPPOSED_PARTITION_MAX_SEPARATION)
                    {
                        continue;
                    }

                    // Equal-weight (within 1%) midpoint rule (Phase 2b): move BOTH to the midplane and grow
                    // BOTH buckets by the distance moved, instead of treating the earlier-sorted panel as an
                    // unconditional backer. Reads backer.Plane live, so a backer already moved by an earlier
                    // candidate in this SAME pass is reached from its updated position.
                    bool equalWeight = System.Math.Abs(backer.Weight - candidate.Weight) <= backer.Weight * 0.01;
                    if (equalWeight)
                    {
                        double halfOffset = backer.PerpendicularSeparation(candidate) / 2.0;
                        bool backerMoved = backer.MoveToMidplaneWith(candidate, toleranceDistance);
                        bool candidateMoved = candidate.SnapToBacker(backer.Plane);
                        if (candidateMoved)
                        {
                            candidate.GrowBucket(halfOffset);
                        }

                        anyChanged = anyChanged || backerMoved || candidateMoved;
                    }
                    else
                    {
                        anyChanged = candidate.SnapToBacker(backer.Plane) || anyChanged;
                    }
                }
            }

            return anyChanged;
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

        // Raw-first attempt: build cells straight from the input faces (no managed clean/extend) and keep the
        // result only when the kernel returns a watertight envelope (zero naked edges). A well-modelled export
        // resolves this way directly; a gappy one leaves naked edges and we fall back to the managed pipeline.
        // Returns true (and populates ResolvedFace3Ds / NativeResolved / ResolvedCellCount) when adopted.
        private bool TryRawResolve(OcctBuildOptions options)
        {
            List<Face3D> rawFace3Ds = face3Ds.Where(x => x != null && x.IsValid()).ToList();
            if (rawFace3Ds.Count == 0)
            {
                return false;
            }

            // Same healing defaults as Resolve: sew sub-cm gaps and keep internal floors/partitions as shared
            // cell faces (a zoned complex, not just the outer envelope).
            OcctBuildOptions rawOptions = options ?? new OcctBuildOptions
            {
                AvoidInternalShapes = false,
                SewBeforeBuild = true,
                SewingTolerance = 0.01
            };

            List<Shell> shells = GeometryCreate.Shells(rawFace3Ds, out OcctCellComplexResult result, rawOptions);
            if (result == null || !result.NativeAvailable)
            {
                // Native kernel unavailable (e.g. non-Windows agent): let the managed pipeline run.
                result?.Dispose();
                return false;
            }

            int cells = result.Cells?.Count ?? 0;
            // Captured before Dispose() (which only tears down a retained native topology handle, never used
            // here): Cells itself is plain managed data, but reading it after Dispose() would be fragile.
            List<double> cellVolumes = result.Cells == null ? new List<double>() : result.Cells.Select(x => x.Volume).ToList();
            List<Face3D> resolved = shells == null
                ? new List<Face3D>()
                : shells.Where(x => x != null).SelectMany(x => x.Face3Ds ?? new List<Face3D>()).Where(x => x != null && x.IsValid()).ToList();
            result.Dispose();

            if (cells < 1 || resolved.Count == 0)
            {
                return false;
            }

            // Adopt the raw solve only when it is watertight; a gappy one leaves naked edges and the managed
            // clean+extend (gap repair) earns its keep. Short-circuit here (skip the merge/sliver/dropped work
            // below) exactly as before - EvaluateRawAdoption still sees the real naked-edge count and checks it
            // first, so passing placeholder zeros for the not-yet-computed sliver/dropped inputs is safe: the
            // rule never reaches them while naked edges are present.
            int nakedEdgeCount = ResolveStage.NakedEdgeCount(resolved, rawOptions);
            int sliverCellCount = 0;
            List<Face3D> droppedFace3Ds = new List<Face3D>();
            double droppedRatio = 0;

            if (nakedEdgeCount == 0)
            {
                // Merge coplanar neighbours so output faces are not left split where MakerVolume cut them.
                // Tightened to SAM's canonical Tolerance.Angle (~2 deg): post-resolve, faces are already
                // split by the kernel, so the caller's (typically 5 deg) ToleranceAngle is generous enough
                // to fuse slightly-sloped roof planes that should stay distinct (docs plan §C live-defect list).
                List<Face3D> merged = GeometryQuery.MergeCoplanarFace3Ds(resolved, out OcctCellComplexResult mergeResult, Tolerance.Angle, rawOptions);
                mergeResult?.Dispose();
                if (merged != null && merged.Count != 0)
                {
                    resolved = merged;
                }

                // A watertight envelope that resolves into a cell smaller than MinCellVolume is an artifact (a
                // hair's-width void), not evidence the raw solve got the room layout right.
                sliverCellCount = cellVolumes.Count(x => x < MinCellVolume);

                // Input faces the volume build did not use as a cell boundary (a partial/internal panel, or a
                // face that bounds no closed cell) - e.g. a partition that stops short of the ceiling and so
                // cannot split the room it was meant to divide. MakerVolume returns only cell-bounding faces,
                // and the watertight check above only validates those, so an unrepresented input panel would
                // otherwise be silently dropped, and the rooms it should have separated silently merge into one
                // cell even though the outer envelope stays watertight - the "watertight-but-wrong" gap a
                // naked-edge check alone cannot see.
                foreach (Face3D rawFace3D in rawFace3Ds)
                {
                    if (rawFace3D != null && rawFace3D.IsValid() && !IsRepresented(rawFace3D, resolved))
                    {
                        droppedFace3Ds.Add(rawFace3D);
                    }
                }

                droppedRatio = rawFace3Ds.Count == 0 ? 0 : (double)droppedFace3Ds.Count / rawFace3Ds.Count;
            }

            RawAdoptionOutcome outcome = EvaluateRawAdoption(cells, resolved.Count, nakedEdgeCount, sliverCellCount, droppedRatio, MaxDroppedRatio);
            switch (outcome)
            {
                case RawAdoptionOutcome.RejectedNakedEdges:
                    Diagnostics.Add(SolverStage.Resolve, DiagnosticCode.NakedEdge, OcctDiagnosticSeverity.Info,
                        string.Format("Raw (L0) resolve left {0} naked edge(s); falling through to the managed pipeline.", nakedEdgeCount),
                        toleranceUsed: rawOptions.EffectiveSewingTolerance);
                    return false;

                case RawAdoptionOutcome.RejectedSliverCell:
                    Diagnostics.Add(SolverStage.Resolve, DiagnosticCode.SliverCell, OcctDiagnosticSeverity.Warning,
                        string.Format("Raw (L0) resolve produced {0} sliver cell(s) (< {1} m3); not adopted.", sliverCellCount, MinCellVolume),
                        toleranceUsed: MinCellVolume);
                    return false;

                case RawAdoptionOutcome.RejectedDroppedRatio:
                    Diagnostics.Add(SolverStage.Resolve, DiagnosticCode.DroppedFace, OcctDiagnosticSeverity.Warning,
                        string.Format("Raw (L0) resolve dropped {0} of {1} input face(s) ({2:P0} > {3:P0} max); not adopted.",
                            droppedFace3Ds.Count, rawFace3Ds.Count, droppedRatio, MaxDroppedRatio));
                    return false;

                case RawAdoptionOutcome.RejectedNoCells:
                    return false; // unreachable here (already returned above); kept for switch exhaustiveness
            }

            // Re-add the dropped faces (RetainDropped contract) - same faces the gate above already measured,
            // so this does not re-run IsRepresented.
            if (RetainDropped && droppedFace3Ds.Count != 0)
            {
                resolved = resolved.Concat(droppedFace3Ds).ToList();
            }

            ResolvedFace3Ds = resolved;
            NativeResolved = true;
            ResolvedCellCount = cells;
            NakedEdgePoint3Ds = new List<Point3D>();

            // Coarse source mapping over the adopted raw output (Phase 2, managed-only stand-in for the native
            // history composed in Phase 3): every output face is attributed to the raw input face(s) it derives
            // from, so no output face is source-orphaned.
            SourceMap = BuildResolvedSourceMap(resolved, rawFace3Ds);

            RawAttemptSignature = new ClosureSignature3D(cells, cellVolumes, nakedEdgeCount: 0, faceCount: resolved.Count, droppedCount: droppedFace3Ds.Count);
            Signature = RawAttemptSignature;
            Diagnostics.Add(SolverStage.Resolve, DiagnosticCode.AdoptedLevel, OcctDiagnosticSeverity.Info,
                string.Format("Adopted raw (L0): {0}", Signature));

            return true;
        }

        /// <summary>
        /// The raw-first (L0) adoption gate's decision rule, pure and native-free so it is unit-testable
        /// without a kernel: given what a raw resolve measured, decides whether it is trusted as-is or the
        /// managed pipeline should run instead. Checked in this order - no cells formed, a gappy envelope
        /// (naked edges), then the two "watertight-but-wrong" cases a naked-edge check alone cannot see (a
        /// sliver artifact cell, or too many input faces silently dropped because they bound no closed cell) -
        /// each closes a distinct failure mode found on real fixtures
        /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §C, Phase 1).
        /// </summary>
        public static RawAdoptionOutcome EvaluateRawAdoption(int cellCount, int resolvedFaceCount, int nakedEdgeCount, int sliverCellCount, double droppedRatio, double maxDroppedRatio)
        {
            if (cellCount < 1 || resolvedFaceCount == 0)
            {
                return RawAdoptionOutcome.RejectedNoCells;
            }

            if (nakedEdgeCount > 0)
            {
                return RawAdoptionOutcome.RejectedNakedEdges;
            }

            if (sliverCellCount > 0)
            {
                return RawAdoptionOutcome.RejectedSliverCell;
            }

            if (droppedRatio > maxDroppedRatio)
            {
                return RawAdoptionOutcome.RejectedDroppedRatio;
            }

            return RawAdoptionOutcome.Adopted;
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
