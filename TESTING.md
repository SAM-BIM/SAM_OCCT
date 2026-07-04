# Testing

SAM_OCCT follows a **two-tier test method** built on **xUnit**. The split exists
because the library straddles a managed/native boundary: most logic is pure C#
and testable everywhere, while the actual geometry kernel work happens in the
C++/OpenCASCADE layer (`SAM.Occt.Native`).

## Layout

| Project | Purpose | Native needed? |
| --- | --- | --- |
| `Testing/SAM.OCCT.UnitTests` | Pure-managed logic that runs identically anywhere: `OcctBuildOptions`, `OcctDiagnostic`, the `OcctNativeInputBuilder` serializer, and the `Query` input guard clauses (which return before any native call). | No |
| `Testing/SAM.OCCT.IntegrationTests` | Real OCCT boolean / cell-complex operations (cells, volumes, face adjacencies), **and** the graceful *native-missing* contract. | Gated on native availability |

Anything whose outcome depends on whether the native library is loadable lives in
the integration project, never in the unit project — so the unit suite cannot be
broken by the host's DLL search state. The native-missing test is *inverse-gated*
(`Skip.If(NativeProbe.Available)`): it runs where the library is absent (e.g. CI)
and skips where it is present, while the boolean-op tests do the opposite.

Common test settings (target framework, xUnit, coverage collector) live in
`Testing/Directory.Build.props` so both projects stay consistent. This is scoped
to `Testing/` and does not affect production builds.

## Conventions

- **Framework:** xUnit (`[Fact]` / `[Theory]` + `[InlineData]`).
- **Naming:** `Method_StateUnderTest_ExpectedBehaviour`
  (e.g. `ToString_WithSourceIndex_FormatsCodeIndexMessage`).
- **Structure:** Arrange / Act / Assert, with comments marking each phase.
- **Assertions:** assert on observable contract — return values and the
  `OcctDiagnostic` codes/severities the production code emits
  (e.g. `SAM_OCCT_INPUT_EMPTY`, `SAM_OCCT_NATIVE_MISSING`) — not on internal
  intermediate state.
- **SPDX:** every `.cs` file (tests included) starts with the header from
  `COPYRIGHT_HEADER.txt`; `spdx-check.yml` enforces this on PRs.
- **Internals:** `SAM.Geometry.OCCT` exposes its internal helpers to
  `SAM.OCCT.UnitTests` via `InternalsVisibleTo` so the input serializer can be
  tested directly.

## Native-gated integration tests

Integration tests use `Xunit.SkippableFact`. `NativeProbe` runs a tiny union
once and inspects `OcctCellComplexResult.NativeAvailable`; each test calls
`Skip.IfNot(NativeProbe.Available, ...)`. When `SAM.Occt.Native` is missing the
tests are reported **skipped**, never failed.

`build-native.ps1` writes `SAM.Occt.Native.dll` (and the OCCT/vcpkg runtime DLLs
it depends on) into `<repoRoot>/build`, not into the testhost's own bin output.
`NativeRuntimeBootstrap` (a module initializer in the integration test assembly)
searches upward from the testhost's base directory for that folder and, if
found, prepends it to `PATH` so the P/Invoke loader can resolve the library and
its colocated dependencies — without copying any files (so it can never shadow
the test project's own managed assemblies). This runs automatically; no manual
setup is required once the native library has been built.

## Running locally

Prerequisite: the sibling **SAM** repo must be built so the referenced
`SAM.Core` / `SAM.Geometry` / `SAM.Analytical` DLLs exist at `..\SAM\build`
(this is the same dependency the normal build needs).

```powershell
# Unit tests (no native library required)
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj

# Integration tests — build the native layer first, then run
.\build-native.ps1 -SkipVcpkgInstall
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj
```

If the native library is not on the load path, the integration tests skip and
the unit tests still validate all managed behaviour.

## Continuous integration

`.github/workflows/build.yml` builds the solution (including sibling SAM
dependencies) and then runs both test projects with `dotnet test`. Unit tests
must pass; integration tests skip without the native library. Test results
(`.trx`) and Cobertura coverage are uploaded as the `SAM_OCCT-test-results`
artifact. Coverage is **report-only** — there is no hard threshold gate yet.

## Persistent topology handles (issue #14, native ABI v2)

`OcctTopology : SafeHandle` wraps a live native `TopoDS_Shape` (`sam_occt_shape`
handle) so operations chain natively (`Query.TopologyUnion` /
`TopologyDifference` / `TopologyIntersection` / `TopologyRepair`,
`Query.IsPointInside`) and decode on demand via `Query.CellComplexResult`.
Contract:

- **Ownership** - every API returning an `OcctTopology` transfers ownership to
  the caller, who should `Dispose` it (the finalizer is only a backstop, and
  `Dispose` is idempotent). Operations never consume their inputs.
- **Retention** - `OcctBuildOptions.RetainTopology = true` makes the
  `Query.Shells*` operations attach the live handle to
  `OcctCellComplexResult.Topology`; the result then owns it and is
  `IDisposable`. The default (`false`) keeps the legacy decode-and-free path
  and `Topology` stays null.
- **Threading** - create, operate and decode on a single thread; only
  `Dispose`/finalization may occur on another thread.
- **Topology keys** are only comparable within one decoded result.
- **ABI probe** - `OcctCellComplexResult.NativeVersion` reports
  `sam_occt_abi_version` ("4" as of Phase 3; "3" adds validation/glue, "2" adds
  the shape handle); a stale native build degrades to diagnostics
  (`SAM_OCCT_NATIVE_ENTRYPOINT_MISSING`) instead of crashing.

## Validation, watertightness diagnostics & BOP glue (issue #37 follow-on, native ABI v3)

ABI v3 adds two co-operating features (validation must precede glue):

- **Native validation** - `Query.Validate(shells / faces / topology, out OcctValidationReport, out result)`
  runs `BRepCheck_Analyzer` + `ShapeAnalysis_FreeBounds` (+ an optional
  `BOPAlgo_ArgumentAnalyzer` self-intersection test) and returns
  `IsValid` / `IsWatertight` plus the located, categorised
  `OcctValidationIssue`s (naked edge with XYZ + gap length, self-intersection,
  small/invalid face). It is the teeth behind `OcctBuildOptions.ValidateInput`:
  on a hard `Create.Shells` close failure the input is sewn and validated so the
  diagnostics carry kernel-grade locations, falling back to the pure-managed
  `OcctOpenShellAnalysis` naked-edge analysis when the native validator is
  unavailable.
- **BOP glue** - `OcctBuildOptions.GlueMode` (`Off` / `Shift` / `Full`, default
  `Off`) routes `Create.Shells` through the glue-aware `_ex` cell-complex builder
  (`BOPAlgo_MakerVolume::SetGlue`). Glue is a throughput win on cell complexes
  with many coincident shared walls but corrupts merely-near-coincident faces,
  so it is applied **only** when the watertightness gate reports no gaps (naked
  edges); non-manifold shared-wall edges - exactly what glue accelerates - do not
  block it. Glue also degrades to the glue-off path (with a
  `SAM_OCCT_GLUE_SKIPPED` / `SAM_OCCT_GLUE_DEGRADED` warning) when the input is
  not gap-free or the native build predates ABI v3.

Guard / mapping / report logic is unit-tested (`ValidateGuardTests`,
`WatertightnessSummaryTests`, `OcctBuildOptionsTests`); the real kernel paths are
native-gated integration tests (`ValidateIntegrationTests`, `GlueIntegrationTests`).

Guard tests live in `OcctTopologyGuardTests` (unit); lifecycle, parity,
chaining and point-in-solid coverage lives in `OcctTopologyIntegrationTests`
and `TopologyChainingIntegrationTests` (integration, native-gated). When the
native ABI changes, rebuild with `.\build-native.ps1` and run the full
integration suite locally - CI does not build the native layer.

## Golden-master closure signatures (true-3D panel solver plan, Phase 0)

`docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md` phases the evolution of the
true-3D panel solver (`Panel3DSnapSolver` / `Modify.Solve3D`). Phase 0 froze
the solver's *current* closure behaviour as a machine-checked baseline before
any of the later phases touch it:

- `SAM.Geometry.OCCT.Solver.ClosureSignature3D` - a comparable fingerprint of
  a resolved cell complex (cell count, per-cell/total volume, naked-edge
  count, face count, dropped-source count) and `IsRegressionOf(other,
  volumeTolerance)`, which later phases use to gate whether a pipeline change
  (an adopted resolve level, an escalation round, a re-sew) is kept. Unit
  tests: `Testing/SAM.OCCT.UnitTests/ClosureSignature3DTests.cs`.
  `DroppedCount` is a plain caller-supplied count in this phase - the plan's
  mapping-derived definition (via a `SourceMap`) arrives with source mapping
  in Phase 2/3.
- `Testing/SAM.OCCT.IntegrationTests/GoldenMasterIntegrationTests.cs` locks
  the signature of the five reference fixtures
  (`whole-level-flat.sam`, `tilted-two-spaces.sam`, `whole-level-tilted.sam`,
  `two-level-tilted.sam`, `whole-level-towers.sam`) on two paths:
  - **Raw path** (`Solve3D_RawPath_ClosureSignatureMatchesGoldenMaster`) -
    today's default behaviour (raw-first adoption). Asserts the exact
    cell/naked-edge targets from the plan (22/0, 2/0, 22/0, ≥40/0, ≥31/0) -
    these already had individual coverage in `FlatSolveIntegrationTests` /
    `TiltedSolveIntegrationTests`; this test additionally locks total volume
    via `ClosureSignature3D` output logged to the test console.
  - **Managed path** (`Solve3D_ManagedPath_ClosureSignatureMatchesGoldenMaster`,
    renamed from `Solve3D_ManagedPath_ClosureSignatureIsRecorded` in the Phase
    7-pre cleanup below) -
    the pre-raw-first clean/extend/resolve pipeline, forced via
    `Panel3DSnapSolver.ForceManagedPipeline` (threaded through
    `Modify.Solve3D`'s `forceManagedPipeline` parameter). Both default to
    `false` and change no existing behaviour; setting either skips only the
    raw-first shortcut and always runs the managed pipeline, which is known
    (see plan §A) to under-close some of these exact fixtures relative to
    the raw path. Phase 0 recorded today's number as a baseline without
    asserting it; Phase 7-pre machine-pins it to the Phase 6c/6d documented
    values (see "Phase 7-pre" below).

**Local-integration merge gate.** Every phase in the plan (0-9) is merged
under the same protocol already documented above: `dotnet test` on
`SAM.OCCT.UnitTests` must be green, and `SAM.OCCT.IntegrationTests` must be
run locally against the committed `build/` native DLLs, with the pass/skip
summary pasted into the PR. A phase's golden-master values must not change
unless that phase explicitly states the delta (which fixture, which number,
why) in its PR description - an unexplained change to these numbers is a
regression, not a refactor.

## Diagnostics contract + raw-adoption gate hardening (true-3D panel solver plan, Phase 1)

Phase 1 gives every later phase a single place to record machine-readable solve events, and closes
a real "watertight-but-wrong" hole in the raw-first adoption gate:

- `SAM.Geometry.OCCT.Solver.SolverDiagnostic`/`SolverDiagnostics` - stage (`SolverStage`: Snap/
  Resolve/Heal), taxonomy code (`DiagnosticCode` - the full §N list; Phase 1 emits `NakedEdge`,
  `SliverCell`, `DroppedFace`, `AdoptedLevel`, the rest reserved for later phases), severity,
  message, and optional geometry/tolerance/timing. `Panel3DSnapSolver.Diagnostics` accumulates these
  across a solve (reset each `Execute()`); `Modify.Solve3D` surfaces them as
  `SAM_OCCT_SOLVE3D_DIAGNOSTIC:` lines alongside its existing summary diagnostics.
- `Panel3DSnapSolver.EvaluateRawAdoption` - the raw-first (L0) adoption gate's decision rule, pure
  and native-free (unit-tested in `RawAdoptionGateTests.cs`). Precedence: no cells formed -> naked
  edges present -> a sliver cell (`MinCellVolume`, default 0.05 m3) -> too many dropped input faces
  (`MaxDroppedRatio`). The last two close the gap the original `cells >= 1 && naked == 0` gate left
  open: a watertight envelope that is nonetheless wrong (e.g. two rooms silently merged because
  their dividing partition doesn't reach a cap and so bounds no closed cell) used to be adopted
  outright.
- `Panel3DSnapSolver.Signature` / `RawAttemptSignature` - the `ClosureSignature3D` (Phase 0) of the
  adopted result, and of the raw attempt whether or not it was adopted, respectively.

**Calibration finding (why `MaxDroppedRatio` defaults to 0.30, not the plan's originally-proposed
0.10):** measured directly against the 5 golden-master fixtures, a correctly-adopted, watertight raw
solve naturally drops 0-25% of its input faces (`whole-level-flat` ~11%, `whole-level-towers` ~11%,
`two-level-tilted` ~8%, `tilted-two-spaces` ~25%, `whole-level-tilted` ~0%) - this is the existing
`RetainDropped` recovery path working as intended (e.g. one of a back-to-back partition's two
coincident room-facing skins gets absorbed into the other during the coplanar merge), not a defect.
At the plan's proposed 0.10 default, 3 of the 5 real fixtures were rejected outright and fell through
to the (historically weaker) managed pipeline - a golden-master regression the phase's own acceptance
bar ("golden masters unchanged") forbids. The default is calibrated to 0.30, comfortably above the
observed 25% ceiling; `RawGateMergedCellsIntegrationTests.cs` demonstrates the plan's original 0.10
threshold is still meaningful as an explicit, tighter override on a small/targeted model (its
synthetic fixture, a box with an undersized partition, naturally sits at ~14% dropped - enough to
trip 0.10 but not 0.30). This is exactly the empirical-recalibration methodology §A of the plan
documents: the roadmap's suggested numbers are a starting point, verified (and corrected, with the
delta recorded here) against the real fixtures before being adopted as defaults.

**New fixture:** `RawGateMergedCellsIntegrationTests.cs` builds its "two rooms with a deleted
partition, watertight outer shell" fixture synthetically (no new `.sam` file) - a 4x4x3 m box with a
partition that stops 0.5 m short of the ceiling. It asserts both directions: the raw gate rejects
the merge with an explicit `MaxDroppedRatio = 0.10` (and the managed fallback then correctly
separates the two rooms), and - as the calibration sanity check - still adopts the same merge at the
real-world default (0.30), demonstrating why that default cannot be tightened globally without
losing the 5 real fixtures.

## Stage decomposition + source mapping (true-3D panel solver plan, Phase 2)

Phase 2 breaks the 1449-line `Panel3DSnapSolver` monolith into explicit stages and threads source
provenance through the managed pipeline, fixing two live defects along the way:

- **Stages.** `SnapStage` (Stage A clean bucket + attribution), `ConditionStage` (extend/fill),
  `ResolveStage` (native MakerVolume + merges + adaptive sew + validate + gap-fill), `HealStage`
  (RetainDropped). `Panel3DSnapSolver.Execute` is now a façade orchestrating these over a
  `SolverContext` (input faces + per-source parameters keyed by **source identity**, `SourceMap`,
  `SolverDiagnostics`, `ToleranceBudget`). `CleanBucket` and the old `Resolve`/`NakedEdgeCount`
  bodies were moved verbatim into the stages; `CleanBucket` stays as a public geometry-only entry
  point delegating to `SnapStage`. (Sew + gap-fill stay inside `ResolveStage` in Phase 2 - they are
  intertwined with the native resolve/validate; Phase 5 moves them into `HealStage` when it reworks
  them with per-loop acceptance.)
- **`SourceMap` + `Provenance`** (`SourceMapTests.cs`): source index → output `FaceKey`s, with
  `Record`/`RecordMerge`/`RecordSplit`/`RecordFabricated` and a `Compose` that chains two stages.
  Managed-only in Phase 2 (the native resolve records a coarse per-output mapping via geometric
  attribution - nearest-source fallback so no output face is ever orphaned); Phase 3 replaces the
  coarse resolve mapping with composed `BRepTools_History`.
- **Fix (a) - MaxExtend by source identity** (`SnapStageTests.cs`): `SnapStage` attributes each
  clean face to the source panels that merged into it and carries `MaxExtend = max` of those
  sources, so a wall the caller marked to extend further keeps its reach through the merge. The
  pre-Phase-2 code re-applied the *input* `maxExtensions` list **positionally** to the
  merged/reordered clean faces, landing the wrong reach on the wrong panel.
- **Fix (b) - `SnapOpposedPartitions` gates** (`Panel3DSnapSolverTests.cs`): an overlap-**footprint**
  ratio gate (replacing the full-area ratio, so a door-cut skin - same footprint, smaller area -
  collapses instead of staying split) and a **thickness-scale separation** gate so a genuine 0.3-0.4 m
  void (a shaft) survives Stage A while a thin partition still collapses. Rejections emit
  `RejectedCollapse` diagnostics.

**Finding (why the separation gate is thickness-based, not the plan's proposed normal-sign test):**
the plan proposed `normal · (centroidB − centroidA) < 0` ("facing-away" skins) to distinguish a
partition from a void. Measured against the fixtures this proved unreliable: SAM/Revit import winding
orients a real partition's two skin normals **toward** each other (into the wall core) - the opposite
of a clean synthetic model - so the sign test mis-classified genuine partitions as voids and
regressed `whole-level-tilted` from 22 to 20 cells (the managed path; it reaches the managed pipeline
because its raw solve leaves 4 naked edges). The winding-independent **thickness-separation** gate
(`OPPOSED_PARTITION_MAX_SEPARATION` = 0.3 m, the "driven by per-panel thickness not the 0.4 floor"
clause of the plan) achieves the same goal without depending on normal orientation, and the same
guard was added to the general weighted `Snap` (whose `IsParallelWith` treats anti-parallel as
parallel and would otherwise collapse the void). Same empirical-recalibration methodology as Phase 1's
`MaxDroppedRatio`.

**Golden-master deltas (Phase 2).** Raw-path signatures are **byte-identical** to Phase 1 (the raw
path is untouched beyond populating its `SourceMap`). Managed-path signatures: four of five unchanged;
`whole-level-towers` (managed, forced) **improved** from 21 cells / 12 naked to **26 cells / 6 naked**
- the intended effect of the MaxExtend-by-source fix and the partition/void gates letting more rooms
close correctly. No managed regressions; the `Solve3D_ManagedPath` golden test asserts only
`cells >= 1` (it records, not pins, the managed number), so no assertion re-baselining was needed.

**New fixtures/tests.** `SnapStageTests` (MaxExtend carry, merge attribution, no-orphan invariant,
wide-void survives Stage A); `SourceMapTests` (compose/merge/split algebra); `SolverContextTests`;
the two new `SnapOpposedPartitions` truth-table cases (wide void kept, door-cut skin collapsed);
`SourceMapMappingIntegrationTests` (native: every solved output face carries a source on both the
raw-first and forced-managed paths). The shaft-void fixture is built synthetically (no new `.sam`).

## Native history / naked-wire / tolerance export (true-3D panel solver plan, Phase 3, native ABI v4)

Phase 3 adds the **observational** ABI v4 exports and threads exact provenance through the resolve
stage (design record: `docs/P3_ABI_V4_NATIVE_HISTORY_DESIGN_REVIEW.md`). "Observational" is the hard
constraint: capturing history / wires / tolerance must **not** change the geometry the ops produce -
the golden-master signatures are unchanged from Phase 2 (verified: the raw and managed paths route
the cell build through the sew-before-build path, which does not capture history, so panel
reconstruction keeps the geometric `NearestSourceIndex` heuristic and the output is byte-identical).

- **`OcctHistory`** (`SAM.Geometry.OCCT`, pure managed snapshot - no native lifetime, nothing to
  dispose) copies the native `BRepTools_History` off a result handle: per input face, its deletion
  flag and the **flat output ordinals** (cell-major, face-minor - identical to the managed decode
  walk) it was `Modified`/`Generated` into, plus the result's max/average sub-shape tolerance.
  Captured by the two ops that record history - `sam_occt_build_cell_complex` and
  `sam_occt_merge_coplanar` - and surfaced on `OcctCellComplexResult.History` (null on a pre-v4
  native or when the op does not capture it).
- **`HistorySourceMap.ToSourceMap`** (`SAM.Geometry.OCCT.Solver`) adapts a snapshot into a composable
  `SourceMap` hop (source = input ordinal, `FaceKey` = output ordinal): a 1→N split fans out, an N→1
  merge collapses, a deleted input is excluded, and an input that is neither mapped nor deleted (or an
  output ordinal no input maps to) is emitted as a `DiagnosticCode.HistoryGap` warning - never silent.
  `ResolveStage` composes it per **adopted** hop (pre-merge → MakerVolume → post-merge), disabling the
  composition when any load-bearing hop lacks history or an adaptive residual sew is adopted (the §7.1
  scope cut below); `Modify.Solve3D`'s `BuildPanels` then prefers the exact map and demotes
  `NearestSourceIndex` to a per-face fallback.
- **Naked wires** - `Query.Validate` now also returns `OcctValidationReport.NakedWires`: the
  free-boundary edges from the same `ShapeAnalysis_FreeBounds` pass, grouped into ordered polylines
  with a closed flag and best-effort per-edge owner faces (`-1`-tolerant). Empty on a pre-v4 native;
  the located `NakedEdge` issues remain the always-available signal.
- **Tolerance drift** - `sam_occt_result_max_tolerance` (per op) and `sam_occt_shape_max_tolerance`
  (live handle) expose max/average sub-shape tolerance for the `ToleranceDrift` signal.

**§7.1 scope cut (documented deviation).** History is captured on `build_cell_complex` and
`merge_coplanar` only. The standalone `sam_occt_sew_faces`→`sam_occt_shape_decode` hop (used by the
solver's sew-before-build cell build and by `ResolveStage`'s adaptive residual sew) captures **no**
history by design. Consequently the default solver path (sew-before-build) produces a null
`ResolveHistorySourceMap` and falls back to the geometric heuristic - which is exactly why the
observational golden masters are unchanged. History composition is exercised end-to-end on the direct
`build_cell_complex` path (a caller-supplied `OcctBuildOptions` defaults to `SewBeforeBuild = false`).

- **Unit** (`OcctHistorySourceMapTests.cs`, pure managed via the public `OcctHistory` snapshot
  constructor): split, merge, deletion, deleted-then-regenerated compose-to-nothing, the
  shared-face-two-ordinals decode-order invariant, gap and reverse-gap diagnostics, and the null-history
  (pre-v4) fallback.
- **Integration** (`HistoryExportIntegrationTests.cs`, native-gated): the 7-face split fixture (unit
  box + mid plane, `AvoidInternalShapes = false`) - 7 inputs, a wall splits 1→2, the shared mid face
  gets two ordinals; coplanar merge 2→1; open-box naked wire (1 closed 4-vertex wire at z = 1); the
  drift ceiling on a clean build (< 10× input tolerance); the null-history degrade on the
  sew-before-build path; and a 50-build soak asserting managed memory plateaus (the snapshot has no
  native lifetime by design).

## Panel reconstruction fidelity: Guid, parameters, apertures, provenance (true-3D panel solver plan, Phase 4)

Phase 4 restores 2D output parity: a solved panel is the *same* panel with new geometry, not a
construction/type-only rebuild that silently drops Guid, parameters and apertures (the pre-Phase-4
`Solve3D` behaviour). It consumes the solver's `SourceMap` (exact via Phase 3's composed native
history when available, geometric fallback otherwise - never null) instead of the single-winner
`NearestSourceIndex`, so a genuine split or merge is reconstructed as such.

- **`PanelReconstruction.Build`** (`SAM.Analytical.OCCT.Solver`, pure and native-free - unit-tested
  without any OCCT DLL): for each resolved output face, reads its source set from the `SourceMap` and
  applies one of three policies:
  - **1:1** (one source, itself unsplit): keeps the source's own Guid via
    `Analytical.Create.Panel(source.Guid, source, face3D, ...)` - construction, type and every
    parameter survive untouched.
  - **Split** (one source mapping to more than one output face): each piece gets a fresh Guid and a
    `PanelProvenanceParameter.SourceGuid` stamp naming the original source.
  - **Merge** (more than one source mapping to one output face): the dominant (largest-area) source
    keeps its own Guid ("wins Guid"); the others are stamped as
    `PanelProvenanceParameter.MergedSourceGuids` (comma-separated).
  - `BucketSize`/`Weight`/`MaxExtend` are stamped from the dominant source in every case, so the
    existing `SAMAnalytical.Visualize` contract (Phase 2) is unaffected.
- **Apertures are never matched by hand.** Every contributing source's own apertures are handed to
  `Create.Panel`, whose ctor already re-hosts each one only if it lands within `maxDistance` of that
  particular output face (trimmed to fit, via the existing `Modify.AddApertures` bounding-box gate) -
  trying an aperture against every piece a split produced *is* "assigned to the piece that
  geometrically contains it", for free, with no bespoke matching logic. An aperture that fits no
  produced piece is collected into `orphanedApertures` (never silently dropped) with its original
  world-space geometry and source Guid, and surfaced as a `SAM_OCCT_SOLVE3D_APERTURE_ORPHANED:`
  diagnostic line - the orphan policy's "return for manual re-hosting" leg (the plan does not require
  a "try the nearest piece" retry step beyond what re-trying every produced piece already achieves).
- **Air/gap-fill panels** are stamped `PanelProvenanceParameter.Provenance = "GapFill"` so they are
  distinguishable from a "real" opening an analytical model might otherwise carry.
- **API shape.** `Modify.Solve3D` gained a new overload with an `out List<OrphanedAperture>
  orphanedApertures` parameter (plus `minApertureArea`/`maxApertureDistance`); the existing
  2-out-param overload is now a thin wrapper over it, so the shipped Grasshopper `SAMOCCT.Solve3D`
  component keeps compiling and behaving identically without modification (out-parameters cannot be
  optional in C#, so a new required one could not be added to the existing signature without breaking
  every positional caller - this is the same additive-overload pattern used for the ABI probes).
  `Clean3D`/`Extend3D`/`OpenPanels3D` are untouched: they keep the original `BuildPanels`/
  `NearestSourceIndex` path (no `SourceMap` is threaded through those pre-resolve inspection passes).
- **Post-resolve angle-tolerance tightening (live-defect fix, plan §C).** Both post-resolve
  `MergeCoplanarFace3Ds` calls (`ResolveStage.Resolve`'s post-merge hop; `Panel3DSnapSolver.
  TryRawResolve`'s raw-path merge) were tightened from the caller's `ToleranceAngle` (5° by default)
  to SAM's canonical `Tolerance.Angle` (~2°): post-resolve, faces are already split by the kernel, so
  5° was generous enough to fuse slightly-sloped roof planes that should stay distinct. **Measured
  delta: none.** All 5 golden-master fixtures (raw and managed paths, 10 signatures total) were
  captured before and after the change (`git stash` on the two touched files, rebuild, re-run) and
  are byte-identical - none of the reference fixtures happen to have a roof/wall pair in the 2°-5°
  drift band, so this is a defensive fix with no observed effect on today's fixture set, not a
  regression.

**New tests.** `PanelReconstructionTests.cs` (unit): 1:1 preserves Guid/construction/parameters;
split assigns fresh Guids + `SourceGuid` stamps; an aperture placed inside one split piece's footprint
lands only there; merge keeps the dominant's Guid and stamps `MergedSourceGuids`; an aperture in the
gap between two split pieces is reported as orphaned; a face with no `SourceMap` entry at all falls
back to `NearestSourceIndex` rather than being dropped. `AperturePreservationIntegrationTests.cs`
(native-gated): a synthetic sealed room with one window round-trips through `Solve3D` end-to-end -
Guid, construction, parameters and the trimmed aperture all survive, with zero orphans (the raw-first
path adopts an already-closed box as-is, so this exercises the 1:1 leg through the real kernel; the
split/merge legs are exercised without native geometry in the unit tests above, since forcing a
literal split deterministically through `BOPAlgo_MakerVolume` is not a stable substrate for a fast
unit-style assertion).

**Known CI drift (not fixed by Phase 0 - owner-deferred).**
`.github/workflows/build.yml` clones the sibling `SAM` / `SAM_Solver` repos
pinned to branch `sow/2026-Q2`, but as of this plan (2026-07-02) both sibling
repos live on `sow/2026-Q3`. CI currently still passes because the pinned
`sow/2026-Q2` branch state happens to still build against this repo's needs,
but the pin no longer reflects where development is actually happening and
should be bumped (or parameterized) in a follow-up hygiene change - out of
scope here because CI native build is separately owner-deferred (CI stays
managed-only; see the native-native gap below) and this plan's phases are
gated by the *local* integration run, not CI.

## Diagnosis-driven closure: AutoTune3D, per-loop sew, benchmark guard (true-3D panel solver plan, Phase 5)

Phase 5 (design authority: `docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md`) replaces blind extension
margins with bounded, diagnosed, locally-escalated closure. Six sub-phases (5a-5f), each its own commit,
both suites green and golden masters re-run before the next:

- **5a** - foundations: `ClosureSignature3D.SliverCellCount` (additive; `IsRegressionOf` semantics
  unchanged), managed-path `Signature` population, `MinPairSeparation`, `LoopAttribution.AttributeLoopsToSources`,
  a determinism lock. Unit: `ClosureSignature3DTests`, `MinPairSeparationTests`, `LoopAttributionTests`,
  `DeterminismTests`.
- **5b** - `GapFill.FromNakedWires` (native-wire-driven patching; planar patch / tagged fan fallback /
  residual warnings, never silent) + `Panel3DSnapSolver.FinalizeAndValidate` (the single final-truth step:
  assembles resolved + patches + retained, runs the signature-gated consolidation rebuild via
  `Create.Shells`, composes provenance, and is the ONLY producer of the outward naked count/wires/`Signature`
  - every earlier validate in the pipeline is an intermediate diagnostic only). Unit: `GapFillV2Tests`.
  Integration: `GapFillIntegrationTests`.
- **5c** - `HealStage.RetainDroppedV2` (re-adds the ORIGINAL CLEAN geometry - the `SnapStage` output, not
  the extended/overshooting candidate - for every input source the resolve genuinely dropped, detected
  map-side via `SourceMap.FacesOf(source).Count == 0`, behind `IsRepresented`/area/validity filters,
  tagged `Provenance.DroppedRetained`). Unit: `RetainDroppedV2Tests`. Integration:
  `DroppedRetainIntegrationTests`.
- **5d** - `HealStage.SewV2` replaces `ResolveStage`'s inline adaptive sew: the native sew stays GLOBAL
  (there is no per-loop native sew) but is capped below `MinPairSeparation x SewSafetyFactor` (default 0.5,
  hard clamp 0.3 m) so a global sew cannot fuse a genuine double wall, with before/after per-loop
  bookkeeping (Closed/Persisting/New) as acceptance EVIDENCE and a fusion veto (any pre-sew near-parallel
  pair that does not survive the sew rejects the whole result). Unit: `SewV2Tests`. Integration:
  `SewV2IntegrationTests` (ParallelPairWeld: a 0.08 m double wall caps the sew `<= 0.04` m and both skins
  survive; ShaftProtection: a 0.35 m cavity is far wider than the sew tolerance and survives).
- **5e** - `AutoTune3DSolver` (`SAM.Geometry.OCCT.Solver`) and its analytical wrapper `Modify.AutoTune3D`
  (`SAM.Analytical.OCCT.Solver`): a bounded (`AutoTune3DOptions.MaxRounds`, default 3), diagnosis-driven
  escalation loop wrapping `Panel3DSnapSolver`. See "AutoTune3D engagement and acceptance" below.
- **5f** - the ~1,500-face `BenchmarkFixture`/`PerformanceGuardIntegrationTests` performance guard and this
  documentation. See "Benchmark1500 performance guard" below.

**AutoTune3D engagement and acceptance rules.** `AutoTune3DSolver.Execute` runs a raw-first
`Panel3DSnapSolver` baseline, then engages the escalation loop when the baseline leaves naked (free)
boundary edges **or** closed only by fabricating GapFill/HoleFill patches (owner refinement, 2026-07-03: a
strict "naked edges only" gate never fires on synthetic fixtures, because 5b's GapFill pre-closes many
simple gaps to `naked == 0` before AutoTune ever sees them - engaging on fabrication too lets AutoTune try
measured wall extension first, replacing last-resort fabricated geometry where it safely can). Each round
attributes the residual naked loops (`LoopAttribution`) and the walls adjacent to each fabricated patch to
their source panels, raises ONLY those culprits' `MaxExtend` one ladder rung
(`[0.5, 0.75, 1.0, 1.5]`; a culprit already at the top rung is exhausted and drops out;
`EscalateBucket` stays OFF by default - a partition-collapse hazard), and re-solves through the managed
pipeline (`ForceManagedPipeline`, so the raw path is not repeatedly re-tried). `IsAcceptableRound` has two
modes sharing the same guards (no `ClosureSignature3D` regression, no sliver-cell rise, every new cell
positively proven adjacent to a loop that closed this round via `CellIncreaseAdjacent` - unprovable
adjacency rejects conservatively, never benefit-of-the-doubt): naked-driven rounds require the naked count
to strictly drop; fabrication-driven rounds require naked to stay at 0 while the fabricated patch count
strictly drops. A rejected round is atomic (discarded, loop stops) with an `EscalatedPanel` Warning naming
the reason; accepted escalations get an `EscalatedPanel` Info; residual naked loops and residual
fabrication get diagnosed (never silently returned). Best-effort always: `AutoTune3DSolver` never throws
for an unresolved loop. Unit: `AutoTune3DTests` (ladder progression, culprit-only/exhausted escalation, the
acceptance truth table for both modes, the conservative adjacency reject, bucket-off). Integration
(native-gated): `AutoTune3DIntegrationTests` (a fabricated-patch bake-off replaced by measured extension; an
unclosably-wide gap rejected and stopped boundedly with residual diagnostics and no throw; a 0.35 m
shaft/partition survives; a watertight baseline never engages) and
`AutoTune3DAnalyticalIntegrationTests` (the `Modify.AutoTune3D` wrapper closes end-to-end while preserving
Guid/construction via `PanelReconstruction`). `Modify.AutoTune3D` is a SEPARATE, opt-in entry point -
normal `Modify.Solve3D` does not invoke AutoTune and its behaviour (including all ten golden-master
signatures) is unchanged.

**Benchmark1500 performance guard.** `BenchmarkFixture.Benchmark1500()` (`Testing/SAM.OCCT.IntegrationTests`)
generates a deterministic, parametric ~1,500-face fixture (10 x 5 x 5 = 250 independent, disjoint,
watertight 6-face room boxes, each separated from its neighbours by a 1 m gap on every axis - nothing is
randomised, so there is nothing to seed). `PerformanceGuardIntegrationTests` runs it through
`AutoTune3DSolver` and asserts: wall-clock under a 90 s soft ceiling (docs §J fixture 8 / §M), and exact
closure sanity (`naked == 0`, `cells == 250`, `rounds == 0` - every room is independently watertight by
construction, so the outcome is an exact count, not a range). Measured: ~3.5 s, comfortably inside budget.

- *Skip seam:* set `SAM_OCCT_SKIP_PERF` (any non-empty value) to opt out of this one test - useful on a
  resource-constrained or shared CI agent where even a fast 90 s-budgeted test is undesirable. The skip
  reason names the variable explicitly, and the test writes the variable's state to test output before
  checking it, so a report gathered from a run where native is unavailable still shows whether the
  opt-out was set. This is independent of, and checked before, the ordinary native-missing skip every
  other integration test in this suite already uses - neither skip reason fails the lightweight/CI
  (managed-only, native-less) run.
- *Why the fixture is watertight, not gappy:* owner decision (2026-07-04) - Benchmark1500 is a
  performance/scaling guard only; it does not need to force AutoTune escalation rounds, because
  AutoTune's correctness does not depend on overall model size and is already proven at small scale by
  `AutoTune3DIntegrationTests` above. Measured separately: a single continuous shared-wall lattice at a
  comparable cell count (~700 cells) took over 130 s for just the first native resolve - MakerVolume's
  cost is driven by CONNECTED COMPONENT size, not total face/cell count (the review's own non-linear-
  scaling caution, §M) - so Benchmark1500 uses many small independent components instead of one large
  connected one. Separately, introducing a gappy perturbation into a *multi-room* model proved unreliable
  with the current pipeline: a single broken room's faces get silently absorbed by the raw-adoption
  gate's `RetainDropped` recovery path (the room never forms a cell, but the drop also never surfaces a
  naked-edge or fabricated-patch signal) whenever OTHER valid rooms coexist in the same solve - confirmed
  across defect size (a partial wall cut vs. an entire missing face), sew settings
  (`SewBeforeBuild` true/false), and room connectivity/clustering style. This is the existing (Phase
  1/5c) raw-first + `RetainDropped` design working exactly as calibrated for a low tolerated drop ratio
  (see the `MaxDroppedRatio` finding, Phase 1 above) - not a defect introduced in Phase 5f - and is
  recorded here as a known scale limitation rather than something this phase's benchmark fixture needs to
  route around.

## Per-level frames (true-3D panel solver plan, Phase 6)

Phase 6 removes the single-global-`Up` / 20° world-frame limitations by clustering caps into per-level
frames. It lands as sub-phases:

- **6a** - `LevelFrame` (`SAM.Geometry.OCCT.Solver`): the level-datum clustering foundation
  (`LevelFrame.Cluster` groups caps by normal cone ~5° + a 0.15 m elevation band; `AssignCapToFrame`/
  `AssignWallToFrames` assignment with ambiguous-membership diagnostics). Pure, native-free; nothing in the
  solver consumed it, so all golden masters were byte-identical. Unit: `LevelFrameTests`.
- **6b** - frame-aware wall/cap/vertical classification (`SnappedPanel.IsVertical(double, Vector3D)`,
  `LevelFrame.IsWall`/`IsCap`/`IsVertical`/`ClassifyFace`, `FaceRole`). The classification *layer* was made
  frame-parametric; the pipeline was not re-routed, so golden masters stayed byte-identical. Unit:
  `FrameAwareClassificationTests`.
- **6c** - **frame-aware cap normalization only** (see below). Per-frame extend/fill conditioning is
  **deferred** (see the deviation note).
- **6d** - observational stacked-slab / inter-storey interface detection (see below). Changes no geometry
  and no source mapping.
- **6e** - Phase 6 wrap-up: this section - fixture/coverage audit, full-suite verification (unit + native
  integration + golden masters + Phase 5 benchmark), and documentation. No new production code; see
  "6e - completion" below.

### 6c - frame-aware `NormalizeCaps` (split-level landing preservation)

`Panel3DSnapSolver.NormalizeCaps(panels, IReadOnlyList<LevelFrame>, ...)` (new overload, consumed by
`SnapStage.Clean`) normalizes each level's caps onto that level's own datum plane, grouped by `LevelFrame`
membership (a ~0.15 m band) instead of the legacy flat 0.30 m `NormalizeCapOffset`. A cap ~0.18-0.25 m above
a floor is therefore its **own** frame and keeps its own elevation instead of being flattened onto the floor -
the split-level landing the plan (§E Phase 6, Risk 5) requires be preserved ("do not normalize away split
levels"). The legacy world-frame overload is retained and used as the fallback when no cap forms a frame. Cap
membership is decided frame-aware (`LevelFrame.ClassifyFace`), so a wall on a tilted level is never mistaken
for a cap. Unit: `NormalizeCapsFrameAwareTests` (landing preserved; legacy-merges-it contrast lock;
same-level tiles still snap to the datum; deterministic under input order; stacked levels never merge;
no-frames no-op).

**Golden-master re-baseline (managed path only; documented and intentional).** Raw-path signatures are
**byte-identical** to Phase 5 for all five fixtures (the raw path is untouched - `NormalizeCaps` runs only in
the managed clean/extend pipeline). Managed-path signatures: three of five byte-identical
(`whole-level-flat` 22c, `tilted-two-spaces` 2c, `whole-level-tilted` 22c/0 naked); the two multi-level
fixtures shift because they genuinely contain caps in the 0.15-0.30 m band that the legacy flat band was
flattening away:

| fixture (managed, forced) | before (Phase 5) | after (Phase 6c) |
| --- | --- | --- |
| `two-level-tilted.sam`  | 13 cells / 1848.092 m³ / 14 naked / 362 faces | **29 cells / 2213.303 m³ / 29 naked / 416 faces** |
| `whole-level-towers.sam`| 24 cells / 7614.729 m³ / 14 naked / 285 faces | **22 cells / 8777.056 m³ / 12 naked / 231 faces** |

These deltas are the **intended** managed-conditioning consequence of preserving frame separation: e.g.
`two-level-tilted` has caps at 0.186 m and 0.253 m above its main slabs (a real split-level/step structure)
that the 0.30 m band merged and the 0.15 m frame band now keeps distinct - so more of its rooms survive as
their own cells (closer to the raw path's 43). The `Solve3D_ManagedPath` golden test asserts only `cells >= 1`
(it records, not pins, the managed number), so no assertion re-baselining was needed; this table is the record
of the numbers. The managed naked-edge rise on `two-level-tilted` (14 -> 29) is expected while the paired
per-frame extend/fill is deferred (below): preserving a landing exposes the floor/wall gaps around it that the
per-frame conditioning is meant to close.

**Deviation - per-frame extend/fill conditioning is deferred (owner decision, 2026-07-04).** The plan's §E
Phase 6 also calls for running extend/fill *per level frame*. A prototype that clustered the clean caps into
level frames, grouped them by orientation, and conditioned each orientation group in its own frame **regressed
the `whole-level-tilted` RAW golden master (22 -> 8 cells)** - a hard stop. Root cause: `whole-level-tilted` is
**one** analytical level whose caps span two very different tilts (~34° floors and ~56° roof faces, across 15
elevation frames); splitting the conditioning across those orientations severs the walls/caps that must meet
between them, and because that fixture's raw solve falls through to the managed pipeline, the regression
surfaces on the raw path. The pre-6c single-global-`Up` (average floor normal) conditions everything in one
frame and gets 22. Per-frame extend/fill therefore needs a safer design - **condition in a dominant frame, and
split only across proven-separate storeys, never within a single multi-orientation level** - and is deferred to
a later focused sub-phase. 6c lands the frame-aware cap normalization only; the conditioning path is unchanged
(a `TODO` in `Panel3DSnapSolver.Execute` records the follow-up). Raw golden masters remain byte-identical.

### 6d - stacked-slab / inter-storey interface handling (observational)

`StackedSlabInterfaceDetector.DetectStackedInterfaces` (`SAM.Geometry.OCCT.Solver`) identifies inter-storey
stacked-slab interfaces - pairs of near-congruent, **opposite-facing** cap faces (the floor of level N and the
ceiling of level N-1) that represent one physical inter-storey boundary - and `VerifyRepresented` checks that
both analytical source panels survive into the `SourceMap`. `Panel3DSnapSolver.Execute` runs both on the raw and
managed paths and exposes the result on `StackedSlabInterfaces`.

**It is purely observational - it changes NO geometry and NO source mapping.** The geometric "treat two skins as
one interface for the cell build" is already done by the proven, golden-locked mechanisms the solver runs today:
the native **sew** stitches coincident/near-coincident skins on the raw path, and
`Panel3DSnapSolver.SnapOpposedPartitions` collapses an opposed, congruent, within-a-wall-thickness (<= 0.3 m)
pair on the managed path; and provenance is already preserved (the geometric attribution maps both
coplanar-overlapping sources to the shared face, and `PanelReconstruction`'s merge policy keeps the dominant
source's Guid and stamps the other as a merged source). A NEW collapse here would be redundant within that band
and unsafe beyond it (a wider opposed pair bounds a genuine cavity/shaft the plan requires be kept) - and would
risk the raw golden masters this sub-phase must hold byte-identical. So 6d formalises the **detection** across
level frames, **verifies** the both-sources provenance, and **rejects** the unsafe cases with diagnostics,
rather than introducing a geometry change.

- **Gates (conservative).** A pair is an interface only when: both are caps (non-vertical); their normals are
  anti-parallel within ~5° (opposite-facing = two different rooms' skins - this is what distinguishes a
  stacked slab from a co-parallel split-level whole floor / double-skin, winding-independently, because
  opposite-facing skins are anti-parallel under either winding convention); their footprints overlap >= 80% of
  the larger (congruent - which rejects a small-over-large split-level landing / partial step); their
  perpendicular separation is <= 0.3 m (the `OPPOSED_PARTITION_MAX_SEPARATION` slab band - wider is a
  cavity/shaft, kept); and both skins assign to a level frame unambiguously. Every rejection (wide cavity, low
  overlap, co-parallel double-skin/split-level, ambiguous frame) emits a diagnostic (`RejectedCollapse` /
  `AmbiguousLevelFrame`); an accepted interface emits `DuplicateFace` (Info); the provenance check emits
  `AdoptedLevel` (both represented) or `DroppedFace` (one missing) - never silent.
- **Golden masters: unchanged.** Raw signatures **byte-identical** to 6c (5/5); managed signatures identical to
  the documented 6c baselines (5/5, including the re-baselined `two-level-tilted` 29c/29n and
  `whole-level-towers` 22c/12n) - because the detector is observational. The perf guard (Benchmark1500) is
  unaffected (a plan-bbox pre-filter keeps the pairwise cap scan cheap).
- **Tests.** Unit `StackedSlabInterfaceDetectorTests` (+9): opposed congruent skins detected (coincident and
  slab-separated); co-parallel split-level rejected (diagnosed); partial-step landing rejected (low overlap);
  wide cavity rejected; side-by-side caps ignored (plan pre-filter); deterministic order; `VerifyRepresented`
  both-mapped / one-missing. Integration `StackedSlabInterfaceIntegrationTests` (+3, native-gated): a two-storey
  stacked fixture resolves to 2 cells / 0 naked with both mid-slab sources represented and the interface
  detected; a 0.4 m cavity is rejected and survives (>= 2 cells, both sources kept); a split-level landing is
  not detected as a stacked interface.

### 6e - completion: fixture/coverage audit, full-suite verification, documentation

6e is the Phase 6 wrap-up called for by the plan: no new solver architecture, only a coverage audit against
the plan's acceptance list, a full local-integration verification pass, and this documentation. Audited
against 6a-6d's existing tests, every item on the acceptance list already has coverage - no gaps were found,
so no new tests were added:

| Coverage item | Where it is covered |
| --- | --- |
| Flat single-level model | `LevelFrameTests.Cluster_FlatSingleTile_ProducesOneFrameAtItsElevation`; `FrameAwareClassificationTests` flat-frame cases; `whole-level-flat.sam` golden master |
| Tilted single-level model | `LevelFrameTests.Cluster_TiltedSingleLevel_ProducesOneTiltedFrame`; `whole-level-tilted.sam` / `tilted-two-spaces.sam` golden masters |
| >20° tilted level | `LevelFrameTests`/`FrameAwareClassificationTests` use a 25° tilt explicitly past the legacy 20° world-frame ceiling (`IsWall_TiltedLevelBeyond20Degrees_FixesWorldFrameMisclassification`) |
| Two stacked levels | `LevelFrameTests.Cluster_TwoStackedLevels_ProducesTwoFramesSortedByElevation`; `NormalizeCapsFrameAwareTests.NormalizeCaps_FrameAware_TwoStackedLevelsDoNotMerge`; `StackedSlabInterfaceIntegrationTests.TwoStoreyStacked_...` (native) |
| Split-level landing preserved | `LevelFrameTests.Cluster_SplitLevelLanding_IsNotMergedIntoMainFloor`; `NormalizeCapsFrameAwareTests.NormalizeCaps_FrameAware_PreservesSplitLevelLanding` (+ legacy-merges-it contrast lock); `StackedSlabInterfaceIntegrationTests.SplitLevelLanding_NotDetectedAsStackedInterface` (native) |
| Shaft/cavity preserved | `StackedSlabInterfaceDetectorTests.DetectStackedInterfaces_WideCavity_RejectedNotDetected`; `StackedSlabInterfaceIntegrationTests.WideCavity_RejectedAsUnsafeMerge_CavitySurvives` (native); `SewV2IntegrationTests` ShaftProtection (Phase 5d, still green) |
| Wall spanning multiple frames | `LevelFrameTests.AssignWallToFrames_StoreyHeightWall_SpansBothFloorAndCeilingFrames` (full-storey wall belongs to both its floor and ceiling frame) vs. `AssignWallToFrames_ShortWall_SpansOnlyItsLevel` |
| Stacked-slab duplicate-skin handling | `StackedSlabInterfaceDetectorTests.DetectStackedInterfaces_CoincidentOpposedSkins_DetectsOneInterface` (zero-thickness convention) and `_OpposedCongruentSkins_...` (slab-thickness-separated); `StackedSlabInterfaceIntegrationTests.TwoStoreyStacked_...` end-to-end (native) |
| SourceMap/provenance after frame operations | `StackedSlabInterfaceDetectorTests.VerifyRepresented_BothSourcesMapped_EmitsPreservedInfo` / `_OneSourceMissing_EmitsIncompleteWarning`; `StackedSlabInterfaceIntegrationTests` asserts `SourceMap.FacesOf(...)` non-empty for both interface sources (native) |
| Phase 5 golden masters + benchmark still valid | Re-run in full below - unchanged |

**Verification run (2026-07-04, local, native present).**

```
dotnet build SAM_OCCT.sln -c Debug                                              # 0 errors
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj                # 400 passed, 0 failed, 0 skipped
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj  # 126 passed, 1 skipped (native-missing
                                                                                 # inverse-gate; expected since native
                                                                                 # IS present), 0 failed
```

Golden masters (`GoldenMasterIntegrationTests`), re-captured this run:

| fixture | raw cells / naked | managed cells / naked |
| --- | --- | --- |
| `whole-level-flat.sam` | 22 / 0 | 22 / 0 |
| `tilted-two-spaces.sam` | 2 / 0 | 2 / 0 |
| `whole-level-tilted.sam` | 22 / 0 | 22 / 0 |
| `two-level-tilted.sam` | 43 / 0 | **29 / 29** |
| `whole-level-towers.sam` | 32 / 0 | **22 / 12** |

Raw signatures match the plan's Phase 0 targets exactly (all 5 fixtures, 0 naked edges) - **byte-identical**
through every 6a-6e sub-phase. Managed signatures match the documented 6c re-baseline exactly for the two
multi-level fixtures (bold above); the other three are unchanged from Phase 5. No golden-master value changed
in 6e - this run is a confirmation, not a re-baseline.

`PerformanceGuardIntegrationTests.AutoTune3D_Benchmark1500_CompletesWithinNinetySecondsWithExactClosure`
passed: ~6.4 s elapsed (90 s soft ceiling), 250/250 cells, 0 naked, 0 AutoTune rounds (every room independently
watertight by construction, as designed) - the Phase 5f benchmark guard is unaffected by Phase 6.

**No changes required.** The verification run above surfaced no regression, no missing coverage, and no
documentation drift, so 6e makes no production-code change - only this documentation and the coverage table.

### Running Phase 6 tests

```powershell
# LevelFrame clustering / assignment (unit, no native library required)
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj --filter "FullyQualifiedName~LevelFrameTests"

# Frame-aware wall/cap/vertical classification (unit)
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj --filter "FullyQualifiedName~FrameAwareClassificationTests"

# Frame-aware NormalizeCaps / split-level landing (unit)
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj --filter "FullyQualifiedName~NormalizeCapsFrameAwareTests"

# Stacked-slab interface detector (unit, no native library required)
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj --filter "FullyQualifiedName~StackedSlabInterfaceDetectorTests"

# Stacked-slab interface end-to-end (integration, native-gated - build-native.ps1 first)
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj --filter "FullyQualifiedName~StackedSlabInterfaceIntegrationTests"

# Golden masters (raw + managed, all 5 fixtures) and the Phase 5f performance guard
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj --filter "FullyQualifiedName~GoldenMasterIntegrationTests|FullyQualifiedName~PerformanceGuardIntegrationTests"
```

### Stop rules for future Phase 6 follow-up work (per-frame extend/fill, or anything else touching this area)

Anyone picking up the deferred per-frame extend/fill conditioning (or any further per-level-frame work) must
stop and reassess, not push through, if:

- **Any raw golden-master signature changes** for any of the 5 reference fixtures (cell count, naked-edge
  count, or volume beyond `ClosureSignature3D`'s tolerance) - the prototype that regressed
  `whole-level-tilted` 22->8 cells is the exact failure mode this guards against.
- **A managed golden-master value changes** from the documented 6c/6d baseline (`two-level-tilted` 29c/29n,
  `whole-level-towers` 22c/12n; the other three fixtures unchanged from Phase 5) without an explicit,
  reasoned delta recorded in TESTING.md and the plan (same contract as every other phase).
- **The Phase 5f benchmark regresses materially** (approaches or exceeds the 90 s soft ceiling, or its exact
  closure sanity - `naked == 0`, `cells == 250`, `rounds == 0` - stops holding).
- **The fix requires conditioning across orientation groups within a single analytical level** (the root
  cause of the reverted prototype) rather than "a dominant frame" or "proven-separate storeys" as the
  deferral note requires - that is a sign the safer design constraint has been violated, not satisfied.
- **Any change would touch native ABI, Grasshopper components, or Phase 7 cell-classification scope** -
  those are out of bounds for a Phase 6 follow-up by the plan's own phase boundaries.

When none of the above trip, the safe design space per the 6c deviation note is: condition in a dominant
frame, and split extend/fill only across proven-separate storeys - never within a single multi-orientation
level.

## Phase 7-pre: pinned managed golden masters + frame-normalization diagnostics

Two bounded pre-flight cleanup items from `docs/P6_ARCHITECTURE_REVIEW.md` §O (O1, O2), landed before
Phase 7 (cell classification / Spaces handoff) starts. No solver geometry changed; both suites green,
golden masters re-run first (raw byte-identical, managed now exactly pinned instead of drifting).

- **O1 - managed golden masters machine-pinned.** `GoldenMasterIntegrationTests.
  Solve3D_ManagedPath_ClosureSignatureIsRecorded` (which asserted only `cells >= 1`) is renamed to
  `Solve3D_ManagedPath_ClosureSignatureMatchesGoldenMaster` and now asserts the exact Phase 6c/6d
  baseline for all five fixtures - cell count and naked-edge count exactly, total volume within
  `ClosureSignature3D.IsRegressionOf`'s own tolerance (`Core.Tolerance.MacroDistance`, not string-formatted
  equality, so harmless floating-point noise in the native volume sum cannot fail the test while real
  drift still does):

  | fixture | cells | naked | total volume (m³) |
  | --- | --- | --- | --- |
  | `whole-level-flat.sam` | 22 | 0 | 3479.896696920142 |
  | `tilted-two-spaces.sam` | 2 | 0 | 723.6524777123251 |
  | `whole-level-tilted.sam` | 22 | 0 | 3377.8280592825126 |
  | `two-level-tilted.sam` | 29 | 29 | 2213.30306718148 |
  | `whole-level-towers.sam` | 22 | 12 | 8777.056042123724 |

  These are the values already documented in the "Per-level frames" §6c/§6e tables above (measured
  freshly for this change, matching exactly); this is a test-hardening change, not a re-baseline - the
  §6e stop rule ("a managed golden-master value changes... without an explicit, reasoned delta") is now
  machine-enforced instead of living in prose only.
- **O2 - frame-aware `NormalizeCaps` diagnostics parity.** The frame-aware `NormalizeCaps` overload
  (`Panel3DSnapSolver.cs`, consumed by `SnapStage.Clean`) was diagnostically silent (the legacy
  world-frame overload's call site is too, but the frame-aware path is where Phase 6 debugging actually
  needs the signal). It gained an optional `SolverDiagnostics` parameter, purely additive:
  - A new `DiagnosticCode.FrameNormalization` (Info) reports the frame count formed, the cap count
    classified onto a frame, and a per-frame line naming the cap count normalized onto that frame's
    datum (or "nothing to normalize onto" for a single-cap frame).
  - `SnapStage.Clean` reports the legacy-fallback event (`FrameNormalization`, Info) when no cap forms a
    frame and the flat world-frame band is used instead.
  - The existing `LevelFrame.ClassifyFace` (and the `AssignCapToFrame`/`RoleInFrame` diagnostics it
    already supported but the frame-aware `NormalizeCaps` call never wired through) now receives the
    `diagnostics` instance, so an ambiguous cap-to-frame assignment (`AmbiguousLevelFrame`) surfaces from
    cap normalization too, not only from `StackedSlabInterfaceDetector`.
  - The frame bucket iteration in `NormalizeCaps` is now explicitly ordered by frame index
    (`OrderBy(x => x.Key)` instead of an implicit `Dictionary.Values` walk) as a side effect of adding the
    per-frame diagnostic line - each frame's cap set is disjoint and self-contained, so this has no effect
    on the produced geometry (confirmed: all ten golden-master signatures unchanged).
  - Visible via `Panel3DSnapSolver.Diagnostics` and the `SAM_OCCT_SOLVE3D_DIAGNOSTIC:` lines `Modify.Solve3D`
    already surfaces; no new output parameter.

**Verification run (local, native present).** Build 0 errors; unit **400/400**; integration **126 passed /
1 skipped** (the inverse-gated native-missing test, expected since native is present); all 10 golden-master
signatures confirmed (raw byte-identical 5/5; managed now exact-match 5/5 against the table above); Phase
5f `Benchmark1500` unaffected (~5 s, well inside the 90 s ceiling). `docs/P6_ARCHITECTURE_REVIEW.md` records
the full checkpoint review these two items come from; `docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md`
points to it.
