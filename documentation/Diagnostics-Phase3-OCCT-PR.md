# Unified Diagnostics, Phase 3 (SAM_OCCT integration) - PR record

Base: `sow/2026-Q4` (`46dac7a`). Branch: `claude/determined-wright-9r0q71`. Dependency baseline: SAM `sow/2026-Q4` with SAM#201 (`293eb645`) and SAM#202 (`fca3d4d4`, closeout `d6c35692`); no SAM or SAM_Solver change.
Validated content SHA: `c50c0b9c143868839b025e2144fbb2a6e0d1983b` (code and tests). A record-only commit follows; the final head and hosted checks are in the PR description.
Merge needs owner approval and the Windows/Rhino 8 acceptance below (AGENTS.md). `PROJECT_PROGRESS.md` is not touched (post-merge closeout only).

## Status
Implementation complete; Linux build and unit tests pass. **Not run here:** everything that needs Windows, Rhino 8 or the native OCCT library (below). Draft PR.

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
- Not run: the native call itself on this input (no native here). Windows step 6 below confirms it.
- No solver repair attempted.

## Risks
- Visible: the default `Diagnostics` list shrinks; canvas message levels change as listed under Decisions.
- Runtime needs a SAM build with SAM#201 (`SAM.Core.Diagnostic`); an older `SAM.Core.dll` would fail with a type load error. CI and the installer take SAM `sow/2026-Q4`.
- `OcctDiagnosticReporter` is compiled into two assemblies (internal); the GH tests see only the Analytical copy. Unverified on a canvas.
- Other OCCT GH components still use their own Remark handling (out of scope).

## Windows / Rhino 8 acceptance (owner, merge gate)
1. Build SAM `sow/2026-Q4` Release, then this branch (`dotnet build SAM_OCCT.sln -c Release`, native built with `build-native.ps1`), restart Rhino 8; model units metres.
2. `dotnet test Testing/SAM.OCCT.UnitTests -c Release` (expect 700 pass), `dotnet test Testing/SAM.OCCT.GrasshopperTests -c Release` (expect the 15 existing + 11 new pass), `dotnet test Testing/SAM.OCCT.IntegrationTests -c Release` (record passed / failed / skipped; compare the failing names with a run on base `46dac7a`; the 19 known failures must be identical, the 3 new `DiagnosticsDetailIntegrationTests` must pass).
3. Open a saved definition with `Solve3D`, `CreateAdjacencyCluster` and `CreateShells` (e.g. `contemporary1/import-test.gh`): it loads, wires intact, "update component" mark only; run `SAM.CoreUpdate` and check wires and values survive.
4. Default: `Diagnostics` shows the outcome lines and every Warning/Error; no timing / metadata lines. Show `diagnosticDetail_` (zoom, +) and set 1, then 2: more lines appear; 2 equals the earlier output. Panels, AdjacencyCluster, spaces, volumes identical at 0 / 1 / 2 (compare counts and a bake).
5. Set 5 or -1: an orange Warning names `diagnosticDetail_`, `Diagnostics` line 1 says Summary is used, results unchanged.
6. Status 40: feed the `CreatePanelsBySolids` output of the house straight to `CreateAdjacencyCluster`: expect `SAM_OCCT_NATIVE_FAILED` status 40 with `SAM_OCCT_OPEN_SHELL_ANALYSIS` (about 201 naked edges) as a red Error, `Successful` false. Repeat on base: same status and numbers. Then `Solve3D` -> `CreateAdjacencyCluster`: expect a closed result.
7. Balloon colours: Info none, Warnings orange (at most 3 per code plus one count line), Errors red.

## Next step
Owner runs the Windows acceptance; then merge on approval (checks green on the final head), then the post-merge `PROJECT_PROGRESS.md` closeout on `sow/2026-Q4`. Phase 4 (OpenStudio / TAS adapters) is a separate PR and is not started. Recommended: Opus, high effort, new session, Plan mode.
