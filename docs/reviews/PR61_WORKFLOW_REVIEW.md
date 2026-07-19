# PR #61 Workflow Review — P4 Acceptance

**Branch:** `feat/cw-p4-acceptance` → `sow/2026-Q3`
**Review Date:** 2026-07-13
**Reviewer:** OpenCode automated review per Fable 5 plan
**Plan Reference:** `docs/CONTROLLED_WORKFLOW_PLAN.md` §10 P4 review prompt

---

## 1. Severity-Ranked Findings

| # | Severity | Finding | Status |
|---|----------|---------|--------|
| F1 | LOW | `WorkflowParityIntegrationTests` unpinned expectation: Face3D-home B-clean-extend diverges from solver cell count (19 vs 20) — no entry in expectations dict | Pre-existing, not blocking |
| F2 | MEDIUM | `mergeCoplanarBeforeBuild_` not exposed on any Grasshopper component — users could not control this from GH | **FIXED** — exposed on `SAMOCCTCreateAdjacencyCluster` |
| F3 | LOW | MergeCoplanarBeforeBuild ON vs OFF has **zero effect** on the 9-space fixture (identical 11 cells, 9 matched, 2 extra) | Diagnostic, not blocking |
| F4 | HIGH | `directionalCapGrow=true` on whole-level-towers is **catastrophic**: 31→2 cells (complete collapse) | Documented tradeoff |
| F5 | CRITICAL | AutoTune3D spaces tie-break **bug**: fixed in SAMOCCTAutoTune3DDiscover | **FIXED** |
| F6 | LOW | Test comment incorrectly claimed `fillMargin=1.0, directionalCapGrow=false` as cause of Extra cells | **FIXED** |
| F7 | LOW | Modeling-Guide.md described Extra cells as "more robust cap growth" (vague) | **FIXED** |
| F8 | MEDIUM | Face3D-home B-clean-extend workflow yields 19 spaces at head vs 20 at base c643b29 (coplanar-cap coalescing) — earlier evidence claimed B-path parity | **DISCLOSED + PINNED** (`Assert.Equal(19, spacesB)`; real base/head logs committed) |
| F9 | LOW | `dotnet build SAM_OCCT.sln` failed on a dev machine with Rhino open: the GH post-build deployment copy hits the locked `.gha` in `%APPDATA%\SAM` | **FIXED** — post-build split into strict local packaging + best-effort deployment (`IgnoreExitCode`) |

## 2. Actual Implemented Workflow

```
Panels → Clean3D(bucketBetweenLevels=0.21)
      → Extend3D(inputAlreadyClean=true, directionalCapGrow=true, bucketBetweenLevels=0.21)
      → CreateAdjacencyCluster(MergeCoplanarBeforeBuild=true, SewBeforeBuild=true, AvoidInternalShapes=false)
      → SpaceMatcher (LevelBand=0.21, LevelGroupBand=0.21)
      → ValidateSpaces
```

Additional components:
- **SAMOCCTAutoTune3D** (legacy, GUID `9de8b4c0`, v0.1.0) — original escalation-solver contract restored exactly
- **SAMOCCTAutoTune3DDiscover** (new, GUID `dce4ce6d`, v0.4.0) — parameter discovery sweep component
- **ParameterDiscoverySolver** with WorkflowMode.Extend3D
- **BucketSizeEstimator** and **FillMarginEstimator** for automatic parameter tuning
- **doubleWallGap_** lever on Extend3D for explicit double-wall consolidation
- **MergeCoplanarBeforeBuild** in OcctBuildOptions (exposed on GH via `SAMOCCTCreateAdjacencyCluster.mergeCoplanarBeforeBuild_`)
- **Coplanar-cap coalescing** pass in Panel3DSnapSolver.Fill()

## 3. Recommended Production Workflow

```
SAMOCCT.AutoTune3D Discover (discover=true)
    → Band  ──→ SAMOCCT.Extend3D.bucketBetweenLevels_
    → Fill  ──→ SAMOCCT.Extend3D.fillMargin_
    → DirGrow → SAMOCCT.Extend3D.directionalCapGrow_
                  inputAlreadyClean_=true
                  → SAMOCCT.CreateAdjacencyCluster(mergeCoplanarBeforeBuild_=true)
                  → SAMOCCT.ValidateSpaces
```

**NOTE:** `mergeCoplanarBeforeBuild_` is now a Voluntary Boolean input on `SAMOCCTCreateAdjacencyCluster` (default `false`). Wire to `true` for the controlled workflow chain.

**WARNING:** `directionalCapGrow_=true` is safe on the 9-space fixture but catastrophic on whole-level-towers (31→2 cells). Users MUST test with `discover_=true` before accepting discovered parameters.

## 4. Fixture-by-Fixture Metrics Table

| Fixture | Input Panels | Chain | Cells | Volume (m³) | Matched | Missing | Extra | Notes |
|---------|-------------|-------|-------|-------------|---------|---------|-------|-------|
| 9-Space Model | 66 | Clean3D+Ext+Cluster | 11 | 1744.66 | 9 | 0 | 2 | 9/9 expected-space result |
| EastSouth Isolated | 40 | Clean3D+Ext+Cluster | 5 | 253.56 | 3 | 0 | 2 | East1/South1/South2 matched |
| Towers (gap=0) | 215 | Ext(band=0.4,fill=0.4) | 31 | 9605.396 | — | — | — | baseline |
| Towers (gap=0.4) | 215 | Ext(band=0.4,fill=0.4,gap=0.4) | 30 | 9620.406 | — | — | — | accepted setting: slivers removed, 22↔26 joined |
| Towers (gap=0.5) | 215 | Ext(band=0.4,fill=0.4,gap=0.5) | 29 | 9552.766 | — | — | — | over-aggressive: north-strip merges (67.6 m³ volume loss) |
| Towers (dir=true) | 215 | Ext(band=0.4,fill=0.4,dir=true) | 2 | 129.64 | — | — | — | **CATASTROPHIC COLLAPSE** |
| Towers (defaults) | 215 | Ext(band=0.15,fill=0.3) | 23 | 11340.89 | — | — | — | solver defaults |
| Face3D-home | 124 | Solve3D raw | 20 | — | — | — | — | solver cells=20, A-matched=19 (pre-existing) |
| Golden masters | — | — | — | — | — | — | — | all 15/15 pins pass (5 fixtures × raw/managed/managed-0.21) |

## 5. Sol Review Blocker Resolution

| Blocker | Description | Resolution |
|---------|-------------|------------|
| **AutoTune contract** | Original escalation-solver AutoTune3D must be restored under GUID `9de8b4c0` | Restored exactly from c643b29: version 0.1.0, 9 original inputs (`_panels`, `minBucketSize_`, `thicknessFactor_`, `alignColinearOffset_`, `normalizeCapOffset_`, `maxRounds_`, `maxExtendLadder_`, `escalateBucket_`, `_run`), 9 outputs (`Panels`, `NakedPoints`, `Diagnostics`, `Successful`, `NakedWires`, `SourceMap`, `ClosureReport`, `Rounds`, `RoundsAccepted`) |
| **New component naming** | Discovery sweep component must have distinct class | Renamed to `SAMOCCTAutoTune3DDiscover` with display "SAMOCCT.AutoTune3D (Discover)", GUID `dce4ce6d`, v0.4.0 |
| **GH tests** | Replace tautological literal-with-same-literal tests | Created dedicated `Testing/SAM.OCCT.GrasshopperTests` project that instantiates real GH components; old tautological tests removed from integration project |
| **Face3D parity** | Prove 19-vs-20 is pre-existing at c643b29 | Real worktree executions committed (`docs/reviews/evidence/PR61_FACE3D_BASE.log` / `PR61_FACE3D_HEAD.log`): solver=20 and A-matched=19 at both base and head (A under-close pre-existing). B-clean-extend is NOT at parity (base 20 → head 19, coplanar-cap coalescing) — disclosed and pinned (F8) |
| **Towers validation** | Replace weak assertions (>=25, >=26) with exact values | Exact assertions: gap0=31, gap0.4=30, gap0.5=29 cells; volumes, drifts, slivers quantified; gap 0.4 documented as accepted setting, 0.5 as over-aggressive |
| **Documentation** | Correct inaccurate comments and PR body | Updated review doc, evidence doc, Modeling-Guide references |

## 6. Fixes Applied

| Fix | File | Description |
|-----|------|-------------|
| **AutoTune legacy restoration** | `SAMOCCTAutoTune3DLegacy.cs` | Exact original escalation-solver contract from c643b29 |
| **Discovery component rename** | `SAMOCCTAutoTune3DDiscover.cs` | Renamed class and display name; distinct from legacy |
| **GH component tests** | `Testing/SAM.OCCT.GrasshopperTests/GHComponentContractTests.cs` | Real component instantiation tests |
| **GH test cleanup** | `Testing/SAM.OCCT.IntegrationTests/GHComponentContractTests.cs` | Removed tautological literal-vs-literal tests |
| **Towers validation** | `PR61TowersQuantitativeValidationTests.cs` | Exact deterministic assertions; gap 0.4 accepted, 0.5 over-aggressive |
| **Face3D parity** | `PR61Face3DHomeParityTests.cs` | Removed always-passing manual-procedure test |
| **Face3D evidence** | `docs/reviews/PR61_FACE3D_BASE_HEAD.md` | Base/head parity evidence with SHA256s |

## 7. Verdict

**READY FOR FINAL SOL REVIEW**

### Final test counts

Executed 2026-07-13 at code-complete commit `171e4a5` (the final PR head adds only
documentation/evidence and comment-only test doc corrections on top — no assertion or
production-code changes):

| Suite | Passed | Failed | Skipped |
|-------|--------|--------|---------|
| Unit tests | 615 | 0 | 0 |
| Integration tests | 261 | 0 | 2 (by design: inverse-gated native-missing + large-panel sew guard) |
| GH component contracts (dedicated project) | 15 | 0 | 0 (always execute — no skip path) |
| — focused: golden masters | 15/15 | 0 | 0 |
| — focused: controlled-workflow acceptance | 2/2 | 0 | 0 |
| — focused: towers quantitative | 4/4 | 0 | 0 |
| — focused: Face3D parity (head worktree / base worktree) | 1/1 each | 0 | 0 |

### P4 acceptance: 9/9 expected-space result

All 9 expected spaces match cleanly (0 missing, 0 merged, 0 split, 0 incorrect).
2 benign Extra cells remain from the coplanar-cap coalescing pass. These are pinned (not asserted away).

### Golden-master status: UNCHANGED

All 15 golden-master pins pass unchanged (5 fixtures × raw/managed/managed-0.21). The Face3D-home A-path under-close (19 vs 20 solver cells) is pre-existing at c643b29 base.

### Proven Face3D base/head result (real executions)

See `docs/reviews/PR61_FACE3D_BASE_HEAD.md` and the committed logs
`docs/reviews/evidence/PR61_FACE3D_BASE.log` / `PR61_FACE3D_HEAD.log` — identical native DLL
SHA256 `81CDABA60E5E0CF270371FDB3DED4C7E6201EF38B23D0D56AF9EEFD508DBBD4F` for both runs:

- Solver raw cells: 20 = 20 (parity).
- A-solver-matched spaces: 19 = 19 (parity; pre-existing under-close from PR #60 E2).
- B-clean-extend spaces: base **20** → head **19** — a PR #61 delta from the coplanar-cap
  coalescing pass, disclosed in the PR body and pinned in `PR61Face3DHomeParityTests`.

### Accepted towers gap and quantitative metrics (all asserted)

| Metric | Gap 0 | Gap 0.4 (accepted) | Gap 0.5 (over-aggressive) |
|--------|-------|--------------------|---------------------------|
| Cells | 31 | 30 | 29 |
| Total volume (m³) | 9605.396 | 9620.406 (+0.156%) | 9552.766 (−0.548%) |
| Cell-derived floor area (m²) | 3160.075 | 3168.220 | 3142.820 |
| Level-datum floor area (m², Z≈12.24) | 1336.727 | 1335.811 (−0.069%) | 1330.101 (−0.496%) |
| Slivers (<3 m³) | 2 | 0 | 0 |
| 22↔26 adjacency | False | True | True |

Gap 0.4: +15.010 m³ fully attributed (sliver absorption +3.833; exact 157.574 → 78.049 + 79.526
north-strip split; +7.610/+7.427 double-wall void reclamation) — no legitimate room lost.
Gap 0.5: destroys the west north-strip room (78.049 m³, 28.809 m² floor); −67.640 m³ vs gap 0.4
fully attributed (−78.049 + 10.408). Log: `docs/reviews/evidence/PR61_TOWERS_VALIDATION.log`.

### Legacy/new AutoTune migration

- **Legacy** `SAMOCCTAutoTune3D` (GUID `9de8b4c0`, v0.1.0): original escalation-solver contract, unchanged from base c643b29.
- **Discover** `SAMOCCTAutoTune3DDiscover` (GUID `dce4ce6d`, v0.4.0): parameter discovery sweep component with Band/Fill/Bucket/Align/Gap/DirGrow outputs for Extend3D wiring.

### Signature

Generated by Michal Dengusiak & Codex
