# Towers residual cap-edge closure (rule E) - branch record (2026-10-23)

Branch: `fix/towers-residual-closure-q4`, created from North0 head `2a09f69d0e3798dfb1369033cf06282d59b16a26`
(tested North0 content `791bcaa`). **Stacked on unmerged work:** it carries the eight nine-space / North0 commits
that are not on `origin/sow/2026-Q4` (see "Dependency").

Commits:
- `0dac3f4`: correction, tests, towers pin reconciliation;
- `119eae0`: the workflow-parity pin found by the full native run;
- `4b16897`, `229f971`: this record;
- then the managed-path test/evidence revision (tests, `TESTING.md` and this record only; see "Managed path").

**Production content tested: `119eae0fd695a84e310b43fd66fbdde2d6a098b4`** (unchanged by everything after it). The
revision's tests were validated on `229f971` plus the revision diff (test file blob
`b029443f3c4bce6a61e52736ff0918b6dd7a9b1e`); its commit SHA is in the PR description. `PROJECT_PROGRESS.md` is not touched (post-merge
closeout only).

Specification and evidence (machine-local, outside the repository; copy them before switching machines):
- `SAM-BIM/.review-towers-closure-revision-20261023/PROPOSAL-REVISED-2.md`: the validated rule.
- `SAM-BIM/.review-towers-closure-independent-20261023/REVIEW.md`: the independent review behind fixes 1-5.
- `SAM-BIM/.review-towers-closure-impl-20261023/`: this implementation's evidence (README, CENSUS-SUMMARY).
- `SAM-BIM/.diag-towers-managed-room-20261023/REPORT.md`: the managed-path diagnosis (adopted complex vs recount).
- `SAM-BIM/.review-towers-managed-independent-20261023/REVIEW.md`: its independent, uninstrumented reproduction.
- `SAM-BIM/.evidence-towers-managed-tests-20261023/`: the managed-path revision (README, PARAMETERS, exports,
  boundary comparison, suite runs).

## Status

Implemented, locally validated, and in an owner-review draft PR. **The middle towers room, lost to a 12-28 mm
floor/north-wall slot, is recovered:**
- on the one-step uniform Extend3D chain at doubleWallGap 0 / 0.4 / 0.47 / 0.5 (119.364 m³), where every cell of the
  gated-off two-step control keeps its complete boundary face for face;
- on forced-managed `Solve3D` (113.717 m³ at the default bucketBetweenLevels 0; 119.364 m³ at 0.21), in the complex
  the solver adopts. Every baseline room keeps its geometric boundary there; two neighbours per chain are re-faced
  (see "Managed path").

The directional and two-step paths, and so the owner-accepted nine-space exports, are identical.

Full native: 496 = 469 passed / 25 failed / 2 not executed. Against the 791bcaa baseline: nothing new, nothing resolved; of the 25 failures
(19 historical + 6 rigid-motion), 23 keep their message and 2 (the towers managed golden masters, which measure an
unguarded recount; see "Managed path") changed message; the 2 not-executed tests are unchanged.

Not merged. Hosted CI: see the PR description.

## Problem

The middle room's floor on `whole-level-towers.sam` was merged by Clean3D from ten sources including `efdf7fef`. It
was authored ON the north wall `9407a38c`. Clean3D snapped that wall 0.1646 m away. The whole-cap uniform Fill
(`SnappedPanel.GrowOutwardTo`, whose gap metric is the minimum boundary-point distance) grew the floor only enough
to leave a 12-28 mm slot. The room therefore never closed, at any gap.

## What changed (`SAM_OCCT/SAM.Geometry.OCCT.Solver`)

1. **`ResidualContactClosure`** (new, internal). After every cap's existing grow, an edge that was **authored on a
   wall** and is still short of it is moved onto the wall plane plus the fill overshoot. It fails closed unless all
   of the following hold.
   - **Lineage:** `SnapStage`'s own attribution, keyed by the clean ordinal (`SourceIndices[0]`).
     - G1: lineage from the nearest-panel fallback never counts.
     - A source attributed to more than one clean face is ambiguous and never counts.
     - G2, correspondence at the source-contact edge: the source cap's 0.02 m band along the contact segment
       lies inside the current cap, and the source is within the level reach
       (max(bucketBetweenLevels, normalizeCapOffset)).
     - The contact is within the largest Stage-A move (max bucket, align, doubleWallGap, consolidation range) of
       the current wall.
   - **Geometry:**
     - the target wall's exact section at cap height covers the contact interval;
     - one cyclically contiguous facing chain lies within the fill margin;
     - there is exactly one candidate interval and exactly one qualifying wall per cap.
   - **Blockers:**
     - every non-horizontal panel (vertical, tilted or sloped) is sectioned exactly at cap height and blocks when it
       enters the swept region including overshoot;
     - corner partners are exempt, but only at their corner;
     - another wall's source foot inside the strip blocks;
     - the swept region must not enter the cap itself.
   - **Move:** split points are inserted at the interval ends, then `SnappedPanel.MoveSplitBoundaryRun` (new,
     wrapping the existing `MoveBoundaryRun`) moves the run. The result is adopted only if it is a simple polygon
     (raw rings, no repair); holes are kept.
2. **`SnapStage.Result.GeometricAttributionPerFace`** (internal): per clean face, whether the attribution came from
   Contributors or from the nearest fallback.
3. **`Panel3DSnapSolver.Execute`** builds the lineage only when
   `!InputAlreadyClean && !DirectionalCapGrow && toCanonical == null` and passes it through the internal
   `ConditionStage.Settings.ResidualContactLineage`.
   - The public `Fill` is unchanged; it delegates to the internal `FillCore`.
   - Each closure adds a `CapGrow` ExtendRecord (target `walls-source-contact`) and an Info diagnostic
     `[Snap/Gap/Info] Residual cap-edge closure: ...` (visible at Summary, because it changes the model).

Not changed: public API, settings, defaults, native ABI, SAM / SAM_Solver, fixtures, goldens, the directional path,
the two-step (`inputAlreadyClean`) path, tilted levels, Grasshopper components. The managed-path revision changes
tests, `TESTING.md` and this record only.

## Decisions and why

- **Scope: one-step, untilted, uniform only (owner-approved 2026-10-23).**
  - On `inputAlreadyClean` the authored faces never reach the solver, so "authored contact" cannot be judged
    there.
  - Tilted levels would need the original faces in the canonical frame (untested).
  - Consequence: towers one-step and two-step uniform results differ by this room. The owner nine-space chain
    (two-step + directional) is unaffected by construction.
- **C31 is reachable through geometric attribution, so it fails closed.** `SnapStage.Contributors` attributes a
  panel to every clean face that contains its bounding-box centre on the same plane.
  - Example: a partition 7° off a wall and crossing it (outside the 5° snap, inside the 0.99 cone) is attributed
    to both clean faces. This is unit-pinned: `SnapStageClean_ShallowCrossingPartition_AttributedToBothCleanFaces`.
  - An unrelated, still-standing wall could thus enter W's lineage. The ambiguity rule rejects it:
    `Apply_WallSourceAttributedToTwoCleanFaces_LeavesVoid`.
  - A wall source that is unique and geometric is by construction one Stage A moved onto W's plane, which is the
    intended case: `Apply_UniqueGeometricWallLineage_Closes`.
- **Fail closed per pair, stricter than the diagnostic probe:** any interval that fails closed rejects the whole
  cap/wall pair. The production move is still face-identical to the validated probe at all four gaps.
- **Geometry:** SAM's own NTS conversion (`Planar.Convert.ToNTS_LinearRing`) plus NTS overlay operations, as in
  the validated probe.
- **Reconciled pins:** only those that encode the missing room, and only by measurement (owner-approved).
- **Managed path (2026-10-23, after the managed-room diagnosis and its independent review):** rule E's scope and
  resolve/heal stay unchanged. A StopAfterExtend scope restriction was rejected: it would remove a genuine managed
  recovery (+1 room, nothing lost in the adopted complex) only to match golden counts measured by an unguarded
  recount that is itself invalid at the baseline. The managed acceptance is asserted on the solver's adopted complex
  and on a guarded rebuild; the goldens stay as they are.

## Tests

- **`ResidualContactClosureTests` (unit, 37):** controls C01-C31, the self-overlap proof, the reachable-C31
  attribution test, and end-to-end gate and misattribution cases. See `TESTING.md`.
  - **Mutation check** (`mutation-check.txt`): G1, ambiguity, G2, the non-vertical blockers and cyclic contiguity
    each fail at least one test when disabled alone.
    - G1: C28.
    - Ambiguity: the two-clean-face test.
    - G2: C26 and C30.
    - Non-vertical blockers: C19 and C20.
    - Cyclic contiguity: C21.
    - Self-overlap: disabling the pre-check alone passes all 37 tests; only removing BOTH the pre-check and the
      simple-polygon post-check fails C29. The two are redundant defence in depth and both are kept.
- **`TowersResidualContactClosureIntegrationTests` (native).**
  - `OneStepUniform_..._RecoversRoomAndKeepsEveryOtherCell` (4 gaps). Per gap:
    - exactly one closure, and the room appears: 119.364 m³, 10 faces, bbox x 17.1427..21.9509,
      y -8.9172..-0.7442, containing (19.5, -5, 13.8);
    - every cell of the gated-off two-step control reappears with an identical complete boundary (the strict gate,
      unchanged);
    - the cells are valid (`OcctUnderSplitGuard`).
  - `Solve3D_ForceManagedTowers_ResidualClosure_KeepsBaselineRoomsAndAddsMiddleRoom` (bucketBetweenLevels 0 and
    0.21; the golden masters' managed call). On `report.ResolvedCellComplex`, each cell rebuilt from the faces whose
    `OwnerCellIndices` name it, with zero unkeyed faces and every owner a valid cell index; then again on a guarded
    rebuild of the returned non-air panels (golden options + `ValidateSewCoverage = true`):
    - each of six room points lies in exactly one cell, at its chain's volume (± 0.05 m³), and no cell holds two:

      | Room (point) | b = 0 | b = 0.21 |
      | --- | --- | --- |
      | west (14, -5, 13.8) | 159.479 | 174.145 |
      | far-west (9.2, -5.8, 13.8) | 76.994 | 80.817 |
      | south (34.4, -21.5, 13.8) | 238.960 | 250.827 |
      | south-east (42.5, -22.5, 13.8) | 155.536 | 163.261 |
      | north-east (34.5, -5, 13.8) | 159.373 | 159.373 |
      | **middle (19.5, -5, 13.8)** | **113.717** | **119.364** |

    - 24 / 27 cells; finite positive volumes; `OcctUnderSplitGuard.ValidateCells` passes on both cell sets.
    - **Against `791bcaa`** (scratch build) it fails on both chains for the intended reason only: the five baseline
      rooms pass, then "adopted complex: the middle room point (19.5, -5, 13.8) lies in 0 cell(s), expected
      exactly one" (23 / 26 cells).
    - The unguarded golden masters are untouched and not asserted here.
- **Reconciled pins** (measured values in `towers-pins-measure-before-reconcile.log`):
  - `PR61TowersQuantitativeValidationTests`: cells 31 -> 32 (gap 0) and 30 -> 31 (0.4, delta test); volume
    +119.364 m³; cell floor area +39.136 m²; level-datum panel area +0.286 m² at every gap; VolumeDrift05
    0.3722 -> 0.3677; the 0.4 -> 0.5 delta 20.745 m³ unchanged;
  - `TowersBucketLeverDiagnosticTests.Towers_DoubleWallGap_FixAcceptance_Gap05StripSurvives`: 30 -> 31 at 0.47;
  - `WorkflowParityIntegrationTests.WorkflowParity_WholeLevelTowers_FixtureTuning04_Produces32ParityCleanSpaces`
    (renamed from `..._Produces31...`): its own documentation named the one missing reference space near
    (19.584, -4.909, 13.925), which is this room. It now reaches 32 OCCT cells and 32 SAM spaces, parity clean.

## Managed path: adopted complex vs the unguarded golden recount

Evidence: `.diag-towers-managed-room-20261023/REPORT.md` (diagnosis), `.review-towers-managed-independent-20261023/
REVIEW.md` (uninstrumented reproduction), `.evidence-towers-managed-tests-20261023/` (this revision).

- **What `Create.Spaces` consumes keeps every room.** On forced-managed `Solve3D`, the adopted complex
  (`report.ResolvedCellComplex`) goes 23 -> 24 cells at bucketBetweenLevels 0 and 26 -> 27 at 0.21. The returned
  non-air panels rebuilt with the SAM_OCCT#81 sew guard give the same counts.
- **Only the unguarded recount loses a room.** The two towers golden masters rebuild the returned panels with
  `SewBeforeBuild = true` and `ValidateSewCoverage` false.
  - At the baseline this recount is already invalid: a sew cell of signed volume −3767.501 m³ (the "3428.663 m³
    blob"), and the 76.994 and 155.536 m³ rooms are missing. The guard rejects it (`SAM_OCCT_SEW_BUILD_INVALID`)
    and adopts the 23-cell direct build.
  - After the change, the default chain's recount omits the 159.479 m³ west room (7 of its 9 faces in no shell).
    The guard reports incomplete coverage and a 159.481 m³ volume shortfall (`SAM_OCCT_BUILD_FALLBACK_DIRECT`) and
    adopts the 24-cell direct build.
  - The loss does not depend on the sew tolerance (0.001-0.01 m). A T-junction partition dropped by manifold sewing
    is a supported hypothesis, not a traced native cause.
  - At 0.21 the recount is valid on both sides (26 -> 27).
  - So the golden counts 20 -> 23 (expected 21) and 26 -> 27 (expected 25) measure that recount, not the solver's
    cells. They are not re-pinned.
- **Which caps matter (diagnosis, 3 repeats each, deterministic):** on the default chain only cap 89 (the middle
  room's floor onto the north wall) changes any solver checkpoint; caps 88 and 91 are inert there. At 0.21 only
  cap 5 (the same edge) matters; cap 8 is inert. The unguarded recount alone reacts to the inert caps: it is
  deterministic but input-sensitive, not random.
- **Baseline boundaries preserved (this revision).** Every baseline adopted cell was compared with the after cells
  by geometry, not by face lists: per plane, the union of the coplanar faces (holes kept) before and after, by
  symmetric-difference area and boundary Hausdorff distance. Tolerances, declared before the runs: 1e-6 m for
  plane membership and Hausdorff (the strict gate's own), and 1e-6 m × boundary length for the area.

  | Chain | Before -> after | Preserved | Face-identical | Re-faced (faces before -> after) | Added |
  | --- | --- | --- | --- | --- | --- |
  | b = 0, adopted | 23 -> 24 | 23 / 23 | 21 | 159.479 m³ west (7 -> 9), 109.347 m³ (10 -> 8) | middle 113.717 m³ |
  | b = 0.21, adopted | 26 -> 27 | 26 / 26 | 24 | 174.145 m³ west (7 -> 15), 80.817 m³ far-west (6 -> 8) | middle 119.364 m³ |
  | b = 0, guarded rebuild | 23 -> 24 | 23 / 23 | 21 | 159.479 m³ (7 -> 9), 109.347 m³ (10 -> 8) | middle 113.717 m³ |
  | b = 0.21, guarded rebuild | 26 -> 27 | 26 / 26 | 23 | 174.145 m³ (7 -> 10), 80.817 m³ (6 -> 6, split differently), 119.577 m³ (10 -> 8) | middle 119.364 m³ |

  - Worst values over all matched planes: symmetric difference 3.0e-7 m² (at most 1.04 % of its tolerance), boundary
    Hausdorff 1.1e-7 m, out-of-plane 3.1e-7 m.
  - The re-faced cells changed only how their shared walls are split into faces (the middle room now closes against
    them). **Output face partitions are therefore not all unchanged on the managed chains**; the boundaries are.
  - Comparator control on all 100 adopted cells: identity and a 0.5e-6 m in-plane shift pass; a dropped face, a
    2e-6 m off-plane offset and a 2e-6 m in-plane shift are all detected (500 / 500).
  - Repeat runs are face-identical. My builds match the reviewer's frozen snapshots face for face.
  - The Extend3D one-step chain keeps its strict face-for-face gate, unchanged.
- **Authored-room coverage (diagnosis, heuristic).** Over 32 room-candidate groups enumerated from the source caps
  and walls plus named probes, every chain has exactly one difference, the middle room recovered; nothing newly lost
  or merged, except in the default unguarded recount. Pre-existing and unchanged, not investigated here:
  - managed default chain: groups G12-G15, G18, G19, G27, G30 are in no cell;
  - managed 0.21 chain: G13-G15, G27, G30;
  - Extend3D merges G27+G30 (gap 0) and G13+G14 (gaps 0.4-0.5).
  - These rely on fixed thresholds and a geometric cap/wall classification; they are not whole-model completeness.
- **`Create.Spaces`** reads the adopted complex but still refuses the default forced-managed towers result for
  naked edges (19 before, 13 after). Retained rooms are not a successful `Create.Spaces` run. Plain `Solve3D` on
  towers is raw-first and not affected (diagnosis).

## Validation (local, Windows; frozen native)

Native `build/` was copied from the North0 worktree: `SAM.Occt.Native.dll` 438089F8..., `TKBO.dll` 1FA732FE...
(ABI 4, unchanged). Managed SAM / SAM_Solver were read-only from their checkouts' `build/` (unchanged since
2026-10-22).
- **Builds** (`--no-incremental -p:SAM_OCCT_SKIP_NATIVE_BUILD=true`): unit 0 errors / 243 warnings, integration
  0 errors / 284 warnings. Both equal the 791bcaa baseline; neither the closure nor the new test adds a warning.
- **`BuildAlls_v5` not used.** It builds and deploys the main checkouts, not this worktree, so the worktree was
  validated with the commands below.

```powershell
dotnet build Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug --no-incremental -p:SAM_OCCT_SKIP_NATIVE_BUILD=true
dotnet build Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug --no-incremental -p:SAM_OCCT_SKIP_NATIVE_BUILD=true
$env:SAM_OCCT_NINE_SPACE_EXPORT_DIR = "<dir>"; $env:SAM_OCCT_TEST_CODE_SHA = "<tested SHA>"
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug --no-build
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug --no-build
```

| Suite (local) | Base `791bcaa` | `119eae0` | **Revision (`229f971` + diff)** |
| --- | --- | --- | --- |
| Unit, full | 995 / 0 | 1032 / 0 (+37) | **1032 / 0** |
| Nine-space filter (wall capture + controlled acceptance + North0 follow) | 39 = 33 / 6 / 0 | 39 = 33 / 6 / 0 | **unchanged** (from the full run, by class: 50 = 44 / 6 / 0, as at `119eae0`) |
| Both towers classes | 14 / 0 | 14 / 0 (pins reconciled) | **14 / 0** |
| `TowersResidualContactClosureIntegrationTests` | - | 4 / 0 | **6 / 0** (+2 managed) |
| Native integration, full (12.2 min) | 490 = 463 / 25 / 2 | 494 = 467 / 25 / 2 | **496 = 469 / 25 / 2** |

**Failure identities** (`compare-trx.ps1`, by name and message; `native-full-vs-791bcaa.txt`,
`native-full-vs-119eae0.txt`, list in `native-full-non-passing.txt`):
- vs `119eae0`: identical; all 25 failures and both not-executed tests keep name and message.
- vs `791bcaa`: none new, none resolved. Of the 25 failures (19 historical + 6 rigid-motion), 23 keep their message;
  the 2 not-executed tests (`LargePanelSewGuard...` without its fixture variable, `NativeMissing...` with native
  present) are unchanged. The comparator's "SAME name, SAME message: 25" counts those 23 failures plus the 2 skips.
  Two already-failing golden masters changed message (an unguarded recount; see "Managed path"):
  - `Solve3D_ManagedPath` on towers: expected 21; got 20 at the baseline, 23 now;
  - `Solve3D_ManagedPath021` on towers: expected 25; got 26, now 27.

**Every firing in the suites** (at `119eae0`; temporary uncommitted census, reverted before the final run): 223 native
and 9 unit firings, all on `whole-level-towers.sam` or in the new unit tests. Seven distinct closures:
- the middle room, 100 firings;
- two caps at align >= 0.45 (0.042 / 0.052 m), 114 firings;
- one at align 0.7 (0.35 m), 4 firings;
- three only on the managed/default chains, 5 firings.

Over the 91-combination towers sweep (bucket 0.3..1.0 x align 0.15..0.7), **no combination loses any original cell
boundary**. Every non-middle closure leaves the cell set identical (`sweep-diff.txt`).

**Exports, labelled before/after:**
- towers processed panels and cells at four gaps match the validated probe face for face and shell for shell;
- the two-step and directional towers chains are identical to before;
- the nine-space owner exports at none / 0.20 / 0.45 / 0.55 / 0.65 (processed panels, adjacency cluster, cells)
  are identical to `exports-791bcaa` (`nine-space-exports-119eae0-compare.txt`), and the revision's run reproduces the `119eae0` exports at 1e-6 m
  (`nine-space-exports-vs-119eae0.txt`, 15 / 15 files);
- managed adopted complexes, guarded rebuilds and solved panels, before/after at bucketBetweenLevels 0 and 0.21,
  with round-trip checks and hashes: `.evidence-towers-managed-tests-20261023/exports/`.

## Dependency (disclosed)

`origin/sow/2026-Q4` is `819962d`. This branch adds twelve commits on top of it.
- **Nine-space wall-capture acceptance test** (record `documentation/Nine-Space-Wall-Capture-Q4.md`): `ef9bf81`,
  `8210c85`, `30e0e27`, `4d77ef4`.
- **North0 consolidation follow** (record `documentation/North0-Consolidation-Follow-Q4.md`): `ff71a70`, `4b0e53d`,
  `791bcaa`, `2a09f69`.
- **This correction:** `0dac3f4`, `119eae0`, and this record (two commits), then the managed-path revision.

A merge into `sow/2026-Q4` delivers all three. The North0 change provides `SnappedPanel.MoveBoundaryRun`, which this
correction uses. `git merge-tree` reports a clean merge with `origin/sow/2026-Q4`. Neither inherited branch had been
pushed or had its own PR; this PR is their first publication. Their review status is in their own records (the North0
record ends at "Independent review of 791bcaa").

## Risks and limits

- **Unguarded sew consumers (pre-existing; not only tests).** Any build of these panels with `SewBeforeBuild = true`
  and `ValidateSewCoverage` false (its default) sees a deterministic, input-dependent room set on the towers default
  chain: before, it lost the 76.994 and 155.536 m³ rooms inside an invalid −3767.5 m³ sew cell; now it loses the
  159.479 m³ room. `SAMOCCTCreateShells` builds its options from the user's sewing inputs without the guard;
  `SAMOCCTCreateAdjacencyCluster` enables it. Repairing or guarding that path is a separate owner decision, as is any
  change to the golden recount.
- **Managed re-facing:** on both managed chains two neighbouring cells change their face partition (boundaries
  preserved, see "Managed path"). Consumers keyed on output face identity, rather than geometry, see a change.
- **`Create.Spaces`** still refuses the default forced-managed towers result (13 naked edges, 19 before).
- **Coverage:** only `whole-level-towers.sam` fires in the suite. The rule is evidence-gated and fails closed, but
  other real models may contain contacts the controls do not cover. The authored-room inventory is heuristic and
  records pre-existing managed losses and Extend3D merges that are not addressed here.
- **Inert closures:** the align >= 0.45 and default-chain closures (caps 88 and 91; cap 8 at 0.21) move floor edges
  by 0.04-0.35 m without changing any solver cell. They are within the rule's definition; their downstream effect
  on other models is unproven.
- **Diagnostic ambiguity:** in `SAM_OCCT_EXTEND3D_PANEL`, the record line labels the target "cap #N" (an existing
  formatter); the Info diagnostic names the wall.
- **Fixed thresholds:** the G2 band depth (0.02 m) and tolerance (1e-6 m²/m) are pinned by controls and towers only.
- **Out of scope:** tilted levels and two-step chains keep the leak, by design.

## Owner decisions

Approved 2026-10-23: implementation of PROPOSAL-REVISED-2, the one-step / untilted / uniform scope, and measured
reconciliation of the directly affected towers count/volume pins. Open:
- review of this draft PR, including the inherited North0 and nine-space commits and the managed-path revision;
- acceptance of the two towers goldens as unguarded-recount measurements (left failing, not re-pinned), alongside
  the six rigid-motion and the historical failures;
- whether the unguarded sew path (golden recount, `SAMOCCTCreateShells`) is addressed by a separate task or only
  documented.

## Next step

Independent review of the managed-path revision, then owner review of the draft PR and the hosted CI result. No
merge or deployment is authorized. After an approved merge: the `PROJECT_PROGRESS.md` closeout on `sow/2026-Q4`, and
cleanup of the merged task branches (including the two inherited local branches, if their content merged here).
