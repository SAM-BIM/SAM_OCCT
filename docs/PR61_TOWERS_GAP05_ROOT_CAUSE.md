# Towers `doubleWallGap = 0.5` — root causes and the cap-selection / junction-follow rules

Fixture: `Testing/SAM.OCCT.IntegrationTests/Fixtures/whole-level-towers.sam`. Production chain
`Extend3D → Create.AdjacencyCluster` with `minBucketSize 0.4, alignColinearOffset 0.3,
bucketBetweenLevels 0.4, fillMargin 0.4, directionalCapGrow false`.

`doubleWallGap = 0.5` is a valid user input. The lost north-strip room at gap 0.5 was not caused by
the gap being over-aggressive; it was caused by two independent defects that fire once the 0.474 m
wall pair consolidates. Both are fixed here with geometry-based rules; the gap threshold is not
lowered and no fixture coordinates or GUIDs appear in production code.

---

## Root cause A — implicit default consolidation (independent merge blocker)

`Clean3D` stamps `SolverParameter.BucketSize` on its output panels. `Modify.ResolveConsolidationRanges`
read those stamps as per-panel consolidation ranges, and `SnapStage.Clean` armed
`ConsolidateWallStacks` whenever any panel carried a range `> tol` — so wall-stack consolidation ran
on every `Clean3D → Extend3D` chain even at the default `doubleWallGap = 0`.

**Fix.** The explicit `doubleWallGap > 0` is the only arm switch. Stamped ranges still *refine* the
reach of an armed pass (`EffRange`) but never *activate* it.
- `SnapStage.Clean` §2a: gate is `if (doubleWallGap > tol.Distance)`.
- `Panel3DSnapSolver.ConsolidateWallStacks`: early-out no longer consults stamped ranges.

**Invariant:** `doubleWallGap = 0` ⇒ wall-stack consolidation OFF, whatever is stamped.
**Result:** Face3D-home `Clean3D → Extend3D → cluster` returns to 20 spaces (base c643b29 behaviour).

---

## Root cause B — towers gap 0.5 vertical / closure behaviour

At gap 0.5 Stage A consolidates the 0.474 m north-strip pair onto the tower-face plane (correct).
Two downstream defects then fired.

### B1 — missed junction follow (the room-killer)

When the strip's west wall moved onto the tower-face plane, `DragAbuttingWallEnds` must carry the
abutting perpendicular walls' ends onto the new plane. The room's north wall ended **46 mm beyond**
the moved wall's foot end, so the old `PlanDistancePointToSegment > STACK_FOLLOW_END_TOLERANCE (0.02)`
test rejected it and the Z-blind plan loop then parked that end on an upper tower-face line — leaving
the NW corner open, so the room never closed and dropped from the cell complex.

**Fix.** `DraggedEnd`/`TerminatesOnFootLine` split the match into two axes: perpendicular distance to
the moved wall's foot **line** ≤ `STACK_FOLLOW_END_TOLERANCE` (0.02 m, unchanged) **and** along-line
overhang ≤ `STACK_FOLLOW_END_OVERHANG` (0.1 m — the import corner-undershoot). Within the foot span
the behaviour is identical to the old distance-to-segment test; the new constant only widens the
along-line capture past the ends. Direct boundary tests: `WallJunctionFollowTests`.

### B2 — cap selection let walls extend to caps they only graze (the tall walls)

`NearestCoveringCap` accepted any cap whose axis-aligned bounding box overlapped the wall and read the
cap plane's elevation at the wall centre — an extrapolation when the cap does not physically reach
over that point. Once the strip walls sat on the tower-face plane they bbox-grazed the tower's stepped
floor plates by ~40–50 mm and were extended one-to-four storeys past their own ceiling
(z 15.47 → 18.39 / 27.54).

**Final rule (rigid-frame-invariant, uses the actual cap face — `Panel3DSnapSolver.NearestCoveringCap`).**
For each plan-bbox-overlapping cap whose surface over the wall sample is on the grow side:

- **Case A — physical containment.** The wall sample projected onto the cap plane, `(x, y, capZ)`,
  lies on the real cap material (`Face3D.On`, outer-boundary-inclusive, internal openings excluded).
  The wall genuinely sits under/over the cap, so it is a valid target at any vertical gap (a wall grows
  to its own ceiling however far above). A sample under an internal opening (an atrium/stairwell hole)
  is not covered — it falls to Case B, exactly like a sample outside the cap footprint. (An earlier
  version used `Face3D.InRange`, which tests only the outer loop and wrongly accepted an in-opening
  sample at unlimited gap; `WallCapSelectionTests` cases 13–17 pin the fix.)
- **Case B — bounded boundary continuation.** The sample lies outside the real face (a graze). Valid
  only when the cap surface over the sample is within `CAP_LOCAL_LEVEL_CONTINUATION` (2.0 m) of the
  wall extreme — the cap continues the wall's own floor/roof boundary within its local level rather
  than jumping a storey.
- A cap with a null / invalid face (or null plane normal) is skipped entirely.

The selection keeps its nearest-surface preference; only the validity gate is new. There is **no**
use of cap AABB containment, world `normal.Z` flatness, a pitch threshold, or a pitched-cap exemption.

**Why the discriminant is the vertical gap, not a distance.** Measured on the real fixtures at the
wall-to-cap stage: legitimate pitched-roof grazes on the export home fixtures reach up to ~1.57 m of
vertical continuation, while the towers phantom grazes a next-storey plate at ~2.87 m (≈ one 3.05 m
storey). Neither the 3-D distance to the cap face nor the in-plane distance to its boundary separates
the two — a legitimate flat floor tile can be metres from a wall centre in plan while a phantom plate
grazes it to ~40 mm. The vertical continuation gap does separate them, and it is exactly "the cap
elevation belongs to the nearest valid local level interval." `CAP_LOCAL_LEVEL_CONTINUATION = 2.0` sits
between the two measured populations and below one storey pitch. Direct synthetic tests:
`WallCapSelectionTests` (12 cases; the AABB-false-containment, within-band graze, and sub-15° shallow-
roof cases fail at commit 20a7665 and pass after).

---

## Acceptance evidence (production chain, gap sweep)

| gap | cells | west north-strip room | notes |
|---|---|---|---|
| 0.0 | 31 | one 157.6 m³ parent | baseline; two sliver cells; 22↔26 not connected |
| 0.4 | 30 | 78.049 m³ | slivers absorbed, parent split, 22↔26 connected |
| 0.47 | 30 | 78.049 m³ | 0.474 pair not yet merged (near-miss diagnostic) |
| 0.5 | 30 | 88.385 m³ | 0.474 pair merged, room reclaims the void (+10.336); no room lost |

Gap 0.5 keeps the same 30-cell topology as gap 0.4, grows the west room and the southeast room by the
two reclaimed double-wall voids (+20.745 m³ total), removes the two sliver cells, keeps 22↔26, and
holds the strip walls within z≈12.19–15.47. Integration coverage: `PR61TowersQuantitativeValidationTests`,
`TowersGap05StageDiagnosticTests`, `TowersBucketLeverDiagnosticTests`.

---

## Cross-fixture evidence and golden-change classification

The raw (production-default) path is unaffected — all five raw-path golden signatures are unchanged.
The changes below are on the managed diagnostic path and the GH `bucketBetweenLevels = 0.21` path;
each cell difference was matched old↔new by centroid and classified geometrically.

**whole-level-towers, managed (forced, band 0): 21 → 20 cells (−78.783 m³).** The removed cell
(centroid (39.467, −6.160), 78.784 m³, z=[12.384, 15.290]) had all its neighbours unchanged (it
dropped, was not merged). Its band-0 closure depended on a bounding wall grazing a tower floor plate
**≥ 5 m away** (verified: the cell reappears only when `CAP_LOCAL_LEVEL_CONTINUATION` is raised past
5 m, not at 5 m) — i.e. a multi-storey phantom extension, the exact B2 defect class. Classification:
**false closure removed** — the wall no longer reaches a plate it does not sit under. The real room is
preserved on both production paths: raw-first gives 31 cells, and the GH band-0.21 path contains it
(centroid (39.467, −6.160), 82.696 m³) in both old and new.

**two-level-tilted, managed band 0.21: 9 → 10 cells (+112.009 m³).** The added cell (centroid
(43.452, −3.533, 6.143), 112.008 m³, floor area 37.713, z=[4.162, 8.124], 6 faces) is the mirror twin
of the pre-existing (43.452, −22.467, 6.143) cell — identical volume, floor area and Z extent, a
distinct symmetric location, correctly bounded, within the envelope and on the correct upper storey.
Classification: **legitimate room recovery** — rejecting the phantom graze that previously blocked its
closure lets the symmetric room close (and naked edges drop 24 → 21). Accepted.

**whole-level-towers, managed band 0.21: 231 → 228 signature faces.** Cell count (25) and total volume
(9281.107 m³) are identical old↔new; only the face count falls by 3. Classification: **face cleanup
without topology change** — three redundant wall faces left by phantom graze extensions are no longer
produced.

Managed-path golden expectations were updated with these deltas in `GoldenMasterIntegrationTests`.
Permanent geometric evidence for all three classifications lives in the native-gated
`PR61GoldenEvidenceIntegrationTests`, with base/head console captures committed at
`docs/reviews/evidence/PR61_GOLDEN_EVIDENCE_BASE.log` (7a677de, OLD state) and
`docs/reviews/evidence/PR61_GOLDEN_EVIDENCE_HEAD.log` (NEW state). No byte identity is claimed.

---

## Known limitations

- The forced-managed pipeline (`forceManagedPipeline: true`) is a diagnostic tripwire, not a
  production path. On `whole-level-towers` at band 0 it now under-closes the (39.467, −6.160) room
  (previously closed only via the phantom extension); both production paths close it. Per-frame
  extend/fill (which would close it legitimately on the managed path) remains deferred (see the
  Phase 6c deviation note in `TESTING.md`).
- `CAP_LOCAL_LEVEL_CONTINUATION` is a fixed 2.0 m band justified empirically against the current
  fixtures (legitimate continuation ≤ ~1.57 m, cross-storey ≥ ~2.87 m). A fixture with a legitimate
  grazing cap continuation beyond 2.0 m, or a phantom plate less than 2.0 m above a wall, would need
  the band revisited; the band is deliberately below one storey pitch, so a cap a full storey (~3.05 m)
  above a wall is not selected by a graze. It is a bound between the two measured populations, not a
  guarantee that no adjacent-storey surface is ever selectable — a plate 2–3 m above a wall would still
  fall inside the band; the band holds because the real fixtures separate cleanly.
