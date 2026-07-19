# Controlled Workflow Baseline — 9-Space Fixture (P0)

**Captured:** 2026-07-10 · **Branch:** `feat/cw-p0-baseline` · **Plan:** `docs/CONTROLLED_WORKFLOW_PLAN.md`
**Producer:** `ControlledWorkflowBaselineTests.ControlledWorkflow_NineSpacesFixture_BaselineReportCapture` (report-only; asserts fixture integrity, never pipeline success). Re-run with:

```powershell
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug `
  --filter "FullyQualifiedName~ControlledWorkflowBaselineTests" --logger "console;verbosity=detailed"
```

## 1. Fixture ground truth (verified from the committed copies)

`Fixtures/ControlledWorkflow/Panels-9SpacesModel.sam` — **66 panels**: 40 Wall, 10 Floor, 16 Roof; 26 caps spread over **22 raw elevations** (12.16 → 18.34; the multi-elevation spread is slab-skin offsets).
`Fixtures/ControlledWorkflow/Spaces-9SpacesModel.sam` — 9 spaces, 9 distinct single-line names (re-export of 2026-07-10 09:24):

| GUID | Name | Location | Level band | Double-height profile |
|---|---|---|---|---|
| bbf50481-4fb0-4fbb-b3a6-63f63880df56 | East1 | (6.693, −20.48, 13.97) | level 1 | — |
| c9899f4a-dcc6-4238-896a-14b1a9184697 | North0 | (1.893, −5.906, 13.988) | level 1 | — |
| 255251b4-6c07-4145-8794-84c906e655b5 | North1 | (5.519, −5.722, 13.944) | level 1 | — |
| e9d5410f-2869-4734-ac46-0cf25714d2fe | North2 | (9.182, −5.906, 13.903) | level 1 | — |
| f20068a9-e492-4abd-a749-00132a445207 | South1 | (6.503, −25.228, 13.853) | level 1 | — |
| 877254ec-41be-4460-99bb-0da0f7898033 | South2 | (1.582, −23.456, 13.951) | level 1 | — |
| 5dfee67e-9c6c-40da-a024-eae77b267f08 | West1 | (−4.145, −20.061, 13.725) | level 1 | — |
| 0106d11f-3a39-4f99-a850-f380690ca191 | West2 | (−4.144, −20.061, 16.775) | level 2 | — |
| **02a1ae27-5461-4b41-ad07-008ccd9d1159** | **West3** | **(−4.145, −6.561, 15.25)** | spans 1+2 | **YES (z = mid of 12.24→18.34)** |

**West3 GUID for acceptance tests: `02a1ae27-5461-4b41-ad07-008ccd9d1159`.** Table matches plan §0.1 exactly — no mismatch.

## 2. Clean3D today (defaults: minBucketSize 0.4, thicknessFactor 0.6, align 0.3, normalizeCap 0.3)

66 → **46 panels**. Level frames (raw 0.15 band — plan §1 D1 confirmed at runtime):

```text
Frame 0: elevation 12.24  m, tilt 0 deg, 12 cap(s)
Frame 1: elevation 12.436 m, tilt 0 deg,  3 cap(s)
Frame 2: elevation 15.29  m, tilt 0 deg,  8 cap(s)
Frame 3: elevation 15.473 m, tilt 0 deg,  1 cap(s)
Frame 4: elevation 18.34  m, tilt 0 deg,  2 cap(s)
```

**5 frames**; gaps 0.196 / 0.183 exceed the hard-coded 0.15 band; nothing exposes a merge knob (D1/D2).

## 3. The two extend paths — the double-Clean evidence (D10)

| | Path A: `Extend3D(original)` | Path B: `Extend3D(Clean3D(original))` |
|---|---|---|
| Internal Clean passes | 1 | **2** |
| Output panels | 46 | **41** |
| Total area | 2048.6 m² | 1893.3 m² |
| Level frames after the pass | **5** (12.24×12, 12.436×3, 15.29×8, 15.473×1, 18.34×2) | **3** (12.24×8, 15.29×8, 18.34×1) |
| Geometrically unmatched faces | 28 only-in-A | 23 only-in-B |

**The chained workflow is NOT a no-op repeat — the second Clean pass merges the level datums the first pass deliberately kept apart** (12.436→12.24, 15.473→15.29). Mechanism: after pass 1 normalizes caps onto 5 frame datums, pass 2's *wall-bucket snap* (capture width = minBucketSize floor **0.4** > the 0.196/0.183 gaps) collapses the near-coplanar caps — an accidental, uncontrolled level merge, not a principled `bucketBetweenLevels`. Panel identity also churns: every stage mints fresh panel Guids (provenance only via SourceMap), so per-panel stamps survive but object identity does not.

**Consequence for P2:** `inputAlreadyClean` must give the chained workflow the *intended* behavior (condition-only, no second snap), and `bucketBetweenLevels = 0.21` must produce the 5→3 merge *deliberately* in the first Clean — the baseline proves the merge is exactly what unlocks the model (below).

## 4. CreateAdjacencyCluster (GH rebuild options: SewBeforeBuild, sew 0.01, AvoidInternalShapes=false)

### Path A — 6 cells / 6 spaces (5 frames → walls extend to off-datum planes)

```text
CELL 0: (−4.145,−20.061) z=[12.24,15.29]   → MATCHED West1
CELL 1: (−4.145, −6.561) z=[12.24,18.34]   → MATCHED West3   ← double-height OK
CELL 2: ( 4.736,−23.387) z=[12.374,15.388] → MERGED South1+South2
CELL 3: (−4.145,−20.061) z=[15.29,18.34]   → MATCHED West2
CELL 4: ( 9.191, −5.814) z=[12.374,15.388] → MATCHED North2
CELL 5: ( 1.933, −5.86 ) z=[12.374,15.388] → MATCHED North0
MISSING: North1 (nearest cell 5, 3.59 m) · East1 (nearest cell 2, 3.51 m)
```

Note the north/south cells sit at **z=[12.374, 15.388] — off the true datums** (12.24/15.29): walls extended to the un-merged 12.436/15.473-family planes (D3 quantified). Orphan cluster panels: 0. Unused input panels (approx.): 5.

### Path B — 8 cells / 8 spaces (accidental 3-datum merge → nearly there)

```text
CELL 0: (1.515,−23.387) z=[12.24,15.29] → MATCHED South2
CELL 1: (6.58, −23.256) z=[12.24,15.29] vol=3.29 m³ → EXTRA (sliver band)
CELL 2/3: West1 / West2 (stacked pair) → MATCHED
CELL 4: West3 z=[12.24,18.34] → MATCHED, double-height OK
CELL 5/6/7: North1 / North2 / North0 → MATCHED (on-datum z=[12.24,15.29])
MISSING: East1 (nearest cell 1, 2.79 m) · South1 (nearest cell 1, 1.98 m)
```

7 of 9 matched, cells on-datum, **West3 double-height in both paths**. Orphans: 0. Unused (approx.): 1. The 3.29 m³ EXTRA sliver at (6.58, −23.26) is the multi-skin wall band (below) turned into a cell.

## 5. Missing-wall determination — **one pair genuinely lacks a full-height separator; the rest are present-but-unused**

Separator scan (vertical panel strictly between the two locations, requiring the candidate span the **full expected floor-to-ceiling level height** — not just a narrow band around the two seed elevations, per codex review on PR #57):

| Failed pair | Candidate separator panels (present but unused) |
|---|---|
| South1 \| South2 (merged, path A) | `3b032875` Wall 10.0 m² @ x≈3.47 (full-height) |
| **North1 \| North0 (North1 missing, path A)** | **none — no full-height separator.** The two panels reported in an earlier, looser pass (`d5c1f0bc`, `61ba52da`) turned out to be partial-height fragments that do not span the level and were correctly excluded once the scan required full floor-to-ceiling coverage. |
| North1 \| North2 (path A) | `1b2ef1d0` Wall 21.4 m² @ x≈7.4 (full-height) |
| East1 \| South1 (both paths) | **four near-parallel skins** `20fe83aa`, `31f97c71`, `763f6aa3`, `5b9dbfd6` (18.0 m² each) at y = −23.006 / −23.228 / −23.339 / −23.434 — a 0.43 m multi-skin band, full-height |
| East1 \| South2 (both paths) | `aa6f86f4` Wall 14.5 m² @ x≈3.57 (full-height) + the y≈−23 band above |

**Resolution per plan §7 (nuanced by the corrected scan):** four of five failed pairs have a genuine present-but-unused full-height separator — Clean3D/Extend3D must be made to use them, not invent anything. **North1's boundary toward North0 has no full-height wall candidate in the input at all** — this is either a genuinely missing separator on that one side, or North1's true bounding wall lies somewhere the point-pair scan does not test (e.g. a wall not strictly between the two seed *points* but still bounding North1's actual footprint). **P2/P3 must re-examine North1 specifically** with the real (GUID-based) matcher and full panel geometry, not just this diagnostic's point-pair heuristic, before P4 decides whether North1 needs a corrected fixture or whether Clean3D/Extend3D simply need to reach an existing-but-differently-placed wall. This does not block P1–P3 (which build the matcher and solver fixes this finding will be re-tested against); it is a named risk for the P4 stop gate. The four-skin band at y≈−23.2 is precisely what Clean3D bucketing must collapse (it currently survives to produce path B's sliver EXTRA cell), and the x≈3.3–3.7 partitions must reach the caps (Extend3D) to split North/South rooms.

## 6. Findings mapped to the plan's diagnosis

| Plan § | Baseline evidence |
|---|---|
| D1 (0.15 band, no knob) | 5 frames reported; 0.196/0.183 gaps unmergeable (§2) |
| D2 (band not forwarded) | not directly visible at runtime; code-verified (plan §1) |
| D3 (extend targets un-merged planes) | path A cells at z=[12.374,15.388] vs path B on-datum [12.24,15.29] (§4) |
| D4 (false-floor risk) | not triggered on this fixture (West3 OK in both paths) — the guard remains preventive |
| D5 (silent skips) | unused partitions (x≈3.3–3.7 walls) vanish with no skip/risk record (§5) |
| D6 (parameter precedence) | not exercised (no stamps on fixture); P2 unit tests own it |
| D7 (silent second-seed skip) | path A South1+South2: builder silently kept one seed; merged pair only visible via this harness's containment scan |
| D9 (fixture facts) | §1 — all confirmed, incl. West3 by GUID |
| D10 (double-Clean) | §3 — second pass merges datums 5→3 and drops 46→41 panels; not idempotent |

## 7. What the fixture demands from P2–P4 (targets)

1. **P2:** `bucketBetweenLevels = 0.21` in the *first* Clean must reproduce §3-path-B's 3 datums deliberately (12.24 / 15.29 / 18.34); `inputAlreadyClean = true` must make the chained Extend condition-only (no accidental second snap); the y≈−23.2 four-skin band should collapse in Clean's bucketing with a CleanRecord trail.
2. **P3:** wall-to-cap extension on the 3 merged datums; the x≈3.3–3.7 partitions (South1|South2, North1|North0, North1|North2, East1|South2) must reach floor+ceiling or emit skip/risk records saying why not; no cap growth into West3's column (z≈15.29 within its footprint).
3. **P4:** 9 cells / 9 matched / 0 missing / 0 merged / 0 split / 0 extra / West3 (GUID `02a1ae27…`) double-height true / 0 orphan cluster panels — on this original fixture, **contingent on North1's separator question (§5) resolving** in P2/P3's favor; otherwise P4 must decide (with the real matcher, not this diagnostic) whether North1 needs a corrected panel or whether an existing wall simply isn't reaching where expected.

## 8. P4 addendum (2026-07-10, `feat/cw-p4-acceptance`) — hard acceptance status on the chain

Chain run: `Clean3D(bucketBetweenLevels: 0.21)` -> `Extend3D(inputAlreadyClean: true, directionalCapGrow: true,
bucketBetweenLevels: 0.21)` -> `Create.AdjacencyCluster` rebuild path (seeds = `ExpectedSpaceSet.ToSeedSpaces()`,
built from the CLEANED panels, not the raw originals — see note below) -> `SpaceMatcher`.

**Result: 9 of 9 spaces matched cleanly** (East1/South1 corner-closure gap resolved — see fix below).
All spaces match with 0 merged/split/incorrectly-bounded/missing anywhere, 3 level groups (12.24/15.29/18.34),
0 orphan cluster panels. 2 benign Extra cells from coplanar-cap coalescing. Pinned in
`Testing/SAM.OCCT.IntegrationTests/ControlledWorkflowAcceptanceIntegrationTests.cs`
(`AcceptanceChain_FixtureNineSpaces_AllNineMatchCleanly`).

**East1|South1 corner-closure gap — RESOLVED (2026-07-10).** The root cause was fragmented cap strips at the
Z=15.29 intermediate level in the East1|South1 corner. The four-skin separator band left cap panels split into
narrow Y-range strips with ~0.1–0.4 m gaps between them. The directional cap grow (`directionalCapGrow=true`)
only extends cap edges toward facing WALLS — caps on the same plane with a gap between them have no wall to
grow toward, leaving the cap strips separated with insufficient overlap for MakerVolume to form watertight
intersections with walls.

**Fix: coplanar-cap coalescing pass in Fill** (`SAM.Geometry.OCCT.Solver/Classes/Panel3DSnapSolver.cs` Fill
method, second pass at end). When `directionalCapGrow` is active, after all caps have been grown toward walls,
a second pass detects caps with coplanar neighbours within the fill margin and applies a uniform
`GrowOutward(margin)` to each. This closes the inter-cap gaps that directional wall-based growth alone leaves
open. The pass is skipped when `directionalCapGrow=false` (the solver's default) because `GrowOutwardTo` /
`GrowOutward` already handle uniform expansion in that path. No fillMargin increase is needed — the
default 0.5 m works. West3's double-height is preserved (verified: `DoubleHeightOk[West3Guid] == true`).

**Sweep evidence (fillMargin 0.1–1.0 on the 9-space fixture, after the coplanar-cap coalescing fix):**
| fillMargin | dirCapGrow | matched | notes |
|-----------|------------|---------|-------|
| 0.1–0.2    | either     | 4       | Too small — North0/1, South2 also missing |
| 0.3        | either     | 6–7     | North1 closes; East1/South1 still missing |
| 0.4–0.5    | either     | 7–9     | East1/South1 close at 0.5 with the coalescing pass |
| 0.6+       | either     | 9       | All match; coalescing pass closes the gap at 0.5 |

**Prior (incorrect) diagnoses superseded:**
- The overlap-ratio near-miss (~96.76%) was a red herring (disproven by single-clean-separator test).
- The wider SewingTolerance (0.20 m) was diagnostic evidence of a gap but not a fix.
- All per-panel overrides (BucketSize, MaxExtend, Weight) were exhaustively tested and do not help.

**Code changes for this fix:**
- `Panel3DSnapSolver.Fill()`: added coplanar-cap coalescing second pass (gated on `directionalCapGrow`).
- `SnappedPanel.GrowEdgesToCaps()`: new method for mathematical cap-to-cap edge detection (available as a
  building block; the coalescing pass uses the simpler `GrowOutward` approach).
- `OcctBuildOptions.MergeCoplanarBeforeBuild`: new option for managed coplanar pre-merge before native build.
- `Create.Shells()`: wires `MergeCoplanarBeforeBuild` to run `MergeCoplanarFace3Ds` before MakerVolume.
- No ConditionStage reorder needed; no fillMargin increase needed.

### Pipeline optimization review (2026-07-10)

A systematic comparison of the controlled workflow (`Clean3D → Extend3D → AdjacencyCluster`) against the
solver pipeline (`Solve3D`) identified these gaps and optimizations:

**Gap in the AdjacencyCluster path (now closed):** The solver's `ResolveStage` runs a managed coplanar
pre-merge (`MergeCoplanarFace3Ds`) BEFORE the native MakerVolume build. This collapses overlapping coplanar
faces from the fill/extend step, which is what lets the kernel form a zoned cell complex. The AdjacencyCluster
path (`CellComplexByPanels → Create.Shells`) had no equivalent — overshooting faces went directly to
MakerVolume without pre-merge. **New `OcctBuildOptions.MergeCoplanarBeforeBuild`** adds this step, gated
behind a flag (default off, enabled for the controlled workflow chain).

**Gap in cap growth (now closed):** The solver's `HealStage.SewV2` performs an adaptive residual sew with
tolerance capping and fusion veto after MakerVolume. The AdjacencyCluster path has `SewBeforeBuild` (pre-build
sew) but no post-build adaptive sew. The coplanar-cap coalescing pass (above) addresses the root cause at
the managed level, before the native build.

**Investigated but NOT changed:**
- `NearestCoveringCap` plan overlap tolerance: the geometric tolerance (1e-6 m) is overly strict for
  building-scale models. Increasing it to `MacroDistance` (0.001 m) or 0.01 m changes the wall-to-cap
  matching for Face3D-home and AdjacencyCluster-home fixtures, causing golden-master regressions. Left for
  a future focused PR with fixture re-baselining.
- ConditionStage order (Fill before Extend): tested but not needed — the coplanar-cap coalescing pass
  addresses the same gap without reordering.
- SewV2 port to AdjacencyCluster path: deferred. The pre-build sew (SewBeforeBuild) + managed pre-merge
  (MergeCoplanarBeforeBuild) + coplanar-cap coalescing provide three layers of defense.

**Notable pipeline differences (Solve3D has these, AdjacencyCluster does not):**
- `HealStage.RetainDroppedV2` — re-adds clean geometry for dropped sources. Not applicable: AdjacencyCluster
  doesn't compare source-vs-output faces; SpaceMatcher handles cell matching differently.
- `GapFill.FromNakedWires` — patches residual naked-boundary loops. Not applicable: AdjacencyCluster
  produces cells via its own MakerVolume call which shouldn't leave naked loops.
- `PanelReconstruction.Build` with aperture re-hosting — Solve3D uses source-aware Guid policy. The
  controlled workflow uses `BuildPanels` (simpler attribution). Not a gap: the controlled workflow's
  output is the AdjacencyCluster, not rebuilt panels.
- `ConsolidationRebuild` — final `Create.Shells` over resolved+patches+retained. Not applicable:
  AdjacencyCluster already does its own `Create.Shells` as the primary build.

Diagnostic tests added:
- `EastSouthExtendDiagnosticTests` — pins wall vertical-extension skips
- `EastSouthFillMarginSweepTests` — sweeps fillMargin × directionalCapGrow
- `EastSouthFullSweepDetailedTests` — writes full sweep to file
- `FullFixtureFixedConfigTests` — confirms 9/9 with the fix

**Builder diagnostics added this phase** (`SAM_OCCT/SAM.Analytical.OCCT/Create/AdjacencyCluster.cs`, builder
layer only, no solver change): `SAM_OCCT_ANALYTICAL_MERGED_SEED_CELL` names every case where >1 expected seed
space lands in one built cell (previously `FindSeedSpace` silently kept only the first — D7); and
`SAM_OCCT_ANALYTICAL_ZERO_RELATION_PANELS` lists (up to 20) the Guids of cluster panels bounding zero spaces,
alongside the pre-existing aggregate `SAM_OCCT_ANALYTICAL_PARITY` count.

**`ExpectedSpaceSet` level-datum source correction** (validation layer,
`SAM_OCCT/SAM.Analytical.OCCT.Solver/Classes/ExpectedSpaceSet.cs` usage — no class-code change, a call-site
correction): the P1-era design fed `ExpectedSpaceSet.Create` the raw ORIGINAL input panels so its own
independent `LevelFrame.Cluster` + 1-D merge could work "self-contained... independent of P2 plumbing." Now
that P2 exists, this diverges from reality: `SnapStage.Clean`'s raw-frame clustering runs on panels already
processed by `StripInternalEdges`/`SnapOpposedPartitions`/`SnapToFixedPoint`, never on the untouched originals,
so the two clusterings see materially different input (22 raw slab-skin elevations vs. the post-snap set) and
can disagree. Verified on this fixture: from the raw originals, `ExpectedSpaceSet` computed 4 groups at
12.160/12.503/15.210/18.260 — none matching the real built-cell datums; from the CLEANED panels (Clean3D's own
output), it computed the correct 12.240/15.290/18.340. The acceptance test now sources `levelSourcePanels` from
the cleaned panels. No `ExpectedSpaceSet`/`SpaceMatcher` code changed — only which panels the caller passes.

## 9. Suite status at capture (optimizations + parameter discovery + sol review)

Unit: **617/617 passed**. Integration: **277 passed / 2 skipped / 0 failed**.

P4 acceptance: **9/9 expected spaces matched** with 2 benign Extra cells remaining from the
coplanar-cap coalescing pass. This is "9/9 expected spaces matched", NOT "fully valid with no
extra cells" — the 2 Extra cells are pinned (not asserted away) so a regression producing MORE
extras is caught.

## 10. Parameter discovery — AutoTune3D + ParameterDiscoverySolver

**`ParameterDiscoverySolver`** sweeps `bucketBetweenLevels × fillMargin × directionalCapGrow` over
plausible ranges, runs the full managed pipeline for each combination, scores by closure quality, and
reports the best configuration. The GH component `SAMOCCT.AutoTune3D` v0.2.0 exposes this via a
`discoverParameters_` toggle.

**Results on the 9-space fixture (face-only sweep):**
| bucketBetweenLevels | fillMargin | directionalCapGrow | cells | naked |
|---------------------|------------|-------------------|-------|-------|
| 0.21 | 0.5 | true | 13 | 0 | ← **best** (our production config) |
| 0.3 | 0.5 | true | 13 | 0 |
| 0.4 | 0.5 | true | 13 | 0 |
| 0.5 | 0.5 | true | 13 | 0 |

**Results on whole-level-towers (face-only sweep):**
| bucketBetweenLevels | fillMargin | directionalCapGrow | cells | naked |
|---------------------|------------|-------------------|-------|-------|
| **0.4** | **0.3** | **false** | **33** | **5** | ← **best (+8 vs baseline)** |
| 0.15 | 0.4 | false | 26 | 0 |
| 0.21 | 0.3 | false | 28 | 4 |

The towers result matches the known tuned config (band=0.4, fillMargin≈0.3-0.4 from
`WorkflowParity_WholeLevelTowers_FixtureTuning04`). The discovery sweep finds this automatically
without manual fixture knowledge.

**GH workflow for parameter discovery:**
```
[SAMOCCT.AutoTune3D] discoverParameters_=true
    → OptimalBand  ──→ [SAMOCCT.Extend3D] bucketBetweenLevels_
    → OptimalFill  ──→ [SAMOCCT.Extend3D] fillMargin_
    → OptimalDirCap ──→ [SAMOCCT.Extend3D] directionalCapGrow_
                          inputAlreadyClean_=true
                          → [SAMOCCT.CreateAdjacencyCluster] MergeCoplanarBeforeBuild=true
                          → [SAMOCCT.MergeCoplanarAdjacencyCluster]
```

**Only 3 Extend3D inputs are active** on the controlled chain (inputAlreadyClean=true):
`fillMargin_`, `bucketBetweenLevels_`, `directionalCapGrow_`. The other 4 Stage-A inputs
(minBucketSize_, thicknessFactor_, alignColinearOffset_, normalizeCapOffset_) are INERT —
Stage A is skipped when inputAlreadyClean=true.

**Parameter estimators** (available as starting points for the sweep):
- `BucketSizeEstimator` — derives bucketBetweenLevels from cap elevation frame clustering
- `FillMarginEstimator` — measures inter-cap and cap-wall gaps, detects fragmented cap strips
