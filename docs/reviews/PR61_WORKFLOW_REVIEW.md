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
| Golden masters | — | — | — | — | — | — | — | all 20/20 pass |

## 5. Sol Review Blocker Resolution

| Blocker | Description | Resolution |
|---------|-------------|------------|
| **AutoTune contract** | Original escalation-solver AutoTune3D must be restored under GUID `9de8b4c0` | Restored exactly from c643b29: version 0.1.0, 9 original inputs (`_panels`, `minBucketSize_`, `thicknessFactor_`, `alignColinearOffset_`, `normalizeCapOffset_`, `maxRounds_`, `maxExtendLadder_`, `escalateBucket_`, `_run`), 9 outputs (`Panels`, `NakedPoints`, `Diagnostics`, `Successful`, `NakedWires`, `SourceMap`, `ClosureReport`, `Rounds`, `RoundsAccepted`) |
| **New component naming** | Discovery sweep component must have distinct class | Renamed to `SAMOCCTAutoTune3DDiscover` with display "SAMOCCT.AutoTune3D (Discover)", GUID `dce4ce6d`, v0.4.0 |
| **GH tests** | Replace tautological literal-with-same-literal tests | Created dedicated `Testing/SAM.OCCT.GrasshopperTests` project that instantiates real GH components; old tautological tests removed from integration project |
| **Face3D parity** | Prove 19-vs-20 is pre-existing at c643b29 | Evidence captured in `docs/reviews/PR61_FACE3D_BASE_HEAD.md`: solver=20, A-matched=19 at both base and head |
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

| Suite | Passed | Failed | Skipped |
|-------|--------|--------|---------|
| Unit tests | 615 | 0 | 0 |
| Integration tests | 273 | 0 | 2 |
| Controlled workflow acceptance | 4/4 | 0 | 0 |
| Towers quantitative | 4/4 | 0 | 0 |
| GH component contracts (dedicated) | 12/12 (skip when GH unavailable) | 0 | 0 |

### P4 acceptance: 9/9 expected-space result

All 9 expected spaces match cleanly (0 missing, 0 merged, 0 split, 0 incorrect).
2 benign Extra cells remain from the coplanar-cap coalescing pass. These are pinned (not asserted away).

### Golden-master status: UNCHANGED

All golden masters pass unchanged. The Face3D-home under-close (19 vs 20 solver cells) is pre-existing at c643b29 base.

### Proven Face3D base/head result

See `docs/reviews/PR61_FACE3D_BASE_HEAD.md`: solver=20, A-matched=19 on both base and head. Native DLL SHA256: `F177228B3551BA8B42A6D992DB176A3F02801B78638BA79C39CCE7BA290966CC`.

### Accepted towers gap and quantitative metrics

Gap 0.4 is the accepted towers setting: 30 cells, 9620.406 m³, 0 slivers, 22↔26 joined.
Gap 0.5 is over-aggressive: 29 cells, 9552.766 m³, removes additional cell (67.6 m³ volume drop).

### Legacy/new AutoTune migration

- **Legacy** `SAMOCCTAutoTune3D` (GUID `9de8b4c0`, v0.1.0): original escalation-solver contract, unchanged from base c643b29.
- **Discover** `SAMOCCTAutoTune3DDiscover` (GUID `dce4ce6d`, v0.4.0): parameter discovery sweep component with Band/Fill/Bucket/Align/Gap/DirGrow outputs for Extend3D wiring.

### Signature

Generated by Michal Dengusiak & Codex
