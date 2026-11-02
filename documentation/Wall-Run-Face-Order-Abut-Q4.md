# Wall run and height independent of face vertex order in `AbutsColinearWithin` - Q4 (2026-11-02)

Status: draft PR into `sow/2026-Q4` (base `f007824`), branch `val/face-order-standalone-q4`. **Not ready for review; no merge is
authorised.** Standalone: it contains no #84 code and no kink rule. PR #84 is untouched and stays draft.

**Tested content `a3b092b`.** The production change is `f08c442` and has not changed since; revision 2 (this record's
"Revision 2") changed tests only. The commit that adds this text changes this record only. The PR description holds the final head
and the hosted checks.

## Problem

`SnappedPanel.AbutsColinearWithin` read a wall's run as the plane's `AxisX` and its height as `AxisY`. The plane's axes follow the
face's vertex order, so on about half the walls the run and the height were swapped and the same pair of pieces got opposite
answers depending on how the faces were written.

## Change

- `SAM_OCCT/SAM.Geometry.OCCT.Solver/Classes/SnappedPanel.cs`: new private `RunHeightBounds` - run = `(p - plane.Origin)` along
  `RunAxis(normal)` (the horizontal unit `(-Ny, Nx, 0)` of the normal), height = world Z (the vertical-wall reading `IsVertical`
  already assumes). A plane with no horizontal run (a cap, normal horizontal part <= 1e-9) falls back to `FootprintBounds`
  (unchanged). `AbutsColinearWithin` reads through it. Thresholds and tolerances are unchanged; `OverlapsInPlane` and the two
  `InPlaneOverlapRatio*` methods are not touched (see "Remaining face-order dependence").
- `Testing/SAM.OCCT.UnitTests/WallRunFrameTests.cs` (new): 35 tests, each wall through the full presentation matrix (below).
- `Testing/SAM.OCCT.IntegrationTests/WorkflowParityIntegrationTests.cs`: Face3D-home row, see "Face3D-home parity".

Port note: `e7a4b57` (the #84-side form of this change) shares `RunAxis` with `Panel3DSnapSolver.WallPlanFrame`, which exists only in
#84. Here `RunAxis` is a private helper of `SnappedPanel` (same computation); `Panel3DSnapSolver.cs` is not touched. When #84 lands the
two copies need reconciling.

## Revision 2 (test review, 2026-11-02): what changed and what it showed

Review findings on the first published head (`4efd790`) and how each was closed. No production code changed.

1. **A missing naked-edge measurement could satisfy the closure invariant.** The solver publishes an empty naked list when its own
   `Validate` produced no report (`Panel3DSnapSolver.cs`, the single outward validate ignores the return value), and the test read
   `null` as 0. `AssertSolverInvariants` now requires `NativeResolved`, a `Signature`, a non-null list and the expected 20 cells / 0
   naked, and **re-validates the solved faces** with a call whose return value, `NativeAvailable` and report are checked, in the order
   and with the options the solver used. This is not an order-independent invariant: the same 152 solved faces give **0, 17, 6 and 33**
   naked edges for as-given / list reversed / loops +1 / both. It proves the measurement was made and agrees with the solver's report;
   it is not claimed as geometric equivalence.
2. **A diagnostic row could pass on a failed rebuild.** With the space count diagnostic, a null cluster, no spaces or a missing parity
   diagnostic skipped the remaining checks. The diagnostic branch now requires a cluster, a merged cluster, a cell-complex result, at
   least one space and the analytical parity diagnostic, so the relation checks always run. Only the diagnostic row is stricter; no
   other row changed.
3. **The presentation matrix was sampled, not enumerated.** `Presentations` now enumerates 8 plane-axis assignments x 3 plane origins
   (first vertex, centroid, a point on the plane outside the face) x 2 windings x every loop start = **192 per wall** (was 16, with loop
   start tied to frame and winding and a fixed origin). A guard test asserts the counts and that each dimension actually varies. New
   cases: walls at 5 bearings; leaning walls (0.25, 1, 3 degrees); offset, run gap and height overlap exactly at the limits (axis
   aligned, exact binary values: 0.25 offset/gap inclusive, 0.125 height overlap exclusive) and a nanometre either side at two
   bearings; a near-horizontal plane just above the cap threshold (one answer); degenerate loops (they create no face and a panel cannot
   be built without one, so they never reach the wall reading). 35 tests (was 15).
   Mutation checks on the production code (scratch tree only): restoring the plane-frame reading fails **20 of 34** (the degenerate-loop test was added afterwards); making the run gap
   exclusive fails exactly the 0.25 gap case; making the height test inclusive fails exactly the 0.125 case.
   Not covered, by design: the cap fallback (`|horizontal part| <= 1e-9`) keeps the plane-frame reading and is **not** presentation
   independent; production calls the method for vertical walls only. Pairs that are not exactly parallel read the other plane's origin
   in the offset test; the matrix varies origins only for parallel pairs.

## Validation

Two isolated Release trees, identical build and configuration, differing only in the files above: base `f007824` (`fa0`) and the
branch (`fa1`). Frozen native `438089F8` in both (the solution build had rebuilt it per tree; the frozen DLL was restored before any
native run), SAM build `7c4591cd`, `APPDATA` redirected, one native process at a time.

| suite | base `f007824` | `6ae40c8` (first head) | `a3b092b` (revision 2) |
| --- | --- | --- | --- |
| unit | 1055 / 1055 | 1070 / 1070 | 1090 / 1090 (+35 `WallRunFrameTests`) |
| native integration | 496: 25 failed, 469 passed, 2 skipped | same | same: 25 failed, 469 passed, 2 skipped |
| Grasshopper | 38 / 38 | 38 / 38 | not re-run (production and Grasshopper code unchanged since `6ae40c8`) |

**Native, per test:** zero outcome differences against the base, at both heads. The same 25 tests fail. Of those, 21 have identical
failure messages and four fail with a different measured value (below). With revision 2's stricter Face3D-home assertions the
`WorkflowParity ... Face3D-home.sam` test passes on the branch **and on the base** (run with the revised test file in both trees).

### Moved measurements (all four already failing on the base; no pin changed)

What each one measures matters; the first three are rebuilds of the solver output, the fourth is the solver's own cell count:

| test | measure | base | branch | pinned |
| --- | --- | --- | --- | --- |
| `GoldenMaster ... ManagedPath ... two-level-tilted` | cells of a sew-0.01 **rebuild** of the solved faces | 13 | 19 | 15 |
| `GoldenMaster ... ManagedPath021 ... two-level-tilted` | same, bucket 0.21 | 16 | 20 | 9 |
| `PR61Face3DHomeParityTests ... RecordsCurrentState` | A-solver-matched sew **rebuild** spaces | 15 | 20 | 19 |
| `Extend3DPlaneTarget ... TiltedLevel_UnchangedByPlaneTarget` | the **solver's own** `ResolvedCellCount` (forced managed) | 29 | 23 | 32 |

(An earlier version of this record labelled the last row "naked 29 -> 23"; it is a cell count.) The plane-target `AdjacencyCluster-home`
and `Face3D-home` rows, which moved in the #84-side comparison, did not move here (identical failure messages): that movement came with the
kink rule, which is not in this branch.

### Trace of the moved measurements (bounded; no code change)

Probe (scratch, not in the repository): each fixture solved from its stored panels ("as-is") and from the same panels re-presented
(loops rotated, reversed), then the sew-0.01 rebuild of the solved faces as given / list reversed / loops +1. The probe reproduces the
four test values exactly (13 -> 19, 16 -> 20, 15 -> 20, 29 -> 23). Base -> branch, as-is input:

| fixture / config | solver cells | solver naked | solved panels / area | sew-rebuild cells (as given / list reversed / loops +1) |
| --- | --- | --- | --- | --- |
| two-level-tilted, managed 0 | 29 -> 23 | 8 -> 14 | 401 / 5118 -> 333 / 4777 | 13/19/12 -> 19/22/19 |
| two-level-tilted, managed 0.21 | 33 -> 23 | 3 -> 0 | 408 / 5147 -> 317 / 4777 | 16/15/12 -> 20/22/21 |
| Face3D-home, default | 20 -> 20 | 0 -> 0 | 160 -> 152 / 1096.2 both | 15/20/15 -> 20/15/14 |

- **The three rebuild measures (13 -> 19, 16 -> 20, 15 -> 20) lie inside what presentation alone produces.** The same solved faces give
  12 to 19 cells on the base and 19 to 22 on the branch depending on list order and loop start, and Face3D-home swaps between 15 and
  20 (and 14, 13) with identical geometry (area equal to 0.01 m2, same 20 solver cells, 0 naked). These moves do not show an
  improvement or a regression in geometry. This extends the #84-side trace to this base.
- **The solver's own result on two-level-tilted does change** (cells 29 -> 23; naked 8 -> 14 worse at managed 0 and 3 -> 0 better at 0.21;
  solved panels fall by 17 % to 22 % and area by 6.7 % to 7.2 %; both configs end on the same 4777 m2 / 23 cells). Direction is mixed, the cause is not
  identified (candidates, unproven: more coplanar merges, or duplicate faces dropped from the solved set), and the pinned values
  (32 cells; 15 and 9 for the rebuild counts) match neither side. **Unexplained; not repinned.**
- The solver is also sensitive to re-presented input on both trees (two-level-tilted managed 0: 12, 25, 25, 30 cells for rot0, rot1,
  reversed, reversed+rot2). Caveat: re-created panels drop their internal edges (the "rot0" control, an identity re-creation, already
  gives 12 against 29 as-is), so only the differences among the rotated rows isolate vertex order. Those rows are **identical on the
  base and the branch**: the correction changes the result only on the fixture's own stored plane frames.

### Softhouse F and R presentations

Chain: PanelsBySolids -> SnapToJunctions -> Clean3D -> Extend3D -> AdjacencyCluster -> Rooms2 -> good classification, on the stored
extracted panels, as stored (F) and with every face re-presented reversed (R, `REVFACES=1`). "Frozen 48" is the reference set of 48
good rooms. (Production unchanged since `f08c442`, so the chains are not re-run for revision 2.)

| presentation | base: spaces / good / frozen 48 kept | branch: spaces / good / frozen 48 kept |
| --- | --- | --- |
| F | 71 / 48 / **48 of 48** | 72 / 48 / **48 of 48** |
| R | 74 / 39 / **39 of 48** | 72 / 45 / **45 of 48** |

- **F:** the per-room good/not-good status is identical on the base and the branch (no room changes); the space count is 71 -> 72.
- **R:** seven rooms recovered - Bath 4 A202, Bath 8 2OG A302, Bed 12 2OG A303, Bed 7 2OG D303, Carpark 4 A109, Circulation 8 A301,
  Kitchen 5 A204 - and one lost: **Bed 2 1OG D203**. Net 39 -> 45.
- Rooms good on F but not on R: 9 on the base, 3 on the branch (Carpark 2 C109, Bed 2 1OG D203, Bed 4 1OG C203).

### Bed 2 1OG D203 (traced on this base)

Earlier evidence (the #84-side record, `Wall-Run-Face-Order-Q4.md` on local `fix/wall-run-face-order-q4` `c829dea`, unpushed) classified
D203 as good on R only through the order fault. That is not carried over here; the trace below is on `f007824` vs the branch, using the
saved chain outputs and the rebuild of the saved `panels_extended.json` sets (no production change).

1. **The snapped stage is identical.** The wall pieces around the room in `panels.json` are identical across base-R, branch-R, base-F
   and branch-F. The abut rule is not what differs between R and F on the branch.
2. **R and F diverge in Clean3D, identically on the base and the branch.** For two 1OG walls south of the room, F aligns the lower wall
   to the midplane (y 3.34) and keeps the y 3.18 wall as a 2OG piece; R merges the y 3.18 wall across the storeys (z 3.425 to 9.5, 26.7
   m2) and leaves the other at y 3.50. These two R walls are the same in base-R and branch-R.
3. **What the branch changes in R is the other walls.** On the base, R also had legacy merges that F does not make (a y 4.890 wall merged
   from ground to 2OG; a slightly angled x -3.58 wall merged across storeys). On the branch those are the same as F's, so the room is
   no longer closed by them.
4. **Localised by substitution (rebuild of the branch-R set, `panels_extended`):** D203's cell is open (volume NaN) in R and closed
   (43.68 m3) in F, stable across five presentations of each set (as stored, list reversed, three loop rotations); swapping all
   slabs or all panels near the room does not change it; swapping **only the two 1OG walls** (y 3.18 and y 3.50 in R, y 3.18 and
   y 3.34 in F) closes it in R (43.68) and, put into F, opens it. The base-R cell closed with volume **42.22**, not F's 43.68: it was a
   different, accidentally closed shape.

**Conclusion:** the loss is not a new fault of the abut rule. It is the remaining R-only decision of the stage after snapping, which the
base's compensating merges had hidden. This answers where, not whether it is acceptable: D203 is a real loss on R relative to the base
and remains an acceptance risk. No fix is attempted here.

### Remaining face-order dependence (candidate, not proven)

`OverlapsInPlane`, `InPlaneOverlapRatio` and `InPlaneOverlapRatioVsSmaller` still bound faces in the plane's own frame
(`FootprintBounds`), which turns with the vertex order. They are the obvious candidates for the Clean3D divergence above. This record
does not claim it: the walls were traced, the code path was not.

### Face3D-home parity

On the base, `WorkflowParity ... Face3D-home.sam` asserted that the sewing rebuild ("A-solver-matched") does **not** match the solver's
20 cells (`SpacesMatchResolvedCellCount = false`). That count depends on how the solved faces are presented: the same solved geometry
gives 15 on the base as given, 20 on the branch as given, and 13 to 20 across presentations (trace table above), with the solver at 20
cells, 0 naked in both. With the correction the old assertion would fail by construction (20 == 20). This test-only change makes that
row diagnostic (`[diagnostic] ... sew-rebuild spaces=N, solver cells=M`) and, since revision 2, requires a successful rebuild (cluster,
merged cluster, result, spaces, parity diagnostic) and asserts the solver's closure (20 cells, 0 naked points, plus the checked
re-validation described above). `ParityClean` and the relation-invariance assertions on that row stay; `B-clean-extend` (19) and every
other row are unchanged. Not asserted: the cell volume (601.3 m3), which comes from the same solver.

## Not achieved, not established

- **F/R full invariance is not achieved.** Three rooms are good on F but not on R, and Clean3D still treats R and F differently.
- **Frozen-48 acceptance is not achieved** on R (45 of 48); F keeps 48 of 48.
- The solver's own two-level-tilted result changes (29 -> 23 cells, naked 8 -> 14 / 3 -> 0) and is unexplained.
- The 65.2 m2 Roof/Floor reclassification between solved sets was observed in the earlier #84-side comparison. Whether it applies to
  this branch is **not established**; it was not measured here. (This branch does show an F/R slab difference at z 3.475 that is the
  same on the base and the branch and does not control D203.)
- The cap fallback and non-parallel pairs are not presentation independent (see Revision 2, item 3).
- Hosted CI and a Grasshopper canvas run were not part of this validation; `PROJECT_PROGRESS.md` is post-merge only.

## Reproduce

```
git archive <commit> | tar -x -C %TEMP%\r84\SAM_OCCT_<tree>     # fa0 = f007824, fa1 = the branch head
# build the solution in Release; restore the frozen SAM.Occt.Native.dll (438089F8) into <tree>\build
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Release --no-build
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Release --no-build
dotnet test Testing/SAM.OCCT.GrasshopperTests/SAM.OCCT.GrasshopperTests.csproj -c Release --no-build
```

The F/R chain is the existing `softhouse.sh` harness (`tools-2026-10-27-combined/scripts`), run per tree. The trace probes are scratch
files copied into the isolated trees only.

## Evidence (machine-local; copy before switching machines)

`Documents/SAM_daily/2026-10-25-Softthouse/handover/evidence/2026-11-02-faceorder-standalone/`: `suites/` (trx and logs, unit and native
for both trees and for revision 2, Grasshopper logs), `native-per-test.txt` (outcome and moved-value comparison), `chains/{fa0,fa1}-{F,R}`
(stage outputs, `good.tsv`), `trace/` (presentation probe `probe-fa0/fa1.txt`, rebuild presentations `rebuild.txt`, substitution runs
`swap*.txt`, stage listings `d203-*.txt`), `probes/` (the five scratch probe sources). Built trees: `%TEMP%\r84\SAM_OCCT_fa0` and
`SAM_OCCT_fa1`.

## Next step

Owner review of this draft. Order of integration: this PR on `sow/2026-Q4`, then the kink-dependent correction separately. Open items
before promotion out of draft: a disposition for D203 and for the unexplained two-level-tilted solver change, and (separately) whether
`OverlapsInPlane` / `InPlaneOverlapRatio*` should read the same wall frame. Nothing here authorises a merge or the kink integration.

Generated by Michal Dengusiak and CodeClaude
