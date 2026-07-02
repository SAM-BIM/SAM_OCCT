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
  `sam_occt_abi_version` ("3"); a stale native build degrades to diagnostics
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
  - **Managed path** (`Solve3D_ManagedPath_ClosureSignatureIsRecorded`) -
    the pre-raw-first clean/extend/resolve pipeline, forced via
    `Panel3DSnapSolver.ForceManagedPipeline` (threaded through
    `Modify.Solve3D`'s `forceManagedPipeline` parameter). Both default to
    `false` and change no existing behaviour; setting either skips only the
    raw-first shortcut and always runs the managed pipeline, which is known
    (see plan §A) to under-close some of these exact fixtures relative to
    the raw path. This test records today's number as the baseline - it does
    not assert a specific value, since Phase 0 makes no pipeline changes.

**Local-integration merge gate.** Every phase in the plan (0-9) is merged
under the same protocol already documented above: `dotnet test` on
`SAM.OCCT.UnitTests` must be green, and `SAM.OCCT.IntegrationTests` must be
run locally against the committed `build/` native DLLs, with the pass/skip
summary pasted into the PR. A phase's golden-master values must not change
unless that phase explicitly states the delta (which fixture, which number,
why) in its PR description - an unexplained change to these numbers is a
regression, not a refactor.

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
