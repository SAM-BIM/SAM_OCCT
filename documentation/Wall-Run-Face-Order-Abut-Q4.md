# Wall run and height independent of face vertex order in `AbutsColinearWithin` - Q4 (2026-11-02)

Status: draft PR into `sow/2026-Q4` (base `f007824`), branch `val/face-order-standalone-q4`. **Not ready for review; no merge is
authorised.** Standalone: it contains no #84 code and no kink rule. PR #84 is untouched and stays draft.

**Tested content `6ae40c8`** (implementation `f08c442`, test correction `6ae40c8`). Later commits change one comment in
`WorkflowParityIntegrationTests.cs` and add this record only; they were compiled but not re-run (see "Validation"). The PR
description holds the final head and the hosted checks.

## Problem

`SnappedPanel.AbutsColinearWithin` read a wall's run as the plane's `AxisX` and its height as `AxisY`. The plane's axes follow the
face's vertex order, so on about half the walls the run and the height were swapped and the same pair of pieces got opposite
answers depending on how the faces were written.

## Change

- `SAM_OCCT/SAM.Geometry.OCCT.Solver/Classes/SnappedPanel.cs`: new private `RunHeightBounds` - run = `(p - plane.Origin)` along
  `RunAxis(normal)` (the horizontal unit `(-Ny, Nx, 0)` of the normal), height = world Z (the vertical-wall reading `IsVertical`
  already assumes). A plane with no horizontal run (a cap) falls back to `FootprintBounds` (unchanged). `AbutsColinearWithin` reads
  through it. Thresholds and tolerances are unchanged; `OverlapsInPlane` is not touched.
- `Testing/SAM.OCCT.UnitTests/WallRunFrameTests.cs` (new): each wall through 16 presentations (normal either way, run/height first,
  every loop start, both windings); every presentation must give one answer. 15 cases.
- `Testing/SAM.OCCT.IntegrationTests/WorkflowParityIntegrationTests.cs`: Face3D-home row, see "Face3D-home parity".

Port note: `e7a4b57` (the #84-side form of this change) shares `RunAxis` with `Panel3DSnapSolver.WallPlanFrame`, which exists only in
#84. Here `RunAxis` is a private helper of `SnappedPanel` (same computation); `Panel3DSnapSolver.cs` is not touched. When #84 lands the
two copies need reconciling.

## Validation

Two isolated Release trees, identical build and configuration, differing only in the three files above: base `f007824` (`fa0`) and
`6ae40c8` (`fa1`). Frozen native `438089F8` in both (the solution build had rebuilt it per tree; the frozen DLL was restored before
any native run), SAM build `7c4591cd`, `APPDATA` redirected, one native process at a time.

| suite | base `f007824` | `6ae40c8` |
| --- | --- | --- |
| unit | 1055 / 1055 | 1070 / 1070 (+15 `WallRunFrameTests`) |
| native integration | 496 tests: 25 failed, 469 passed, 2 skipped | 496 tests: 25 failed, 469 passed, 2 skipped |
| Grasshopper | 38 / 38 | 38 / 38 |

**Native, per test:** zero outcome differences. The same 25 tests fail on both. Of those, 21 have identical failure messages and four
fail with a different measured value (below).

### Moved measurements (all four already failing on the base; not explained)

| test | base | `6ae40c8` | pinned |
| --- | --- | --- | --- |
| `GoldenMaster ... ManagedPath ... two-level-tilted` cells | 13 | 19 | 15 |
| `GoldenMaster ... ManagedPath021 ... two-level-tilted` cells | 16 | 20 | 9 |
| `Extend3DPlaneTarget ... TiltedLevel_UnchangedByPlaneTarget` naked | 29 | 23 | 32 |
| `PR61Face3DHomeParityTests ... RecordsCurrentState` A-solver-matched | 15 | 20 | 19 |

Direction is mixed. More walls now align, so the managed output genuinely changes, and the sew-rebuild measurements these pins use are
presentation dependent; the two effects have not been separated. **These four are not traced on this base and no pin was changed.**
The plane-target `AdjacencyCluster-home` and `Face3D-home` rows, which moved in the #84-side comparison, did not move here: their
failure messages are identical (that movement came with the kink rule, which is not in this branch).

### Softhouse F and R presentations

Chain: PanelsBySolids -> SnapToJunctions -> Clean3D -> Extend3D -> AdjacencyCluster -> Rooms2 -> good classification, on the stored
extracted panels, as stored (F) and with every face re-presented reversed (R, `REVFACES=1`). "Frozen 48" is the reference set of 48
good rooms.

| presentation | base: spaces / good / frozen 48 kept | `6ae40c8`: spaces / good / frozen 48 kept |
| --- | --- | --- |
| F | 71 / 48 / **48 of 48** | 72 / 48 / **48 of 48** |
| R | 74 / 39 / **39 of 48** | 72 / 45 / **45 of 48** |

- **F:** the per-room good/not-good status is identical on the base and the branch (no room changes); the space count is 71 -> 72.
- **R:** seven rooms recovered - Bath 4 A202, Bath 8 2OG A302, Bed 12 2OG A303, Bed 7 2OG D303, Carpark 4 A109, Circulation 8 A301,
  Kitchen 5 A204 - and one lost: **Bed 2 1OG D203**. Net 39 -> 45.
- Rooms good on F but not on R: 9 on the base, 3 on the branch (Carpark 2 C109, Bed 2 1OG D203, Bed 4 1OG C203).

**Bed 2 D203** is not re-investigated here. Evidence already on record: the #84-side face-order record
(`documentation/Wall-Run-Face-Order-Q4.md` on local branch `fix/wall-run-face-order-q4`, `c829dea`, not pushed; section "Softhouse room
by room") found D203 good on R only through the order fault, and `evidence/2026-10-27-combined/rooms/good48-table.md` (machine-local)
lists it as lost in the #84 comparison. Whether that classification carries over to this base is **not established**; here it is
observed as a loss in R.

### Face3D-home parity

On the base, `WorkflowParity ... Face3D-home.sam` asserted that the sewing rebuild ("A-solver-matched") does **not** match the solver's
20 cells (`SpacesMatchResolvedCellCount = false`). That rebuild's space count depends on how the solved faces are presented. Measured
on this base: the rebuild gives 15 spaces on `f007824` (the comment's "19 vs 20" was already stale) and 20 on `6ae40c8`, with the
solver itself at 20 cells, 0 naked points in both. With the correction the old assertion would fail by construction (20 == 20), so
this test-only change makes that row diagnostic (`[diagnostic] ... sew-rebuild spaces=N, solver cells=M`) and asserts the native
solver's own closure instead (20 cells, 0 naked points; `SolverInvariants`). `ParityClean` and the relation-invariance assertions on
that row stay; `B-clean-extend` (19) and every other row are unchanged. Not asserted: the cell volume (601.3 m3), because it comes
from the same solver and there is no independent value.

The "11 to 20 spaces by face order and loop start" range quoted in the test comment is from the #84-side trace
(`Wall-Run-Face-Order-Q4.md`, "Face3D-home trace"); it was not re-measured on this base.

## Not achieved, not established

- **F/R full invariance is not achieved.** Three rooms are good on F but not on R.
- **Frozen-48 acceptance is not achieved** on R (45 of 48); F keeps 48 of 48.
- The four moved native measurements above are unexplained.
- The 65.2 m2 Roof/Floor reclassification between solved sets was observed in the earlier #84-side comparison. Whether it applies to
  this standalone branch is **not established**; it was not measured here.
- Panel-order dependence is not covered; this correction is vertex order only.
- Hosted CI and a Grasshopper canvas run were not part of this validation; `PROJECT_PROGRESS.md` is post-merge only.

## Reproduce

```
git archive <commit> | tar -x -C %TEMP%\r84\SAM_OCCT_<tree>     # fa0 = f007824, fa1 = 6ae40c8
# build the solution in Release; restore the frozen SAM.Occt.Native.dll (438089F8) into <tree>\build
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Release --no-build
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Release --no-build
dotnet test Testing/SAM.OCCT.GrasshopperTests/SAM.OCCT.GrasshopperTests.csproj -c Release --no-build
```

The F/R chain is the existing `softhouse.sh` harness (`tools-2026-10-27-combined/scripts`), run per tree.

## Evidence (machine-local; copy before switching machines)

`Documents/SAM_daily/2026-10-25-Softthouse/handover/evidence/2026-11-02-faceorder-standalone/`: `suites/` (trx and logs, unit and native
for both trees, Grasshopper logs), `native-per-test.txt` (outcome and moved-value comparison), `chains/{fa0,fa1}-{F,R}` (stage outputs,
`good.tsv`). Built trees: `%TEMP%\r84\SAM_OCCT_fa0` and `SAM_OCCT_fa1`.

## Next step

Owner review of this draft. Order of integration: this PR on `sow/2026-Q4`, then the kink-dependent correction separately. Nothing here
authorises either a merge or the kink integration.

Generated by Michal Dengusiak and CodeClaude
