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
- One existing test changed by this work: `Spaces_TwoLevelTiltedManagedPath_RefusesWithDiagnostics` pinned the degraded managed result of `two-level-tilted` (the closure gate refusing it). With the guard that model closes (29 cells, 0 naked) and `Create.Spaces` returns 24 spaces, so the test failed (26 failures in the first full run). It is re-pointed to `AdjacencyCluster-home.sam`, which is unchanged by the guard and is still refused ("6 naked edge(s) remain"; the same on the unguarded build), as `Spaces_AdjacencyClusterHomeManagedPath_RefusesWithDiagnostics`, and `Spaces_TwoLevelTiltedManagedPath_CreatesSpacesNowThatThePostMergeKeepsEveryCell` pins the new behaviour (non-null, no `SpacesRefused`, 24 spaces). Owner to confirm this re-pointing.
- Environment lesson: `dotnet build` of the Grasshopper test project overwrote `buildSAM.Occt.Native.dll` with a different, smaller native (237 568 bytes, hash `de590ab2`). The first full-suite run and the first Softhouse chains ran on it; every result quoted above for suites and chains was rerun after restoring the frozen `438089F8` (hash checked before and after each run). Do not build the Grasshopper test project after restoring the native.

## Unresolved / risks
- At 0.21 the repair reverts 12 of 16 merge groups (coarser output: 506 panels vs 474), reported by the warning. A finer repair is possible but was not pursued.
- The raw-first post-merge (`TryRawResolve`) has the same exposure in principle and is unguarded; no failure observed. Decide on evidence from a raw-path dump before wiring.
- Cell-match tolerances are validated on this repository's fixtures only.
- The end-to-end claim rests on `Solve3D` runs, not on direct-decode substitutions; the repaired runs were not compared with a ground-truth room set (none exists for this fixture).
- Stale pins elsewhere stay stale (not repinned). Moved values seen in the same failing tests: `Solve3D_ManagedPath_TiltedLevel_UnchangedByPlaneTarget` actual 27 -> 29 cells (pin 32); `Solve3D_ManagedPath021_ClosureSignatureMatchesGoldenMaster` two-level-tilted sew-rebuild cells 23 -> 19 (pin 9).

## Next step
Owner review of the diff and this record; decision on committing and opening a draft PR stacked on #87; decision on wiring the raw path. Nothing here authorises a merge.

Generated by Michal Dengusiak and CodeClaude
