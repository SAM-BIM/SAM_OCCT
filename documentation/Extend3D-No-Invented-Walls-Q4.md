# Extend3D: walls extend to their own cap, never through another storey — Q4 (2026-10-26)

Status: revision 3c implemented and validated locally on `fix/extend3d-no-invented-walls-q4` (base `sow/2026-Q4` at
`22e4fd0`), local commits `639ef12` (revision 1), `1408b93`, `696dce0` (revision 2), `ce8e45d` (revision 2 robustness fix
found by the Softhouse run, see "Softhouse"), `33ea9bb`, `5600f90`, `2ab5316` (revision 3, see "Revision 3"), `2986ad5`,
`0f060ff`, `b837165` (revision 3b, see "Revision 3b"), `695e814` (revision 3c, see "Revision 3c"). **Tested content:
`695e814`** (unit 1133/1133, GrasshopperTests 38/38, native integration 24 failed / 472 / 2 skipped - the `b837165`
failures less WorkflowParity Revit-home, now passing; every output change classified in "Revision 3c" item 5).
**Draft PR SAM-BIM/SAM_OCCT#84** (opened 2026-10-26). `sow/2026-Q4` merged in on 2026-10-27 (SAM_OCCT#85, see "Merge of
sow/2026-Q4"); native not re-run on the merge. **Merge: NO-GO** (open items in "Handover"). Step 1 of the Softhouse "Dining 1"
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
   - Only the extension is clipped: since revision 3c (`695e814`) the clipped face is the extended face cut by the
     removed extension pieces (a local cut); before, the original face was united back whole with the kept extension
     pieces, which could re-open a tolerance-wide crack (revision 3c item 2). A clip that would lose original
     material is not made. Each clip reports `SAM_OCCT_EXTEND3D_LEVEL_CLIP` (area, levels, extent); census
     `LevelClipCount`.

Plain rectangles never have free columns (fast path unchanged). No public API, parameter, default or native change.
Grasshopper: no GUID or parameter change; minor version bumps for the changed computed result (revision 3c item 4).
`SolverReportFormat.cs`: doc comment only.

## Revision 3 (2026-10-26; answers `SAM-BIM/.review-extend3d-opus-plan-20261026/REVIEW.md`, machine-local)

The review (REVISE the next action, NO-GO for merge) reproduced M2 with an isolated managed harness; it was re-run here
first and gave its stated output on the `ce8e45d` solver (`E5618CB7...`): merged crossing wall 36.600000 m2 with the lower
junction invented, split representation 36.150000150 m2, with and without `ExtendWalls`.

1. **M2 fixed (`33ea9bb`)**: junctions read from the crossing wall's material at this wall's line, not its overall extent.
   `Crossings` (`Panel3DSnapSolver.cs`) cuts the crossing face (openings excluded) with a column of its own plane around the
   meeting point, half-width = band + tolerance (`MaterialAtJunction`; a tilted wall is read in its own plane), and
   returns one entry per stretch of elevation with material, flagged when it runs to the crossing wall's own base or
   top. A level's junction needs that material in the storey beyond the level (overlap > band, as before, now with
   material instead of the bounding box); `ClipBeyondLevels` keeps the fill only over the elevations with material
   (open beside an opening at the junction; a stretch running to the crossing wall's base/top counts on to its cap, since
   that wall's own extend moves it there). Coverage alignment (`AlignToCrossings`) uses the same material entries, over
   the wall's own elevations as before - deliberately not narrowed to the storey beyond the level: the towers #142
   control relies on a crossing wall that reaches the raised level only after its own extend. The wall feet are
   snapshotted with their faces before any wall moves (order independence kept). Internal signatures only.
2. **L1/L2 reporting (`5600f90`)** in `SnappedPanel.ClipBeyondLevels`: a kept extension piece not connected to the
   wall (the face is the union piece holding the original) is reported, and the routine's own failure returns (no union
   piece holding the whole original; invalid clipped face) report too, as `SAM_OCCT_EXTEND3D_LEVEL_CLIP_FAILED` (shown at
   Summary by the existing `_FAILED` rule); the extension is then kept whole, as before, but never as an ordinary extend.
   SAM's planar `Difference` returns null for an empty result as well as on failure, so null still reads as empty
   (treating it as failure broke 34 tests and was reverted before commit). Exceptions still propagate: no
   catch-and-skip. *Correction:* the previous record/plan said the diagnostics standard calls for catch-and-skip; it does
   not (`SAM/docs/DIAGNOSTICS_STANDARD.md` section 6: failure paths carry an Error, uncaught exceptions still propagate).
3. **L2 area threshold (`2ab5316`)**: the report of item 2 showed one real clip failure on the Softhouse Clean3D output
   (wall #17, top extension past z 3.475/6.475: "would not hold the whole original wall"), silent at `ce8e45d` (the
   extension kept whole, no line). Cause, local to the clip routine: areas (removed extension, lost original, dropped
   pieces) were compared with the 1e-6 m distance tolerance, i.e. a 1 mm square. A shadow grazing the extension removed
   about 1e-5 m2 along the 12 m wall (kept 1.9569 of 1.9569 m2 to 4 decimals), so the clip ran, and the union's original
   then measured 1.49e-6 m2 short (temporary debug build, `evidence/2026-10-26-r3/l2/failmin-debug.txt`). The threshold is
   now a sliver of the tolerance's width along the face's boundary (tolerance x perimeter): a graze is no clip. Result:
   identical to `ce8e45d` on that input (the intended clip result already kept the whole extension), and no failure line.
   Whether that kept extension is right is a rule question outside this defect: the second-floor slab does not cover
   y -3.345..-2.887 beside #17, so that column is treated as an opening and the notch above z 6.475 is filled (rule of
   revision 2, unchanged; not investigated further).

**Failing before / passing after** (test assembly of `5600f90`, solver DLL swapped; `evidence/2026-10-26-r3/`):

| test (ExtendIntermediateCapTests) | `696dce0` | `ce8e45d` | `5600f90` | `2ab5316` |
| --- | --- | --- | --- | --- |
| `Extend_CrossingWallMeetingTheLineInTheOtherStoreyOnly_SetBackStaysOpenInAnyRepresentation` merged x lateral x direction (4) | fail | fail | pass | pass |
| same, split representation (4) | pass | pass | pass | pass |
| `Extend_CrossingWallMeetingTheLineInTheSetBackStorey_JunctionClosedInAnyRepresentation` (8, positive controls) | pass | pass | pass | pass |
| `ExtendWallsThenExtend_CrossingWallOpenAtTheJunction_FillOnlyWhereItHasMaterial` | fail | fail | pass | pass |
| `ExtendWallsThenExtend_CrossingWallOpeningAwayFromTheJunction_JunctionClosed` (control) | pass | pass | pass | pass |
| `Extend_SoftHouseGroundSlabWithThreeOpenings_CompletesAndKeepsTheWall` (L1, real geometry) | **fail** (NullReferenceException) | pass | pass | pass |
| `ClipBeyondLevels_KeptExtensionNotConnectedToTheWall_IsReportedNotDroppedSilently` (L2, direct call) | - | fail (internal signature differs) | pass | pass; fails with the report disabled (mutation, on `5600f90` source) |
| `Extend_ShadowGrazingTheExtension_IsNotAClipNorAFailure` (L2, real geometry) | - | pass (nothing reported then) | **fail** (false failure) | pass |
| `Extend_FloorWithSeveralOpeningsAtTheWall_EachOpeningKeepsTheExtension` (`ce8e45d`'s test) | **pass** - not a reproduction | pass | pass | pass |

Unit suite at `2ab5316`: **1112 / 1112** (`5600f90`: 1111, `33ea9bb`: 1109). Review harness
(`.review-extend3d-opus-plan-20261026`, solver swapped): all four rows 36.150000150 m2, no invented junction (before:
merged rows 36.600000000, invented). Earlier harnesses (`.review-extend3d-20261026`, `.review-extend3d-revision2-20261026`):
output identical at `ce8e45d` and `33ea9bb`.

**Clip #20 (review M1), managed conditioning only** (`STAGE=condition`, REBUILD unset, `nativeResolved=False` in every
run; fixture `AdjacencyCluster-home.sam` SHA256 `eee08973...a2e115`; SAM `SAM-wt-junctions` `fe5bbce4`; solver DLLs
pinned, below). Conditioned panels paired by source identity (`SnappedPanel.SourceIndices` = clean-face ordinal 20; the
`LEVEL_CLIP` line names `#20`, panel index 20, removed region x -3.09..-2.865, y 4.8, z -0.05..2.95):
- clean faces (conditioning input): 41 of 41 identical, ordered loops, `639ef12` vs `ce8e45d` vs `33ea9bb`;
- `639ef12` vs `ce8e45d`: 40 of 41 conditioned panels identical (ordered loops); #20 differs only in the clip region:
  outside it the 27 vertices are bit-identical, in the same cyclic order and direction; A has 1 vertex in the region,
  B 4 (the clip's corners and a collinear vertex at z ~0 on the step); the planar symmetric difference is 0.675 m2, all
  inside the region (0 outside); the only edges that differ outside the region are the two edges ending at its corners
  (the bottom edge now starts at x -2.865, the end edge stops at z 2.95);
- `ce8e45d` vs `33ea9bb` (M2 fix): 41 of 41 identical.
So the managed geometry does **not** explain the naked ribbon at x 3.635..11.535, z 3..4.378 on #20 (bit-identical there)
or the first-floor merge; the remote effect arises in the native resolve of the changed face. Stopped at that boundary.
Smallest next probe (native, needs owner authorisation): resolve only, on the saved conditioned faces, swapping #20
alone - `stage.sh <out> <pinned ce8e45d-build> AdjacencyCluster-home.sam 0 resolve res-B FACES=<cond-ce8e45d.faces.json>`
and the same with `SWAP="<cond-639ef12.faces.json>|20"` (index 20 = #20: 41 conditioned faces, all valid, so
`faces.json` order = panel order); compare naked points and cells. If the swap alone moves 9 -> 6, the native resolve's
sensitivity to that one face is established; a third run with only the collinear z ~0 vertex removed would test that
vertex.

**L1 reproduction** (managed `Modify.Extend3D` alone, `inputAlreadyClean`, bbl 0.21, fill 0.5, directional - the chain's
arguments - on the saved Softhouse Clean3D output `evidence/2026-10-26-r2/softhouse/fix/panels_clean.json`, SHA256
`d1724384...a23c93ff`): `696dce0` throws NullReferenceException (`Face.InternalEdge2Ds` <- `FixEdges` <- `Intersection`
<- `SideCoverage`), as the original crash; `ce8e45d`, `33ea9bb`, `2ab5316`: 82 panels, 327 diagnostics, 15 level clips,
0 clip failures, identical diagnostics and identical extended panels (82 of 82, ordered loops, paired by position since
Extend3D issues new Guids); `5600f90`: the same plus the one false failure of item 3. Reduced (all caps with one wall, then caps removed while it still throws): the ground
slab with 3 openings and wall 7 (or 9); the slabs with 7 and 5 openings do not throw with those walls. The crash needs
the slab exactly as saved (its plane, normal -Z, and 2D loops at 1e-6 m): rebuilt from 3D points or with any loop
orientation it does not occur, which is why the regression builds the faces from the saved plane and loops.

**L2 assessment.** Retention argument: on Extend's own output every column of the wall's plan range is filled from the
target to the wall (flat and sloped caps alike); a shadow removes only elevations beyond a level (past the band) in a
free column, where the original has no material past the band, so each kept addition keeps its part adjacent to the
original; a junction piece lies beside a continuing column, which is never shadowed and is filled to the target, so it
is attached laterally. Hence the union is one piece holding the original and every permitted extension - up to the
planar operations' noise, which is what item 3 corrects (a graze measured as a clip; one occurrence on the Softhouse
input, none on the fixtures). Evidence at `2ab5316`: 0 `LEVEL_CLIP_FAILED` lines in the Softhouse Extend3D run and in the
conditioning of 8 fixtures (below); the direct test shows a hand-built disconnected piece is reported. Residual (SAM
side, no change here): SAM's multi-face `Difference` skips a piece whose single-face step returns null, and null also
means empty, so a planar failure there would over-clip without trace; not observed.

**Fixtures, managed conditioning `ce8e45d` vs `2ab5316`** (paired by source key, ordered loops; `evidence/2026-10-26-r3/
fixtures-cond-2ab5316/`, `validation-2ab5316.txt`): conditioned panels identical in all 8 - AdjacencyCluster-home 41,
Revit-home-panels 28, three-spaces 16, tilted-two-spaces 11, two-level-tilted (bbl 0.21) 183, whole-level-flat 106,
whole-level-tilted 106, whole-level-towers 160 - and their clean faces too; 12 level clips (1, 2, 0, 0, 1, 0, 0, 8), 0
clip failures; `nativeResolved=False` in every run. `5600f90` gave the same on all 8 (`fixtures-cond/`), `33ea9bb` on
AdjacencyCluster-home (`clip20/`).

## Revision 3b (2026-10-26; answers `SAM-BIM/.review-extend3d-revision2b-20261026/REVIEW.md`, machine-local)

The review of `2ab5316` (draft PR GO once this record is committed; merge NO-GO) found: L1 failures reported at Info
(finding 1), an L2 allowance scaled by the whole wall (finding 2), the stale committed record (finding 3), no native run
on revision 3 (finding 4), the untested AtBase/AtTop assumption (finding 5) and test gaps (finding 7). Three code commits,
each built and unit-tested on its own from a `git archive` export:

1. **L2 per piece (`2986ad5`).** The clip compared areas with tolerance x the extended face's whole perimeter, so the
   allowance grew with the wall: the review's 30 m wall with a 20 mm lower-storey set-back was filled silently at
   tolerance 1e-3 (0.06 m2 < 1e-3 x 72 m). Every residual piece is now judged by its **own mean width** - 2 x area / boundary
   length (outer and inner loops), the measure of Clean3D's collapse test (`SnapStage.MeanWidth`, #83 `506fda7`): a
   piece is noise only when that width is within the distance tolerance. A strip w wide has mean width ~w however long,
   so neither the wall's size nor the other pieces change the verdict. Applied the same way to the additions and kept
   additions, to the **removed** pieces (the additions' intersections with the shadows; a clip runs only if one is wider
   than the tolerance), to **lost original** material (any non-sliver piece of the original outside the kept face fails
   the clip) and to **discarded** pieces. Not applied where original geometry is read as evidence (the columns where the
   original continues past a level, crossing material, cap coverage): a width heuristic never removes or discounts
   original material. A compact piece of original, however small (a 50 mm square at 1e-3), fails the clip; at
   `2ab5316` it was dropped silently (0.0025 m2 < 1e-3 x 20.1 m).
2. **L1 severity at the producer (`0f060ff`).** `SnappedPanel.ExtendDiagnosticSeverities` (additive, index-aligned with
   `ExtendDiagnostics`) records the severity where each line is produced: `LEVEL_CLIP` and `HOLE_DROPPED` Info; a
   required clip that cannot be made - the extension kept whole, so the wall may stand where the input has none - is an
   **Error** (`SAM_OCCT_EXTEND3D_LEVEL_CLIP_FAILED`, both reasons through one reporting call, the text naming the
   consequence); a made clip that drops a kept extension piece not connected to the wall is a usable, degraded result -
   **Warning**, new code `SAM_OCCT_EXTEND3D_LEVEL_CLIP_PIECE_DROPPED` (that line was a `_FAILED` line in `5600f90`, never
   released), consequence in the text: "the wall may leave an opening there". `SolverReportFormat.FormatExtendPanelDiagnosticRecords`
   pairs each line with that severity (an undefined one maps to Error); `FormatExtendPanelDiagnostics` returns the same
   text. Solve3D adds them through `Modify.AppendExtendPanelDiagnostics`, so records and legacy list stay in step and the
   Error/Warning survive `Summary`, `Standard` and `Detailed`. `Solve3DReport.ExtendPanelDiagnosticRecords` (additive,
   internal set, as `DiagnosticRecords`) is set by the conditioning passes; `SAMOCCT.Extend3D` raises those records as
   runtime messages through `OcctDiagnosticReporter` (Info log only; its `Diagnostics` output unchanged). The extend
   record of an extension whose clip failed carries `ExtendRiskFlag.LevelClipFailed` (appended value), so the ordinary
   `SAM_OCCT_EXTEND3D_PANEL` Info line is followed by a `SAM_OCCT_EXTEND3D_RISKY` line and never reads as a completed
   clip (the `ClipFailed` comment of `5600f90` claimed this without doing it). No public signature, GUID, parameter,
   output, default or legacy text changed; no severity is read from text. `AutoTune3D` surfaces no per-panel extend
   lines at all (pre-existing, `HOLE_DROPPED` included) - not changed here, listed below.
3. **AtBase/AtTop (`b837165`).** Material running to a crossing wall's own base or top was widened to +/- infinity
   ("its own extend moves it onto its cap"). Reproduced at `2ab5316`: a crossing ending at 4.2 under a roof that does not
   cover it, the same crossing clipped by a mezzanine at 5 on its far side, and a crossing hanging from 1.5 over no
   floor - the receiving wall kept the fill to its own cap in each, where no wall meets it. `Crossings` now returns per
   stretch the reach the crossing's own extend is verified to give (`CrossingReach`): the cap it already passes within
   the overshoot, or the cap `ExtendWallToNearestCap`'s rule selects for it plus that extend's overshoot (roof overshoot
   for a thick level cap; the cap plane at the junction for a pitched one), cut at the first level between its extreme
   and that cap whose coverage reaches its line at the junction (its own clip may stop it there); its own extreme when
   no cap covers it. Read from the crossing's geometry before any wall moves (order independence kept); conservative
   (its own continuation/junction exceptions are not followed - a fill it would justify further is left open).
   *Tilted oblique control (finding 7):* a 60-degree crossing tilted 3 degrees, merged and split, positive and negative:
   passes. A tilted crossing taller than it is long gets its leaning side as plan foot (`GetBaseSegment` takes the
   longest edge) - pre-existing, outside the junction calculation, listed below; the control uses a crossing longer than
   tall.

**Failing before / passing after.** The revision-3b test assembly with the pinned `2ab5316` solver DLLs swapped in
(`evidence/2026-10-26-r3b/before-run-2ab5316/`, `runs/before-2ab5316.txt|trx`): 13 of the 93 cases in
`ExtendIntermediateCapTests` + `ExtendLevelClipDiagnosticsTests` fail. Behavioural: `Extend_LongWallWithNarrowSetBack_StaysOpenAtAnyTolerance(1e-3)`
("the 20 mm set-back must stay open"; 1e-6 and 1e-4 pass before too), `Extend_CrossingWallWithNoCapAboveIt_...`,
`Extend_CrossingWallClippedByALevelOnItsOtherSide_...`, `Extend_HangingCrossingWallWithNoFloorBelowIt_...` (fill
invented). The others fail on members that did not exist (`MissingMethodException`: the `out failed` overload,
`ExtendDiagnosticSeverities`, the record adapter, `AppendExtendPanelDiagnostics`), on the renamed `PIECE_DROPPED` code,
and on the `LevelClipFailed` risk name. The failed-clip guard at 1e-3, at `2ab5316`, through the review harness's own
reflection-free path is not reachable; the direct test of it needs the new overload, so its before-state is the
`MissingMethodException` - the 1e-3 drop of a compact original is shown by arithmetic above, not executed.

**Mutation checks** (isolated export of the revision-3b tree, `evidence/2026-10-26-r3b/mutations/`, the same 93 cases;
baseline 93/93): producer Error -> Info: 5 fail; adapter forces Info: 2; Solve3D path forces Info: 1;
`FilterDiagnosticRecords` drops Warnings/Errors: 1; failure not reported (`return false`): 5. Not detected by a test:
removing the one `OcctDiagnosticReporter.Report` call in `SAMOCCTExtend3D.SolveInstance` (no input reaches a failed clip
through the component; the retention argument says Extend's own fill never does) - covered by review only.

**Managed equality** (`evidence/2026-10-26-r3b/fixtures-cond-wip/`, build = this branch's `build`, solver
`3B116050...`): conditioned panels and clean faces of all 8 fixtures identical to `2ab5316` (ordered loops; #20 of
AdjacencyCluster-home identical, outside-mask symmetric difference 0), 12 level clips, 0 failed, 0 dropped. Softhouse
Extend3D alone on the saved Clean3D output: 82 panels, 327 diagnostics identical to `2ab5316`; 81 of 82 panels identical,
wall #77 (x -6.64) has one vertex moved 1e-6 m (y 3.341, z 6.525002 -> 6.525001: the Bed 1/Bed 2 junction fill now ends
at the partition's verified reach, its slab 6.475 + 0.05, instead of the next level's shadow start 6.525001). Attributed:
with only change 3 reverted the panels equal `2ab5316` exactly (`variant-item3-reverted/`). Within the distance
tolerance; classified as numerical.

**Harnesses.** The review harness of `.review-extend3d-revision2b-20261026` (unchanged `Program.cs`, built against this
branch's DLLs, `review-harness-wip.txt`): every line equal to the review's `out-after.txt` (`2ab5316`) except
`L2.longNarrowSetback tol=1e-3`: now open, 182.940020000 m2, one clip (the `ce8e45d` value; `2ab5316` filled it, 183.0).
M2 merged/split/lateral/oblique, positive controls and the AtBase/AtTop rows of the review are unchanged in both
frames. The revision-3b probe (`harness/Probe.cs`, `probe-wip.txt` vs `probe-2ab5316.txt`): the three reproductions
change from filled to open axis-aligned; rotated 30 degrees and translated, the crossing's own cap selection (axis-aligned
plan boxes, the pre-E2 rule) finds the roof/floor for it, so it extends and the fill is rightly kept - the receiving wall
follows the crossing's actual material in both frames; the mezzanine case changes in both frames; L2 open at 1e-6, 1e-4,
1e-3 in both frames.

**Clip #20 (review M1): native resolve-only runs, the two specified in "Revision 3"** (`evidence/2026-10-26-r3b/clip20/`;
`tools-2026-10-26-r3b` = r3 plus naked-point locations in the stage output; build: pinned `ce8e45d-build`, solver
`E5618CB7...`; the resolve path is unchanged since, conditioning off, `InputAlreadyClean`; inputs hashed in
`inputs.sha256`). A = the 41 saved `ce8e45d` conditioned faces; B = the same 41 with only face 20 (= #20) taken from the
`639ef12` set (the other 40 identical, and #20 itself differs only inside the clip region - "Revision 3", "Clip #20"):

| | A (`ce8e45d` #20, clipped) | B (`639ef12` #20, unclipped stub) |
| --- | --- | --- |
| solver cells / volume | 18 / 601.301 m3 | 18 / 601.301 m3, cell list identical |
| solver naked edges | **9**: all on y 4.8, x 3.635..11.535, z 3..4.378 (the ribbon of the integration runs) | **6**: (1.635..3.635, 4.8, 2.95..3) and (6.031..6.036, 2.223, 3) - the `639ef12` integration set |
| output faces | 194 | 188 |
| sew-0.01 rebuild of the output (the golden/probe measurement) | 16 cells: first floor x 3.635..6.036 and 8.135..11.535 missing | 13 cells: ground floor merged into one open shell (-329 m3), first-floor cells present |

A and B reproduce the full-pipeline probes of revision 2 exactly (`evidence/2026-10-26-r2/runs/ach/ach-fix.txt`,
`ach-fix1.txt`: the same rebuilt cells and the same naked points), so the whole 6 -> 9 change is carried by #20's face.

Paired by area and box (1e-4 m): 103 output faces differ only by re-decode noise (<= 1e-6 m); 29 differ materially or have
no partner - all within x -2.865..8.135: the first-floor slab pieces around the x 6.036 partition, the ground-floor walls
there, and three ground-floor walls of the west room (x -2.865, y -1.6, and #20's own ground part). The faces on the
naked ribbon are geometrically identical in A and B: what changes there is whether the native result shares their edges.
**So the remote ribbon and the first-floor room change arise in the native resolve** (same 40 faces, one face swapped,
resolve only); the reconstruction (rebuild) follows the resolve output and is not the source. Not explained: why the
native General Fuse connects #20's upper-storey neighbours 6.5-14.6 m away differently when #20's far bottom corner
changes. The managed side offers nothing more (bit-identical outside the clip region). **Blocker:** native kernel
behaviour. **Minimal next probe** (native, not authorised here): resolve-only C = A with #20's collinear vertex at
(-2.865, 4.8, ~0) removed from the clipped loop (the only vertex of the clip region that is not a corner); if C gives
B's naked set, the resolve is sensitive to that collinear vertex and a managed mitigation (dropping collinear vertices
from the clipped face in `ClipBeyondLevels`) can be tested; otherwise D = B with only the clip's two corners applied.
No reach exemption, no golden re-pin (owner decision 2026-10-26).

*Corrected in revision 3c:* `STAGE=resolve` runs the whole `Panel3DSnapSolver.Execute` after conditioning (healing,
GapFill, `FinalizeAndValidate` with its consolidation rebuild), so these A/B runs do not isolate General Fuse. The
stage trace (below) shows General Fuse / MakerVolume give the same cells for A and B; the first divergence is the
intermediate sewing-based naked-edge measure.

## Revision 3c (2026-10-26; answers `SAM-BIM/.review-extend3d-opus-plan-20261026/REVISION3B-REVIEW.md`, machine-local)

The review of `b837165` (draft PR suitable; merge blocked) found: the #20 cause not isolated to the native resolve
(finding 1), Workflow B 18 -> 8 not reconciled room by room (finding 2), and the component versions not bumped
(finding 3). The owner's task (`REVISION3B-NEXT.md`) authorised bounded native probes, a demonstrated generic correction
and the minor bumps. One code commit, `695e814`.

**1. #20 on the Solve3D path: first divergence = the intermediate sewing measure, not General Fuse**
(`evidence/2026-10-26-r3b/clip20-trace/`). An isolated instrumented export of `b837165` (`C:\Users\michal\AppData\Local\Temp\r3bv\instr`,
hooks in `tools-2026-10-26-r3b/instrumentation/StageDump.cs` + `hooks-on-b837165.patch`, runner `scripts/trace20.sh`) dumps every
stage of `Panel3DSnapSolver.Execute` for A (`ce8e45d` #20, clipped), B (`639ef12` #20) and C (A with #20's collinear
vertex removed):

| stage (`dump-<X>/`) | A | B | C |
| --- | --- | --- | --- |
| R1 input / R2 pre-merge | 41 / 41 | 41 / 41 | 41 / 41 |
| R3 MakerVolume (General Fuse) cells, faces | 18, 166 | 18, 166 - same volumes (1e-6 m3); vertex-equal except #20's corner (x -2.865, +/-1 vertex, 0.05 m: the intended clip) and <= 6e-7 m jitter / loop start | = A |
| R4 post-merge / R5 sew | 145 / not adopted | 145 / not adopted, equal | = A |
| **R6 intermediate naked edges** (`Query.Validate`, sew 0.01, of the flattened cell faces) | **45** (6 wires) | **44** (7 wires), on different walls | 45 |
| H2 GapFill patches -> F1 appended | 17 -> 162 | 16 -> 161 | = A |
| F2 consolidation rebuild (adopted) cells, faces | 18, 194 | 18, 188 | = A |
| F3 final naked (integration measure) | 9 | 6 | 9 |

The R6 measure is **presentation-dependent on the same geometry** (`validate-*.txt`, the R5 face set re-validated as
given / in reverse face order / every loop started one vertex later): A 45 / 48 / 40, B 44 / 68 / 40. C is
geometry-equivalent to A (#20's vertex 4.4e-16 m off the line of its neighbours, area and plane identical, the other 40
faces serialised identically - `variant-C.txt`) and gives A's result at every stage: the collinear vertex is not the
cause, and no collinear cleanup was added. **Cause:** manifold sewing of a non-manifold cell-complex face soup (every
partition listed once per cell) used as the naked-edge measure; its result depends on face order and loop start, and
GapFill and the consolidation rebuild act on it, so a remote change of #20's far corner moves the final naked set.
Correction needs a topology-based free-edge measure (edges with one face in the MakerVolume result) - native/solver
architecture, outside this branch; bounded proposal under "Handover".

**2. Workflow B (Clean3D -> Extend3D -> `Create.AdjacencyCluster`) 18 -> 8 reconciled** (`evidence/2026-10-26-r3b/workflowB/`).
One saved Clean3D output of AdjacencyCluster-home (`clean.json`) extended by the `ce8e45d`, `b837165` (`dll-wip`) and
`695e814` (`dll-fix`) solvers, then built natively with the same code (`Probe.cs build`, as given and in reverse panel
order). The `ce8e45d` and `b837165` extended sets differ **only in #20** (stub 0.675 m2 removed, plus #20's topology):

| extended set (same Clean3D input) | cells as given / reversed | cells lost against `ce8e45d` |
| --- | --- | --- |
| `ce8e45d` (no clip - the false failure) | 18 / 18 | - |
| `b837165` (clip made by re-union) | **8** / 8 | the 10 cells bounded by #20 (y 4.8): ground 136.320, 13.883, 12.437, 65.280 m3; first floor 78.507, 27.354, 75.692, 32.837, 73.633, 9.148 m3 |
| `b837165`, #20 with the slit removed | 17 | 9.148 m3 (x 6.036..8.135, z 3..6.035) |
| `b837165`, #20 with slit and seam hole removed | 18 | none |
| `b837165`, #20 with only the seam hole removed | 8 | as `b837165` |
| `ce8e45d`, #20 seam hole removed | 18 | none |
| **`695e814`** (local cut) | **18 / 18** | none; equal to `ce8e45d` except two ground-cell tops 3.000001 -> 3 m (x 6.036..8.135 and 8.135..11.535) |

Mechanism: the clip's "original" for #20 is the wall after its top extend; it carries a 1 um crack down from its top at
x 6.036 (to z 3.000001) and a 1 um seam hole (the jagged 1 um storey line Clean3D's merge leaves), while the bottom
extend's own outline has neither. `b837165` re-assembled the kept face as Union(original, kept additions), which
brought the crack and the hole back into the clipped wall (`face20-extended-wip.txt`: the slit vertices
(6.036155|6.036156, 4.8, 3.000001..4.377943) and a `HOLE3D`), and the native build lost every room bounded by #20.
`face20-extended-fix.txt` (`695e814`): one loop, 16 vertices, no hole, no slit, area 64.083703 m2. The stub removal itself
loses no enclosure (removing only the stub, without the crack, builds 18): the removed material is invented, as
intended.

**3. Correction (`695e814`, `SnappedPanel.ClipBeyondLevels`).** The clipped face is the extended face minus the removed
(non-sliver) extension pieces - a local cut where the extension is removed - and is no longer re-assembled from the
original and the kept extension pieces. The wall is the largest resulting piece; other non-sliver pieces are reported
as dropped (Warning, as before); lost original material, invalid faces and the `removed` sliver gate are unchanged
(`removed` now excludes slivers before the cut, as the gate already judged them). A separate sliver-hole remover was
tried and withdrawn: SAM already drops such holes in both constructions (that mutation was not detectable).
Acceptance criteria: (a) the clipped face has no crack or seam hole that the extended face does not have; (b) Workflow
B on AdjacencyCluster-home builds 18 cells as given and reversed, equal to the pre-clip build within 1e-6 m; (c) the
invented stub stays removed (`LEVEL_CLIP` 0.675 m2, unchanged); (d) an opening wider than the tolerance is kept;
(e) no fixture-specific branch. Tests: `ClipBeyondLevels_AdjacencyClusterHomeNorthWall_CutsOnlyTheStubWithoutCrackOrSliverHole`
(the captured real inputs of #20's clip; fails at `b837165` - `evidence/2026-10-26-r3b/runs/before-b837165-clipface.txt`) and
`ClipBeyondLevels_OpeningWiderThanTheTolerance_IsKept`.

**Managed impact** (`evidence/2026-10-26-r3b/fixtures-cond-fix/`: conditioning of the 8 fixtures, `cmp-*.txt` and the
boundary-distance comparison `facecmp-2ab5316-vs-fix.txt` against the `2ab5316` dumps; Softhouse Extend3D alone on
the saved Clean3D output, `facecmp-softhouse-wip-vs-fix.txt` against `b837165`). Clip diagnostics identical in every
fixture (12 clips, 0 failed) and in Softhouse (327 lines, 15 clips). Clean faces identical in all 8 fixtures. Every
changed panel is a clipped panel; no unclipped panel changed:

| input | changed / clipped panels | classification |
| --- | --- | --- |
| AdjacencyCluster-home | 1 / 1 (#20) | loop start only (same vertices, same order) |
| Revit-home | 2 / 2 | #4 loop start only; #20 far (bottom) edge 1e-6 m inwards (z -0.529999 -> -0.529998), area -1.4e-5 m2 |
| two-level-tilted (0.21) | 1 / 1 (#180) | 2 collinear seam vertices gone (the original/extension junction at the level), far edge 1e-6 m inwards, area -7.6e-6 m2 |
| whole-level-towers | 7 / 7 | #105, #106, #139 loop start only; #142 2 collinear seam vertices gone (same boundary); #97, #112 2 seam vertices gone + far edge 1e-6 m inwards (area -6.6e-6 m2); #135 4 seam vertices gone, 2 vertices 2.6e-7 m |
| other 4 fixtures | 0 / 0 | identical |
| Softhouse Extend3D | 12 / 14 | 11 loop start only; #77 one collinear vertex added on an existing edge (same boundary) |

The seam vertices are where the old union joined the original and a kept extension; the far-edge moves are the
extended face's own edge (the cut keeps every edge of the extended face other than the cut, as an unclipped wall
does), which lies within the 1e-6 m distance tolerance of the original's. Maximum boundary distance 1e-6 m. Nothing
non-local beyond that tolerance.

**4. Grasshopper versioning** (GH_COMPONENT_STANDARDS GH-F2/H2: a computed-result change is behavioural -> minor):
`SAMOCCT.Extend3D` 0.10.0 -> **0.11.0**, `SAMOCCT.Solve3D` 0.7.1 -> **0.8.0**, `SAMOCCT.AutoTune3D` (Discover) 0.4.0 ->
**0.5.0**, `SAMOCCT.AutoTune3D` (legacy) 0.1.0 -> **0.2.0** (both AutoTune solvers run the managed extend/clip in their
trials). GUIDs, names, parameters, order, access and defaults unchanged (`GHComponentContractTests`,
`Extend3DLevelClipDiagnosticsTests`). **Superseded at the 2026-10-27 merge of `sow/2026-Q4`** (see "Merge of sow/2026-Q4"):
SAM_OCCT#85 had already taken these four numbers for its Clean3D merge guard, so this change now bumps one minor further -
Extend3D **0.12.0**, Solve3D **0.9.0**, AutoTune3D Discover **0.6.0**, AutoTune3D legacy **0.3.0**. Manifest: SAM_OCCT has no approved `design/grasshopper-icons/param_contract.json`
(the standard's validation applies only where one exists), so there is no manifest to regenerate; the icon manifests
(`manifest.csv|json`) carry no version. `param_contract.py` was not run (no Python on this machine).

**5. Final checks on `695e814`** (one run each; `evidence/2026-10-26-r3c/runs/`). Built from the clean `695e814` checkout
with `--no-incremental` through `safebuild.sh` (APPDATA/USERPROFILE redirected; `%APPDATA%\SAM` still the 06:55
deployment, not updated); tests with the real APPDATA and `--no-build`. Solver DLL SHA256: integration/`build`
`A089A655...`, unit bin `2CC2A208...`, Grasshopper bin `D3A731CC...` (`hashes-695e814.txt`, `integ-695e814.head`; builds
are not deterministic). The integration suite was the only OCCT process; an unrelated SAM_OpenStudio test host of
another workspace ran at the same time (timing only).
- Unit **1133 / 1133** (b837165: 1131; +2 new tests, no other identity or outcome change). GrasshopperTests **38 / 38**
  (37; `AutoTune3D_Legacy_Version_Is010` -> `_Is020`, + `AutoTune3D_Discover_Version_Is050`).
- Native integration **24 failed / 472 passed / 2 skipped** of 498, 19 m 25 s (b837165: 25 / 471 / 2). Same 498
  identities. One outcome change: `WorkflowParity_AllWorkflows_ObservedAcrossAllFixtures(Revit-home-panels)` **now
  passes**. Failure messages identical (`integ-695e814.msg.txt` vs `msg-b837165.txt`) except two golden actuals:
  two-level-tilted managed 0.21 cells 17 -> **16** (pinned 9; base `22e4fd0` 16) and whole-level-towers managed
  23 -> **22** (pinned 21).

Captured output, masked (`stdout-b837165-vs-695e814.diff`): **16 tests differ - 11 passing, 5 failing** (outcomes from
`integ-695e814.trx`; each of the 5 also failed at `b837165`, none is a new failure; corrected after the final
independent review, `.review-extend3d-final-20261026/REVIEW.md` R1, `changed-output-status.json`). The run-level
output differs accordingly.

| test | outcome (`b837165` -> `695e814`) | change | classification |
| --- | --- | --- | --- |
| WorkflowParity AdjacencyCluster-home | **Failed -> Failed**: A-solver-matched **16 spaces vs 18 expected** (unchanged) | workflow B **8 -> 18 spaces** (56 -> 112 panels, 13 -> 49 adjacencies) - the `76f6f22` values; the `LEVEL_CLIP` extent of #20 now the stub only ((-3.09, -0.05)-(-2.865, 2.95) instead of up to (8.135, 3)); new Info `HOLE_DROPPED: 1 of 1` on #20 | workflow B: **intended** (item 2); the dropped opening is #20's 1e-6 m x 2.1 m seam sliver, carried silently by the old union. Restoring workflow B does **not** make this test pass |
| same test, workflow A (Solve3D) | (same test) | still 18 cells / 9 naked; 211 -> 195 resolved panels, 17 -> 13 air panels, consolidation alternative 20 -> 56 naked; A-old 130 -> 125 panels, relation parity WARN (192/194) -> clean (182/182); A-matched still 16 spaces, relation parity 168/170 -> 159/159 | presentation: the only conditioning change is #20's loop start; resolve-only on the two saved face sets, same build: 194 vs 182 output faces, 18 cells / 601.301 m3 / 9 naked both (`presentation/res-ach-*`). Relation parity is a different measure from the room count the test asserts (16 vs 18 still fails) |
| WorkflowParity Revit-home | **Failed -> Passed** | A-matched 5 -> **7 spaces** (= the solver's 7 cells); solver 51 -> 57 resolved panels, 8 -> 4 naked | presentation: resolve-only, swapping in only #4 (loop start only) reproduces the whole change (46 -> 55 faces, 9 -> 4 naked); only #20 (1e-6 m) also changes it (52, 4); 7 cells / 158.222 m3 in all four (`presentation/res-rh-*`) |
| GoldenMaster managed whole-level-towers | **Failed -> Failed**: expected 21, actual 23 -> **22** | sew-rebuild 8880.3 -> 8613.4 m3 | measurement: resolve-only on the 2ab5316 vs fix faces gives the **same solver result** (24 cells, 9039.786 m3, 9 naked, 222 faces; all 222 faces pair within 4.3e-8 m, 14 with another loop start) and the golden's sew-0.01 rebuild 23 vs 22 (the 159.373 m3 cell at x 31.5..37.6 becomes part of an open shell x 31.5..46.0); swapping only the three loop-start-only faces moves the solver's naked count 9 -> 13 (`presentation/res-tw-*`, `cmp-tw-resolved.txt`) |
| GoldenMaster managed 0.21 two-level-tilted | **Failed -> Failed**: expected 9, actual 17 -> **16** | 350 -> 416 faces, 2869.7 -> 2707.7 m3 | **inference**, not traced: same class (only #180 changed: seam vertices, far edge 1e-6 m) |
| GoldenMaster managed 0.21 whole-level-towers | **Failed -> Failed**: expected 25, actual **27** (unchanged) | volume 9400.474569530 -> 9400.474569391 m3 | the volume delta is numerical; the cell-count failure is pre-existing and unchanged |
| NineSpaceWallCapture capture 0.45, rotate Z 45 | **Failed -> Failed** (pre-existing North0 / North2 geometry comparison, message unchanged) | cell numbering South2 / West2 swapped | order only |
| ParameterDiscovery NineSpace / Towers | Passed -> Passed | trial rows change (e.g. towers best 35 -> 36 cells, baseline naked 11 -> 21; nine-space best faces 121 -> 115; same best configurations) | **inference** from the nine-space conditioning check (bbl 0 and 0.21: only loop starts and collinear vertices change, 8 / 1 faces, boundary distance 0, clip diagnostics identical - `ninespace/`); trial rows not separately traced |
| Extend3DCensus whole-level-towers | Passed -> Passed | fast-path / plane-ops 182 / 38 -> 183 / 37 | **inference**, not traced: a clipped wall without its seam vertices is a plain rectangle (towers #135: 8 -> 4 vertices) and takes the fast path in a later pass |
| ControlledWorkflow NineSpaces, GHParity MergeCoplanarFalse, EastSouthGap, FullFixture FillMargin1 AllNineMatch, Towers DoubleWallGap | Passed -> Passed | cell numbering swapped (same cells, volumes and matches); ControlledWorkflow 74 -> 73 rebuilt panels | order only |
| Towers FullUserChain / BucketAlignSweep | Passed -> Passed | a cell centre 1e-6 m | numerical |

**Conclusion.** Every managed change of `695e814` is confined to clipped faces and is geometry-neutral there (loop
start, collinear vertices, <= 1e-6 m), apart from #20 on the Extend3D path, where the crack is removed as intended.
Workflow B on AdjacencyCluster-home is restored to 18 rooms; the enclosing WorkflowParity test still fails on its
A-solver-matched room count (16 vs 18), as before. The native-measured swings elsewhere - both directions: Revit-home
parity now passes, AdjacencyCluster-home relation parity clean, towers baseline naked 11 -> 21 - follow from the
solver's dependence on face presentation (item 1), shown directly on AdjacencyCluster-home, Revit-home and towers and
inferred for the others. They are not caused by geometry this branch changes, and they are not repaired here. Failing
tests among the changed outputs: 5 (all failing before as well). No golden re-pin.

## Merge of sow/2026-Q4 (2026-10-27)

GitHub reported PR #84 conflicting with `sow/2026-Q4` at `1e39868`. That head adds SAM_OCCT#85, the Clean3D coplanar-merge
coverage guard (`SnapStage.Clean` step 3a), on top of `22e4fd0`. Merged `1e39868` into the branch; no rebase.

- **Conflicts:** two, both version-assertion comments (`DiagnosticsContractTests`, `GHComponentContractTests`). #85 had
  bumped the same components to the same numbers: Extend3D 0.11.0, Solve3D 0.8.0, AutoTune3D Discover 0.5.0, legacy 0.2.0.
- **Versions:** each version shown must mark one computed result, and the update offer must appear after either change
  (GH-H3). So this change bumps one minor further: Extend3D **0.12.0**, Solve3D **0.9.0**, Discover **0.6.0**, legacy
  **0.3.0**. Clean3D stays at #85's 0.7.0, because the extend/clip does not run there.
- **Test updates:** the version tests now assert the new numbers, with both steps commented. The expected versions in
  #85's `CoplanarMergeGuardVersionTests` move to the current ones. The bump is the owner's to review (GH-H2: "chosen by
  the impact, and the reviewing owner").
- **Code overlap:** the two changes touch different code (#85: `SnapStage.cs`, `DiagnosticCode.cs`; this branch: the
  extend/clip in `Panel3DSnapSolver.cs` / `SnappedPanel.cs`). They meet only in computed results: Extend3D (unless
  `inputAlreadyClean`), Solve3D and AutoTune3D run both stages.
- **Local results on the merged tree:** unit 1147 / 1147 (1133 + #85's 14) and GrasshopperTests 43 / 43 (38 + #85's 5),
  both built through `safebuild.sh --no-incremental`.
- **Not re-run:**
  - **The native integration suite.** The 24 / 472 / 2 result and the 16 changed-output classifications belong to
    `695e814` without #85. #85's guard changes the conditioned panels, so the native counts on the merged head are
    unmeasured.
  - The Softhouse chain, as before.

## Review findings (status at `695e814`)

Review of revision 3b (`.review-extend3d-opus-plan-20261026/REVISION3B-REVIEW.md`):

| id | finding | status | evidence |
| --- | --- | --- | --- |
| 3b-R1 | #20 cause not isolated to the native resolve (A/B ran healing, GapFill and the consolidation rebuild) | **corrected**: stage trace; General Fuse / MakerVolume equal for A and B; first divergence = the intermediate sewing naked-edge measure, presentation-dependent on the same geometry; collinear vertex excluded (variant C). The order-dependent measure itself is **open** (native/solver proposal) | "Revision 3c" item 1 |
| 3b-R2 | Workflow B 18 -> 8 not reconciled | **reconciled and fixed** `695e814`: the clip's re-union re-opened a 1 um crack in #20; local cut -> 18 cells, equal to the pre-clip build within 1e-6 m; regression on the captured real inputs | items 2, 3 |
| 3b-R3 | component versions not bumped | **done** `695e814`: Extend3D 0.11.0, Solve3D 0.8.0, AutoTune3D Discover 0.5.0, AutoTune3D 0.2.0; GUIDs/parameters kept; no approved `param_contract.json` in SAM_OCCT | item 4 |

Revision-3b review (`.review-extend3d-revision2b-20261026`):

| id | finding | status | evidence |
| --- | --- | --- | --- |
| 2b-1 | failed clip reported as Info, never reaches the user | **fixed** `0f060ff`: Error at the producer (dropped piece: Warning), kept through the adapter, Solve3D and the Extend3D runtime messages at every detail level; extend record marked | "Revision 3b" item 2, mutation checks |
| 2b-2 | L2 allowance = tolerance x whole perimeter | **fixed** `2986ad5`: per-piece mean width; 30 m / 20 mm open at 1e-6, 1e-4, 1e-3 (1e-3 fails at `2ab5316`) | item 1 |
| 2b-3 | committed record stale | **fixed** by the record commit after `b837165` | this record |
| 2b-4 | no native run on revision 3 | **run** on `b837165` | "Validation" (revision 3b) |
| 2b-5 | AtBase/AtTop assumes the crossing's own extend | **fixed** `b837165`: verified reach; three reproductions fail at `2ab5316` | item 3 |
| 2b-6 | an opening at the junction depends on the crossing's representation (pieces close their door on extend) | pre-existing, **tracked separately** ("Separate") | review finding 6 |
| 2b-7 | no test of the failed-clip returns; no tilted oblique crossing | **closed**: real failed-clip tests (both reasons share one reporting call; the invalid-face reason is not constructible - `IsValid` fails only on NaN or < 3 points); tilted 60-degree control | items 2, 3 |

Revision-3 review (opus plan), status as committed at `2ab5316`, updated where revision 3b changed it:

| id | finding | status | evidence |
| --- | --- | --- | --- |
| M1 | clip #20's effect is remote (naked ribbon x 3.6..11.5 on #20, first-floor cells missing/merged) | managed side localised (#20 changes only inside the clip region); revision 3c: not General Fuse - the solver's intermediate sewing naked-edge measure (order/loop-start dependent) diverges first, GapFill and the rebuild follow; collinear vertex excluded. Measure **open** (bounded native proposal) | "Revision 3c" item 1 |
| M2 | junction/alignment evidence from the crossing wall's whole extent | **fixed** `33ea9bb`, 18 regressions (5 fail before) | table above |
| M3 a-d | record errors (ambiguous rooms, reference volumes, Bed 11, M1 location) | **corrected** in this record | "Corrected diagnostics", item 4 |
| L1 | no containment; crash regression not shown to reproduce | **reproduced** on the real input (3-opening ground slab, not the 7-opening one); real-geometry regression fails at `696dce0`; exceptions propagate (policy kept, no catch-and-skip); non-exception clip failures reported - since `0f060ff` as an Error (dropped piece: Warning) | "L1 reproduction", "Revision 3b" |
| L2 | dropped extension pieces unchecked | **assessed and corrected**: retention argued; dropped pieces and clip failures reported; the false failure (area vs distance tolerance) fixed in `2ab5316` and its whole-wall allowance replaced by the per-piece width in `2986ad5`; SAM `Difference` null ambiguity remains (bounded, SAM side) | "L2 assessment", "Revision 3b" |
| L3 | Softhouse `fix` run not tied to a binary | **partly closed**: its saved Extend3D output equals a pinned-`ce8e45d` managed rerun (82/82); the native stage and room tables remain untied | "Softhouse" provenance note |

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
- Revision 3 adds 21 cases to `ExtendIntermediateCapTests.cs` (74 in the class): see the table in "Revision 3". Unit
  suite 1112 / 1112 at `2ab5316`.
- Revision 3b: `ExtendIntermediateCapTests.cs` + 13 cases (87; 30 m / 20 mm set-back at three tolerances, the failed-clip
  guard at two, the AtBase/AtTop reproductions and control, the tilted oblique crossing x4; the dropped-piece test now
  expects `PIECE_DROPPED`); new `ExtendLevelClipDiagnosticsTests.cs` (6: producer severities, adapter, Solve3D path at
  every detail level with the runtime messages, risk line); new `Testing/SAM.OCCT.GrasshopperTests/Extend3DLevelClipDiagnosticsTests.cs`
  (4: Extend3D GUID/version/outputs unchanged, failed clip an Error message at each detail level). Unit suite 1131 / 1131,
  GrasshopperTests 37 / 37 at `b837165`.
- Revision 3c (`695e814`): `ExtendIntermediateCapTests.cs` + 2 (`ClipBeyondLevels_AdjacencyClusterHomeNorthWall_CutsOnlyTheStubWithoutCrackOrSliverHole`
  on #20's captured clip inputs - fails at `b837165`; `ClipBeyondLevels_OpeningWiderThanTheTolerance_IsKept`);
  GrasshopperTests: version assertions 0.11.0 / 0.8.0, `AutoTune3D_Legacy_Version_Is010` renamed `..._Is020`, new
  `AutoTune3D_Discover_Version_Is050`; the diagnostics contract comments updated. Unit suite 1133 / 1133,
  GrasshopperTests 38 / 38.
- Merge of `sow/2026-Q4` (2026-10-27): version assertions 0.12.0 / 0.9.0 / 0.6.0 / 0.3.0 (`..._Is030`, `..._Is060`, #85's
  `CoplanarMergeGuardVersionTests` expected versions moved to the current ones, Clean3D 0.7.0 unchanged).

## Validation (local, machine `michal`; identical conditions; base = `SAM_OCCT-wt-extend3d-base` at `22e4fd0` + new tests)

Built with APPDATA/USERPROFILE redirected (GH PostBuild deploys to `%APPDATA%\SAM`); tests with the real APPDATA,
`--no-build`, one suite at a time. SAM from `..\SAM\build` (`sow/2026-Q4` `fe5bbce4`).

```powershell
dotnet build Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug
dotnet build Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug --no-build
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug --no-build
```

Tested content: `696dce0` (table below), `76f6f22` (code = `ce8e45d`; identical results, see "Re-run on `76f6f22`"),
`b837165` (revision 3b, "Re-run on `b837165`" below) and **`695e814`** (revision 3c, "Revision 3c" item 5). Revision 3 (`2ab5316`) was validated managed-only; its
statement that the conditioned faces of every fixture equal `ce8e45d` holds for the Solve3D conditioning stage only - on
the Clean3D -> Extend3D path AdjacencyCluster-home differs (#20, below).

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
Not run: GrasshopperTests, hosted CI (nothing pushed).

**Re-run on `76f6f22` (owner 2026-10-26).** `76f6f22` = `ce8e45d` (side-coverage robustness, see "Softhouse") plus one
record-only commit (`git diff --name-only ce8e45d 76f6f22` lists only this record). Same configuration as the `696dce0` run:
built with APPDATA/USERPROFILE redirected, tests with the real APPDATA, `--no-build`, no other native process running,
`%APPDATA%\SAM` unchanged (deployment of 06:55). Commands as above, with
`--logger "trx;LogFileName=integ-76f6f22.trx"`. Results: unit 1091/1091; integration 25 failed / 471 passed / 2 skipped.
Against the `696dce0` run: the same 498 test identities, no outcome changes, the same 25 failing tests with identical
failure messages, and the captured output of every test identical once fresh GUIDs and timings are masked (raw
differences: 3293 lines in 19 tests, all GUIDs or timings). The side-coverage change had no observable effect on the
native suite; no new failure or geometry change to investigate. The table above therefore also holds for `76f6f22`.
Evidence: `evidence/2026-10-26-r2/runs/integ-76f6f22.log|trx`, `msg-76f6f22.txt`, `nonpass-76f6f22.txt`.

**Re-run on `b837165` (revision 3b; final native suite, authorised by the owner's revision-3b task).** Built from the
`b837165` checkout with APPDATA/USERPROFILE redirected (solver `82E27CEC...`, `runs/integ-b837165.head`); unit and
Grasshopper suites rebuilt and re-run on the same binaries (1131/1131, 37/37, `unit-b837165.trx`, `gh-b837165.trx`);
integration with the real APPDATA, `--no-build`, the only native process; `%APPDATA%\SAM` unchanged (06:55 deployment).
Result: **25 failed / 471 passed / 2 skipped of 498, 16 m 53 s.** Against `76f6f22`: the same 498 test identities and
outcomes, the same 25 failing tests with identical failure messages (`msg-b837165.txt`, `nonpass-b837165.txt`, from the
log by `tools-2026-10-26-r3b/scripts/msgs.sh`, which reproduces the saved `76f6f22` files exactly). Captured output with
GUIDs, timings and temporary file names masked (`stdout-76f6f22-vs-b837165.diff`): one material change, in a passing
test - `WorkflowParity_AllWorkflows_ObservedAcrossAllFixtures(AdjacencyCluster-home)`, workflow B (Clean3D -> Extend3D ->
`Create.AdjacencyCluster`): 18 spaces / 112 panels -> **8 spaces / 56 panels**, verdict "both workflows diverge from solver
cell count"; the extend log gains `LEVEL_CLIP: panel (#20) ... 0.675 m2 removed`. Classified: **#20 on the Extend3D path,
from `2ab5316`, not revision 3b.** The managed chain (`harness/Probe.cs chain`, `chain-ach-<sha>.txt`) gives on that path:
`ce8e45d` and `33ea9bb` no clip (923.56749 m2 - the clip judged the original 1e-6 m2 "lost" and returned silently, the
false failure that `5600f90` exposed), `5600f90` `LEVEL_CLIP_FAILED` (the same, reported), `2ab5316`, `b837165` and
`b837165` with change 3 reverted the clip made, identical panels (922.89249 m2). So since `2ab5316` the Extend3D path
removes the same #20 stub the Solve3D path has removed since revision 2, and the room change it causes downstream belongs
to the open #20 item. The two skips are the native-missing and sew-guard tests, as before. No other test's output
changed (all other `[extend]` lines of the parity fixtures identical). The 25 failures are therefore the baseline
explained in the table above (revision 2 column), value for value: those not listed there fail as at base `22e4fd0`;
AdjacencyCluster-home naked 9 (#20; revision 3c: the solver's intermediate sewing measure, not General Fuse); Revit-home solver cells 7 (trace
item 2); the towers / two-level-tilted managed goldens (duplicate-face
rebuild measurement, parked; no re-baseline); the NineSpaceWallCapture West2/West3 deltas (< 0.005 m3, pre-existing
failure reason). None is repaired or re-pinned here.

## Softhouse (owner request 2026-10-26: same inputs and settings as the 70 -> 52 comparison)

Chain (machine-local tool `pipe`, unchanged since the 2026-10-26 stage comparison): PanelsBySolids (minThickness 0.04,
continuation 0.01) -> SnapToJunctions 0.3 -> Clean3D (bbl 0.21) -> Extend3D (inputAlreadyClean, fillMargin 0.5, bbl 0.21,
directionalCapGrow) -> `Create.AdjacencyCluster` (sew 0.01, fuzzy MacroDistance) -> apertures; then the reference-room
leak report (`LEAK=1`, reference `SAM_daily/2026-10-24-OCCT-models/000000/softhouse3.xml`, 70 rooms). SAM side: worktree
`SAM-wt-junctions` at `fe5bbce4` (SAM `sow/2026-Q4`, no changes). Inputs: `handover/inputs/solids_panels_392.3dm`,
`windows_201.3dm`. One native process at a time. Command (Git Bash; `tools/softhouse.sh <out> <base|fix1|fix>`):

```sh
env SAMDIRS="<SAM-BIM>\SAM-wt-junctions\build;<SAM_OCCT build>;%APPDATA%\SAM" CONT=0.01 MINTHK=0.04 CHAIN=1 pipe.exe solids_panels_392.3dm windows_201.3dm <out>
env SAMDIRS=... LEAK=1 GBXML=<softhouse3.xml> pipe.exe solids_panels_392.3dm windows_201.3dm <out>
```

Revision 2 at `696dce0` crashed in Extend3D (`SideCoverage`): a plan face built with several cap openings at once has
them unioned by SAM `Face2D.Create`, which left a null internal edge (`FixEdges` threw). `ce8e45d` subtracts openings one
at a time. *Corrected in revision 3 (review L1):* the slab was named here as the one with 7 openings without a
reproduction; the reduced reproduction on the saved Clean3D output isolates the **ground-floor slab with 3 openings**
(z 0.025) with walls 7 and 9 - the 7- and 5-opening slabs do not throw with them - and the two-opening unit test of
`ce8e45d` passes at `696dce0`, so it was not a reproduction. See "Revision 3". Results below are `ce8e45d`. Base and revision 1 reproduce the 2026-10-26 runs exactly (leak reports identical to `base2` / `fix4`).

| | base `22e4fd0` | revision 1 `639ef12` | revision 2 `ce8e45d` |
| --- | --- | --- | --- |
| spaces / hosted apertures | 70 / 96 | 52 / 83 | 54 / 84 |
| v1 leak report (superseded, see "Corrected diagnostics"): rooms alone in a space, no leak / sharing / in no space | 48 / 18 / 1 | 42 / 21 / 7 | 43 / 21 / 6 |
| **corrected (v2): reference rooms good** - clear match, space closed, volume finite and positive, alone, no leak | **48** | **45** | **46** |
| corrected (v2): spaces closed with a valid volume | 64 of 70 | 47 of 52 | 49 of 54 |
| naked edges of the extended panel set (same options, one build) | 134 | 177 | 178 |

The 70 -> 52 change persists (70 -> 54). Material room changes, base -> revision 2 (revision 2 = revision 1 except Bed 1):

- **Houses A-C ground floors** (Dining 2/3/4, Kitchen 2/3/4, Circulation 2/3/4 - 9 reference rooms by the corrected
  matching): separate in base (several leaking), one open space (19 naked segments, volume undefined, leak 3.5-21.4 %)
  in revisions 1 and 2. Base separated them with walls that exist only upstairs; without them the masked defects show:
  Clean3D kink jog and extraction junction blocks (Separate). Unchanged from revision 1.
- **House D** Kitchen 1 (both reference entries), Bed 2 D203, Circulation 9 D206, Bed 7 D303: in no space in revisions 1
  and 2 (base enclosed them, Kitchen 1 with 10.6/33.1 % leak). The ground-floor corner slot and the first/second-floor
  facade hole at x -6.64, y 3.179..3.827 (extraction) are open once the invented walls are gone. Unchanged from revision 1.
- **Bed 1 D204** (house D first floor): in no space in revision 1, enclosed again in revision 2 (39.32 m3, as base, no
  leak). Wall #77 (x -6.64) stops at y 3.179 on the first floor (the facade hole) and the Bed 1/Bed 2 partition crosses
  its line at y 3.341 on that storey: the junction rule keeps the fill of that 0.162 m stretch (crossing wall present,
  within the 0.4 m reach). Bed 2's part of the hole (y 3.341..3.827, 0.486 m to the next wall) is beyond the reach and
  stays open; revision 1 measured the whole 0.648 m free stretch and kept nothing.
- **Bath 7 B302** 43.39 -> 62.76 m3: base separated it from a 19.37 m3 region with no reference room by the wall at
  y 6.013 grown through the second-floor slab over x 4.92..10.73 (M2 notch fill); the input has that wall on the first
  floor only there. Bed 4 C203 37.99 -> 40.83 m3 likewise absorbs a 2.84 m3 base sliver space. Unchanged from revision 1.
- Revision 2 also removes a 2 mm stray column of wall #77 that revision 1 kept below the first floor (y -0.683..-0.681)
  and adds one 0.01 m3 sliver space at z 4.975 (x 4.9, y -2.71; revision 1 already had one at x 10.7).

Evidence: `evidence/2026-10-26-r2/softhouse/` (`base`, `fix1`, `fix`, `fix-crash`: chain/leak logs, per-room `status.tsv`,
spaces, extended panel sets, models, naked-edge lists, `notalone.txt` per build).

*Provenance (review L3).* The `fix` and `fix-crash` outputs are timestamped 13:20:19, before the `ce8e45d` commit (13:20:35)
and the 13:25 DLL; no DLL hash was recorded for them. Revision 3 ties the Extend3D stage: a managed rerun of
`Modify.Extend3D` with the pinned `ce8e45d` DLL (`E5618CB7...`) on the saved `fix/panels_clean.json` gives extended panels
identical to the saved `fix/panels_extended.json` (82 of 82, ordered loops). The native AdjacencyCluster stage and the
room tables of that run are not re-derived (a chain run, not authorised here).

### Corrected diagnostics (v2, 2026-10-26; supersedes the v1 per-room review)

The v1 leak report (`Leak.cs`) mixed four things and had two defects: it matched each reference room by one probe point
(the floor-vertex average at mid-height, which lies outside L-shaped rooms), and it meshed panels from their outer edge
only. v2 (`Rooms2.cs`, isolated copy; run on the saved models, no chain re-run) reports separately:

1. **matching** - points sampled inside each reference room's own shell (0.4 m grid, three heights; 0.15 m for small
   rooms); the share of them in each model space; "clear" only when one space holds >= 80 % and no sample lies in two
   spaces. *Corrected (review M3a):* base has three AMBIGUOUS rooms - Dining 1 D107 (10 % in Cell 43, 408 of 453 samples
   outside every space), Circulation 1 D103 (66 % Cell 60) and Bath 7 B302 (63 % Cell 53) (`softhouse-v2/base/v2_rooms.tsv`);
   revisions 1 and 2 have none. The
   v1 probe lay outside its own room for Circulation 1/2/3/4 D/C/B/A103 and Circulation 8 A301: v1 paired Circulation 1/2/4
   with the 6.2 m3 "Unoccupied and Unconditioned" spaces, but their samples lie entirely in the houses A-C ground-floor
   space (Circulation 2, 4) or in no space (Circulation 1, house D); Circulation 8's samples lie in the 36.17 m3 space v1
   listed as matching no reference room. Flag: 13 reference rooms' own gbXML shells give no valid SAM volume (Dining
   2/3/4, Kitchen 1-4 ground floor, Carpark 1/2, Circulation 1/9, Bed 1 D204, Bath 4 A202); their samples (84-486 each)
   rely on point-in-shell tests on those shells.
2. **closure** - naked segments of each model space's shell (MacroDistance).
3. **volume** - each space's shell volume finite and positive. Closure and volume fail together: base 6, revision 1 5,
   revision 2 5 spaces (all with volume NaN).
4. **leakage** - rays from up to 6 samples (250 each) through the panels meshed with their internal openings. Finding:
   the saved extended panel sets and models contain **no internal openings** (`InternalEdge2DLoops`: Clean3D output 3
   panels, Extend3D output 0, model 0 - in base too), so v2 and v1 meshing are identical here; the Clean3D slab openings
   (e.g. the first-floor slab's 7) are lost inside Extend3D without a `HOLE_DROPPED` diagnostic (pre-existing, not traced).

*Reference volumes withdrawn (review M3b):* the `refVolume` column of `v2_rooms.tsv` and the "(ref ... m3)" figures in
`notgood.txt` are SAM volumes of the gbXML shells and are unreliable even when finite (the reviewer's divergence-theorem
estimates from the gbXML loops: Bed 10 C305 2.709 vs 84.8, Bath 7 B302 196.9 vs 54.6, Bed 11 B304 13.05 vs 101.5, Toilet 3
B102 47.5 vs 7.07 m3 - reviewer's estimates, not repeated here). No conclusion in this record rests on them any more.

**Bed 11 B304** (v1: alone, 0 % leak, in every build) - *corrected (review M3c)*: not a false enclosure of a small room.
Its samples lie 100 % in Cell 5, which spans x 4.916..10.726, y -2.887..4.892 (`v2_spaces.tsv`); the reference room spans
about x 4.966..10.676, y -2.662..4.842 (reviewer's reading of the gbXML, about 35.6 m2 / 101.5 m3, not repeated here):
the right room, offset by half a wall. It is still not good: the shell is open (16 naked segments, 42.9 m) with no valid
volume, and the ray test does not detect those gaps (rays meet panels inside the room's box). Same in base, revision 1
and 2 (pre-existing).

Revision 2's 24 reference rooms not good (by reason):
- no space (7): Dining 1 D107, Circulation 1 D103, Kitchen 1 D105-6 (ground and first floor), Bed 2 D203, Circulation 9
  D206, Bed 7 D303 - all house D (corner slot and facade hole);
- the open houses A-C ground-floor space (9): Dining 2/3/4, Kitchen 2/3/4, Circulation 2/3/4;
- open spaces holding two reference rooms (4): Dining 5 A206 + Kitchen 4 A105-6 (first floor), Bed 5 B204 + Kitchen 3
  B105-6 (first floor) - pre-existing;
- closed space holding two rooms (2): Bed 3 C205 + Circulation 10 C206 (133.89 m3) - pre-existing;
- open single-room spaces (2): Bed 11 B304, Bed 13 A304 - pre-existing.
"Good" is this saved diagnostic classification (clear match, closed, finite positive volume, alone, no ray leak), not
engineering acceptance: closure of the 13 rooms whose gbXML shells have no valid SAM volume rests on a parity point test
and a divergence-volume estimate by the reviewer; a boundary-coverage check separating T-junction subdivision from real
gaps was not run, and the slab openings are missing in every build (below).
Against base: Bath 7 B302 becomes good (base split it with the invented wall at y 6.013); Bed 2 D203, Bed 7 D303 and
Dining 4 A107 become not good (Dining 2/3 already leaked in base). Revision 1 -> 2: Bed 1 D204 becomes good. Spaces
without a reference room in revision 2: two 0.01 m3 slivers (base: five slivers of 0.007-0.024 m3 and the 2.84, 19.37,
19.53 and 23.0 m3 pieces). All new losses trace to the defects the invented walls had masked (Clean3D kink jog, extraction junction
blocks, house D corner slot and facade hole) - owned by the separate Clean3D and SAM extraction tasks.

Evidence: `evidence/2026-10-26-r2/softhouse-v2/<build>/` (`v2_rooms.tsv`, `v2_spaces.tsv`, `notgood.txt`, `run.log`),
`softhouse-v2/internal-openings.txt`; inputs `softhouse/<build>/{model,panels_extended,panels_clean}.json` (unchanged);
v1 outputs kept in `softhouse/`. Tool: `handover/tools-2026-10-26-r2/` (`scripts/rooms2.sh <savedDir> <out>`), verified to
reproduce the v2 tables exactly.

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

4. **AdjacencyCluster-home naked 6 -> 9 (owner 2026-10-26: keep clip #20 open provisionally, no reach exemption;
   closure unresolved)** - follows the single conditioning difference from revision 1: clip `#20` removes a 0.675 m2
   stub below the first floor at the north wall's end (y 4.8, x -3.09..-2.865, z -0.05..2.95), where the upper storey
   overhangs the ground-floor corner wall (x -2.865) and no wall crosses the line below the floor there. Revision 1 kept
   it by the reach rule the review rejected. The solver's own cells are identical (18). *Corrected (review M1 / M3d):*
   the effect is **not** at that corner. All 9 naked points of revision 2 lie on the north wall itself (#20, a 14.6 m face
   x -3.09..11.535, top 4.378) at y 4.8, x 3.635..11.535, z 3.0..4.378 (`runs/ach/ach-fix.txt`), 6.5-14.6 m from the
   clip. In the rebuilt measurement the first-floor cells next to that ribbon, x 3.635..6.036 (32.83 m3) and x 8.135..
   11.535 (73.63 m3), are missing (`ach-fix1.txt` vs `ach-fix.txt`); in the duplicate-free rebuild the first-floor cells
   75.69 + 32.85 m3 merge into 108.53 m3 (`dd-fix1.txt` vs `dd-fix.txt`): a change of first-floor room identity. The
   managed conditioning does not explain it - #20 changes only inside the clip region and every other conditioned face is
   bit-identical (revision 3, "Clip #20"); the remote change arises after conditioning (revision 3b: resolve-only on
   the saved faces with only #20 swapped reproduces 9 <-> 6 naked and the rebuilt cells exactly) - revision 3c locates
   it in the solver's intermediate sewing naked-edge measure, not General Fuse (MakerVolume cells equal). Workflow B
   showed the stub itself is invented material: removing it loses no enclosure once the clip no longer re-opens a crack
   (revision 3c item 2). The clip is kept (the owner's provisional decision, now supported by that evidence); no reach
   exemption was added.

So the golden-master, plane-target and parity counts on these fixtures measure the rebuild of an output with coincident
duplicates, not Extend3D's geometry alone; no re-baseline is proposed on them.

## Separate (not on this branch)

Tracked separately, pre-existing, not changed by this branch (revision 3b):
- **Split-piece opening loss at a junction** (review 2b finding 6): a crossing wall with a door at the junction gives a
  gap beside the door (36.30 m2) as one face with a hole, but none (36.60 m2) as four pieces, because the crossing's own
  extend closes its door when it is represented as pieces (`ce8e45d` and `2ab5316` alike). The receiving wall follows
  the crossing's post-extend material in both. Owner: a separate issue on extending pieces.
- `SnappedPanel.GetBaseSegment` takes a wall's longest edge as its plan direction, so a tilted wall taller than it is
  long gets its leaning side as plan foot (found building the tilted oblique control; affects `Crossings` and the
  plan-loop extend for such walls).
- `AutoTune3D` surfaces no per-panel extend diagnostics (`HOLE_DROPPED`, level clips) - a failed clip there stays in the
  panel's `ExtendDiagnostics` only.
- Clean3D kink candidate: `clean3d-kink-jog-candidate.patch` (machine-local evidence folder of 2026-10-26).
- SAM extraction fix (junction blocks shorter than the wall thickness): SAM worktree `SAM-wt-junctions`, empty.
- Solver output with exact duplicate faces (item 1 above): traced in its own session to the per-cell face listing of the
  adopted consolidation rebuild in `FinalizeAndValidate`. The dedup (local branch `fix/solver-output-duplicate-faces-q4`,
  commits `1978171`, `ac47976`, `8afa30e`) makes sew-based rebuilds worse (whole-level-flat managed golden 22 -> 13, both
  #82 towers pins merge the west room). Owner 2026-10-26: **parked**, commits and evidence kept, no PR. Rebuilding the
  output directly (MakerVolume) is a candidate measurement correction; its 18-case evidence (identical under order and
  noise variants at base) is kept for independent review before any change to what the tests pin.

## Evidence (machine-local, copy before switching machines)

`SAM_daily/2026-10-25-Softthouse/handover/evidence/2026-10-26-r2/`: `runs/` (integration logs and trx, probe outputs:
`tlt/` two-level-tilted, `rh/` Revit-home, `fx/` and `towers/` whole-level-towers, `ach/` AdjacencyCluster-home),
`tools/pipe` (probe sources), `tools/stage.sh`, `tools/safebuild.sh`, `reviewed-dll/` (the reviewed revision-1 solver
DLL, SHA256 `E63A58A3...DF4`), `softhouse/`, `softhouse-v2/`.

`SAM_daily/2026-10-25-Softthouse/handover/evidence/2026-10-26-r3/` (revision 3; index `README.md`):
- `pinned/<sha>-build/`: the OCCT build folders used, `HASHES.txt`. Solver DLL SHA256: `639ef12` `7B6F982C...`,
  `696dce0` `BB1C1A82...` (built from `git archive 696dce0` in `SAM-BIM/.extend3d-r3-696dce0`, other DLLs from
  `ce8e45d`), `ce8e45d` `E5618CB7...`, `33ea9bb` `AD1C76AC...`, `5600f90` `AFD030DE...`, `2ab5316` `4A4E3DB7...`
  (builds are not deterministic: the hash identifies the pinned copy, not the source).
- `before-run-<sha>/`: the test assembly with that solver DLL; `unit-before-runs.txt`, `before-ce8e45d-newtests.txt`.
- `review-harness/`, `earlier-harness/`: the review harnesses with each solver DLL.
- `clip20/`: conditioning dumps (`cond-<sha>.*`) and source-keyed comparisons (`cmp-*.txt`).
- `l1/`: Extend3D-alone runs on the saved Softhouse Clean3D output, the reduction (`l1min-*`, `l1var-*`), panel
  comparisons (`panelcmp-*`); `l2/`: the clip-failure reduction and the temporary debug build's output.
- `fixtures-cond/`, `fixtures-cond-2ab5316/`: conditioning of 8 fixtures, `summary.txt`; `validation-2ab5316.txt`.

Portable tools: `handover/tools-2026-10-26-r3/` (r2 tools plus `SrcDump.cs`, `Ext3D.cs`, `L1Min.cs`; `stage.sh` quoting
fixed - the r2 copy wrote `Fixtures\$fx`, which escapes the `$`, so a fixture name was never found and the probe ran on
nothing). Inputs: fixtures from this branch's `Testing/SAM.OCCT.IntegrationTests/Fixtures`; SAM from `SAM-wt-junctions`
(`fe5bbce4`); Rhino 8 for the tool host (`RhinoInside`).
Comparison worktrees (detached, do not commit): `SAM_OCCT-wt-extend3d-base` (`22e4fd0` + tests), `SAM_OCCT-wt-extend3d-fix1`
(`639ef12`); export `SAM-BIM/.extend3d-r3-696dce0` (not a worktree; build input of the pinned `696dce0` DLL).

### Revision 3 commands (Git Bash; `H` = `SAM_daily/2026-10-25-Softthouse/handover`, `E` = `$H/evidence/2026-10-26-r3`)

```sh
# build (APPDATA/USERPROFILE redirected), unit tests with the real profile
sh $H/tools-2026-10-26-r3/scripts/safebuild.sh build Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug --no-build
# the same test assembly against another solver DLL (copy the bin, swap SAM.Geometry.OCCT.Solver.dll)
dotnet test "$E/before-run-696dce0/SAM.OCCT.UnitTests.dll" --filter "FullyQualifiedName~ExtendIntermediateCapTests"
# managed conditioning dump (no native resolve: nativeResolved=False), then source-keyed comparison
sh $H/tools-2026-10-26-r3/scripts/stage.sh <out> <pinned build dir> AdjacencyCluster-home.sam 0 condition cond-<sha> SRCDUMP=1
sh $H/tools-2026-10-26-r3/scripts/stage.sh <out> <pinned build dir> AdjacencyCluster-home.sam 0 condition cmp.txt \
   SRCCMP_A=<out>/cond-639ef12 SRCCMP_B=<out>/cond-ce8e45d MASK=-3.09,4.79,-0.05,-2.865,4.81,2.95 KEY=20
# Extend3D alone on saved Clean3D output; reduction to a minimal throwing (or, with L1FAILED=1, clip-failing) set
sh $H/tools-2026-10-26-r3/scripts/stage.sh <out> <pinned build dir> x 0 condition ext3d-<sha>.txt EXT3D=<panels_clean.json>
sh $H/tools-2026-10-26-r3/scripts/stage.sh <out> <pinned build dir> x 0 condition l1min.txt L1MIN=<panels_clean.json>
sh $H/tools-2026-10-26-r3/scripts/stage.sh <out> <pinned build dir> x 0 condition cmp.txt PANELCMP_A=<a.json> PANELCMP_B=<b.json>
```

### Revision 3b evidence and commands (`E` = `SAM_daily/2026-10-25-Softthouse/handover/evidence/2026-10-26-r3b`)

Machine-local; copy before switching machines. Tools: `handover/tools-2026-10-26-r3b/` (r3 plus naked-point locations in
`pipe/Stage.cs` and `scripts/msgs.sh`, which reproduces the saved `msg-76f6f22.txt` / `nonpass-76f6f22.txt` from their log).
- `runs/`: `unit-wip.trx` (1131/1131), `before-2ab5316.txt|trx` (the new tests on the `2ab5316` solver DLLs; the bin with
  the swapped DLLs in `before-run-2ab5316/`), `integ-b837165.log|trx|head`, `msg-b837165.txt`, `nonpass-b837165.txt`.
- `commits/verify.txt`: `2986ad5` and `0f060ff` built and tested from `git archive` exports (unit 1115 / 1123, GH 33 / 37).
- `mutations/`: the five L1 mutations and the baseline (`<name>.txt`: the change made and the failing tests).
- `fixtures-cond-wip/`: conditioning dumps of the 8 fixtures, `cmp-*.txt` against `2ab5316`, `summary.txt`; Softhouse
  `ext3d-wip.*`, `panelcmp-2ab5316-vs-wip.txt`, `diffpanel-2ab5316-vs-wip.txt`; `variant-item3-reverted/` (change 3
  reverted: Softhouse equal to `2ab5316`).
- `harness/Probe.cs` with `run-probe-wip|2ab5316/` and `run-review-wip/` (the review's `Program.cs` against `dll-wip/`);
  outputs `probe-*.txt`, `review-harness-wip.txt`. `dll-wip/`: the managed DLLs of the build tested (`HASHES` below).
- `clip20/`: `res-A`, `res-B` (+ `.rebuild`, `.resolved.json`), `cmp-resolved-A-B.txt`, `inputs.sha256`.

Builds are not deterministic (the hash identifies the copy, not the source). Solver DLL SHA256: `3B116050...` (first
unit/Grasshopper runs, mutations' reference, managed equality and harness runs; working tree = `b837165` code before the
commit split); `82E27CEC...` (built from the `b837165` checkout: native integration suite and the final unit 1131 /
Grasshopper 37 re-runs; `integ-b837165.head`). Also `chain-ach-<sha>.txt` (Clean3D -> Extend3D on
AdjacencyCluster-home with `ce8e45d`, `33ea9bb`, `5600f90`, `2ab5316`, wip and item-3-reverted DLLs) and
`runs/stdout-76f6f22-vs-b837165.diff`.

```sh
# build (APPDATA/USERPROFILE redirected); tests with the real APPDATA and --no-build, one native process at a time
sh $H/tools-2026-10-26-r3/scripts/safebuild.sh build Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug
sh $H/tools-2026-10-26-r3/scripts/safebuild.sh build Testing/SAM.OCCT.GrasshopperTests/SAM.OCCT.GrasshopperTests.csproj -c Debug
sh $H/tools-2026-10-26-r3/scripts/safebuild.sh build Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug --no-build
dotnet test Testing/SAM.OCCT.GrasshopperTests/SAM.OCCT.GrasshopperTests.csproj -c Debug --no-build
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug --no-build --logger "trx;LogFileName=integ-b837165.trx"
sh $H/tools-2026-10-26-r3b/scripts/msgs.sh $E/runs/integ-b837165.log $E/runs/x   # -> x.msg.txt, x.nonpass.txt (sorted)
# failing-before: copy the unit bin, swap SAM.Geometry.OCCT.Solver.dll and SAM.Analytical.OCCT.Solver.dll from r3 pinned/2ab5316-build
dotnet test $E/before-run-2ab5316/SAM.OCCT.UnitTests.dll --filter "FullyQualifiedName~ExtendIntermediateCapTests|FullyQualifiedName~ExtendLevelClipDiagnosticsTests"
# clip #20 resolve-only (native; pinned ce8e45d build; FACES/SWAP from evidence/2026-10-26-r3/clip20)
sh $H/tools-2026-10-26-r3b/scripts/stage.sh $E/clip20 <r3 pinned/ce8e45d-build> AdjacencyCluster-home.sam 0 resolve res-A FACES=<cond-ce8e45d.faces.json> REBUILD=1
sh $H/tools-2026-10-26-r3b/scripts/stage.sh $E/clip20 <r3 pinned/ce8e45d-build> AdjacencyCluster-home.sam 0 resolve res-B FACES=<cond-ce8e45d.faces.json> SWAP="<cond-639ef12.faces.json>|20" REBUILD=1
sh $H/tools-2026-10-26-r3b/scripts/stage.sh $E/clip20 <same> x 0 condition cmp-resolved-A-B.txt COMPARE_A=<res-A.resolved.json> COMPARE_B=<res-B.resolved.json>
# managed conditioning of a fixture and the source-keyed comparison (repeat for the 8 fixtures; bbl 0.21 for two-level-tilted)
sh $H/tools-2026-10-26-r3/scripts/stage.sh $E/fixtures-cond-wip <build> <fixture>.sam 0 condition <fixture>-wip SRCDUMP=1
sh $H/tools-2026-10-26-r3/scripts/stage.sh $E/fixtures-cond-wip <build> x 0 condition cmp-<fixture>.txt SRCCMP_A=<r3>/fixtures-cond-2ab5316/<fixture>-2ab5316 SRCCMP_B=$E/fixtures-cond-wip/<fixture>-wip
# harnesses (offline NuGet config of the review folder)
dotnet run --project $E/run-review-wip/Review.csproj --no-build ; dotnet run --project $E/run-probe-wip/Probe.csproj --no-build
```

### Revision 3c evidence and commands

Machine-local; copy before switching machines. `E` = `evidence/2026-10-26-r3b` (investigation and managed impact),
`F` = `evidence/2026-10-26-r3c` (final suites).
- `$E/clip20-trace/`: `dump-A|B|C/` (stages R1..F3 as in the table of "Revision 3c" item 1), `res-A|B|C` (+ `.rebuild`,
  `.resolved.json`), `cmp-AB-r3.txt`, `cmp-AB-r5.txt`, `validate-<A|B>-<same|reverse|rotate>.txt`, `cond-C.faces.json`,
  `variant-C.txt`; instrumented DLLs `$E/dll-instr/`, `$E/dll-instr-chain/`.
- `$E/workflowB/`: `clean.json` (saved Clean3D output), `extended-<ce8e45d|wip|fix|wip-noslit|wip-noslit-nohole|wip-nohole|ce8e45d-nohole>.json`,
  `build-<set>-<same|reverse>.txt` (native build: cells, volumes, boxes), `face20-*.txt`, `clipdump/` (the clip inputs of
  every clipped panel, source of the regression's captured #20 inputs), `diffpanel.txt`, `variant-noslit.txt`;
  DLL sets `$E/dll-wip/` (`b837165` code), `$E/dll-fix/` (`695e814` code, solver `CC625E1C...`).
- `$E/fixtures-cond-fix/`: conditioning dumps of the 8 fixtures and Softhouse `ext3d-fix.*` on the `695e814` code,
  `cmp-*.txt`, `summary.txt`, `facecmp-2ab5316-vs-fix.txt`, `facecmp-softhouse-wip-vs-fix.txt` (`$E/harness/Probe.cs`
  `facecmp` mode, added in revision 3c; `Probe.cs.before-facecmp` is the previous copy).
- `$E/runs/before-b837165-clipface.txt`: the new regression against the `b837165` solver (`$E/before-run-b837165/`).
- `$F/runs/`: `hashes-695e814.txt`, `unit-695e814.trx|log`, `gh-695e814.trx|log`, `integ-695e814.trx|log|head`,
  `integ-695e814.msg.txt|nonpass.txt` (`msgs.sh`), masked per-test output `integ-<76f6f22|b837165|695e814>.out.txt` and
  `stdout-b837165-vs-695e814.diff`, build logs `rebuild-*.log`.
- `$F/presentation/`: resolve-only controls on the fix build (`res-ach-<2ab5316faces|fixfaces>`, `res-rh-<2ab5316faces|fixfaces|swap4|swap20>`,
  `res-tw-<2ab5316faces|fixfaces|swap-orderonly>` with `.rebuild` and `.resolved.json`), `cmp-tw-resolved.txt`,
  `facecmp-tw-resolved.txt`. `$F/ninespace/`: conditioning of `ControlledWorkflow/Panels-9SpacesModel.sam` at bbl 0 and
  0.21 with `dll-wip` (`b837165`) and the fix build, `facecmp-ns-*.txt`.

Masked output: per test of the `.trx`, the captured output with GUIDs, `h:mm:ss` and `<n> ms|s` timings and temporary
file names masked, lines sorted within each test, tests by name, run-level output last (inline PowerShell, text in
`$F/tools/trxout.ps1`). Checked on the earlier pair: `76f6f22` vs `b837165` gives only the known WorkflowParity
AdjacencyCluster-home workflow B lines.

```sh
# trace (isolated instrumented export of b837165; StageDump hooks): A, B, C; then the presentation variants of R5
sh $H/tools-2026-10-26-r3b/scripts/trace20.sh $E/clip20-trace
dotnet $E/run-probe-instr/bin/Debug/net8.0/Probe.dll validate $E/clip20-trace/dump-A/07-R5-after-sew.json <same|reverse|rotate>
# Workflow B: Extend3D on the saved Clean3D output with a DLL set (CHAIN_CLEAN reads it, CHAIN_SAVE writes the extended
# panels), then the native build exactly as WorkflowParity's workflow B (as given / reversed)
CHAIN_CLEAN=$E/workflowB/clean.json CHAIN_SAVE=$E/workflowB/extended-fix.json dotnet $E/run-probe-fix/bin/Debug/net8.0/Probe.dll chain AdjacencyCluster-home.sam
dotnet $E/run-probe-fix/bin/Debug/net8.0/Probe.dll build $E/workflowB/extended-fix.json <same|reverse>
# managed impact
dotnet $E/run-probe-fix/bin/Debug/net8.0/Probe.dll facecmp <a.faces.json> <b.faces.json> faces
dotnet $E/run-probe-fix/bin/Debug/net8.0/Probe.dll facecmp <a.panels_extended.json> <b.panels_extended.json> panels
# presentation controls: resolve-only of saved conditioned faces, optionally with single faces swapped in from the other set
sh $H/tools-2026-10-26-r3b/scripts/stage.sh $F/presentation fix Revit-home-panels.sam 0 resolve res-rh-swap4 FACES=<2ab5316 faces.json> SWAP="<fix faces.json>|4"
sh $H/tools-2026-10-26-r3b/scripts/stage.sh $F/presentation fix whole-level-towers.sam 0 resolve res-tw-fixfaces FACES=<fix faces.json> REBUILD=1
# final suites at 695e814 (as revision 3b, --no-incremental builds through safebuild.sh)
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug --no-build --logger "trx;LogFileName=$F/runs/integ-695e814.trx"
sh $H/tools-2026-10-26-r3b/scripts/msgs.sh $F/runs/integ-695e814.log $F/runs/integ-695e814
```

## Handover (2026-10-26, revision 3c; machine `michal`)

**State (updated 2026-10-27).** Pushed; draft PR SAM-BIM/SAM_OCCT#84 into `sow/2026-Q4`; `sow/2026-Q4` (`1e39868`, #85)
merged in (see "Merge of sow/2026-Q4"). The table below is the 2026-10-26 local state.

| repository / worktree | branch | commit | working tree |
| --- | --- | --- | --- |
| `SAM_OCCT-wt-extend3d` (this record) | `fix/extend3d-no-invented-walls-q4` (upstream `origin/sow/2026-Q4`, not pushed) | record commit on `695e814`; **tested content `695e814`** | clean after the commit |
| `SAM_OCCT-wt-extend3d-base` / `-fix1` (comparison only) | detached | `22e4fd0` + untracked rev-1 tests / `639ef12` | do not commit |
| duplicate-faces worktree | `fix/solver-output-duplicate-faces-q4` | `3ff5f4b` | **parked** - untouched |
| `SAM-wt-junctions` (SAM side) | `fix/panels-by-solids-junctions-q4` | `fe5bbce4` (= SAM `sow/2026-Q4`) | clean, empty branch |

Branch commits on `22e4fd0`: `639ef12` (rev 1), `1408b93`, `696dce0` (rev 2), `dc8f9df`, `ce8e45d`, `76f6f22`, `7ab06e8`,
`bdde7c6` (records except `ce8e45d`), `33ea9bb` (M2), `5600f90` (L1/L2 reporting), `2ab5316` (L2 area threshold),
`2986ad5` (L2 per piece), `0f060ff` (L1 severity), `b837165` (AtBase/AtTop reach), `59ad662` (record), `695e814`
(revision 3c: local-cut clip, minor version bumps; its subject still says "WIP" - the content is final and tested; the
message was not rewritten) and this record commit.

**Results at `695e814`.** Unit 1133/1133; GrasshopperTests 38/38; native integration 24 failed / 472 / 2 skipped (the
`b837165` failures less WorkflowParity Revit-home, now passing). Workflow B on AdjacencyCluster-home is back to 18
spaces; the WorkflowParity AdjacencyCluster-home test itself still fails (A-solver-matched 16 vs 18 expected, as at
`b837165`). Of the 16 tests whose output changed, 11 pass and 5 fail - all 5 failed before. Managed changes confined to
clipped faces and geometry-neutral there; the native-measured swings elsewhere are the solver's presentation
dependence, shown by resolve-only controls on three fixtures and inferred for the rest ("Revision 3c" item 5). Not run: hosted CI
(nothing pushed), the Softhouse chain or any building simulation (excluded), an interactive Grasshopper canvas (headless
contract and reporter tests only; `%APPDATA%\SAM` still holds the 06:55 deployment, so Grasshopper does not load this
code until it is redeployed). **The full Softhouse rebuild is not done.**

**Assumptions.** As before for the Softhouse diagnostics (sampling grids, 80 % share, leak rays, chain settings). The L2
retention argument assumes Extend's own fill (each column filled from the target to the wall); it is also why no input
through the component reaches a failed clip. The AtBase/AtTop reach is conservative (the crossing's own exceptions are
not followed). The two-level-tilted golden and the ParameterDiscovery trial rows are attributed to the shown
presentation dependence without a trace of their own.

**Unresolved / blockers (owner).**
1. **Presentation-dependent naked-edge measure in the solver** (the remaining #20 item): the intermediate sewing
   `Validate` of the flattened cell faces (ResolveStage) and the measurements built on it (GapFill, the consolidation
   rebuild choice, the goldens' sew-0.01 rebuild) give different answers for the same geometry in another face order or
   loop start (A 45 / 48 / 40, B 44 / 68 / 40). **Bounded proposal** (separate solver/native task, not this branch):
   count free edges topologically from the MakerVolume result - an edge is naked when it bounds exactly one face of the
   cell complex (native `TopExp::MapShapesAndAncestors` over the result's edges and faces; no sewing) - and use that
   count in `ResolveStage`, `GapFill` candidate selection and the consolidation choice; acceptance: the A/B/C sets and
   their reverse/rotate variants give one answer each; the A/B naked sets agree away from #20's corner; the integration
   suite's `Solve3D` outputs become invariant to loop start (the `presentation/` controls). Until then, cell counts from
   a sew-based rebuild are not evidence of a geometry change.
2. Goldens and other failing tests, all failing before this branch's last change: managed whole-level-towers (21
   expected, now 22) and managed 0.21 two-level-tilted (9, now 16) - sew-rebuild measurement of an output with
   duplicates; managed 0.21 whole-level-towers (25 expected, 27, unchanged); WorkflowParity AdjacencyCluster-home
   (A-solver-matched 16 vs 18); NineSpaceWallCapture West2/West3 deltas and the capture 0.45 rotate Z 45 North0/North2
   comparison (pre-existing reasons). Parked branch untouched, no re-baseline. Disposition needed before merge.
3. Pre-existing, tracked separately ("Separate"): split-piece opening loss at a junction; `GetBaseSegment` for tilted
   walls taller than long; AutoTune3D surfaces no per-panel extend lines; Extend3D drops the Clean3D slab openings
   without `HOLE_DROPPED`; SAM multi-face `Difference` null ambiguity (SAM side); L3 native stage of the saved Softhouse
   run untied.
4. Before merge: the native integration suite re-run on the merged head (with #85) and its changes against `695e814`
   classified; hosted CI green at the final PR head, every review finding resolved or accepted, owner/code-owner
   approval, codex-bot comment check. The GH manifest check of the standard does not apply (no approved
   `param_contract.json` in SAM_OCCT); a reviewer reads the four version bumps in the diff.

**Decisions (owner, 2026-10-26).** Duplicate-faces branch parked. Clip #20 kept, no reach exemption (Workflow B shows the
stub is invented material). No golden re-baseline. No push, PR or merge without owner approval. Revision 3c: bounded
native probes, the demonstrated correction, minor component bumps and one final managed/GH/native check authorised by
the owner's task (`REVISION3B-NEXT.md`); no Softhouse chain.

## Next step (recommended; not started)

1. **Owner: approve opening the draft PR** into `sow/2026-Q4` (push this branch, PR body from this record, marked "not
   ready: merge blockers listed"), then hosted CI. Recommended: this or a new session, Build mode, Claude Opus high
   effort - mechanical.
2. **Independent review of revision 3c** (`b837165..695e814` and this record) before merge. Recommended: new session,
   Plan mode (read-only), Claude Opus high effort, the reviewer of `REVISION3B-REVIEW.md`, so the local cut and the
   presentation attribution get a reader not anchored on this session. Evidence: `evidence/2026-10-26-r3b`, `-r3c`.
3. **Owner decision** on the solver measure proposal (blocker 1) and the goldens (blocker 2). The measure work is a
   separate task (new session, Build mode, Claude Opus high effort; native/solver change with its own PR).
