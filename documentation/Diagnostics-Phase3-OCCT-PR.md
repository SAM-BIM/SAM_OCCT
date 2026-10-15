# Unified Diagnostics, Phase 3 (SAM_OCCT integration) - PR record

Base: `sow/2026-Q4` (`46dac7a`). Branch: `claude/determined-wright-9r0q71`. Dependency baseline: SAM `sow/2026-Q4` with SAM#201 (`293eb645`) and SAM#202 (`fca3d4d4`, closeout `d6c35692`); no SAM or SAM_Solver change.
Validated content SHA: `c50c0b9c143868839b025e2144fbb2a6e0d1983b` (code and tests). A record-only commit follows; the final head and hosted checks are in the PR description.
Merge needs owner approval and the Windows/Rhino 8 acceptance below (AGENTS.md). `PROJECT_PROGRESS.md` is not touched (post-merge closeout only).

## Status
Implementation complete. Linux build and unit tests pass (cloud run, below). **Windows, native OCCT and headless Rhino 8 acceptance passed on 2026-10-15** at code SHA `c50c0b9c` (no code change since; see "Windows / Rhino 8 acceptance results"). No Phase 3 defect was found, so this PR carries no code change after the acceptance. The record-only commits that follow add that evidence.

## Investigation (what was found)
- Three unrelated mechanisms: `OcctDiagnostic` (severity + code + message + source index, text `CODE [idx]: message`), `SolverDiagnostic`/`SolverDiagnostics` (stage + 25-value `DiagnosticCode` + severity + geometry), and plain strings in `Solve3D` / `CreateAdjacencyCluster`.
- GH handling before this change:
  - `CreateAdjacencyCluster`: a hard-coded list of 7 codes matched with `Contains`; everything was a Warning when the cluster was null, otherwise a Remark unless listed. OCCT Warnings outside the list (e.g. `SHELL_NOT_REBUILT`, `SHELLS_MERGED`, `CELL_SPACE_DELTA`) were only Remarks; Errors (e.g. `NATIVE_FAILED`) showed as Warning; Info showed as Warning on failure.
  - `CreateShells`: every diagnostic, Errors included, was a Remark.
  - `Solve3D`: no runtime message for any diagnostic (only a Remark for passed-through Air panels).
- All 7 previously listed codes are Warnings at their source, so none is downgraded.

## Work completed
- `SAM.Core.OCCT` (new, additive): `DiagnosticRecord` (shared `Diagnostic` + the exact legacy text), `DiagnosticMessage`; `Query.ToDiagnosticSeverity`, `ToDiagnostic`, `ToDiagnosticRecord`, `CreateDiagnosticRecord`, `GetDiagnosticDetail`, `FilterDiagnosticRecords` (real `Core.Diagnostics` rules), `GetDiagnosticMessages` (runtime grouping).
- `SAM.Geometry.OCCT.Solver`: `SolverDiagnosticExtensions.ToDiagnostic` / `ToDiagnosticRecord` / `GetDiagnosticDetail` (Code = `DiagnosticCode` name, Subject = stage, Value = tolerance in m).
- `SAM.Analytical.OCCT.Solver`: `SolverReportFormat.FormatDiagnosticRecords` (same loop and text as `FormatDiagnostics`, which now delegates); `Solve3DReport.DiagnosticRecords` (get, `internal set`; the constructor is unchanged); `Modify.Solve3D` builds one record per diagnostics line where the line is produced (`INPUT_EMPTY` / `NO_RESULT` Error, `APERTURE_ORPHANED` Warning, `AIR` / `RESULT` / extend / input-effect lines Info, solver lines with the solver's own severity). The `List<string>` output, the signatures and the algorithm are unchanged.
- Grasshopper: `Grasshopper/Shared/OcctDiagnosticReporter.cs` (linked, `internal`, into the Analytical and Geometry GH projects; no new cross-plugin dependency). `SAMOCCTSolve3D` 0.7.1 and `SAMOCCTCreateAdjacencyCluster` 0.2.1 gain the hidden optional `diagnosticDetail_` (last, `Voluntary`, `Optional`, default 0, Phase 2 wording and invalid-value Warning). `SAMOCCTCreateShells` 0.3.1: severity mapping only, all lines kept. The warning-code list and `IsWarning` are gone.

## Decisions and assumptions
- **Default Summary** (owner-chosen, Phase 2 convention): the default `Diagnostics` list of Solve3D / CreateAdjacencyCluster no longer carries Standard/Detailed Info (timing, metadata, extend and input-effect lines). `diagnosticDetail_` 1 / 2 brings them back; 2 equals the pre-change list. Warnings and Errors are never dropped. Anything parsing `Diagnostics` Info lines at the default must set 2.
- Severity is the producer's: `OcctDiagnostic.Severity`, `SolverDiagnostic.Severity`, or a severity chosen at the component's own production site. It is never inferred from code names or text. Unclassified legacy lines (the `List<string>` direct-handoff lines, extend lines) are Info.
- Detail (Info only): a short list of important codes and families (`*_SUCCESS`, `*_RESULT`, `*_INPUT_EMPTY`, `*_NO_RESULT`, `*_FAILED`, `COMPLEX_*`, `NATIVE_*`, `OPEN_SHELL_ANALYSIS`, `SEW_UNDER_SPLIT_SUSPECTED`, `EXTEND3D_HOLE_DROPPED` are Summary; `PANEL_METADATA`, `SHELL_FACES`, `*_PANEL`, `CELL_CENTERS` Detailed); everything else, including new codes, is Standard.
- Runtime messages: Info is log only. Errors are never merged unless the text is identical. Warnings show individually up to 3 per code; more of the same code become one "N more ... see Diagnostics" message. `Diagnostics` keeps the full text of every retained line.
- Behaviour changes visible on the canvas (intended): real OCCT Warnings now show as Warning (were Remark); Errors show as Error (were Warning/Remark); Info no longer shows as Warning on failure; Solve3D now raises Warning/Error for its structured Warnings/Errors (it raised none); `COMPLEX_REBUILD` (a wired `cellComplex_` was not used) is a Warning (was Remark). Failure paths always carry an Error (Solve3D and CreateShells add a component-level Error when a failure has none).
- Solve3D falls back to the unfiltered legacy list as Info (nothing hidden) if the records ever disagree with it.
- The Info detail level applies to what `Diagnostics` shows; nothing in the build or the solve reads the level.

## Files changed
New: `SAM_OCCT/SAM.Core.OCCT/{Classes/DiagnosticRecord,Classes/DiagnosticMessage,Query/ToDiagnosticSeverity,Query/ToDiagnostic,Query/CreateDiagnosticRecord,Query/GetDiagnosticDetail,Query/FilterDiagnosticRecords,Query/GetDiagnosticMessages}.cs`, `SAM_OCCT/SAM.Geometry.OCCT.Solver/Classes/SolverDiagnosticExtensions.cs`, `Grasshopper/Shared/OcctDiagnosticReporter.cs`, tests `Testing/SAM.OCCT.UnitTests/OcctDiagnosticAdapterTests.cs`, `Testing/SAM.OCCT.GrasshopperTests/DiagnosticsContractTests.cs`, `Testing/SAM.OCCT.IntegrationTests/DiagnosticsDetailIntegrationTests.cs`.
Modified: `SAM.Analytical.OCCT.Solver/{Classes/Solve3DReport,Classes/SolverReportFormat,Modify/Solve}.cs`; `SAMOCCTSolve3D.cs`, `SAMOCCTCreateAdjacencyCluster.cs`, `SAMOCCTCreateShells.cs`; the two GH `.csproj` (linked file) and the Analytical GH `AssemblyInfo.cs` (`InternalsVisibleTo` for the GH test project).

## Validation
Linux cloud container, .NET SDK 8.0.131 from apt, SAM and SAM_Solver built Release from `sow/2026-Q4` (SAM with Microsoft.Net.Compilers.Toolset 4.11.0, as in Phase 2), `SAM_OCCT_SKIP_NATIVE_BUILD=true`. Machine-local; no native OCCT library (not built here), no Windows, no Rhino.

| Check | Result |
|---|---|
| Base `46dac7a` unit tests | PASS 654 / 654 |
| Head unit tests (`dotnet test Testing/SAM.OCCT.UnitTests -c Release`) | **PASS 700 / 700** (654 + 46 new) |
| Mutation checks (filter drops Warnings; Warning mapped to Info; Errors merged by code) | caught: 5, 5 and 1 failures; restored, 700 / 700 |
| Base integration (`Testing/SAM.OCCT.IntegrationTests`, native absent) | 31 passed, 0 failed, 248 skipped (279) |
| Head integration (same) | 31 passed, 0 failed, **251 skipped** (282; the 3 new tests are native-gated and skip) |
| GH projects compile (Geometry and Analytical OCCT GH, post-build neutralised, WinForms off) | PASS, 0 errors |
| GH contract tests (`Testing/SAM.OCCT.GrasshopperTests`): 15 existing + 11 new | **NOT RUN**: need `System.Windows.Forms` (Windows Desktop runtime); on Linux 25 of the 26 fail with `FileNotFoundException`, existing tests included, so no comparison is possible. Compile PASS. Not part of hosted CI. |
| Native integration (house `Solve3D` / `AdjacencyCluster` at all levels) | **NOT RUN** (no native). The 19 known pre-existing native failures were not re-run |
| Hosted checks | see PR description |

What the automated tests establish: severity maps 1:1 for every enum value (undefined -> Error); Warning/Error records are the same instances in the same order at every level for arbitrary and unknown codes; Info grows monotonically; Detailed equals the legacy list; text that looks like a Warning with Info severity stays Info; grouping rules; `FormatDiagnosticRecords` text equals `FormatDiagnostics`. Not established without native: identical panels / cluster across levels on the real house (structurally true - the level is read only after the solve - and covered by the native-gated tests), and the GH behaviour on a canvas.

## Status 40 (`SAM_OCCT_NATIVE_FAILED`)
Conclusion: **pre-existing and independent of Diagnostics.**
- `OcctCellComplexBuilder` raises it only after the native builder returns status 40 ("MakerVolume produced no closed solid - the faces do not bound a watertight volume"); the diagnostics code only reads the status. This PR changes nothing under `SAM.Geometry.OCCT` or the native input path (`git diff --stat -- SAM_OCCT/SAM.Geometry.OCCT` is empty).
- The input is open: the managed `OcctOpenShellAnalysis` (no native needed; same function the component uses for the follow-up line) on the 38 panels `PanelsBySolid` makes from the committed house fixture (`SAM` `Golden/PanelsBySolid/contemporary1_solids.json`, default `maxThicknessRatio` 0.2) reports **201 naked edges of 202, 668.2 m**; 39 panels at ratio 0.25: 204 of 205, 680.8 m. These mid-surface panels are not a closed shell, so MakerVolume cannot close them whatever the Diagnostics code does. This is the documented situation (`docs/Modeling-Guide.md`); `Solve3D` extends and closes panels first. SAM#202's record shows the same status before and after Phase 2 with identical panels.
- Not run in the cloud: the native call itself on this input. **Run on Windows 2026-10-15: identical on base and head** (see "Windows / Rhino 8 acceptance results", Status 40). The managed `OcctOpenShellAnalysis` numbers above are a separate measure from the native validation (17 naked edges, 3 self-intersections, status 40).
- **Correction to the cloud record:** `SAM_OCCT_OPEN_SHELL_ANALYSIS` is *not* emitted by `CreateAdjacencyCluster` or `CreateShells` for this input, on base or on head. It is only produced by the vertical-face shell path (`ShellsFromVerticalFace3Ds`). The status 40 outcome shown on the canvas is `SAM_OCCT_NATIVE_FAILED` plus `SAM_OCCT_VALIDATE_INPUT` (and, for `CreateAdjacencyCluster`, `SAM_OCCT_ANALYTICAL_PANEL_REBUILD_FAILED`). Acceptance step 6 below is read that way.
- No solver repair attempted.

## Risks
- Visible: the default `Diagnostics` list shrinks; canvas message levels change as listed under Decisions.
- Runtime needs a SAM build with SAM#201 (`SAM.Core.Diagnostic`); an older `SAM.Core.dll` would fail with a type load error. CI and the installer take SAM `sow/2026-Q4`.
- `OcctDiagnosticReporter` is compiled into two assemblies (internal); the GH tests see only the Analytical copy. Unverified on a canvas.
- Other OCCT GH components still use their own Remark handling (out of scope).

## Windows / Rhino 8 acceptance results (2026-10-15)
Machine: Windows 11, .NET SDK 10.0.401 (tests target net8.0), Rhino 8 at `C:\Program Files\Rhino 8`, native `SAM.Occt.Native.dll` from `build-native.ps1` (built 2026-10-13, no native source change since 2026-10-06), SAM `sow/2026-Q4` at `d6c35692` (Phase 1 and 2 merged; library DLLs in `SAM\build` contain `SAM.Core.Diagnostic`; later SAM commits are GH-component, test and docs only). Base = `sow/2026-Q4` `46dac7a`; head = code SHA `c50c0b9c` (PR head `1094d6a`). Same machine, same fixtures, base and head back to back. Commands: `dotnet build SAM_OCCT.sln -c Release`, then `dotnet test Testing/SAM.OCCT.<UnitTests|IntegrationTests|GrasshopperTests>/...csproj -c Release --no-build` with `SAM_OCCT_SKIP_NATIVE_BUILD=true`.

| Check | Base `46dac7a` | Head | Verdict |
|---|---|---|---|
| Release build (`SAM_OCCT.sln`, three OCCT GH projects, test projects) | 0 errors | 0 errors | PASS |
| Unit tests | 654 / 654 | **700 / 700** | PASS |
| Integration tests (native present) | 258 passed, 19 failed, 2 skipped (279) | **261 passed, 19 failed, 2 skipped (282)** | PASS, no regression |
| GH contract tests | 15 / 15 | **26 / 26** (15 + 11 new) | PASS (see harness note) |
| Headless Rhino 8 acceptance (below) | 6 / 6 (structure, outputs; level tests n/a) | **7 / 7** | PASS |
| Hosted CI on `1094d6a` | - | `build` SUCCESS, `spdx` SUCCESS | PASS |

**Integration comparison.** The 19 failing tests are the same 19 on base and head, with identical failure messages (`CreateSpaces` two-level tilted, 4 x `Extend3DPlaneTarget`, `Extend3DRegression` PR49-Test2, 7 x `GoldenMaster`, `PR61Face3DHomeParity`, 4 x `WorkflowParity`). They are pre-existing and untouched. The 2 skipped tests are the same on both and are **not** counted as passed: `NativeMissingIntegrationTests.ShellsUnion_NativeUnavailable_ReturnsNullAndReportsNativeMissing` (inverse-gated, native is present) and `LargePanelSewGuardIntegrationTests.AdjacencyCluster_LargePanelSoupWithSewBeforeBuild_SkipsNativeSew`. The 3 new `DiagnosticsDetailIntegrationTests` pass on the real house.

**GH contract-test harness note (pre-existing, not Phase 3).** `Testing/SAM.OCCT.GrasshopperTests` runs in a plain testhost with the NuGet Grasshopper kernel and no Rhino host. Instantiating any `GH_SAMVariableOutputParameterComponent` calls `SAM.Core.Grasshopper.Query.Clone` (SAM, unchanged since 2026-01), which touches `Grasshopper.Instances.ComponentServer`; without Rhino that raises a modal "Grasshopper Loading Error" (`rhcommon_c` not found) and the testhost waits for a click. The baseline behaves identically (its first test took 20 s while the dialog waited for a human OK; unattended it hangs). The 26 / 26 and 15 / 15 above were obtained with the dialog dismissed (`WM_CLOSE`) by a driver script. Recommended follow-up (separate PR, not started): move this project to the NUnit + `Rhino.Testing` host that `SAM.Core.Grasshopper.Tests` already uses.

**Headless Rhino 8 acceptance** (machine-local harness, not committed: NUnit + `Rhino.Testing` 8.0.28-beta, in-process Rhino 8 and Grasshopper, metre `RhinoDoc`, the installed plugin loaded as users get it; components created from the component server, wired, solved with `GH_Document.NewSolution`). Inputs: the contemporary house (`SAM/SAM/SAM.Tests/Golden/PanelsBySolid/contemporary1_solids.json`, 19 solids, 18 apertures) as 38 `PanelsBySolid` panels (`maxThicknessRatio` default), and the 29 panels `Solve3D` returns for them; the saved definition `SAM_daily/2026-10-13-House/contemporary1/import-test.gh` (copied, not modified). All of the following held:
- **GUIDs, names, order, access, optional flags**: the only structural difference between base and head is the added `diagnosticDetail_` (Param_Integer, optional, voluntary) as the last input of `CreateAdjacencyCluster` and `Solve3D`; `CreateShells` is structurally unchanged. Component versions `0.2.0 -> 0.2.1`, `0.7.0 -> 0.7.1`, `0.3.0 -> 0.3.1`.
- **Saved definition**: `import-test.gh` loads; its `CreateAdjacencyCluster` keeps its 7 saved inputs in order, 3 outputs, canvas position (565, 759) and every wire (`_panels`, `spaces_`, `_run` in; `AdjacencyCluster`, `Diagnostics` out). `SAMCore.Update` (the `Modify.UpdateComponents` routine) updates it `0.2.0 -> 0.2.1`: names, position and wires unchanged, and the new input stays hidden. Inserting `diagnosticDetail_` at index 7, setting 1, saving and reopening keeps all 8 inputs, the value and the wires. (The 24 IO messages on opening that file are Rhino model-object components written by a newer Grasshopper; unrelated to SAM.)
- **Old instance (no input) and unwired input** fall back to Summary and give identical `Diagnostics` to level 0.
- **Levels** (`Diagnostics` line counts, base -> head 0 / 1 / 2): `CreateAdjacencyCluster` status 40 case 7 -> 3 / 6 / 7; `CreateAdjacencyCluster` success 21 -> 3 / 18 / 21; `Solve3D` 114 -> 2 / 49 / 114. Each level is an order-preserving subset of the next; **level 2 equals the base list in text and order for all three** (timings and GUIDs normalised). Warnings and Errors are the same at every level.
- **Invalid values** (5 and -1): an orange Warning that names `diagnosticDetail_`, first `Diagnostics` line says Summary is used, the rest equals Summary, results unchanged.
- **Results identical** across all levels, old instance, invalid values, and equal to base: `CreateAdjacencyCluster` cluster (2 spaces, 25 panels, same panel types and areas) and `Solve3D` panels (same types and areas), `Successful` unchanged.
- **Severity as GH message levels** (base -> head): status 40 `CreateAdjacencyCluster`: 7 Warnings -> 2 Errors (`SAM_OCCT_NATIVE_FAILED`, `SAM_OCCT_ANALYTICAL_PANEL_REBUILD_FAILED`) + 1 Warning (`SAM_OCCT_VALIDATE_INPUT`); success `CreateAdjacencyCluster`: 21 Remarks -> none (Info is log only); `Solve3D`: none -> 1 Warning (the solver's own `Heal/RejectedSew` Warning); `CreateShells` on the house faces: 2 Remarks -> 1 Error (`SAM_OCCT_NATIVE_FAILED`) + 1 Warning (`SAM_OCCT_VALIDATE_INPUT`). No Remark is raised from Info at any level.
- **Grouping** did not hide distinct reasons: each distinct code stays its own message (the Errors above are two different codes, not merged). The 3-per-code Warning overflow is **not exercised** by this house (at most 1 Warning per code); it is covered by the unit tests only.

**Status 40, base vs head** (`CreateAdjacencyCluster` on the 38 `PanelsBySolid` panels, library call and GH component): both give native status 40 ("MakerVolume produced no closed solid"), `Success` false, no cluster, and `SAM_OCCT_VALIDATE_INPUT`: INVALID, NOT watertight, 17 naked edges (near (4.51, 9.447, 6.445)), 3 self-intersections. The full library output (144 lines including the `Solve3D` -> `CreateAdjacencyCluster` follow-up, which yields 2 spaces and 25 panels) is byte-identical between base and head after normalising GUIDs and timings. The failure is **pre-existing and independent of Diagnostics**; no geometry or native repair was attempted.

## Limitations and follow-ups
- Not verified: the visual balloon colours on an interactive canvas. The headless run checks the Grasshopper runtime message level (Warning / Error / none) that sets the colour, not the pixels.
- The 3-warnings-per-code overflow grouping is covered by unit tests but not by the house data.
- `Testing/SAM.OCCT.GrasshopperTests` still needs a Rhino host to run unattended (harness note above); not part of hosted CI.
- The 19 pre-existing native integration failures remain (unrelated to Diagnostics).
- Phase 4 (OpenStudio / TAS adapters) is not started.

## Windows / Rhino 8 acceptance (checklist as written in the cloud run; passed, see results above)
1. Build SAM `sow/2026-Q4` Release, then this branch (`dotnet build SAM_OCCT.sln -c Release`, native built with `build-native.ps1`), restart Rhino 8; model units metres.
2. `dotnet test Testing/SAM.OCCT.UnitTests -c Release` (expect 700 pass), `dotnet test Testing/SAM.OCCT.GrasshopperTests -c Release` (expect the 15 existing + 11 new pass), `dotnet test Testing/SAM.OCCT.IntegrationTests -c Release` (record passed / failed / skipped; compare the failing names with a run on base `46dac7a`; the 19 known failures must be identical, the 3 new `DiagnosticsDetailIntegrationTests` must pass).
3. Open a saved definition with `Solve3D`, `CreateAdjacencyCluster` and `CreateShells` (e.g. `contemporary1/import-test.gh`): it loads, wires intact, "update component" mark only; run `SAM.CoreUpdate` and check wires and values survive.
4. Default: `Diagnostics` shows the outcome lines and every Warning/Error; no timing / metadata lines. Show `diagnosticDetail_` (zoom, +) and set 1, then 2: more lines appear; 2 equals the earlier output. Panels, AdjacencyCluster, spaces, volumes identical at 0 / 1 / 2 (compare counts and a bake).
5. Set 5 or -1: an orange Warning names `diagnosticDetail_`, `Diagnostics` line 1 says Summary is used, results unchanged.
6. Status 40: feed the `CreatePanelsBySolids` output of the house straight to `CreateAdjacencyCluster`: expect `SAM_OCCT_NATIVE_FAILED` status 40 as a red Error and `SAM_OCCT_VALIDATE_INPUT` (17 naked edges) as an orange Warning, `Successful` false (the `SAM_OCCT_OPEN_SHELL_ANALYSIS` line named in the cloud run is not produced on this path; see Status 40). Repeat on base: same status and numbers. Then `Solve3D` -> `CreateAdjacencyCluster`: expect a closed result.
7. Balloon colours: Info none, Warnings orange (at most 3 per code plus one count line), Errors red.

## Next step
Windows acceptance is done (above). Merge once hosted checks are green on the final head, then the post-merge `PROJECT_PROGRESS.md` closeout on `sow/2026-Q4` (docs-only, `[skip ci]`). Phase 4 (OpenStudio / TAS adapters) is a separate PR and is not started. Recommended: Opus, high effort, new session, Plan mode.
