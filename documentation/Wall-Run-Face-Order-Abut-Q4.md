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

## Diagnostic: two-level-tilted solver change (2026-11-03; diagnosis only, no code or test change)

Supersedes "unexplained" for this row above. Base `f007824` (tree `fa0`) vs `2cb0d34` (tree `fa1`); frozen native `438089F8` in both (checked
before and after every run); `Fixtures/two-level-tilted.sam`, `Solve3D(forceManagedPipeline: true)`, default options, `bucketBetweenLevels` 0 and
0.21; scratch probes only (`probes/Tilted*.cs`, outputs in `trace/tilted/`).

- **Reproduced.** Solver cells / naked: bucket 0 **29 / 8 -> 23 / 14**; bucket 0.21 **33 / 3 -> 23 / 0**. Native results, not sew-rebuild counts.
- **Earliest stage: Stage A snap.** The only production difference is the abut predicate (b) of `Panel3DSnapSolver.Snap`. Stage A keeps 186 / 183
  clean faces in both trees, but 15 clean faces change plane position (0.001 to 0.21 m, all walls with normal +-Y), and `snapped-to-backer`
  records rise 212 -> 228 over the two runs.
- **Cause: the world-Z wall frame misreads a tilted level.** The fixture is tilted 12.84 deg about Y (level up (-0.222, 0, 0.975)); its stored wall
  planes carry the tilted frame (`AxisY` = -level up). The base read that stored frame, i.e. the level frame (right for this geometry); the branch
  reads run = horizontal, height = world Z. Of 1084 vertical, parallel, within-0.3 m pairs, the base equals a level-frame reading in 1084/1084 and
  the branch equals a world-frame reading in 1084/1084. They differ in **28 directed pairs (14 pairs, 24 panels), all gained, none lost**. Each is a
  vertically stacked wall pair (an upper-storey piece over the next bay's lower-storey piece): level-frame height overlap **-0.08 m** (the floor
  gap), but the tilt makes the world-Z bands overlap by 0.7 to 2.7 m, so `AbutsColinearWithin` accepts it. The rule requires overlapping heights,
  so these are false accepts. Panels: `trace/tilted/affected-panels.txt` (sources S3..S284, both facades, x -3.6 to 49.5).
- **Presentations (predicate level, native-free, 9 presentations: stored + 2 windings x 4 loop starts, all loops kept).** Branch: 172 abut-true in
  all nine (invariant, as designed). Base: **144 on the stored frames, 172 on all eight re-created ones** (re-created planes get world-vertical axes;
  `AxisX` was along the run for all 176 walls in every variant, so this is the tilt of `AxisY`, not an axis swap). So the base's stored-frame
  answer was right by accident of the stored frames, and a panel the solver moves is rebuilt with a canonical frame, so the base mixes frames
  mid-solve. Full solver on the 16 re-presented runs (2 buckets x 8): cells / naked **identical on the base and the branch** (e.g. bucket 0:
  12, 25, 19, 24, 25, 32, 30, 28 cells); re-created clean faces identical. The change exists only on the stored tilted frames.
- **Cells lost (bucket 0).** Absent on the branch (and one added, (10.8, -22.9, -1.3) 77.65 m3, a room the base has only at bucket 0.21; 26 real cells -> 23): (33.8, -3.4, 7.1) 161.96 m3; (43.5, -3.5, 6.1) 112.01; (38.6, -2.3, 8.2) 83.92;
  (25.1, -21.9, 2.0) 78.04 (436 m3), plus three slivers (0.46, 0, 0). Four others resize by more than 0.05 m3 ((33.8, -22.6, 7.1) 145.53 -> 161.96,
  (38.6, -23.7, 8.2) 80.58 -> 84.40, (20.9, -21.8, 1.0) 115.81 -> 113.67, (34.5, -21.5, 4.1) +0.29); the other 14 are unchanged. Total cell volume 4371 -> 4031 m3 (bucket 0.21: 4437 -> 4031; the base had 6 cells under 1 m3, two with
  non-positive volume). Solved area 5118 -> 4777 m2.
- **Mutation check (scratch copy of `fa1` only, restored byte-identical to `2cb0d34`; `trace/tilted/MUTATION-scratch.txt`).** Reading the wall
  frame from the level up axis: up = world Z reproduces the branch exactly (23 / 14, 23 / 0); up = level up gives **27 / 4 and 27 / 0**. It
  recovers two lost rooms (112.01, 78.04), still lacks 161.96 and 83.92, and adds three south-facade cells absent from the base (120.97, 116.10,
  77.65). So the 28 pairs account for a large part of the base -> branch move, not all of it, and **neither tree is a verified-complete
  decomposition**; the repository has no ground-truth cell set for this fixture (the pinned 32 / 32 is stale).

**Classification: REGRESSION, tilted-level specific.** The new reading introduces 28 false cross-storey abuts when a level is tilted: the
world-Z height is the "vertical-wall reading `IsVertical` already assumes", and `IsVertical` accepts walls up to 20 deg off vertical. The
revision-2 matrix covers leaning walls to 3 deg only, so 12.84 deg was not tested. The base is not a correct reference either (frame-dependent,
144 vs 172), so the 29 -> 23 count is not itself the measure of harm. **Recommendation: REVISE.**

**Single smallest next action:** give `RunHeightBounds` / `AbutsColinearWithin` an optional level-up axis (default world Z, so every existing result
is unchanged) and pass the solver's existing `Up` through `SnapStage` to `Snap`, with one unit test: a stacked pair on a 12.84 deg level
(height gap 0.08 m) must not abut.

**Not established:** which step after Stage A drops each of the four rooms (not traced per room); whether the 15 moved clean faces are harmful in
themselves; behaviour on the other tilted fixtures (`tilted-two-spaces`, `whole-level-tilted`), not measured.

## Correction: model-up axis for the wall height (2026-11-03; local, not pushed)

**Change.** `SnappedPanel.AbutsColinearWithin(other, maxOffset, tolerance, Vector3D up = null)` reads the wall height along `up` and the run
along `up x normal`. `up` is the solver's existing `Up` (`Solve.ResolveUp` = mean floor normal, else roof normal, else world Z), passed
`Panel3DSnapSolver.Up` -> `SnapStage.Clean(..., up)` -> `SnapToFixedPoint(..., up)` -> `Panel3DSnapSolver.Snap(..., up)` -> the abut predicate (b). Stage A
runs before the solver's canonical rotation, so `Up` is in world coordinates there, the same frame as the faces. No new frame type. Null, zero,
NaN/infinite, world Z (including -Z and an XY part under 1e-12) take the world-Z code path unchanged, so every caller that passes no `up`, and
every horizontal level, is bit-identical. `up` and `-up` give one frame. An `up` along the wall normal has no run and reads like a cap
(`FootprintBounds`), as before. `IsVertical`, the 20 deg wall test, the kink rules, Clean3D, wall repair and the tolerances are untouched.
**Signatures (revised after review of `e2e7a0c`, which had replaced four public signatures and broke compiled callers).** The pre-PR public
signatures are restored unchanged and forward with `up = null`: `AbutsColinearWithin(other, maxOffset, tolerance)`, `Panel3DSnapSolver.Snap(..., records)`,
`SnapStage.SnapToFixedPoint(..., records)` and the public `SnapStage.Clean(..., doubleWallGap)`. The `up`-carrying versions are added beside them as
overloads with a required `up`, so no call can bind ambiguously: `AbutsColinearWithin(..., up)`, `Snap(..., records, up)`, `SnapToFixedPoint(..., records, up)`.
The `up` version of `Clean` is internal only (`Clean(..., doubleWallGap, coplanarUnion, up)`, which the solver calls); the internal
`Clean(..., doubleWallGap, coplanarUnion)` is kept as a forwarder.

**Unit tests** (`WallRunFrameTests`, 51 in the class, each over the 192-presentation matrix): stacked pieces on a 12.84 deg level with a 0.08 m
level gap do not abut (both directions); the same pair abuts under the world-Z reading (guards the fixture); valid abuts, jogs, offset and run limits
on the tilted level; `-up`; tilts of 5, 20 and -30 deg; horizontal level with `up` = +Z, -Z, 7.5 Z, a 1e-14 tilt, zero, NaN and infinity all equal
no `up`; `up` along the wall normal does not throw; `Snap` aligns the jogged stacked pair under world Z and leaves it under the level up. Full
unit project: 1106 passed, 0 failed.

**Native** (tree `%TEMP%\r84\SAM_OCCT_fa2` = branch `2cb0d34` + this change; frozen native `438089F8`, checked in fa0, fa1 and fa2; same fixtures,
`Solve3D(forceManagedPipeline: true)`, default options; probe `LevelUpCompareProbe`, output `trace/levelup/cmp-fa{0,1,2}.txt`):

| fixture, bucket | base fa0 `f007824` | branch fa1 `2cb0d34` | corrected fa2 |
|---|---|---|---|
| two-level-tilted, 0 | 29 cells / 8 naked / 4371.5 m3 | 23 / 14 / 4031.2 | **27 / 4 / 4465.7** |
| two-level-tilted, 0.21 | 33 / 3 / 4436.8 | 23 / 0 / 4031.2 | **27 / 0 / 4711.6** |
| tilted-two-spaces, 0 and 0.21 | 2 / 0 / 723.65 | same | same |
| whole-level-tilted, 0 and 0.21 | 22 / 0 / 3377.8 | same | same |
| whole-level-flat, three-spaces | 22 / 0 / 3479.9; 3 / 0 / 264.8 | same | same |

- **Predicate evidence (`LevelUpPairProbe`, `trace/levelup/pairs-fa*.txt`).** Over 2344 directed vertical pairs within 0.3 m plane offset (parallelism not filtered) of the 287
  sources of two-level-tilted: a direct level-frame reading accepts 406. fa0 accepts 406, fa1 499 (93 false accepts, all with level height overlap
  -0.08 m), fa2 **406 with 0 disagreements** with the reference.
- **Lost rooms at bucket 0.21 (base -> fa1 -> fa2; `trace/levelup/c21-fa*.txt`).** **Both north rooms recover**: (33.8,-3.4,7.1) 161.96 m3 and
  (38.6,-2.3,8.2) 83.92 m3 are present on the base, absent on fa1 and present on fa2, as are the 112.01 and 78.x rooms. At bucket 0.21 fa2 has 27 cells,
  0 naked (base 33 / 3). The south room at (29.2,-21.4,2.9) 116.10 is new in fa2; 120.97 (20.3,-22.5,4.0) has no separate cell there, and a 283.20 m3 cell at
  (17.3,-22.6,3.3) matches the combined volume of that room and its neighbour (a merge is plausible, **not verified**; fa2 traces carry no face lineage).
  Earliest divergence from the base is Stage A wall snapping (changed records near source panels S182 and S228 at both buckets).
- **Lost rooms (bucket 0, base -> fa1 -> fa2).** (43.5,-3.5,6.1) 112.01 m3 and (25.1,-21.9,2.0) 78.04 m3 **recover** (112.01; 78.94, the base's
  78.04 and 78.56 pair resized to 78.94 + 78.94). (33.8,-3.4,7.1) 161.96 m3 and (38.6,-2.3,8.2) 83.92 m3 **do not recover**: absent in all of
  fa1 and fa2, as the mutation check predicted. Their absence is not traced to a stage here.
- **New geometry versus the base (bucket 0).** Three cells the base lacks at bucket 0: 116.10 (29.2,-21.4,2.9), 120.97 (20.3,-22.5,4.0),
  77.65 (10.8,-22.9,-1.3, a room the base has at bucket 0.21). Resized: 145.53 -> 161.96 at (33.8,-22.6,7.1), 80.58 -> 84.39, 82.62 -> 84.00,
  150.12 -> 152.75, and the 610.1 hall -> 610.32. Four base slivers (volume 0 to 0.46) are gone. These are consistent with the level-frame reading
  but **no ground-truth cell set exists for this fixture**, so the new cells are not independently verified rooms. Naked 4 at bucket 0 remains
  (8 on the base).
- **Softhouse F/R smoke** (`chains/fa2-{F,R}`, `softhouse.sh <dir> <dirs> [REVFACES=1]`): F 72 spaces, 48 of 70 good; R 72 spaces, 45 of 70
  good. `panels.json`, `panels_clean.json`, `panels_extended.json` equal fa1 once GUIDs are masked; `good.tsv` is byte-identical. Not changed.
- **Native integration (502 tests incl. 6 skipped probes).** 25 failed in fa0, fa1 and fa2 alike: the same set (stale pins, not repinned). Moved
  values: `TiltedLevel_UnchangedByPlaneTarget` 29 / 23 / **27** (pin 32); two-level-tilted golden masters 13 / 19 / **17** (raw, pin 15) and 16 / 20 / **23**
  (021, pin 9); `Face3DHome_Parity` 15 / 20 / 20. Grasshopper tests 38 passed.

**Parallel filter.** The 28 directed pairs that differ between the world-Z reading and the branch are exactly the parallel-filtered subset of the 93
false accepts the wider `LevelUpPairProbe` counts: that probe checks plane offset but not parallelism, and its other 65 pairs are near-perpendicular and
cannot reach the solver's abut decision (`Snap` filters on parallelism first).

### Revision after review: forwarding overloads (2026-11-03; local, not pushed)

Tree `%TEMP%\r84\SAM_OCCT_fa3` = branch `2cb0d34` + the corrected `up` change with the restored signatures (frozen native `438089F8`).

- **Binary compatibility.** `%TEMP%\r84\compat\src\Program.cs` is compiled against the **base** `f007824` Solver assembly and calls the eight pre-PR
  public forms: `AbutsColinearWithin` (3 arguments), `Snap` (3 arguments; named `alignColinearOffset`; 7 arguments with `records`), `SnapToFixedPoint`
  (3 and 6 arguments), `SnapStage.Clean` (4 and 9 arguments). Run unchanged beside the base assembly, beside the fa3 assembly and beside the `2cb0d34`
  assembly it prints the same eight results (True x4, 2, 2, 1, 1). Beside the `e2e7a0c` assembly (fa2) it throws
  `MissingMethodException: ... AbutsColinearWithin(SnappedPanel, Double, Double)`, which is the reviewed defect. Full output and exit codes
  (0, 0, 0 and non-zero respectively; fa2 reported 127 under Git Bash) are in `evidence/.../compat/result-{base,new,fa1,fa2}.txt`: each of the three
  passing runs lists all eight calls.
- **Behaviour unchanged.** Unit project 1106 passed, 0 failed (one test call updated to the explicit `Snap(..., records, up)` form). Native
  `LevelUpCompareProbe` (all five fixtures, buckets 0 and 0.21) is **line-identical** to the fa2 output (162 lines), so every cell count, naked-edge
  count, volume and centre in the table above is unchanged by the signature work.
- **Not changed:** the algorithm, tolerances, `IsVertical`, test pins, PR #84.

**Recommendation: TEST.** The false accepts are gone and the predicate agrees with the level frame on
all 2344 pairs, horizontal levels and Softhouse are unchanged. At bucket 0.21 all four lost rooms recover; at bucket 0 the two north rooms (161.96 and
83.92 m3) are still absent. 3 cells at bucket 0 are new, with no ground truth, and the exact solved boundary fidelity of the recovered rooms is not
established: owner review of the remaining bucket-0 difference before promotion out of draft. Not pushed.

Generated by Michal Dengusiak and CodeClaude
