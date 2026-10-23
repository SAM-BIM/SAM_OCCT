# Towers residual cap-edge closure (rule E) - branch record (2026-10-23)

Branch: `fix/towers-residual-closure-q4`, created from North0 head `2a09f69d0e3798dfb1369033cf06282d59b16a26`
(tested North0 content `791bcaa`). **Stacked on unmerged work:** it carries the eight nine-space / North0 commits
that are not on `origin/sow/2026-Q4` (see "Dependency").

Commits:
- `0dac3f4`: correction, tests, towers pin reconciliation;
- `119eae0`: the workflow-parity pin found by the full native run;
- then this record (`4b16897` and a wording follow-up).

**Tested content: `119eae0fd695a84e310b43fd66fbdde2d6a098b4`.** `PROJECT_PROGRESS.md` is not touched (post-merge
closeout only).

Specification and evidence (machine-local, outside the repository; copy them before switching machines):
- `SAM-BIM/.review-towers-closure-revision-20261023/PROPOSAL-REVISED-2.md`: the validated rule.
- `SAM-BIM/.review-towers-closure-independent-20261023/REVIEW.md`: the independent review behind fixes 1-5.
- `SAM-BIM/.review-towers-closure-impl-20261023/`: this implementation's evidence (README, CENSUS-SUMMARY).

## Status

Implemented, locally validated, and in an owner-review draft PR. **The middle towers room (119.364 m³), lost to a
12-28 mm floor/north-wall slot, is recovered** at doubleWallGap 0 / 0.4 / 0.47 / 0.5 on the one-step uniform chain,
with every other cell's complete boundary unchanged. The directional and two-step paths, and so the owner-accepted
nine-space exports, are identical.

Full native: 494 = 467 passed / 25 failed / 2 not executed. Against the 791bcaa baseline:
- nothing new;
- the 25 failures (19 historical + 6 rigid-motion) are unchanged by name and message;
- two already-failing towers golden masters report different counts (explained below; goldens not re-pinned).

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
the two-step (`inputAlreadyClean`) path, tilted levels, Grasshopper components.

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

## Tests

- **`ResidualContactClosureTests` (unit, 37):** controls C01-C31, the self-overlap proof, the reachable-C31
  attribution test, and end-to-end gate and misattribution cases. See `TESTING.md`.
  - **Mutation check** (`mutation-check.txt`): each safeguard disabled alone makes at least one test fail.
    - G1: C28.
    - Ambiguity: the two-clean-face test.
    - G2: C26 and C30.
    - Non-vertical blockers: C19 and C20.
    - Cyclic contiguity: C21.
    - Self-overlap: the pre-check and the simple-polygon post-check each cover C29 alone, so both must be removed
      before C29 fails; defence in depth.
- **`TowersResidualContactClosureIntegrationTests` (native, 4 gaps).** Per gap:
  - exactly one closure, and the room appears: 119.364 m³, 10 faces, bbox x 17.1427..21.9509,
    y -8.9172..-0.7442, containing (19.5, -5, 13.8);
  - every cell of the gated-off two-step control reappears with an identical complete boundary;
  - the cells are valid (`OcctUnderSplitGuard`).
- **Reconciled pins** (measured values in `towers-pins-measure-before-reconcile.log`):
  - `PR61TowersQuantitativeValidationTests`: cells 31 -> 32 (gap 0) and 30 -> 31 (0.4, delta test); volume
    +119.364 m³; cell floor area +39.136 m²; level-datum panel area +0.286 m² at every gap; VolumeDrift05
    0.3722 -> 0.3677; the 0.4 -> 0.5 delta 20.745 m³ unchanged;
  - `TowersBucketLeverDiagnosticTests.Towers_DoubleWallGap_FixAcceptance_Gap05StripSurvives`: 30 -> 31 at 0.47;
  - `WorkflowParityIntegrationTests.WorkflowParity_WholeLevelTowers_FixtureTuning04_Produces32ParityCleanSpaces`
    (renamed from `..._Produces31...`): its own documentation named the one missing reference space near
    (19.584, -4.909, 13.925), which is this room. It now reaches 32 OCCT cells and 32 SAM spaces, parity clean.

## Validation (local, Windows; frozen native)

Native `build/` was copied from the North0 worktree: `SAM.Occt.Native.dll` 438089F8..., `TKBO.dll` 1FA732FE...
(ABI 4, unchanged). Managed SAM / SAM_Solver were read-only from their checkouts' `build/` (unchanged since
2026-10-22).
- **Builds** (`--no-incremental -p:SAM_OCCT_SKIP_NATIVE_BUILD=true`): unit 0 errors / 243 warnings, integration
  0 errors / 284 warnings. Both equal the 791bcaa baseline; the new code adds no warning.
- **`BuildAlls_v5` not used.** It builds and deploys the main checkouts, not this worktree, so the worktree was
  validated with the commands below.

```powershell
dotnet build Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug --no-incremental -p:SAM_OCCT_SKIP_NATIVE_BUILD=true
dotnet build Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug --no-incremental -p:SAM_OCCT_SKIP_NATIVE_BUILD=true
$env:SAM_OCCT_NINE_SPACE_EXPORT_DIR = "<dir>"; $env:SAM_OCCT_TEST_CODE_SHA = "<tested SHA>"
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug --no-build
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug --no-build
```

| Suite (local) | Base `791bcaa` | **`119eae0`** |
| --- | --- | --- |
| Unit, full | 995 / 0 | **1032 / 0** (+37) |
| Nine-space filter (wall capture + controlled acceptance + North0 follow) | 39 = 33 / 6 / 0 | **39 = 33 / 6 / 0** |
| Both towers classes | 14 / 0 | **14 / 0** (pins reconciled) |
| New towers integration | - | **4 / 0** |
| Native integration, full (9.6 min) | 490 = 463 / 25 / 2 | **494 = 467 / 25 / 2** |

**Failure identities** (`native-full-119eae0-vs-791bcaa.txt`, by `compare-trx.ps1`): none new, none resolved, and 25
unchanged by name and message. Two already-failing golden masters changed message:
- `Solve3D_ManagedPath` on towers: expected 21; got 20 at the baseline, 23 now;
- `Solve3D_ManagedPath021` on towers: expected 25; got 26, now 27.

Analysis (`CENSUS-SUMMARY.md`, `managed-step2-shells-compare.txt`, `managed-subsets.txt`):
- On the managed path's own Step-2 output, the change is exactly +1 room with every other cell boundary identical.
- The managed resolve and heal stage downstream was already unstable. At the baseline it formed a 3428.663 m³
  multi-storey blob and dropped the 76.994 m³ room. Now it forms no blob, keeps the 76.994 m³ room, and instead
  drops the 159.479 m³ room next to it.
- The goldens are not re-pinned.

**Every firing in the suites** (temporary uncommitted census, reverted before the final run): 223 native and
9 unit firings, all on `whole-level-towers.sam` or in the new unit tests. Seven distinct closures:
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
  are identical to `exports-791bcaa` (`nine-space-exports-119eae0-compare.txt`).

## Dependency (disclosed)

`origin/sow/2026-Q4` is `819962d`. This branch adds twelve commits on top of it.
- **Nine-space wall-capture acceptance test** (record `documentation/Nine-Space-Wall-Capture-Q4.md`): `ef9bf81`,
  `8210c85`, `30e0e27`, `4d77ef4`.
- **North0 consolidation follow** (record `documentation/North0-Consolidation-Follow-Q4.md`): `ff71a70`, `4b0e53d`,
  `791bcaa`, `2a09f69`.
- **This correction:** `0dac3f4`, `119eae0`, and this record (two commits).

A merge into `sow/2026-Q4` delivers all three. The North0 change provides `SnappedPanel.MoveBoundaryRun`, which this
correction uses. `git merge-tree` reports a clean merge with `origin/sow/2026-Q4`. Neither inherited branch had been
pushed or had its own PR; this PR is their first publication. Their review status is in their own records (the North0
record ends at "Independent review of 791bcaa").

## Risks and limits

- **Managed `Solve3D` resolve instability** on towers: see Validation. It is pre-existing, but the room it drops
  moved. A user of `Solve3D(forceManagedPipeline)` on this model sees 23 cells instead of 20, including a
  different missing room. Track it separately; it is not caused by this rule's geometry.
- **Coverage:** only `whole-level-towers.sam` fires in the suite. The rule is evidence-gated and fails closed, but
  other real models may contain contacts the controls do not cover.
- **Inert closures:** the align >= 0.45 and default-chain closures move floor edges by 0.04-0.35 m without
  changing any cell. They are within the rule's definition; their downstream effect on other models is unproven.
- **Diagnostic ambiguity:** in `SAM_OCCT_EXTEND3D_PANEL`, the record line labels the target "cap #N" (an existing
  formatter); the Info diagnostic names the wall.
- **Fixed thresholds:** the G2 band depth (0.02 m) and tolerance (1e-6 m²/m) are pinned by controls and towers only.
- **Out of scope:** tilted levels and two-step chains keep the leak, by design.

## Owner decisions

Approved 2026-10-23: implementation of PROPOSAL-REVISED-2, the one-step / untilted / uniform scope, and measured
reconciliation of the directly affected towers count/volume pins. Open: review of this draft PR (including the
inherited North0 and nine-space commits), and the treatment of the managed-resolve golden instability.

## Next step

Owner review of the draft PR and the hosted CI result. No merge or deployment is authorized. After an approved
merge: the `PROJECT_PROGRESS.md` closeout on `sow/2026-Q4`, and cleanup of the merged task branches (including the
two inherited local branches, if their content merged here).
