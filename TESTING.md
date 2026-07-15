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
| `Testing/SAM.OCCT.GrasshopperTests` | PR #61 GH component contract tests. Instantiates production Grasshopper components, inspects registered GUIDs, inputs, outputs, access, and persistent defaults. The Grasshopper SDK assembly is resolved at runtime from the restored NuGet package (no Rhino installation needed), so these tests always execute — a resolution failure is a loud test failure, never a silent skip. | No (GH SDK from NuGet) |

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

# Grasshopper component contract tests — GH SDK resolved from the NuGet cache; always execute.
# The contract tests instantiate the components and inspect their managed metadata only, so they need
# neither the native library nor a live Rhino deployment. SAM_OCCT_SKIP_NATIVE_BUILD=true skips the
# native pre-build (BuildNativeOcct); the live-Rhino .gha copy is already best-effort (IgnoreExitCode),
# so this command succeeds even when Rhino is open (its deployed DLLs are locked) or the native
# toolchain is absent. The local build\*.gha packaging step is untouched and still must succeed.
$env:SAM_OCCT_SKIP_NATIVE_BUILD='true'; dotnet test Testing/SAM.OCCT.GrasshopperTests/SAM.OCCT.GrasshopperTests.csproj
```

If the native library is not on the load path, the integration tests skip and
the unit tests still validate all managed behaviour.

**Full regression (build + Grasshopper + both test projects).** The command sequence a PR review runs
end to end — the integration run already includes `GoldenMasterIntegrationTests` (all 10 raw/managed
signatures) and `PerformanceGuardIntegrationTests` (`Benchmark1500`; see "Benchmark1500 performance
guard" above for the `SAM_OCCT_SKIP_PERF` opt-out):

```powershell
dotnet build SAM_OCCT.sln -c Debug
dotnet build Grasshopper/SAM.Analytical.Grasshopper.OCCT/SAM.Analytical.Grasshopper.OCCT.csproj -c Debug
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug
$env:SAM_OCCT_SKIP_NATIVE_BUILD='true'; dotnet test Testing/SAM.OCCT.GrasshopperTests/SAM.OCCT.GrasshopperTests.csproj -c Debug
```

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

## Cell classification and spaces handoff (Phase 7)

Phase 7 (docs/P6_ARCHITECTURE_REVIEW.md §P) makes the resolved cell complex analytically meaningful:
surface per-cell metadata, classify each cell, and hand the closed, classified cells off to
`SAM.Analytical` as `Space`s - reusing the existing adjacency/panel construction path rather than
reimplementing it. Landed as three sub-steps, each its own commit with both suites green and golden
masters re-run first (all raw signatures byte-identical throughout; all managed signatures matching the
7-pre pinned baseline throughout - Phase 7 never touches solver geometry, only reads its output):

- **7a** (`0956ced`) - `SolverCell` (`SAM.Geometry.OCCT.Solver`): an additive per-cell snapshot (index,
  volume, centre, boundary shell) copied from the native decode's already-exposed `OcctCell` metadata -
  no new native ABI. `Panel3DSnapSolver.Cells` is populated at the exact point `Signature` is produced on
  both paths (raw: `TryRawResolve`; managed: `FinalizeAndValidate`, both the consolidation-rebuild-adopted
  and `DecodeCellVolumes` fallback legs), so `Cells.Count`/`Cells[i].Volume` always agree with
  `Signature.CellCount`/`CellVolumes[i]`. Unit/integration: `SolverCellIntegrationTests` (raw + managed
  populate/match-signature tests, plus same-input-twice determinism on both paths).
- **7b** (`5db337d`) - `CellRole` (Interior/Exterior/Sliver/Unknown) and `CellClassifier`
  (`SAM.Geometry.OCCT.Solver`). `CellClassifier.Classify(volume, minCellVolume, insideEnvelope)` is the
  pure, unit-tested decision (a truth table, no native kernel): volume below `MinCellVolume` is always
  Sliver regardless of location; otherwise Interior/Exterior/Unknown by an inside/outside flag, never
  defaulted to Interior when unevaluated. `CellClassifier.ClassifyCells(cells, resolvedFace3Ds,
  minCellVolume, options, diagnostics)` supplies that flag by building ONE extra `Create.Shells` decode of
  the SAME resolved faces with `AvoidInternalShapes = true` (collapsing internal partitions to the
  model's own outer envelope) and `RetainTopology = true`, then testing each cell's centre against that
  single outer solid via the existing `Query.IsPointInside` - reusing native metadata the kernel already
  exposes, no new entry point. **`RetainTopology` is only honoured by the native sew-then-MakerVolume
  path** (`OcctCellComplexBuilder.TrySewThenMakeVolume`) - the plain direct MakerVolume build discards its
  topology handle before returning - so the envelope build forces `SewBeforeBuild = true` regardless of
  the caller's own options; a caller that reuses the default direct-build options here would silently get
  `Unknown` for every cell. Purely additive and opt-in: `Panel3DSnapSolver.Execute` never calls it, so
  every existing solve is unaffected. Unit: `CellClassifierTests` (the truth table). Integration:
  `CellClassificationIntegrationTests` (a hand-built hairline sliver alongside its real-room sibling -
  the sliver classifies Sliver and is diagnosed, the room classifies Interior; a genuine two-room box
  classifies both Interior; all 22 real rooms in `whole-level-flat.sam` classify Interior).
- **7c** (`7bdc355`) - `Create.Spaces` (`SAM.Analytical.OCCT.Solver`). Solves via the existing
  `Modify.Solve3D`, classifies via `CellClassifier`, then builds the FULL adjacency cluster via the
  existing `SAM.Analytical.OCCT.Create.AdjacencyCluster(panels, ...)` entry point (the Tower prior art) -
  not reimplemented - and removes the `Space` for every non-Interior cell (matched back to its cell by
  centre location, since `RelationCluster`'s object storage order is not a documented guarantee;
  `RemoveObject` cleans up its panel relations automatically). `SAM.Analytical.OCCT.Solver` gained a
  project reference to `SAM.Analytical.OCCT` for this (no cycle: that project does not reference back).

  **Closure gate.** `Create.ShouldRefuseSpaces(nakedEdgeCount)` - a one-line pure predicate, unit-tested
  as its own truth table - refuses to create ANY space when the resolved geometry has one or more naked
  (free) boundary edges; the method then returns null with a `DiagnosticCode.SpacesRefused` diagnostic
  instead of building spaces on an incomplete cell complex. This is why `Create.Spaces` on
  `two-level-tilted.sam` behaves differently by path: the raw solve (43 cells / 0 naked, the pinned golden
  master) produces 43 spaces, while forcing the managed pipeline (29 cells / 29 naked, the pinned 7-pre
  baseline) is refused outright - a degraded managed result never silently becomes 29 spaces.

  **Cell exclusion.** A `Sliver`/`Exterior`/`Unknown` cell does not become a Space; each exclusion emits a
  `DiagnosticCode.CellExcludedFromSpaces` Info diagnostic (in addition to the classifier's own
  `SliverCell` diagnostic for the Sliver case) - never silent.

  **Air policy.** `Modify.Solve3D` already excludes input air panels from solving and does not re-add
  them to its own output; `Create.Spaces` collects the caller's original air panels before solving and
  rejoins them - together with every solver-fabricated `PanelType.Air`/`Provenance=GapFill` panel already
  in the solved output - into the returned cluster unchanged. Neither kind of air panel ever gains or
  splits a Space.

  Unit: `CreateSpacesTests` (the closure-gate truth table). Integration: `CreateSpacesIntegrationTests` -
  `whole-level-flat.sam` yields exactly 22 spaces whose count/panel-count match a reference
  `AdjacencyCluster` built directly on the same solved panels (no cells excluded on this fixture);
  `two-level-tilted.sam` raw yields 43 spaces (all interior); the SAME fixture's managed path (29 naked)
  is refused with a `SpacesRefused` diagnostic; a synthetic single-room box with one extra `PanelType.Air`
  panel yields exactly 1 space and the air panel surfaces unchanged in the output panels.

**Verification run (local, native present), cumulative across 7a-7c.**

```
dotnet build SAM_OCCT.sln -c Debug                                              # 0 errors
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj                # 412 passed, 0 failed
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj  # 137 passed, 1 skipped
```

All 10 golden-master signatures unchanged (raw byte-identical 5/5; managed matching the 7-pre pinned
baseline 5/5); Phase 5f `Benchmark1500` unaffected. No native ABI changes; no Grasshopper changes -
`SolverCell`/`CellClassifier`/`Create.Spaces` are new managed-only surfaces over metadata the kernel
already exposes.

### Running Phase 7 tests

```powershell
# Cell metadata surfacing (integration, native-gated)
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj --filter "FullyQualifiedName~SolverCellIntegrationTests"

# Cell classification (unit truth table + integration, native-gated)
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj --filter "FullyQualifiedName~CellClassifierTests"
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj --filter "FullyQualifiedName~CellClassificationIntegrationTests"

# Spaces handoff / closure gate (unit truth table + integration, native-gated)
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj --filter "FullyQualifiedName~CreateSpacesTests"
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj --filter "FullyQualifiedName~CreateSpacesIntegrationTests"

# Golden masters (raw + managed, all 5 fixtures) and the Phase 5f performance guard
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj --filter "FullyQualifiedName~GoldenMasterIntegrationTests|FullyQualifiedName~PerformanceGuardIntegrationTests"
```

### Prerequisites for Phase 8 (Grasshopper staged exposure)

Per `docs/P6_ARCHITECTURE_REVIEW.md` §M, most §J staged outputs already exist on `Panel3DSnapSolver`
(`CleanFace3Ds`, `ResolvedFace3Ds`, `NakedWires`, `SourceMap`, `Diagnostics`, `Signature`, and now `Cells`
from Phase 7a). Still to expose before/during Phase 8: `LevelFrame` info (frame count, per-frame
elevation/tilt) is computed but never surfaced on the solver; `CellRole` classification results
(currently computed on demand by callers of `CellClassifier.ClassifyCells`, not cached on
`Panel3DSnapSolver` itself); and a closure-report string combining adopted level/rounds/cell-classification
counts. None of these require new native ABI or block Phase 8 from starting - they are additive surface
area, consistent with every other Phase 7/7-pre change.

## Grasshopper staged solver outputs (Phase 8)

Phase 8 (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §E Phase 8) exposes the true-3D solver's
staged/diagnostic output in Grasshopper for MEP-engineer inspection - **UI/API exposure only**: no
solver geometry, algorithm, or native ABI changed anywhere in this phase. Landed as three sub-steps,
each its own commit with both suites green and golden masters re-run first (all raw signatures
byte-identical throughout; all managed signatures matching the 7-pre pinned baseline throughout - Phase
8 never touches solver geometry, only reads and formats its output):

- **8a** (`336d848`) - foundation. `Panel3DSnapSolver` gained two additive, read-only captures of data
  it already computes: `RawAdopted` (true when the raw-first attempt was adopted, set at the existing
  early-return branch - no new branch, no behaviour change) and `LevelFrames` (the `LevelFrame` list
  `SnapStage.Clean` already clusters for frame-aware cap normalization, now also stored on the solver
  instead of being discarded after use). `SAM.Geometry.OCCT.Solver.ClosureReport.Format(...)` is a pure
  formatter (adopted path, raw/final signatures, AutoTune round counts, diagnostics counts by severity,
  level frame summary, "Timings: not tracked") over data the solver already produces. `SAM.Analytical.OCCT.Solver.
  Solve3DReport` bundles every staged field (signatures, diagnostics, source map, cells, cell roles, naked
  wires, Stage A clean faces, level frames, native-resolved/cell-count, AutoTune round counts, and the
  formatted closure report text) into one DTO, with `SolverReportFormat` supplying the shared
  `FormatDiagnostics`/`FormatSourceMap`/`FormatLevelFrames`/`FormatCells` string formatters reused by every
  component below. Unit: `ClosureReportTests`, `SolverReportFormatTests`, `Solve3DReportTests`.
- **8b** (`3120a40`) - `SAMOCCTSolve3D`. `Modify.Solve3D` gained a new most-detailed overload
  (`out Solve3DReport report`, plus opt-in `classifyCells`/`minCellVolume` parameters) built the same way
  as the Phase 4 aperture-orphan overload: the existing 2-out and 3-out overloads become thin wrappers
  delegating to it with `out _`, so their signatures, behaviour and compiled call sites are completely
  unchanged. The component appends ten `Voluntary` outputs (`CleanFaces`, `GapFillPanels`, `Cells`,
  `CellCentres`, `CellVolumes`, `CellClassification`, `NakedWires`, `SourceMap`, `LevelFrames`,
  `ClosureReport`) and two `Voluntary` inputs (`classifyCells_` default false, `minCellVolume_` default
  0.05) after the existing six outputs/eight inputs, which are untouched in name, type and order. Unit:
  none new (GH component bodies are not unit-testable without Rhino). Integration:
  `Solve3DReportIntegrationTests` (raw-adopted vs forced-managed report shape, `classifyCells` populating
  index-aligned `CellRoles`, empty-input report never null).
- **8c** (`7555e9f`) - `SAMOCCTClean3D`/`SAMOCCTExtend3D`/`SAMOCCTAutoTune3D`. `Modify.Clean3D`/`Extend3D`
  each gained the same additive `out Solve3DReport report` overload pattern (existing overloads become
  thin wrappers); since neither pass runs a native resolve, `Signature`/`Cells`/`NakedWires` stay
  null/empty on their reports - an honest reflection of the pipeline stage, not a gap. Both components
  append `Voluntary` `SourceMap`/`LevelFrames` outputs. `SAMOCCTAutoTune3D` is a **new** component - the
  Phase 5e `AutoTune3D` diagnosis-driven closure solver had an analytical entry point
  (`Modify.AutoTune3D`) but no Grasshopper component before this phase. `Modify.AutoTune3D` gained the
  same detailed-overload pattern; the component takes the usual panels/bucket/align/normalize inputs plus
  `maxRounds_`/`maxExtendLadder_`/`escalateBucket_` (mapping to `AutoTune3DOptions`) and outputs tuned
  panels, naked points/wires, diagnostics, source map, closure report, and round-attempted/accepted
  counts. Unit: none new. Integration: `StageReportIntegrationTests` (Clean3D/Extend3D report shape;
  AutoTune3D's report on a watertight baseline matches the "0 rounds, matches Solve3D" contract).

**Verification run (local, native present), cumulative across 8a-8c.**

```
dotnet build SAM_OCCT.sln -c Debug                                                          # 0 errors
dotnet build Grasshopper/SAM.Analytical.Grasshopper.OCCT/SAM.Analytical.Grasshopper.OCCT.csproj -c Debug  # 0 errors
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj                             # 432 passed, 0 failed
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj               # 144 passed, 1 skipped
```

All 10 golden-master signatures unchanged (raw byte-identical 5/5; managed matching the 7-pre pinned
baseline 5/5); Phase 5f `Benchmark1500` unaffected. No native ABI changes; no solver geometry/algorithm
changes - every Phase 8 change is either a pure read-only capture of already-computed data (`RawAdopted`,
`LevelFrames`), a pure formatter, an additive method overload, or an append-only Grasshopper parameter.

### Deviations from the plan's literal Phase 8 wording (scope-clarifications, not scope-cuts)

1. **"Clean/snapped panels"** are exposed as raw `Face3D` geometry (`CleanFaces`, via `GooSAMGeometryParam`
   on `SAMOCCTSolve3D`) rather than fully-reconstructed `Panel`s with Guid/construction/parameters. Building
   full Panels from Stage A output would duplicate `SAMOCCT.Clean3D`'s own `BuildPanels` call inside
   `SAMOCCT.Solve3D` for no added information the dedicated Clean3D component doesn't already give a user
   who wants clean Panels specifically.
2. **"Conditioned/pre-resolve panels"** are not duplicated inside `SAMOCCTSolve3D`. They are already
   exposed by the existing `SAMOCCT.Extend3D` component (itself enhanced in 8c with `SourceMap`/
   `LevelFrames`), and adding a second internal solve pass inside Solve3D to recompute them would be
   exactly the "second expensive solve" the plan's own wording says to avoid.
3. **"Retained dropped panels"** are not surfaced as their own dedicated output. `PanelReconstruction`
   stamps no Panel-level provenance tag distinguishing a retained-dropped face after reconstruction (only
   `SourceGuid`/`MergedSourceGuids`/`Provenance=GapFill` are stamped), so isolating them cheaply from the
   returned `Panels` list alone is not possible without new plumbing into `PanelReconstruction`. The
   `SourceMap` output already shows every source's `DroppedRetained` provenance line - the same "derivable
   via provenance, acceptable" resolution TESTING.md's Phase 7 section already used for the equivalent
   question there.
4. **Cells** are exposed as boundary `Shell`s (via `GooSAMGeometryParam`, matching how `Slits` already wraps
   arbitrary SAM geometry) rather than "cell panels" - a per-cell `Shell` is exactly what the native decode
   produces; a per-cell `Panel`/`Space` concept only exists after `Create.Spaces` (Phase 7c) runs, which is
   a separate, heavier operation intentionally not invoked from inside `Solve3D`.
5. **Cell classification is opt-in** (`classifyCells_` input, default false) because it costs one extra
   native envelope decode (`CellClassifier.ClassifyCells`, Phase 7b) per solve - not run unconditionally so
   a definition that never wires up `CellClassification` pays no extra native cost.
6. **`LevelFrames` is empty whenever the raw-first attempt is adopted** (`RawAdopted = true`): the raw path
   never clusters caps into frames - only the managed pipeline's `SnapStage.Clean` does, for frame-aware cap
   normalization. This is an honest reflection of the current architecture (`docs/P6_ARCHITECTURE_REVIEW.md`
   §D.1/§M), not a Phase 8 gap; a well-modelled model that raw-adopts will simply report zero frames.
7. **`SAMOCCTAutoTune3D`'s report has empty `Cells`/`LevelFrames`/`CleanFace3Ds` and `RawAdopted = false`
   always.** `AutoTune3DSolver`'s own public surface (Phase 5e) does not track per-cell metadata, level
   frames, or whether its internal baseline attempt was raw-adopted - extending that surface for this
   UI-only phase would touch the highest-risk algorithm class in the codebase (per the Phase 5/6 design
   reviews' own risk framing) for a reporting-only benefit, so it was deliberately not done. The fields are
   honestly empty/false, never fabricated.
8. **Diagnostics/source-map/level-frame outputs are flat, formatted `Param_String` lists**, not native
   Grasshopper data trees (`GH_Structure` branches per source/stage). No component in this Grasshopper
   project uses `GH_Structure` trees anywhere; every existing structured-output component (including
   `SAMOCCTCreateAdjacencyClusterByShells`'s own diagnostics) already uses flat formatted string lists.
   Matching that convention avoids introducing a new UI paradigm the hard scope boundaries prohibit ("No
   broad UI redesign").
9. **"Timings" in the closure report always reads "not tracked".** `SolverDiagnostic.ElapsedMs` exists on
   the diagnostic model but no current diagnostic emission call in the solver ever populates it (every call
   site passes the 0 default) - reported honestly rather than fabricating a number.

### Backwards compatibility

Every new output on `SAMOCCTSolve3D`/`SAMOCCTClean3D`/`SAMOCCTExtend3D` is `ParamVisibility.Voluntary`.
`GH_SAMVariableOutputParameterComponent.RegisterOutputParams` only auto-registers `ParamVisibility.Default`-
flagged (i.e. `Binding`) parameters (`SAM.Core.Grasshopper.ParamVisibility`: `Binding = Mandatory | Default`,
`Voluntary = 0`) - the same mechanism every existing optional input (`thicknessFactor_`,
`alignColinearOffset_`, ...) already relies on. A Grasshopper document saved before Phase 8 deserializes its
components with exactly the parameter sockets it was saved with; Phase 8's new Voluntary outputs are not
added to an old document automatically, so old wiring keeps solving identically. A user opts into the new
outputs by right-clicking the component and adding the parameter from the menu (`CanInsertParameter`/
`CreateParameter`), the same gesture already used for every pre-Phase-8 optional input on these components.
No existing input/output name, type, or order changed anywhere in Phase 8.

### Manual Grasshopper verification checklist

Native-dependent; run in Rhino/Grasshopper with `build-native.ps1` already run once (see "Running locally"
above) so `SAM.Occt.Native.dll` is on the search path the GH plugin loads from.

**Build the Grasshopper project:**

```powershell
dotnet build Grasshopper/SAM.Analytical.Grasshopper.OCCT/SAM.Analytical.Grasshopper.OCCT.csproj -c Debug
```

Copy/symlink the built `SAM.Analytical.Grasshopper.OCCT.gha` (and its SAM_OCCT dependencies) into Rhino's
Grasshopper `Libraries` folder, or point Grasshopper's plugin search path at the build output, then start
Rhino.

1. Drop a `SAMOCCT.Solve3D` component - it should place with all 6 original outputs
   (Panels/NakedPoints/Slits/SlitPanels/Diagnostics/Successful) and no parameter errors.
2. Open (or rebuild from memory) a definition using only those 6 outputs wired to panels/points/text
   readouts - it should still solve, with `Successful` = true on a well-modelled test model.
3. Right-click the component -> Zoom (or the parameter list) -> add `CellCentres`/`CellVolumes`/
   `CellClassification`/`Cells`; wire `classifyCells_` = true; on a closed multi-room model, confirm the
   centre/volume/role lists are the same length and each `Cells` shell visibly matches its centre.
4. On the same closed model, add `ClosureReport` - confirm it contains `Adopted path: Raw` (or `Managed`),
   a `Final:` line with the cell/volume/naked-edge counts, and (if applicable) an `AutoTune rounds:` line.
5. Build (or reuse) a deliberately gappy model (a wall pulled short of its neighbour); confirm `Diagnostics`
   lists `SAM_OCCT_SOLVE3D_DIAGNOSTIC:` lines describing the gap/rejection, `NakedPoints` is non-empty, and
   the new `NakedWires` output shows the open loop as a polyline.
6. Add `CleanFaces` on the same gappy model (which should fall through to the managed pipeline) - confirm
   it is non-empty and represents the Stage A geometry; on the closed model from step 3 (which raw-adopts),
   confirm `CleanFaces` is empty (see deviation 6 above) and `LevelFrames` is also empty.
7. Add `SourceMap` - confirm one line per input panel naming its resolved output face(s).
8. Rename/remove `SAM.Occt.Native.dll` from the search path (or run on a machine without it) and re-run:
   confirm the component reports `Successful = false` with a native-missing diagnostic rather than
   crashing Rhino - the existing graceful-degrade contract, unchanged by Phase 8.
9. Repeat steps 1-2 for `SAMOCCT.Clean3D` and `SAMOCCT.Extend3D` (their pre-Phase-8 outputs unchanged), then
   add their new `SourceMap`/`LevelFrames` outputs and confirm they populate.
10. Drop a new `SAMOCCT.AutoTune3D` component on the gappy model from step 5: confirm `Panels`/`Successful`
    behave like `Solve3D`, `Rounds`/`RoundsAccepted` are populated, and `ClosureReport` names the escalation
    round counts. On the closed model from step 3, confirm `Rounds` = 0 (AutoTune never engages on an
    already-watertight baseline).

### Remaining work (Phase 9)

Phase 8 is UI/API exposure only and made no performance change. Carried forward to Phase 9 (per
`docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md` §E Phase 9): the timing harness over the Phase 5f
benchmark fixture (per-stage ms in diagnostics - the reason this phase's `ClosureReport` still reads
"Timings: not tracked"), the Stage-A spatial index, `GlueMode=Shift` on escalation re-runs, and the
`O3`/`O4`/`O7` hygiene items from `docs/P6_ARCHITECTURE_REVIEW.md` §O ("Should/Can fix"). The deferred
per-level-frame extend/fill work (§6c/§6e stop rules) remains untouched and out of scope for Phase 8, as
required.

### Phase 9 closeout (performance/hardening audit and PR readiness, 2026-07-04)

Phase 9, as scoped for this closeout pass, is a performance-verification/hardening/documentation audit
over the completed Phases 0-8 - **not** the full spatial-index/`GlueMode`/timing-harness rewrite the
plan's §E Phase 9 originally sized (that remains a legitimate, larger follow-up; see below). The audit:

- **Performance verification** - `Benchmark1500`/`SAM_OCCT_SKIP_PERF` were already fully documented in
  Phase 5f ("Benchmark1500 performance guard" above); re-confirmed this pass (build 0 errors, `Benchmark1500`
  passes in ~6 s against the 90 s ceiling, `SAM_OCCT_SKIP_PERF=1` correctly skips it) - **already complete,
  no doc changes needed**.
- **Test verification** - full regression re-run: build 0 errors (solution + Grasshopper project), unit
  **432/432**, integration **144 passed / 1 skipped**, all 10 golden-master signatures (raw byte-identical
  5/5, managed matching the pinned 7-pre baseline 5/5) confirmed via `GoldenMasterIntegrationTests`.
- **Hardening audit** - reviewed every Phase 8 addition (`ClosureReport`, `Solve3DReport`,
  `SolverReportFormat`, the `Modify.Solve3D`/`Clean3D`/`Extend3D`/`AutoTune3D` report overloads, all four
  Grasshopper components) for native handle lifetime risk (none found - `CellClassifier.ClassifyCells`'s
  envelope decode, the only native call Phase 8 added a path to via `classifyCells_`, already disposes its
  `OcctCellComplexResult` in a `try`/`finally`, unmodified since Phase 7b), repeated/eager expensive
  recomputation (none found - every `Solve3DReport` is built exactly once per solve, on exactly one control
  path, and `classifyCells_` defaults to false so no extra native decode runs unless explicitly requested),
  and null-safety in report formatting (one minor inconsistency found and fixed: `SAMOCCTSolve3D`'s
  `CellVolumes` output was missing the null-element guard its sibling `Cells`/`CellCentres` outputs already
  had - `report?.Cells?.Select(x => x.Volume)` -> `report?.Cells?.Where(x => x != null).Select(x => x.Volume)`;
  a defensive fix, not a fix for an observed failure, since `SolverCell` list entries are never actually
  null under normal operation).
- **Documentation** - this section, the "Full regression" command block above, and the PR review checklist
  below are the only additions; every other Phase 9 documentation requirement (Benchmark1500/skip-seam,
  the manual Grasshopper checklist) was already satisfied by Phase 5f/Phase 8 and is not duplicated here.

**Known follow-ups (unchanged in substance from the Phase 8 note above, restated per the closeout brief):**

1. **Safer per-frame extend/fill conditioning** - the Phase 6c deferral (condition in a dominant frame,
   split only across proven-separate storeys) remains untouched; resume only per the TESTING.md §6e stop
   rules.
2. **Real gappy multi-storey fixtures** - the plan's own "as they become available" item (§K, Phase 6
   review); the synthetic perturbed-towers route was invalidated in 5f. Still blocked on real models.
3. **Optional native CI** - CI still builds managed-only (`SAM_OCCT_SKIP_NATIVE_BUILD=true`); native-gated
   integration tests are run locally per this document's protocol, an owner decision unchanged since Phase 0.
4. **Optional `Panel3DSnapSolver` façade slimming** - `docs/P6_ARCHITECTURE_REVIEW.md` §O item `O7`
   (extract `FinalizeAndValidate` + legacy statics out of the ~2,300-line façade, behaviour-preserving);
   still "can defer," not required by any phase's acceptance criteria.
5. **Deferred codex review findings (PR #48)** - eight inline findings from the `chatgpt-codex-connector`
   bot were assessed against the current code and recorded (with line numbers, mechanism, trigger, and
   fix direction) in `docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md` "Deferred codex review findings
   (PR #48)". All are **latent** - none fires on the five golden-master fixtures - and all are
   pre-existing defects in the Phase 5 heal/reconstruct and raw-adoption paths (air-panel/gap-fill
   provenance and emission consistency, the rebuild cell-count gate, the raw-adoption gate's under-split
   blind spot, plus one native history-ordinal issue), not regressions from Phases 8-9. Each needs a
   targeted fail-before/pass-after fixture; five of them cluster into one air/gap-fill sub-phase and
   several become reproducible once follow-up 2 (real gappy multi-storey fixtures) lands. Two other
   findings from the same review were already fixed (`b1eca3a`: `Execute` per-run reset of
   `NakedWires`/`ResolveHistorySourceMap`, and the split+merge Guid collision in `PanelReconstruction`;
   `ExecuteResetIntegrationTests` + a new `PanelReconstructionTests` case cover them).

The original §E Phase 9 performance work (timing harness, Stage-A spatial index, `GlueMode=Shift` on
escalation re-runs, `O3`/`O4` hygiene) remains a separate, larger, legitimate follow-up beyond this
closeout's audit-only scope - listed above in "Remaining work (Phase 9)" and not reopened here.

### PR review checklist (Phase 9)

Copy into the PR description; every item below was verified during the Phase 9 closeout pass.

- [ ] `dotnet build SAM_OCCT.sln -c Debug` - 0 errors.
- [ ] `dotnet build Grasshopper/SAM.Analytical.Grasshopper.OCCT/SAM.Analytical.Grasshopper.OCCT.csproj -c Debug` - 0 errors.
- [ ] `dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj` - all passed, 0 failed.
- [ ] `dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj` - all passed, only the
  inverse-gated native-missing test skipped (native present) or every native-gated test skipped (native
  absent).
- [ ] `GoldenMasterIntegrationTests` - all 10 raw/managed signatures unchanged from the pinned 7-pre baseline.
- [ ] `PerformanceGuardIntegrationTests` (`Benchmark1500`) passed under the 90 s ceiling, or was skipped only
  via `SAM_OCCT_SKIP_PERF`.
- [ ] No native ABI change (`sam_occt_abi_version` unchanged; no new/removed native entry points).
- [ ] No solver geometry/algorithm change (`ConditionStage`/`ResolveStage`/`HealStage` untouched, unless the
  PR explicitly says otherwise with a golden-master delta explained).
- [ ] Grasshopper compatibility preserved: every new component output/input is `ParamVisibility.Voluntary`
  (or a brand-new component); no existing input/output renamed, retyped, reordered, or removed.
- [ ] Docs (`TESTING.md`, `docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md`) updated to match what actually
  shipped, with commit IDs recorded.
- [ ] Working tree clean after the final commit.

## CellComplex-first workflow parity (docs/CELLCOMPLEX_FIRST_HANDOVER.md, Phase P1)

P1 makes workflow A measurable against the OCCT CellComplex and fixes a production options
mismatch, without touching solver geometry or any adoption gate. Two changes:

1. **GH options bugfix.** `SAMOCCTCreateAdjacencyCluster` and `SAMOCCTCreateAdjacencyClusterByShells`
   built their cell complex with `OcctBuildOptions`' bare defaults
   (`AvoidInternalShapes=true, SewBeforeBuild=false, SewingTolerance=0.0`), diverging from every
   solver/test call site, which builds with the **solver-matched options**
   (`AvoidInternalShapes=false, SewBeforeBuild=true, SewingTolerance=0.01`). Both components now use
   the solver-matched recipe by default (`SAMOCCTCreateAdjacencyClusterByShells`'s `sew_` toggle
   default flipped from `false` to `true`; it stays overridable per-run).
   A later robustness guard keeps this options-parity behaviour for normal fixtures while preventing
   Rhino-host crashes on very large analytical panel soups: `OcctBuildOptions.MaxSewFaceCount`
   defaults to `2048`, so pre-build native sew is skipped with `SAM_OCCT_SEW_SKIPPED_LARGE_INPUT`
   when the face count is above that value, and the full input is built once through the direct
   MakerVolume path. The threshold is empirical, not an OCCT kernel limit: it is deliberately far
   above the current P1 fixture set (largest `.sam` fixture is under 300 analytical panels) and below
   the Talybont regression file (`Panels-InputForAdjac1.sam`, 6927 valid panels), which previously
   stack-overflowed inside `sam_occt_sew_faces`. A local scratch-run verification on that file built
   directly in about 200.7s, producing 1740 spaces / 9076 panels and the sew-skip diagnostic.
2. **Parity diagnostic.** `Create.AdjacencyCluster`'s `DirectAdjacencyCluster`
   (`SAM_OCCT_ANALYTICAL_PARITY`) counts, on every direct rebuild: relations added vs. expected
   (`2 x |FaceAdjacencies| + envelope faces`, i.e. every shared face owned by two cells contributes
   two relations and every face owned by exactly one cell contributes one); faces with
   `TopologyKey == 0` (silently skipped by the relation loop otherwise); and panels that ended up
   with zero relations. Counting only - no rebuild behaviour changes. `TopologyKey` is a per-decode
   signature (`OcctCellComplexResult.BuildFaceAdjacencies`), so this never compares keys across two
   different decodes.

### Workflow parity harness

`Testing/SAM.OCCT.IntegrationTests/WorkflowParityIntegrationTests.cs` runs all 9 fixtures through
three workflows built from the same source panels, each finished with
`MergeCoplanarPanels(AdjacencyCluster)`:

- **A (old defaults)** - `Solve3D` -> `Create.AdjacencyCluster` with the pre-P1 default options.
  Logged for the bugfix comparison only; never asserted as the workflow that should be correct.
- **A (solver-matched)** - the same solved panels, rebuilt with the solver-matched options the P1
  bugfix now uses in production.
- **B (Clean3D -> Extend3D)** - the pre-conditioned pipeline that never runs Solve3D's own native
  resolve, finished the same way.

For A-solver-matched and B, the harness asserts: the cluster's space count equals the solver's own
`Solve3DReport.ResolvedCellCount`; the parity diagnostic is clean; and the set of spaces each panel
relates to (an unordered space-pair for an internal panel, a single space for an envelope panel) is
identical before and after `MergeCoplanarPanels` - space `Guid`s survive the merge
(`SAMObject(string name, SAMObject)` preserves `Guid`), so this is a real identity comparison, not a
count comparison. Where a fixture/workflow is broken today, the harness pins the **current** value
via an explicit `Expectations` entry with a tracking comment - it never skips silently.

**Result of this harness turning up real E2E coverage of `MergeCoplanarPanels(AdjacencyCluster)`
for the first time** (previously zero, per the CellComplex handover's diagnosed seam): the relation
invariant holds on all 9 fixtures for both solver-matched workflows today. The gaps this harness
found are all in workflow closure (space count), not in the merge.

### Per-fixture table (2026-07-09, post-E2, native present)

Verbatim `WorkflowParityIntegrationTests` output (its `FormatWorkflow` rows). Refreshed after E2 landed
(the 2026-07-07 snapshot predated it); **E3 verified this is byte-identical** — the observability phase
changes no geometry, so re-running the harness reproduces these rows exactly.

| Fixture | Input panels | Solver `ResolvedCellCount` (naked) | Workflow A (old defaults) | Workflow A (solver-matched) | Workflow B (Clean3D→Extend3D) | Dropped/Retained (solve) | Verdict |
| --- | --- | --- | --- | --- | --- | --- | --- |
| whole-level-flat.sam | 148 | 22 (naked=0) | OCCT cells=22 faces=134 adj=2; SAM spaces=22 panels=134 (int=2 ext=132 orphan=0) parity=clean merged=134 | OCCT cells=22 faces=134 adj=2; SAM spaces=22 panels=134 (int=2 ext=132 orphan=0) parity=clean merged=134 | OCCT cells=22 faces=237 adj=22; SAM spaces=22 panels=237 (int=22 ext=215 orphan=0) parity=clean merged=142 | 16/0 | OK |
| tilted-two-spaces.sam | 16 | 2 (naked=0) | OCCT cells=2 faces=12 adj=0; SAM spaces=2 panels=12 (int=0 ext=12 orphan=0) parity=clean merged=12 | OCCT cells=2 faces=12 adj=0; SAM spaces=2 panels=12 (int=0 ext=12 orphan=0) parity=clean merged=12 | OCCT cells=2 faces=19 adj=1; SAM spaces=2 panels=19 (int=1 ext=18 orphan=0) parity=clean merged=12 | 4/0 | OK |
| whole-level-tilted.sam | 148 | 22 (naked=0) | OCCT cells=22 faces=199 adj=22; SAM spaces=22 panels=199 (int=22 ext=177 orphan=0) parity=clean merged=146 | OCCT cells=22 faces=173 adj=22; SAM spaces=22 panels=173 (int=22 ext=151 orphan=0) parity=clean merged=143 | OCCT cells=22 faces=234 adj=22; SAM spaces=22 panels=234 (int=22 ext=212 orphan=0) parity=clean merged=146 | 30/0 | OK |
| two-level-tilted.sam | 287 | 44 (naked=0) | OCCT cells=44 faces=266 adj=2; SAM spaces=44 panels=266 (int=2 ext=264 orphan=0) parity=clean merged=266 | OCCT cells=43 faces=261 adj=1; SAM spaces=43 panels=261 (int=1 ext=260 orphan=0) parity=clean merged=259 | OCCT cells=25 faces=402 adj=46; SAM spaces=25 panels=398 (int=44 ext=354 orphan=0) parity=WARN merged=174 | 22/0 | both workflows diverge from solver cell count |
| whole-level-towers.sam | 215 | 32 (naked=0) | OCCT cells=32 faces=192 adj=0; SAM spaces=32 panels=192 (int=0 ext=192 orphan=0) parity=clean merged=192 | OCCT cells=32 faces=192 adj=0; SAM spaces=32 panels=192 (int=0 ext=192 orphan=0) parity=clean merged=192 | OCCT cells=28 faces=320 adj=27; SAM spaces=28 panels=320 (int=27 ext=293 orphan=0) parity=clean merged=184 | 23/0 | workflow B under/over-closes |
| AdjacencyCluster-home.sam | 106 | 18 (naked=4) | OCCT cells=18 faces=115 adj=47; SAM spaces=18 panels=115 (int=47 ext=68 orphan=0) parity=clean merged=106 | OCCT cells=18 faces=115 adj=47; SAM spaces=18 panels=115 (int=47 ext=68 orphan=0) parity=clean merged=106 | OCCT cells=18 faces=112 adj=49; SAM spaces=18 panels=112 (int=49 ext=63 orphan=0) parity=clean merged=106 | 0/0 | OK |
| Face3D-home.sam | 124 | 20 (naked=4) | OCCT cells=20 faces=113 adj=47; SAM spaces=20 panels=113 (int=47 ext=66 orphan=0) parity=clean merged=113 | OCCT cells=19 faces=108 adj=46; SAM spaces=19 panels=108 (int=46 ext=62 orphan=0) parity=clean merged=108 | OCCT cells=20 faces=115 adj=47; SAM spaces=20 panels=115 (int=47 ext=68 orphan=0) parity=clean merged=114 | 0/0 | workflow A under/over-closes |
| Revit-home-panels.sam | 39 | 14 (naked=0) | OCCT cells=14 faces=75 adj=30; SAM spaces=14 panels=75 (int=30 ext=45 orphan=0) parity=clean merged=69 | OCCT cells=14 faces=74 adj=29; SAM spaces=14 panels=74 (int=29 ext=45 orphan=0) parity=clean merged=69 | OCCT cells=9 faces=49 adj=17; SAM spaces=9 panels=49 (int=17 ext=32 orphan=0) parity=clean merged=47 | 12/0 | workflow B under/over-closes |
| three-spaces.sam | 19 | 3 (naked=0) | OCCT cells=3 faces=18 adj=0; SAM spaces=3 panels=18 (int=0 ext=18 orphan=0) parity=clean merged=18 | OCCT cells=3 faces=18 adj=0; SAM spaces=3 panels=18 (int=0 ext=18 orphan=0) parity=clean merged=18 | OCCT cells=3 faces=28 adj=2; SAM spaces=3 panels=28 (int=2 ext=26 orphan=0) parity=clean merged=19 | 1/0 | OK |

The solver `ResolvedCellCount (naked)` column is production `Solve3D` (raw-first); the managed
`Extend3DPlaneTargetIntegrationTests` pins (Revit 14/0, AdjacencyCluster-home 18/4, Face3D-home 20/4,
two-level-tilted 32/32, `forceManagedPipeline`) are a *different* pipeline — consistent, not the same
number. Known-not-yet-closing rows are pinned in the harness's `Expectations` table with a tracking
comment (E2-current), never silently skipped:

- **E2's real-export wins.** `AdjacencyCluster-home` now closes 18/18 cleanly on **both** solver-matched
  workflows (was 25 naked upstream); `Revit-home-panels` closes production 14/0 (A-matched 14/14, B still
  9 vs 14); `Face3D-home` closes B 20/20 (A-matched 19 vs 20). Pitched roofs followed as clamped
  column-wise planes — see the E2 re-baseline table below.
- **two-level-tilted / whole-level-towers, workflow B:** still under-close (25 vs 44; 28 vs 32) —
  *rigidly tilted flat levels*, **byte-identical under E2** (their caps are flat-relative to their walls).
  Closing the residual needs *frame-aware* extension (extend along the level's tilted up-axis), deferred
  as a follow-up; this is not E2's plane-targeting nor E3's observability.

### Running the P1 harness

```powershell
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj --filter "FullyQualifiedName~WorkflowParityIntegrationTests"
```

## Robust Extend3D primitives (docs/EXTEND3D_ROBUST_HANDOVER.md, Phase E1)

E1 rebuilds the `SnappedPanel` extend/trim primitives (`ExtendTopTo`, `ExtendBottomTo`,
`SetVerticalFootprint`, `GetBaseSegment`; `ExtendHorizontal` retired) so they preserve a wall's
profile and openings instead of collapsing or verticalizing it. The old code re-extruded a flat
rectangle from a horizontal base cut, which turned a sloped/shifted/gable/M-top wall into a
degenerate sliver ("walls disappear after `SAMOCCT.Extend3D`", PR #49) and verticalized a tilted
wall (dropping its plane).

**Mechanism (plane-ops, reusing SAM-core primitives):**

- A plain **vertical rectangle** (4 corners, horizontal top/bottom, vertical sides, no openings)
  takes a byte-identical **fast path** (the legacy straight-up re-extrude, frozen behind a private
  helper so it is insulated from the `GetBaseSegment` change).
- Any other profile takes **plane-ops**: `ExtendTopTo`/`ExtendBottomTo` call
  `Query.Extend(Face3D, horizontal plane at targetZ)` (base profile + openings + supporting plane
  preserved, flat top/bottom at the target for the kernel to re-cut); `SetVerticalFootprint` moves
  each plan end via `Query.Extend` (lengthen) or `Query.Cut` keeping the wall-body side (shorten).
  A trim that clips an opening emits `SAM_OCCT_EXTEND3D_HOLE_DROPPED` on the panel (never silent).
- `GetBaseSegment` now spans the **full plan extent** of all boundary points (not a horizontal cut
  that under-measures a door-notched foot), with a deterministic direction (longest edge → lower
  mean Z → lower index).

Unit tests assert **shape**, not bounding-box spans alone (a bbox check passes even under a shear):
gable/M-top reach the target without collapse, a 10-degree-tilted wall keeps its plane normal, a
window survives an extend and is diagnosed on a trim, and the parallelogram base/top tie resolves
deterministically. The three PR #49 (`26ac05a`) profile-preservation tests are cherry-picked in.

### Managed golden re-baseline (E1)

The **raw** golden master and the production **raw-first** path are byte-identical (they never call
these primitives - verified). The **managed** tripwire (`GoldenMasterIntegrationTests.ManagedFixtures`,
`forceManagedPipeline: true`) and workflow B share the conditioning code and the same walls, so the
fix necessarily changes the managed signatures. Per the golden-master contract (a managed pin is "a
tripwire, not a correctness assertion... a phase that changes either pipeline's closure must update
the expected values with a stated delta"), the three moved managed pins are re-baselined:

| Fixture | Before (cells/naked/vol) | After (cells/naked/vol) | Mechanism |
| --- | --- | --- | --- |
| whole-level-flat | 22 / 0 / 3479.897 | **unchanged** | all walls take the fast path or extend identically |
| tilted-two-spaces | 2 / 0 / 723.652 | **unchanged** | " |
| whole-level-tilted | 22 / 0 / 3377.828 | 22 / 0 / 3377.**841** | tilted walls keep their true plane instead of being verticalized (R5); cells/naked identical, volume +0.0004% |
| whole-level-towers | 22 / **12** / 8777.056 | 21 / **8** / 8689.707 | sloped/non-rectangular walls that collapsed to slivers now extend to full profile; **naked improved 12 → 8** |
| two-level-tilted | 29 / **29** / 2213.303 | 15 / **32** / 2307.863 | already-degraded managed fixture (raw-first, the production path, closes it 40+ / 0 and is unchanged); E2 (plane-target cap extension) targets the residual managed closure |

Workflow B (E1's actual target, measured by `WorkflowParityIntegrationTests`) mostly improved:
whole-level-towers 26 → 28 spaces, AdjacencyCluster-home now closes 18 / 18 cleanly (was 16),
Face3D-home's A-solver-matched parity warning cleared. Those harness rows are re-pinned with a
mechanism note; none are silently skipped.

### Fast-vs-plane-ops census

`Extend3DCensusIntegrationTests` records, per golden fixture, how many operations took each path
(observational, not a freeze gate - the fast path is deliberately NOT expected to fire for every
wall, since these fixtures contain the non-rectangular walls E1 fixes). Current split (native-free
`Extend3D`): whole-level-flat 120/56, tilted-two-spaces 11/7, whole-level-tilted 107/72,
two-level-tilted 230/107, whole-level-towers 183/37 (fast/plane-ops).

```powershell
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj --filter "FullyQualifiedName~Extend3DCensusIntegrationTests"
```

## Plane-target cap extension (docs/EXTEND3D_ROBUST_HANDOVER.md, Phase E2)

E2 makes `Panel3DSnapSolver.Extend` grow each wall to the ACTUAL cap surface instead of a flat Z at the
cap's ridge height. Cap SELECTION is the pre-E2 nearest-cap-over-the-wall-centre rule verbatim; what
changes is the TARGET, gated by a discriminator (`IsCapFlatRelativeToWall`):

- A cap **flat relative to its wall** (its normal aligned within 15° of the wall's own in-plane up-axis
  — a level floor/ceiling, *including a rigidly tilted level* where wall and slab tilt together) keeps
  the pre-E2 scalar extend to the cap elevation + overshoot. **Byte-identical.**
- A cap **pitched relative to the wall** (a real sloped roof over a vertical wall) is followed as a
  sloped plane (`SnappedPanel.ExtendTopToPlane`/`ExtendBottomToPlane`), so the wall gains a matching
  sloped top. The extension is **column-wise within the wall's own plan extent and clamped at the cap's
  real extreme + overshoot** (E2 review correction: the first implementation reused `Query.Extend`,
  whose extreme-perpendicular-projection construction is horizontal-target-only — on an inclined line it
  spilled sideways in plan past the wall's ends, the room-merge vector, and under-covered the high side
  as pitch grew; the clamp stops a cap plane extrapolated beyond the cap's physical extent from dragging
  the wall past what the cap can trim). The plane target uses the **small** wall overshoot — it meets
  the surface itself, unlike E1's flat-at-ridge scalar target which needs `roofOvershoot` to clear the
  pitch from below. Guards: parallel-plane, diving-plane (base/top preservation), wall-parallel slope
  and failed-union all fall back to the scalar path — never a throw, never a silent no-grow swallow.

**Key finding.** The tilted golden fixtures the phase originally named (two-level-tilted,
whole-level-towers) are *rigidly tilted flat levels*, not sloped-roof models — the discriminator
correctly routes their caps to the scalar path, so they are byte-identical. Improving those needs
*frame-aware* extension (extend along the level's tilted up-axis), a larger change deferred out of E2.
A naive world-Z plane target (tried in development) collapsed two-level-tilted to 3 cells / 254 dropped
— the discriminator is what prevents that.

### Managed golden re-baseline (E2)

All five **managed** golden pins (`GoldenMasterIntegrationTests.ManagedFixtures`) and the **raw** pins
are **byte-identical** under E2 (their caps are flat-relative). E2's improvement lands on the real-export
fixtures (genuine pitched roofs), pinned by `Extend3DPlaneTargetIntegrationTests` (managed `Solve3D`
report closure):

| Fixture | Before E2 (cells/naked) | After E2 (cells/naked) | Mechanism |
| --- | --- | --- | --- |
| whole-level-flat / tilted-two-spaces / whole-level-tilted / two-level-tilted / whole-level-towers | golden pins | **byte-identical** | caps flat-relative to walls → scalar path (incl. tilted levels) |
| Revit-home-panels | 12 / 4 | 14 / **0** | pitched roofs followed as clamped column-wise planes → watertight (**naked 4 → 0**) |
| AdjacencyCluster-home | 18 / **25** | 18 / **4** | same 18-cell decomposition, **naked 25 → 4** |
| Face3D-home | 25 / 3 | 20 / **4** | coarser managed decomposition; **+1 solver-internal naked** accepted as net-win (owner decision 2026-07-08) |

**Net across fixtures: −24 naked.** The one +1 (Face3D-home) is on the solver's internal closure metric
(that fixture has no golden pin); on the **workflow** metric every fixture stays parity-clean with zero
orphans — E2 adds *no* workflow-level naked regression. The gate was relaxed from "no naked increase on
any fixture" to "net non-increasing" by owner decision; the tradeoff is documented in the
`Extend3DPlaneTargetIntegrationTests` pins and the `WorkflowParityIntegrationTests` tracking comments.

### Workflow parity re-pins (E2)

`WorkflowParityIntegrationTests` rows changed by E2, each re-pinned with a mechanism note (no silent
skips): Revit-home-panels A-solver-matched closes cleanly 14/14, B 9 vs 14; AdjacencyCluster-home now
closes 18/18 cleanly on BOTH workflows (was 25 naked upstream); Face3D-home B closes cleanly 20/20,
A 19/20. The tilted fixtures' rows (two-level-tilted, whole-level-towers) are unchanged from E1.

```powershell
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj --filter "FullyQualifiedName~Extend3DPlaneTargetIntegrationTests"
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj --filter "FullyQualifiedName~Panel3DSnapSolverTests"
```

## Extend3D observability (docs/EXTEND3D_ROBUST_HANDOVER.md, Phase E3)

E3 makes every managed extend/fill mutation **observable**, with **zero geometry change**. The E1/E2
primitives are untouched; the solver merely *measures* each applied operation and hands it to the
report/Grasshopper layer. The recorder threads through the extend statics as an optional sink (a null
sink is byte-identical to a run without it — asserted by `Panel3DSnapSolverTests`).

**What is recorded** — one `ExtendRecord` (`SAM.Geometry.OCCT.Solver`) per *applied* operation (never a
no-op call), surfaced on `Panel3DSnapSolver.ExtendRecords` and `Solve3DReport.ExtendRecords`:

- panel identity: source Guid (resolved at the analytical layer — the geometry solver has no Guids) +
  solver index;
- operation kind: `top` / `bottom` (cap extend), `plan-start` / `plan-end` (lateral foot move),
  `cap-grow`;
- measured **from → to**: elevation / distance-to-plane for a cap extend, plan parameter for a foot
  move, area for a cap grow — each cross-checked in tests against the actual post-op geometry;
- target identity: the cap's solver index plus whether it took the **scalar** (E1 flat-Z, `cap-scalar`)
  or the **E2 sloped-plane** (`cap-plane`) branch, or the 2D `walls` plan-loop, or `fixed-margin`;
- overshoot applied, and the **lateral-cap flag** (set only when an *extended* foot end reached
  `min(MaxExtend, 0.49·length)` — vertical reach is MaxExtend-uncapped, so top/bottom are always false).

**Surfacing** — through the existing diagnostics/report path, so nothing new to plumb:

- coded `SAM_OCCT_EXTEND3D_PANEL:` lines (one per record, Guid-resolved) join the `diagnostics` list on
  `Extend3D`, `OpenPanels3D` and managed `Solve3D` runs (empty on a raw-adopted solve);
- `Solve3DReport.FormatExtendRecords()` and `Solve3DReport.ExtendPreviewSegment3Ds()` (moved-edge
  `Segment3D` from → to; cap grows have no single edge and contribute none);
- the E1-deferred `SAM_OCCT_EXTEND3D_HOLE_DROPPED` (a footprint trim clipping an opening, R6) is now
  flowed out of the panel to the same list — previously recorded but never surfaced;
- Grasshopper `SAMOCCT.Extend3D` gains two **append-only Voluntary** outputs — `ExtendReport` (the text
  lines) and `ExtendPreview` (the segments). Existing canvases load unchanged (component `0.5.0 → 0.6.0`).

**Diagnostics codes (extend track):** `SAM_OCCT_EXTEND3D_PANEL:` (per-op summary, E3),
`SAM_OCCT_EXTEND3D_HOLE_DROPPED:` (opening clipped by a trim, E1 — surfaced E3),
`SAM_OCCT_EXTEND3D_RESULT:` / `SAM_OCCT_EXTEND3D_OPEN_ENDS:` (pre-existing pass summaries).

**Acceptance (zero geometry change).** All raw + managed golden pins and the E2 real-export pins are
**byte-identical**; the full blast-radius suite is green with native present (**467 unit / 168
integration**, +1 native-missing skip). The per-fixture table above was refreshed and re-verified
against a fresh harness run. This phase moves nothing.

**Tests.** `SolverReportFormatTests` / `Solve3DReportTests` (the `SAM_OCCT_EXTEND3D_PANEL:` line format
+ Guid resolution + preview segments + the report round-trip); `Panel3DSnapSolverTests` (the extend
statics record moves that match the geometry, and a null recorder is byte-identical);
`Extend3DObservabilityIntegrationTests` (native-free end-to-end via `Extend3D` on a short-walled room —
every record matches an actual move and the coded lines surface).

```powershell
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj --filter "FullyQualifiedName~SolverReportFormatTests|FullyQualifiedName~Solve3DReportTests"
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj --filter "FullyQualifiedName~Extend3DObservabilityIntegrationTests"
```

### E-track summary (E1 · E2 · E3)

The robust-Extend3D track (`docs/EXTEND3D_ROBUST_HANDOVER.md`), interleaved with the CellComplex-first
program:

| Phase | One line | Managed re-baseline | Geometry |
| --- | --- | --- | --- |
| **E1** | Profile/hole-preserving plane-ops rebuild of the extend/trim primitives (no more flat-rectangle re-extrude) | [Managed golden re-baseline (E1)](#managed-golden-re-baseline-e1) — 3 pins moved, mechanism table | raw byte-identical; managed changed (necessary — same walls/code) |
| **E2** | Walls extend to the ACTUAL cap surface (sloped-plane branch, discriminator keeps tilted/flat levels scalar) | [Managed golden re-baseline (E2)](#managed-golden-re-baseline-e2) — real-export pins, **net −24 naked** | raw + managed goldens byte-identical; wins on pitched-roof exports |
| **E3** | Per-op observability (`ExtendRecord` → `SAM_OCCT_EXTEND3D_PANEL:` + GH outputs); hole-drop surfaced | none — verification pass | **byte-identical (moves nothing)** |

Raw golden signatures are byte-identical across **all three** phases; managed movement is confined to
the E1 and E2 re-baseline tables above (each row justified by mechanism), and E3 adds none. P4 gate
hardening branches off the post-E2 baseline — its false-positive judgements must use the current
post-E2 managed baselines (Revit 14/0, AdjacencyCluster-home 18/4, Face3D-home 20/4), not the plan's
original snapshot.

## ResolvedCellComplex product (docs/CELLCOMPLEX_FIRST_HANDOVER.md, Phase P2)

P2 turns the cell complex the solver validated into a first-class, pure-managed, serializable product
and consumes it directly, closing the diagnosed seam (solver builds the complex, then everything
downstream rebuilt it blind). Purely additive to the adopted geometry - all goldens stay
byte-identical (raw AND the E1-re-baselined managed pins).

- **`ResolvedCellComplex` DTO** (`SAM.Geometry.OCCT`): cells (index/volume/centre), unique faces
  (Face3D + per-decode `TopologyKey` + owner cell indices + flat ordinals), adjacency pairs, naked
  wires, and a `SolveId`. `IJSAMObject`-serializable (round-trips through `ToJsonObject`), carries no
  native lifetime. Projected ONCE from the adopted decode, before it is disposed.
- **Flat-ordinal bridge:** each owner records the face's position in the decode's `IsValid`-filtered
  flatten (`shells.SelectMany(Face3Ds).Where(...)`) - emitted by replicating that filter, never
  derived arithmetically from per-cell counts, so a decoded-but-invalid face does not drift the count
  (`ResolvedCellComplexTests.Project_InvalidFaceInCell_DoesNotAdvanceFlatOrdinal`).
- **Capture:** `Panel3DSnapSolver` publishes `ResolvedCellComplex`/`SolveId` at adoption on BOTH paths
  (`TryRawResolve`, `FinalizeAndValidate`), surfaced on `Solve3DReport.ResolvedCellComplex`.
- **Public overload** `Create.AdjacencyCluster(IEnumerable<Panel>, ResolvedCellComplex, out diagnostics,
  ..., excludeCellIndices)`: builds a cluster with NO rebuild - one space per (kept) cell, one panel
  per unique cell face, relations from owner cells; non-Interior cells excluded BY CELL INDEX. Panel
  identity (construction/type/Guid) is inherited from a supplied panel ONLY on an unambiguous coplanar
  containment match, defaulted-and-reported otherwise (`SAM_OCCT_ANALYTICAL_PANEL_IDENTITY`), never
  silently mis-attributed.
- **`Create.Spaces` rewired** to consume `report.ResolvedCellComplex` instead of a second
  `CellComplexByPanels` decode: the only native build it now pays for is the classifier's envelope
  point-in-solid decode. Space count invariants hold (flat 22, two-level-tilted raw 43, managed
  refusal); panel granularity can differ slightly from a fresh rebuild (the DTO is the solver's adopted
  decode, the topology source of truth, not a re-decode of the reconstructed panels).

Tests: `ResolvedCellComplexTests` (DTO projection/dedup/ordinal-bridge/round-trip, native-free),
`ResolvedCellComplexIntegrationTests` (report carries the complex on both paths; direct consume +
identity inheritance; index-based exclusion), and the updated `CreateSpacesIntegrationTests`.

```powershell
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj --filter "FullyQualifiedName~ResolvedCellComplexTests"
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj --filter "FullyQualifiedName~ResolvedCellComplexIntegrationTests"
```

## CellComplex Grasshopper handoff (docs/CELLCOMPLEX_FIRST_HANDOVER.md, Phase P3)

P3 exposes the P2 `ResolvedCellComplex` in Grasshopper (D) and wires the value-based direct handoff (E):
`SAMOCCT.Solve3D` outputs the complex a solve adopted; `SAMOCCT.CreateAdjacencyCluster` optionally
consumes it directly (no native rebuild) when the incoming panels can PROVE they still match what that
solve produced. Library semantics are otherwise unchanged - the new `Create.AdjacencyCluster(panels,
resolvedCellComplex, ...)` call path is the SAME P2 overload, gated by a new roster check.

- **`ResolvedCellComplex.PanelGuids`** (new, additive field): the Guids of the resolved OUTPUT panels a
  solve produced. Empty until attached - the solver has no `Panel`/Guid concept, so `Project(...)`
  leaves it empty; `WithPanelGuids(IEnumerable<Guid>)` (mirrors the existing `WithNakedWires`) attaches
  it once the analytical/GH layer has built the output panels. JSON round-trips. An empty roster is a
  safe "unknown" default - the gate below never consumes a complex with no recorded roster.
- **`PanelProvenanceParameter.SolveId`** (new enum member): a string-valued stamp, the complex's
  `SolveId` as text, set on every output panel by `SAMOCCT.Solve3D` (mirrors the existing
  `SourceGuid`/`MergedSourceGuids`/`Provenance` stamps).
- **`CellComplexHandoff`** (`SAM.Analytical.OCCT.Solver`, new, pure managed - no native, no Grasshopper
  document required): the roster gate as a plain, unit-testable static class.
  - `StampSolveId(panels, solveId)`: sets the stamp on every panel.
  - `TryDirectConsume(panels, resolvedCellComplex, out reason)`: true ONLY when every incoming panel
    carries a `SolveId` stamp matching the complex's `SolveId` AND the incoming panel Guid roster equals
    `resolvedCellComplex.PanelGuids` exactly (same count, same Guid SET - order-independent). False with
    a named reason otherwise: no complex supplied, no panels supplied, complex has no recorded roster,
    N panel(s) missing/mismatched stamp (also catches a SolveId collision - a Guid coincidentally in the
    roster but stamped by a DIFFERENT solve), roster count mismatch, or roster Guid-set mismatch (same
    count, different panels - a swap the count check alone would miss). Never a silent bypass.
- **`SAMOCCT.Solve3D`** (`0.4.0 -> 0.5.0`, append-only Voluntary outputs): stamps every output panel's
  `SolveId`, attaches the SAME roster to the complex it outputs, and adds `CellComplex` (the DTO, via
  the new `GooResolvedCellComplex`/`GooResolvedCellComplexParam`), `ComplexFaces`/`ComplexFaceOwners`
  (unique cell face geometry + owner cell index/indices, index-aligned), `ComplexAdjacencies`/
  `ComplexAdjacencyFaces` (shared-face adjacency pairs + geometry, index-aligned), and `ComplexSummary`
  (one-line cell/face/adjacency/naked-wire/roster counts). Formatters live on `SolverReportFormat`
  (`FormatResolvedCellComplex*`), the same Grasshopper-facing-text-formatter class as the existing
  `FormatSourceMap`/`FormatCells`/`FormatLevelFrames`.
- **`SAMOCCT.CreateAdjacencyCluster`** (`0.1.0 -> 0.2.0`, one new Voluntary/Optional input
  `cellComplex_`): unwired (every existing saved definition), behaviour is BYTE-IDENTICAL to pre-P3 - the
  native rebuild runs exactly as before, same diagnostics, same order. Wired, the roster gate runs
  first: approved -> direct consume via the P2 overload (no native call, `SAM_OCCT_ANALYTICAL_COMPLEX_*`
  diagnostics from that overload); refused -> falls back to the SAME rebuild path, with one additional
  `SAM_OCCT_ANALYTICAL_COMPLEX_REBUILD:` diagnostic naming why (only when a complex was actually wired
  in - an unwired input is the normal, quiet default, not a drift to report).
- **`GooResolvedCellComplex`/`GooResolvedCellComplexParam`** (new,
  `Grasshopper/SAM.Analytical.Grasshopper.OCCT/Classes`): a thin `GooJSAMObject<ResolvedCellComplex>`
  subclass (mirrors the existing `GooResult` pattern) - JSON round-trip via the DTO's own
  `ToJsonObject`/`FromJsonObject`, no native handle captured, internalize/bake/file save-load all just
  work. Solver-output only (interactive prompts not implemented, matching `GooResultParam`).

**Back-compat proven, not assumed:** the rebuild code path in `SAMOCCTCreateAdjacencyCluster` is the
EXACT pre-P3 code, now guarded by `if (!directConsumed)` - an unwired `cellComplex_` produces the
identical diagnostics list, in the identical order, as before P3. All new outputs on both components are
Voluntary/append-only; the new input is Optional. Full suite green with native present (**470 unit /
171 integration**, +1 native-missing skip); all raw/managed goldens and E1/E2 pins byte-identical
(P3 touches no solver/geometry code - only the DTO gains an additive field, and the analytical/GH layers
gain new wiring).

Tests: `CellComplexHandoffTests` (the roster gate: matching, order-independent, missing stamp, SolveId
collision, no roster recorded, no complex, no panels, roster count mismatch, roster Guid-set mismatch -
all pure managed, fabricated Guids); extended `ResolvedCellComplexTests` (`PanelGuids` projection default/
`WithPanelGuids`/JSON round-trip); `CellComplexHandoffIntegrationTests` (native-gated, a REAL solve's
panels/complex - unmodified roster approves and matches the direct P2-overload call by relation-key set;
a deleted panel refuses with a roster-count reason; a panel swapped in from an UNRELATED solve refuses on
the stamp/roster check; a complex with no roster attached refuses).

```powershell
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj --filter "FullyQualifiedName~CellComplexHandoffTests|FullyQualifiedName~ResolvedCellComplexTests"
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj --filter "FullyQualifiedName~CellComplexHandoffIntegrationTests"
```

## Adoption-gate hardening (docs/CELLCOMPLEX_FIRST_HANDOVER.md, Phase P4)

P4 closes the two "watertight-but-wrong" adoption-gate holes (deferred codex findings #3 and #7) without
moving any of the five golden fixtures - they still adopt exactly as before (verified byte-identical, the
findings do not fire on them).

- **Codex #3 - consolidation-rebuild baseline** (`Panel3DSnapSolver.FinalizeAndValidate`): the direct
  (history-capturing) rebuild of the appended set is now accepted only when it does not regress versus the
  APPENDED set's OWN decoded cell/naked counts - the fallback it would replace - not the pre-append
  `resolveCellCount`. That old baseline was measured before patches/retained faces were appended and was
  typically lower, so a rebuild that DISSOLVED a shared separator (fewer cells = two rooms merged into one)
  could still pass `rebuiltCells >= resolveCellCount` and be wrongly adopted. The appended set is decoded
  once, up front, only when a rebuild is attempted, and reused by the reject path (no second decode). The
  acceptance rule is extracted as the pure, unit-testable `AcceptConsolidationRebuild`.
- **Codex #7 - under-split raw adoption** (`Panel3DSnapSolver.EvaluateRawAdoption` + `CountUnderSplitCells`):
  a new gate, checked after the coarse dropped-RATIO test, rejects a raw solve when a dropped input face is a
  room-dividing partition the build failed to imprint - so two rooms silently merged into one watertight
  cell (a case the dropped-ratio check misses when only one partition of many faces is dropped, e.g. 0-14%).
  A dropped face counts as such a partition only when it is (1) wall-like (vertical, within
  `VerticalAngleTolerance` of horizontal), (2) strictly INTERIOR to a single adopted cell (inside, not on its
  boundary), and (3) nearly fills that cell's cross-section - spanning >= `UNDER_SPLIT_MIN_HEIGHT_RATIO`
  (0.8) of its height AND >= `UNDER_SPLIT_MIN_PLAN_RATIO` (0.7) of its plan width perpendicular to the
  partition. Deliberately conservative (a false positive pushes a well-modelled input onto the weaker managed
  pipeline): a partial-height fin, a short balcony upstand, a horizontal cap sliver, a boundary-coincident
  duplicate, or a fragment interior to a large real room all fall short and are ignored - so atria, courtyard
  rings and double-height rooms are not tripped. `EvaluateRawAdoption` stays pure (the geometry is measured by
  the caller and passed in as a count); rejections emit `SAM_OCCT_..._UnderSplit` with the measured values
  (cell, volume, height/plan span). The new `RawAdoptionOutcome.RejectedUnderSplit` /
  `DiagnosticCode.UnderSplit` are additive.

**Calibration evidence (the gate must fire on real under-splits but never on good models):** the thresholds
were tuned against the real fixtures. The under-split gate does NOT fire on any of the five golden fixtures
(all raw pins byte-identical) nor on the clean PR #49 exports (`Extend3DRegressionIntegrationTests`,
incl. `PR49-Test2` at 43 cells / 88 walls, whose one dropped interior face spans only 54% of its cell's
height - correctly below the 0.8 bound). It DOES fire on the door-cut two-room fixture below.

Tests: `RawAdoptionGateTests` (the pure under-split branch + precedence, native-free);
`ConsolidationRebuildGateTests` (the pure #3 acceptance rule, all branches incl. the separator-dissolving
case, native-free); `GateHardeningIntegrationTests` (native-gated, built programmatically): a door-cut
partition (0.5 m short of the ceiling) is raw-rejected under-split and the managed pipeline separates the two
rooms into 2 cells (fail-before: with the gate disabled during development the raw path adopted it as 1
merged cell), and a partition-free large single room is still adopted raw with the gate silent (false-positive
control).

```powershell
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj --filter "FullyQualifiedName~RawAdoptionGateTests|FullyQualifiedName~ConsolidationRebuildGateTests"
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj --filter "FullyQualifiedName~GateHardeningIntegrationTests"
```

## PR #49 regression fixtures (Extend3D "walls disappear" bug)

PR #49 (`feature/extend3D`, closed as superseded by the E-track - `docs/EXTEND3D_ROBUST_HANDOVER.md`
§9) attached four real sample models reproducing the original bug ("walls disappear after
`SAMOCCT.Extend3D`"). These were never captured into the repo; they only existed as GitHub
user-attachment links in the PR description. Downloaded and converted from raw JSON to the
compressed `.sam` fixture format (`SAM.Core.Convert`'s existing zip writer/reader) - **~80%
smaller** (1.73 MB raw JSON -> 353 KB total) - and added under
`Testing/SAM.OCCT.IntegrationTests/Fixtures/Extend3D-Regression/` (a subfolder, like `Robustness/`,
kept OUT of `OcctFixtureIntegrationTests.Shells_UploadedSamFixtures_BuildCells`'s top-level scan:
these panels are deliberately gappy/disconnected, so a naive raw build is not expected to succeed).

`Extend3DRegressionIntegrationTests` runs each through production `Solve3D` and pins the CURRENT
result (first coverage these fixtures ever got, measured post-E1/E2 - not a priori targets):

| Fixture | Walls in | Raw adopted | Cells | Naked | Walls out |
| --- | --- | --- | --- | --- | --- |
| PR49-Test0-3spaces.sam | 12 | yes | 3 | 0 | 12 (all survive) |
| PR49-Test1.sam | 4 | yes | 2 | 0 | 4 (all survive) |
| PR49-Test2.sam | 88 | yes | 43 | 0 | 88 (all survive) |
| PR49-Test0a-3spaces.sam | 12 | no (managed) | 1 | 0 | **4** (partial) |

Three of the four are clean, watertight raw-first adopts today - the literal PR #49 bug (walls
silently reaching zero) does not reproduce. `Test0a` (a harder variant with a larger real gap)
still only partially resolves under default settings: not the zero-walls bug (some walls survive,
nothing crashes, no naked edges), but a genuine residual gap-size limit worth tracking honestly
rather than either failing the suite or silently declaring victory. A future MaxExtend/bucket tune
or frame-aware extension may close it further; the pin should move only with a stated mechanism,
per this repo's golden-master convention.

```powershell
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj --filter "FullyQualifiedName~Extend3DRegressionIntegrationTests"
```

## Controlled-workflow 9-space fixture (P0 baseline)

The controlled workflow (`Panel → Clean3D → Extend3D → CreateAdjacencyCluster → ValidateSpaces`,
plan: `docs/CONTROLLED_WORKFLOW_PLAN.md`) is anchored on one real acceptance model captured under
`Testing/SAM.OCCT.IntegrationTests/Fixtures/ControlledWorkflow/` (a subfolder, like `Robustness/`,
kept OUT of the top-level watertight scan - the panels model is deliberately imperfect; the source
folder was named "MissingWalls"):

- `Panels-9SpacesModel.sam` - 66 panels (40 Wall / 10 Floor / 16 Roof) that should form 9 spaces.
- `Spaces-9SpacesModel.sam` - the 9 expected spaces (distinct single-line names; `West3` =
  the double-height space, GUID `02a1ae27-5461-4b41-ad07-008ccd9d1159`).

`ControlledWorkflowBaselineTests.ControlledWorkflow_NineSpacesFixture_BaselineReportCapture` is the
P0 baseline harness: it runs the CURRENT pipeline (both extend paths: `Extend3D(original)` and
`Extend3D(Clean3D(original))`), prints level frames, per-cell containment of the expected space
locations, orphan/unused panels, and a separator scan for merged/missing pairs - **report-only**
(asserts fixture integrity, never pipeline success; the captured state lives in
`docs/CONTROLLED_WORKFLOW_BASELINE.md`). Hard acceptance assertions arrive with their owning phases
(P2 level groups, P4 nine-space) per the plan's test-phasing rule.

```powershell
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj --filter "FullyQualifiedName~ControlledWorkflowBaselineTests" --logger "console;verbosity=detailed"
```

## SpaceMatcher validation harness (P1)

`docs/CONTROLLED_WORKFLOW_PLAN.md` §6 GUID-based matcher, pure managed
(`SAM_OCCT/SAM.Analytical.OCCT.Solver/{Enums,Classes}/`) - no OCCT DLL required, so its own tests live
in `Testing/SAM.OCCT.UnitTests`:

- `ExpectedSpaceSet` - GUID-keyed expected spaces; hard-fails (throws) on an empty/multiline name or a
  duplicate Guid; a duplicate NAME warns and gets a disambiguated `Name#n` report label instead. Level
  groups are inferred self-contained from a supplied panel set's cap faces
  (`LevelFrame.Cluster` + a dominant-area-first, non-transitive 1-D merge at
  `SpaceMatchOptions.LevelGroupBand`) - independent of any Clean3D/Extend3D `bucketBetweenLevels`
  plumbing, so P1 does not depend on P2. Each expected space gets a vertical span from those datums;
  double-height spaces (GUID-backed, never inferred from a location) skip the intermediate datum their
  location sits near. Tests: `ExpectedSpaceSetTests.cs`.
- `SpaceMatcher.Match` - the containment matrix mirrors `AdjacencyCluster`'s own `FindSeedSpace`
  predicate (`Shell.Inside(location, silverSpacing, tolerance) || Shell.On(location, tolerance)`) but is
  independent of the builder's own (greedy, first-fit) space matching. Classifies every expected space as
  Matched/Merged/Missing/Split/IncorrectlyBounded, and every generated cell not hosting an expected space
  as Extra; a double-height space additionally fails (as Split) if its matched cell carries an unexpected
  near-horizontal boundary face at an intermediate level-group datum inside its own footprint. Boundary
  ambiguity resolves deterministically to the nearest cell centre, then lowest index. Tests:
  `SpaceMatcherTests.cs` (1:1 match, merged pair, missing-with-distance, null location, extra cell,
  undersized-cell-with-partner split, span-overshoot-with-no-partner incorrectly-bounded, boundary
  determinism, double-height ok/violated, stable `SUMMARY`/`LEVELS` report lines).
- `SeparatorPanelFinder` - for a merged pair, scans a panel set for a vertical separator strictly between
  the two locations, requiring it cover the pair's full expected floor-to-ceiling span (not just the
  narrow band between the two seed elevations) and classifies it Candidate (full lateral coverage) /
  Partial (a `SpaceMatchOptions.LateralMargin` near-miss) / Absent. Tests: `SeparatorPanelFinderTests.cs`.
- `PanelContributionFinder` - `OrphanClusterPanels` (`cluster.GetSpaces(panel)` empty) vs
  `UnusedInputPanels` (a geometric coplanar-overlap test against the cluster's own faces, since the
  builder mints fresh panel identities and an input panel's Guid never appears in the built cluster).
- `SpaceMatchReport.Valid` is true only when there is no Missing/Merged/Split/IncorrectlyBounded space,
  no Extra cell, every requested double-height check passes, and there are no orphan cluster panels
  (unused/merged-away source panels are warnings only). `ToLines()` emits the stable, greppable
  `SAM_OCCT_SPACEMATCH: <KIND> ...` lines the plan documents.

`SAMOCCT.ValidateSpaces` (`Grasshopper/SAM.Analytical.Grasshopper.OCCT/Component/SAMOCCTValidateSpaces.cs`,
v1.0.0) wires an `_adjacencyCluster` + `_expectedSpaces` through `CellGeometry.FromCluster` +
`SpaceMatcher.Match`; `sourcePanels_`/`doubleHeightSpaces_` are optional and enable the panel-contribution
and double-height outputs. It re-derives the cluster's cell shells itself rather than trusting whatever
space matching the builder performed - `CreateAdjacencyCluster`'s `spaces_` input only steers its rebuild
path and seeds names, it is not a validation reference.

`ControlledWorkflowBaselineTests` now also runs `SpaceMatcher` (independently of its own hand-rolled
containment/classification code) against both extend paths and prints the full `SpaceMatchReport.ToLines()`
output (`SPACEMATCH:` lines) - report-only, no new assertions. Because P1's level-group inference reads
the ORIGINAL (uncleaned) panels' 22 raw cap elevations self-contained, its inferred datums are less precise
than Clean3D's own eventual 12.24/15.29/18.34 (e.g. West3's inferred span lands around 12.5/18.3, not
exactly on the clean datums) - an expected, documented characteristic of the self-contained P1 approach on
this messy fixture, not a bug; P2's `bucketBetweenLevels` plumbing is what makes the datums precise.

## Level groups, clean records, exact Clean→Extend handoff (P2)

`docs/CONTROLLED_WORKFLOW_PLAN.md` §4/§5.1/§3. Core/API defaults remain off, so
all ten existing golden-master signatures stay byte-identical. GH components use the documented
`bucketBetweenLevels_ = 0.21`; a separate managed-0.21 golden variant pins that intentional path. The P2
work is exercised by:

**Unit (pure managed, `Testing/SAM.OCCT.UnitTests`):**

- `LevelGroupTests` — `LevelFrame.GroupFrames` (the second-stage grouping over the raw frames, §4.1). The
  raw `LevelFrame.Cluster`/0.15 band is UNCHANGED (`LevelFrameTests` pins are untouched); `GroupFrames`
  merges frames dominant-area-first, seed-anchored and **non-transitively**. Covers the EXACT fixture
  elevations 12.24/12.436/15.29/15.473/18.34 → **band 0.21 gives 3 groups at datums 12.24/15.29/18.34**,
  0.15 gives 5 (with a near-miss diagnostic), 0 gives 5 (identity, no near-miss); dominant-area beats
  lower-elevation for the datum; a frame beyond the band from the seed is not chained in via an intermediate
  frame; shuffle determinism; tilted perpendicular-distance membership.
- `CleanRecordTests` — the Stage A clean recorder (§4.4). The headline guarantee: **a null recorder is
  byte-identical to the geometry** (the exact OCCT input coordinate/loop-count arrays match between null and
  live recorder runs), and the real mutation sites (bucket snap → `SnappedToBacker`, cap normalization →
  `CapNormalized`) emit their records. The 0.196 m synthetic cap test additionally proves it is claimed over
  the 0.21 effective band and projected exactly onto the dominant group datum.
- `ParameterPrecedenceTests` — the D6 precedence fix (§3): a valid per-panel **stamp always wins** over the
  derived value, which wins over the default, for BucketSize, Weight and MaxExtend. `ResolveWeights` now
  derives with `SetWeights(@override: false)` and reads the stamp straight off the source, so a hand-set
  `SolverParameter.Weight` is no longer clobbered (a stamp-free model derives identically to before). Each
  resolved value carries a provenance tag (`stamped` / `derived-length` / `derived-thickness` / `min-floor`
  / `default`) surfaced on the CleanReport. The resolvers are `internal` (InternalsVisibleTo the test
  assemblies) so they can be asserted directly.
- `InputAlreadyCleanTests` — the condition-only stage selection (§2). Flag OFF: Stage A runs and merges two
  overlapping coplanar tiles into one clean face. Flag ON: Stage A is SKIPPED (the tiles pass through
  unchanged, an identity source map is built, no clean records are produced, a `SAM_OCCT_CLEAN3D_SKIPPED`
  diagnostic is emitted, and frames/groups are still clustered for reporting).

**Integration (`Testing/SAM.OCCT.IntegrationTests`):**

- `ControlledWorkflowLevelGroupIntegrationTests` — the fixture-level P2 outcomes. Clean3D/Extend3D are the
  MANAGED pre-resolve passes, so these are plain `[Fact]`s (no native gate) and verify on CI: `Clean3D` on
  the 9-space fixture with `bucketBetweenLevels: 0.21` yields **5 raw frames → 3 level groups at
  12.24/15.29/18.34**; the default (0) is the identity; the CleanReport names the `5 -> 3` grouping and the
  band and records real per-panel actions; and `Extend3D(inputAlreadyClean: true)` skips the second clean
  (CLEAN-SKIPPED diagnostic, no clean records) while still reporting frames/groups.
- `ControlledWorkflowBaselineTests` — the P0 baseline harness gains **path C**: the exact P2 handoff
  `Extend3D(Clean3D(original, 0.21), inputAlreadyClean: true, 0.21)`, dumped alongside paths A/B with its
  level groups, CleanReport, and the `PATH_C_CLEAN_SKIPPED` confirmation — the parity evidence that the
  chain no longer double-cleans. Report-only (hard nine-space acceptance is P4).
- `GoldenMasterIntegrationTests.Solve3D_ManagedPath021_ClosureSignatureMatchesGoldenMaster` — a second,
  explicit managed golden theory pins the GH path independently of the unchanged core-default
  pins. The closure signature is `cells / naked / volume m³ / faces`:

  | Fixture | Managed 0.21 pin |
  |---|---:|
  | `whole-level-flat.sam` | 22 / 0 / 3479.896693 / 219 |
  | `tilted-two-spaces.sam` | 2 / 0 / 723.652483 / 9 |
  | `whole-level-tilted.sam` | 22 / 0 / 3377.873886 / 184 |
  | `two-level-tilted.sam` | 9 / 24 / 2082.410010 / 412 |
  | `whole-level-towers.sam` | 25 / 0 / 9281.107191 / 231 |

  The two disputed groupings are classified and asserted in the test. `whole-level-towers` merges only the
  12.240/12.397978 slab skins; 12.602691 and 15.572691 remain singleton datums. `two-level-tilted` merges
  only the 0.950536/1.124485 slab skins; its -5.149464 and -2.179464 levels remain distinct. Every claimed
  cap is asserted to lie on the supplied group datum, catching any later reintroduction of the former
  largest-cap/nearest-centroid substitution. These P2 pins are release gates; a later phase may change them
  only with its own explicit mechanism and re-baseline.
- `WorkflowParity_WholeLevelTowers_FixtureTuning04_Produces31ParityCleanSpaces` pins the reported
  fixture-specific `fillMargin=0.4` / `bucketBetweenLevels=0.4` experiment: 10 raw frames → 7 groups,
  **31/32 spaces**, zero open wall ends, zero orphan panels, and clean analytical parity. Sweeping
  `fillMargin` through 0.4/0.5/0.6 did not recover the last cell, located near
  `(19.584, -4.909, 13.925)`. This proves the residual is not a level-group or uniform-fill-margin issue;
  it remains an explicit P3 frame-aware/directional-extension acceptance gate. The 0.4 band crosses this
  fixture's 0.283/0.363 m near-misses and is not promoted to the generic GH default.

Grasshopper: `SAMOCCT.Clean3D` (0.4.0 → 0.5.0) and `SAMOCCT.Extend3D` (0.6.0 → 0.7.0) gain
`bucketBetweenLevels_` (generic default 0.21) — plus `inputAlreadyClean_` (false) on Extend3D — inputs and `LevelGroups` /
`CleanReport` outputs; `SAMOCCT.Solve3D` (0.5.0 → 0.6.0) gains `bucketBetweenLevels_` and a `LevelGroups`
output. The GH description records that SAM_Solver's same-named 0.21 control is a final cross-level wall
re-snap, whereas SAM_OCCT performs level-datum merging. Larger values are explicit per-model tuning.
Because a Voluntary input exists neither on an old saved component nor on a fresh placement, the 0.21 GH
default is delivered by a **version-gated fallback** (`SolverComponentDefaults.BucketBetweenLevelsFallback`,
pinned by `SolverComponentDefaultsTests`): a component saved before the input's introducing version
(0.5.0 / 0.7.0 / 0.6.0 respectively) resolves the absent input to the core default 0, so existing saved GH
documents never change behavior on plugin update; components placed at or after it resolve to 0.21.

## P3 — directional cap growth, extend skip/risk diagnostics, MaxExtend, input-effect (`feat/cw-p3-extend3d`)

Unit (pure-managed, `Testing/SAM.OCCT.UnitTests`):

- `FillDirectionalTests` — `SnappedPanel.GrowEdgesToWalls`: an edge with a facing wall grows only that edge
  by its measured gap + overshoot; an edge with no facing wall in reach grows 0 (fail-closed, the D4
  false-floor guard); a cap with a hole keeps the hole; `Fill(directionalCapGrow: true)` records
  `walls-directional` on success and falls back to `fixed-margin` with a `LegacyUniformCapGrow` risk flag
  when no facing wall exists — never silent.
- `ExtendSkipTests` — a real decision point that leaves a panel untouched is a `Skipped` `ExtendRecord`
  (e.g. `Fill` with margin <= tolerance -> `FillTooSmall`); the `ExtendRecord.Skip` factory carries the
  reason/detail; `SolverReportFormat.FormatExtendRecords` routes a Skipped record to
  `SAM_OCCT_EXTEND3D_SKIP:` and an Applied record with a risk flag to the frozen
  `SAM_OCCT_EXTEND3D_PANEL:` line plus a `SAM_OCCT_EXTEND3D_RISKY:` line.
- `MaxExtendDerivationTests` — `Modify.ResolveMaxExtends`: a stamp (or supplied list) wins; an **unstamped**
  panel falls back to the flat **0.4 m** default (`DEFAULT_MaxExtension`), reported `Default`; an invalid
  (non-positive) stamp is ignored. This is the regression guard for the P3 decision **not** to adopt the
  `SetMaxExtends` clone-derivation: its 0.49x-length pre-cap crushed short/segmented walls (~0.1 m) and
  regressed the managed golden masters, so the default stays flat 0.4 m (byte-identical to pre-P3) and
  MaxExtend is a per-panel tuning knob. The plan's original section 5.6 derivation wording is superseded by
  this compatibility decision.

Integration (`Testing/SAM.OCCT.IntegrationTests/ControlledWorkflowExtendTargetIntegrationTests`, managed
Clean3D/Extend3D — runs everywhere, no native call):

- `ExtendTargets_FixtureChainedRun_CapsNormalizeToGroupDatumsAndWallsReachThem` — on the 9-space fixture's
  exact chain `Clean3D(0.21) -> Extend3D(inputAlreadyClean: true, 0.21)`, every floor/roof **cap** normalizes
  onto one of the three group datums 12.24 / 15.29 / 18.34 and none stays on an un-merged raw skin
  (12.436 / 15.473) — the D3 merged-plane targeting proof. Walls are **not** asserted against the raw datums:
  a wall whose original top sat at a raw skin (e.g. North0's 15.473 roof, now normalized to the 15.29 cap)
  legitimately *overshoots* its lowered cap in this pre-resolve view because Extend3D only grows walls; the
  native split trims that overshoot in Solve3D. The guarantee lives on the caps, not on overshooting wall tops.
- `ExtendTargets_FixtureChainedRunDirectionalCapGrowOn_NoFalseFloorCoversDoubleHeightColumn` — with
  `directionalCapGrow: true`, no near-horizontal cap face lands within 0.21 m of the intermediate datum 15.29
  inside West3's plan footprint (no fabricated false floor in the double-height column).
- `InputEffect_ChainedRunInputAlreadyClean_CleanStageInputsInertButRunSaysSo` — pins the input-effect
  contract: on the `inputAlreadyClean = true` handoff, varying the clean-stage inputs (minBucketSize /
  thicknessFactor / alignColinearOffset / normalizeCapOffset / bucketBetweenLevels) produces **byte-identical
  geometry** and the run emits a `SAM_OCCT_EXTEND3D_INPUT_INERT:` line, while `fillMargin_` (a live knob on
  that path) **does** change the geometry. The pinned answer to "changing the input gives the same result":
  it is by design (Stage A is skipped), and the run now says so.

Golden masters stay **byte-identical with flags off**: `GoldenMasterIntegrationTests`
(`Solve3D_ManagedPath_...`, `Solve3D_ManagedPath021_...`, `Solve3D_RawPath_...`) is unchanged by P3 — the
MaxExtend revert restores the pre-P3 signatures exactly (all 15 pinned rows pass), so no golden was


### Consolidation / double-wall merge tests (Phase 1–2, 2026-07-12)

- `ConsolidateWallStacksTests` — Pure-managed unit tests for `ConsolidateWallStacks`:
  cap-follow (`DragAbuttingCapEdges`) drags abutting cap edges by exactly the move distance;
  non-abutting caps remain untouched; other-storey caps (z outside band) untouched; gap 0 /
  no move ⇒ no-op. Stamped pair merges with global off; one-sided stamped ranges both ways;
  travel cap = max(member, dominant); stamped caps merge while unstamped caps are untouched.
  `SnapStage.Clean` activates on ranges with gap 0. Near-miss emitted + bounded + suppressed
  when overlap/ratio fails. All 15 existing facts unchanged (no-stamp guard).

- `ParameterPrecedenceTests.ResolveConsolidationRanges_*` — Unit tests for the
  `ResolveConsolidationRanges` resolver: stamped BucketSize ⇒ value; unstamped ⇒ 0.

- `TowersBucketLeverDiagnosticTests.Towers_DoubleWallGap_FillMargin_DefectDiagnostic` —
  Phase 0 matrix test {gap 0.4, 0.47, 0.5} × {fillMargin 0.4, 0.5}: cell diff vs baseline,
  wall z-span / super-tall / coplanar-duplicate report, ExtendRecords + NoTargetWithinReach
  roll-up, cap seam-crossing diagnostics.

- `Towers_DoubleWallGap_FixAcceptance_Gap05StripSurvives` — Phase 1d acceptance: gap 0.5
  must give 29 cells (strip space survives); gap 0.47 ⇒ 29 cells, no merge.re-baselined in this phase.
