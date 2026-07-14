# PR #61 towers gap-0.5 — continuation handover (for Opus 4.8 Max)

Date: 2026-07-14. Author: Claude (Fable 5) session on branch `debug/pr61-towers-gap05`.
State: **both root causes identified with stage-level evidence and fixed in the working tree;
verification battery is mid-run and 3 test files still need re-pinning/compile fixes.**

---

## 1. Git state

- Branch: `debug/pr61-towers-gap05` (created from PR #61 head, as instructed)
- HEAD SHA: `7a677de63b611c0baf2996548f1542f65471605a` (nothing committed this session)
- Staged: none. Stashes: 2 pre-existing (unrelated; do not touch).
- Unstaged modified:
  - `SAM_OCCT/SAM.Geometry.OCCT.Solver/Classes/Panel3DSnapSolver.cs` (production — fixes A2/B)
  - `SAM_OCCT/SAM.Geometry.OCCT.Solver/Classes/SnapStage.cs` (production — fix A1)
  - `SAM_OCCT/SAM.Analytical.OCCT.Solver/Modify/Solve.cs` (doc comment only)
  - `Testing/SAM.OCCT.UnitTests/ConsolidateWallStacksTests.cs`
  - `Testing/SAM.OCCT.IntegrationTests/GoldenMasterIntegrationTests.cs`
  - `Testing/SAM.OCCT.IntegrationTests/PR61Face3DHomeParityTests.cs`
  - `Testing/SAM.OCCT.IntegrationTests/PR61TowersQuantitativeValidationTests.cs`
  - `Testing/SAM.OCCT.IntegrationTests/TowersBucketLeverDiagnosticTests.cs`
- Untracked (new diagnostic tests, keep):
  - `Testing/SAM.OCCT.IntegrationTests/TowersGap05StageDiagnosticTests.cs` (stage-by-stage harness)
  - `Testing/SAM.OCCT.IntegrationTests/FlatCapCandidateProbeTests.cs` (cap-candidacy diff probe)

⚠ `PR61TowersQuantitativeValidationTests.cs` currently **does not compile**: the rewritten
gap-0.5 test references a constant `NorthStripWestVolume05` that was never added (see §9 step 1).

---

## 2. Fixed reproduction

Fixture `Testing/SAM.OCCT.IntegrationTests/Fixtures/whole-level-towers.sam`, chain
`Extend3D → Create.AdjacencyCluster` (RunUserChain in TowersBucketLeverDiagnosticTests):

```
minBucketSize (bucket) = 0.4      alignColinearOffset (align) = 0.3
bucketBetweenLevels    = 0.4      fillMargin                  = 0.4
directionalCapGrow     = false    doubleWallGap               = {0 | 0.4 | 0.5}
OcctBuildOptions: AvoidInternalShapes=false, SewBeforeBuild=true, SewingTolerance=0.01
```

Storey pitch 3.05; podium storey z = 12.24..15.29; tower top plate z=27.49, roof 30.46.
`Tolerance.Distance = 1e-6`. Podium wall raw tops 15.41–15.47 (pierce the 15.29 ceiling by
0.12–0.18 — normal for this import; kernel trims).

## 3. The actors (source index = solver source index, from the stage harness at gap 0.5)

| role | src | GUID | raw geometry |
|---|---|---|---|
| Tower east face L1 (consolidation dominant) | 3 | `edb39f15-e5a1-4bae-8481-3aec45ed2b1e` | x=-0.345, y=[-26.5,0.5], z=[12.24,15.21] |
| North-strip west wall (moved member) | 199 | `ca160254-72b3-4f3e-987e-a15f5ce15db9` | plane x=+0.1289, foot y=[-9.506,-2.306], z≈[12.24,15.47] |
| West room SOUTH wall | 196 | `b03bc2a4-8624-4354-aaab-33ddca71d81b` | y=-9.506 (clean mid-plane y=-9.414), x=[0.129,3.657], z=[12.503,15.473] |
| West room NORTH wall | 198 | `a577d53d-de51-4853-924b-a7a873be8e8e` | y=-2.306 (clean mid-plane y=-2.260), x=[0.129,3.657], z=[12.503,15.473] |
| Block wall (0.345 pair, merges at ≥0.4) | 78 | (not extracted) | plane x≈0.0003 |
| Vanishing cell (pre-fix) | — | — | west north-strip room, centre (1.716365, -5.905851, 13.765), 78.0488 m³ |

Tower east faces STEP WEST as they rise: x = -0.345 (L1), -0.344 (L2), -0.302 (L3), -0.223 (L4),
-0.117 (L5), +0.003 (L6/L7). Tower plates overhang east of the face per level (pre-Fill):
z=15.29 plate reaches x=+9.917 (covers strip), z=18.34 → -0.302, 21.39 → -0.223, 24.44 → -0.117,
27.49 → +0.003, 30.46 → +0.003. East tower mirror: plates near x≈46.0.

## 4. Root cause A (independent merge blocker — Sol finding, CONFIRMED + FIXED + VERIFIED)

Clean3D stamps `SolverParameter.BucketSize` on outputs → `Solve.ResolveConsolidationRanges`
read stamps as per-panel consolidation ranges → `SnapStage.Clean` armed `ConsolidateWallStacks`
when ANY range > tol **even at doubleWallGap=0** → Face3D-home B-workflow
(Clean3D → Extend3D(band 0.21) → cluster) lost a space: base 20 → head 19.

Fix (implemented): the explicit `doubleWallGap > 0` is the ONLY arm switch; stamps refine an
armed pass, never activate it.
- `SnapStage.cs` `Clean` (§2a, ~line 157): gate is now `if (doubleWallGap > tol.Distance)` only.
- `Panel3DSnapSolver.cs` `ConsolidateWallStacks` early-out (~line 3290): `anyRange` arm removed.
- Docs updated on `ConsolidationRanges` property + `Solve.ResolveConsolidationRanges`.

Verified: `Face3DHome_Parity_PR_Branch_RecordsCurrentState` → solver=20, A-matched=19 (pre-existing),
**B-clean-extend=20** (restored; test re-pinned 19→20). `Towers_NorthStripPair_StampedBucket_InertWithGlobalOff`
(new) passes; `..._MergesWhenArmed` (renamed, arm=0.01) passes; 32/32 consolidation unit tests pass.
⚠ Face3D parity was verified BEFORE fix B below landed — must re-run (§9 step 4).

## 5. Root cause B (towers gap 0.5) — proven causal chain

At gap 0.5 Stage A consolidates BOTH pairs onto src3's plane (CleanRecords):
`stack-consolidated src=78 → backer=3 moved=0.3449` and `src=199 → backer=3 moved=0.4738`
(at gap 0.4 only src78; at 0.47 near-miss diagnostic — separation 0.4738 > 0.47).
All consolidation gates for the 0.474 pair PASS at 0.5 (parallel ✓, separation 0.4738 ≤ 0.5,
in-plane overlap ✓, ratio-vs-smaller ≥ 0.97 ✓, travel ≤ cap ✓). Z ranges overlap at Stage A
(both storey-1 panels) — consolidation ordering is NOT the defect. The user's GH observation
"pair remains separate at 0.5" is explained: their run had doubleWallGap unset (=0).

Then two INDEPENDENT defects (pre-fix state):

**B1 — room-killer (missed junction follow → open NW corner).**
`DragAbuttingWallEnds` drags abutting ends onto the new plane, but src198's west end
(0.129, -2.260) lies **46 mm beyond** src199's foot end (foot stops at y=-2.306) →
`PlanDistancePointToSegment > STACK_FOLLOW_END_TOLERANCE (0.02)` → not dragged
(src196's end at y=-9.414 is INSIDE the foot span → dragged to -0.345 ✓).
`ExtendWalls` (2D plan loop, Z-blind by design) then extends the north wall's west end
0.129 → **-0.047** = the tower L6/L7 face line (x=+0.003) − 0.05 overshoot — a line 15 m above;
the true target (-0.345) needs 0.474 > DEFAULT_MaxExtension 0.4, so the plan loop can never
close it. Result: NW corner open by 0.298 m → west room unclosed → cell vanishes (29 cells).

**B2 — abnormally tall walls (graze cap selection).**
`NearestCoveringCap` accepted any cap whose bbox merely intersects the wall bbox
(tol 1e-6) and read its plane Z at the wall centre (extrapolation). First invalid stage per wall
= **wall-to-cap `Extend` (ConditionStage step 2)**; enabling footprint change one stage earlier:
- src196 (south): dragged to x=-0.345 in Stage A → bbox grazes z=18.34 plate by 43 mm →
  `Top 15.4728 → 18.3900 (targetIdx 50)` — one storey too far.
- src198 (north): plan-loop end at -0.047 → grazes ONLY the z=27.49/30.46 plates (+0.003) →
  `Top 15.4728 → 27.5400 (targetIdx 68)` — four storeys (final z=[12.19,27.54], h=15.35).
- src3 (tower face): centre IS under the tower plates (containment, not graze) →
  `Top 15.473 → 18.39` at ALL gaps — pre-existing, baked into pinned baselines, must stay.
- Same defect class exists at gap 0/0.4: e.g. wall y=-19.856 (dragged at 0.4 via 0.345 pair) →
  18.39; and TWO east-tower-junction walls at gap 0 — centres (43.6809,-9.8001) and
  (43.6809,-1.8001), tops 15.41 → 27.49 via ~50 mm graze of east plates (was baked into the
  managed towers golden).

Stage-by-stage MinZ/MaxZ (west room walls, gap 0.5): RAW 12.503/15.473 → CLEAN (A) unchanged
heights (only plan moves) → after ExtendWalls unchanged → after Extend PRE-FIX: south 12.19/18.39,
north 12.19/27.54 → POST-FIX (both fixes): south and north **12.190/15.473**, feet reach
x=-0.345, tower face keeps 12.19/18.39. Fill: cap growth only.

**Fixes (implemented):**
- A2 (`Panel3DSnapSolver.cs`): `DraggedEnd` now uses `TerminatesOnFootLine` — perpendicular
  distance to the old foot LINE ≤ `STACK_FOLLOW_END_TOLERANCE` (0.02) AND along-line overhang ≤
  new const `STACK_FOLLOW_END_OVERHANG = 0.1` (covers the 46 mm corner undershoot; within-span
  behaviour identical to the old segment test — strict superset of old matches).
- B (`Panel3DSnapSolver.cs`): `NearestCoveringCap` gained `grazeContinuationBand` parameter
  (caller passes `max(overshoot, roofOvershoot)` = 0.5): a cap whose plan bbox does NOT contain
  the sample point (wall centre) is admissible only if `|capZ − wallExtreme| ≤ band`.
  Near-graze caps stay load-bearing (whole-level-flat corridor walls take their ±0.05 overshoot
  from coplanar neighbour tiles at distance 0–0.08 — hard containment broke flat 22/0 → 16/8,
  which is why v1 containment was replaced by this banded rule); far grazes (2.87–12.07 m,
  the towers defect) are rejected. Containing caps trusted at any distance (pre-existing rule).

## 6. Verified results (commands run, all `dotnet test … -c Debug --no-build --filter …`)

| run | result |
|---|---|
| Build solution | fails only at Grasshopper native rebuild step (build-native.ps1, pre-existing env issue); all managed projects build clean individually |
| Consolidation unit tests (32) | PASS post-fix |
| Face3D parity | PASS (B=20) — **before fix B landed; re-run required** |
| Stamped towers tests (new/renamed) | PASS |
| Golden masters (15) | **PASS 15/15** after 3 documented re-pins (below); raw path 5/5 byte-identical throughout |
| PR61 gap 0 baseline | PASS byte-identical (31 cells, 9605.396, slivers 2, no 22↔26) |
| PR61 gap 0.4 | PASS byte-identical (30 cells, 9620.406, same 3 removed cells) — fixes did NOT move gap 0.4 |
| PR61 gap 0.5 (old pins) | FAILS as expected: actual now 30 cells / 9641.151 (+0.3722%) / cellArea 3171.799 / panelArea 1330.101 / slivers 0 / 22↔26 True / removed = same 3 as 0.4; 0.4→0.5 delta +20.745 m³, removed-vs-0.4 = 0 |
| Stage harness (3 gaps) | PASS; replication cross-check == real Extend3D output |

Golden re-pins already made in `GoldenMasterIntegrationTests.cs` (all justified in comments):
- towers [managed]: 21c/8n/8689.707 → **20c/8n/8610.924** (removes one −78.78 m³ artifact cell
  enclosed by the two east-junction phantom walls; diagnostic forced-managed path only)
- two-level-tilted [managed-0.21]: 9c/24n/2082.410/412f → **10c/21n/2194.4185132571847/397f** (strictly better closure)
- towers [managed-0.21]: cells/naked/volume byte-identical, faces 231 → **228**

## 7. Current hypotheses status (task list from the brief)

1 cap from another level via weak XY overlap — **CONFIRMED** (B2, fixed). 2 consolidation groups
across levels — refuted (same-storey members). 3 consolidation changes XY before cap selection —
confirmed as trigger, not defect. 4 junction dragging — confirmed twofold: missed drag kills the
room (B1); performed drag enables the graze (B2). 5 level bucketing global elevations — refuted
(frames/groups per storey, band 0.4 < pitch 3.05). 6 NormalizeCaps/Fill changing cap association —
refuted (cap-normalize moves ≤ 0.196 in-level; Fill runs after Extend). 7 room fails due to a
vertically incorrect wall — refined: room fails due to the HORIZONTAL NW-corner gap (B1); the
vertical defect (B2) is real but non-fatal to closure (fix A2 alone already restored 30 cells).

## 8. Tests added / modified this session

- NEW `TowersGap05StageDiagnosticTests.cs` — per-stage tables + cap-candidate audit (3 gaps). PASS.
- NEW `FlatCapCandidateProbeTests.cs` — old-vs-new candidacy diff (flat/two-level/towers); its
  inline "NEW gate" mimic still encodes v1-containment+band — update or delete before merge. PASS.
- `ConsolidateWallStacksTests.cs` — stamped-range tests re-contracted to explicit-arm (gap 0.01),
  NEW `ConsolidateWallStacks_StampedRange_GlobalZero_Inert`, `Clean_StampedRange_GapZero_StaysInert`,
  `Clean_StampedRange_ArmedSmallGap_ActivatesConsolidation`. ALL PASS.
- `TowersBucketLeverDiagnosticTests.cs` — H renamed `..._MergesWhenArmed` (gap 0.01), NEW H2
  `..._InertWithGlobalOff`. BOTH PASS. ⚠ other tests in this file not yet re-run post-fix.
- `PR61Face3DHomeParityTests.cs` — spacesB pin 19 → 20 + docs. PASS (pre-fix-B run).
- `GoldenMasterIntegrationTests.cs` — 3 re-pins per §6. ALL 15 PASS.
- `PR61TowersQuantitativeValidationTests.cs` — class doc + consts (Volume05=9641.151,
  CellFloorArea05=3171.799) updated; `Towers_Gap05_OverAggressive_QuantitativeValidation` rewritten
  as `Towers_Gap05_Fixed_QuantitativeValidation` (30 cells, west room survives+grows);
  `Towers_Gap04Vs05_QuantitativeDelta` rewritten (30→30, Empty(removed), +20.745 attribution).
  **DOES NOT COMPILE** — missing const `NorthStripWestVolume05` (west05 exact volume unknown; ~88.4
  expected = 78.049 + ~10.337). NOT YET RUN.

## 9. Exact next steps (in order)

1. Add `private const double NorthStripWestVolume05 = <actual>;` to
   `PR61TowersQuantitativeValidationTests.cs`. Get `<actual>` by building and running
   `Towers_Gap04Vs05_QuantitativeDelta` — it prints `west room: 78.049 → X (+g)`; also confirm the
   west05 FindCell probe `(NorthStripWestX − 0.237, −5.9059, 13.765, r=0.45)` matches, then pin
   west growth (`westGrowth`) if you add an assert (expected ≈ +10.337 = 20.745 − 10.408).
2. Update `Towers_DoubleWallGap_FixAcceptance_Gap05StripSurvives` (TowersBucketLeverDiagnosticTests
   ~line 571): gap 0.5 now expects **30** cells (currently asserts 29); keep 0.47 → 30.
3. Update `Towers_NorthStripPair_GapSweep_MergedAt05_NearMissAt047` (~line 910): asserts
   `cellsAt05 < cellsAt047` — now 30 = 30. Re-express "pair merges at 0.5" via the
   stack-consolidated diagnostic (present at 0.5, absent at 0.47) and keep the near-miss assert.
4. Re-run: PR61Face3DHomeParityTests (fix B landed after its last green run),
   Towers_DoubleWallGap_FillMargin_DefectDiagnostic (diagnostic, should pass),
   MultiFixtureAdjacencyDiagnosticTests + WorkflowParityIntegrationTests (towers row),
   ControlledWorkflowAcceptance/Baseline (9/9 spaces contract), Extend3DCensus (pins fast/plane
   counts — may shift with NoTarget changes), AutoTune suites, StageReport tests.
5. Add the focused regression test the brief requires (new test in PR61 quantitative file or the
   stage harness): pin at gap 0.5 the three wall Z-ranges by stable centroid —
   south (1.706,-9.414) z ∈ [12.0..12.3, 15.3..15.6]; north (1.706,-2.260) same;
   feet MinX ≤ -0.34; tower face (−0.345,−13.2) may keep z top ≤ 18.5; assert no wall in the
   strip region spans > 1.5×storey (4.575) except the tower face; assert west-room volume/floor
   area and 22↔26 (§8 test already covers counts/volumes).
6. Run the full battery: `dotnet test` UnitTests, IntegrationTests (includes goldens + perf guard;
   set `SAM_OCCT_SKIP_PERF=1` only if the agent machine is constrained), GrasshopperTests.
   Sibling SAM repos must stay built at `..\SAM\build` (already true on this machine).
7. Produce the investigation deliverables in the PR/debug report: the §5 comparison table,
   first-invalid-stage statement (wall-to-cap Extend; statement = `ExtendTopTo(capBox.Max.Z +
   overshoot)` after `NearestCoveringCap` graze acceptance), the two root causes reported
   SEPARATELY (Root cause A / Root cause B per Sol's instruction), cross-fixture risk note
   (managed-path golden deltas §6 — raw/production path untouched 5/5), and the A/B/C/D matrix
   (A: towers chain uses raw panels — no stamps — so A≡B for this fixture; state that explicitly).
8. Do NOT merge. Finish with `ROOT CAUSE FIXED — READY FOR SOL REVIEW` only after §9.1–9.6 are
   green; otherwise `ROOT CAUSE NOT YET PROVEN`.

## 10. Working notes

- Diagnostic logs from this session: `%TEMP%\claude\defect-diag.log`, `stage-diag*.log`,
  `flat-probe*.log`, `towers-probe.log`, `pr61-post.log` (post-fix towers metrics).
- The user's Rhino section observation (bucket_=0.5, band=0.5, gap unset): double line at the
  junction = the un-merged 0.474 pair at gap 0 (correct behaviour); "wall not extended to floor"
  = candidate for the same graze/NoTarget family — worth one check at those exact GH params after
  step 6 (not blocking).
- Never reduce doubleWallGap or add fixture-specific coordinates/GUIDs to production code.
