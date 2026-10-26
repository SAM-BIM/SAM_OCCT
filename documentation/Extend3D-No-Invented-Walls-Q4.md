# Extend3D: walls extend to their own cap, never through another storey — Q4 (2026-10-26)

Status: implemented locally on `fix/extend3d-no-invented-walls-q4` (base `sow/2026-Q4` at `22e4fd0`). Not committed, not
pushed, no PR. Step 1 of the Softhouse "Dining 1" stage investigation (owner, 2026-10-26: SAM_OCCT Extend3D first, the SAM
extraction fix as a separate PR). Revised after an independent review (bounding-box coverage made equivalent single and
split floors give different walls) and after the fixture-by-fixture review below. The Clean3D kink candidate is kept out
of this branch (patch only, see "Separate").

## Problem

`Extend3D` (and the `Solve3D` / `AutoTune3D` managed paths, which share `ConditionStage`) grew walls into storeys where the
input has none, which changes room identity. Softhouse: +555 m2 of panel area, walls growing by 9 to 98 m2 each. Two
mechanisms, each reproduced synthetically:

- **M1 - a cap the wall already passes is skipped.** `NearestCoveringCap` accepted only caps at or beyond the wall extreme.
  A wall whose base passes its floor by a few centimetres (a mid-surface wall snapped to a slab that Clean3D normalised
  25 mm higher; a foundation wall ending 5 cm above the ground slab; an attic wall starting 5 cm below the ceiling; or
  Extend3D's own output) was grown to the next cap, a storey away: vertical reach is uncapped by policy.
- **M2 - extending a non-rectangular wall fills every notch.** Non-rectangular walls extend through
  `SAM.Geometry.Spatial.Query.Extend` (or the column-wise sloped-cap polygon), which fill everything between the outline
  and the target line. A wall merged across storeys by Clean3D whose lower storey is narrower became a full rectangle.

## Change (`SAM_OCCT/SAM.Geometry.OCCT.Solver`)

1. **M1** `Panel3DSnapSolver.NearestCoveringCap` (new `out bool passed`) / `ExtendWallToNearestCap`: when the cap surface
   nearest the wall extreme is one the wall already passes by at most the flat extend overshoot, the wall already meets
   its cap: it is not extended (record `AlreadyMeetsTarget`, "already passes its cap by at most the overshoot"). It never
   counts when it lies below the wall's mid-height (the floor a short wall stands on, seen from its top), nor when another
   cap lies within the wall's reach (`MaxExtension`) on the grow side (a split-level floor beside the wall). Otherwise the
   previous nearest-cap rule applies unchanged (a cap exactly at the extreme still gets the overshoot).
2. **M2** `Panel3DSnapSolver.ClipBeyondIntermediateCaps` + `SnappedPanel.ClipBeyondLevels`: after a vertical extend, the
   extension grown past an intermediate flat cap (between the wall's opposite extreme and the target) is removed over the
   free columns of the wall: columns where the original wall does not continue past that level (material within the
   overshoot of the level does not count).
   - Where a level covers the wall is read from the caps' **actual outlines** (concave parts and openings kept) within
     the wall's capture half-width (`BucketSize`) of its plan line; caps within the overshoot of one elevation are
     merged, so a floor split into pieces covers the wall exactly as the floor in one piece (review finding).
   - A free stretch narrower than the wall's lateral reach (`MaxExtension`) is a junction gap (the wall stopping short of a
     crossing wall) that the lateral extend exists to close: it keeps its fill.
   - A level within the wall's reach of the target is a step of the target's own level (split-level floors either side of
     a wall, 0.144 m apart in whole-level-towers), not another storey: it does not stop the extension.
   - Only the extension is clipped: the original face is united back whole, so no material the wall had is removed.
   - Each clip is reported: `SAM_OCCT_EXTEND3D_LEVEL_CLIP` (area removed, levels, extent), surfaced through
     `SolverReportFormat.FormatExtendPanelDiagnostics`. Census counter `LevelClipCount`.

Plain rectangles never have free columns (fast path unchanged). No public API, parameter, default, Grasshopper component
or native change.

## Tests

- `Testing/SAM.OCCT.UnitTests/ExtendIntermediateCapTests.cs` (new, 34 cases): set-back upper wall 25 mm below / at Extend's
  output / on / 25 mm above its floor; top 25 mm above its ceiling; a base 0.2 m below the floor still extends (unchanged
  rule); short wall stubs (0.05, 0.095 m) standing on a floor still reach the ceiling; run twice = no change; merged L wall
  keeps its notch, also translated and rotated; a 0.15 m junction gap stays filled; a split-level floor 0.15 m above the
  target beside the wall stops neither the extend nor counts as the cap reached; a wall continuous through the first
  floor and a door notch within one storey keep the previous extend; plain rectangles unchanged. **Equivalence**: the
  first floor as one outline or split into pieces (full cover, a concave double-height corner at the wall, a stair
  opening over the wall line), each also translated and rotated, gives the same wall (area and a 31 x 31 inside grid),
  and the floor's actual geometry decides where the notch stays (under the corner and the opening the previous extend
  applies). With bounding-box coverage 5 of these fail.
- `ExtendCensusCollection.cs` (new): `Panel3DSnapSolverTests` and `ExtendIntermediateCapTests` share an xUnit collection;
  the census assertions in `Panel3DSnapSolverTests` failed intermittently when the new class ran `Extend` in parallel.
  `SqueezeAndAngledExtendTests` and `ResidualContactClosureTests` also call `Extend` outside the collection (pre-existing
  risk, unchanged).
- `Testing/SAM.OCCT.IntegrationTests/Extend3DIntermediateCapIntegrationTests.cs` (new, native-gated): room volumes through
  Clean3D -> Extend3D -> AdjacencyCluster. Set-back upper storey: base 30 + 150 + 150 m3 (ground floor split by the
  invented wall), fix 150 + 180. Party wall upstairs only with a 2 m ground-floor stub: base four 90 m3 rooms, fix 90 +
  90 + 180.

## Validation (local, machine `michal`, 2026-10-26; identical conditions for base and fix)

Base = detached worktree `SAM_OCCT-wt-extend3d-base` at `22e4fd0` with only the new test files added; fix = this
worktree. Both use `..\SAM\build` (SAM `sow/2026-Q4` `fe5bbce4`) and a copy of the same native `build` folder; built with
APPDATA/USERPROFILE redirected (the GH PostBuild deploys to `%APPDATA%\SAM`), tests run one after the other with the
real APPDATA and `--no-build` (HouseAirAndAttic needs the default internal-condition library from it).

```powershell
dotnet build Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug
dotnet build Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug --no-build
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug --no-build
```

| suite | base | fix |
| --- | --- | --- |
| unit | 12 failed / 1060 passed (the 12 new defect cases) | **1072 / 1072** |
| integration (native) | 27 failed / 469 passed / 2 skipped | 25 failed / 471 passed / 2 skipped |

The integration runs differ in exactly five tests; every other test has the same outcome and message:

| test | pinned | base | fix |
| --- | --- | --- | --- |
| Extend3DIntermediateCap SetBack (new) | 2 rooms | 3 (fail) | pass |
| Extend3DIntermediateCap PartyWall (new) | 3 rooms | 4 (fail) | pass |
| Extend3DPlaneTarget RevitHome | 14 | 22 | 7 |
| GoldenMaster managed whole-level-towers | 21 | 23 | 24 |
| WorkflowParity Revit-home A-solver-matched spaces | - | 22 | 7 |

The other 23 non-passing tests (the 2026-10-25 recorded set) fail identically on both. No re-baseline is proposed until
the open items below are traced. Evidence (machine-local):
`SAM_daily/2026-10-25-Softthouse/handover/evidence/2026-10-26-stage/occt-final` (trx, logs, fixture probes, Softhouse).
`git diff --check` clean; SPDX headers on the new files. Not run: GrasshopperTests, hosted CI (nothing pushed).

## Moved fixture results, explained (managed `Solve3D(forceManagedPipeline)`, probe = cells from `Create.Shells` on the solved faces)

| fixture (bbl) | base cells / naked | fix cells / naked | why |
| --- | --- | --- | --- |
| whole-level-flat, whole-level-tilted, tilted-two-spaces (0, 0.21) | 22/0, 22/0, 2/0 | identical cell lists | no wall passes a cap; no clip |
| Face3D-home (0) / (0.21) | 15/0, 18/8 | identical cell lists | no extend geometry change |
| two-level-tilted (0) | 13/8 | identical cell list | four walls whose top passes their roof by 2.6 cm stay as they are (as in base) |
| two-level-tilted (0.21) | 16/3 | 16/3, two cells differ | **open**: one clip (a wall at y -17.49 whose top extension crossed a level, 23.8 m2) is the only extend geometry change, yet two upper rooms at x 30.5..40.8, y -7.6..1.3 that the input separates by a double wall ([65]/[79], 0.12 m apart) come out as one 245.9 m3 cell; not traced (global resolve / GapFill effect suspected) |
| whole-level-towers (0) | 23/13 | 24/7 | base grows a wall top 12.1 m through four levels (15.41 -> 27.54); the fix stops it at the next level (one clip, 73.2 m2). One more room closes (x 11.07..17.14, level 12.38..15.44, 159.5 m3); four walls passing their cap by <= 5 cm are no longer grown on |
| whole-level-towers (0.21) | 27/0 | identical cell list | one clip |
| Revit-home (0, 0.21) | 11/5 | 5/8 | see below |

Revit-home ground floor (input cut at z 1.3 has no wall at x 0.535 and none across the south strip): base split the south
strip into four cells with attic walls carried down to the ground floor (x 0.535, -2.69, 8.135; bottom 2.6 -> -0.005) and
added three 4-44 mm sliver cells with foundation walls pulled up through the ground floor (top 0.095 -> 2.7) beside the
real wall. The fix gives the strip as one 44.2 m3 room, no slivers, and the three small rooms unchanged. The west room
(x -3.09..4.235) is open in both (base only closed its eastern part behind the invented x 0.535 wall). The east room
(x 8.135..11.535, 53.5 m3) closed in base only as part of a merged 85.7 m3 cell; in the fix two of its wall faces (x 11.535;
x 8.135, y 2.62..4.575) come out of the native resolve with all four edges naked. Cause in the resolve not yet traced.

Softhouse (same extracted panels, identical setups): base 70 spaces, 1 reference room outside a space; fix 52, 7 outside
(all house D) and the ground floors of houses A-C in one cell. The invented walls had masked a Clean3D kink defect and
extraction junction-block defects (see the SAM extraction task and the Clean3D candidate); total extend growth 555 -> 311 m2.

## Separate (not on this branch)

- Clean3D kink candidate: `clean3d-kink-jog-candidate.patch` (machine-local evidence folder) - the jog alignment requires
  parallel pieces (0.3 degrees); `SnapStageTests` kink test fails before, passes after. Applies to `22e4fd0`.
- SAM extraction fix (junction blocks shorter than the wall thickness): SAM worktree `SAM-wt-junctions`, empty.

## Open before any re-baseline

1. two-level-tilted (bbl 0.21): two upper rooms separated in the input by a double wall merge although the only extend
   geometry change is a clip on a wall in another block; trace through the native resolve / GapFill.
2. Revit-home east room: two wall faces leave the resolve with all edges naked (base closed the room only merged with its
   neighbour); trace in the resolve.
3. Revit-home 7 spaces in the plane-target / parity tests vs 5 cells in the probe: reconcile the two measurements.

## Next step

Owner review of the explanations and the three open items; trace them (same branch); then commit, PR, and the order with
the Clean3D candidate and the SAM extraction PR (Softhouse alone: 70 -> 52 spaces until those land).
