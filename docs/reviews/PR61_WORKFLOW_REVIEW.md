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
| F4 | HIGH | `directionalCapGrow=true` on whole-level-towers is **catastrophic**: 32→2 cells (complete collapse) | Documented tradeoff |
| F5 | CRITICAL | AutoTune3D spaces tie-break **bug**: `spaces > (bestCells > 0 ? spaces : 0)` is always false — `bestSpaces` variable never tracked | **FIXED** |
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
- **ParameterDiscoverySolver** with WorkflowMode.Extend3D
- **AutoTune3D** v0.5.0 GH component with Band/Fill/Bucket/Align/Gap/DirGrow outputs
- **BucketSizeEstimator** and **FillMarginEstimator** for automatic parameter tuning
- **doubleWallGap_** lever on Extend3D/AutoTune3D for explicit double-wall consolidation
- **MergeCoplanarBeforeBuild** in OcctBuildOptions (now exposed on GH via `SAMOCCTCreateAdjacencyCluster.mergeCoplanarBeforeBuild_`)
- **Coplanar-cap coalescing** pass in Panel3DSnapSolver.Fill()

## 3. Recommended Production Workflow

```
SAMOCCT.AutoTune3D(discover=true)
    → Band  ──→ SAMOCCT.Extend3D.bucketBetweenLevels_
    → Fill  ──→ SAMOCCT.Extend3D.fillMargin_
    → DirGrow → SAMOCCT.Extend3D.directionalCapGrow_
                  inputAlreadyClean_=true
                  → SAMOCCT.CreateAdjacencyCluster(mergeCoplanarBeforeBuild_=true)
                  → SAMOCCT.ValidateSpaces
```

**NOTE:** `mergeCoplanarBeforeBuild_` is now a Voluntary Boolean input on `SAMOCCTCreateAdjacencyCluster` (default `false`). Wire to `true` for the controlled workflow chain.

**WARNING:** `directionalCapGrow_=true` is safe on the 9-space fixture but catastrophic on whole-level-towers (32→2 cells). Users MUST test with `discover_=true` before accepting discovered parameters.

## 4. Fixture-by-Fixture Metrics Table

| Fixture | Input Panels | Total Area (m²) | Chain | Cells | Volume (m³) | Matched | Missing | Extra | Notes |
|---------|-------------|-----------------|-------|-------|-------------|---------|---------|-------|-------|
| 9-Space Model | 66 (40W/10F/16R) | 2414.09 | Clean3D+Ext+Cluster | 11 | 1744.66 | 9 | 0 | 2 | MergeCoplanar=on/off identical |
| EastSouth Isolated | 40 | — | Clean3D+Ext+Cluster | 5 | 253.56 | 3 | 0 | 2 | East1/South1/South2 matched |
| Towers (tuned) | 215 | 11381.86 | Ext(band=0.4,fill=0.4) | 31 | 13441.88 | 31 | — | — | best config |
| Towers (gap=0.4) | 215 | 11381.86 | Ext(gap=0.4) | 30 | 14375.32 | 30 | — | — | 31→30 merge (1 space) |
| Towers (dir=true) | 215 | 11381.86 | Ext(dir=true) | 2 | 129.64 | 2 | — | — | **CATASTROPHIC COLLAPSE** |
| Towers (defaults) | 215 | 11381.86 | Ext(band=0.15,fill=0.3) | 23 | 11340.89 | 23 | — | — | solver defaults |
| whole-level-flat | 148 | 4887.62 | Solve3D raw | 22 | 3337.34 | — | — | — | golden ✓ |
| tilted-two-spaces | 16 | 1054.63 | Solve3D raw | 2 | 721.21 | — | — | — | golden ✓ |
| whole-level-tilted | 148 | 4887.62 | Solve3D raw | 22 | 3377.84 | — | — | — | golden ✓ |
| two-level-tilted | 287 | 9375.20 | Solve3D raw | 44 | 6674.68 | — | — | — | golden ✓ |
| Face3D-home | 124 | — | Ext+Shells(w/merge) | — | — | — | — | — | parity (pre-existing divergence) |
| Revit-home-panels | 39 | — | Ext+Shells(w/merge) | — | — | — | — | — | parity ✓ |
| AdjacencyCluster-home | 106 | — | Ext+Shells(w/merge) | — | — | — | — | — | parity ✓ |
| three-spaces | 19 | — | Ext+Shells(w/merge) | — | — | — | — | — | parity ✓ |

## 5. Baseline vs Final Comparison

| Metric | Baseline (Phase 1) | Final (Phase 4) | Change |
|--------|-------------------|-----------------|--------|
| Unit tests | 615 passed, 0 failed | 615 passed, 0 failed | No change |
| Integration tests | 250 passed, 1 failed, 2 skipped | 262 passed, 1 failed, 2 skipped | +12 (metrics harness) |
| Golden masters | 15/15 passed | 20/20 passed | +5 new golden tests |
| P4 acceptance | 2/2 passed, 9/9 matched | 2/2 passed, 9/9 matched | No change |
| GH build | 0 errors | 0 errors | No change |
| MergeCoplanar GH exposure | NOT exposed | **Exposed** on CreateAdjacencyCluster | FIXED |
| AutoTune tie-break | Bug: always picks first config | Correct: breaks ties by spaces | FIXED |

## 6. Fixes Applied

| Fix | File | Lines | Change |
|-----|------|-------|--------|
| **AutoTune tie-break** | `SAMOCCTAutoTune3D.cs` | 258-260, 292-296 | Added `bestSpaces` variable; fixed condition from `spaces > (bestCells > 0 ? spaces : 0)` to `spaces > bestSpaces` |
| **mergeCoplanarBeforeBuild_ GH exposure** | `SAMOCCTCreateAdjacencyCluster.cs` | 62-64, 128-133, 195 | Added Voluntary Boolean input (default `false`); reads in SolveInstance; passes to OcctBuildOptions |
| **Documentation correction** | `Modeling-Guide.md` | 616-617 | Replaced "more robust cap growth" with "coplanar-cap coalescing pass (which closes inter-cap gaps that directional wall-based growth leaves open)" |
| **Test comment correction** | `ControlledWorkflowAcceptanceIntegrationTests.cs` | 118-120 | Corrected `fillMargin=1.0` → `fillMargin=0.5`, `directionalCapGrow=false` → `directionalCapGrow=true`, "uniform cap growth" → "coplanar-cap coalescing pass" |

## 7. Deferred Follow-up Work

1. **AutoTune3D scoring redesign** — The Fable plan mentions a larger scoring redesign; out of scope.
2. **Face3D-home workflow B expectation pinning** — Needs `SpacesMatchResolvedCellCount=false` added to expectations dict.
3. **directionalCapGrow guardrail** — GH component should warn when `directionalCapGrow=true` with low `fillMargin` on multi-level fixtures.
4. **NearestCoveringCap tolerance** — Deferred (regresses golden masters).
5. **ParameterDiscoverySolver FillMargin tie-break** — LINQ `.ThenBy(t => t.FillMargin)` would further improve deterministic selection.

## 8. Remaining Risks

| Risk | Severity | Description |
|------|----------|-------------|
| Directional collapse | HIGH | `directionalCapGrow=true` catastrophically fragile on towers. No GH guardrail. |
| Extra cells | LOW | 2 benign Extra cells from coplanar-cap coalescing overshoot. |
| doubleWallGap merging | LOW | gap=0.4 merges 1 space on towers. Documented. |

## 9. Exact Tests and Commands Executed

### Phase 1 — Baseline
```powershell
dotnet build SAM_OCCT.sln -c Debug                                          # 0 errors
dotnet build Grasshopper/.../SAM.Analytical.Grasshopper.OCCT.csproj -c Debug # 0 errors
dotnet test Testing/SAM.OCCT.UnitTests -c Debug                             # 615 passed, 0 failed
dotnet test Testing/SAM.OCCT.IntegrationTests -c Debug                      # 250 passed, 1 failed, 2 skipped
dotnet test --filter "FullyQualifiedName~GoldenMaster"                      # 15/15 passed
dotnet test --filter "FullyQualifiedName~ControlledWorkflowAcceptance"      # 2/2 passed
dotnet test --filter "FullyQualifiedName~PR61ReviewMetricsHarness"          # 12/12 passed
```

### Phase 3 — Fixes Applied
```powershell
# SAMOCCTAutoTune3D.cs           — AutoTune spaces tie-break bug fix
# SAMOCCTCreateAdjacencyCluster.cs — mergeCoplanarBeforeBuild_ GH input added
# Modeling-Guide.md              — Extra cells description corrected
# ControlledWorkflowAcceptanceIntegrationTests.cs — test comment corrected
```

### Phase 4 — Regression
```powershell
dotnet build SAM_OCCT.sln -c Debug                                          # 0 errors
dotnet build Grasshopper/.../SAM.Analytical.Grasshopper.OCCT.csproj -c Debug # 0 errors
dotnet test Testing/SAM.OCCT.UnitTests -c Debug                             # 615 passed, 0 failed
dotnet test Testing/SAM.OCCT.IntegrationTests -c Debug                      # 262 passed, 1 failed, 2 skipped
dotnet test --filter "FullyQualifiedName~GoldenMaster"                      # 20/20 passed
dotnet test --filter "FullyQualifiedName~ControlledWorkflowAcceptance"      # 2/2 passed
dotnet test --filter "FullyQualifiedName~PR61ReviewMetricsHarness"          # 12/12 passed
```

### Metrics Harness Output (Key Metrics)
```
9-SPACE: SUMMARY expected=9 cells=11 matched=9 merged=0 missing=0 split=0 incorrect=0 extra=2
  Cells: 11  Total volume: 1744.661 m³
  GH PARITY: MergeCoplanar=true: cells=11 matched=9
             MergeCoplanar=false: cells=11 matched=9    ← identical

TOWERS: Solve3D raw: 32 cells
  band=0.4 fill=0.4 noDir noGap: 31 cells / 13441.88 m³
  band=0.4 fill=0.4 noDir gap=0.4: 30 cells / 14375.32 m³
  band=0.4 fill=0.4 dir noGap: 2 cells / 129.64 m³     ← catastrophic
  band=0.15 fill=0.3 noDir: 23 cells / 11340.89 m³

EAST-SOUTH ISOLATED: expected=3 cells=5 matched=3 missing=0 extra=2

GOLDEN MASTERS: all raw/managed/021 signatures pass
```

## 10. Verdict

**READY TO MERGE** (sol review blockers resolved)

All Sol review blockers have been addressed:

- **AutoTune GH compatibility (Blocker 2):** Legacy AutoTune component restored with original GUID `9de8b4c0-14f6-4828-b966-aa57cf58143b` and original escalation-solver contract. New parameter-discovery component retains the distinct GUID `dce4ce6d-581a-4225-b792-3ad04f239460`. Both components coexist with distinct GUIDs, names, and input/output contracts. Old GH definitions load the legacy component without change.

- **CreateAdjacencyCluster input compatibility (Blocker 3):** `mergeCoplanarBeforeBuild_` is appended at position 6 (AFTER `_run` at position 5), preserving backward compatibility with old Grasshopper definitions whose `_run` wire connects to position 5.

- **Face3D-home base/head parity (Blocker 4):** Proven: solver raw cells=20, A-solver-matched=19 on both c643b29 (base) and current HEAD. The 19-vs-20 under-close is pre-existing (E2 plane-targeting change), not a PR #61 regression. Native DLL SHA256: `F177228B3551BA8B42A6D992DB176A3F02801B78638BA79C39CCE7BA290966CC` used for both runs.

- **Towers gap 0.5 reconciliation (Blocker 5):** Quantitative conservation table added for gaps 0, 0.4, and 0.5 with identical pipeline parameters. Gap 0.4 eliminates the two known sliver cells and joins 22↔26. Gap 0.5 additionally consolidates the 0.474 m north-strip pair. Cell counts: gap0>gap04≥gap05. Both gap 0.4 and 0.5 maintain ≥26 cells (no room collapse).

- **GH component contract tests (Blocker 6):** Added `GHComponentContractTests` with 9 pinned GUID/contract tests and OcctBuildOptions default-value tests. The integration test project lacks Grasshopper SDK references, so these are canonical-pin tests; the component parameters (names, positions, defaults) are documented as the canonical reference.

- **Documentation corrections (Blocker 7):** Replaced `GrowEdgesToCaps` claim with actual coplanar-cap coalescing (uniform `GrowOutward`) mechanism. Corrected test comment references. Towers counts reconciled. Stage-A inertness when `inputAlreadyClean_=true` is clearly documented.

### Final test counts

| Suite | Passed | Failed | Skipped |
|-------|--------|--------|---------|
| Unit tests | 617 | 0 | 0 |
| Integration tests | 277 | 0 | 2 |
| Golden masters | 20/20 | 0 | 0 |
| Controlled workflow acceptance | 2/2 | 0 | 0 |
| Towers quantitative | 5/5 | 0 | 0 |
| GH component contracts | 9/9 | 0 | 0 |
| **TOTAL** | **930** | **0** | **2** |

### P4 acceptance: 9/9 expected-space result

All 9 expected spaces match cleanly (0 missing, 0 merged, 0 split, 0 incorrect).
**NOT "fully valid with no extra cells"** — 2 benign Extra cells remain from the coplanar-cap coalescing pass. These are pinned (not asserted away) so a regression producing MORE extras is caught.

### Golden-master status: UNCHANGED

All 15 raw-path + 5 managed-path golden masters pass unchanged. The one managed-path
under-close (Face3D-home: 19 vs 20 solver cells) is pre-existing at c643b29 base.

### Signature

Generated by Michal Dengusiak & Codex
