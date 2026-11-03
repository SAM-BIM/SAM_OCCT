# Post-merge guard (PR record, 2026-11-03)

Status: **draft, not ready for review; no merge is authorised.** Branch `fix/post-merge-guard-q4`, cut from `560d422` (`val/face-order-standalone-q4`, the wall-run
face-order correction, draft PR #87). Draft PR stacked on that branch (base `val/face-order-standalone-q4`); retarget to `sow/2026-Q4` when #87 lands. #87 and its record are unchanged.

**Tested content `b5fd1d6`** (code, tests, `TESTING.md`; every result below was measured on this content, then committed unchanged). The commit that adds this
text changes this record only. The hosted checks are not available for a PR whose base is not `sow/**` (`build.yml` triggers on pull requests and pushes to
`master`, `main`, `sow/**` only): they run after the retarget, and the PR description holds the final head and checks.

## Problem
With the wall-run correction, the native coplanar post-merge of the resolved faces (`ResolveStage`) re-decodes to fewer cells than MakerVolume had formed,
on `two-level-tilted` (forced managed pipeline):
- bucketBetweenLevels 0: both north rooms (S75-S80, 161.96 m3, and S63-S68, 83.92 m3) vanish (27 cells, 4 naked);
- bucketBetweenLevels 0.21: 7 of the 29 MakerVolume cells are not reproduced (27 cells). Confirmed at polygon level (two-way area difference 3e-6 m2): 120.97 + 162.23 -> 283.20 m3.
  Observed by volume only: 115.81 + 78.94 -> 194.75 m3; the other lost cells were not analysed individually.
The pre-merge faces decode to all 29 cells. The same window input merges differently on f007824, so the native merge is global and the effect is not local.
Evidence: `Documents/SAM_daily/2026-10-25-Softthouse/handover/evidence/2026-11-03-capture-f007824-vs-560d422/` (dumps, hooks, analyser, `substitution/`).

## Change
- `PostMergeGuard` (`SAM_OCCT/SAM.Geometry.OCCT.Solver/Classes/PostMergeGuard.cs`), same shape as `HealStage.SewV2`: keep the kernel's merge only if the merged faces
  still decode (direct path: SewBeforeBuild false, AvoidInternalShapes false) to every non-sliver MakerVolume cell (volume +-1e-3 m3 / 1e-4 relative, bounds +-1e-3 m,
  one-to-one, cells below `MinCellVolume` 0.05 m3 may vanish). Otherwise revert merge groups to their pre-merge faces: (1) groups with a face on the boundary of a lost cell
  (lineage: complex owners/flat ordinals -> pre-merge ordinals -> kernel history), (2) widened to the cells adjacent to the lost ones, (3) all groups. Each restored group is
  inserted at its own removed face's position; a restore is refused (and the next step tried) if a restored face also feeds a merged face that stays. The repaired history map
  replaces the kernel's in the composition. An accepted merge is returned exactly as before (no change to geometry, order or history).
- `ResolveStage`: projects the MakerVolume complex before the native result is released, passes the pre-merge list explicitly, uses `guard.Faces` and `guard.RepairedHop ?? kernel hop`.
- `DiagnosticCode.RejectedMerge` appended at the end of the enum; Warning, Resolve stage, once per solve, names the reverted groups and the lost cells (volume in m3, centre in m).
- `CellSignature`, `PostMergeGuardOutcome` (public, additive). `MergeCoplanarFace3Ds`, the native library (frozen `438089F8`) and every existing public signature are unchanged.
- Not changed: the raw-first path's own post-merge (`Panel3DSnapSolver.TryRawResolve`), `Create.Shells` pre-build merge, `MergeCoplanarPanels`.

## Decisions and assumptions
- Outcome gate, not a rule: a conformity-signature rule (merge only faces with equal owner and other-side cell) was rejected on the saved data - the base merges 6-10 such groups harmlessly
  and the rule flags only 3 of the 12 groups the failing window needs. Bisecting the fixture was declined by the owner.
- Verification uses the direct decode only; the sew-path decode is unstable (15-17 cells in the captures).
- The raw path is not wired: the default `Solve3D` on `two-level-tilted` adopts the raw solve (44 cells, 0 naked) and no raw-path loss was observed. Open, see below.
- Pins for the repaired run come from the observed run (29 cells at both buckets, equal to the 29-cell MakerVolume reference up to rounding).

## Files
`SAM_OCCT/SAM.Geometry.OCCT.Solver/Classes/PostMergeGuard.cs` (new), `Enums/PostMergeGuardOutcome.cs` (new), `Classes/ResolveStage.cs`, `Enums/DiagnosticCode.cs`,
`Testing/SAM.OCCT.UnitTests/PostMergeGuardTests.cs` (new, 27), `Testing/SAM.OCCT.IntegrationTests/PostMergeGuardIntegrationTests.cs` (new, 8 cases), `Testing/SAM.OCCT.IntegrationTests/CreateSpacesIntegrationTests.cs` (one test re-pointed, one added, see below), `TESTING.md`, this record.

## Validation (Release; frozen native `438089F8` checked before and after; APPDATA redirected to a scratch profile; tested tree = this worktree, content `b5fd1d6`)
Commands: `dotnet test Testing/SAM.OCCT.{UnitTests,IntegrationTests,GrasshopperTests}/*.csproj -c Release --no-build` after `dotnet build` of each project.
- Unit: 1133 passed, 0 failed (1106 + 27 new, rerun under the frozen native). A two-part mutation (bounds ignored; restored groups inserted in reverse) makes 4 of the 27 fail.
- Grasshopper: 38 passed.
- New integration cases written first: on 560d422 the four guard assertions failed; all 8 pass with the guard.
- Public `Solve3D`, forced managed, `two-level-tilted`: bucket 0 27 cells / 4 naked / 447 panels / 4465.7 m3 -> **29 / 0 / 487 / 4711.58**; bucket 0.21 27 / 0 / 474 / 4711.58 -> **29 / 0 / 506 / 4711.58**.
  Warning text: bucket 0 "7 of 23 coplanar merge group(s) were reverted ... lost 2 cell(s): 83.92 m3 at (38.62, -2.33, 8.17) m; 161.96 m3 at (33.84, -3.38, 7.08) m"; bucket 0.21 "12 of 16 ... lost 7 cell(s)".
  Two guarded runs are identical. Default (non-forced) `Solve3D`: raw-first adopted, 44 cells, unchanged.
- tilted-two-spaces, whole-level-tilted, whole-level-flat, three-spaces: every CELL/SUMMARY row (cells, panels, area, volume, naked) identical before and after, both buckets, both pipelines; no `RejectedMerge`.
- Softhouse F/R smoke (`softhouse.sh`, this build): F 72 spaces, 48 of 70 good; R 72 spaces, 45 of 70 good; `good.tsv` byte-identical and `panels.json` / `panels_clean.json` / `panels_extended.json` equal (GUIDs masked) to the saved 560d422-equivalent chains. (That chain does not pass through `ResolveStage`'s merge.)
- Cost: one verification decode per merged solve plus up to three on a repair. Wall clock, forced managed, noisy (+-15%): two-level-tilted 4.7 -> 5.5 s (bucket 0), 4.1 -> 5.3-6.0 s (0.21); whole-level-tilted/flat +0.1-0.3 s; small fixtures unchanged.
- Full integration suite (frozen native): 25 failed, 478 passed, 2 skipped (505). The failing set is identical, test for test, to the saved 25-test set of the 560d422-equivalent build (stale pins, not repinned). Unit 1133 and Grasshopper 38 were rerun under the same native.
- One existing test changed by this work: `Spaces_TwoLevelTiltedManagedPath_RefusesWithDiagnostics` pinned the degraded managed result of `two-level-tilted` (the closure gate refusing it). With the guard that model closes (29 cells, 0 naked) and `Create.Spaces` returns spaces (24 on the tested content), so the test failed (26 failures in the first full run). It is re-pointed to `AdjacencyCluster-home.sam`, which is unchanged by the guard and is still refused ("6 naked edge(s) remain"; the same on the unguarded build), as `Spaces_AdjacencyClusterHomeManagedPath_RefusesWithDiagnostics`, and `Spaces_TwoLevelTiltedManagedPath_CreatesSpacesNowThatThePostMergeKeepsEveryCell` pins the new behaviour (non-null, no `SpacesRefused`). Re-pointing confirmed by the owner 2026-11-03; the first version also pinned the count 24, which the owner had removed (see the classifier finding).
- **Classifier finding (separate follow-up, not fixed here):** `CellClassifier` (Create.Spaces) marks real rooms Exterior on the guarded two-level-tilted result: 84.9, 161.96, 84, 120.97 and 162.23 m3, all inside the footprint and all in the MakerVolume cell set. The Exterior count depends on the face set it is given: 1 on f007824, 9 on the 560d422 tree, 5 with the guard. The cause is the classifier's own sew-path envelope decode (unstable, 15-17 cells in the captures), not the guard; the closure gate used to hide it by refusing the result. Consequence: the space count of two-level-tilted is not trustworthy and is not pinned. Probe: scratch `ZzRolesProbe` (not committed), which classifies the `Solve3D` cells exactly as `Create.Spaces` does.
- Environment lesson: `dotnet build` of the Grasshopper test project overwrote `buildSAM.Occt.Native.dll` with a different, smaller native (237 568 bytes, hash `de590ab2`). The first full-suite run and the first Softhouse chains ran on it; every result quoted above for suites and chains was rerun after restoring the frozen `438089F8` (hash checked before and after each run). Do not build the Grasshopper test project after restoring the native.

## Revalidation after dropping the 24-space pin (2026-11-03)
Tested content `10830fc` (`b5fd1d6` plus the one-line test change; the commit after it changes this record only). Frozen native `438089F8` checked before and after every run; APPDATA redirected.
- Unit `PostMergeGuard*`: 27 / 27. Focused integration (`PostMergeGuardIntegrationTests` + `CreateSpacesIntegrationTests`): 13 run, 12 passed, 1 failed (`Spaces_TwoLevelTiltedRawPath_ProducesSpacesForInteriorCells`, expected 43 / actual 44, a stale pin that is in the saved 25).
- Both buckets through public `Solve3D`: `Solve3D_TwoLevelTiltedBucket0_KeepsBothNorthRooms`, `Solve3D_TwoLevelTiltedBucket021_KeepsTheTwoSouthRoomsSeparate` and both `RejectedMerge` diagnostic cases passed; the four control fixtures passed.
- Full integration on `10830fc`: 25 failed / 478 passed / 2 skipped (505), the failing set identical, test for test, to the saved 25 of `b5fd1d6`.
- The 25 by group (identical set before and after the guard; no repin made; owner decides repin vs retire per group, after #87):
  - `GoldenMasterIntegrationTests` 8: cell, face and naked pins from older pipelines (e.g. two-level-tilted 9 -> 19 cells, whole-level-flat faces 219 -> 236, towers 25 -> 27).
  - `NineSpaceWallCaptureIntegrationTests` 6: rigid-motion invariance (capture 0.45 / 0.55 / 0.65 x translate / rotate Z 45).
  - `Extend3DPlaneTargetIntegrationTests` 4: plane-target pins (e.g. tilted level 32 -> 29 cells, Revit-home 14 -> 22).
  - `WorkflowParityIntegrationTests` 4 fixtures; `Extend3DRegressionIntegrationTests` 1 (PR49-Test2 cells 43 -> 42); `PR61Face3DHomeParityTests` 1 (19 -> 20); `CreateSpacesIntegrationTests` 1 (tilted raw path 43 -> 44).

## Guard review revision (2026-11-03)

Revised `PostMergeGuard.cs` and `PostMergeGuardTests.cs` on top of `21d8d4e`. When verification returns no decode,
the guard restores the original face list and identity history, then emits a `RejectedMerge` Warning describing the
failed verification. If an input contributes to multiple reverted groups, it is restored once at the first group's
position and the repaired history points to that copy. The existing refusal when an input also feeds a group that
stays remains in effect. Both cases are covered by managed regression tests.

Release validation of this revision used the existing SAM and SAM_Solver build dependencies, a scratch APPDATA
profile and the frozen native DLL (`438089F8`, unchanged before and after the focused native tests).

- Full unit suite passed 1,134 / 1,134, including both reviewed guard paths.
- Focused integration passed 10 / 10 with zero skips. It covered both buckets through public `Solve3D`, the guard
  diagnostics and control fixtures, plus the tilted Spaces acceptance and AdjacencyCluster-home refusal.
- The full integration failure inventory above comes from `10830fc`. Full native comparison and hosted checks
  remain gates for the combined candidate.

Run `dotnet test <project> -c Release --no-restore --disable-build-servers -m:1` for the unit project and the integration
project. For integration add `--filter 'FullyQualifiedName~PostMergeGuardIntegrationTests|FullyQualifiedName~Spaces_TwoLevelTiltedManagedPath_CreatesSpaces|FullyQualifiedName~Spaces_AdjacencyClusterHomeManagedPath_RefusesWithDiagnostics'`.
Machine-local logs and TRX files are in `build/pr88-guard-review-evidence/`.

## Second review revision: failed builds and missing history (2026-11-03)

Two paths still lived outside the first revision's null-decode handling:
- `DecodeSignatures` ignored the return of `GeometryCreate.Shells`. A kernel build failure returns null shells while `NativeAvailable` stays true, so
  the guard saw "zero cells"; with no protected cell (every initial cell below `MinCellVolume`) it accepted the unverified merge. `SignaturesOf` now returns
  null (unverified) when the shells are null, the result is null or the library is unavailable, which takes the restore-and-warn path.
- `ResolveStage` composed the guard's repaired hop and then read `postMergeHistory.MaxTolerance`, a null dereference when the native merge captured no
  history. The composition is now `ResolveStage.TryComposePostMerge`: repaired hop, else the kernel history, else unusable; the merge tolerance is
  carried only when the history exists.

Tests added (unit, managed): `SignaturesOf` for a failed build, an unavailable library and no result; `Evaluate` with an unverified decode and nothing to
protect restores the pre-merge faces with one `RejectedMerge` warning; `TryComposePostMerge` for a repaired hop without kernel history, no hop and no history,
and kernel history carrying its tolerance.
Validation (frozen native `438089F8`, checked before and after; APPDATA redirected): unit 1140 / 1140 on the second full run (the first run had one failure,
`Panel3DSnapSolverTests.Extend_LevelCeilingVsPitchedRoof_RoutesScalarVsPlaneOps`, which reads the static `SnappedPanel.PlaneOpsExtendCount`; it passes alone, twice,
and in the rerun, so it looks order/parallelism dependent and is unrelated); focused integration 10 / 10 (both buckets through `Solve3D`, diagnostics, controls,
tilted Spaces, AdjacencyCluster-home refusal). Full native integration on `efdba56` (frozen native `438089F8` before and after): 25 failed / 478 passed / 2 skipped (505), the failing set identical, test for test, to the saved 25. PR-attached checks on the final head are still to be run.

## Owner decision on Softhouse D203 (2026-11-03)

Accepted as a known limitation for the combined #87/#88 change. The as-stored D203 result remains closed at 43.68 m³.
With reversed faces the Q4 base closed a different shape at 42.22 m³ through compensating merges, while #87 leaves D203 open.
After merge, follow up separately on the remaining face-order dependence in Clean3D. Verify both presentations against the
source wall and slab boundaries, using the as-stored 43.68 m³ result as the current reference.

## Landing decision for #87 and #88 together (owner, 2026-11-03)

Approved: land #87 and #88 together through #88, retargeted from `val/face-order-standalone-q4` to `sow/2026-Q4` (tip `f007824`, which is the merge base,
so the combined diff is exactly 19 commits, 15 files, +2790 / -30: #87's 6 files plus #88's 9, disjoint). #87 stays open and draft until #88 merges, then is
closed or marked merged because its head is an ancestor. Merge authority is separate and follows green checks on the final head.

Tested content: `efdba56` (code and tests; later commits are record-only). Full native integration, frozen native `438089F8` before and after, SAM `7c4591cd`,
SAM_Solver `fd63b467`: Q4 `f007824` 25 failed / 469 passed / 2 not executed (496) versus `efdba56` 25 failed / 478 passed / 2 not executed (505); the 25 failing names
and the 2 not-executed names are identical. Saved comparison (machine-local): `build/pr88-native-efdba56-20261103-172554/q4-comparison.txt`. Hosted checks cannot run the native
tests (they skip); the hosted result is build, unit and SPDX only.

Owner disposition of the 25 (no repin, no golden or baseline change, nine-space invariance assertions untouched and failing identically to Q4):
- 22 inherited unchanged from Q4: GoldenMaster 6, NineSpaceWallCapture rigid motion 6, Extend3DPlaneTarget 4, WorkflowParity 4, Extend3DRegression (PR49-Test2) 1, CreateSpaces tilted raw path 1.
- 3 moved measurements, accepted: managed golden two-level-tilted (Q4 13 cells; pin 15 cells / 32 naked; now 15 cells, 0 naked); 021 golden two-level-tilted (pin 9; Q4 16, now 19);
  PR61 Face3D-home parity (pin 19; Q4 15, now 20, the move comes from #87).
- The combined diff also removes the WorkflowParity spaces-count-match assertion (#87, recorded in its record).
- Softhouse D203 stays accepted and deferred; #84 and the `CellClassifier` follow-up are out of scope.

Checks: retarget, then this record commit (no skip marker) so Build (Windows) and the SPDX check run as pull_request checks on the final head. The PR description names the final head and checks.

## Unresolved / risks
- At 0.21 the repair reverts 12 of 16 merge groups (coarser output: 506 panels vs 474), reported by the warning. A finer repair is possible but was not pursued.
- The raw-first post-merge (`TryRawResolve`) has the same exposure in principle and is unguarded; no failure observed. Decide on evidence from a raw-path dump before wiring.
- Cell-match tolerances are validated on this repository's fixtures only.
- The end-to-end claim rests on `Solve3D` runs, not on direct-decode substitutions; the repaired runs were not compared with a ground-truth room set (none exists for this fixture).
- Stale pins elsewhere stay stale (not repinned). Moved values seen in the same failing tests: `Solve3D_ManagedPath_TiltedLevel_UnchangedByPlaneTarget` actual 27 -> 29 cells (pin 32); `Solve3D_ManagedPath021_ClosureSignatureMatchesGoldenMaster` two-level-tilted sew-rebuild cells 23 -> 19 (pin 9).

## Next step
Read the PR-attached Build (Windows) and SPDX results on the final head; then the owner decides the merge (`--merge --match-head-commit`, no auto-merge). After
merge: confirm #87 is closed or merged, clean up both task branches under AGENTS.md, add the `PROJECT_PROGRESS.md` closeout (docs-only, `[skip ci]`, merge SHA) on
`sow/2026-Q4`, then re-measure #84 against the merged base.

Generated by Michal Dengusiak and CodeClaude
