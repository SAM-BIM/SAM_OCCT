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

## 8. Suite status at capture

Unit: **495/495 passed**. Integration: **185 passed / 3 skipped / 0 failed** (baseline test included; perf benchmark skipped via `SAM_OCCT_SKIP_PERF=1`). Note: building the full solution's Grasshopper projects fails on their post-build deploy (`copy` into `%APPDATA%\SAM`) while Rhino/Grasshopper is running — close Rhino for full-solution builds; the test projects and solver libraries build clean regardless.
