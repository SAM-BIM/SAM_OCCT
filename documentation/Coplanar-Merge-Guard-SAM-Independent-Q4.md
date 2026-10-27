# Coplanar-merge guard tests independent of SAM's hole-loop defect — Q4 (2026-10-27)

Status: draft PR into `sow/2026-Q4` (base `1e39868`), branch `test/coplanar-merge-guard-sam-independent-q4`.
**Tested content `3730224`**; later commits change this record only, and the PR description holds the final head and
hosted checks. Companion of the SAM correction `fix/face2d-degenerate-hole-loop-q4` (SAM record
`documentation/Face2D-Degenerate-Hole-Loop-Q4.md`, tested content `b852b687`). **This PR merges first.** Not merged; no
merge is authorised.

## Why

SAM_OCCT#85 (`daa6f183`) added the Clean3D coplanar-merge coverage guard (`SnapStage.RecoverLostPanels`,
`CoplanarMergeRecovered` Warning). Three of its tests reached the Warning only through SAM's defect (the seed-400 slab's
merged face invalidated by a 2-point hole loop):

- `Clean_CoplanarMergeLeavesDegenerateHoleLoop_KeepsMergedSlabAndWarns` (2 rows);
- `Clean3D_CoplanarMergeRecovered_WarningSurvivesEveryDetailLevel`.

With the SAM correction the union keeps the slab, no Warning is emitted, and these rows fail although the guard is
unchanged (measured: unchanged `sow/2026-Q4` unit suite on corrected SAM.Geometry 1049 / 1052, each failing only at the
Warning assertion). SAM_OCCT CI builds against the newest SAM `sow/*`, so this companion must merge before the SAM fix.

## Change

- `SAM_OCCT/SAM.Geometry.OCCT.Solver/Classes/SnapStage.cs`: the public `Clean` now calls a new **internal** overload
  that takes the step-3 coplanar union (`Func<List<Face3D>, double, List<Face3D>>`); null (what the public overload
  passes) uses `Geometry.Spatial.Query.Union` exactly as before. A test seam only: no static state, no public API or
  behaviour change (the same call with the same arguments).
- `Testing/SAM.OCCT.UnitTests/SnapStageTests.cs`:
  - `Clean_Seed400DominantPieceFirst_KeepsMergedSlabWithSources` (2 rows, renamed from the first #85 test): seed-400
    geometry, coverage of every input piece and the source map both ways; no Warning assertion, because whether the
    union or the guard keeps the slab depends on the SAM version.
  - `Clean_CoplanarMergeLosesAGroup_RecoversItWithSourcesAndWarns` (2 rows, new): a deterministic loss independent of
    SAM's conversion (the real union with the merged slab removed, through the internal seam) on the seed-400
    orientation every SAM version merges. Pins recovery, attribution and one Warning (`Snap`, `6 panel face(s)`,
    `0 degenerate loop(s) removed`).
  - `Clean3D_Seed400DominantPieceFirst_KeepsMergedSlab` (public Clean3D path, outcome only: 2 panels, 126 m2, repeatable).
  - `Clean_CoplanarMergeRecoveredWarning_SurvivesEveryClean3DDetailLevel`: the Warning Clean emits for that loss,
    through Clean3D's own `SolverReportFormat.FormatDiagnostics`/`FormatDiagnosticRecords` and every level's
    `FilterDiagnosticRecords`: one line, the same text, Warning severity at Summary, Standard and Detailed.
  - The other #85 tests (`RecoverLostPanels_*`, injected union already) are unchanged.

Coverage choice: the Clean3D-specific wiring (`Panel3DSnapSolver` passing its diagnostics to `SnapStage.Clean`, then
`Clean3D` formatting them) is no longer exercised with this particular Warning; it is generic and unchanged. Keeping
it would need an internal Clean3D overload plus `InternalsVisibleTo("SAM.Analytical.OCCT.Solver")` on
`SAM.Geometry.OCCT.Solver`. Not done (owner may ask for it).

## Validation (local, machine `michal`, 2026-10-27, tested content `3730224`)

Isolated managed-only builds (`SAM_OCCT_SKIP_NATIVE_BUILD=true`, APPDATA/USERPROFILE redirected) under
`C:\Users\michal\AppData\Local\Temp\sroot\occt` (machine-local): `SAM/build` = frozen SAM `91f25290` product build
(SAM.Geometry `A20CB3E8`), frozen native `SAM.Occt.Native.dll` `438089F8` (the Q4 / #83 native baseline). `*fix` trees are
the same with only SAM.Geometry.dll replaced by the corrected one (`188DA1F6`, built from `b852b687`). Evidence:
`SAM_daily\2026-10-25-Softthouse\handover\evidence\2026-10-27-sam-root-face2d-loop\` (`occt/`, `occt-mutation/`;
commands in its `README.md`; machine-local, copy before switching machines).

| Suite | SAM 91f25290 | corrected SAM.Geometry |
| --- | --- | --- |
| Unit (`comp`) | **1055 / 1055** | **1055 / 1055** |
| Unit, unchanged `sow/2026-Q4` (`q4`) | 1052 / 1052 | 1049 / 1052 (the three #85 rows, at the Warning assertion only) |
| Integration (native) | 469 / 25 failed / 2 skipped | 469 / 25 / 2: all 496 outcomes and messages identical (TRX) |
| Grasshopper | **38 / 38** | **38 / 38** |

- Mutations (`occt-mutation/`, current SAM): guard call removed (G1) → 6 fail (both injected-loss rows, the
  detail-level row, and both seed-400 Clean rows and the Clean3D row, since the current SAM still loses that slab);
  Warning downgraded to Info (G2) → 6 fail (both injected-loss rows, the detail-level row and three existing
  `RecoverLostPanels` Warning rows).
- Native suite baseline: the Q4 / #83 baseline is 469 passed / 25 failed / 2 skipped on native `438089F8`; #84 has its
  own (472 / 24 / 2 at `26ddbaf`), not compared here.
- Softhouse chains on #84 `26ddbaf` (no kink) with the current SAM need 1 recovery Warning on R and none with the
  corrected SAM; see the SAM record.

## Decisions

- No production behaviour change and no Grasshopper version change (test seam only).
- #85's guard stays: defence against any other merge loss, and its diagnostic.

## Unresolved

- The Clean3D wiring coverage noted above.
- SAM_OCCT#84 must take this change when it next merges `sow/2026-Q4` (it carries the #85 tests at `26ddbaf`).

## Next step

Owner review and hosted checks; merge this PR first (owner approval), then the SAM PR. Recommended: Claude Opus 5.5,
medium effort, this session, Build mode.
