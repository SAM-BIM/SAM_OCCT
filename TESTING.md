# Testing

SAM_OCCT follows a **two-tier test method** built on **xUnit**. The split exists
because the library straddles a managed/native boundary: most logic is pure C#
and testable everywhere, while the actual geometry kernel work happens in the
C++/OpenCASCADE layer (`SAM.Occt.Native`).

## Layout

| Project | Purpose | Native needed? |
| --- | --- | --- |
| `Testing/SAM.OCCT.UnitTests` | Pure-managed logic: `OcctBuildOptions`, `OcctDiagnostic`, the `OcctNativeInputBuilder` serializer, `Query` guard clauses, and the graceful *native-missing* path. | No |
| `Testing/SAM.OCCT.IntegrationTests` | Real OCCT boolean / cell-complex operations producing cells, volumes and face adjacencies. | Yes (auto-skips when absent) |

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
