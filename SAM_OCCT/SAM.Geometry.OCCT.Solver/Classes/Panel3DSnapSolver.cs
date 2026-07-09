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

        /// <summary>Under-split gate (codex #7): a dropped wall-like face counts as a room-dividing partition
        /// only when its vertical extent spans at least this fraction of the cell it sits inside AND its plan
        /// width (perpendicular to its own normal) spans at least <see cref="UNDER_SPLIT_MIN_PLAN_RATIO"/> of the
        /// cell - i.e. it very nearly fills the cell's cross-section, the way a wall that genuinely divides a
        /// room into two must. A partial-height fin, a short balcony upstand, or a fragment interior to a large
        /// real room all fall short of one bound and are ignored. Deliberately high (conservative): a false
        /// positive pushes a well-modelled input onto the weaker managed pipeline.</summary>
        public const double UNDER_SPLIT_MIN_HEIGHT_RATIO = 0.8;

        /// <summary>Under-split gate (codex #7): the minimum fraction of the containing cell's plan width - in
        /// the horizontal direction perpendicular to the dropped partition's own normal - the partition must
        /// span to count as a room divider (a real partition reaches wall-to-wall). Paired with
        /// <see cref="UNDER_SPLIT_MIN_HEIGHT_RATIO"/>; both must hold.</summary>
        public const double UNDER_SPLIT_MIN_PLAN_RATIO = 0.7;

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

        /// <summary>
        /// The inter-storey stacked-slab interfaces detected in the input (Phase 6d,
        /// <see cref="StackedSlabInterfaceDetector"/>): near-congruent, opposite-facing floor/ceiling skin pairs
        /// that represent one physical inter-storey boundary. Purely observational - detecting them does NOT
        /// change the resolved geometry or the <see cref="SourceMap"/>; the geometric "one interface for the cell
        /// build" is done by the existing sew / opposed-partition collapse, and this records/verifies/diagnoses it
        /// (both source panels preserved, unsafe cavities/landings rejected). Reset each <see cref="Execute"/>.
        /// </summary>
        public List<StackedSlabInterface> StackedSlabInterfaces { get; private set; } = new List<StackedSlabInterface>();

        /// <summary>Step 2: after the resolve, re-sew the resolved faces at an expanded tolerance to stitch the
        /// floor/wall slot gaps that survive the volume build, instead of patching them with fabricated faces.
        /// The sewn result is kept only when it strictly reduces the naked-edge count. Default true.</summary>
        public bool SewResidualGaps { get; set; } = true;

        /// <summary>Upper bound (metres) on the post-resolve sew tolerance - how wide a residual floor/wall slot
        /// the sew may bridge. Larger than the pre-build <c>SewingTolerance</c> (which only closes sub-cm gaps),
        /// but clamped (≤ 0.3 m) so unrelated near edges are not over-merged. Additionally capped below half the
        /// closest near-parallel gap by <see cref="SewSafetyFactor"/> (Phase 5d). Default 0.1 m.</summary>
        public double SewExpandTolerance { get; set; } = 0.1;

        /// <summary>
        /// Phase 5d: the fraction of the closest near-parallel gap the adaptive sew tolerance is capped at
        /// (<c>HealStage.SewV2</c>). The measured <see cref="MinPairSeparation"/> × this factor bounds the sew,
        /// so a global sew can never bridge more than half a genuine double-wall / cavity gap and fuse it. At the
        /// default 0.5 an 0.08 m double wall caps the sew at 0.04 m - it stays unsewable while an unrelated wider
        /// gap is closed (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §F). Default 0.5.
        /// </summary>
        public double SewSafetyFactor { get; set; } = HealStage.DEFAULT_SewSafetyFactor;

        /// <summary>Step 2: after the resolve (and the sew pass), build a Face3D over each residual naked-boundary
        /// loop (air-panel candidate) so every space is fully enclosed. Default true. (Step 1 strips input holes
        /// outright.)</summary>
        public bool FillHoles { get; set; } = true;

        /// <summary>
        /// Phase 5b: after gap-fill patches and retained faces are assembled, run ONE signature-gated
        /// consolidation rebuild (<c>Create.Shells</c> over resolved + patches + retained) instead of appending
        /// them unimprinted - the kernel then mutually imprints and trims them into the cell complex. The
        /// rebuilt faces are adopted only when they do not regress the closure (cells not reduced, naked not
        /// increased vs the appended set); otherwise the appended set is kept with a Warning. Default true; set
        /// false to keep the pre-5b append-unimprinted behaviour (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §H).
        /// </summary>
        public bool ConsolidateRebuild { get; set; } = true;

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

        /// <summary>Per-operation observability for the managed conditioning passes (E3,
        /// docs/EXTEND3D_ROBUST_HANDOVER.md): one <see cref="ExtendRecord"/> per applied extend/fill
        /// mutation (which panel, which edge, from where to where, toward what target). Populated only on
        /// the managed path (empty when the raw solve is adopted, and on <c>StopAfterClean</c>). Points are
        /// in the world frame. Recording only - the geometry is byte-identical to a run without it.</summary>
        public IReadOnlyList<ExtendRecord> ExtendRecords { get; private set; } = new List<ExtendRecord>();

        /// <summary>The resolved faces. After native resolve these are split/merged; otherwise the snapped faces.</summary>
        public List<Face3D> ResolvedFace3Ds { get; private set; } = new List<Face3D>();

        /// <summary>Locations of naked (free) boundary edges reported by the native validator.</summary>
        public List<Point3D> NakedEdgePoint3Ds { get; private set; } = new List<Point3D>();

        /// <summary>True when the native OCCT kernel ran the resolve stage; false for a managed-only result.</summary>
        public bool NativeResolved { get; private set; }

        /// <summary>
        /// True when the raw-first (L0) attempt was adopted (<see cref="TryRawResolve"/> returned true) -
        /// i.e. the managed clean/extend/resolve pipeline below never ran. False when the managed pipeline
        /// produced the adopted result (raw rejected, or <see cref="ForceManagedPipeline"/>/<see cref="StopAfterClean"/>/
        /// <see cref="StopAfterExtend"/> skipped the raw attempt). Additive (Phase 8): lets a caller report
        /// which path was adopted without re-deriving it from diagnostic message text.
        /// </summary>
        public bool RawAdopted { get; private set; }

        /// <summary>Number of closed cells (rooms/levels) the native MakerVolume formed. 1 = single space; 0 = none.</summary>
        public int ResolvedCellCount { get; private set; }

        /// <summary>
        /// Per-cell metadata (index, volume, centre, boundary shell) for the ADOPTED resolve (Phase 7a).
        /// Captured at the exact point <see cref="Signature"/> is produced on both paths (raw:
        /// <see cref="TryRawResolve"/>; managed: <see cref="FinalizeAndValidate"/>), so
        /// <see cref="ClosureSignature3D.CellCount"/>/<see cref="ClosureSignature3D.CellVolumes"/> and this
        /// list always agree. Reuses cell metadata the native decode already exposes - no new native ABI.
        /// Reset (empty) at the start of each <see cref="Execute"/> call.
        /// </summary>
        public IReadOnlyList<SolverCell> Cells { get; private set; } = new List<SolverCell>();

        /// <summary>Identifies this solve (a fresh GUID per <see cref="Execute"/> call). Stamped onto
        /// <see cref="ResolvedCellComplex.SolveId"/> so a downstream consumer can prove a panel set came from
        /// THIS solve before consuming the complex directly (P3 roster gate).</summary>
        public System.Guid SolveId { get; private set; }

        /// <summary>
        /// The cell complex the solver ADOPTED, as a first-class pure-managed product (Phase P2). Projected
        /// once from the same native decode that produced <see cref="Cells"/>/<see cref="Signature"/>, before
        /// that result is disposed - so it carries no native lifetime. Null when no cell complex was adopted
        /// (native unavailable, no result, or a Stop-after-clean/extend pass that never resolves). Additive:
        /// capturing it changes no adopted geometry, cell count, or signature.
        /// </summary>
        public ResolvedCellComplex ResolvedCellComplex { get; private set; }

        /// <summary>Step 1 output: clean single panels - external shape only, within-bucket parallels snapped
        /// onto one backer, contained/overlapping coplanar faces merged. The input to Step 2 (fill/extend).</summary>
        public List<Face3D> CleanFace3Ds { get; private set; } = new List<Face3D>();

        /// <summary>
        /// The level datums (Phase 6a <see cref="LevelFrame"/>) clustered from the managed clean bucket's caps
        /// (<see cref="SnapStage.Clean"/>), captured for reporting (Phase 8). Empty when <see cref="RawAdopted"/>
        /// is true (the raw path never clusters caps into frames - see <see cref="RawAttemptSignature"/>) or
        /// when no cap formed a frame (the legacy world-frame fallback ran instead). Read-only capture of data
        /// the managed pipeline already computes; does not change any solver geometry or algorithm.
        /// </summary>
        public IReadOnlyList<LevelFrame> LevelFrames { get; private set; } = new List<LevelFrame>();

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
            RawAdopted = false;
            ResolvedCellCount = 0;
            Cells = new List<SolverCell>();
            LevelFrames = new List<LevelFrame>();
            ExtendRecords = new List<ExtendRecord>();
            Diagnostics = new SolverDiagnostics();
            Signature = null;
            RawAttemptSignature = null;
            StackedSlabInterfaces = new List<StackedSlabInterface>();
            NakedWires = new List<OcctNakedWire>();
            ResolveHistorySourceMap = null;
            ResolvedCellComplex = null;
            SolveId = System.Guid.NewGuid();

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
                RawAdopted = true;
                DetectStackedSlabInterfaces(); // Phase 6d: observational - detect/verify/diagnose, no geometry change
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
            LevelFrames = snapResult.LevelFrames;

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
            //
            // TODO (Phase 6c deferral): the plan calls for running extend/fill PER LEVEL FRAME rather than in
            // this single global-Up frame. A prototype that clustered the caps into level frames and conditioned
            // each orientation group in its own frame was measured to regress the whole-level-tilted RAW golden
            // master (22 -> 8 cells): that fixture is ONE analytical level whose caps span two very different
            // tilts (~34° floors and ~56° roof faces), and splitting the conditioning across those orientations
            // severs the walls/caps that must meet between them. Per-frame extend/fill therefore needs a safer
            // design (condition in a dominant frame, and split ONLY across proven-separate storeys, never within
            // a single multi-orientation level) and is deferred to a later focused sub-phase. 6c lands the
            // frame-aware cap normalization (NormalizeCaps per LevelFrame) only, which preserves split-level
            // landings without touching this conditioning path. Raw golden masters stay byte-identical.
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

            // E3: collect one observability record per applied extend/fill mutation (recording only - the
            // geometry is byte-identical to a run with a null recorder).
            List<ExtendRecord> extendRecords = new List<ExtendRecord>();
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
                tolerances,
                extendRecords);

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

                // The extend records' preview points were captured in the canonical frame - rotate them too so
                // the moved-edge preview segments land where the world-frame geometry does.
                foreach (ExtendRecord extendRecord in extendRecords)
                {
                    extendRecord.From = extendRecord.From?.Transform(fromCanonical);
                    extendRecord.To = extendRecord.To?.Transform(fromCanonical);
                }
            }

            // E3 source attribution: the records carry CLEAN-face ordinals (Register stamps the clean ordinal
            // as each SnappedPanel's source index, and conditioning never merges panels). Map them back to the
            // ORIGINAL input source index via the snap stage's per-clean-face attribution, so the analytical
            // layer resolves the right source Guid even when Stage A merged/reordered faces (before this the
            // SAM_OCCT_EXTEND3D_PANEL line could name the wrong panel). Recording-only - geometry untouched.
            RemapExtendRecordSourceIndices(extendRecords, snapResult.SourceIndicesPerFace);

            ExtendRecords = extendRecords;
            ResolvedFace3Ds = snappedFace3Ds;

            // Stop before the native resolve: the split (MakerVolume trim) stays in Solve3D. The output here
            // is the filled caps + extended (overshooting) walls, for reviewing the pre-resolve geometry.
            if (StopAfterExtend)
            {
                SourceMap = BuildResolvedSourceMap(ResolvedFace3Ds, face3Ds);
                return;
            }

            // Native RESOLVE (Stage B). Gap-fill is NOT run here any more (fillHoles: false): Phase 5b moves
            // it into FinalizeAndValidate so the FINAL naked count is measured AFTER patches/retains, not
            // before (the "pre-patch naked-count lie" §B/§H). The wires/naked points the resolve reports are
            // INTERMEDIATE diagnostics only - the outward truth is produced by FinalizeAndValidate.
            ResolveStage.Result resolveResult = ResolveStage.Resolve(snappedFace3Ds, options, ToleranceAngle, SewResidualGaps, SewExpandTolerance, SewSafetyFactor, false, Diagnostics);
            BucketMergedFace3Ds = resolveResult.BucketMergedFace3Ds;
            NativeResolved = resolveResult.NativeResolved;

            if (!resolveResult.NativeResolved)
            {
                // Native kernel unavailable (e.g. non-Windows agent): keep the managed snap result
                // (ResolvedFace3Ds already = snappedFace3Ds) and report a best-effort zero-cell signature.
                NakedWires = resolveResult.NakedWires ?? new List<OcctNakedWire>();
                ResolveHistorySourceMap = null;
                SourceMap = BuildResolvedSourceMap(ResolvedFace3Ds, face3Ds);
                Signature = new ClosureSignature3D(0, new List<double>(), NakedEdgePoint3Ds?.Count ?? 0, ResolvedFace3Ds?.Count ?? 0, DroppedSourceCount());
                return;
            }

            List<Face3D> resolvedFace3Ds = resolveResult.ResolvedFace3Ds ?? new List<Face3D>();

            // Base source map over the INTERMEDIATE resolved faces (pre-heal, pre-patch): exact via composed
            // native history when available, geometric fallback otherwise. FinalizeAndValidate carries this
            // forward across the consolidation rebuild, never discarding it (owner caution 2).
            SourceMap resolvedSourceMap;
            if (resolveResult.SourceMap != null)
            {
                SourceMap snappedToSource = BuildResolvedSourceMap(snappedFace3Ds, face3Ds);
                ResolveHistorySourceMap = snappedToSource.Compose(resolveResult.SourceMap);
                resolvedSourceMap = BackfillGeometric(CloneSourceMap(ResolveHistorySourceMap), resolvedFace3Ds, face3Ds);
            }
            else
            {
                ResolveHistorySourceMap = null;
                resolvedSourceMap = BuildResolvedSourceMap(resolvedFace3Ds, face3Ds);
            }

            // Stage C - HEAL (RetainDropped v2, Phase 5c): re-add the ORIGINAL CLEAN geometry (SnapStage
            // output) for every input source the native resolve DROPPED - detected map-side
            // (resolvedSourceMap.FacesOf(source) empty), not via a geometric nearest-source guess - behind
            // area/dedup safety filters, tagged for Provenance.DroppedRetained and diagnosed. The clean faces
            // (snapResult.CleanFace3Ds) and their per-face source indices are world-frame and index-aligned to
            // the resolved output, so IsRepresented dedups correctly. The imprint into the cell complex is the
            // consolidation rebuild's job (FinalizeAndValidate), not a raw append.
            HealStage.RetainDroppedResult retainResult = RetainDropped
                ? HealStage.RetainDroppedV2(resolvedFace3Ds, snapResult.CleanFace3Ds, snapResult.SourceIndicesPerFace, resolvedSourceMap, Diagnostics)
                : new HealStage.RetainDroppedResult();
            List<Face3D> retainedFace3Ds = retainResult.RetainedFace3Ds;

            // GapFill v2: patch the residual naked loops from the NATIVE ordered wires (the legacy managed
            // walk survives only as the pre-v4 fallback). Every loop outcome is diagnosed.
            List<Face3D> patchFace3Ds = FillHoles
                ? BuildGapFillPatches(resolvedFace3Ds, resolveResult.NakedWires, resolveResult.NakedEdgePoint3Ds)
                : new List<Face3D>();

            // The single final-truth step: assemble resolved + patches + retained, run the signature-gated
            // consolidation rebuild, compose provenance, and produce the FINAL naked count / wires / cells /
            // signature (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §H).
            FinalizeAndValidate(resolvedFace3Ds, patchFace3Ds, retainedFace3Ds, retainResult.RetainedSourceIndices, resolvedSourceMap, resolveResult.ResolvedCellCount, options);

            DetectStackedSlabInterfaces(); // Phase 6d: observational - detect/verify/diagnose, no geometry change
        }

        /// <summary>
        /// Phase 6d: detects the inter-storey stacked-slab interfaces in the input and verifies that both
        /// analytical source panels of each survive into the <see cref="SourceMap"/>. Purely observational -
        /// it records <see cref="StackedSlabInterfaces"/> and emits diagnostics (accepted interface / rejected
        /// cavity-or-landing / incomplete provenance) but changes NO geometry and NO source mapping, so every
        /// golden-master signature (raw and managed) is byte-identical to before it ran. The geometric merge of
        /// two congruent skins into one interface is already performed by the native sew (raw path) and
        /// <see cref="SnapOpposedPartitions"/> (managed path); this makes that handling explicit, frame-aware,
        /// and provenance-checked.
        /// </summary>
        private void DetectStackedSlabInterfaces()
        {
            StackedSlabInterfaces = StackedSlabInterfaceDetector.DetectStackedInterfaces(
                face3Ds,
                ToleranceAngle,
                StackedSlabInterfaceDetector.DEFAULT_MaxSlabSeparation,
                StackedSlabInterfaceDetector.DEFAULT_MinOverlapRatio,
                VerticalAngleTolerance,
                Diagnostics);

            StackedSlabInterfaceDetector.VerifyRepresented(StackedSlabInterfaces, SourceMap, Diagnostics);
        }

        /// <summary>
        /// Builds the residual-loop patch faces for the managed pipeline. Primary input: the native ordered
        /// naked <paramref name="nakedWires"/> (ABI v4) via <see cref="GapFill.FromNakedWires"/>. Falls back to
        /// the legacy managed loop-walk (<see cref="GapFill.NakedLoopFace3Ds"/>) ONLY when the wires are
        /// unavailable (a pre-v4 native) but naked points remain - emitting an Info diagnostic that the
        /// fallback ran (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §G).
        /// </summary>
        private List<Face3D> BuildGapFillPatches(List<Face3D> resolvedFace3Ds, List<OcctNakedWire> nakedWires, List<Point3D> nakedPoint3Ds)
        {
            List<OcctNakedWire> wires = nakedWires ?? new List<OcctNakedWire>();
            if (wires.Count != 0)
            {
                return GapFill.FromNakedWires(wires, Diagnostics, ToleranceDistance).Patches;
            }

            if (nakedPoint3Ds != null && nakedPoint3Ds.Count != 0)
            {
                Diagnostics.Add(SolverStage.Heal, DiagnosticCode.NakedLoop, OcctDiagnosticSeverity.Info,
                    "GapFill: native naked wires unavailable; using the legacy managed loop-walk fallback.");
                return GapFill.NakedLoopFace3Ds(resolvedFace3Ds, nakedPoint3Ds, 0.01);
            }

            return new List<Face3D>();
        }

        /// <summary>
        /// The single final-truth step (Phase 5b, docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §H).
        /// Assembles <paramref name="resolvedFace3Ds"/> + <paramref name="patchFace3Ds"/> +
        /// <paramref name="retainedFace3Ds"/>; when either the patches or retained set is non-empty and
        /// <see cref="ConsolidateRebuild"/> is on, runs ONE <c>Create.Shells</c> consolidation rebuild
        /// (the direct build path, which captures ABI v4 history) and adopts its faces only when they do not
        /// regress the closure (cells &gt;= pre-rebuild cells AND naked &lt;= the appended set's naked) -
        /// otherwise keeps the appended (unimprinted) set with a Warning. Provenance is preserved: the
        /// pre-rebuild map is composed with the rebuild history (patch faces keep their GapFill identity via
        /// history), and geometric backfill runs ONLY for faces left unmapped - never overwriting a mapped
        /// entry. Ends with the ONE outward <c>Validate</c> that produces the reported naked count / wires,
        /// and the managed-path <see cref="Signature"/>.
        /// </summary>
        private void FinalizeAndValidate(
            List<Face3D> resolvedFace3Ds,
            List<Face3D> patchFace3Ds,
            List<Face3D> retainedFace3Ds,
            List<List<int>> retainedSourceIndices,
            SourceMap resolvedSourceMap,
            int resolveCellCount,
            OcctBuildOptions options)
        {
            OcctBuildOptions occtOptions = options ?? new OcctBuildOptions
            {
                AvoidInternalShapes = false,
                SewBeforeBuild = true,
                SewingTolerance = 0.01
            };

            int patchStart = resolvedFace3Ds.Count;
            int retainedStart = patchStart + patchFace3Ds.Count;

            // The appended (unimprinted) set: resolved, then patches, then retained - a fixed order the
            // pre-rebuild source map and the rebuild history are both keyed against.
            List<Face3D> appended = new List<Face3D>(resolvedFace3Ds);
            appended.AddRange(patchFace3Ds);
            appended.AddRange(retainedFace3Ds);

            // Pre-rebuild map over `appended`: resolved keep their attribution; patches are fabricated GapFill;
            // retained (Phase 5c) are recorded DroppedRetained against the exact dropped source(s) RetainDroppedV2
            // detected, so the retained wall keeps its source Guid through reconstruction (P4) rather than being
            // re-attributed geometrically.
            SourceMap preMap = CloneSourceMap(resolvedSourceMap);
            for (int i = 0; i < patchFace3Ds.Count; i++)
            {
                preMap.RecordFabricated(new FaceKey(patchStart + i), Provenance.GapFill);
            }

            RecordRetainedProvenance(preMap, retainedStart, retainedFace3Ds.Count, retainedSourceIndices);

            List<Face3D> finalFaces = appended;
            List<double> cellVolumes = new List<double>();
            List<SolverCell> solverCells = new List<SolverCell>();
            int cellCount = resolveCellCount;
            SourceMap finalMap = null;
            bool adoptedRebuild = false;
            // P2: the adopted complex, projected from whichever decode produces the final cells below
            // (the rebuild result if adopted, else the DecodeCellVolumes decode). Naked wires are stitched
            // in at the end, once the single outward Validate has measured them.
            ResolvedCellComplex managedComplex = null;

            bool tryRebuild = ConsolidateRebuild && (patchFace3Ds.Count + retainedFace3Ds.Count) > 0;

            // The APPENDED (unimprinted) set is the fallback kept if the consolidation rebuild is rejected, so
            // its OWN decoded cell/naked counts are the correct no-regress baseline - NOT the pre-append
            // resolveCellCount (codex #3). resolveCellCount is measured before patches/retained were appended
            // and is typically LOWER, which let a rebuild that DISSOLVED a separator (fewer cells than the
            // appended fallback = two rooms merged into one) still pass rebuiltCells >= resolveCellCount and be
            // wrongly adopted. Decode the appended set ONCE here (only when a rebuild is attempted) and reuse it
            // in the reject path below, so the reject path adds no second decode.
            int appendedNaked = 0;
            int appendedCells = resolveCellCount;
            List<double> appendedVolumes = new List<double>();
            List<SolverCell> appendedSolverCells = new List<SolverCell>();
            ResolvedCellComplex appendedComplex = null;
            if (tryRebuild)
            {
                appendedNaked = ResolveStage.NakedEdgeCount(appended, occtOptions);
                appendedVolumes = DecodeCellVolumes(appended, occtOptions, SolveId, out appendedCells, out appendedSolverCells, out appendedComplex);
            }

            if (tryRebuild)
            {
                // The DIRECT build path (SewBeforeBuild = false) - the only one that captures ABI v4 history,
                // so the rebuild's provenance can be composed onto the existing map.
                OcctBuildOptions rebuildOptions = new OcctBuildOptions(occtOptions)
                {
                    SewBeforeBuild = false,
                    AvoidInternalShapes = false
                };

                List<Shell> shells = GeometryCreate.Shells(appended, out OcctCellComplexResult rebuildResult, rebuildOptions);
                if (rebuildResult != null && rebuildResult.NativeAvailable)
                {
                    List<Face3D> rebuiltFaces = shells == null
                        ? new List<Face3D>()
                        : shells.Where(x => x != null).SelectMany(x => x.Face3Ds ?? new List<Face3D>()).Where(x => x != null && x.IsValid()).ToList();
                    int rebuiltCells = rebuildResult.Cells?.Count ?? 0;
                    List<double> rebuiltVolumes = rebuildResult.Cells == null ? new List<double>() : rebuildResult.Cells.Select(x => x.Volume).ToList();
                    // Phase 7a: the same per-cell snapshot, captured alongside rebuiltVolumes so it reflects
                    // exactly the cells the acceptance rule below measures.
                    List<SolverCell> rebuiltSolverCells = rebuildResult.Cells == null ? new List<SolverCell>() : rebuildResult.Cells.Select((x, idx) => new SolverCell(idx, x.Volume, x.Center, x.Shell)).ToList();
                    OcctHistory rebuildHistory = rebuildResult.History;
                    int rebuiltNaked = ResolveStage.NakedEdgeCount(rebuiltFaces, occtOptions);

                    // Accept iff the rebuild does not regress versus the appended-unimprinted fallback it would
                    // replace (§H / §I acceptance rule); baseline is the appended set's OWN counts (codex #3).
                    if (AcceptConsolidationRebuild(rebuiltFaces.Count, rebuiltCells, rebuiltNaked, appendedCells, appendedNaked))
                    {
                        finalFaces = rebuiltFaces;
                        cellCount = rebuiltCells;
                        cellVolumes = rebuiltVolumes;
                        solverCells = rebuiltSolverCells;
                        finalMap = ComposeRebuildMap(preMap, rebuildHistory, patchStart, patchFace3Ds.Count, retainedStart, retainedFace3Ds.Count, retainedSourceIndices, rebuiltFaces);
                        adoptedRebuild = true;
                        managedComplex = ResolvedCellComplex.Project(rebuildResult, null, SolveId); // this decode is the adopted one
                        Diagnostics.Add(SolverStage.Heal, DiagnosticCode.AdoptedLevel, OcctDiagnosticSeverity.Info,
                            string.Format("Consolidation rebuild adopted: {0} cell(s), {1} naked edge(s) (appended alternative had {2}).", rebuiltCells, rebuiltNaked, appendedNaked));
                    }
                    else
                    {
                        Diagnostics.Add(SolverStage.Heal, DiagnosticCode.RejectedSew, OcctDiagnosticSeverity.Warning,
                            string.Format("Consolidation rebuild regressed (cells {0} vs appended {1}; naked {2} vs appended {3}); patches/retained appended unimprinted.", rebuiltCells, appendedCells, rebuiltNaked, appendedNaked));
                    }
                }

                rebuildResult?.Dispose();
            }

            if (!adoptedRebuild)
            {
                // Keep the appended set. Attribute it geometrically, then re-assert the explicit GapFill mark
                // on the patch faces (so they still surface as air, not solids) and the DroppedRetained mark on
                // the retained faces (so they stay identifiable as recovered dropped geometry) downstream.
                finalMap = BuildResolvedSourceMap(appended, face3Ds);
                for (int i = 0; i < patchFace3Ds.Count; i++)
                {
                    finalMap.RecordFabricated(new FaceKey(patchStart + i), Provenance.GapFill);
                }

                RecordRetainedProvenance(finalMap, retainedStart, retainedFace3Ds.Count, retainedSourceIndices);

                if (tryRebuild)
                {
                    // Reuse the up-front appended decode (codex #3) - the fallback geometry is exactly `appended`,
                    // already decoded for the acceptance baseline, so do not decode it a second time.
                    cellVolumes = appendedVolumes;
                    cellCount = appendedCells;
                    solverCells = appendedSolverCells;
                    managedComplex = appendedComplex;
                }
                else
                {
                    cellVolumes = DecodeCellVolumes(appended, occtOptions, SolveId, out cellCount, out solverCells, out managedComplex);
                }
            }

            // Publish the adopted geometry + provenance. HoleFillFace3Ds stays the patch set (the air-panel
            // candidates) - empty when fill+sew closed everything, so the "no fabricated air face" contract holds.
            ResolvedFace3Ds = finalFaces;
            ResolvedCellCount = cellCount;
            Cells = solverCells;
            HoleFillFace3Ds = patchFace3Ds ?? new List<Face3D>();
            SourceMap = finalMap ?? new SourceMap();

            // The ONE outward validate: naked count + wires measured AFTER patches/retains (the single
            // final-truth producer; every earlier validate is an intermediate diagnostic only).
            GeometryQuery.Validate(finalFaces, out OcctValidationReport report, out OcctCellComplexResult validateResult, occtOptions, false);
            validateResult?.Dispose();

            List<Point3D> nakedPoint3Ds = new List<Point3D>();
            List<OcctNakedWire> nakedWires = new List<OcctNakedWire>();
            if (report != null)
            {
                nakedPoint3Ds = report
                    .IssuesOf(OcctValidationIssueCategory.NakedEdge)
                    .Where(x => x?.Location != null)
                    .Select(x => x.Location)
                    .ToList();
                nakedWires = report.NakedWires?.ToList() ?? new List<OcctNakedWire>();
            }

            NakedEdgePoint3Ds = nakedPoint3Ds;
            NakedWires = nakedWires;
            // P2: publish the complex captured from the adopted decode, now with the naked wires the single
            // outward Validate just measured stitched in.
            ResolvedCellComplex = managedComplex?.WithNakedWires(nakedWires);
            Signature = new ClosureSignature3D(cellCount, cellVolumes, nakedPoint3Ds.Count, finalFaces.Count, DroppedSourceCount(), cellVolumes.Count(x => x < MinCellVolume));
        }

        /// <summary>
        /// Composes the pre-rebuild source map onto the consolidation rebuild's ABI v4 history so every
        /// existing source is carried from its appended-set ordinal to its rebuilt ordinal (owner caution 2 -
        /// provenance is never discarded). Patch faces keep their <see cref="Provenance.GapFill"/> identity via
        /// the same history (their input ordinals -&gt; rebuilt ordinals). Geometric backfill runs ONLY for
        /// rebuilt faces still unmapped (retained-derived, or a history gap), never overwriting a mapped entry.
        /// When history is unavailable, falls back to a full geometric attribution over the rebuilt faces.
        /// </summary>
        private SourceMap ComposeRebuildMap(SourceMap preMap, OcctHistory rebuildHistory, int patchStart, int patchCount, int retainedStart, int retainedCount, List<List<int>> retainedSourceIndices, List<Face3D> rebuiltFaces)
        {
            if (rebuildHistory == null)
            {
                return BuildResolvedSourceMap(rebuiltFaces, face3Ds);
            }

            SourceMap historyHop = HistorySourceMap.ToSourceMap(rebuildHistory, Provenance.Resolved, Diagnostics);
            SourceMap composed = preMap.Compose(historyHop);

            // Re-assert GapFill on the patch-derived rebuilt faces (Compose adopts the later hop's provenance,
            // which would otherwise relabel them Resolved). History-precise, no geometry.
            for (int p = 0; p < patchCount; p++)
            {
                int input = patchStart + p;
                foreach (int ordinal in rebuildHistory.ModifiedOrdinals(input))
                {
                    composed.RecordFabricated(new FaceKey(ordinal), Provenance.GapFill);
                }

                foreach (int ordinal in rebuildHistory.GeneratedOrdinals(input))
                {
                    composed.RecordFabricated(new FaceKey(ordinal), Provenance.GapFill);
                }
            }

            // Re-assert DroppedRetained on the retained-derived rebuilt faces (Phase 5c) against the exact
            // dropped source(s), for the same reason - Compose would otherwise relabel them Resolved. The real
            // source is preserved so reconstruction keeps its Guid (P4); history-precise, no geometry.
            for (int r = 0; r < retainedCount; r++)
            {
                int input = retainedStart + r;
                List<int> sources = retainedSourceIndices != null && r < retainedSourceIndices.Count ? retainedSourceIndices[r] : null;
                foreach (int ordinal in rebuildHistory.ModifiedOrdinals(input).Concat(rebuildHistory.GeneratedOrdinals(input)))
                {
                    RecordDroppedRetainedAt(composed, new FaceKey(ordinal), sources);
                }
            }

            return BackfillGeometric(composed, rebuiltFaces, face3Ds);
        }

        /// <summary>
        /// Records <see cref="Provenance.DroppedRetained"/> for the retained faces at their appended-set ordinals
        /// (Phase 5c). Each retained face is keyed against the exact dropped source(s)
        /// <see cref="HealStage.RetainDroppedV2"/> recovered - so panel reconstruction keeps the source Guid (P4) -
        /// or, if none is known, as a fabricated retained face.
        /// </summary>
        private static void RecordRetainedProvenance(SourceMap sourceMap, int retainedStart, int retainedCount, List<List<int>> retainedSourceIndices)
        {
            for (int i = 0; i < retainedCount; i++)
            {
                List<int> sources = retainedSourceIndices != null && i < retainedSourceIndices.Count ? retainedSourceIndices[i] : null;
                RecordDroppedRetainedAt(sourceMap, new FaceKey(retainedStart + i), sources);
            }
        }

        /// <summary>Records <see cref="Provenance.DroppedRetained"/> for output <paramref name="key"/> against
        /// each source in <paramref name="sources"/> (or as fabricated when none is known).</summary>
        private static void RecordDroppedRetainedAt(SourceMap sourceMap, FaceKey key, List<int> sources)
        {
            if (sources != null && sources.Count != 0)
            {
                foreach (int source in sources)
                {
                    sourceMap.Record(source, key, Provenance.DroppedRetained);
                }
            }
            else
            {
                sourceMap.RecordFabricated(key, Provenance.DroppedRetained);
            }
        }

        /// <summary>Decodes <paramref name="face3Ds"/> into a cell complex once to read its cell count/volumes
        /// (the appended-set signature metrics when the consolidation rebuild is off or rejected), plus the
        /// Phase 7a per-cell <paramref name="cells"/> snapshot (index/volume/centre/shell) and the P2
        /// <paramref name="complex"/> product (projected before dispose, sans naked wires) for the same build -
        /// one decode, three outputs, so they always describe the same cell complex.</summary>
        private static List<double> DecodeCellVolumes(List<Face3D> face3Ds, OcctBuildOptions options, System.Guid solveId, out int cellCount, out List<SolverCell> cells, out ResolvedCellComplex complex)
        {
            cellCount = 0;
            cells = new List<SolverCell>();
            complex = null;
            if (face3Ds == null || face3Ds.Count == 0)
            {
                return new List<double>();
            }

            GeometryCreate.Shells(face3Ds, out OcctCellComplexResult result, options);
            try
            {
                cellCount = result?.Cells?.Count ?? 0;
                cells = result?.Cells == null ? new List<SolverCell>() : result.Cells.Select((x, idx) => new SolverCell(idx, x.Volume, x.Center, x.Shell)).ToList();
                complex = result == null ? null : ResolvedCellComplex.Project(result, null, solveId);
                return result?.Cells == null ? new List<double>() : result.Cells.Select(x => x.Volume).ToList();
            }
            finally
            {
                result?.Dispose();
            }
        }

        /// <summary>
        /// Number of input source faces with no surviving representation in the resolved output, driven by
        /// the composed <see cref="SourceMap"/> (a source whose <c>FacesOf</c> is empty is genuinely
        /// unrepresented - docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §B). The geometric backfill
        /// guarantees every OUTPUT face carries a source, so this measures dropped INPUTS, by index.
        /// </summary>
        private int DroppedSourceCount()
        {
            if (face3Ds == null || face3Ds.Count == 0)
            {
                return 0;
            }

            int dropped = 0;
            for (int i = 0; i < face3Ds.Count; i++)
            {
                if (SourceMap == null || SourceMap.FacesOf(i).Count == 0)
                {
                    dropped++;
                }
            }

            return dropped;
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
        /// Counts adopted cells that harbour a dropped room-dividing partition - the under-split gate's
        /// geometric measurement (codex #7, P4). A cell is under-split when some dropped (unrepresented)
        /// input face is: (1) wall-like (its normal is within <see cref="VerticalAngleTolerance"/> of
        /// horizontal, measured relative to the LEVEL - only a wall divides rooms in plan); (2) strictly
        /// INTERIOR to that one cell (its interior point is inside the cell and not merely on its boundary -
        /// a face used AS a separator sits on the boundary and is represented, so is never a dropped face); and
        /// (3) spanning at least <see cref="UNDER_SPLIT_MIN_HEIGHT_RATIO"/> of the cell's height AND
        /// <see cref="UNDER_SPLIT_MIN_PLAN_RATIO"/> of its plan width (a real partition, even one stopping short
        /// of the ceiling - not a short decorative fin or a fragment interior to a large room). Such a face is
        /// a partition the raw build failed to imprint, so the rooms it should have separated merged into one
        /// watertight cell.
        /// <para><b>Vertex-projected measurement (codex #7 review, rounds 2 and 3).</b> Height/plan-width are
        /// measured by projecting the ACTUAL boundary vertices of the face/shell onto a direction (<see cref="Up"/>
        /// for height, the in-level tangent for plan-width) and taking max-min - never a bounding box's extent.
        /// A bounding box (world-axis-aligned, or even one built in a level-tilted frame) is only tight when the
        /// room happens to be aligned with that box's own axes; a room tilted out of the world/level plane OR
        /// merely rotated in plan (yaw, about <see cref="Up"/> itself) inflates any axis-aligned box, which can
        /// silently understate a real partition's height/plan RATIO (the box's own extent grows, while the
        /// partition's does not) and mask a genuine under-split. Direct vertex projection is exact and immune to
        /// both failure modes - and needs no canonical-frame transform at all, since a dot product with a fixed
        /// world direction is frame-invariant by construction.</para>
        /// <para>Deliberately conservative (errs toward NOT rejecting, since a false positive pushes a
        /// well-modelled input onto the weaker managed pipeline): a horizontal cap sliver, a stray face outside
        /// every cell, a boundary-coincident face, and a short fin are all excluded - so atria, courtyard rings
        /// and double-height rooms (none of which contain a dropped full-height interior wall) do not trip it.
        /// Native-free (pure managed Shell/Face3D geometry); the pure decision stays in
        /// <see cref="EvaluateRawAdoption"/>, which just receives this count.</para>
        /// </summary>
        private static int CountUnderSplitCells(List<Face3D> droppedFace3Ds, IReadOnlyList<SolverCell> cells, Vector3D up, double verticalAngleTolerance, double fuzzyTolerance, double tolerance, out List<string> details)
        {
            details = new List<string>();
            if (droppedFace3Ds == null || droppedFace3Ds.Count == 0 || cells == null || cells.Count == 0)
            {
                return 0;
            }

            Vector3D upUnit = (up == null || up.Length <= tolerance) ? new Vector3D(0, 0, 1) : up.Unit;
            double maxVerticalNormalZ = System.Math.Sin(verticalAngleTolerance); // |n.Up| at/below this => wall-like
            HashSet<int> underSplitCells = new HashSet<int>();

            foreach (Face3D dropped in droppedFace3Ds)
            {
                Vector3D normal = dropped?.GetPlane()?.Normal?.Unit;
                if (normal == null || System.Math.Abs(normal.DotProduct(upUnit)) > maxVerticalNormalZ)
                {
                    continue; // only a face vertical relative to the level (wall-like) can be a room divider
                }

                Point3D internalPoint = dropped.GetInternalPoint3D(tolerance);
                if (internalPoint == null)
                {
                    continue;
                }

                // The in-level horizontal tangent along the partition (perpendicular to both Up and the
                // partition normal): the direction a room divider runs. Zero-length only if normal || Up, which
                // the wall-like test above already excluded.
                Vector3D tangent = upUnit.CrossProduct(normal);
                if (tangent.Length <= tolerance)
                {
                    continue;
                }
                Vector3D tangentUnit = tangent.Unit;

                if (!TryVertexExtent(dropped, upUnit, out double faceHeightMin, out double faceHeightMax)
                    || !TryVertexExtent(dropped, tangentUnit, out double facePlanMin, out double facePlanMax))
                {
                    continue;
                }
                double faceHeight = faceHeightMax - faceHeightMin;
                double facePlan = facePlanMax - facePlanMin;

                for (int c = 0; c < cells.Count; c++)
                {
                    if (underSplitCells.Contains(c))
                    {
                        continue; // this cell is already counted
                    }

                    Shell shell = cells[c]?.Shell;
                    if (shell == null)
                    {
                        continue;
                    }

                    // Strictly interior: inside the cell AND not merely on its boundary.
                    if (!shell.Inside(internalPoint, fuzzyTolerance, tolerance) || shell.On(internalPoint, tolerance))
                    {
                        continue;
                    }

                    if (!TryVertexExtent(shell, upUnit, out double cellHeightMin, out double cellHeightMax)
                        || !TryVertexExtent(shell, tangentUnit, out double cellPlanMin, out double cellPlanMax))
                    {
                        continue;
                    }
                    double cellHeight = cellHeightMax - cellHeightMin;
                    double cellPlan = cellPlanMax - cellPlanMin;

                    if (cellHeight <= tolerance || faceHeight < UNDER_SPLIT_MIN_HEIGHT_RATIO * cellHeight)
                    {
                        continue; // a partial-height fin, not a room-height partition
                    }

                    if (cellPlan <= tolerance || facePlan < UNDER_SPLIT_MIN_PLAN_RATIO * cellPlan)
                    {
                        continue; // does not span the cell wall-to-wall - a partial element, not a divider
                    }

                    underSplitCells.Add(c);
                    details.Add(string.Format(
                        "cell {0} (vol {1:0.###} m3, height {2:0.###} m) harbours a dropped partition spanning {3:P0} of its height and {4:P0} of its plan width at ({5:0.##}, {6:0.##}, {7:0.##})",
                        c, cells[c].Volume, cellHeight, faceHeight / cellHeight, facePlan / cellPlan, internalPoint.X, internalPoint.Y, internalPoint.Z));
                    break;
                }
            }

            return underSplitCells.Count;
        }

        /// <summary>The min/max of every boundary vertex of <paramref name="face3D"/> dotted with
        /// <paramref name="direction"/> - the exact support of the face's actual shape along that direction
        /// (never a bounding box's, which is only tight when the shape happens to be axis-aligned to it).</summary>
        private static bool TryVertexExtent(Face3D face3D, Vector3D direction, out double min, out double max)
        {
            min = double.PositiveInfinity;
            max = double.NegativeInfinity;
            List<Point3D> point3Ds = (face3D?.GetExternalEdge3D() as ISegmentable3D)?.GetPoints();
            if (point3Ds == null || point3Ds.Count == 0)
            {
                return false;
            }

            foreach (Point3D point3D in point3Ds)
            {
                double d = (point3D.X * direction.X) + (point3D.Y * direction.Y) + (point3D.Z * direction.Z);
                min = System.Math.Min(min, d);
                max = System.Math.Max(max, d);
            }

            return true;
        }

        /// <summary>The min/max of every boundary vertex across all of <paramref name="shell"/>'s faces dotted
        /// with <paramref name="direction"/> - the cell's true extent along that direction.</summary>
        private static bool TryVertexExtent(Shell shell, Vector3D direction, out double min, out double max)
        {
            min = double.PositiveInfinity;
            max = double.NegativeInfinity;
            bool any = false;
            foreach (Face3D face3D in shell?.Face3Ds ?? new List<Face3D>())
            {
                if (TryVertexExtent(face3D, direction, out double faceMin, out double faceMax))
                {
                    any = true;
                    min = System.Math.Min(min, faceMin);
                    max = System.Math.Max(max, faceMax);
                }
            }

            return any;
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
        public static void ExtendWalls(List<SnappedPanel> panels, double verticalAngleTolerance, double overshoot, double toleranceDistance, List<ExtendRecord> records = null)
        {
            if (panels == null || panels.Count < 2)
            {
                return;
            }

            // Collect the walls and their foot segments (axis + plan footprint) once, up front, so every
            // reach is measured against the original wall lines (deterministic, order-independent).
            List<SnappedPanel> walls = new List<SnappedPanel>();
            List<Segment3D> feet = new List<Segment3D>();
            List<int> wallPanelIndices = new List<int>(); // position in `panels` (= SnappedPanels), for E3 records
            for (int panelIndex = 0; panelIndex < panels.Count; panelIndex++)
            {
                SnappedPanel panel = panels[panelIndex];
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
                wallPanelIndices.Add(panelIndex);
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

                bool changed = walls[i].SetVerticalFootprint(new Geometry.Planar.Point2D(nsX, nsY), new Geometry.Planar.Point2D(neX, neY), toleranceDistance);

                if (records != null && changed)
                {
                    RecordPlanFootprintMoves(records, walls[i], wallPanelIndices[i], oStart, oEnd, ux, uy, length,
                        nsX, nsY, neX, neY, startParam, endParam, feet[i].GetStart().Z, overshoot, toleranceDistance);
                }
            }
        }

        /// <summary>E3 observability for a lateral foot move (<see cref="SnappedPanel.SetVerticalFootprint"/>):
        /// emits one <see cref="ExtendRecord"/> per plan end that actually moved (start and/or end), measured as
        /// the plan parameter along the wall axis relative to the original start. The lateral-capped flag is set
        /// when an EXTENDED end reached the wall's extension cap <c>min(MaxExtend, 0.49 * length)</c> (the same
        /// cap the 2D <c>ExtensionSolver</c> enforces); a trimmed (inward) end is never capped and carries no
        /// overshoot. Recording only.</summary>
        private static void RecordPlanFootprintMoves(
            List<ExtendRecord> records, SnappedPanel wall, int panelIndex,
            Geometry.Planar.Point2D oStart, Geometry.Planar.Point2D oEnd,
            double ux, double uy, double length,
            double nsX, double nsY, double neX, double neY,
            double startParam, double endParam, double baseZ,
            double overshoot, double toleranceDistance)
        {
            int sourceIndex = RepresentativeSource(wall);
            double cap = System.Math.Min(System.Math.Max(0, wall.MaxExtension), length * EXTENSION_LIMIT_LENGTH_RATIO);

            // Applied plan parameters (post-overshoot), relative to the original start along the wall axis.
            double nsParam = (nsX - oStart.X) * ux + (nsY - oStart.Y) * uy;
            double neParam = (neX - oStart.X) * ux + (neY - oStart.Y) * uy;

            // START end (original plan parameter 0). Extended when the resolved start moved past the original.
            if (System.Math.Abs(nsParam) > toleranceDistance)
            {
                bool extended = startParam < -toleranceDistance;
                double extensionDistance = extended ? -startParam : 0;
                records.Add(new ExtendRecord(
                    panelIndex, sourceIndex, ExtendOperationKind.PlanStart,
                    0, nsParam, "plan",
                    new Point3D(oStart.X, oStart.Y, baseZ), new Point3D(nsX, nsY, baseZ),
                    -1, -1, "walls", "2D plan-loop junction",
                    extended ? overshoot : 0,
                    extended && cap > toleranceDistance && extensionDistance >= cap - toleranceDistance));
            }

            // END end (original plan parameter == length).
            if (System.Math.Abs(neParam - length) > toleranceDistance)
            {
                bool extended = endParam > length + toleranceDistance;
                double extensionDistance = extended ? endParam - length : 0;
                records.Add(new ExtendRecord(
                    panelIndex, sourceIndex, ExtendOperationKind.PlanEnd,
                    length, neParam, "plan",
                    new Point3D(oEnd.X, oEnd.Y, baseZ), new Point3D(neX, neY, baseZ),
                    -1, -1, "walls", "2D plan-loop junction",
                    extended ? overshoot : 0,
                    extended && cap > toleranceDistance && extensionDistance >= cap - toleranceDistance));
            }
        }

        /// <summary>The panel's representative source-face index (its first <see cref="SnappedPanel.SourceIndices"/>)
        /// for an <see cref="ExtendRecord"/>; -1 when it carries none. On the managed path this is the CLEAN-face
        /// ordinal (see <see cref="Register"/>); <see cref="RemapExtendRecordSourceIndices"/> converts it to the
        /// original input source index once conditioning is done.</summary>
        private static int RepresentativeSource(SnappedPanel panel)
        {
            List<int> sourceIndices = panel?.SourceIndices;
            return sourceIndices != null && sourceIndices.Count > 0 ? sourceIndices[0] : -1;
        }

        /// <summary>Remaps every record's <see cref="ExtendRecord.SourceIndex"/>/<see cref="ExtendRecord.TargetSourceIndex"/>
        /// from the CLEAN-face ordinal it was emitted with to the ORIGINAL input source index, via the snap
        /// stage's per-clean-face attribution (<c>SnapStage.Result.SourceIndicesPerFace</c>, index-aligned to the
        /// clean faces the panels were registered from). A clean face is dropped/merged from one or more input
        /// sources; the representative is the first. Leaves an index untouched when there is no attribution to
        /// map it through (keeps -1 as -1). Recording-only.</summary>
        private static void RemapExtendRecordSourceIndices(List<ExtendRecord> records, List<List<int>> sourceIndicesPerFace)
        {
            if (records == null)
            {
                return;
            }

            foreach (ExtendRecord record in records)
            {
                if (record == null)
                {
                    continue;
                }

                record.SourceIndex = MapCleanOrdinalToSource(record.SourceIndex, sourceIndicesPerFace);
                record.TargetSourceIndex = MapCleanOrdinalToSource(record.TargetSourceIndex, sourceIndicesPerFace);
            }
        }

        /// <summary>The representative original input source index for a clean-face ordinal (first of its
        /// <c>SourceIndicesPerFace</c> entry); -1 when the ordinal is -1, out of range, or has no recorded
        /// source.</summary>
        private static int MapCleanOrdinalToSource(int cleanOrdinal, List<List<int>> sourceIndicesPerFace)
        {
            if (cleanOrdinal < 0 || sourceIndicesPerFace == null || cleanOrdinal >= sourceIndicesPerFace.Count)
            {
                return -1;
            }

            List<int> sources = sourceIndicesPerFace[cleanOrdinal];
            return sources != null && sources.Count > 0 ? sources[0] : -1;
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
        /// Managed extend: grow each (vertical) wall up to the nearest cap - the floor or roof that sits above
        /// it and covers it in plan - so the native resolve can trim the wall against that surface and close the
        /// volume. E2 (docs/EXTEND3D_ROBUST_HANDOVER.md): the SELECTION is the pre-E2 nearest-cap-over-the-wall
        /// -centre rule (so rigidly-tilted and flat levels stay byte-identical), but the TARGET is now the
        /// cap's real surface. A cap that is flat relative to the wall (a level floor/ceiling, incl. a tilted
        /// level) still uses the scalar extend to the cap elevation + overshoot; only a cap genuinely PITCHED
        /// relative to the wall (a real sloped roof over a vertical wall) is followed as a sloped plane, so the
        /// wall gains a matching sloped top instead of a flat one at the ridge height - the sloped-roof models
        /// E2 targets. Walls with no cap above (true parapets/outer tops) are left untouched. Vertical reach
        /// stays MaxExtension-UNCAPPED (as before E2 - MaxExtension governs the lateral wall-to-wall reach only;
        /// changing that is out of E2 scope).
        /// </summary>
        public static void Extend(List<SnappedPanel> panels, double verticalAngleTolerance, double overshoot, double toleranceDistance, double roofOvershoot = 0.5, bool includeRoofs = true, List<ExtendRecord> records = null)
        {
            if (panels == null || panels.Count < 2)
            {
                return;
            }

            List<SnappedPanel> walls = new List<SnappedPanel>();
            List<int> wallPanelIndices = new List<int>();      // position in `panels` per wall, for E3 records
            List<BoundingBox3D> capBoxes = new List<BoundingBox3D>();
            List<Plane> capPlanes = new List<Plane>();
            List<int> capPanelIndices = new List<int>();        // position in `panels` per cap, for E3 records
            List<int> capSourceIndices = new List<int>();       // representative source per cap, for E3 records
            for (int panelIndex = 0; panelIndex < panels.Count; panelIndex++)
            {
                SnappedPanel panel = panels[panelIndex];
                BoundingBox3D boundingBox3D = panel.GetBoundingBox();
                if (boundingBox3D == null)
                {
                    continue;
                }

                if (panel.IsVertical(verticalAngleTolerance))
                {
                    walls.Add(panel);
                    wallPanelIndices.Add(panelIndex);
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
                    capPanelIndices.Add(panelIndex);
                    capSourceIndices.Add(RepresentativeSource(panel));
                }
            }

            for (int w = 0; w < walls.Count; w++)
            {
                SnappedPanel wall = walls[w];
                BoundingBox3D wallBox = wall.GetBoundingBox();
                if (wallBox == null)
                {
                    continue;
                }

                ExtendWallToNearestCap(wall, wallBox, capBoxes, capPlanes, overshoot, roofOvershoot, toleranceDistance, true, records, wallPanelIndices[w], capPanelIndices, capSourceIndices);
                ExtendWallToNearestCap(wall, wallBox, capBoxes, capPlanes, overshoot, roofOvershoot, toleranceDistance, false, records, wallPanelIndices[w], capPanelIndices, capSourceIndices);
            }
        }

        /// <summary>
        /// Extends one wall in one direction (<paramref name="up"/> = to a roof/ceiling above; false = to a
        /// floor below) to the single nearest covering cap over the wall centre. Cap SELECTION is the pre-E2
        /// rule verbatim (nearest cap whose surface over the wall centre clears the wall extreme, whole-wall
        /// plan overlap), so the accepted managed baselines do not move on the fixtures E1 already conditioned.
        /// <para>What E2 changes is the TARGET: a cap that is flat RELATIVE TO THIS WALL (its normal aligned
        /// with the wall's own up-axis - a level floor/ceiling, including a rigidly TILTED level where wall and
        /// slab tilt together) takes the pre-E2 scalar extend to the cap's world extreme + overshoot
        /// (byte-identical - a level cap has a single target elevation). Only a cap genuinely PITCHED relative
        /// to the wall (a real sloped roof over a vertical wall - the case E2 exists for) takes the sloped plane
        /// target, so the wall gains a matching sloped top instead of a flat one at the ridge.</para>
        /// <para>A three-sample / multi-cap covering test (feet + centre, extend to every covering plane) was
        /// implemented and REJECTED: on the rigidly-tilted golden fixtures it selected extra/farther caps in
        /// world-Z and collapsed the managed solve (docs/EXTEND3D_ROBUST_HANDOVER.md E2 review notes). A wall
        /// spanning two roof planes still receives its correct multi-slope top from the native kernel, which
        /// trims it against every roof face in the cell complex.</para>
        /// </summary>
        private static void ExtendWallToNearestCap(SnappedPanel wall, BoundingBox3D wallBox, List<BoundingBox3D> capBoxes, List<Plane> capPlanes, double overshoot, double roofOvershoot, double toleranceDistance, bool up, List<ExtendRecord> records = null, int wallPanelIndex = -1, List<int> capPanelIndices = null, List<int> capSourceIndices = null)
        {
            double wallExtreme = up ? wallBox.Max.Z : wallBox.Min.Z;
            double centreX = 0.5 * (wallBox.Min.X + wallBox.Max.X);
            double centreY = 0.5 * (wallBox.Min.Y + wallBox.Max.Y);

            int capIndex = NearestCoveringCap(wallBox, centreX, centreY, capBoxes, capPlanes, toleranceDistance, up, wallExtreme);
            if (capIndex < 0)
            {
                return;
            }

            Plane capPlane = capPlanes[capIndex];
            BoundingBox3D capBox = capBoxes[capIndex];
            if (capPlane == null || capBox == null)
            {
                return;
            }

            // Overshoot selection: a thin (flat) cap keeps the small wall overshoot; a thick (sloped/roof)
            // cap on the SCALAR path keeps the larger roof overshoot - E1's flat-at-ridge target needs the
            // wall to clear the whole pitch from below. The PLANE target follows the surface itself, so it
            // needs only the small overshoot to guarantee the kernel intersection (E2 review: the large
            // roof overshoot applied to a surface-following target pierces 0.5 m PAST the roof everywhere,
            // shredding adjacent geometry into naked fragments on the real-export fixtures).
            bool capIsRoof = capBox.Max.Z - capBox.Min.Z > toleranceDistance + 0.1;
            double scalarOvershoot = capIsRoof ? roofOvershoot : overshoot;

            // E3: snapshot the wall's real extreme BEFORE the mutation so the record measures the actual move
            // (the passed wallBox is captured once per wall for cap SELECTION and is stale after the first of
            // the two up/down calls - the fresh box here is the honest from-value).
            BoundingBox3D preBox = records != null ? wall.GetBoundingBox() : null;

            bool flatRelative = IsCapFlatRelativeToWall(wall, capPlane);
            if (up)
            {
                if (flatRelative)
                {
                    wall.ExtendTopTo(capBox.Max.Z + scalarOvershoot, toleranceDistance);
                }
                else
                {
                    wall.ExtendTopToPlane(capPlane, capBox.Max.Z, overshoot, toleranceDistance);
                }
            }
            else
            {
                if (flatRelative)
                {
                    wall.ExtendBottomTo(capBox.Min.Z - scalarOvershoot, toleranceDistance);
                }
                else
                {
                    wall.ExtendBottomToPlane(capPlane, capBox.Min.Z, overshoot, toleranceDistance);
                }
            }

            if (records != null && preBox != null)
            {
                RecordCapExtend(records, wall, wallPanelIndex, preBox, up, capIndex, capBox, capPlane, flatRelative,
                    flatRelative ? scalarOvershoot : overshoot, capPanelIndices, capSourceIndices, toleranceDistance);
            }
        }

        /// <summary>E3 observability for a vertical cap extend (<see cref="SnappedPanel.ExtendTopTo"/> /
        /// <c>ExtendTopToPlane</c> and their bottom mirrors): emits one <see cref="ExtendRecord"/> when the
        /// wall's top/base actually moved, recording the elevation delta at the wall centre, the target cap
        /// (its panel index) and whether the scalar (E1 flat-Z) or the sloped-plane (E2) branch was taken.
        /// Vertical reach is MaxExtend-UNCAPPED by policy, so the lateral-capped flag is always false. Recording
        /// only.</summary>
        private static void RecordCapExtend(
            List<ExtendRecord> records, SnappedPanel wall, int wallPanelIndex, BoundingBox3D preBox, bool up,
            int capIndex, BoundingBox3D capBox, Plane capPlane, bool flatRelative, double overshoot,
            List<int> capPanelIndices, List<int> capSourceIndices, double toleranceDistance)
        {
            BoundingBox3D postBox = wall.GetBoundingBox();
            if (postBox == null)
            {
                return;
            }

            double fromValue = up ? preBox.Max.Z : preBox.Min.Z;
            double toValue = up ? postBox.Max.Z : postBox.Min.Z;
            if (System.Math.Abs(toValue - fromValue) <= toleranceDistance)
            {
                return; // no-op: the wall already reached the cap, or the (clamped) target did not clear it
            }

            double centreX = 0.5 * (preBox.Min.X + preBox.Max.X);
            double centreY = 0.5 * (preBox.Min.Y + preBox.Max.Y);
            double capExtremeZ = up ? capBox.Max.Z : capBox.Min.Z;

            int targetPanelIndex = capPanelIndices != null && capIndex >= 0 && capIndex < capPanelIndices.Count ? capPanelIndices[capIndex] : -1;
            int targetSourceIndex = capSourceIndices != null && capIndex >= 0 && capIndex < capSourceIndices.Count ? capSourceIndices[capIndex] : -1;

            string targetDescription;
            if (flatRelative)
            {
                targetDescription = string.Format("cap z={0:0.###}", capExtremeZ);
            }
            else
            {
                Vector3D n = capPlane.Normal?.Unit;
                targetDescription = n == null
                    ? string.Format("cap z={0:0.###}", capExtremeZ)
                    : string.Format("cap plane n=({0:0.##},{1:0.##},{2:0.##}) z={3:0.###}", n.X, n.Y, n.Z, capExtremeZ);
            }

            records.Add(new ExtendRecord(
                wallPanelIndex, RepresentativeSource(wall),
                up ? ExtendOperationKind.Top : ExtendOperationKind.Bottom,
                fromValue, toValue, flatRelative ? "elevation" : "distance-to-plane",
                new Point3D(centreX, centreY, fromValue), new Point3D(centreX, centreY, toValue),
                targetPanelIndex, targetSourceIndex, flatRelative ? "cap-scalar" : "cap-plane", targetDescription,
                overshoot, false));
        }

        /// <summary>
        /// True when <paramref name="capPlane"/> is a level floor/ceiling FOR THIS WALL - its normal aligned
        /// (within <see cref="CapFlatnessConeTolerance"/>) with the wall's own in-plane up-axis (world Z
        /// projected onto the wall plane). A rigidly tilted level (wall and slab tilted together by the same
        /// frame angle) is flat in this sense - the slab has one target elevation over the wall, so the pre-E2
        /// scalar extend is correct and byte-identical. Only a cap genuinely pitched relative to the wall (a
        /// sloped roof over a vertical wall) is NOT flat-relative and takes the E2 sloped plane target. Safe
        /// default (true = scalar) when either normal is unavailable or the wall is degenerate.
        /// </summary>
        private static bool IsCapFlatRelativeToWall(SnappedPanel wall, Plane capPlane)
        {
            Vector3D wallNormal = wall?.Plane?.Normal?.Unit;
            Vector3D capNormal = capPlane?.Normal?.Unit;
            if (wallNormal == null || capNormal == null)
            {
                return true;
            }

            // The wall's in-plane up-axis: world +Z with its wall-normal component removed. Zero only for a
            // (near) horizontal wall, which is not a wall - fall back to scalar.
            Vector3D up = new Vector3D(0, 0, 1) - (wallNormal * (wallNormal.Z));
            if (up.Length <= 1e-6)
            {
                return true;
            }

            return System.Math.Abs(capNormal.DotProduct(up.Unit)) >= System.Math.Cos(CapFlatnessConeTolerance);
        }

        /// <summary>Half-angle cone (radians) within which a cap normal counts as aligned with the wall's
        /// up-axis - i.e. a level floor/ceiling for that wall rather than a pitched roof. 15 degrees: comfortably
        /// admits rigidly tilted levels (the golden fixtures tilt ~13 degrees, plus modelling noise about the
        /// frame) while a real roof pitch (typically &gt;=15-20 degrees) reads as pitched and takes the sloped
        /// plane target.</summary>
        private const double CapFlatnessConeTolerance = 15.0 * (System.Math.PI / 180.0);

        /// <summary>The nearest cap over plan point (<paramref name="x"/>, <paramref name="y"/>) whose surface
        /// is beyond the wall extreme in the grow direction (<paramref name="up"/> = above the wall top; false
        /// = below the base), or -1 if none. Whole-wall plan overlap (the pre-E2 rule), so a slightly-inset cap
        /// is still found; the surface is read from the cap PLANE at the point (sloped roofs evaluate correctly,
        /// not by bounding-box Z).</summary>
        private static int NearestCoveringCap(BoundingBox3D wallBox, double x, double y, List<BoundingBox3D> capBoxes, List<Plane> capPlanes, double toleranceDistance, bool up, double wallExtreme)
        {
            int best = -1;
            double bestZ = up ? double.MaxValue : double.MinValue;
            for (int i = 0; i < capBoxes.Count; i++)
            {
                if (!OverlapsInPlan(capBoxes[i], wallBox, toleranceDistance))
                {
                    continue;
                }

                double capZ = CapZAtPlan(capPlanes[i], x, y, up ? capBoxes[i].Max.Z : capBoxes[i].Min.Z);
                if (up ? capZ < wallExtreme - toleranceDistance : capZ > wallExtreme + toleranceDistance)
                {
                    continue; // the cap surface here is beyond the wall extreme on the WRONG side - not a cap
                }

                if (up ? capZ < bestZ : capZ > bestZ)
                {
                    bestZ = capZ;
                    best = i;
                }
            }

            return best;
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
        public static void Fill(List<SnappedPanel> panels, double verticalAngleTolerance, double margin, double toleranceDistance, double overshoot = 0.05, List<ExtendRecord> records = null)
        {
            if (panels == null || panels.Count == 0 || margin <= toleranceDistance)
            {
                return;
            }

            // The walls each cap grows toward. Measuring the gap to these (rather than blindly offsetting by
            // the full margin) lets a cap reach exactly the walls it is short of and no further - the kernel
            // trims the small overshoot. A cap with no wall in reach falls back to the fixed-margin grow.
            List<SnappedPanel> walls = panels.Where(x => x.IsVertical(verticalAngleTolerance)).ToList();

            for (int panelIndex = 0; panelIndex < panels.Count; panelIndex++)
            {
                SnappedPanel panel = panels[panelIndex];
                if (panel.IsVertical(verticalAngleTolerance)) // floors and roofs are the caps
                {
                    continue;
                }

                double areaBefore = records != null ? panel.GetArea() : 0;

                bool measured = panel.GrowOutwardTo(walls, margin, overshoot, toleranceDistance);
                if (!measured)
                {
                    panel.GrowOutward(margin, toleranceDistance);
                }

                // E3: record the cap grow (area before -> after). A cap grow is an in-plane offset of the whole
                // boundary - no single moved edge - so it carries no preview segment (null From/To); the measured
                // grow tells the reviewer which caps reached their walls (measured) vs fell back to fixed-margin.
                if (records != null)
                {
                    double areaAfter = panel.GetArea();
                    if (areaAfter > areaBefore + toleranceDistance)
                    {
                        records.Add(new ExtendRecord(
                            panelIndex, RepresentativeSource(panel), ExtendOperationKind.CapGrow,
                            areaBefore, areaAfter, "area",
                            null, null,
                            -1, -1, measured ? "walls-measured" : "fixed-margin", string.Empty,
                            overshoot, false));
                    }
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

        /// <summary>
        /// Frame-aware cap normalization (Phase 6c): snaps each level's caps onto that level's own datum plane,
        /// grouped by <see cref="LevelFrame"/> membership instead of the flat <see cref="NormalizeCapOffset"/>
        /// band. This is the same per-group backer snap as the legacy
        /// <see cref="NormalizeCaps(List{SnappedPanel}, double, double, double, double)"/> - within a frame the
        /// largest-area cap is the backer and the rest are projected onto its plane - but the group is the level
        /// frame (a ~0.15 m elevation band, <see cref="LevelFrame.DEFAULT_ElevationBand"/>), never the 0.3 m
        /// <see cref="NormalizeCapOffset"/> that would swallow a split-level landing. Because caps never cross a
        /// frame, a landing ~0.25 m above the floor is its OWN frame and keeps its own elevation - the split-level
        /// landing the plan (§E Phase 6, Risk 5) requires be preserved. Cap membership is decided frame-aware
        /// (<see cref="LevelFrame.ClassifyFace"/>), so a wall on a tilted level is never mistaken for a cap.
        /// A frame with a single cap is a no-op. When <paramref name="frames"/> is null/empty the caller falls
        /// back to the legacy world-frame overload.
        /// </summary>
        public static void NormalizeCaps(List<SnappedPanel> panels, IReadOnlyList<LevelFrame> frames, double toleranceAngle, double toleranceDistance, double verticalAngleTolerance = 20 * (System.Math.PI / 180), SolverDiagnostics diagnostics = null)
        {
            if (panels == null || panels.Count < 2 || frames == null || frames.Count == 0)
            {
                return;
            }

            // Bucket the cap panels by the level frame they belong to. ClassifyFace is frame-aware, so a wall on a
            // >20°-tilted level classifies as a wall (not a cap) and is skipped - the world-frame ceiling the
            // legacy overload's IsVertical carries. `diagnostics` is threaded through to ClassifyFace so an
            // ambiguous cap-to-frame assignment (AmbiguousLevelFrame) is reported rather than silently resolved;
            // the per-frame grouping summary below (FrameNormalization) is the routine, non-spammy report.
            Dictionary<int, List<SnappedPanel>> capsByFrame = new Dictionary<int, List<SnappedPanel>>();
            foreach (SnappedPanel panel in panels)
            {
                if (panel?.Plane == null || panel.Face3D == null || !panel.Face3D.IsValid())
                {
                    continue;
                }

                FaceRole role = LevelFrame.ClassifyFace(panel.Face3D, frames, out int frameIndex, verticalAngleTolerance, diagnostics);
                if (role != FaceRole.Cap || frameIndex < 0)
                {
                    continue;
                }

                if (!capsByFrame.TryGetValue(frameIndex, out List<SnappedPanel> bucket))
                {
                    bucket = new List<SnappedPanel>();
                    capsByFrame[frameIndex] = bucket;
                }

                bucket.Add(panel);
            }

            diagnostics?.Add(SolverStage.Snap, DiagnosticCode.FrameNormalization, OcctDiagnosticSeverity.Info,
                string.Format("Frame-aware cap normalization: {0} level frame(s) formed from {1} level frame(s) supplied, {2} cap(s) classified onto a frame.", capsByFrame.Count, frames.Count, capsByFrame.Values.Sum(x => x.Count)));

            // Within each frame, snap every member cap onto the largest-area member's plane (the backer/datum).
            // Identical to the legacy per-group backer snap - only the grouping is by frame, not the flat band.
            foreach (KeyValuePair<int, List<SnappedPanel>> entry in capsByFrame.OrderBy(x => x.Key))
            {
                List<SnappedPanel> bucket = entry.Value;
                if (bucket.Count < 2)
                {
                    diagnostics?.Add(SolverStage.Snap, DiagnosticCode.FrameNormalization, OcctDiagnosticSeverity.Info,
                        string.Format("Level frame {0}: 1 cap - nothing to normalize onto.", entry.Key));
                    continue;
                }

                SnappedPanel backer = bucket.OrderByDescending(x => x.GetArea()).First();
                diagnostics?.Add(SolverStage.Snap, DiagnosticCode.FrameNormalization, OcctDiagnosticSeverity.Info,
                    string.Format("Level frame {0}: {1} cap(s) normalized onto the largest-area cap's datum plane.", entry.Key, bucket.Count));

                foreach (SnappedPanel candidate in bucket)
                {
                    if (ReferenceEquals(candidate, backer))
                    {
                        continue;
                    }

                    if (backer.IsParallelWith(candidate, toleranceAngle))
                    {
                        candidate.SnapToBacker(backer.Plane);
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
            // Phase 7a: the same per-cell snapshot (index/volume/centre/shell), captured alongside cellVolumes
            // so it reflects exactly the cells the adoption gate below measures.
            List<SolverCell> solverCells = result.Cells == null ? new List<SolverCell>() : result.Cells.Select((x, idx) => new SolverCell(idx, x.Volume, x.Center, x.Shell)).ToList();
            List<Face3D> resolved = shells == null
                ? new List<Face3D>()
                : shells.Where(x => x != null).SelectMany(x => x.Face3Ds ?? new List<Face3D>()).Where(x => x != null && x.IsValid()).ToList();
            // P2: project the adopted complex from THIS decode before Dispose (raw is adopted only when
            // watertight, so no naked wires). Only published as ResolvedCellComplex if the gate below adopts.
            ResolvedCellComplex rawComplex = ResolvedCellComplex.Project(result, null, SolveId);
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
            List<int> droppedSourceIndices = new List<int>();
            double droppedRatio = 0;
            int underSplitCellCount = 0;
            List<string> underSplitDetails = new List<string>();

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
                for (int idx = 0; idx < rawFace3Ds.Count; idx++)
                {
                    Face3D rawFace3D = rawFace3Ds[idx];
                    if (rawFace3D != null && rawFace3D.IsValid() && !IsRepresented(rawFace3D, resolved))
                    {
                        droppedFace3Ds.Add(rawFace3D);
                        droppedSourceIndices.Add(idx); // the exact input source this dropped face is (Phase 5c provenance)
                    }
                }

                droppedRatio = rawFace3Ds.Count == 0 ? 0 : (double)droppedFace3Ds.Count / rawFace3Ds.Count;

                // Codex #7: the finer "watertight-but-wrong" net the dropped-RATIO check misses - a dropped
                // wall-like face sitting strictly inside an adopted cell is a partition that failed to split its
                // room, so two rooms merged into one cell (droppedRatio stays low because only one face dropped).
                underSplitCellCount = CountUnderSplitCells(droppedFace3Ds, solverCells, Up, VerticalAngleTolerance, rawOptions.FuzzyTolerance, rawOptions.Tolerance, out underSplitDetails);
            }

            RawAdoptionOutcome outcome = EvaluateRawAdoption(cells, resolved.Count, nakedEdgeCount, sliverCellCount, droppedRatio, MaxDroppedRatio, underSplitCellCount);
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

                case RawAdoptionOutcome.RejectedUnderSplit:
                    Diagnostics.Add(SolverStage.Resolve, DiagnosticCode.UnderSplit, OcctDiagnosticSeverity.Warning,
                        string.Format("Raw (L0) resolve under-split: {0} adopted cell(s) harbour a dropped room-dividing partition (a watertight-but-wrong merge the {1:P0}-max dropped-ratio check did not catch at {2:P0}); not adopted. {3}",
                            underSplitCellCount, MaxDroppedRatio, droppedRatio, string.Join("; ", underSplitDetails)));
                    return false;

                case RawAdoptionOutcome.RejectedNoCells:
                    return false; // unreachable here (already returned above); kept for switch exhaustiveness
            }

            // Re-add the dropped faces (RetainDropped contract) - same faces the gate above already measured,
            // so this does not re-run IsRepresented. On the raw path the candidates ARE the clean raw input
            // faces, so this is filter-only (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §H): the face
            // SET is unchanged from before Phase 5c, keeping the raw closure signature byte-identical.
            int retainedStart = resolved.Count; // the index the first re-added face lands at (0 retained => no-op below)
            if (RetainDropped && droppedFace3Ds.Count != 0)
            {
                resolved = resolved.Concat(droppedFace3Ds).ToList();
            }

            ResolvedFace3Ds = resolved;
            NativeResolved = true;
            ResolvedCellCount = cells;
            Cells = solverCells;
            NakedEdgePoint3Ds = new List<Point3D>();

            // Coarse source mapping over the adopted raw output (Phase 2, managed-only stand-in for the native
            // history composed in Phase 3): every output face is attributed to the raw input face(s) it derives
            // from, so no output face is source-orphaned.
            SourceMap = BuildResolvedSourceMap(resolved, rawFace3Ds);

            // Phase 5c: mark each re-added face DroppedRetained against its EXACT input source and emit a
            // DroppedFace Info diagnostic, so a retained face is identifiable downstream (and keeps its source
            // Guid through reconstruction, P4). Provenance/diagnostics only - the face set, geometry, cell and
            // naked counts are untouched, so the raw golden-master signature stays byte-identical.
            if (RetainDropped && droppedFace3Ds.Count != 0)
            {
                for (int i = 0; i < droppedFace3Ds.Count; i++)
                {
                    SourceMap.Record(droppedSourceIndices[i], new FaceKey(retainedStart + i), Provenance.DroppedRetained);
                    Diagnostics.Add(SolverStage.Heal, DiagnosticCode.DroppedFace, OcctDiagnosticSeverity.Info,
                        string.Format("RetainDropped (raw): re-added clean input geometry for dropped source {0}.", droppedSourceIndices[i]));
                }
            }

            RawAttemptSignature = new ClosureSignature3D(cells, cellVolumes, nakedEdgeCount: 0, faceCount: resolved.Count, droppedCount: droppedFace3Ds.Count);
            Signature = RawAttemptSignature;
            ResolvedCellComplex = rawComplex; // adopted: publish the complex captured from this decode
            Diagnostics.Add(SolverStage.Resolve, DiagnosticCode.AdoptedLevel, OcctDiagnosticSeverity.Info,
                string.Format("Adopted raw (L0): {0}", Signature));

            return true;
        }

        /// <summary>
        /// The pre-P4 six-argument gate signature, preserved for binary compatibility: a downstream binary
        /// compiled against it keeps resolving this exact overload (no <see cref="System.MissingMethodException"/>
        /// after a DLL swap). Delegates with no under-split cells - i.e. the pre-P4 behaviour exactly.
        /// </summary>
        public static RawAdoptionOutcome EvaluateRawAdoption(int cellCount, int resolvedFaceCount, int nakedEdgeCount, int sliverCellCount, double droppedRatio, double maxDroppedRatio)
        {
            return EvaluateRawAdoption(cellCount, resolvedFaceCount, nakedEdgeCount, sliverCellCount, droppedRatio, maxDroppedRatio, 0);
        }

        /// <summary>
        /// The raw-first (L0) adoption gate's decision rule, pure and native-free so it is unit-testable
        /// without a kernel: given what a raw resolve measured, decides whether it is trusted as-is or the
        /// managed pipeline should run instead. Checked in this order - no cells formed, a gappy envelope
        /// (naked edges), then the two "watertight-but-wrong" cases a naked-edge check alone cannot see (a
        /// sliver artifact cell, or too many input faces silently dropped because they bound no closed cell) -
        /// each closes a distinct failure mode found on real fixtures
        /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §C, Phase 1). <paramref name="underSplitCellCount"/>
        /// (codex #7, P4) is the count of adopted cells found to harbour a dropped room-dividing partition -
        /// the caller computes it geometrically (<see cref="CountUnderSplitCells"/>) and passes it here so this
        /// rule stays pure and unit-testable across every branch.
        /// </summary>
        public static RawAdoptionOutcome EvaluateRawAdoption(int cellCount, int resolvedFaceCount, int nakedEdgeCount, int sliverCellCount, double droppedRatio, double maxDroppedRatio, int underSplitCellCount)
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

            // The finer net after the coarse dropped-RATIO check: even when only a few faces are dropped (ratio
            // under the max), a single dropped partition sitting strictly inside a cell means rooms merged.
            if (underSplitCellCount > 0)
            {
                return RawAdoptionOutcome.RejectedUnderSplit;
            }

            return RawAdoptionOutcome.Adopted;
        }

        /// <summary>
        /// The consolidation-rebuild acceptance rule (codex #3, P4), pure and native-free so it is unit-testable:
        /// the direct (history-capturing) rebuild of the appended set is adopted only when it does NOT regress
        /// versus the appended-unimprinted fallback it would replace - it produced faces, kept AT LEAST as many
        /// cells (a rebuild that DISSOLVED a room-dividing separator would form fewer, silently merging rooms),
        /// and did not open new naked edges. The baseline is the appended set's OWN decoded cell count
        /// (<paramref name="appendedCellCount"/>), never the pre-append resolve count - which, being measured
        /// before patches/retained were added, was typically lower and let a separator-dissolving rebuild through.
        /// </summary>
        public static bool AcceptConsolidationRebuild(int rebuiltFaceCount, int rebuiltCellCount, int rebuiltNakedEdgeCount, int appendedCellCount, int appendedNakedEdgeCount)
        {
            return rebuiltFaceCount != 0 && rebuiltCellCount >= appendedCellCount && rebuiltNakedEdgeCount <= appendedNakedEdgeCount;
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
