# PR #61 — towers `doubleWallGap=0.5`: root-cause report

Branch `debug/pr61-towers-gap05` (from PR #61 head `7a677de`). Fixture
`Testing/SAM.OCCT.IntegrationTests/Fixtures/whole-level-towers.sam`.

Two independent defects were found and fixed. They are reported **separately** below, as
requested. The headline conclusion reverses the PR #61 premise: **`doubleWallGap = 0.5` is not
inherently over-aggressive.** The lost room was caused by two extension/junction defects that fire
once the 0.474 m pair consolidates; with those fixed, gap 0.5 keeps every legitimate room and
removes the intended double-wall artifacts.

---

## Reproduction

`Extend3D → Create.AdjacencyCluster`, parameters:

```
minBucketSize = 0.4   alignColinearOffset = 0.3   bucketBetweenLevels = 0.4
fillMargin    = 0.4   directionalCapGrow  = false doubleWallGap = {0 | 0.4 | 0.5}
OcctBuildOptions: AvoidInternalShapes=false, SewBeforeBuild=true, SewingTolerance=0.01
```

Storey pitch 3.05 m; podium storey z≈12.24–15.29; tower plates step west as they rise
(x = -0.345, -0.344, -0.302, -0.223, -0.117, +0.003 for z = 15.29 … 30.46).

---

## Root cause A — implicit default consolidation (independent merge blocker)

**Symptom.** The default `Clean3D → Extend3D → CreateAdjacencyCluster` workflow regressed the
Face3D-home fixture from 20 spaces (base) to 19 (PR head), with `doubleWallGap` left at its
default 0.

**Mechanism.** `Clean3D` stamps `SolverParameter.BucketSize` on its output panels (the snap
capture width). `Modify.ResolveConsolidationRanges` reads those stamps as per-panel consolidation
ranges, and `SnapStage.Clean` armed `ConsolidateWallStacks` whenever *any* panel carried a range
`> tol` — so wall-stack consolidation ran on **every** `Clean3D → Extend3D` chain even though the
user never opted in (gap 0). A legacy feature (`BucketSize`) silently activated a new one
(`doubleWallGap`).

**Fix (smallest correction).** The explicit `doubleWallGap > 0` is now the **only** arm switch.
Stamped ranges still *refine* the reach of an armed pass (`EffRange`), but never *activate* it.
- `SnapStage.Clean` (§2a): gate is `if (doubleWallGap > tol.Distance)`.
- `Panel3DSnapSolver.ConsolidateWallStacks`: early-out no longer consults `anyRange`.
- Doc updated on `ConsolidationRanges` and `Modify.ResolveConsolidationRanges`.

**Invariant restored:** `doubleWallGap = 0` ⇒ wall-stack consolidation OFF, whatever is stamped.

**Result.** Face3D-home B-clean-extend: base = 20, head = **20** (re-pinned). Stamped ranges remain
usable as a deliberate opt-in when the global gap is armed (`>0`).

---

## Root cause B — towers gap 0.5 vertical/closure behaviour

At gap 0.5 Stage A correctly consolidates **both** near-parallel pairs onto the tower-face plane
(src 3): the 0.345 m block pair (`src 78`, moved 0.345) and the 0.474 m north-strip pair
(`src 199`, moved 0.474). Every consolidation gate passes; this is intended. Two downstream defects
then fired.

### Wall of interest

| role | src | GUID | raw z |
|---|---|---|---|
| tower east face L1 (dominant) | 3 | `edb39f15-e5a1-4bae-8481-3aec45ed2b1e` | 12.24–15.21 |
| north-strip west wall (moved) | 199 | `ca160254-72b3-4f3e-987e-a15f5ce15db9` | 12.24–15.47 |
| west room SOUTH wall | 196 | `b03bc2a4-8624-4354-aaab-33ddca71d81b` | 12.50–15.47 |
| west room NORTH wall | 198 | `a577d53d-de51-4853-924b-a7a873be8e8e` | 12.50–15.47 |

### B1 — the room-killer (missed junction follow → open NW corner)

After src199 moves onto the tower-face plane, `DragAbuttingWallEnds` must carry the abutting
perpendicular walls' ends onto the new plane. The south wall (src196) end at y=-9.414 lay *inside*
src199's foot span and was dragged to x=-0.345 correctly. The **north** wall (src198) end at
y=-2.306 lay **46 mm beyond** src199's foot end (the moved wall's face stops 46 mm short of the
corner it turns) → the old `PlanDistancePointToSegment > STACK_FOLLOW_END_TOLERANCE (0.02)` test
rejected it → not dragged. The Z-blind plan loop (`ExtendWalls`) then extended the north wall's
west end to the nearest wall *line* in plan — the tower L6/L7 face at x=+0.003 — 15 m above,
because the true target (x=-0.345) needs 0.474 m > `DEFAULT_MaxExtension` 0.4. The NW corner was
left open by ~0.298 m ⇒ the west room never closed ⇒ dropped from the cell complex (29 cells).

**Fix.** `DragAbuttingWallEnds`/`DraggedEnd` now use `TerminatesOnFootLine`: perpendicular
distance to the foot **line** ≤ `STACK_FOLLOW_END_TOLERANCE` (0.02, unchanged) **and** along-line
overhang ≤ new `STACK_FOLLOW_END_OVERHANG = 0.1` (the import corner-undershoot; observed 0.046 m).
Within the segment span the behaviour is byte-identical to the old distance-to-segment test — the
new constant only widens the along-line capture past the ends. No fixture coordinates or GUIDs.

### B2 — abnormally tall walls (graze cap selection)

**First invalid stage: wall-to-cap extension** (`ConditionStage` step 2 →
`Panel3DSnapSolver.Extend` → `ExtendWallToNearestCap`). The offending statement is the
`ExtendTopTo(capBox.Max.Z + overshoot)` (and its bottom mirror) executed after
`NearestCoveringCap` returns a cap the wall only bbox-**grazes**.

`NearestCoveringCap` accepted any cap whose plan bbox merely intersected the wall bbox (tol 1e-6)
and read the cap plane's Z **at the wall centre** — an extrapolation when the cap does not
physically reach over that point. Once src196/src198 sat on the tower-face plane, they grazed the
tower's stepped floor plates by ~40–50 mm in plan and were extended one-to-four storeys past their
own ceiling:

| wall | pre-fix Extend | post-fix Extend |
|---|---|---|
| src196 south (centre 1.706,-9.414) | Top 15.47 → **18.39** (plate z=18.34) | 15.47 → **15.47** |
| src198 north (centre 1.706,-2.260) | Top 15.47 → **27.54** (plate z=27.49) | 15.47 → **15.47** |
| src3 tower face (centre -0.345,-13.2) | 15.47 → 18.39 (CONTAINS sample) | 15.47 → 18.39 (unchanged — legitimate) |

**Fix.** `NearestCoveringCap` gained a `grazeContinuationBand` parameter (caller passes
`max(overshoot, roofOvershoot)` = 0.5). A **flat** (near-horizontal) cap whose footprint does
**not** contain the sample point is admissible only if its surface lies within the band of the wall
extreme — it may only *continue* the wall's own boundary (the near-graze coplanar-neighbour-tile
case whole-level-flat's corridor walls legitimately rely on), never *relocate* the wall to another
storey. A cap that **contains** the sample point is trusted at any distance (pre-existing rule, so
the tower face at src3 is unaffected).

**Flat-only discriminator (required, verified).** The far-graze rejection is restricted to flat
caps (normal within `CapFlatnessConeTolerance` = 15° of vertical). A **pitched** roof grazed in
plan is exempt: its plane genuinely rises across the wall, and a wall reaching a roof it only grazes
is exactly the E2 sloped-plane target the real-export home fixtures depend on. Without this
restriction the band regressed the managed-path home fixtures (`Revit-home-panels` 14/0 → 14/11
naked; `AdjacencyCluster-home` 18 → 17 cells) — a flat plate has a single elevation, so a far graze
necessarily lands on another storey, whereas a pitched roof's extrapolated plane over the wall is a
legitimate target. The towers plates are perfectly horizontal (normal.Z = 1), so the towers fix is
unaffected by the restriction.

### B causal chain (revised hypothesis — confirmed)

```
0.474 m pair consolidates (correct)
  → north wall's junction end 46 mm past the foot end is NOT dragged (B1)
  → plan loop parks that end on an upper tower face line (open NW corner)  → room lost
  and, independently,
  → the room's side walls graze upper tower plates and extend cross-storey (B2)
```

Fix A2 (B1) alone restores the 30-cell topology; fix B2 additionally corrects the wall heights.
Both are needed for correct geometry.

---

## A/B/C/D configuration matrix

| cfg | gap | code | towers cells | west room | notes |
|---|---|---|---|---|---|
| A | 0 | current | 31 | 157.574 (one parent) | baseline; 2 slivers; no 22↔26 |
| B | 0 | consolidation correctly disabled | 31 | 157.574 | **identical to A** — the towers Extend3D chain feeds RAW panels (no BucketSize stamps), so root cause A never armed on this fixture. A≡B here; A vs B differs only on stamped inputs (e.g. Clean3D outputs / Face3D-home). |
| C | 0.4 | fixed | 30 | 78.049 | slivers absorbed, north-strip parent splits, 22↔26 joined; **byte-identical to pre-fix** |
| D | 0.5 | fixed | 30 | 88.385 | as C plus the 0.474 pair reclaimed (+10.336 west, +10.408 SE); no room lost |

Pre-fix D was 29 cells (west room destroyed). The A≡B equality on this fixture is why root cause A
had to be proven on Face3D-home (a stamped-input path), not on towers.

---

## Cross-fixture risk

The production/raw path is untouched: all five raw-path golden masters are byte-identical. The
graze-continuation band changes only the managed conditioning path, and only where a wall was
selecting a cap it does not lie under. Managed-path golden deltas (documented, intended):

- `whole-level-towers` [managed]: 21c/8n/8689.707 → **20c/8n/8610.924** (removes one −78.78 m³
  artifact cell that two east-junction phantom walls had enclosed; forced-managed diagnostic path).
- `whole-level-towers` [managed-0.21]: cells/naked/volume byte-identical; faces 231 → **228**
  (phantom graze-extended wall faces gone).
- `two-level-tilted` [managed-0.21]: 9c/24n → **10c/21n** (one more room closes, three fewer naked
  edges — strictly better once cross-storey extensions are removed).

No other fixture moved. `whole-level-flat` managed 22/0 is preserved specifically because the band
(not hard containment) keeps the corridor walls' legitimate near-graze cap targets.

---

## Tests (added / modified)

- `ConsolidateWallStacksTests` — stamped-range cases re-contracted to explicit-arm; new
  `..._StampedRange_GlobalZero_Inert`, `Clean_StampedRange_GapZero_StaysInert`,
  `Clean_StampedRange_ArmedSmallGap_ActivatesConsolidation`.
- `TowersBucketLeverDiagnosticTests` — `..._StampedBucket_MergesWhenArmed` (was `_MergesWithGlobalOff`),
  new `..._StampedBucket_InertWithGlobalOff` (root cause A pin);
  `..._FixAcceptance_Gap05StripSurvives` now 30 cells + room-survives asserts;
  `..._GapSweep_MergedAt05_NearMissAt047` now asserts equal topology + west-room void reclaim.
- `PR61Face3DHomeParityTests` — B-clean-extend pinned 20 (was 19) + rationale.
- `GoldenMasterIntegrationTests` — three managed re-pins (above), justified in comments.
- `PR61TowersQuantitativeValidationTests` — gap-0.5 test rewritten to the fixed outcome
  (30 cells, west room survives + grows to 88.385); delta test rewritten (30→30, no removed cells,
  +20.745 attribution).
- NEW `TowersGap05StageDiagnosticTests` — stage-by-stage tables + hard regression
  `Towers_Gap05_StripRoomWalls_StayWithinLocalStorey`.
- NEW `FlatCapCandidateProbeTests` — old-vs-new cap-candidacy diff probe (diagnostic).

## Test results

Full battery, all green (native present):

| suite | result |
|---|---|
| `SAM.OCCT.UnitTests` | **617 passed, 0 failed** |
| `SAM.OCCT.GrasshopperTests` | **15 passed, 0 failed** |
| `SAM.OCCT.IntegrationTests` | **269 passed, 0 failed, 2 skipped** (18m43s) |

The 2 integration skips are environmental, not coverage gaps: `NativeMissingIntegrationTests`
(inverse-gated — skips when native IS available) and `LargePanelSewGuardIntegrationTests`
(size-gated). The integration run includes all 15 golden-master signatures, the PR #61 towers
quantitative suite (gaps 0 / 0.4 / 0.5 + delta), `WorkflowParityIntegrationTests` (9 fixtures),
`Extend3DPlaneTargetIntegrationTests` (the E2 home fixtures), `ControlledWorkflow*` (9/9), the
`Benchmark1500` perf guard, and the new `Towers_Gap05_StripRoomWalls_StayWithinLocalStorey`
regression.

### Verification note — flat-only refinement (found and fixed during the battery)

The first full-suite run surfaced two managed-path regressions from the initial (flat-agnostic)
graze band: `Revit-home-panels` (14/0 → 14/11 naked) and `AdjacencyCluster-home` (18 → 17 cells) —
both real-export fixtures with pitched roofs. Root cause: the band rejected the legitimate E2
sloped-plane cap targets. Fixed by restricting the far-graze rejection to flat caps (§ Root cause B
› B2); re-verified all affected fixtures green. (A separate one-off run showed ~56 spurious
fixture-load failures — `Convert.ToSAM` returning empty under transient file contention; these did
not recur on clean runs and are unrelated to the change. Every affected test passes deterministically
in isolation and in the clean full run.)
