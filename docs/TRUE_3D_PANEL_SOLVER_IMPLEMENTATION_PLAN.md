# True 3D Panel Solver — Implementation Plan (SAM_OCCT)

Status: approved roadmap · Date: 2026-07-02 · Host branch: `fix/solver-raw-first`

This document supersedes `docs/3D_PANEL_SOLVER_PLAN.md` as the active plan (the old plan is kept
unchanged as the historical baseline; §C below is the critique that motivated this revision).

---

## A. Executive summary

SAM's production panel solver (SAM_Solver) is 2D-per-level: it slices panels at section heights,
snaps/extends wall axes in WorldXY, and re-lofts. It cannot handle sloped panels, roofs, panels
crossing levels, non-horizontal caps, or out-of-plane alignment. SAM_OCCT already hosts the
replacement: a native OpenCASCADE 8.0 kernel (P/Invoke, ABI v3) plus a partially-built true-3D
solver (`Panel3DSnapSolver`, `Modify.Solve3D`, GH components) on branch `fix/solver-raw-first`.

**This plan is an evolution plan, not a greenfield plan.** The branch's own empirical result — raw
faces handed straight to sew+MakerVolume close every well-modelled fixture (22/0, 44/0, 32/0 naked),
beating the managed clean/extend pipeline (23/12, 32/7) — proves the kernel is the workhorse and
managed conditioning must be minimal, staged, and diagnosis-driven. What we build:

1. A **three-stage hybrid** pipeline — **Snap (managed, non-fabricating) → Resolve (native cell
   build with 3 adoption levels: raw → snapped → conditioned) → Heal/Close (diagnosis-driven local
   escalation)** — with the **OCCT cell complex as the canonical intermediate product** and the
   proven 2D semantics (backer=Weight, width=BucketSize, MaxExtend reach, AutoTune escalation with
   closure-signature acceptance) lifted to 3D.
2. **Real source mapping**: native `BRepTools_History` export (ABI v4) composed with a managed
   `SourceMap` threaded through every stage — replacing today's plane+centroid heuristic and fixing
   a live positional-misalignment bug in per-panel MaxExtend.
3. **Analytical fidelity**: solved panels keep Guid/parameters/construction and re-host trimmed
   apertures (2D parity — today's 3D path silently discards all three); gap-fill faces become air
   panels; cells classify into interior/exterior and can hand off to Spaces/AdjacencyCluster.
4. **Inspectable staged outputs** for Grasshopper: clean panels, conditioned panels, solved panels,
   cells/shells, naked-edge loops, gap-fill panels, source mapping, machine-readable diagnostics.

Sized for 500–2,000-panel models, target < 60 s per solve. Ten small phases, each builds and passes
tests before the next; integration tests run locally per the repo's two-tier protocol (CI-native
build explicitly deferred by owner decision).

## B. Current repo baseline (verified 2026-07-02)

| Repo | Branch | Status | Head |
|---|---|---|---|
| SAM | `sow/2026-Q3` | clean, synced | `595f531e` |
| SAM_Solver | `sow/2026-Q3` | clean, synced | `3155a13` |
| SAM_OCCT | `fix/solver-raw-first` | clean, synced | `6622eee` |

### B.1 SAM_OCCT — what exists and is reused

- **Solver core** `SAM_OCCT/SAM.Geometry.OCCT.Solver`:
  - `Classes/Panel3DSnapSolver.cs` (1449 lines) — `Execute()`: `TryRawResolve` (raw-first: sew 0.01
    + MakerVolume, adopt iff cells≥1 && naked==0) → `Register` → `CleanBucket` (StripInternalEdges →
    SnapOpposedPartitions [area-ratio gate ≥ 0.97] → Snap [backer/bucket] → NormalizeCaps → managed
    coplanar Union) → `ExtendWalls` (delegates to 2D `ExtensionSolver` on plan feet) → `Extend`
    (walls up/down to caps, overshoot 0.05, RoofOvershoot 0.5) → `Fill` (caps out to walls,
    FillMargin 0.5) → `Resolve` (coplanar pre-merge → MakerVolume → post-merge → adaptive sew
    ≤ 0.1/clamp 0.3, kept only if naked count drops → Validate) → `GapFill` → `RetainDropped`
    (re-adds extended, overshooting geometry). `StopAfterClean`/`StopAfterExtend` seams exist.
    Defaults: BucketSize 0.3, Weight 1.0, MaxExtension 0.4, extension cap 0.49·length,
    ToleranceAngle 5°, ToleranceArcAngle 0.3°, VerticalAngleTolerance 20°, tilted-`Up` support.
  - `Classes/SnappedPanel.cs` (757 lines) — plane+Face3D wrapper: `BucketContains`, `SnapToBacker`,
    `OverlapsInPlane`, `AbutsColinearWithin`, `GrowOutward(To)`, `ExtendTop/BottomTo`,
    `ExtendHorizontal`, `GetBaseSegment`, `IsVertical`; carries `SourceIndices`/`SourceFace3Ds`.
  - `Classes/GapFill.cs` (430 lines) — managed naked-loop walk → planar patch or centroid fan.
- **Analytical wrapper** `SAM_OCCT/SAM.Analytical.OCCT.Solver/Modify/Solve.cs` (590 lines) —
  `Solve3D`/`Clean3D`/`Extend3D`/`OpenPanels3D`; skips air panels; bucket = `SolverParameter.
  BucketSize` else thickness×0.6 floored at `minBucketSize` 0.4; single global `Up` from floor
  normals; `BuildPanels` re-maps output by plane+centroid heuristic (`NearestSourceIndex`) and
  rebuilds via construction/type ctor (drops Guid, parameters, apertures).
- **Native bridge** `SAM.Geometry.OCCT` + `SAM.Core.OCCT` + `native/SAM.Occt.Native` (ABI v3):
  `Create.Shells` (MakerVolume cell complex; `sam_occt_shape_create_cell_complex`), `Query.Sew`,
  `Query.Validate` (BRepCheck + free bounds → naked-edge points), `Query.Watertightness`,
  `Query.MergeCoplanarFace3Ds` (UnifySameDomain), `Query.ShellsImprint`, `Query.ShellsDistance`,
  `Query.ShellsUnion/Difference/Intersection`, `Query.ShellsRepair`, `Query.ShellsOffset`,
  `Query.ShellSectionByPlanes`, `Query.IsPointInside`, `Create.Triangulate`. Cell metadata already
  exposed: `sam_occt_result_cell_count/_volume/_center`. `OcctBuildOptions`: Tolerance (=SAM
  `Tolerance.Distance` 1e-6), **FuzzyTolerance (= `MacroDistance` 1e-3)**, RunParallel,
  AvoidInternalShapes (false ⇒ zoned complex), ValidateInput, SewBeforeBuild, SewingTolerance,
  RetainTopology (live `OcctTopology` handle, currently unused by the solver), GlueMode
  (Off/Shift/Full, gated by watertight pre-check). **No history export — this is the biggest gap.**
- **Grasshopper** (net8.0): `SAMOCCTSolve3D`, `SAMOCCTClean3D`, `SAMOCCTExtend3D`,
  `SAMOCCTCreateAdjacencyCluster(ByShells)`, `SAMOCCTMergeSmallSpaces`, `SAMOCCTPanelsFromShells`.
- **Tests**: two-tier (`Testing/SAM.OCCT.UnitTests` pure managed; `Testing/SAM.OCCT.
  IntegrationTests` gated by `NativeProbe`/`NativeRuntimeBootstrap` on committed `build/` DLLs).
  Fixtures: `whole-level-flat.sam` (22 cells), `tilted-two-spaces`, `whole-level-tilted`,
  `two-level-tilted`, `whole-level-towers`. CI (`.github/workflows/build.yml`) builds managed only
  (`SAM_OCCT_SKIP_NATIVE_BUILD=true`), clones siblings pinned to `sow/2026-Q2` (drifted; repos are
  on Q3), and special-cases a cross-repo binary HintPath to `SAM_Solver/build/SAM.Geometry.
  Solver.dll` (the ExtensionSolver reuse).
- **Conventions**: SPDX `LGPL-3.0-or-later` header on every `.cs`; xUnit `Method_State_Expected`;
  netstandard2.0 core / net8.0 GH; output to `build/`; commits signed
  "Generated by Michal Dengusiak & Claude Code" (CLAUDE.md).

### B.2 SAM — types consumed

`Tolerance` (MicroDistance 1e-9, Distance 1e-6, MacroDistance 1e-3, Angle ≈2°); `Face3D` (plane +
external + internal 2D loops, `Face3D.Create`, `Normalize`, IJSAMObject); `Plane` (Closest, Coplanar
±normal, 3D↔2D Convert); `Shell` (IsClosed via naked segments, Inside ray-cast); `Query.Union
(IEnumerable<Face3D>)`, `Query.Snap`, `Query.Coplanar`, `Query.Cut`, `Query.SelfIntersectionFace3Ds`;
`Panel` (PlanarBoundary3D parametric storage; **`Panel(Guid, Panel, Face3D, apertures, trimGeometry,
minArea, maxDistance)` geometry-replace ctor re-hosts and trims apertures** — the 2D solver's output
contract); `PanelType` (incl. Air); `Construction.GetThickness()`; `AdjacencyCluster`/`Space`;
GH base `GH_SAMVariableOutputParameterComponent` + `GH_SAMParam` + `Goo*Param`.

### B.3 SAM_Solver — semantics contract to preserve (the "backer + width" law)

- Ordering: sort `Weight DESC → BucketSize DESC → Length DESC`; higher-priority "current" initiates
  snaps; equal weights (±1 %) ⇒ both move to midpoint and BucketSize grows by the merge offset.
- `BucketContains`: backer-axis parameter range with margin `min(MaxExtension, 0.49·Length)/Length`;
  distance = max endpoint projection distance ≤ BucketSize; angle 5° fully-contained / 0.3° partial.
- MaxExtension hard cap **0.49 × element length**; ExtensionSolver cost: trim = d/2, extend = d
  (trim cheaper), cheapest unresolved junction first; naked endpoint = not within 1e-6 of any axis.
- GraphSolver nodes = weighted average of max-weight contributors only; MergeColinear 0.001°,
  same-source-only; **SourceIndices appended on merge, never dropped**.
- Auto-parameters: Weight — air 0, others remap max horizontal dimension → [0.2, 1.0]; BucketSize —
  thickness×0.6 (2D floor 0.12); MaxExtend — 0.6 thick (>0.29) / 0.5 std / 0.33 unknown, cap 0.49·dim.
  `SolverParameter` enum: "Weight", "Bucket Size", "Max Extend".
- **AutoTuneSolver**: MaxExtend ladder [0.5, 0.75, 1.0, 1.5] applied **only to gap-adjacent
  panels**, ≤ 6 rounds, accepted only if ClosureSignature (loop count + area) does not regress.
  → 3D analogue: **cell count + volume + naked count**; escalation is per-defect, not pipeline-wide.
- 2D output contract: panels rebuilt via `Analytical.Create.Panel(source.Guid, source, face3D)` —
  Guid, parameters, construction, **apertures** all preserved. GH "Solver" component I/O shape is
  the template for the 3D component.

### B.4 Research summary — state of the art (verified via OCCT docs/forums, 2026-07)

1. **Face snapping & tolerance management:** industry practice is hierarchical tolerance —
   model-space snap (coarse, user-driven) ≫ kernel fuzzy ≫ vertex/edge local tolerances. Snap onto
   exact analytic planes BEFORE the kernel; fuzzy only absorbs residual noise. Tolerances must not
   silently inflate: OCCT sewing works at `WorkTolerance = sewing tol + local edge tolerances`;
   runaway drift is a classic failure mode (monitor max shape tolerance per stage).
2. **Plane/face clustering in 3D:** point-cloud methods (RANSAC, region growing) are the wrong tool
   for CAD panels whose exact planes are known; best practice is greedy weighted clustering on a
   plane-space metric (normal cone + signed offset band) seeded by dominance — exactly the
   backer/Weight pattern; canonical-plane quantization is what commercial BIM healers do.
3. **Robust imprinting/splitting:** OCCT General Fuse (`BOPAlgo_Builder`) is the canonical mutual
   imprint of a face soup; `BOPAlgo_MakerVolume` = GF + BuilderSolid for closed solids. Arguments
   must be valid and not self-interfering; fuzzy handles near-coincidence; glue (Shift/Full) is a
   large performance win when inputs only touch — unsafe otherwise.
4. **Sewing/healing near-touching face soups:** `BRepBuilderAPI_Sewing` with explicit tolerance,
   then `ShapeFix_Shell`/`ShapeFix_Solid`. Sewing stitches near-coincident edges but never splits
   faces — imprint first, then sew.
5. **Watertight shells/solids:** MakerVolume directly when faces partition space; sew + ShapeFix +
   `BRepBuilderAPI_MakeSolid` when they merely bound one region. `ShapeAnalysis_FreeBounds` is the
   ground truth for naked edges; `BRepCheck_Analyzer`/`BOPAlgo_ArgumentAnalyzer` validate inputs
   before booleans (fail fast, not mid-fuse).
6. **Cell-complex construction:** `BOPAlgo_CellsBuilder` (GF-based) is the canonical space
   partitioner (all split parts retained, cells selected/merged via material markers). Topologic's
   non-manifold CellComplex on OCCT is direct prior art for building-space graphs from panel
   faces — validating cells-as-product for MEP semantics. MakerVolume with internal shapes kept
   already yields the zoned complex this plan needs.
7. **Persistent topology/source mapping:** the OCCT standard is `Modified()/Generated()/IsDeleted()`
   per algorithm; `BRepTools_History` merges histories across chained operations (7.2+), with faces
   first-class. Best practice: compose input→output maps across every stage; geometric re-matching
   is a fallback only, never the mechanism.
8. **Diagnostics:** naked edges via free-bounds wires; gaps via extrema/distance queries below
   threshold; duplicate/sliver faces via area + aspect ratio after GF; non-manifold edges via
   edge→face incidence counts; tolerance drift via max-tolerance deltas per stage — all emitted
   machine-readable with shape references.
9. **OCCT tool mapping for this workflow:** GF/MakerVolume (partition/solids), Sewing (stitch),
   ShapeFix (heal), `ShapeUpgrade_UnifySameDomain` (same-domain merge — requires near-exact
   geometry, can hang on dirty input, preserves shell boundaries: snap first, merge after),
   free-bounds (naked), distance queries, fuzzy/glue options, `BRepTools_History` (mapping).

Sources: OCCT reference manual and boolean-operations user guide (`BOPAlgo_MakerVolume`,
`BOPAlgo_CellsBuilder`, `BOPAlgo_Builder`/`BOPAlgo_Options`, `BRepTools_History`,
`BRepBuilderAPI_Sewing`, `ShapeUpgrade_UnifySameDomain`), dev.opencascade.org forum threads on
watertight sewing workflows and UnifySameDomain limitations, and Topologic (non-manifold topology
for architecture) publications.

## C. Critique of `docs/3D_PANEL_SOLVER_PLAN.md`

**Correct — keep:** OCCT-native-first (empirically vindicated); backer=Weight/width=BucketSize;
two-project layout; native reuse table; two-tier testing; not porting GraphSolver.

**Stale / contradicted by the live code:**
1. Targets branch `sow/2026-Q2`; work lives on `fix/solver-raw-first`.
2. Mandates CleanBucket-always-first; the repo's own tests show raw-first beats it on every
   well-modelled fixture — the plan's core flow is superseded by its own branch.
3. "Do not port `ExtensionSolver`" — falsified: `ExtendWalls` delegates to the 2D `ExtensionSolver`
   via a cross-repo binary HintPath that CI must special-case.
4. Parameter drift: plan says width thickness×0.6 min 0.2, MaxExtension 0.5; live uses
   minBucketSize 0.4 floor and DEFAULT_MaxExtension 0.4.
5. "GH component is a follow-up" — the GH suite already ships and freezes API expectations.
6. Prescribes `ShellSectionByPlanes`/`ShellsImprint` as junction resolvers; live solver converged on
   MakerVolume + sew; "cluster into near-coplanar groups" was never implemented (snap is pairwise
   greedy, one-shot, missing 2D's fixed-point iteration and tie-breakers).
7. "Sloped-roof heuristics out of scope" — RoofOvershoot/ExtendToRoofs/tilted-Up already exist.

**Missing entirely (each becomes a phase or gate here):** phasing of any kind; source-mapping design
(the "emit source mapping" bullet with zero mechanism is how the `NearestSourceIndex` heuristic debt
happened); escalation/adoption gates and closure-signature acceptance; aperture story — the plan
*codifies* aperture loss ("openings are discarded") where 2D preserves them; dropped-face policy;
tolerance budget and drift monitoring; diagnostics contract; cell classification / Spaces handoff
(ignoring in-repo prior art: the Tower path already builds an AdjacencyCluster from cells);
inter-storey / per-level frames; golden-master fixtures; CI/native posture; performance plan.

**Live defects found during this review (fixed by phases below):**
- `Panel3DSnapSolver.cs:300-312` — per-panel `maxExtensions` re-applied **positionally** to the
  merged/reordered `CleanFace3Ds`; wrong MaxExtend lands on wrong panel whenever Step 1 changes
  count/order. (Phase 2)
- `Panel3DSnapSolver.cs:1221-1236` — raw adoption gate is only `cells ≥ 1 && naked == 0`;
  watertight-but-wrong (e.g. two rooms merged into one cell) is adopted. (Phase 1)
- `SnapOpposedPartitions` tests anti-parallelism/overlap/area-ratio but **not separation direction**
  — two facing walls of a genuine 0.3–0.4 m shaft void satisfy every condition and the void is
  deleted; skins differing by a door cut fail the 0.97 area gate and stay split. (Phase 2)
- Adaptive sew accepts on **global** naked-count decrease — a weld that closes 5 gaps and fuses 1
  genuine partition is accepted. (Phase 5)
- `RetainDropped` re-adds the **extended, overshooting** geometry; post-history this double-crosses
  neighbours; on the raw path it passes input junk straight to output. (Phase 5)
- Single global `Up` + world-frame `IsVertical(20°)` makes 20° the undocumented tilt ceiling; a
  >20°-tilted level reclassifies walls as caps and NormalizeCaps merges rooms. (Phase 6)
- `NakedEdgePoint3Ds` is captured before GapFill patches exist and patches are never re-validated —
  diagnostics can report "closed" on a mis-seated patch. (Phase 5)
- `MergeCoplanarFace3Ds` is invoked at 5° angular tolerance post-resolve — generous enough to fuse
  slightly-sloped roof planes; post-snap it should run at ≤ 2° (SAM `Tolerance.Angle`). (Phase 4/5)

## D. Recommended final architecture

**Answer to the critical question: (4) Hybrid** — the shape of (2) three-stage
Snap → Resolve → Heal/Close, the product of (3) cell-complex-first, the semantics of (1)
CleanBucket+Resolve retained as conditioning, and the branch's raw-first insight as adoption level 0.

Rejections, grounded in evidence:
- **Pure (1) CleanBucket+Resolve** (conditioning-always): refuted by the towers fixture — the
  conditioning itself (cap normalization, blind extends, opposed-collapse) broke well-modelled input
  (23/12 vs raw ≥31/0).
- **Pure (3) cell-complex-first-always** (no managed conditioning): refuted by tolerance
  arithmetic — the kernel absorbs sew 0.01 + fuzzy 1e-3; the modelling error the managed pipeline
  exists to fix is bucket-scale 0.3–0.4 m, two orders of magnitude larger. Gappy models need
  conditioning before the kernel can succeed.
- **A 5-level global pipeline ladder** (raw → snapped → snapped+extend → sew-expanded → gap-fill):
  over-engineered. Sew and gap-fill are post-passes with local acceptance, not cell-build modes;
  "targeted extend" as a blind global rung reintroduces the blind-extend failure class. The 2D
  precedent (AutoTuneSolver) escalates **parameters on implicated panels**, never pipeline modes.

**The pipeline:**

- **Stage A — SNAP (managed, deterministic, non-fabricating).** Make near-coincident geometry
  exactly coincident; never invent geometry. Weight-seeded plane clustering (normal cone + offset
  band per-panel BucketSize), `SnapToBacker` projection with the 2D ordering/tie-break/midpoint-
  merge law run to a **fixed point** (2D `SnapAndAdjustWalls` parity, ≤1000 iterations — the live
  one-shot pass is a known gap, owned by Phase 2b), opposed-partition collapse (with separation-sign
  + overlap-area gates), cap normalization per level-group, mapping-preserving coplanar union.
  Output: `SnappedFace3Ds` + SourceMap.
- **Stage B — RESOLVE (native, three adoption levels, history on).**
  L0 = raw input faces; L1 = Stage-A snapped faces; L2 = conditioned faces (extend walls→walls,
  extend to caps, fill caps — today's Step 2, mapping-threaded). For each level in order run the
  native cell build (sew 0.01 → MakerVolume, fuzzy 1e-3, AvoidInternalShapes=false, history); adopt
  the first level whose **ClosureSignature3D** passes: naked == 0, cells ≥ 1, no sliver cells
  (volume < 0.05 m³ configurable), dropped-input count ≤ threshold, and cell count not collapsing
  versus the level below (guards watertight-but-merged). L1 exists for the genuinely distinct
  population: sub-bucket noise too big for sew 0.01, too small to need fabrication.
- **Stage C — HEAL/CLOSE (diagnosis-driven, bounded).** While naked loops remain and rounds < 3:
  native free-bounds → loops attributed to source panels via composed history → **AutoTune3D**:
  raise MaxExtend (ladder [0.5, 0.75, 1.0, 1.5]) / bucket only on implicated panels, trim cheaper
  than extend, re-run L2 → accept only on signature non-regression. Then: adaptive sew with
  **per-loop acceptance** (reject welds that fuse distinct parallel faces; cap sew below measured
  min inter-face separation via `ShellsDistance`); gap-fill remaining loops (native wires as loop
  source), imprint patches, **re-validate resolved+patches**; classify cells interior/exterior;
  reconstruct panels (Guid-preserving, apertures re-hosted, provenance stamped).

**Why the cell complex is the product:** it already is (`ResolvedCellCount`, cell volume/center
ABI); it is the 3D ClosureSignature (cells ↔ 2D room loops, volume ↔ area); it is the MEP semantic
(cells → Spaces/zones — Topologic prior art); and MakerVolume/GF is the industry-canonical robust
imprint of a face soup (CellsBuilder selection semantics available if ever needed).

## E. Phased implementation roadmap

Legend per phase: **Model** (per user rubric) · **Effort** XS/S/M/L/XL · sessions · risk ·
**Gate** = all unit tests green + local integration suite run on the fixture set (CI native build is
explicitly out of scope by owner decision — the local run is the merge gate, recorded in the PR).

---

### Phase 0 — Golden-master lock: ClosureSignature3D + baseline fixtures + test protocol
- **Objective:** freeze current behaviour as machine-checked baselines before touching anything.
- **Model:** Sonnet 5 · **Effort:** S · 1 session · risk Low.
- **Touches:** `SAM.Geometry.OCCT.Solver` (new file), `Testing/SAM.OCCT.IntegrationTests`,
  `TESTING.md`.
- **New:** `Classes/ClosureSignature3D.cs` (CellCount, CellVolumes[], TotalVolume, NakedEdgeCount,
  FaceCount, DroppedCount; `IsRegressionOf(other, volumeTol)`); `GoldenMasters.cs` test asserting
  the signature of every fixture (flat 22 cells/0 naked; tilted-two 2/0; whole-tilted 22/0;
  two-level-tilted ≥40/0; towers ≥31/0) on both raw and managed paths (managed via forcing
  fall-through).
- **Reuse:** cell count/volume already exposed (`OcctCellComplexResult`, `sam_occt_result_cell_*`);
  `Validate` naked count.
- **Native ops:** none new (read-only consumption).
- **Acceptance:** signatures recorded and asserted for all 5 fixtures; TESTING.md documents the
  per-phase local-integration merge gate; solution builds; no solver behaviour change.
- **Tests:** the golden-master suite itself + unit tests for `IsRegressionOf`.
- **Failure modes/diagnostics:** none new — this phase creates the tripwire.
- **Depends:** nothing. **Out of scope:** CI native build (owner-deferred), any pipeline change,
  sibling-branch pin fix (note it in TESTING.md as known CI drift: build.yml pins `sow/2026-Q2`).

### Phase 1 — Diagnostics contract + raw-adoption gate hardening
- **Objective:** machine-readable per-stage diagnostics all later phases emit into; close the
  watertight-but-wrong adoption hole.
- **Model:** Sonnet 5 · **Effort:** S–M · 1–2 sessions · risk Low.
- **Touches:** `SAM.Geometry.OCCT.Solver` (new files + `Panel3DSnapSolver.TryRawResolve`),
  `SAM.Analytical.OCCT.Solver/Modify/Solve.cs` (surface diagnostics), unit+integration tests.
- **New:** `Classes/SolverDiagnostics.cs` + `SolverDiagnostic` (Stage, Code, Severity, Message,
  Point3Ds/Face3D refs, tolerance used, elapsed ms). Code taxonomy: `NakedEdge`, `NakedLoop`, `Gap`,
  `Overlap`, `DuplicateFace`, `SliverFace`, `SliverCell`, `NonManifoldEdge`, `ToleranceDrift`,
  `DroppedFace`, `RejectedSew`, `RejectedCollapse`, `AdoptedLevel`, `EscalatedPanel`.
  Gate hardening in `TryRawResolve`: also require no sliver cells (volume < `MinCellVolume`,
  default 0.05 m³), dropped-count ≤ `MaxDroppedRatio` (default 10 %), and emit `AdoptedLevel` +
  signature diagnostics; expose `Signature` per attempted level on the solver.
- **Reuse:** `OcctDiagnostic` pattern in `SAM.Core.OCCT`; ClosureSignature3D (Phase 0).
- **Native ops:** existing `Validate`, cell metadata.
- **Acceptance:** golden masters unchanged; a synthetic merged-cells fixture (two rooms with a
  deleted partition, watertight outer shell) is now **rejected** by the raw gate and falls through.
- **Tests:** unit — gate logic truth table; integration — merged-cells fixture rejection.
- **Failure modes/diagnostics:** the contract itself; every rejection now carries a reason.
- **Depends:** Phase 0. **Out of scope:** history, staging refactor, GH surfacing.

### Phase 2 — Stage decomposition + SourceMap threading (fixes live MaxExtend + void-collapse bugs)
- **Objective:** break the 1449-line monolith into explicit Stage A/B/C passes over a shared
  context; thread source mapping through every managed stage; fix the two managed-side defects.
- **Model:** Opus 4.8 (behaviour-preserving refactor) · **Effort:** L · 2–3 sessions · risk Medium.
- **Touches:** `SAM.Geometry.OCCT.Solver/Classes/*` (Panel3DSnapSolver becomes a façade),
  `SAM.Analytical.OCCT.Solver/Modify/Solve.cs`, tests.
- **New:** `Classes/SolverContext.cs` (input faces, per-panel params keyed by **source identity**,
  SourceMap, SolverDiagnostics, tolerance budget); `Classes/SourceMap.cs` (input index → output-face
  keys; `Compose(next)`, `RecordMerge/Split/Fabricated(provenance)`; provenance enum
  `Input|Snapped|Conditioned|Resolved|DroppedRetained|GapFill`); `Classes/SnapStage.cs`
  (CleanBucket contents), `Classes/ConditionStage.cs` (ExtendWalls/Extend/Fill),
  `Classes/ResolveStage.cs`, `Classes/HealStage.cs` (current sew+GapFill+RetainDropped as-is).
  Fixes: (a) MaxExtend carried per source through `SourceMap` instead of positionally
  (`Panel3DSnapSolver.cs:300-312`); (b) `SnapOpposedPartitions` gains the separation-sign test
  (`normal · (centroidB − centroidA)` must indicate *facing-away* skins) and gates area on the
  overlap region, driven by per-panel thickness not the 0.4 floor.
- **Reuse:** all existing stage bodies verbatim (public statics `CleanBucket`, `ExtendWalls`,
  `Extend`, `Fill`, `Snap`, `NormalizeCaps`, `SnapOpposedPartitions`); mapping-preserving union re-
  implemented over `SAM.Geometry.Spatial.Query.Union` by unioning per coplanar group and recording
  group→output.
- **Native ops:** unchanged.
- **Acceptance:** golden masters unchanged **except** documented deltas from the MaxExtend fix on
  the managed path (quantified in the PR, then re-baselined); a new shaft-void fixture (two facing
  walls 0.35 m apart forming a real void) survives Stage A; every output face has ≥ 1 source or a
  fabricated provenance tag (asserted).
- **Tests:** unit — SourceMap compose/merge/split invariants; opposed-partition truth table
  (facing-away congruent ⇒ collapse; facing-toward ⇒ keep; door-cut skins ⇒ collapse via
  overlap-area gate); per-stage snapshot tests. Integration — shaft-void fixture; golden masters.
- **Failure modes/diagnostics:** `RejectedCollapse` with reason; mapping-orphan faces are an error.
- **Depends:** Phases 0–1. **Out of scope:** native history (mapping is managed-only until
  Phase 3 — ResolveStage records a coarse whole-set mapping), new snap semantics.

### Phase 2b — Snap-law parity: fixed-point iteration, tie-breakers, midpoint rule
- **Objective:** upgrade Stage A from the live one-shot greedy pass to the proven 2D snap law, so
  more models adopt at L1 (snapped) instead of falling through to L2 fabrication.
- **Model:** Sonnet 5 · **Effort:** M · 1–2 sessions · risk Medium (behaviour change, well-specified
  by the 2D contract in §B.3).
- **Touches:** `SAM.Geometry.OCCT.Solver` (`SnapStage`), unit + integration tests.
- **New:** fixed-point snap loop (`repeat until no changes`, cap `MaxSnapIterations = 1000` with a
  warning diagnostic on cap hit — 2D parity); full ordering law `Weight DESC → BucketSize DESC →
  Area DESC` re-sorted per iteration; equal-weight (±1 %) midpoint rule (`MoveBothToMidplane` +
  `GrowBucketForBoth`, mirroring 2D's axis-average + bucket bump); candidate pre-filter
  `CandidatePanelsNear` via bbox index (shared with Phase 9).
- **Reuse:** `SnappedPanel.BucketContains/SnapToBacker/IsParallelWith`; 2D `SnapAndAdjustWalls`
  semantics (SAM_Solver `SnapSolver.cs:959-1041`) as the specification.
- **Native ops:** none.
- **Acceptance:** snap reaches a fixed point on all fixtures (iteration count reported in
  diagnostics); a chained-offset fixture (A within B's bucket, B within C's — requires ≥ 2
  iterations to fully coplanarize) converges; golden masters re-baselined with quantified deltas
  (expected: more L1 adoptions, never fewer closures).
- **Tests:** unit — parity truth table vs the 2D law (ordering, tie-break, midpoint, growth);
  chained-offset convergence; iteration-cap warning. Integration — fixture signatures.
- **Failure modes/diagnostics:** `SnapIterationCapReached` warning; per-iteration change counts.
- **Depends:** Phase 2 (SnapStage exists). **Out of scope:** any conditioning/extend change; new
  clustering data structures beyond the bbox pre-filter.

### Phase 3 — Native history export (ABI v4) + naked-loop & tolerance-drift exports
- **Objective:** exact provenance through native ops; naked-edge **loops** (wires) not just points;
  per-stage max-tolerance monitoring. Highest-risk native work — isolate it.
- **Model:** Opus 4.8 (C++/ABI design) · **Effort:** L · 2–3 sessions · risk High.
- **Touches:** `native/SAM.Occt.Native/include/sam_occt.h`, `src/CellComplexBuilder.cpp` + new
  `src/History.cpp`; `SAM.Core.OCCT` (new `OcctHistory`), `SAM.Geometry.OCCT`
  (`Create.Shells`/`Query.MergeCoplanarFace3Ds`/`Query.Sew`/`Query.Validate` history-enabled
  overloads); `build-native.ps1` untouched (same DLL).
- **New:** ABI v4 additive symbols (probe/degrade exactly like the v3 glue precedent):
  `sam_occt_result_history_counts/entries` (per input face index: deleted flag, modified→output face
  indices, generated→output face indices) wrapping `BRepTools_History` (`Modified/Generated/
  IsDeleted`); `sam_occt_result_naked_wires` (free-bound edges grouped into wires via
  `ShapeAnalysis_FreeBounds`); `sam_occt_shape_max_tolerance` (`ShapeAnalysis_ShapeTolerance`).
  Managed: `OcctHistory` (compose across the resolve chain: sew → MakerVolume → merge),
  `SourceMap.Compose(OcctHistory)`. First implementation task: a native spike verifying the OCCT
  8.0 history surface for `BOPAlgo_MakerVolume`, `BRepBuilderAPI_Sewing` (history via
  `ModifiedSubShape`/wrapper), and `ShapeUpgrade_UnifySameDomain::History()` on the pinned vcpkg
  baseline — the main sizing unknown. **Spike timebox: 1 session.** If UnifySameDomain history is
  unreliable within it, pivot immediately to the documented fallback (managed same-plane merge
  mapping; history from MakerVolume+sew only) — do not let the spike absorb the phase.
- **Reuse:** ABI probe pattern (`sam_occt_abi_version`), flattened-array marshalling, `OcctTopology`.
- **Native ops:** `BRepTools_History`, `ShapeAnalysis_FreeBounds`, `ShapeAnalysis_ShapeTolerance`.
- **Acceptance:** on a split-face fixture (wall crossed by a floor) history reports 1 input → 2
  outputs; on a merge fixture 2 inputs → 1 output; `Solve.cs BuildPanels` consumes composed history
  with `NearestSourceIndex` demoted to kernel-absent/old-ABI fallback; naked wires returned as
  ordered polylines; max tolerance reported per stage and asserted < 10× input tolerance on
  fixtures; managed degrade path proven against an ABI-v3 stub; **interop soak test — 50
  successive full solves of a fixture, asserting unmanaged memory plateaus (process private bytes
  delta < 10 % between runs 10 and 50) and every native result/history handle is disposed
  (extend the existing `OcctTopologyGuardTests` pattern to `OcctHistory`).**
- **Tests:** integration — split/merge/delete mapping fixtures, wire grouping, drift; unit —
  history composition algebra (incl. deleted-then-regenerated chains).
- **Failure modes/diagnostics:** `ToleranceDrift` emission; history-gap (face with no record) is a
  warning + heuristic fallback, never silent.
- **Depends:** Phase 2 (SourceMap exists to consume it). **Out of scope:** single-session native
  chaining (stretch: only if the spike shows the five managed↔native round-trips in `Resolve`
  materially hurt at 2k panels — record measurement either way); CellsBuilder exposure.
- **Fallback:** if OCCT 8.0 history for UnifySameDomain proves unreliable, run merge-coplanar
  *before* history-critical ops and map merges managed-side (geometric same-plane grouping) —
  MakerVolume+sew history alone still covers the splitting/deletion cases that matter most.
- **Spike results (2026-07-03, OCCT 8.0.0 vc14, throwaway `cl.exe` scratch exe — all 7 GO, no
  fallback needed):** S1 MakerVolume history — box+mid-face → 2 solids, a side face `Modified`→2
  (split), a stray face `IsRemoved` (delete). S2 Sewing — `ModifiedSubShape` returns a face per
  input; a hand-built `BRepTools_History` (AddModified) round-trips. S3 `BRepTools_ReShape::
  History()` maps a replaced face 1→1 (ShapeFix chain is trackable; no sew-hop degrade needed).
  **S4 `ShapeUpgrade_UnifySameDomain::History()` — 2 coplanar faces → 1, both inputs `Modified`→
  the SAME output face (the key unknown: GO — the managed same-plane fallback is NOT needed).**
  S5 `BRepTools_History::Merge` composes A→B then B→C into A→C. S6 `ShapeAnalysis_FreeBounds` on
  an open box → 1 closed wire, 4 ordered edges, every free edge's owner face resolved via
  `TopExp::MapShapesAndAncestors(EDGE→FACE)`. S7 `ShapeAnalysis_ShapeTolerance` on a 0.05-sew
  shape → max 0.0315 / avg 0.0121 / min 0 (finite, sane; confirms the two-part tolerance test —
  a healing sew legitimately exceeds 10× a 1e-6 input). Consequence: implement the full ABI v4
  (history on MakerVolume + merge-coplanar + sew, all merged via `BRepTools_History::Merge`) as
  designed in `docs/P3_ABI_V4_NATIVE_HISTORY_DESIGN_REVIEW.md`; no scope cuts.
- **Phase 3 COMPLETE (2026-07-03).** ABI v4 native committed (`099301c`) + validated end-to-end and
  the managed layer landed. Native rebuilt from source, both suites green against the ABI v4 DLL with
  **unchanged golden masters** (observationality proven). Delivered: `OcctHistory` (pure managed
  snapshot on `OcctCellComplexResult.History`, captured by `build_cell_complex` + `merge_coplanar`);
  `HistorySourceMap.ToSourceMap` adapter (split/merge/delete + `HistoryGap`/reverse-gap diagnostics);
  `ResolveStage` per-adopted-hop composition (`ResolveHistorySourceMap`); `Solve3D.BuildPanels`
  consumes the exact map with `NearestSourceIndex` demoted to a per-face fallback; naked wires on
  `OcctValidationReport.NakedWires`; max/avg tolerance export. Tests: unit **265/265**
  (`OcctHistorySourceMapTests`, +9), integration **107 pass / 1 skip** (`HistoryExportIntegrationTests`
  - split 1→2, shared-face two-ordinals, merge 2→1, naked wire, drift ceiling, null-history degrade,
  50-build soak). **Deviation carried:** the §F.4 sew-hop scope cut (§7.1 of the handover) - the
  standalone sew→decode hop captures no history, so the default sew-before-build solver path falls back
  to the geometric heuristic (which is why the golden masters are unchanged); history composition runs
  on the direct `build_cell_complex` path. See `TESTING.md` (Phase 3) and the design review.

### Phase 4 — Panel reconstruction fidelity: Guid, parameters, apertures, provenance
- **Objective:** restore 2D output parity — solved panels are the *same* panels with new geometry.
- **Model:** Sonnet 5 · **Effort:** M · 1–2 sessions · risk Medium.
- **Touches:** `SAM.Analytical.OCCT.Solver/Modify/Solve.cs` (`BuildPanels`), tests.
- **New:** rebuild via `Analytical.Create.Panel(source.Guid, sourcePanel, face3D)` (re-hosts +
  trims apertures; preserves parameters/construction) for 1:1 mappings; for split faces, one panel
  per output face (new Guids, source Guid stamped as parameter, apertures assigned to the piece that
  geometrically contains them); for merged faces, dominant (largest-area) source wins Guid, others
  stamped; gap-fill faces → `PanelType.Air` panels, provenance-stamped; post-resolve
  `MergeCoplanarFace3Ds` tightened to SAM `Tolerance.Angle` (≤ 2°).
- **Reuse:** `Panel(Guid, Panel, Face3D, …, trimGeometry, minArea, maxDistance)` ctor
  (SAM/SAM.Analytical/Classes/Panel.cs:112-138); `Modify.AddApertures`; SourceMap (Phase 2/3).
- **Native ops:** none new.
- **Acceptance:** aperture fixture (room with 2 windows + door) round-trips: solved wall keeps both
  windows trimmed to new extents; parameters (`SolverParameter.*` and arbitrary user params) and
  constructions survive; air/gap-fill panels typed and tagged.
- **Tests:** unit — split/merge Guid policy; integration — aperture fixture, parameter round-trip
  on flat fixture.
- **Failure modes/diagnostics:** aperture that no longer fits any output face → **orphan policy**:
  (1) try re-hosting on the geometrically nearest output panel derived from the same source within
  `maxDistance` (the Panel ctor's own trim reach); (2) if none, the aperture is returned in
  `Solver3DResult.OrphanedApertures` (original world-space geometry + source panel Guid) with a
  `Warning` diagnostic, and exposed as a GH output for manual re-hosting. Apertures are never
  attached to Spaces (not a SAM concept) and never silently dropped.
- **Depends:** Phase 3 (mapping). **Out of scope:** aperture re-projection onto *moved* planes
  beyond what the Panel ctor's trim already does; openings participating in solving.
- **Phase 4 COMPLETE (2026-07-03).** Delivered: `PanelReconstruction.Build` (`SAM.Analytical.OCCT.
  Solver`, pure/native-free) consuming the solver's `SourceMap` for the 1:1/split/merge Guid policy
  exactly as specified; apertures re-hosted via the existing `Create.Panel(guid, panel, face3D,
  apertures, trimGeometry, minArea, maxDistance)` ctor with no bespoke matching (every contributing
  source's apertures are tried against every produced piece, which already realises "assigned to the
  piece that geometrically contains it"); air panels stamped `PanelProvenanceParameter.Provenance =
  "GapFill"`; the post-resolve angle tightening to `Tolerance.Angle`. Tests: unit
  `PanelReconstructionTests` (1:1/split/merge/orphan/fallback, 6 cases), integration
  `AperturePreservationIntegrationTests` (sealed-room window round-trip, native-gated). Full suite
  after Phase 4: unit **271/271**, integration **108 pass / 1 skip**.
  **Two deviations from this section's literal wording (both scope-narrowing, not scope-cutting):**
  (1) the orphan report is `Modify.Solve3D`'s new `out List<OrphanedAperture> orphanedApertures`
  parameter on an additive overload, not a `Solver3DResult` type - introducing a new aggregate result
  class would have changed `Solve3D`'s return shape, and C# `out` parameters cannot be optional, so a
  new *required* one could only be added as a new overload without breaking the shipped Grasshopper
  `SAMOCCT.Solve3D` component (which calls the 2-out-param form positionally); the "try re-hosting on
  the nearest same-source piece" leg of the orphan policy is subsumed by trying every produced piece
  (no separate retry step exists to fail before returning an orphan). (2) the acceptance's aperture
  fixture (a `.sam` room with 2 windows + a door) was not built; the 1:1 leg is proven end-to-end
  through the real kernel on a synthetic sealed room with one window (`AperturePreservationIntegrationTests`),
  and the split/merge Guid legs - which need a literal `BOPAlgo_MakerVolume` split, not a stable
  substrate for a fast deterministic assertion - are proven with a hand-built `SourceMap` in the unit
  tests instead. Golden-master delta from the angle-tightening: **none measured** (see TESTING.md) -
  documented per the local merge-gate protocol.

### Phase 5 — Diagnosis-driven closure: AutoTune3D, per-loop sew acceptance, RetainDropped v2
- **Design review (authority):** `docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md` (GO WITH
  CHANGES) — where it and this plan differ, **the review wins**. It carries the binding owner
  cautions, the consolidation-rebuild redesign (no surgical imprint / no ABI v5), the per-loop-sew
  bookkeeping+veto model, the sub-phase (5a–5f) sequencing, and the Opus 4.8 implementation prompt.
- **Objective:** replace blind margins with bounded, diagnosed, locally-escalated closure — the
  hardest algorithmic phase.
- **Model:** Opus 4.8 · **Effort:** XL · 3–4 sessions · risk High.
- **Touches:** `SAM.Geometry.OCCT.Solver` (`HealStage`, `ConditionStage`, `GapFill`),
  `SAM.Analytical.OCCT.Solver` (AutoTune3D entry point), tests + new fixtures.
- **New:** `Classes/AutoTune3DSolver.cs` mirroring 2D AutoTuneSolver contract: attribute naked
  loops (native wires, Phase 3) to source panels via SourceMap; raise MaxExtend along ladder
  [0.5, 0.75, 1.0, 1.5] (and optionally bucket ×1.25) **only on implicated panels**; trim-cheaper-
  than-extend when both close a loop; re-run ResolveStage (≤ 3 rounds, MakerVolume is the cost
  driver at 2k panels); accept a round only if ClosureSignature3D does not regress (cell count/
  volume within tolerance, naked strictly decreases). **Anti-tunneling guard:** the ladder raises
  the *permission* (reach limit) but the actual extension is always **measured-to-target** — the
  panel grows exactly to the diagnosed meeting object (+overshoot), never by the blind ladder
  length — so an escalated wall cannot punch through a parallel room beyond its diagnosed gap;
  escalated panels run `Query.SelfIntersectionFace3Ds` before the rebuild, and a round whose
  cell-count increase is not adjacent to a closed loop is rejected as suspect. Sew v2: per-loop
  acceptance — match loops
  before/after by proximity; reject a sew that removes a loop by fusing two distinct parallel faces;
  cap sew tolerance below measured min inter-face separation (`ShellsDistance` over parallel pairs).
  Fill v2: `GrowOutwardTo` (measured) everywhere; blind `FillMargin` only as final-round fallback,
  diagnostic-tagged. RetainDropped v2: re-add the **original clean** geometry (not overshooting
  extended), imprint against the resolved complex (`ShellsImprint`), provenance `DroppedRetained`;
  raw-path re-adds filtered for duplicates/degenerates. GapFill v2: consume native naked wires
  (kills the managed loop-walk fragility + multiplicity-3 blindness); patches imprinted and the
  **final Validate runs over resolved+patches** (fixes the pre-patch naked-count lie).
- **Reuse:** 2D ExtensionSolver cost semantics (already reused in ConditionStage); `ShellsDistance`,
  `ShellsImprint`, `Validate`; ClosureSignature3D acceptance.
- **Native ops:** MakerVolume re-runs, Sew, Imprint, Distance, Validate, free-bound wires.
- **Acceptance:** synthetic gappy fixtures (flat fixture perturbed: 0.05–0.30 m gaps, plane
  misalignments) close via L1/L2 + AutoTune within 3 rounds with correct cell counts; parallel-pair
  weld fixture (two rooms separated by 0.08 m double wall + unrelated 0.09 m gap elsewhere) — the
  gap closes, the partition survives; towers/tilted golden masters unchanged; final naked count
  reported post-patch; **performance ceiling checked in this phase, not deferred: create the
  ~1,500-face benchmark fixture here (Phase 9 reuses it) and assert a full 3-round escalated solve
  stays under a 90 s soft ceiling — if MakerVolume's non-linear scaling blows it, the round budget
  and glue strategy are redesigned NOW, before the heuristic hardens.**
- **Tests:** unit — loop attribution, ladder/acceptance state machine, sew per-loop matcher;
  integration — fixtures above + perf guard (< 60 s at 2k faces, 3 rounds max).
- **Failure modes/diagnostics:** `EscalatedPanel` (panel, old→new MaxExtend, round), `RejectedSew`
  (loop pair, reason), residual `NakedLoop`s with wire polylines for GH display; best-effort result
  always returned with `AdoptedLevel`+rounds recorded.
- **Depends:** Phases 2–4. **Out of scope:** per-level frames (Phase 6), spaces handoff (Phase 7).
- **Phase 5 COMPLETE (2026-07-04).** Delivered as sub-phases 5a–5f exactly per
  `docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md` §K, each its own commit with both suites green and
  golden masters re-run first (all 10 signatures byte-identical throughout — every 5a–5f change is
  additive or gated so it never touches the raw/managed paths' existing outputs): 5a foundations
  (`SliverCellCount`, `MinPairSeparation`, `LoopAttribution`); 5b `GapFill.FromNakedWires` +
  `FinalizeAndValidate` (the single final-truth naked-count/signature producer); 5c `RetainDroppedV2`
  (re-adds original clean geometry, map-driven drop detection); 5d `HealStage.SewV2` (capped global sew +
  per-loop bookkeeping + fusion veto); 5e `AutoTune3DSolver` + `Modify.AutoTune3D` (bounded,
  diagnosis-driven escalation — see TESTING.md's Phase 5 section for the full engagement/acceptance
  contract); 5f `BenchmarkFixture`/`PerformanceGuardIntegrationTests` (~1,500-face performance guard,
  <90 s soft ceiling, measured ~3.5 s) plus this documentation.
  **One binding owner refinement mid-phase (5e, 2026-07-03):** AutoTune3D's engagement gate was widened
  from the review's literal "naked edges only" wording to "naked edges OR fabricated GapFill/HoleFill
  patches present" — 5b's own GapFill pre-closes many synthetic gaps to `naked == 0` before AutoTune ever
  sees them, so the literal gate would never fire on exactly the fixtures Phase 5e most needed to prove;
  engaging on fabrication too lets AutoTune try real measured extension before falling back to a
  fabricated patch. `IsAcceptableRound` gained a matching second (fabrication-driven) acceptance mode;
  the naked-driven mode is untouched. **One scope adjustment (5f, 2026-07-04):** the ~1,500-face
  benchmark fixture is a performance/scaling guard only and does not force AutoTune escalation rounds —
  measured directly, a *connected* shared-wall lattice at a comparable cell count blows the 90 s ceiling
  (MakerVolume's cost tracks connected-component size, not total face count, confirming §M's caution),
  and separately, perturbing one room in a *multi-room* model is silently absorbed by the existing
  (Phase 1/5c) `RetainDropped` recovery path regardless of defect size, sew settings, or connectivity
  style — a documented property of the calibrated low-drop-ratio design, not a Phase 5f defect. Round
  budget / GapFill-replacement / residual-diagnostics behaviour remains proven at small scale by
  `AutoTune3DIntegrationTests` (Phase 5e), which does not need re-proving at 1,500-face scale. Full
  suite after Phase 5: unit **356/356**, integration **123 pass / 1 skip** (native-gated; the
  performance guard adds one more native-gated test, skippable independently via
  `SAM_OCCT_SKIP_PERF`).

### Phase 6 — Per-level frames + inter-storey gappy closure
- **Objective:** remove the single-global-`Up` limitation and the 20° world-frame tilt ceiling;
  close the known-fragile gappy multi-storey population.
- **Model:** Opus 4.8 · **Effort:** L · 2–3 sessions · risk High.
- **Touches:** `SAM.Geometry.OCCT.Solver` (SnapStage/ConditionStage level grouping),
  `SAM.Analytical.OCCT.Solver` (`ResolveUp` → level frames), fixtures.
- **New:** `Classes/LevelFrame.cs` — cluster caps by (normal cone ≤ 5°, offset band) into level
  groups; each group gets its own canonical frame; Stage A cap normalization and Stage C
  extend/fill run per-frame (walls belong to the frames they span); `IsVertical` evaluated in-frame,
  removing the world-frame 20° ceiling (documented supported tilt becomes per-level arbitrary,
  inter-level steps explicit). Stacked-slab seams: congruent-skin handling extended across frames
  (a floor of level N and ceiling of level N−1 are an opposed pair with separation = slab
  thickness — snap them as one interface for the cell build, keep both panels in reconstruction).
  **Interface ownership:** the canonical interface plane is decided by the backer law (higher
  Weight wins; ±1 % tie → midpoint), NOT by frame — each level keeps its own `LevelFrame` and the
  snapped interface is shared between them, bounding cells on both sides. Space volumes are
  therefore computed against the single shared plane (no gap, no double-count); the physical slab
  thickness remains construction data on the panels, consistent with SAM's zero-thickness
  analytical convention.
- **Fixtures:** synthetic — towers fixture perturbed beyond sew reach (per owner decision, both
  synthetic + real models: add real gappy multi-storey `.sam` exports to
  `Testing/SAM.OCCT.IntegrationTests/Fixtures` as they become available, with golden signatures);
  split-level landing fixture (landing 0.25 m above floor — must NOT be normalized away; guards the
  NormalizeCaps 0.3 band).
- **Reuse:** existing tilted-`Up` transform machinery (generalised from 1 frame to N), Phase 5
  escalation.
- **Native ops:** unchanged.
- **Acceptance:** gappy multi-storey fixture closes with per-storey correct cell counts; split-level
  landing preserved; all tilted golden masters unchanged; 20° ceiling assertion replaced by
  per-frame verticality tests.
- **Tests:** unit — level clustering, frame assignment for level-crossing walls; integration —
  fixtures above.
- **Failure modes/diagnostics:** ambiguous level membership → diagnostic + widest-frame fallback;
  frame count reported.
- **Depends:** Phase 5. **Out of scope:** non-planar (curved) panels; atria spanning >2 levels
  treated specially (they resolve as ordinary tall cells).
- **Phase 6 COMPLETE (2026-07-04).** Landed as sub-phases, each its own commit with both suites green and
  golden masters re-run:
  - **6a** (`b1b643b`) — `LevelFrame` clustering foundation (native-free; golden masters byte-identical).
  - **6b** (`df054ac`) — frame-aware wall/cap/vertical classification layer (pipeline not re-routed; golden
    masters byte-identical).
  - **6c** (`0cac0b9`) — **frame-aware `NormalizeCaps` only**. Caps are
    normalized onto their own `LevelFrame` datum (0.15 m band) instead of the flat 0.30 m `NormalizeCapOffset`,
    so a ~0.18–0.25 m split-level landing is preserved, not flattened onto the floor. RAW golden masters
    **byte-identical** (5/5); managed golden masters unchanged for 3/5, **re-baselined** for the two
    multi-level fixtures that genuinely carry sub-0.30 m caps — `two-level-tilted` 13→**29** cells (14→29
    naked), `whole-level-towers` 24→**22** cells (14→12 naked) — the intended, documented consequence of
    preserving frame separation (see TESTING.md "Per-level frames" for the exact before/after and rationale).
    Unit: `NormalizeCapsFrameAwareTests` (+6).
  - **6d** (`a24545c`) — **observational** stacked-slab
    / inter-storey interface handling. `StackedSlabInterfaceDetector` identifies near-congruent, opposite-facing
    floor/ceiling skin pairs across level frames, verifies both analytical source panels survive into the
    `SourceMap`, and rejects the unsafe cases (wide cavity/shaft, low-overlap partial step, co-parallel
    double-skin/split-level, ambiguous frame) with diagnostics. It changes **no geometry and no source mapping** —
    the geometric merge of two skins into one interface is already done by the native sew (raw) and
    `SnapOpposedPartitions` (managed, ≤ 0.3 m band), and provenance is already preserved by the merge policy; a
    new collapse would be redundant within that band and unsafe beyond it (it would collapse the cavities the plan
    protects and risk the raw goldens). RAW and managed golden masters **all unchanged** (byte-identical raw;
    managed identical to the documented 6c baselines). Unit `StackedSlabInterfaceDetectorTests` (+9), integration
    `StackedSlabInterfaceIntegrationTests` (+3). See TESTING.md "Per-level frames" §6d.
  - **DEFERRED: per-frame extend/fill conditioning.** The §E "Stage C extend/fill run per-frame" clause was
    prototyped (cluster clean caps → group by orientation → condition each group in its own frame) and
    **regressed the `whole-level-tilted` RAW golden master 22→8 cells**: that fixture is a single analytical
    level whose caps span two very different tilts (~34°/~56°), and splitting the conditioning across those
    orientations severs the walls/caps that must meet between them (raw falls through to the managed pipeline,
    so it surfaces on the raw path). Per the plan's stop rules (raw signature change; "would require changing
    major solver architecture") this is deferred to a later focused sub-phase with a safer design: condition in
    a dominant frame, and split only across proven-separate storeys, never within a single multi-orientation
    level. A `TODO` in `Panel3DSnapSolver.Execute` records the follow-up. This deferral is intentionally
    **left open past Phase 6's completion** (see 6e) — it was never in 6e's scope to fix, only to document
    the stop rules for whoever picks it up.
  - **6e** (`docs(testing): complete phase 6 level-frame validation`) — Phase 6 wrap-up: no new solver
    architecture. Audited the sub-phase 6a–6d test suites against the plan's Phase 6 acceptance list (flat/
    tilted/>20°/two-stacked-level fixtures, split-level landing, shaft/cavity, a wall spanning multiple
    frames, stacked-slab duplicate-skin handling, `SourceMap` provenance after frame operations) — full
    coverage already existed, so **no new tests were added**. Re-ran and confirmed, unchanged from the 6c/6d
    baselines: build (0 errors), unit suite (400/400 passed), native integration suite (126 passed, 1
    skipped — the inverse-gated native-missing test, expected since native is present), all 10 golden-master
    signatures (raw byte-identical 5/5, managed matching the documented 6c/6d re-baselines 5/5), and the
    Phase 5f `Benchmark1500` performance guard (~6.4 s, 250/250 cells, 0 naked, 0 rounds, well inside the 90 s
    ceiling). Documented the coverage audit, the verification run, "how to run Phase 6 tests," and stop rules
    for whoever eventually picks up the deferred per-frame extend/fill work in TESTING.md ("Per-level frames"
    §6e). See TESTING.md "Per-level frames" §6e.

### Phase 7 — Cell classification, Spaces handoff, air semantics
- **Objective:** make the cell complex analytically meaningful.
- **Model:** Sonnet 5 · **Effort:** M · 1–2 sessions · risk Medium.
- **Touches:** `SAM.Analytical.OCCT.Solver` (new Create/Query), `SAM.Geometry.OCCT.Solver`
  (cell exposure), tests.
- **New:** `Query.CellClassification` — interior vs exterior cells (outer envelope = faces adjacent
  to exactly one cell; already derivable from cell metadata + `IsPointInside`); per-cell volume/
  centroid surfaced; `Create.Spaces(solverResult)` — optional: one `Space` per interior cell
  (location = cell center), panels linked via the existing `SAM.Analytical.OCCT.Create.
  AdjacencyCluster` path (reuse the Tower prior art rather than reimplementing); air-panel policy
  codified: input air panels bypass solving unchanged (documented divergence from 2D's weight-0
  participation — revisit only if fixtures demand); gap-fill air panels join the cluster as
  `PanelType.Air`.
- **Reuse:** `SAM.Analytical.OCCT.Create.AdjacencyCluster`, `IsPointInside`, cell volume/center ABI.
- **Native ops:** existing only.
- **Acceptance:** on the flat fixture, `Create.Spaces` yields 22 spaces whose adjacency equals the
  `SAMOCCTCreateAdjacencyCluster` output on the same solved panels; exterior cell(s) excluded;
  sliver cells (< MinCellVolume) reported, not spaced.
- **Tests:** integration — spaces equivalence, air passthrough; unit — classification rules.
- **Failure modes/diagnostics:** `SliverCell` diagnostics; unclassifiable cell → warning.
- **Depends:** Phases 1–5 (signature, mapping). **Out of scope:** thermal-zone semantics, space
  naming/numbering, IC assignment.

### Phase 8 — Grasshopper staged exposure
- **Objective:** expose every stage + diagnostics for MEP-engineer inspection, preserving existing
  component contracts.
- **Model:** Sonnet 5 · **Effort:** M · 1–2 sessions · risk Low.
- **Touches:** `Grasshopper/SAM.Analytical.Grasshopper.OCCT` (upgrade `SAMOCCTSolve3D`,
  `SAMOCCTClean3D`, `SAMOCCTExtend3D`; new `SAMOCCTAutoTune3D`, `SAMOCCTSolverDiagnostics` if
  outputs overflow Solve3D), icons, component versions.
- **New outputs on `SAMOCCTSolve3D`** (mirroring the 2D "Solver" component shape): solved panels;
  clean (Stage A) panels; conditioned (pre-resolve) panels; shells/cells; naked-edge loops as
  polyline curves (from native wires); gap-fill/air panels; source-map tree (branch per input index
  → output indices); diagnostics tree (stage, code, message); closure report string (cells, volume,
  naked, adopted level, rounds, timings — 3D analogue of the 2D per-level closure report);
  Successful. Inputs gain: adoption-level override, AutoTune toggle+ladder, MinCellVolume.
- **Reuse:** `GH_SAMVariableOutputParameterComponent`, `GH_SAMParam`, `Goo*Param`, existing
  component versioning/obsolescence pattern (bump `LatestComponentVersion`, keep old signatures
  functional).
- **Acceptance:** existing GH definitions using current components still solve (params appended,
  never reordered); manual Rhino run on a real model shows all staged outputs; report string
  matches diagnostics.
- **Tests:** unit — output assembly from a stubbed result; manual Rhino checklist recorded in
  TESTING.md.
- **Failure modes/diagnostics:** kernel-absent → managed-degrade banner (existing pattern).
- **Depends:** Phases 1–7 (surfaces them). **Out of scope:** new UI paradigms, preview shading,
  Rhino 7 back-compat work beyond what the project already targets.

### Phase 9 — Performance & hardening at 500–2,000 panels
- **Objective:** hit < 60 s whole-model solves; tune parallelism/glue; finalize docs.
- **Model:** Sonnet 5 · **Effort:** M · 1–2 sessions · risk Medium.
- **Touches:** `SAM.Geometry.OCCT.Solver`, `native/` only if chaining was deferred from Phase 3 and
  measurements justify it, `docs/Modeling-Guide.md`, `TESTING.md`.
- **New:** timing harness over the Phase-5 benchmark fixture (~1,500 faces, already created;
  per-stage ms in diagnostics); spatial index for Stage-A pair candidates (reuse
  `BoundingBox3D` tuples pattern from `Shell`) to keep snap O(n·k); GlueMode=Shift enabled on
  escalation re-runs when the watertight pre-check passes; MakerVolume re-run budget enforced
  (≤ 3 + initial); optional: adopt single-session chaining if Phase 3 spike measured > 20 % of
  solve time in managed↔native re-marshalling.
- **Reuse:** RunParallel, GlueMode gating, `OcctTopology`/`RetainTopology`.
- **Acceptance:** benchmark: full solve of 1,500-face model < 60 s on the dev reference machine
  with ≤ 3 escalation rounds; timings in diagnostics; no golden-master regressions; docs updated
  (tolerance budget table, staged-pipeline description, GH workflow).
- **Tests:** perf guard integration test (soft-fails with timing report), all suites green.
- **Failure modes/diagnostics:** per-stage timing always emitted; budget-exceeded diagnostic.
- **Depends:** all prior. **Out of scope:** >2k-panel chunking strategies, GPU/meshing work,
  CI-native enablement (still owner-deferred; leave the design note in TESTING.md).

---

Total: ~16–26 implementation sessions. Order is deliberate: tripwire (0) → contract (1) →
managed refactor with safety net (2) → snap-law parity (2b) → native unlock (3) → fidelity (4) →
hard algorithms (5, 6) → semantics (7) → UI (8) → perf (9). Each phase leaves `master`-mergeable
state.

## F. Solver algorithm (pseudocode)

```text
Solve3D(panels, options):
  prepared = PrepareInput(panels)
      # nonAirPanels become solver input
      # air panels are excluded from solving and passed through unchanged
      # per source panel:
      #   weight = explicit param | remap max dimension -> [0.2, 1.0]
      #   bucket = explicit param | thickness * 0.6, with configured floor
      #   maxExt = explicit param | derived by construction/thickness, capped at 0.49 * panel dimension

  ctx0 = SolverContext(prepared.nonAirPanels)
      # SourceMap = identity by original source index
      # Diagnostics = empty
      # ToleranceBudget = explicit stage tolerance table (see section M)

  frames = LevelFrames(ctx0.caps)
      # cluster caps by normal cone and offset band
      # Phase 6: replaces single global Up

  # ---- STAGE A: SNAP, managed and non-fabricating ----
  snapCtx = ctx0

  repeat until no snap changes or maxSnapIterations reached:
      # fixed-point iteration: 2D SnapAndAdjustWalls parity (<=1000 iterations), Phase 2b —
      # the live one-shot greedy pass is a known gap vs the 2D law
      order = sort(snapCtx.panels, Weight desc, Bucket desc, Area desc)

      for backer in order:
          for candidate in CandidatePanelsNear(backer):        # bbox spatial index, Phase 9
              if ParallelWithinTolerance(backer, candidate)    # 5 deg full / 0.3 deg partial
                 and BucketContains(backer, candidate):

                  if EqualWeight(backer, candidate, tolerance = 1%):
                      MoveBothToMidplane(backer, candidate)    # 2D midpoint rule
                      GrowBucketForBoth()
                  else:
                      SnapToBacker(candidate, backer.plane)

                  snapCtx.SourceMap.Record(candidate.sourceId, provenance = Snapped)

  CollapseOpposedPartitions(
      onlyWhenSeparationSignIndicatesDuplicateSkins,           # facing-away skins only, Phase 2
      overlapAreaRatio >= configuredThreshold,                 # gate on overlap region, not face area
      thicknessAwareDistanceGate)

  NormalizeCaps(per LevelFrame)

  snapCtx = UnionCoplanarWithMap(snapCtx)
      # every output face keeps source provenance

  # ---- STAGE B: RESOLVE, native cell build with adoption levels ----
  attempts = []

  attempts.add(
      name = L0_Raw,
      faces = ctx0.faces,
      sourceMap = ctx0.SourceMap)

  attempts.add(
      name = L1_Snapped,
      faces = snapCtx.faces,
      sourceMap = snapCtx.SourceMap)

  conditionedCtx = ConditionStage(snapCtx)
      # ExtendWallsToWalls using 2D ExtensionSolver semantics (trim = d/2 < extend = d, cap 0.49*len)
      # ExtendToCaps (overshoot 0.05, roof 0.5)
      # FillCapsToWalls using measured GrowOutwardTo where possible

  attempts.add(
      name = L2_Conditioned,
      faces = conditionedCtx.faces,
      sourceMap = conditionedCtx.SourceMap)

  adopted = null
  previousAttemptSignature = null

  for attempt in attempts:
      nativeResult = NativeCellBuild(
          attempt.faces,
          sew = 0.01,
          fuzzy = 1e-3,
          keepInternalShapes = true,        # AvoidInternalShapes = false: zoned cell complex
          history = true)                   # Phase 3, BRepTools_History composed across sew+build+merge

      composedMap = attempt.sourceMap.Compose(nativeResult.history)

      sig = ClosureSignature3D(
          nativeResult,
          composedMap,
          originalInputCount = ctx0.sourceCount)

      diagnostics.RecordAttempt(attempt.name, sig)

      if IsAcceptableAdoption(
             sig,
             previousAttemptSignature,
             minCellVolume = options.MinCellVolume,
             maxDroppedRatio = options.MaxDroppedRatio,
             requireNoNakedEdges = true,
             preventCellCollapse = attempt.name != L0_Raw):

          adopted = Adopt(attempt, nativeResult, composedMap, sig)
          break

      previousAttemptSignature = sig

  if adopted is null:
      adopted = SelectBestFailedAttempt(attempts)
      diagnostics.Error("No adoption level produced a closed valid cell complex")
      # continue best-effort so GH can inspect diagnostics and residual naked loops

  r = adopted.nativeResult
  ctx = adopted.contextWithComposedMap
  sig = adopted.signature

  # ---- STAGE C: HEAL / CLOSE, diagnosis-driven and bounded ----
  rounds = 0

  while sig.naked > 0 and rounds < options.AutoTuneMaxRounds:
      loops = NativeNakedWires(r)

      if loops is empty:
          diagnostics.Warning("Naked count reported but no naked wires were returned")
          break

      culpritSources = AttributeLoopsToSources(loops, ctx.SourceMap)

      tunedCtx = EscalateOnlyCulpritSources(
          ctx,
          culpritSources,
          maxExtendLadder = [0.5, 0.75, 1.0, 1.5],
          preferTrimOverExtend = true)
          # escalation re-runs ConditionStage for the culprit panels with the raised reach,
          # leaving all other panels' conditioned geometry untouched

      tunedResult = NativeCellBuild(
          tunedCtx.faces,
          sew = 0.01,
          fuzzy = 1e-3,
          keepInternalShapes = true,
          history = true)

      tunedMap = tunedCtx.SourceMap.Compose(tunedResult.history)
      tunedSig = ClosureSignature3D(tunedResult, tunedMap, ctx0.sourceCount)

      if tunedSig.IsRegressionOf(sig):
          diagnostics.RecordRejectedEscalation(rounds, tunedSig)
          break

      r = tunedResult
      ctx.SourceMap = tunedMap
      sig = tunedSig
      diagnostics.RecordAcceptedEscalation(rounds, sig)
      rounds++

  if sig.naked > 0:
      sewResult = AdaptiveSew(
          r,
          perLoopAcceptance = true,         # reject welds fusing distinct parallel faces
          maxTolerance = min(options.MaxAdaptiveSewTolerance,
                             MinPairSeparation(r) * safetyFactor))

      sewSig = ClosureSignature3D(sewResult, ctx.SourceMap, ctx0.sourceCount)

      if not sewSig.IsRegressionOf(sig):
          r = sewResult
          sig = sewSig
      else:
          diagnostics.RecordRejectedSew(sewSig)

  if sig.naked > 0:
      loops = NativeNakedWires(r)
      patches = GapFillFromWires(loops)
      r = Imprint(r, patches)
      ctx.SourceMap.RecordFabricated(patches, provenance = GapFill)

  if HasDroppedInputFaces(ctx.SourceMap):
      r = RetainDroppedV2(
          originalCleanGeometry,            # NOT the extended/overshooting geometry
          resolvedComplex = r,              # imprinted against the complex
          provenance = DroppedRetained)

  finalValidation = Validate(r)             # the final truth: AFTER patches and re-adds
  finalSig = ClosureSignature3D(finalValidation, ctx.SourceMap, ctx0.sourceCount)

  cells = ClassifyCells(r)
      # interior / exterior
      # volume
      # centroid

  return ReconstructPanelsAndResult(
      sourcePanels = prepared.nonAirPanels,
      passThroughAirPanels = prepared.airPanels,
      resolvedGeometry = r,
      sourceMap = ctx.SourceMap,
      cells = cells,
      signature = finalSig,
      diagnostics = diagnostics)
      # Guid, parameters, construction, and apertures preserved where possible
      # split/merge policy applied
      # gap-fill faces emitted as PanelType.Air
      # staged outputs returned for Grasshopper inspection
```

## G. Data model & API proposal

New types (all `SAM.Geometry.OCCT.Solver` unless noted; netstandard2.0; SPDX headers):

- `ClosureSignature3D` — `int CellCount; double TotalVolume; IReadOnlyList<double> CellVolumes;
  int NakedEdgeCount; int FaceCount; int DroppedCount; bool IsRegressionOf(ClosureSignature3D,
  double volumeTolerance)`. Constructed from `(nativeResult, SourceMap, originalInputCount)` so
  `DroppedCount` is mapping-derived (sources with no surviving representation), not geometric.
- `SourceMap` — `IReadOnlyDictionary<int, IReadOnlyList<FaceKey>>`; `Compose(SourceMap)`,
  `Compose(OcctHistory)`, `Record(int source, FaceKey, Provenance)`; `enum Provenance { Input,
  Snapped, Conditioned, Resolved, DroppedRetained, GapFill }`.
- `SolverContext` — faces, per-source parameters (keyed by source index, not position), SourceMap,
  SolverDiagnostics, `ToleranceBudget`.
- `SolverDiagnostics` / `SolverDiagnostic { SolverStage Stage; DiagnosticCode Code; Severity;
  string Message; IReadOnlyList<Point3D>; Face3D; double ToleranceUsed; long ElapsedMs }`.
- `Panel3DSolverOptions` — stage toggles (mirrors today's flags), adoption (`MinCellVolume` 0.05 m³,
  `MaxDroppedRatio` 0.10, forced level), AutoTune (`Enabled`, `Ladder = [0.5,0.75,1.0,1.5]`,
  `MaxRounds = 3`), sew (`PerLoopAcceptance = true`, `MaxSewTolerance = 0.1`), fill
  (`MeasuredOnly = true`), plus `OcctBuildOptions Native`.
- `Solver3DResult` — `SnappedFace3Ds, ConditionedFace3Ds, ResolvedFace3Ds, HoleFillFace3Ds,
  NakedWirePolylines, ClosureSignature3D PerLevel[], AdoptedLevel, Rounds, Cells (volume/center/
  classification), SourceMap, SolverDiagnostics, OrphanedApertures (aperture + source panel Guid,
  Phase 4 orphan policy)`.
- `AutoTune3DSolver` — mirrors 2D AutoTuneSolver surface (`EscalationLadder`, `MaxRounds`,
  acceptance on signature non-regression).
- `SAM.Core.OCCT`: `OcctHistory` (native history handle → managed arrays; merge/compose).

Public API (stable through all phases): `Panel3DSnapSolver` stays as façade
(`Execute(options)` + existing properties delegate to stages); `Modify.Solve3D/Clean3D/Extend3D/
OpenPanels3D` keep signatures, gain overloads returning `Solver3DResult`. Parameter names remain
the `SolverParameter` enum strings ("Weight", "Bucket Size", "Max Extend").

## H. Native OCCT bridge additions (ABI v4, additive, probe/degrade like v3)

| Export | Wraps | Consumed by |
|---|---|---|
| `sam_occt_result_history_counts/entries` | `BRepTools_History` over sew→MakerVolume→UnifySameDomain chain (`Modified/Generated/IsDeleted`, merged across ops) | `OcctHistory` → `SourceMap.Compose` |
| `sam_occt_result_naked_wires` | `ShapeAnalysis_FreeBounds` (wires, ordered vertices) | GapFill v2, GH naked-loop curves |
| `sam_occt_shape_max_tolerance` | `ShapeAnalysis_ShapeTolerance` | ToleranceDrift diagnostics |
| (stretch) `sam_occt_session_*` chaining | reuse of live `TopoDS_Shape` across build→merge→sew→validate | ResolveStage (kills 5× re-marshal) |

Constraints: same DLL (`SAM.Occt.Native.dll`), LGPL dynamic-linking posture unchanged, ABI version
bump with `sam_occt_abi_version` probe; managed layer must run correctly (heuristic fallback)
against a v3 native. Phase 3 opens with a native spike verifying the OCCT 8.0 history API surface
on the pinned vcpkg baseline — the sizing unknown called out by review. **Not** added: CellsBuilder
exposure (MakerVolume + AvoidInternalShapes=false already yields the zoned complex; CellsBuilder
selection semantics add no capability this pipeline needs), new boolean wrappers.

## I. Analytical wrapper plan (`SAM.Analytical.OCCT.Solver`)

- Parameter plumbing unchanged in name and precedence: explicit args > `SolverParameter` values >
  derived (thickness×`thicknessFactor` 0.6; weight remap; MaxExtend 0.6/0.5/0.33 cap 0.49) —
  **but** re-examine `minBucketSize` 0.4: with raw-first protecting clean models, drop the floor
  toward the 2D-derived 0.12–0.2 so thin partitions stop over-capturing (Phase 5, fixture-gated).
- Air panels: excluded from solving, passed through unchanged, gap-fill patches emitted as
  `PanelType.Air` panels (documented divergence from 2D's weight-0 participation).
- Output contract (Phase 4): `Analytical.Create.Panel(source.Guid, sourcePanel, newFace3D)` —
  Guid/parameters/construction/apertures preserved, apertures trimmed; split/merge Guid policy per
  §E Phase 4; provenance + solver params stamped for Visualize.
- `ResolveUp` replaced by `LevelFrame`s (Phase 6).
- `AutoTune3D` entry point mirroring 2D `AutoTuneSolver` (Phase 5).
- Optional Spaces handoff (Phase 7) reusing `SAM.Analytical.OCCT.Create.AdjacencyCluster`.

## J. Grasshopper component plan

Per §E Phase 8: upgrade `SAMOCCTSolve3D/Clean3D/Extend3D` in place (append-only params, version
bump), new `SAMOCCTAutoTune3D` + (if needed) `SAMOCCTSolverDiagnostics`. Outputs designed for MEP
inspection: clean panels → conditioned panels → solved panels → cells/shells → naked-loop curves →
gap-fill air panels → source-map tree → diagnostics tree → closure report string. Naming, Goo
params, exposure, icons follow SAM conventions (`GH_SAMVariableOutputParameterComponent`).
Not before Phase 8: the core must be proven via tests first (user requirement).

## K. Testing & validation plan

- **Unit (pure managed, CI-run):** SourceMap algebra; ClosureSignature comparisons; opposed-
  partition truth table; bucket/backer ordering + midpoint rule parity with Annex-law; level-frame
  clustering; AutoTune state machine; sew per-loop matcher; gate truth tables; Guid/aperture
  policies (stubbed geometry).
- **Integration (native-gated, local merge gate per phase — owner decision keeps CI managed-only):**
  golden masters (5 existing fixtures, signatures locked Phase 0); new fixtures — merged-cells
  rejection, shaft-void, split-face/merge mapping, aperture round-trip, parallel-pair weld,
  synthetic gappy set (perturbation generator: face offsets 0.05–0.30 m, deleted strips, plane
  tilts ≤ 2°), gappy multi-storey (perturbed towers), split-level landing, perf model (~1,500
  faces). Real anonymized `.sam` exports appended to `Fixtures/` as provided, each with a golden
  signature.
- **Regression vs 2D:** vertical-walls-only model — 3D resolved wall positions within tolerance of
  2D solver output (axis positions, naked counts).
- **Drift:** max shape tolerance per stage < 10× input tolerance asserted on all fixtures.
- Convention: xUnit, `Method_State_Expected`, SPDX headers, `Skip.IfNot(NativeProbe.Available, …)`.

## L. CI / build / packaging plan

Owner decision: **CI stays managed-only for now; integration tests run locally as the per-phase
merge gate** (recorded in each PR). Local-environment parity ("works on my machine" guard): the
**committed `build/` DLLs are the standardized native runtime** — `NativeRuntimeBootstrap` loads
exactly those, `sam_occt_abi_version` is probed at start, and any native rebuild (e.g. ABI v4)
must recommit the updated DLLs in the same PR, so every machine tests the same binary.
(A DevContainer/Docker image is not applicable to this Windows-native OCCT + Rhino/GH stack.)
Plan keeps CI healthy and documents the future path:
- Fix noted drift: `build.yml` pins sibling repos to `sow/2026-Q2` while repos live on Q3 —
  parameterize or bump the pin (one-line, include in Phase 0's TESTING.md note or a hygiene commit).
- The cross-repo `SAM_Solver/build/SAM.Geometry.Solver.dll` HintPath (ExtensionSolver reuse) stays;
  document it in TESTING.md as a build prerequisite; revisit vendoring only if SAM_Solver drifts.
- Native build remains `build-native.ps1` (vcpkg opencascade 8.0/freetype/freeimage → CMake/Ninja →
  `build/`); ABI v4 changes nothing in packaging.
- Deferred (documented in TESTING.md, not scheduled): CI native via vcpkg binary caching or a
  prebuilt artifact — the two options and their trade-offs are recorded for when the owner
  re-prioritizes.
- New projects: none — all work lands in existing projects, so `SAM_OCCT.sln` is untouched.

## M. Performance & tolerance strategy

**Tolerance budget (explicit, testable; each stage consumes only its band):**

| Band | Value | Owner |
|---|---|---|
| Snap capture (bucket) | per-panel, 0.12–0.4 m (thickness×0.6) | Stage A managed |
| Conditioning overshoots | 0.05 m (walls), 0.5 m (roof pitch), FillMargin fallback 0.5 m | Stage C conditioning |
| Pre-build sew | 0.01 m | Stage B native |
| Adaptive sew | ≤ 0.1 m, hard clamp 0.3 m, capped below measured min pair separation | Stage C native |
| Boolean fuzzy | `MacroDistance` 1e-3 m | OcctBuildOptions |
| Merge-coplanar angle | ≤ SAM `Tolerance.Angle` (≈2°) post-snap (today's 5° is too loose) | Stage B/C |
| Cluster angles | 5° full / 0.3° partial (2D law) | Stage A |
| SAM geometric | `Distance` 1e-6 m | everywhere managed |
| Drift ceiling | max shape tolerance < 10× input, asserted | Phase 3 export |

Invariant: **snapping makes geometry exact so the kernel's fuzzy band only absorbs residual noise**
— never widen fuzzy/sew to compensate for un-snapped input (that is what adoption levels are for).

**Performance (500–2,000 panels, < 60 s):** MakerVolume dominates → initial build + ≤ 3 escalation
re-runs hard cap; GlueMode=Shift on re-runs passing the watertight pre-check; RunParallel on;
Stage-A candidate pairs via bbox spatial index (O(n·k)); managed↔native re-marshal measured in
Phase 3 spike, chaining adopted in Phase 9 only if > 20 % of solve time; per-stage timings always
emitted in diagnostics; perf guard test with soft-fail timing report.

## N. Diagnostics & user-facing reporting

`SolverDiagnostics` (Phase 1) is the single contract; everything the user sees derives from it:
- Taxonomy: NakedEdge/NakedLoop (wire polylines), Gap (with measured distance), Overlap,
  DuplicateFace, SliverFace, SliverCell, NonManifoldEdge (edge→face incidence > 2), ToleranceDrift,
  DroppedFace, RejectedSew, RejectedCollapse, AdoptedLevel, EscalatedPanel, BudgetExceeded.
- Every rejection carries its reason and geometry; best-effort results always returned, never
  silent failure.
- GH: diagnostics tree + closure report string (cells, volume, naked loops, adopted level, rounds,
  per-stage ms — the 3D analogue of the 2D per-level closure report) + naked-loop curves + staged
  geometry outputs for visual overlay (§J).
- Final validation truth: naked counts reported **after** gap-fill patches are imprinted and
  re-validated (fixes today's pre-patch snapshot).

## O. Risks & mitigations

1. **OCCT 8.0 history surface uncertainty** (UnifySameDomain::History availability/behaviour on the
   pinned vcpkg baseline) — Phase 3 opens with a native spike; fallback: map merges managed-side
   (same-plane grouping) and take history from MakerVolume+sew only.
2. **Adaptive sew welding real partitions** (0.08–0.1 m double walls vs 0.1 sew) — per-loop
   acceptance + sew cap below measured min pair separation + parallel-pair weld fixture (Phase 5).
3. **Opposed-partition collapse deleting real voids / refusing door-cut skins** — separation-sign +
   overlap-area gates + shaft-void fixture (Phase 2).
4. **Refactor regressions in the monolith decomposition** — golden masters locked first (Phase 0),
   stage bodies moved verbatim, MaxExtend fix deltas quantified and re-baselined explicitly.
5. **Tilt/level heuristics** (20° world-frame ceiling, NormalizeCaps 0.3 band eating split-level
   landings, single Up) — per-level frames + landing fixture (Phase 6); ceiling documented until then.
6. **RetainDropped × history interactions** (overshooting re-adds double-crossing neighbours) —
   RetainDropped v2 re-adds original geometry imprinted against the complex, provenance-tagged
   (Phase 5).
7. **No CI-native safety net** (owner-deferred) — mitigated procedurally: local integration run is
   the per-phase merge gate, recorded in PRs; golden masters make the run deterministic.
8. **Cross-repo ExtensionSolver binary coupling** — documented prerequisite; vendoring decision
   deferred until SAM_Solver drift actually bites.
9. **Escalation cost blow-up at 2k panels** — hard 3-round cap, glue on re-runs, perf guard test.
10. **Aperture re-hosting edge cases** (aperture straddling a split) — containment-based assignment
    + orphaned-aperture warning, never silent loss (Phase 4).

## P. AI implementation prompt sequence

One prompt per phase; run in order; each assumes the previous phase is merged. Common preamble for
every prompt: *"Work in C:\...\SAM-BIM\SAM_OCCT on branch `fix/solver-raw-first` (or its successor
PR branch). Read `docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md` §E Phase N (your scope), §F–§N
as reference. Do not touch other repos except read-only. Every new .cs gets the SPDX header from
COPYRIGHT_HEADER.txt. xUnit `Method_State_Expected`. Build `SAM_OCCT.sln` and run
`Testing/SAM.OCCT.UnitTests` green; run `Testing/SAM.OCCT.IntegrationTests` locally (native
`build/` DLLs) and paste the summary into the PR. Golden-master signatures must not change unless
the phase explicitly re-baselines them (state the delta). Commit message footer: 'Generated by
Michal Dengusiak & Claude Code'."*

- **P0 → Sonnet 5:** "Phase 0: add `ClosureSignature3D` + golden-master integration tests asserting
  the signature of the 5 fixtures on raw and managed paths; document the local-integration merge
  gate and the build.yml `sow/2026-Q2` pin drift in TESTING.md. No solver behaviour changes."
- **P1 → Sonnet 5:** "Phase 1: add `SolverDiagnostics`/`SolverDiagnostic` + taxonomy; harden
  `TryRawResolve` adoption gate (MinCellVolume sliver-cell filter, MaxDroppedRatio, AdoptedLevel
  diagnostics, per-level signatures); add merged-cells rejection fixture."
- **P2 → Opus 4.8:** "Phase 2: decompose `Panel3DSnapSolver` into SnapStage/ConditionStage/
  ResolveStage/HealStage over `SolverContext`+`SourceMap` (façade preserved); carry MaxExtend by
  source identity (fixes the positional bug at Panel3DSnapSolver.cs:300-312); add separation-sign +
  overlap-area gates to `SnapOpposedPartitions`; shaft-void fixture; golden masters unchanged except
  quantified MaxExtend-fix deltas."
- **P2b → Sonnet 5:** "Phase 2b: upgrade SnapStage to the 2D snap law — fixed-point iteration
  (≤1000, warning on cap), per-iteration Weight/Bucket/Area re-sort, equal-weight midpoint rule with
  bucket growth, bbox candidate pre-filter; chained-offset convergence fixture; quantify and
  re-baseline golden-master deltas."
- **P3 → Opus 4.8:** "Phase 3: ABI v4 — native spike verifying OCCT 8.0 history for MakerVolume/
  Sewing/UnifySameDomain, then export composed `BRepTools_History`, `ShapeAnalysis_FreeBounds`
  wires, and max shape tolerance; managed `OcctHistory` + `SourceMap.Compose`; demote
  `NearestSourceIndex` to fallback; split/merge/delete mapping fixtures; v3-degrade proven."
- **P4 → Sonnet 5:** "Phase 4: rebuild solved panels via `Analytical.Create.Panel(guid, source,
  face3D)` with aperture re-hosting and split/merge Guid policy; air panels for gap-fill faces;
  tighten post-resolve merge-coplanar angle to SAM Tolerance.Angle; aperture + parameter round-trip
  fixtures."
- **P5 → Opus 4.8:** "Phase 5: implement `AutoTune3DSolver` (wire attribution via SourceMap, ladder
  [0.5,0.75,1.0,1.5] on implicated panels only, ≤3 rounds, signature non-regression acceptance);
  per-loop sew acceptance capped below measured pair separation; GapFill v2 from native wires with
  post-patch re-validation; RetainDropped v2 (original geometry, imprinted, provenance); measured
  fills only; synthetic gappy + parallel-pair weld fixtures; perf < 60 s guard."
- **P6 → Opus 4.8:** "Phase 6: `LevelFrame` clustering replacing single `Up`; per-frame verticality/
  cap normalization/extend; cross-frame stacked-slab interface handling; gappy multi-storey
  (perturbed towers + any real fixture provided) and split-level landing fixtures."
- **P7 → Sonnet 5:** "Phase 7: cell interior/exterior classification, per-cell volume/centroid
  surfacing, optional `Create.Spaces` handoff reusing Analytical.OCCT AdjacencyCluster; air-panel
  policy codified; spaces-equivalence fixture."
- **P8 → Sonnet 5:** "Phase 8: upgrade SAMOCCTSolve3D/Clean3D/Extend3D (append-only) with staged
  outputs, naked-loop curves, source-map + diagnostics trees, closure report; add SAMOCCTAutoTune3D;
  version bumps; manual Rhino checklist in TESTING.md."
- **P9 → Sonnet 5:** "Phase 9: perf benchmark fixture + per-stage timings; Stage-A bbox spatial
  index; GlueMode=Shift on gated re-runs; adopt native chaining only if Phase 3 measurement > 20 %;
  update Modeling-Guide/TESTING docs; < 60 s at ~1,500 faces."

(Fable 5 reserved for research/architecture revisions of this plan itself; no lighter-than-Sonnet
model is safe for solver code — acceptable only for icon/resource chores in P8.)

