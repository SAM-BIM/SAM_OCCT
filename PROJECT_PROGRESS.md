# Project Progress - SAM_OCCT (2026-Q4)

## Q4 closeout: SAM_OCCT#83 (chain degenerate robustness) - 2026-10-25

- **PR/status:** SAM-BIM/SAM_OCCT#83 merged into `sow/2026-Q4` as `9794a52fbf7c306ed20b9443a013adc6a7ea7856` (parents base `ac9bbc98`, approved head `49e6cf9af03a049d6528b16c1bfe82a0e3989b2b`) with `--merge --match-head-commit`, no auto-merge or bypass, after owner authorisation in chat and the SAM merges #220/#222/#221 (SAM Q4 tip `945eea51`).
- **Completed:** `AdjacencyCluster.cs` and `SnapStage.cs` degenerate-geometry robustness (mean-width test, length vs distance tolerance), with `FaceOffsetLocationTests.cs`, `SnapStageTests.cs` and record `documentation/OCCT-Chain-Degenerate-Robustness-Q4.md`. Merge diff against base: 5 files, +321/-4.
- **Validation:** final-head Build run `40223907896` was re-run (attempt 2) after the SAM merges: success; SAM and SAM_Solver cloned at `sow/2026-Q4` (SAM tip `945eea51`, SAM_Solver tip `fd63b467`); unit 1038/1038, integration 31 passed/465 skipped (native tests skip on hosted CI - this is not a native full-suite pass). Post-merge Q4 Build `40226066541` on `9794a52f` success. Saved native run (reviewer-compared): 469 passed/25 failed/2 not executed, all 27 non-passing names/outcomes/messages match the baseline. Independent reviewer: OCCT unit 1038/1038. No engine, simulation, deployment, native rebuild or fixture/golden/tolerance change.
- **Unresolved / limits:** the record's older Fix paragraph still describes the superseded area guard (later Review correction section is current); not changed here. Native baseline failures and the Softhouse limits (ownership miss, two missing relations, six not-enclosed cells) remain; no full Softhouse/simulation acceptance is claimed.
- **Cleanup:** remote `fix/occt-chain-degenerate-robustness-q4` deleted with a lease on `49e6cf9a` after the merged/unprotected/no-open-PR checks; local branch kept because worktree `SAM_OCCT-wt-clean3d-degenerate` uses it.
- **Next step:** owner decision on the next Softhouse step (open-plan virtual boundaries).

## Q4 towers residual closure, CreateShells sew guard (+ nine-space / North0) closeout - 2026-10-23

- **PR**: SAM-BIM/SAM_OCCT#82 -> `sow/2026-Q4`, **merged 2026-10-23**, merge commit `e5efa67946e2384b5da8fca1b967ce48b6a7e42e`.
  - Parents: base `819962df361fb7037a5ef762330ed196357f938c` and the final head.
  - Pinned to final PR head `f9f9547118392660f6cee158a301ff88991426d3` (`--match-head-commit`, merge commit).
  - The branch was not based on the Q4 tip: merge-base `3523ed2`, 15 ahead / 4 behind. The 4 Q4-only commits were
    #81 records only. Clean merge; not rebased.
- **Status**: complete.
  - Merge authorised by the owner in chat after independent reviews (towers managed-path revision: accept;
    CreateShells: wording revision, then accept).
  - `sow/2026-Q4` has no branch protection or required reviews; none bypassed. The PR had no submitted reviews and no
    review or bot comments.
- **Work** (three streams; first publication of the two inherited ones):
  - **Towers rule E** (`ResidualContactClosure`, `0dac3f4`/`119eae0`): recovers the middle towers room (119.364 m³)
    on the one-step, untilted, uniform Extend3D path, and in the forced-managed adopted complex (113.717 / 119.364 m³
    at bucketBetweenLevels 0 / 0.21). Fails closed on every ambiguous case; directional, two-step and tilted paths
    unchanged. Managed-path tests (`9441a07`, byte-identical to the reviewed diff `4d7ad14a...`). Record
    `documentation/Towers-Residual-Closure-Q4.md`.
  - **`SAMOCCT.CreateShells` sew guard** (`804ead6`, version 0.3.1 -> 0.4.0): `ValidateSewCoverage = true` when
    `sewBeforeBuild_` is true. Before, on the forced-managed towers outputs, the component lost the west room (20 of
    24 shells) and five cells at 0.21 (22 of 27), reported as success; now it matches the guarded reference. GUID and
    parameters unchanged; the tooltip states every guard outcome. Record `documentation/CreateShells-Sew-Guard-Q4.md`.
  - **Inherited, first published here:** nine-space wall-capture acceptance test (`ef9bf81`..`4d77ef4`, record
    `Nine-Space-Wall-Capture-Q4.md`); North0 consolidation follow (`ff71a70`..`2a09f69`, production change in
    `Panel3DSnapSolver.ConsolidateWallStacks` and `SnappedPanel.MoveBoundaryRun`, record
    `North0-Consolidation-Follow-Q4.md`).
- **Files changed** (24): Solver `Panel3DSnapSolver.cs`, `ResidualContactClosure.cs` (new), `SnapStage.cs`,
  `SnappedPanel.cs`, `ConditionStage.cs`; Grasshopper `SAMOCCTCreateShells.cs`; unit tests
  `ResidualContactClosureTests.cs`, `ConsolidateWallStacksTests.cs` (new); integration tests (towers residual, nine-space,
  North0 new; PR61 towers, bucket-lever, workflow-parity pins reconciled by measurement); Grasshopper tests
  `CreateShellsSewGuardComponentTests.cs`, `NativeRuntime.cs` (new), `GrasshopperTestAssembly.cs`,
  `DiagnosticsContractTests.cs`, csproj; `TESTING.md`; four records. No SAM, SAM_Solver, native, fixture, golden,
  tolerance or CI change.
- **SHAs**: rule-E production content tested `119eae0fd695a84e310b43fd66fbdde2d6a098b4`. Final tested content
  `804ead6`, i.e. `229f971` + both reviewed diffs. `f9f9547` is records-only.
- **Hosted evidence (managed code only)**:
  - Final head `f9f9547`, checked-out merge ref `76d6e53`: Build (Windows) run `40005815034` succeeded, with unit
    1032 / 0 / 0 and integration 31 passed / 465 skipped (no native on the runner); the Grasshopper test project
    compiled but is not run by CI. SPDX run `40005814882` succeeded.
  - Post-merge Build (Windows) run `40006919879` on `e5efa67`: still in progress when this closeout was written;
    its result is not recorded here.
- **Local evidence (machine-local; frozen native SAM.Occt.Native 438089F8..., TKBO 1FA732FE..., ABI 4)**:
  - unit 1032 / 0;
  - full native 496 = 469 passed / 25 failed / 2 not executed, with non-passing names and messages identical to
    the reviewed managed-path run;
  - `SAM.OCCT.GrasshopperTests` 33 / 33, and 30 / 3 with the component change reverted (the intended failures);
  - nine-space owner exports identical to `791bcaa`;
  - builds at baseline warning counts.
  - Evidence: `SAM-BIM/.evidence-towers-managed-tests-20261023/`, `SAM-BIM/.createshells-sew-guard-20261023/`, and
    the review folders named in the records.
- **Remaining failures and limits**:
  - 25 native failures (19 historical + 6 rigid-motion). Among them the two already-failing towers golden masters
    (`Solve3D_ManagedPath`, `..._021`) changed message: they measure an unguarded sew recount (now 23 vs 21
    expected, 27 vs 25) and were left failing, not re-pinned; their disposition is open. 2 not-executed
    (unchanged).
  - `Create.Spaces` still refuses the default forced-managed towers result (13 naked edges).
  - Two neighbouring managed cells per chain are re-faced (boundaries preserved).
  - Only towers fires rule E in the suite; two-step and tilted chains keep the leak by design.
  - Sew guard: explicit sewing is guarded in both Grasshopper components. A valid but suspect sew build may be kept
    with a Warning and `Successful` true. Still unchecked: the automatic sew retry with sewing off, the golden
    recount, and API callers without `ValidateSewCoverage`.
  - The Grasshopper test project is not in CI. The guard's no-valid-replacement branch has no component fixture.
- **Cleanup**: remote `fix/towers-residual-closure-q4` deleted after confirming head `f9f9547`, fully merged, no
  open PR, unprotected. Kept, because each is checked out in a worktree (worktrees are never removed automatically),
  though fully merged:
  - local `fix/towers-residual-closure-q4` (`SAM_OCCT-wt-towers-residual-closure`);
  - local `fix/north0-consolidation-follow-q4` (`SAM_OCCT-wt-north0-consolidation-follow`);
  - local `test/nine-space-wall-capture-q4` (`SAM_OCCT-wt-nine-space-wall-capture`).

  The last two were never pushed. Scratch folder `SAM-BIM/SAM_OCCT-base-229f971-ghcheck` (an archive copy) can be
  deleted.
- **Next step**: owner disposition of the two towers golden masters; optionally a separate task for the remaining
  unguarded sew paths. Removing the three merged worktrees and their local branches is a separate cleanup decision.

## Q4 sew guard against invalid cells (Scope 1) closeout - 2026-10-23

- **PR**: SAM-BIM/SAM_OCCT#81 -> `sow/2026-Q4`, **merged 2026-10-23**, merge commit `a6ed4a9569280dd4d0e0162090c06c859a5bcaca` (parents `dc871f7e6ee7d0e53adb826a2b597645e2eab4cb` and the final head), pinned to final PR head `6c259cd788ece4fc476125ad586bb5e38ebaf5ba` (`--match-head-commit`, merge commit). The merge tree equals the head tree `de4ca310...`. Base before merge `dc871f7`.
- **Status**: complete. Merge authorised by the owner in chat, with the policy acceptances below. `sow/2026-Q4` has no branch protection, rules or required reviews; none bypassed; the PR had no submitted reviews. Branch `fix/sew-guard-invalid-cells-q4` (remote and local) and its worktree `SAM_OCCT-wt-sew-guard` were deliberately kept for the follow-up dependency task, not cleaned up.
- **Work**: the sew path rejects invalid (duplicate / overlapping) cells, with one overlap criterion everywhere: two cells overlap iff their common region holds a ball of diameter greater than the glue band `b = max(Tolerance, FuzzyTolerance)`. `CommonVolume` no longer accepts disappearance at the coarse fuzzy tolerance as a seam; undecided pieces go through the bounded glue-band proof `OcctGlueBandProof.cs` (internal). Duplicate and partial-overlap builds fail closed (`SAM_OCCT_SEW_BUILD_INVALID` Warning + `SAM_OCCT_BUILD_REJECTED` Error); a 0.5 mm seam is accepted at 0 and 45 degrees. New codes `SAM_OCCT_SEW_BUILD_INVALID`, `SAM_OCCT_BUILD_REJECTED`, `SAM_OCCT_SEW_VOLUME_CONFLICT`. `ValidateSewCoverage` stays opt-in. Full record: `documentation/Sew-Guard-Invalid-Cells-Q4.md`.
- **Files changed**: `SAM.Geometry.OCCT/Native/OcctCellComplexBuilder.cs`, `OcctGlueBandProof.cs` (new), `OcctUnderSplitGuard.cs`, `Properties/AssemblyInfo.cs`; `Testing/SAM.OCCT.UnitTests/OcctGlueBandProofTests.cs` (new), `SewInvalidCellGuardTests.cs` (new); `Testing/SAM.OCCT.IntegrationTests/SewGuardInvalidCellIntegrationTests.cs` (new), its `.csproj` and two fixtures under `Fixtures/Revit-Reconstruction/`; `TESTING.md`; the record. No SAM or SAM_Solver change.
- **SHAs**: tested production content `a135c5f4a6e25dd1c40a11a4881219c1271bc4a3`. `3523ed264438f9fc7f1dd7a9eb5ee90c71ca8ee2` (record + TESTING.md; carried a CI-skip marker, so no checks) -> `0718dbcf58cc196f1bd71b30a3ca689b8af3e7ea` (empty commit, tree `2cd205d9...` identical to `3523ed2`, raised the first hosted checks) -> `6c259cd` (record-only; production, test and workflow content identical to `0718dbc`).
- **Hosted evidence (GitHub, managed code only)**: on `0718dbc` Build (Windows) run `39920956168` and SPDX run `39920956200` succeeded; on the final head `6c259cd` Build (Windows) run `39923098244` (checked-out merge ref `c2278c2`) and SPDX run `39923098066` succeeded; post-merge Build (Windows) run `39928341148` succeeded on `a6ed4a95`. Unit 975 passed / 0 failed / 0 skipped, including 275 managed guard and proof cases. Integration 31 passed / 0 failed / **422 skipped** because the native library is unavailable on the runner (`SAM_OCCT_SKIP_NATIVE_BUILD=true`), including all 171 native guard cases. H12 provenance assertion passed. Dependencies cloned at `sow/2026-Q4` (logs show the branch, not SHAs); inferred tips SAM `0168d03f7e9e56775868aecaca73d8224a2034c5`, SAM_Solver `fd63b4670cd4baeb20a74312aacc17eb177ecc18`.
- **Local evidence (machine-local, not committed; native frozen copy SAM.Occt.Native.dll 438089F8..., TKBO.dll 1FA732FE...)** at `a135c5f`: unit 975 passed; focused native (`SewGuardInvalidCell`, `HouseUnderSplit`, `HouseAirAndAttic`) 187 passed; saved full native 432 passed / 19 failed / 2 skipped; 96 of 96 guarded builds on the six real fixed panel sets give identical outcomes. No new native execution was done for the merge, and no green full-native claim is made. Local validation used SAM `ba686d24`, hosted used the `sow/2026-Q4` tip above. Evidence folders: `SAM-BIM/.review-sew-guard-q4-20261022/` (see the record, "Machine-local evidence").
- **Remaining historical failures and skips**: 19 native integration failures, identical by name and message to base `dc871f7` and earlier saved runs (solver golden and parity tests; none sets `ValidateSewCoverage`); 2 skips (the large-panel sew-guard skip and the inverse-gated native-missing test). Not fixed here; separate follow-up.
- **Owner decisions (2026-10-23, accepted in the merge authorisation, recorded in the PR description)**: (1) the ball-diameter overlap semantics, including the strict L-seam behaviour (L-shaped intersections 0.86-1 mm wide are overlaps); (2) conservative replacement/rejection for undecidable geometry (exhausted 20,000-box proof budget, unusable geometry, inadequate closure) and the disclosed runtime (+2 % to +60 % on the recorded sets for guarded calls; a spent budget 0.6-1.5 s on synthetic pieces); (3) reliance on the fine kernel (fuzzy b/10) finding and locating the relevant common pieces. **Unchanged:** the #76 closure policy (closure band). The record file in the repo still lists these as open decisions; this entry and the PR description supersede that wording.
- **Limits accepted with the merge**: the proof is a bounded numerical routine, not a certification; it never ran on the six real sets (synthetic evidence only), so a real piece too complex for the budget fails closed; rotation invariance for hole-bearing shells is not established; 4 of 80 guarded builds on fixed sets still under-split versus the direct decomposition and are flagged `SAM_OCCT_BUILD_UNRELIABLE`; `OcctGlueBandProof.cs` is 1,122 lines.
- **Not done / not authorised**: restoring the two damaged machine-local files `02_July_Solve3D-output_105-panels.sam` and `03_Current_Solve3D-output_131-panels.sam` in `.review-occt-integration19-20261022/sam-models/` (separate task; reconstructions are staged and are not byte-identical); no deployment; no native rebuild; nothing changed in the North0 or nine-space work.
- **Separate follow-up (North0 / motion)**: local-only branches `fix/north0-consolidation-follow-q4` (head `2a09f69d0e3798dfb1369033cf06282d59b16a26`, worktree `SAM_OCCT-wt-north0-consolidation-follow`) and `test/nine-space-wall-capture-q4` (head `4d77ef415074793fc5f2ef484d5edbad1421ddda`, worktree `SAM_OCCT-wt-nine-space-wall-capture`) are unpublished, unmerged and untouched. Recorded state there: 25 failures = 19 historical + 6 motion. To preserve (owner's wording): the owner-approved 0.55 and the unchanged 0.45 / nine rooms at 0.55 / 0.65.
- **Next step**: separately prepare North0 publication and dependency handling for review (these branches now need to be based on, or reconciled with, the merged `sow/2026-Q4` at `a6ed4a9`), preserving the owner-approved 0.55 and the unchanged 0.45 / nine rooms at 0.55 / 0.65 (owner's wording). Do not start it automatically. Recommended: Sonnet, medium, new session, Plan mode first (publication and rebase decisions), then Build.

## Q4 CLAUDE.md attribution wording closeout - 2026-10-16

- **PR**: SAM-BIM/SAM_OCCT#80 -> `sow/2026-Q4`, **merged 2026-10-16**, merge commit `c8fc4a0896bca0f6b1edfeeaecda3b16322deee1` pinned to approved head `3614b1e4b5e941965b5987df352766118273e847` (`--match-head-commit`).
- **Status**: complete. PR-head build and spdx green on `3614b1e4`; post-merge Build (Windows) run `38888620582` succeeded on `c8fc4a08`. `sow/2026-Q4` has no branch protection or required reviews; none bypassed. No review findings.
- **Work**: `CLAUDE.md` attribution line changed from "Generated by Michal Dengusiak & Claude Code" to the owner-approved "Generated by Michal Dengusiak and CodeClaude" (resolves the conflict noted in the engineering standards rollout, SAM_OCCT#79).
- **Files changed**: `CLAUDE.md` only (one line). No source, tests or build files.
- **Decisions**: `documentation/Agent-Continuity-Pilot-Q4.md` line 154 still contains the old wording; left unchanged as historical evidence. Commit authorship and Co-Authored-By trailers untouched.
- **Cleanup**: remote and local `docs/sam-occt-claude-attribution-q4` deleted after confirming it is fully merged and no open PR uses it.
- **Next step**: none required.

## Q4 engineering standards references closeout - 2026-10-16

- **PR**: SAM-BIM/SAM_OCCT#79 -> `sow/2026-Q4`, **merged 2026-10-16**, merge commit `aa3c7904c2695e7701bc46aa3d9891c1853aac5d` pinned to approved head `e8212bbb17751211d0d9c72a9d5f306a8c1731e0` (`--match-head-commit`). Part of a five-repository rollout (SAM_OCCT#79, SAM_OpenStudio#29, SAM_Tas#102, SAM_Tas_Grasshopper#15, SAM_Psychrometrics#13).
- **Status**: complete. PR-head build and spdx green on `e8212bbb`; post-merge Build (Windows) run `38886631948` succeeded on `aa3c7904`. `sow/2026-Q4` has no branch protection or required reviews; none bypassed. No review findings were posted.
- **Work**: added a concise "Engineering Standards" section to `AGENTS.md` (+10 lines) linking SAM's canonical `docs/DIAGNOSTICS_STANDARD.md` and `docs/GH_COMPONENT_STANDARDS.md` on `sow/2026-Q4` (SAM PR #205) by stable GitHub URL; standards not copied. Diagnostics changes follow the first; Grasshopper changes follow the second where applicable. Existing rules unchanged.
- **Files changed**: `AGENTS.md` only. No source, tests, GH definitions or build files.
- **Validation**: both links return HTTP 200 and both files exist on SAM `sow/2026-Q4` (GitHub API). Diff scope checked as AGENTS.md only; CRLF preserved. Hosted checks as above.
- **Attribution**: owner-approved wording "Generated by Michal Dengusiak and CodeClaude" used in the PR description.
- **Cleanup**: remote and local `docs/sam-occt-engineering-standards-refs` deleted after confirming it was fully merged into `sow/2026-Q4` and no open PR used it.
- **Unresolved**: none. Follow-up for owner decision: SAM_OCCT/CLAUDE.md still says "Generated by Michal Dengusiak & Claude Code", which conflicts with the approved wording; left unchanged, propose a separate docs PR.
- **Next step**: none required.

## Q4 unified Diagnostics phase 3 (OCCT) closeout - 2026-10-15

- **PR**: SAM-BIM/SAM_OCCT#78 -> `sow/2026-Q4`, **merged 2026-10-15**, merge commit `de2744a70bd178d2850bad8bef9f6f646ad447c4`, pinned to approved head `022783aa4861729fee05dbdc55e0218bede6890d` (`--match-head-commit`). Validated code SHA `c50c0b9c143868839b025e2144fbb2a6e0d1983b`; the two commits after it (`1094d6a`, `022783a`) are record-only. Base before merge `46dac7a`. Depends on SAM#201 (`293eb645`) and SAM#202 (`fca3d4d4`); no SAM or SAM_Solver change.
- **Status**: complete. PR-head CI (Build (Windows) 4m39s, spdx) green on `022783a`; post-merge Build (Windows) run `38748494608` succeeded on `de2744a7`. CI builds with `SAM_OCCT_SKIP_NATIVE_BUILD=true`; native behaviour was covered by the owner-machine Windows run below.
- **Work**: structured severity for the OCCT Grasshopper components. New additive `SAM.Core.OCCT` adapters (`DiagnosticRecord`, `DiagnosticMessage`, `ToDiagnosticSeverity`, `ToDiagnostic`, `CreateDiagnosticRecord`, `GetDiagnosticDetail`, `FilterDiagnosticRecords`, `GetDiagnosticMessages`), `SolverDiagnosticExtensions`, `Solve3DReport.DiagnosticRecords` and `SolverReportFormat.FormatDiagnosticRecords`; the linked `Grasshopper/Shared/OcctDiagnosticReporter.cs`. `SAMOCCT.Solve3D` 0.7.1 and `CreateAdjacencyCluster` 0.2.1 gain the hidden optional `diagnosticDetail_` (default Summary); `CreateShells` 0.3.1 maps severity only. Info is log only, Warning is a GH Warning, Error is a GH Error; the hard-coded warning-code list is gone. No geometry, solver or native change; GUIDs, names and parameter order unchanged. Full record: `documentation/Diagnostics-Phase3-OCCT-PR.md` (including the machine-local evidence description).
- **Windows / Rhino 8 acceptance (2026-10-15, same machine, base `46dac7a` vs head, machine-local harness not committed)**: Release build 0 errors both; unit 654/654 -> 700/700; native integration 258 passed / 19 failed / 2 skipped -> 261 / 19 / 2, the same 19 pre-existing failures with identical messages, the 2 skips identical and not counted as passes, the 3 new house tests pass; GH contract tests 15/15 -> 26/26 (needs a dismissed modal dialog, see below); headless Rhino 8 acceptance (NUnit + `Rhino.Testing`) 7/7. Saved `contemporary1/import-test.gh` loads with positions and wires; `SAMCore.Update` 0.2.0 -> 0.2.1 keeps them; the inserted `diagnosticDetail_` survives save and reopen. `Diagnostics` lines at levels 0/1/2: status-40 case 3/6/7 (base 7), success 3/18/21 (base 21), `Solve3D` 2/49/114 (base 114); level 2 equals the base text and order; panels, cluster and `Successful` identical at every level and to base; invalid values 5 and -1 warn and fall back to Summary; old instances read Summary. Severity: status 40 now 2 GH Errors + 1 Warning (base 7 Warnings); success path no Remarks (base 21); `Solve3D` raises the solver's own Warning; `CreateShells` Error + Warning (base Remarks).
- **Status 40 (`SAM_OCCT_NATIVE_FAILED`)**: reproduced on the 38 `PanelsBySolid` house panels and **identical on base and head** (native status 40, `SAM_OCCT_VALIDATE_INPUT` 17 naked edges and 3 self-intersections; the 144-line library output is byte-identical after normalising GUIDs and timings; `Solve3D` -> `CreateAdjacencyCluster` gives 2 spaces and 25 panels on both). Pre-existing and independent of Diagnostics; no geometry or native repair attempted. Correction to the cloud record: `SAM_OCCT_OPEN_SHELL_ANALYSIS` is not produced on this path (only by the vertical-face shell path); corrected in the record.
- **Decisions**: default Summary (owner-chosen, Phase 2 convention) means the default `Diagnostics` list no longer carries Standard/Detailed Info; set `diagnosticDetail_` 2 for the previous full list. Warnings and Errors are never dropped. Severity is the producer's, never inferred from text.
- **Merge**: pinned merge commit after the owner's explicit approval in chat; `sow/2026-Q4` has no branch protection or required reviews, none bypassed. The first merge attempt was blocked by the agent permission check ("merge without review") and was retried only after that approval.
- **Cleanup**: remote `claude/determined-wright-9r0q71` deleted with `--force-with-lease` on `022783aa4861729fee05dbdc55e0218bede6890d` after verifying the remote head equals the approved head, it is contained in `sow/2026-Q4`, no unpushed local commits, no open PR uses it as head or base, not protected, no worktree uses it; the ref is confirmed gone. The local branch was deleted with `git branch -d`. The temporary baseline copy `SAM_OCCT_base_tmp` (a plain folder, not a worktree) was removed. Other worktrees were not touched.
- **Unresolved / limitations**: (1) balloon colours were checked as Grasshopper runtime message levels, not as pixels on an interactive canvas. (2) The 3-warnings-per-code overflow grouping is covered by unit tests only; the house gives at most one Warning per code. (3) `Testing/SAM.OCCT.GrasshopperTests` runs in a plain testhost without Rhino and blocks on a modal "Grasshopper Loading Error" (`SAM.Core.Grasshopper.Query.Clone` -> `ComponentServer`); pre-existing, identical on base, not in hosted CI; they were run with the dialog dismissed by a driver script. (4) The 19 native integration failures remain. (5) Building the Grasshopper projects overwrites `%APPDATA%\SAM` and `Documents\SAM`.
- **Next step**: none required for this PR. Recommended follow-ups, each a separate PR and not started: (a) move `Testing/SAM.OCCT.GrasshopperTests` to the NUnit + `Rhino.Testing` host used by `SAM.Core.Grasshopper.Tests` (Sonnet, medium, new session, Build mode); (b) Phase 4, the OpenStudio / TAS diagnostics adapters (Opus, high, new session, Plan mode).

## Q4 under-split guard and Air/attic closeout - 2026-10-14

- **PRs**: SAM-BIM/SAM_OCCT#76 -> `sow/2026-Q4`, **merged 2026-10-14**, merge commit `79d0332e62be39de24401f448321e63813d41ba6`, head `33d1079838c1ef4af901d7c7de3430f844ed2f0c`. SAM-BIM/SAM_OCCT#77 (stacked on #76, same base), **merged 2026-10-14**, merge commit `3e688afcf8ff6197e0c2ac0c4140d104d15524a5`, head `bb97fe61ce1ead35c218f69214dcb6124156b076`.
- **Status**: complete. Post-merge Build (Windows) succeeded on `79d0332e` and `3e688afc` (GitHub). PR-head CI (build, spdx) was green on both heads before merge. CI builds with `SAM_OCCT_SKIP_NATIVE_BUILD=true`; native behaviour is covered by local integration tests only.
- **Work**: #76: detect and correct sew-before-build under-split in `CreateAdjacencyCluster` (`OcctUnderSplitGuard`, `FaceCoverage`, `FaceCoverageReport`, `OcctBuildOptions`, builder changes, House-TwoStorey-23panels fixture, `HouseUnderSplitIntegrationTests`, `FaceCoverageTests`; record `documentation/House-UnderSplit-Q4.md`). #77: keep Air panels as open boundaries, support an unconditioned attic, keep supplied panels' source parameters through `CreateAdjacencyCluster` (`SourceParameterCarrier`, `AdjacencyCluster.cs`, `SAMOCCTSolve3D`; `HouseAirAndAtticIntegrationTests`, `SourceParameterCarrierTests`; record `documentation/House-AirAttic-Q4.md`). After #76 merged, #77's diff reduced to its own 8 files and merged cleanly (verified with git diff / merge-tree against the new base).
- Manual Rhino/Grasshopper/TAS acceptance PASSED on 2026-10-14 (owner-run, machine-local, not committed): 11 correctly named TAS zones, 57 surfaces, 25/25 apertures matched, 50 aperture parts rebound, UseBEWidths ON volume 481.784 m³, no TAS validation errors, no unnecessary OpeningProperties warnings, PhysicalThickness preserved independently of TBD constructions.
- Merge order (sequential, merge commits, each pinned to its verified head SHA): SAM#194 `755bc514` → SAM_OCCT#76 `79d0332e` → SAM_OCCT#77 `3e688afc` → SAM_Tas#100 `4b253dc7`. `sow/2026-Q4` has no branch protection or required reviews; none bypassed. No review or codex comments existed on any of the four PRs. PRs were drafts and were marked ready immediately before merge.
- Remote task branches deleted with lease guards after verifying head SHA, full merge and no open PRs: SAM `feature/panels-by-solid-q4`, SAM_OCCT `fix/sew-under-split-q4` and `feature/air-panels-attic-q4`, SAM_Tas `feature/panels-by-solid-q4`. Local branches left in place (upstream gone).
- Unresolved (non-blocking): the informational wording "SAM panels are the physical inner surfaces" (SAM_Tas `WorkflowCalculator.cs` UseWidths note and `DIRECT_T3D_ROUTE.md`) is misleading for mid-surface panels; deliberately not changed during the merge. Small follow-up PR. The 19 pre-existing SAM_OCCT native integration failures remain documented and were not reinvestigated.
- **Next step**: none required for this stream. Untracked `Match.cs` and `PaneStampMatch.cs` in the local SAM_OCCT checkout are stray copies of SAM_Tas files, not part of any PR, left untouched.

## Q4 agent instructions rollout closeout - 2026-10-12

- SAM-BIM/SAM_OCCT#75 merged as `17e27cbf796c0ea3431977cd52dbe5add31b549c` (merge commit pinned to approved head `e8b39284ed0afc3731a7a704bd108f569606a3c8`; base `sow/2026-Q4` `c825a406606d2ac0a456431268eeaae538014951`; validated content `d7abc9be9ab2f5864335448f28d53cd81ff3b0ca`). Owner-authorized 2026-10-12 after an independent review (`AGENT_ROLLOUT_REVIEW_2026-10-12.md`, local to the owner's workspace, not committed).
- Docs only: AGENTS.md, plus the rollout record `documentation/Agent-Continuity-Rollout-Q4.md`. OCCT `CLAUDE.md` content untouched (already imports `AGENTS.md`). No product code, build, workflow, dependency pin or tool changed; this closeout is the only `PROJECT_PROGRESS.md` change.
- Validation: local documentation checks and commands are in `documentation/Agent-Continuity-Rollout-Q4.md` (`git diff --check`, file scope, UTF-8/LF/no BOM, import). Hosted checks at the approved head: build success (run 38257684835); spdx success (run 38257684679). Dependency selection: the build log shows no sibling dependency clones.
- Cleanup: remote docs/agent-instructions-rollout-q4 deleted with an expected-SHA lease on e8b39284ed0afc3731a7a704bd108f569606a3c8 after confirming it is contained in sow/2026-Q4 and no open PR uses it as head or base; the local branch and its worktree are preserved (worktree in use). Other branches, worktrees and the frozen quarters were not touched.
- Remaining gaps: no agent-client behaviour or token benchmark has been run.
- Exact next step: Continue from the shared `AGENTS.md`. Open item carried from the pilots: verify on each installed agent client, in a fresh Q4 session, that it follows the instructions without loading the full progress history, and record the result.

## Q4 agent continuity pilot closeout — 2026-10-12

- [SAM_OCCT#74](https://github.com/SAM-BIM/SAM_OCCT/pull/74) merged as `3d0a4725ba92ba65f2253501bf3866e12c783b76` (base `sow/2026-Q4` @ `e2724293`), reviewed head `9957f9acc42f17ca7474a59ca5482a047c0181ba` (validated content head `53a19b4bbb92dd98d3b57490cd601fad298fc329`). Merge commit; branch preserved. Owner-authorized; ZiolkowskiJakub's review is required only for merges to `master` (owner decision), and that review request remains open.
- Docs only, three files:
  - `AGENTS.md`: heading-first, task-specific progress reads; section position is not a period marker; SAM-BIM-only operations; quarter transition with the release-promotion closeout on the outgoing `sow` branch, never `master`; handoff evidence rules; `[skip ci]` closeouts.
  - `CLAUDE.md`: adds the `@AGENTS.md` import. The legacy text is unchanged, and master's `CLAUDE.md` (blob `14a41e6`) must be preserved without the import at promotion.
  - Record: `documentation/Agent-Continuity-Pilot-Q4.md`.
- Validation:
  - Local Linux cloud run of the record's Bash block passes at the content and final heads: only the three files changed; `PROJECT_PROGRESS.md` untouched on the PR; footprint measurement `16547 2317`. The PowerShell block was not run on Windows.
  - GitHub on the exact head: `build` (windows-2022) run `38246819673` and `spdx` run `38246819521` green.
  - No product build or native tests were run (docs-only change), and there is no agent-client token benchmark.
  - The machine-local organisation audit stays on the owner's workstation and is not committed.
- Review:
  - The Copilot re-review failed before reviewing (HTTP 402 quota); Claude acted as reviewer at the owner's direction and found no substantive findings.
  - Kimi's review was supplied externally by the owner and is not on GitHub.
  - Copilot thread `discussion_r4246653284` is answered and left unresolved for its reviewer.
- Owner-authorized layout label in this commit: `## Current status` is marked as superseded bootstrap text, and `## Last updated` adds this entry. Verified that no Q4 entry sits beneath the frozen Q3 heading, so nothing was moved. No entry text was rewritten.
- Remaining: companion SAM#191 is independent (merged `02bbafb8`, SAM closeout `46e96b72`).
- Exact next step: on each installed agent client (Claude/Codex/Cursor/Hermes), start a fresh Q4 session and verify that it follows `AGENTS.md` without reading the full history, recording input/cache tokens and handoff correctness before any wider rollout. Quarter-transition steps happen only on owner instruction.

## Icon migration closeout — 2026-10-11

- [SAM_OCCT#73](https://github.com/SAM-BIM/SAM_OCCT/pull/73) merged as `422f65b69732c06e5b9dc4422d5a2681fa42dd94`; owner authorized merging after green/safe validation. Feature branch preserved.
- Completed frozen SAM#179 glyph/palette/badge migration; component GUIDs, APIs, namespaces, metadata and behavior preserved. Icon resources, manifests/generation kit, token assignments and PR validation record changed; no unrelated production refactor.
- Fresh local `Release` build PASS; source guard 37 token swaps / 0 non-icon C# changes (standard SPDX headers and additive resource properties excepted); 37 resource references / 0 errors. Final-head GitHub checks passed before merge. Available regression test results are recorded in `documentation/Icon-Migration-Validation-2026-10-11.md`; hosted Rhino smoke is not claimed as a fresh pass.
- Revit-family additional builds: 2025/2027 passed where applicable. OCCT native build and native integration cases were not required for resource-only changes; skips remain documented.
- Remaining: no known blocker for this merged icon PR. SAM_Building and separate local test/environment failures elsewhere remain tracked in the organisation audit; unrelated PR9 is not merged by this task. All network operations target SAM-BIM; historical external integration/attribution text is unchanged.
- Exact next step: owner visual review of merged icons in the installed host, then continue other work from current Q4. No installer/submodule repin is included in this closeout.


## Branch

`sow/2026-Q4` - bootstrapped 2026-10-06 from `master` `25801568`. Frozen Q3 record: `sow/2026-Q3` @ `64980377` (not modified).

## Last updated

2026-10-12 (agent continuity pilot closeout, SAM_OCCT#74). Earlier: 2026-10-06 (Q4 icon-redesign migration).

## Current status

Superseded bootstrap status (2026-10-06), kept verbatim: this section through `## Next step` describes the bootstrap state. Current state is in the dated closeout sections.

Q4 branch cut from `master` `25801568`, which is the exact commit pinned in SAM_Deploy's frozen Q3 baseline (`v20261006.1`). Bootstrap added only internal docs (this file, `AGENTS.md`) and a narrow CI branch-reference update (see Decisions). No product source changed. No Q4 product work has started.

## Q4 priorities

Not yet set by the owner. Record them here at the first Q4 planning pass. Known carry-over work is listed below.

## Known carry-over work

- **SAM Grasshopper icon redesign - PR #71** (`feature/sam-gh-icon-redesign` @ `5f2f9ab6`, open, base `sow/2026-Q3`, not merged). Analysed 2026-10-06: the branch carries only its own 4 icon-only commits (`d319a93`, `2e980be`, `0ca4851`, `5f2f9ab`) on top of Q3 commit `e9b453bd`. Those commits are not reachable from `sow/2026-Q4` (Q4 is built on the promoted `master` line), so a plain retarget would list 30 commits. Replaying exactly those commits onto `sow/2026-Q4` @ `a7f04242` is conflict-free (verified commit-by-commit with `git merge-tree`; identical to the net-diff merge). Planned action: rebase-onto Q4 as a new branch + PR, then close this one; owner-approved controlled task, not yet executed. **Update:** migrated; replacement Q4 PR SAM_OCCT#73 (see the icon-redesign migration section); this old PR stays open for now.
- Branch `archive/pr61-phase2` - obsolete / owner decision: archive branch, 31 commits not in Q3 (last 2026-07-17).
- Branch `fix/input-changes` - owner decision: 1 commit not in Q3 (last 2026-08-04).

## Repository-specific next steps

- Await Q4 planning. Open PRs for Q4 work against `sow/2026-Q4`.
- Follow the continuity convention in `AGENTS.md` for every PR and closeout.

## Decisions / assumptions

- Q4 base is `master` `25801568`; the internal files were recovered from `sow/2026-Q3` into this branch only, never onto `master`.
- Q4 history intentionally does not contain the Q3 branch history (the maintained `master` is the promoted Q3 line, which is not a descendant of `sow/2026-Q3`); the frozen `sow/2026-Q3` branch is the permanent record.
- Historical Q2/Q3 content below is kept as evidence; its branch names, SHAs and next steps describe Q3 and are not current instructions.
- CI: `build.yml` dependency checkout of SAM/SAM_Solver moved from `sow/2026-Q3` to `sow/2026-Q4` (it was hard-coded; without this Q4 builds would compile against the frozen Q3 SAM).

## Validation

- Bootstrap verified 2026-10-06: `sow/2026-Q4` was created at exactly `25801568` and the push was a normal (non-forced) branch creation.

## Issues / blockers

- None at bootstrap.

## Next step

- Owner to set Q4 priorities; then start the first Q4 task from this branch.

## Q4 operational cleanup (2026-10-06)

- Reviewed every active Q2/Q3 reference in this repository on `sow/2026-Q4` (workflow branch filters, dependency-branch resolution, `.gitmodules`/validation, docs). Historical Q2/Q3 mentions (feature documentation records, the frozen Q3 section below) are intentionally unchanged.
- Changed (`a7f0424`): the dependency branch was hard-coded to `sow/2026-Q4` (a bootstrap bump from Q3); each dependency now resolves to its newest `sow/YYYY-Qn` branch, falling back to the default branch if there is none. Resolves to `sow/2026-Q4` today.
- Checked, no action: the `github.repository_owner == 'SAM-BIM'` build guard (intentional; its comment names HoareLea only to explain why the guard exists), CODEOWNERS (SAM-BIM owners), and workflow secrets (no HoareLea-named secret). The local `upstream` (HoareLea) remote is preserved.
- Carry-over: **SAM Grasshopper icon redesign - PR #71** (`feature/sam-gh-icon-redesign` @ `5f2f9ab6`, open, base `sow/2026-Q3`, not merged). Analysed 2026-10-06: the branch carries only its own 4 icon-only commits (`d319a93`, `2e980be`, `0ca4851`, `5f2f9ab`) on top of Q3 commit `e9b453bd`. Those commits are not reachable from `sow/2026-Q4` (Q4 is built on the promoted `master` line), so a plain retarget would list 30 commits. Replaying exactly those commits onto `sow/2026-Q4` @ `a7f04242` is conflict-free (verified commit-by-commit with `git merge-tree`; identical to the net-diff merge). Planned action: rebase-onto Q4 as a new branch + PR, then close this one; owner-approved controlled task, not yet executed.
- Full cross-repository record, migration table and owner decisions: `SAM_Deploy:sow/2026-Q4` `PROJECT_PROGRESS.md`.

## Q4 icon-redesign migration (2026-10-06)

- Old PR: SAM-BIM/SAM_OCCT#71 (`feature/sam-gh-icon-redesign` @ `5f2f9ab6`, base `sow/2026-Q3`) - **preserved, open, untouched**.
- New branch `feature/sam-gh-icon-redesign-q4` cut from `sow/2026-Q4` @ `01a528fa`; new PR **SAM-BIM/SAM_OCCT#73** (base `sow/2026-Q4`), feature head `f71976c3`. **Not merged.**
- Replayed (old -> new, `cherry-pick -x`; commit set taken from the GitHub PR metadata): `d319a93`->`7327f15`, `2e980be`->`e0366d5`, `0ca4851`->`439e2e2`, `5f2f9ab`->`06c3d11`; replay-only tip `06c3d11c`; plus one new docs commit `f71976c` pointing the PR record at the new PR. No Q3 history imported.
- Verified at the replay-only tip, before the record commit: result tree identical to the net-diff merge of the old feature onto Q4 (`de3bc0fa3e`); same aggregate and per-commit `git patch-id`, file set (156 files), numstat and blobs as the old PR; no workflow/`.gitmodules`/`AGENTS.md`/`PROJECT_PROGRESS.md`/solution changes. The final PR head is not tree-identical to the old feature by design (extra documentation-only commit).
- Validation: `check_source.py origin/sow/2026-Q4` OK, `check_assemblies.py` OK, local build 0 errors, relevant tests green (see the PR body); PR CI `build` success, `spdx` success; mergeable: mergeable.
- Next: owner decides whether/when to close the old PR; merge remains the maintainer's call.

---

# Historical record - 2026-Q3 (frozen)

Source: last revision of the file on `sow/2026-Q3`, commit `65235dc` (the file was removed from the Q3 tip by `bb8fe80`; `sow/2026-Q3` tip is `64980377`). Preserved verbatim except that heading levels are shifted down one. Everything below describes Q3 and is not a current instruction.

## Project Progress

### Branch
`feat/h12-occt-provenance-stamping` (base `sow/2026-Q3` @ `2afaf029`)

### Last updated
2026-09-17

### Current status
H12 release-provenance fix implemented and locally verified (managed + native). Not yet
opened as a PR pending final report review.

### Completed
- Root `Directory.Build.targets`: stamps `AssemblyFileVersion`/`AssemblyInformationalVersion`
  only, only when CI/release supplies `SAMVersion` (validated `^\d+\.\d+\.\d+\.\d+$`, fails
  clearly if malformed), only for non-test / `GenerateAssemblyInfo=false` projects. Never
  emits `AssemblyVersionAttribute`; never touches `AssemblyVersion`/`Deterministic`/
  `GenerateAssemblyInfo`. Defines `SAM_FILEVERSION_STAMPED` in the same conditioned target
  that writes the generated file (coupled by construction).
- Guarded the 5 legacy `AssemblyFileVersion("1.0.*")` lines with
  `#if !SAM_FILEVERSION_STAMPED` in: `SAM_OCCT/SAM.Analytical.OCCT`,
  `SAM_OCCT/SAM.Geometry.OCCT`, `Grasshopper/SAM.Analytical.Grasshopper.OCCT`,
  `Grasshopper/SAM.Core.Grasshopper.OCCT`, `Grasshopper/SAM.Geometry.Grasshopper.OCCT`.
  Their `AssemblyVersion("1.0.*")` lines are untouched. The other 3 in-scope assemblies
  (`SAM.Core.OCCT`, `SAM.Analytical.OCCT.Solver`, `SAM.Geometry.OCCT.Solver`) had no
  `AssemblyFileVersion` attribute at all and needed no source edit.
- `native/SAM.Occt.Native/src/version.rc.in` (new): Windows VERSIONINFO template
  (`FileVersion`, `ProductVersion`, `ProductName=SAM_OCCT`,
  `OriginalFilename=SAM.Occt.Native.dll`, `FileDescription`). `CMakeLists.txt`: reads
  `SAMVersion`/`InformationalVersion` from the environment; when `SAMVersion` is set (and
  MSVC), validates the format, `configure_file`s the template and adds it to
  `target_sources`. Absent `SAMVersion` -> resource not built at all, zero change to local
  dev builds. No native `.cpp`/`.h`/exports/ABI/linking touched.
- `.github/workflows/build.yml`: added a `Compute SAMVersion` step (same convention as
  `SAM_Deploy/installer.yml` and sibling repos), threaded `SAMVersion`/
  `InformationalVersion` into the Restore+Rebuild step and both `dotnet test` invocations
  (tests rebuild the referenced SAM.*.OCCT projects and would otherwise silently overwrite
  the CI-stamped Release DLLs with local-dev-fallback ones), and added a final
  **Assert H12 provenance** step that checks, for all 8 SAM-owned managed assemblies:
  `FileVersion == SAMVersion`, and that `AssemblyVersion` was *not* changed (3 stay
  `0.0.0.0`, 5 stay shaped `1.0.x.y` and are never equal to `SAMVersion`). Native DLL is
  not built in this CI (`SAM_OCCT_SKIP_NATIVE_BUILD=true`, unchanged) - native provenance
  is checked later in SAM_Deploy per the approved plan.

### Decisions / assumptions
- Did not import the standard SAM `Directory.Build.props` (it also drives
  `AssemblyVersion`/`Deterministic`, forbidden here) - wrote a narrower
  `Directory.Build.targets` instead, matching the plan exactly.
- `AssemblyInformationalVersion` falls back to `$(SAMVersion)+$(SAMSourceRevision)` only
  when CI didn't already supply `InformationalVersion` directly (mirrors the standard SAM
  `_GenerateSAMVersionFile` pattern).

### Files changed
- `Directory.Build.targets` (new)
- `SAM_OCCT/SAM.Analytical.OCCT/Properties/AssemblyInfo.cs`
- `SAM_OCCT/SAM.Geometry.OCCT/Properties/AssemblyInfo.cs`
- `Grasshopper/SAM.Analytical.Grasshopper.OCCT/Properties/AssemblyInfo.cs`
- `Grasshopper/SAM.Core.Grasshopper.OCCT/Properties/AssemblyInfo.cs`
- `Grasshopper/SAM.Geometry.Grasshopper.OCCT/Properties/AssemblyInfo.cs`
- `native/SAM.Occt.Native/CMakeLists.txt`
- `native/SAM.Occt.Native/src/version.rc.in` (new)
- `.github/workflows/build.yml`

### Validation (all run locally on this machine, real builds - not simulated)
- **Baseline capture**: snapshotted the pre-existing `build/` output (matches
  `sow/2026-Q3` @ `2afaf029`, the frozen SAM_OCCT pin) before any rebuild. Confirmed the
  defect exactly as scoped: 5 assemblies report `FileVersion`/`ProductVersion` as the
  literal string `"1.0.*"` (CLR does not expand the wildcard for `AssemblyFileVersion`),
  `AssemblyVersion` `1.0.9755.33908`/`...33909` (time-derived); 3 assemblies are
  `0.0.0.0`/`0.0.0.0`/`0.0.0.0` for all three fields.
- **Local dev regression check** (`msbuild SAM_OCCT.sln /t:Rebuild`, no `SAMVersion`):
  build succeeded (exit 0, no errors). All 8 assemblies unchanged in *kind* of value: 5
  still literal `"1.0.*"` FileVersion/ProductVersion with a new time-derived
  `AssemblyVersion`; 3 still `0.0.0.0` across the board. Public-API reflection dump
  (types + public/protected members via `System.Reflection`, all 8 assemblies) is
  **byte-identical** to the baseline dump except for the expected time-derived
  `AssemblyRef` version numbers on `SAM.Analytical.OCCT`/`SAM.Geometry.OCCT` - zero type,
  member, or signature differences.
- **Release stamping check** (`msbuild SAM_OCCT.sln /t:Rebuild
  /p:SAMVersion=2026.3.214.0 /p:SAMSourceRevision=testh12abc`): build succeeded. All 8
  assemblies: `FileVersion=2026.3.214.0`, `ProductVersion=2026.3.214.0+testh12abc`.
  `AssemblyVersion` unchanged in kind (3x `0.0.0.0`, 5x `1.0.x.y`, none equal to
  `SAMVersion`). Same public-API reflection diff against baseline: zero differences beyond
  the expected time-derived `AssemblyRef` numbers.
- **Native regression check** (`build-native.ps1`, no `SAMVersion`): build succeeded.
  `dumpbin /exports` and `/dependents` identical to the pre-existing baseline
  `SAM.Occt.Native.dll` (byte-for-byte tool output, ignoring the dumped file path itself).
- **Native release stamping check** (`build-native.ps1`, `SAMVersion=2026.3.214.0`,
  `InformationalVersion=2026.3.214.0+testh12abc`): build succeeded (re-linked to add the
  resource). `FileVersionInfo` on the output: `FileVersion=2026.3.214.0`,
  `ProductVersion=2026.3.214.0+testh12abc`, `ProductName=SAM_OCCT`,
  `OriginalFilename=SAM.Occt.Native.dll`. `dumpbin /exports` identical to baseline (same
  ordinal/name list). `dumpbin /dependents` identical to baseline (same DLL import list).
  Only the added `.rsrc`/VERSIONINFO differs, as expected. No `.cpp`/`.h` source touched
  (confirmed via `git diff --stat sow/2026-Q3` - only `CMakeLists.txt` and the new
  `version.rc.in`).
- **CI assertion dry-run**: extracted the new "Assert H12 provenance" PowerShell block and
  ran it standalone against the real local build tree - PASSES with
  `SAMVersion=2026.3.214.0` (as above), and correctly FAILS (throws, lists every mismatch)
  when given a deliberately wrong `SAMVersion=9999.9.999.0`.
- Full `git diff --stat sow/2026-Q3`: 6 files modified (+46/-0 total in the 5
  `AssemblyInfo.cs` + `CMakeLists.txt`), 2 new files. No application `.cs`/`.cpp`/`.h`
  source touched anywhere.

### Known non-blocking issue found during validation (OUT OF SCOPE - not fixed)
`Grasshopper/SAM.Core.Grasshopper.OCCT/SAM.Core.Grasshopper.OCCT.csproj` has a
pre-existing (not introduced by this branch - confirmed via
`git diff sow/2026-Q3 -- <path>` = empty) `OutputPath` typo for `Release|AnyCPU`:
`..\build\` (i.e. `Grasshopper\build\`) instead of `..\..\build\` (i.e. repo-root
`build\`, which is what its own `Debug|AnyCPU` config and every sibling project uses).
Effect: a Release rebuild of this one project lands in `Grasshopper\build\`, not
`SAM_OCCT\build\`, so a stale/older copy can be left sitting in `SAM_OCCT\build\`.
This fix's stamping mechanism works correctly on the project regardless (verified above by
reading its actual Release output location) - this is a separate, unrelated packaging-path
defect, not a provenance defect, and per the H12 scope lock it was **not** fixed here.
Flagged for the owner / a separate follow-up; the SAM_OCCT CI's new H12 assertion step
searches recursively so it is not fooled by this. **This may also affect what SAM_Deploy's
installer actually packages for this one DLL - worth checking when SAM_Deploy's
whole-payload audit script runs.**

### Issues / blockers
- None blocking. Native SAM_OCCT_SKIP_NATIVE_BUILD stays `true` in CI (native not built
  there); native provenance is proven locally here and will be checked again by the
  SAM_Deploy audit script against the real installer payload.
- See "Known non-blocking issue" above - not a blocker for this PR, flagged for follow-up.

### Next step
- Open PR `feat/h12-occt-provenance-stamping` -> `sow/2026-Q3` in `SAM-BIM/SAM_OCCT`.
- After merge: SAM_Deploy follow-up (bump SAM_OCCT pin, add
  `.github/scripts/audit-payload-versions.ps1`, wire it into `installer.yml`, correct
  `RELEASE_VALIDATION.md` H12 wording + run-210 erratum) per the approved plan.
