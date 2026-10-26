# Extend3D: walls extend to their own cap, never through another storey — Q4 (2026-10-26)

Status: revision 2 implemented locally on `fix/extend3d-no-invented-walls-q4` (base `sow/2026-Q4` at `22e4fd0`), local
commits `639ef12` (revision 1), `1408b93`, `696dce0` (revision 2). Not pushed, no PR. Step 1 of the Softhouse "Dining 1"
stage investigation (owner, 2026-10-26: SAM_OCCT Extend3D first, the SAM extraction fix as a separate PR). Revision 2
answers the independent review of 2026-10-26 (`SAM-BIM/.review-extend3d-revision2-20261026/REVIEW.md`, machine-local:
REVISE - cap splitting moved the clip level; MaxExtension used as evidence of a junction and of a split level; room
merger and Revit-home measurements unresolved). The Clean3D kink candidate stays out of this branch (see "Separate").

## Problem

`Extend3D` (and the `Solve3D` / `AutoTune3D` managed paths, which share `ConditionStage`) grew walls into storeys where the
input has none, which changes room identity. Softhouse: +555 m2 of panel area, walls growing by 9 to 98 m2 each. Two
mechanisms, each reproduced synthetically:

- **M1 - a cap the wall already passes is skipped.** `NearestCoveringCap` accepted only caps at or beyond the wall extreme.
  A wall whose base passes its floor by a few centimetres was grown to the next cap, a storey away (vertical reach is
  uncapped by policy).
- **M2 - extending a non-rectangular wall fills every notch.** `SAM.Geometry.Spatial.Query.Extend` (and the column-wise
  sloped-cap polygon) fill everything between the outline and the target line: a wall merged across storeys by Clean3D
  whose lower storey is narrower became a full rectangle.

## Change (`SAM_OCCT/SAM.Geometry.OCCT.Solver`)

Every rule reads the caps' actual geometry beside the wall; the wall's reach (`MaxExtension`) only bounds a move that the
geometry justifies, it is never the evidence.

1. **Side coverage** (`Panel3DSnapSolver.SideCoverage`, `LevelCoverage`, new `PlanIntervals`): for each side of the wall,
   the stretches of its plan line beside which a cap lies (the band from the wall's capture half-width `BucketSize` to
   twice that, from the cap's outline with its openings). Interval sets, so the result depends only on the area covered.
2. **Levels are surfaces** (`SameSurface`, `GroupSameSurface`): caps coplanar at the wall (same plane within the distance
   tolerance) form one level, coverage united; no elevation averaging. A floor in one piece, in unequal pieces or listed
   twice clips identically; caps at 3.00 and 3.04 stay two levels (review finding 1). The clip uses the level's extreme
   towards the wall's own part.
3. **Coverage aligned to the room boundaries** (`AlignToCrossings`): a coverage end within the capture half-width of a
   wall crossing this one (where this wall stands) is moved onto that wall's line. Caps are modelled to wall faces; read
   as they are, the clip stepped a wall's base 0.05-0.1 m beside a junction and left an open sliver (towers #142, #139).
4. **M1** (`NearestCoveringCap` lists the passed caps; `RestsOnPassedCaps`): a wall whose extreme passes caps by at most the
   overshoot rests on them, and is not extended, when they cover its whole line and nowhere does the space beside it
   continue past them to the next cap within its reach. Otherwise it is extended and the passed caps clip it column by
   column (item 5).
5. **M2** (`ClipBeyondIntermediateCaps` + `SnappedPanel.ClipBeyondLevels`): after a vertical extend, the extension grown
   past an intermediate flat level is removed in the columns the level covers and the original wall does not continue
   past (material within the overshoot of the level does not count), except:
   - **continuation** (`ContinuationStretches`): where the target covers a side of the wall and the level does not (a
     split level, or a floor stacked over the target on one side only), the space beside the wall continues past the
     level to the target: the extension is kept if the growth past the level is within the wall's reach. A level on
     every side the target covers always clips, however close (review finding 2: distinct levels kept);
   - **junction**: the stretch from where the original continues up to the line of a wall crossing this one in the
     storey beyond the level (the wall stopping short of it there), within the wall's reach; the fill stops at that
     wall's line (no overshoot fin). A stretch without a crossing wall is an intentional set-back or void and is clipped
     whatever `MaxExtension` is (review finding 2). The lateral extend (`ExtendWalls`) does not see such a gap: it reads
     the merged wall's whole plan extent.
   - Only the extension is clipped: the original face is united back whole. Each clip reports
     `SAM_OCCT_EXTEND3D_LEVEL_CLIP` (area, levels, extent); census `LevelClipCount`.

Plain rectangles never have free columns (fast path unchanged). No public API, parameter, default, Grasshopper component
or native change. `SolverReportFormat.cs`: doc comment only.

## Tests

- `Testing/SAM.OCCT.UnitTests/ExtendIntermediateCapTests.cs` (49 cases), revision 1 cases plus: the review's
  reproductions - nearby floors 3.00/3.04 with the lower one whole, split equally, split unequally and listed twice (same
  boundary, review probe point outside); a 0.15 m set-back without a crossing wall stays open at reach 0.1, 0.5, 2.0;
  junction closed with a crossing wall in that storey and kept open with it upstairs only or beyond reach; junction
  fill stops at the crossing wall's line; split level kept within reach (0.5, 2.0) and not beyond it (0.05); the same
  raised floor under both sides is a distinct level at every reach; floor stacked over the target on one side (towers)
  reaches the target at reach 0.4, not at 0.1; clip step at the crossing wall's line, not the floor's edge. With the
  revision-1 binary the review's harness reproduces findings 1 and 2; with revision 2 it gives equal results.
- `ExtendCensusCollection.cs`, `Testing/SAM.OCCT.IntegrationTests/Extend3DIntermediateCapIntegrationTests.cs`: as in
  revision 1 (2 native room-volume tests).

## Validation (local, machine `michal`; identical conditions; base = `SAM_OCCT-wt-extend3d-base` at `22e4fd0` + new tests)

Built with APPDATA/USERPROFILE redirected (GH PostBuild deploys to `%APPDATA%\SAM`); tests with the real APPDATA,
`--no-build`, one suite at a time. SAM from `..\SAM\build` (`sow/2026-Q4` `fe5bbce4`).

```powershell
dotnet build Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug
dotnet build Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug --no-build
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug --no-build
```

Tested content: `696dce0` (code and tests; this record is the only later change).

| suite | base `22e4fd0` | revision 1 `639ef12` | revision 2 `696dce0` |
| --- | --- | --- | --- |
| unit | 12 failed / 1060 passed | 1072 / 1072 | **1090 / 1090** (52 in `ExtendIntermediateCapTests`) |
| integration (native) | 27 failed / 469 / 2 skipped | 25 failed / 471 / 2 skipped | 25 failed / 471 / 2 skipped |

Revision 2 fails the same 25 tests as revision 1 (base's 27 minus the two new Extend3D tests). Messages that differ from
base (other 23 messages identical to base):

| test | pinned | base | rev. 1 | rev. 2 | explained |
| --- | --- | --- | --- | --- | --- |
| Extend3DIntermediateCap SetBack / PartyWall (new) | 2 / 3 rooms | 3 / 4 (fail) | pass | pass | intended |
| Extend3DPlaneTarget RevitHome (solver cells) | 14 | 22 | 7 | 7 | trace item 2 |
| WorkflowParity Revit-home (message prints solver cells) | - | 22 | 7 | 7 | trace item 2 |
| GoldenMaster managed whole-level-towers | 21 | 23 | 24 | 23 | trace item 3 |
| GoldenMaster managed 0.21 two-level-tilted | 9 | 16 | 16 | 17 | trace item 1 |
| Extend3DPlaneTarget AdjacencyCluster-home naked | 4 | 6 | 6 | **9** | trace item 4 - open |
| NineSpaceWallCapture translate 0.45/0.55/0.65 West2 (West3 at 0.45) | - | 298.502 / 290.673 m3 | 298.506 / 290.675 | 298.506 / 290.675 (West3 654.700 / 646.114, base 654.697 / 646.110) | numerical deltas, < 0.005 m3, still failing for the pre-existing reason |

Review harness (`SAM-BIM/.review-extend3d-revision2-20261026`, `dotnet run`): revision 1 binary reproduces the findings
(nearby floors 24.520004 vs 24.546672 m2, probe in/out; set-back filled at reach 0.5 only); revision 2 gives 24.440004 m2
for both representations with the probe outside, and the set-back open at reach 0.10 and 0.50 (36.150000 m2 both). The
original concave-floor case is unchanged (36.6 m2 both, notch filled under no floor). `git diff --check` clean.
Not run: GrasshopperTests, hosted CI (nothing pushed), Softhouse chain with revision 2.

## Where rooms change, traced (open items of revision 1)

Probes (machine-local tools, see "Evidence"): the managed `Solve3D` configured as `Modify.Solve3D` does, stopped after
conditioning, resolve-only on saved conditioned faces, the solver's own cells (`Solve3DReport.Cells`), and rebuilds of
the solver's output faces with the measurement options (`Create.Shells`, sew 0.01) - with and without exact duplicates.

1. **two-level-tilted (bbl 0.21), upper rooms merged in revision 1** - not an Extend3D stage. The solver's own cells are
   identical in base, revision 1 and revision 2 (33 cells, same volumes). The merger (245.9 m3) appears only when the
   solver's output faces are rebuilt (the golden-master / probe measurement), in revision 1 only. Revision 1 and 2 output
   faces are identical to 1e-4 m (same 416 faces, 8 permuted); 35 differ by 1e-9..1e-7 m (resolve noise), and the
   rebuild result follows the face set, not the order (both orders tried) nor the sew tolerance (0.005-0.015 m). The
   output carries 45 exact duplicate faces (base 49), including the separator between the two rooms (two coincident
   copies). Removing exact duplicates, base, revision 1 and revision 2 all rebuild to the same 23 cells with the rooms
   separate. Responsible stage: the solver's output assembly - it emits coincident duplicates (the consolidation rebuild
   is adopted in all three runs and its faces are flattened from shells, `Panel3DSnapSolver.cs` FinalizeAndValidate,
   `shells.SelectMany(x => x.Face3Ds)`; RetainDropped also re-adds faces - the exact source is to be confirmed) - and a
   rebuild of coincident duplicates is numerically unstable. Revision 2 rebuilds the two rooms separately (83.9 + 162.0
   m3; the 162 m3 room, open in base, closes): 17 cells vs base 16.
2. **Revit-home east room and 7 spaces vs 5 cells** - reconciled. Both tests report the solver's own count: the
   plane-target test asserts `ResolvedCellCount` and the parity message prints `spaces=={resolvedCellCount}` (the parity
   comparison itself counts AdjacencyCluster spaces). On one solve (revision 1 and 2 alike): solver 7 cells; parity
   A-solver-matched 5 spaces; probe `Create.Shells` 5 cells. The rebuild merges the south strip the solver splits at
   x 8.135 (33.96 + 10.29 -> 44.24 m3) and does not close the east room the solver closes (x 8.135..11.535, 53.09 m3).
   The output has 12 exact duplicate faces; without them the south split returns (6 cells) but the east room still
   merges with its neighbour. Responsible stage: output/rebuild, as item 1, not Extend3D.
3. **whole-level-towers** - the solver's own cells are identical in revision 1 and 2 (24 cells, same list); room counts
   measured on the rebuilt output swing with nm-level differences (26 exact duplicate faces in every build). Without
   duplicates: base 18, revision 1 18, revision 2 19 (one more room separated, 100.3 m3 at x 21.9..26.7).

4. **AdjacencyCluster-home naked 6 -> 9 (open, owner decision)** - caused by Extend3D, a single conditioning difference
   from revision 1: clip `#20` removes a 0.675 m2 stub below the first floor at the north wall's end (y 4.8, x -3.09..
   -2.865), where the upper storey overhangs the ground-floor corner wall (x -2.865) and no wall crosses the line below
   the floor there. Revision 1 kept it by the reach rule the review rejected. The solver's own cells are identical
   (18); its naked count rises 6 -> 9 at that corner; the rebuilt measurement goes from one invalid cell spanning both
   storeys (-329 m3, base and revision 1) to six separate ground-floor rooms, with two first-floor cells not closed.
   Whether the stub is an intended overhang (keep the clip) or a modelling gap to close needs the owner's call; no reach
   exemption was added to recover the count.

So the golden-master, plane-target and parity counts on these fixtures measure the rebuild of an output with coincident
duplicates, not Extend3D's geometry alone; no re-baseline is proposed on them.

## Separate (not on this branch)

- Clean3D kink candidate: `clean3d-kink-jog-candidate.patch` (machine-local evidence folder of 2026-10-26).
- SAM extraction fix (junction blocks shorter than the wall thickness): SAM worktree `SAM-wt-junctions`, empty.
- New: solver output with exact duplicate faces (item 1 above) - its own bounded task in the output assembly
  (`FinalizeAndValidate` / heal), with a determinism test (rebuild invariant to 1e-7 m perturbation).

## Evidence (machine-local, copy before switching machines)

`SAM_daily/2026-10-25-Softthouse/handover/evidence/2026-10-26-r2/`: `runs/` (integration logs and trx, probe outputs:
`tlt/` two-level-tilted, `rh/` Revit-home, `fx/` and `towers/` whole-level-towers), `tools/pipe` (probe sources:
`Stage.cs` staged solver, `Recon.cs` three measurements, `Reorder.cs` rebuild with order / substitution / dedup /
sew tolerance, `Compare.cs` face-set comparison, `Chain.cs` towers chain, `Replay.cs` vertical extend replay),
`tools/stage.sh`, `tools/safebuild.sh`, `reviewed-dll/` (the reviewed revision-1 solver DLL, SHA256 `E63A58A3...DF4`).
Comparison worktrees (detached, do not commit): `SAM_OCCT-wt-extend3d-base` (`22e4fd0` + tests), `SAM_OCCT-wt-extend3d-fix1`
(`639ef12`).

## Next step

Owner review of revision 2, the trace (items 1-3) and the decision on item 4; an independent re-review against the same
harness; rerun the Softhouse chain with revision 2 (revision 1: 70 -> 52 spaces, masked kink/extraction defects). Then:
the output duplicate-face task as its own bounded change (before any golden re-baseline, since those counts measure it),
then push and draft PR for this branch, the Clean3D candidate, and the SAM extraction PR.
