# North0 consolidation follow - branch record (2026-10-23)

Branch: `fix/north0-consolidation-follow-q4` (local only: not pushed, no PR, not merged). Based on the reviewed,
unpushed nine-space test head `4d77ef415074793fc5f2ef484d5edbad1421ddda` (test content `30e0e27`, guard ancestor
`3523ed2`), not on remote `sow/2026-Q4` (`dc871f7`). Commits: `ff71a70` (correction + tests), `4b0e53d` (record),
`791bcaa` (validation closeout: authored-uniqueness rule, towers test reconciliation), then this record update.
**Tested content: `791bcaa86b92b67f5cb7e0b8a1b3968d2fd46cd9`** (earlier `ff71a70`, results kept below). Task: machine-local
`SAM-BIM/.review-nine-space-bucket-followup-20261022/PROPOSED-NORTH0-PRODUCTION-TASK-20261023.md`, approved and sent
by the owner 2026-10-23; review basis `REVIEW-GATE-FOLLOWUP-20261023.md`, cause in
`documentation/Nine-Space-Wall-Capture-Q4.md` ("Lineage and intervention evidence"). `PROJECT_PROGRESS.md` is not
touched (post-merge closeout only).

## Status

**On the original `Panels-9SpacesModel.sam`, capture 0.55 and 0.65 now keep all nine rooms and pass the unchanged
nine-space contract** (9 cells, all ten shared boundaries incl. North0|West3, North0/West3 distinct, West3 double
height, no Extra/Missing, valid closed disjoint cells, complete coverage, no unreliable diagnostics). The 0.45
owner-accepted output, the unstamped run and 0.20 are spatially identical to the base. The owner visually approved
the corrected model and ran the workflow successfully (2026-10-23). At `791bcaa` the four towers tests that pinned
the room loss are reconciled to assert its survival, and an inset-edge misassociation found on the towers model is
corrected (no nine-space change: exports identical to `ff71a70`). Full native: 19 historical + 6 rigid-motion
failures remain (motion separately tracked, finding 2 of the nine-space record); nothing new. Awaiting independent
review. Nothing pushed, no PR.

## What changed (`SAM_OCCT/SAM.Geometry.OCCT.Solver`)

Both changes are inside the two private follow helpers of `Panel3DSnapSolver.ConsolidateWallStacks` plus minimal
private/internal support. They run only after a wall has actually been consolidated, so no conditioning decision
before it (opposed collapse, bucket/align snap, stack membership, dominant plane, travel cap) changes.

1. **Wall end, `DragAbuttingWallEnds`** (`Panel3DSnapSolver.cs`). Unchanged first: an end within 0.02 m
   (`STACK_FOLLOW_END_TOLERANCE`) of the moved wall's old foot is dragged as before. New fallback for an end that
   test misses (`IsSourceAssociatedEnd`), all required: the end still lies on the old plane (0.02 m); one of the
   partner's SOURCE foot ends lay on one of the moved wall's SOURCE feet (0.02 m) - the authored junction; since
   then that end moved only across the partner's own run (along-run component <= 0.02 m), i.e. a plane snap of the
   partner; that displacement explains the whole overshoot past the old foot; and the projected end lands on the
   stack's dominant wall - within 0.02 m of its foot, storeys overlapping. The existing storey and non-parallel
   checks still apply first. Here: a577d53d's authored end (0.129, -2.306) is ca160254's foot end; its align snap
   moved it 0.046 m to y -2.26; it now follows to x -0.345 on edb39f15 (foot y -27.06..0.5).
2. **Inset cap edge, `DragAbuttingCapEdges` + `MoveInsetCapEdge`**. Caps eligible under the existing rule (plan box
   overlap and boundary within tolerance of the old plane) keep the existing uniform `GrowOutward` - not replaced.
   Any other cap passing the existing storey (Z-band) test may move ONE edge (`SnappedPanel.MoveBoundaryRun`,
   internal) by the wall's signed plan displacement when all hold: the whole cap is on the side the wall moved
   away from; its boundary points nearest the old plane (0.02 m band) are one contiguous run of >= 2 points;
   that run is inset at most 0.3 m (`OPPOSED_PARTITION_MAX_SEPARATION`) from the old plane AND from one of the moved
   wall's source feet, and lies within both feet's runs (+-0.02 m); no other same-storey vertical wall's SOURCE foot
   lies along the edge's run between the edge and the old plane (authored uniqueness, added at `791bcaa`); no other
   same-storey vertical wall crosses the swept strip (edge run x depth from new position to the edge); each cap
   moves this way at most once per pass.
   The authored inset is kept for the existing Extend3D fill. Neighbour edges are kept: an edge already running
   along the move just lengthens, otherwise a stub joins it; holes are kept; the result must be valid and larger.
   Here: the roof 770b8239's west edge (0.088 m inside ca160254) moves 0.474 m to x -0.257; the fill closes it.
3. Diagnostics: two new Info messages under the existing `SAM_OCCT_CLEAN3D_DIAGNOSTIC`/`ConsolidatedStack` code
   ("dragged a source-associated wall end", "moved an inset cap edge ... authored inset kept for the fill").

Not changed: public APIs, capture/void thresholds, chosen plane, native ABI, SAM/SAM_Solver, inputs, goldens,
storey/floor policy, equal-area ordering, rotation path, Extend3D policy, GH components. Diff: `Panel3DSnapSolver.cs`,
`SnappedPanel.cs`, `ConsolidateWallStacksTests.cs`, `North0ConsolidationFollowIntegrationTests.cs`,
`PR61TowersQuantitativeValidationTests.cs`, `TowersBucketLeverDiagnosticTests.cs`, `TESTING.md`, this record.

## Decisions and why

- **The first wall-end rule was too broad and was rejected.** Without the "lands on the dominant wall" condition the
  same lineage pattern also fired four times in the East1|South1 band (East1's west/east walls 0.38 m, the band
  box's end walls 0.047 m) - at 0.45 too - and the 0.45 control collapsed to **one cell** (scratch run
  `iter1`). Each of those ends would land past the end of the band's dominant skin, i.e. a junction with no wall.
  The dominant-wall condition removes all four; clean-stage output at 0.45 and unstamped is then identical to the
  base, and at 0.55 only a577d53d and the roof differ.
- 0.3 m is used only as a conservative maximum together with source/run/side/strip evidence, never as membership
  proof by itself (unit-tested: no source evidence -> no move; behind a same-storey wall -> no move).
- The floor is not touched; storey eligibility is the existing Z-band test (floor is not the cause).
- **No dominant-run requirement on the cap path** (review question, resolved at `791bcaa`). After the move the moved
  wall keeps its own footprint on the target plane, so the final support under the edge is that footprint, which the
  existing run check (edge within the moved wall's old and source runs) already guarantees. Requiring the dominant's
  own run would reject a supported edge (reviewer probe: dominant x 0..4.8, moved wall x 0..6, edge x 5..6). Pinned
  by a unit pair: that edge follows; an edge past the moved wall's run does not. The wall-end path keeps its
  dominant-wall landing guard: an overshooting end lies beyond the moved wall's own footprint by definition.
- **Authored uniqueness on the cap path** (owner-approved 2026-10-23, `791bcaa`). On `whole-level-towers.sam` the
  path credited the South1 roof `cad2b4ca`'s north edge (y -23.5371) to the band's inner skin `763f6aa3` (old plane
  y -23.3391, moved +0.194 m onto the band plane y -23.1449; edge inset 0.198 m, run x 3.647..9.723 equal to the
  wall's, same storey z 15.338). The edge was authored against South1's own north wall `5b9dbfd6` (source foot
  exactly on it), which an earlier snap had already carried onto the band plane, so no current wall marked it. The
  move (to y -23.3429) changed only the merged clean face (a 3.7 mm notch closed); the base's fill reaches the same
  processed geometry. The rule now rejects an edge with another same-storey wall's source foot between it and the
  old plane; the towers activation is gone (at 0.4, 0.47 and 0.5) and North0 is unaffected (its nearest other
  source wall, the West block, is 0.473 m beyond).

## Tests

- Unit, `ConsolidateWallStacksTests` (+15): source-associated end dragged after an earlier snap; not when authored
  beyond the foot, not when it would land past the dominant's end, not on another storey; inset edge moves alone by
  the displacement; slanted neighbours get stubs; holes kept; cap on the move side, inset > 0.3 m, edge longer than
  the run, no source evidence, behind a same-storey wall -> untouched; the same wall on another storey does not
  block; abutting cap keeps the uniform growth; an uncaptured 0.474 m void moves nothing.
- Integration, `North0ConsolidationFollowIntegrationTests` (new, 6): clean-stage follows at 0.55, 0.65 and
  pair-only 0.55 (north wall x -0.345..3.657 at y -2.26; roof min x = authored - 0.474, other extents unchanged;
  each new diagnostic exactly once; North1 roof/west wall at the base clean values); no follow and base positions at
  0.45 and unstamped; pair-only chain keeps North0 beside West3 and North1 (North1 80.047 m3, the base value, not
  the 79.538 of the pre-Clean3D probe edits), West3 double height, the unstamped band's two Extra cells unchanged
  (6.573 / 0.935 m3 at y -23.173 / -23.363), cells valid.
- Red check against the base code (test files copied into a base worktree, then removed): integration 4 of 6 fail
  (all positives), unit 5 of 45 fail (all positives); every negative passes on both.
- `791bcaa`, unit (+5): `ConsolidateWallStacks_InsetEdgeAuthoredAgainstSnappedAwayWall_FollowsOnlyWithoutIt` (the
  towers shape mirrored: the edge stays when the roof's own wall is authored on it and snapped away; controls - no such
  wall, or that wall on another storey - follow by 0.194 m); red against the `ff71a70` solver for the towers case
  only. `..._InsetEdgeWithPartialDominantOverlap_FollowsWhereTheMovedWallSupportsIt` (edge x 5..6 follows, 5..6.5
  does not).
- `791bcaa`, towers reconciliation (owner-approved). The four tests no longer pin the lost west north-strip room:
  `Towers_Gap05_OverAggressive_QuantitativeValidation` is renamed `Towers_Gap05_WestNorthStripRoomSurvives_QuantitativeValidation`;
  it and `Towers_Gap04Vs05_QuantitativeDelta`, `Towers_DoubleWallGap_FixAcceptance_Gap05StripSurvives` and
  `Towers_NorthStripPair_GapSweep_MergedAt05_NearMissAt047` now assert, from the gap-0.4/0.47 runs and source walls:
  the room survives at its gap-0.4 location (1.7164, -5.9059), distinct from and adjacent to its east sibling (volume
  unchanged, 79.526 m3); it shares the merged plane with tower cell 26 (not at 0.47); it grows by the closed slot =
  authored separation of `edb39f15`/`ca160254` x the sibling's depth x height (10.34 m3; measured 78.049 -> 88.385);
  no gap-0.4 cell loses its successor; the cell count equals the 0.4/0.47 count (30); the 0.474 pair's near-miss is
  gone at 0.5; no slivers; cells valid (`OcctUnderSplitGuard.ValidateCells`); the 0.4 -> 0.5 delta (+20.745 m3) is
  exactly that growth plus the unchanged southeast +10.408 m3. Re-measured after those checks, directly affected
  only: 0.5 total volume 9641.151 (was 9552.766), cell floor area 3171.799 (was 3142.820), volume drift +0.3722 %
  (was about -0.548 %), 0.4 -> 0.5 delta +20.745 (was -67.640). The level-datum panel floor area (1330.101 m2,
  -0.496 %) is unchanged and keeps its original constant and range - it never measured the lost room, contrary to the
  old comment. Every other constant in both classes is untouched. Against the base code all four fail through their
  topology assertions (room missing / successor lost / count), not the constants (`red-check-base-4d77ef4-towers.log`).

## Validation (local, Windows; no hosted CI)

Native: frozen `build/` (ignored), `SAM.Occt.Native.dll` SHA256 438089F8..., `TKBO.dll` 1FA732FE..., unchanged.
Managed dependencies read-only: SAM `ff518a37` (two docs-only commits past the recorded `ba686d24`; DLL hashes in
the evidence folder), SAM_Solver `fd63b46`. Builds `--no-incremental -p:SAM_OCCT_SKIP_NATIVE_BUILD=true`: unit
0 errors / 243 warnings, integration 0 errors / 284 warnings (pre-existing counts). Base comparison: detached
worktree `SAM-BIM/SAM_OCCT-base-4d77ef4`, same machine, same native and dependencies.

```powershell
dotnet build Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug --no-incremental -p:SAM_OCCT_SKIP_NATIVE_BUILD=true
dotnet build Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug --no-incremental -p:SAM_OCCT_SKIP_NATIVE_BUILD=true
$env:SAM_OCCT_NINE_SPACE_EXPORT_DIR = "<dir>"; $env:SAM_OCCT_TEST_CODE_SHA = "<tested SHA>"
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug --no-build
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug --no-build --filter "FullyQualifiedName~NineSpaceWallCaptureIntegrationTests|FullyQualifiedName~ControlledWorkflowAcceptanceIntegrationTests|FullyQualifiedName~North0ConsolidationFollow"
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug --no-build
```

### Results at tested content `791bcaa` (current)

| Suite | Base `4d77ef4` | `ff71a70` | **`791bcaa`** |
| --- | --- | --- | --- |
| Unit, full | 975 / 0 | 990 / 0 | **995 / 0** |
| Requested filter (nine-space + controlled acceptance + North0 follow) | 33 = 25 / 8 / 0 | 39 = 33 / 6 / 0 | **39 = 33 / 6 / 0** |
| Consolidation-related subset (as below) | 66 / 0 | 68 / 4 | **72 / 0** |
| Both towers classes (PR61TowersQuantitative, TowersBucketLever) | 14 / 0 | 10 / 4 | **14 / 0** |
| Native integration, full (8.9 min) | 484 = 455 / 27 / 2 | 490 = 459 / 29 / 2 | **490 = 463 / 25 / 2** |

Builds 0 errors / 243 (unit) and 284 (integration) warnings. Failure identities by name and message
(`native-full-791bcaa-nonpass.txt`, `-failmsg.txt`): versus base only the two above-gap acceptance cases are resolved;
versus `ff71a70` only the four towers failures are gone; nothing new. **Not green: 25 failures remain = 19
historical (unchanged names and messages) + 6 rigid-motion** (`AcceptanceChain_RigidMotion_SameSpatialResultAfterInverseMotion`
x 0.45/0.55/0.65 x translate/rotate Z 45; the two rotate cases at 0.55/0.65 report North0 first, as at `ff71a70`).
Same 2 skips.

Nine-space exports at `791bcaa` (`exports-791bcaa/`, read back by the export test) are spatially identical to
`ff71a70` at none/0.20/0.45/0.55/0.65 (`compare-ff71a70-vs-791bcaa.log`): the owner-approved model is unchanged;
versus base, none/0.20/0.45 identical and 0.55/0.65 differ only by the restored room (`compare-4d77ef4-vs-791bcaa.log`).
Towers (`towers-trace-*.log`, exports `towers-exports/` for 0.4/0.47/0.5 x base/`ff71a70`/`791bcaa`): at 0.4 and 0.47
`791bcaa` equals base at every level incl. the clean stage; at 0.5 it equals `ff71a70` in processed panels, cells and
cluster panels and differs from base only by the kept room.

Closeout evidence (machine-local): `SAM-BIM/.review-nine-space-bucket-followup-20261022/north0-validation-closeout-20261023/`
(`README.md`, `SHA256SUMS.txt`).

### Earlier results at `ff71a70` (kept)

Results at tested content `ff71a70` vs exact base `4d77ef4` (same machine, 2026-10-23):

| Suite | Base `4d77ef4` | `ff71a70` |
| --- | --- | --- |
| Unit, full | 975 / 0 | **990 / 0** (+15) |
| Requested filter (nine-space + controlled acceptance + North0 follow) | 33 = 25 / 8 / 0 | **39 = 33 / 6 / 0** |
| Consolidation-related subset (Consolidat, DoubleWall, Towers, ControlledWorkflow, Clean3DDiagnostics, BucketMerge, EastSouth, StageReport) | 66 / 0 | **68 / 4** |
| Native integration, full (8.7 min) | 484 = 455 / 27 / 2 | **490 = 459 / 29 / 2** |

Failure identities, compared by name and message (`native-full-*-nonpass.txt`, `*-failmsg.txt`):
- **Resolved (2):** `AcceptanceChain_CaptureAboveEveryPairGap_NineSpacesWithIntendedSharedBoundaries` at 0.55 and 0.65.
- **Unchanged (19 historical):** golden masters, PR49, plane-target, CreateSpaces tilted, PR61 parity, workflow
  parity - same names and messages (only the worktree path in stack traces differs).
- **Unchanged identity (6 motion):** `AcceptanceChain_RigidMotion_SameSpatialResultAfterInverseMotion` x 3 captures
  x 2 motions. The two rotate-Z-45 messages at 0.55/0.65 now also list North0 (88.946 vs 88.383 m3; its south edge
  y -9.46 vs -9.414 - the same untraced 46 mm rotation difference finding 2 records for North2), because North0 now
  exists there.
- **New (4):** `PR61TowersQuantitativeValidationTests.Towers_Gap05_OverAggressive_QuantitativeValidation`,
  `...Towers_Gap04Vs05_QuantitativeDelta`, `TowersBucketLeverDiagnosticTests.Towers_DoubleWallGap_FixAcceptance_Gap05StripSurvives`
  (each: expected 29 cells, actual 30) and `...Towers_NorthStripPair_GapSweep_MergedAt05_NearMissAt047` ("Gap 0.5
  should reduce cells vs 0.47: 30 < 30"). `whole-level-towers.sam` contains the same West block / North0 pair. Probe
  (`towers-probe-*.log`): at doubleWallGap 0.47 base and branch give the same 30 cells, including the north-strip
  west room (78.049 m3); at 0.5 base gives 29 - that room is gone - and the branch gives 30: the room kept,
  x -0.345..3.706, 88.385 m3; every other cell identical. These tests pin the room loss as the expected outcome
  (PR61 comments: "Gap 0.5 (over-aggressive: north-strip west room destroyed)", "-78.049 (west north-strip room
  destroyed)"; the Phase 1d test asserts 29 while titled "StripSurvives"). Not edited at `ff71a70`; reconciled at
  `791bcaa` with owner approval (see Tests).
- No skip changed: the same 2 (`LargePanelSewGuard...SkipsNativeSew`, `NativeMissing...ReportsNativeMissing`).

At `ff71a70` the suite was not green: 29 failures = 19 historical + 6 motion + 4 towers characterizations of the
fixed defect (the last four reconciled at `791bcaa`).

Spatial comparison of the exports (`compare-4d77ef4-vs-ff71a70.log`; determinism control between two base runs
identical): none, 0.20 and **0.45 identical** (processed panels, cells, cluster panels); 0.55 and 0.65 differ only
in the North0 north wall and roof processed panels, the new North0 cell and West3's extra faces for the shared
boundary (8 -> 9 cells, 80 -> 92 cluster panels). Exports read back with matching counts (export test passed).
Clean-stage trace (`probe-base-v1.log` vs `probe-branch-v2.log`): identical at 0.45 and unstamped; at 0.55 only
a577d53d's and 770b8239's clean faces differ.

Evidence (machine-local, copy to keep): `SAM-BIM/.review-nine-space-bucket-followup-20261022/north0-consolidation-follow-20261023/`
(`README.md` indexes it; exports `exports-before-4d77ef4/`, `exports-after-ff71a70/`, `SHA256SUMS.txt`). Earlier
exports in `wall-capture-exports/` and `gate-followup-20261023/` are untouched.

## Risks and limits

- Only this fixture exercises the new paths on real geometry; the unit cases cover the guards. Other models with
  partner ends displaced by an earlier snap, or caps inset <= 0.3 m from a consolidated wall, will now follow where
  they did not before. In the full native suite the only changed outcomes are the four towers tests (the same room
  kept, now reconciled); goldens, PR49, houses, parity and the other consolidation tests are unchanged.
- At 0.55/0.65 Extend3D raises the North0 north wall's top to 18.39 (processed panels only). Mechanism:
  `Panel3DSnapSolver.NearestCoveringCap` picks the nearest cap whose plan BOX overlaps the wall's plan box and lies
  at or above the wall top. The wall's authored top (15.473) is above North0's normalized roof (15.29), so that roof
  never qualifies; at base the wall's box (x 0.129..3.786) overlaps no higher cap and the top stays 15.473; once its
  end follows to x -0.345 its box overlaps the West block's upper roof (box to x 0.179, z 18.34) and the top goes to
  18.34 + 0.05. On towers the base already raised this wall to 27.54. The part above 15.29 is outside every cell: in
  `_02-adjacency-cluster.sam` North0's north boundary stops at 15.29. Existing Extend3D policy, not changed here.
- The wall-end path's dominant-wall landing guard can reject an end that another coplanar wall (not the dominant)
  would support; it then falls back to the base behaviour (left to the extend pass). Conservative, not exercised.
- Pose dependence (equal-area ordering, rotation path) is unchanged and still fails the six motion tests.

## Owner decisions

Resolved 2026-10-23: the owner visually approved the corrected model and ran the workflow; approved the towers test
reconciliation and the authored-uniqueness correction (`791bcaa`). The dominant-run question is answered by the
actual support (Decisions). Remaining: independent review, then any publication decision (none authorized).

## Next step

Independent review of `791bcaa` (this record follows it). No push, PR, merge or deployment is authorized by this branch.
Pose determinism stays its own bounded task. Local branches (guard, test, this) and the machine-local evidence need
explicit transfer before changing machines.
