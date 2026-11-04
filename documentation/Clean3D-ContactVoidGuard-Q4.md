# Clean3D contact-aware void guard (SAM_OCCT consumer of SAM#230), Q4

Record document for the draft PR `fix/clean3d-contact-void-guard-q4` -> `sow/2026-Q4` (base `e6540954`). Design: rev 5 section 3,
owner-signed 2026-11-03 (machine-local: `SAM_daily/2026-10-25-Softthouse/handover/evidence/2026-11-03-d203-trace/DESIGN-contact-aware-void-guard.md`).
Producer: draft SAM-BIM/SAM#230 (`feat/panelsbysolid-source-evidence-q4`, head `aeafa38f`, tested content `e2bfbf2d`). **Merge order: SAM#230 first.**

## Current status
Implemented and tested locally; **draft, not merged**. Tested content: `06743da` (production code unchanged since `68b678f`; `06743da` adds one test).
Hosted CI cannot be green before SAM#230 is merged (see "CI"). Nothing changes for users until both PRs land: without a valid record on both
panels Stage A is byte-for-byte today's behaviour.

## What it does
Stage A refuses to collapse an anti-parallel pair whose centroid separation is over 0.3 m (`OPPOSED_PARTITION_MAX_SEPARATION`), because that looks
like a void. Softhouse D203 (#251/#250) is two skins of one stepped wall that touch, so the guard blocked the weighted midpoint. The guard is now released
for a pair only when **both** panels carry a still-valid source record and the clipped common region is proved to be in contact.

- `ContactEvidence` (new, geometry layer): plain carrier of the record (plane, guaranteed thickness g, `Support`, `Origin` as `Face2D`) and the one region
  operation the guard may use. The geometry solver has **no `SAM.Analytical` reference** (owner correction 2).
- Adapter `Modify.ResolveContactEvidences` (`SAM.Analytical.OCCT.Solver/Modify/Solve.cs`): the only reader of `Panel.SourceEvidence`, and only through
  `Query.ValidSourceEvidence` (SAM#230 conditions a, b, e). The region operation supplied is `Query.DefiniteIntersection`. Wired into `Clean3D`, `Extend3D`,
  `Solve3D`, `OpenPanels3D` and the analytical `AutoTune3D` (all five build their solver from `PrepareInput`'s `sources`), through new
  `Panel3DSnapSolver.ContactEvidences` / `AutoTune3DSolver.ContactEvidences` properties. Default null = today's behaviour; no public signature changed (new
  `SnappedPanel` constructor overload, new properties).
- `SnappedPanel.LiveContactEvidence` (conditions c, d): the record is live only while (d) the panel stands for exactly one source, (c) every vertex of
  its current boundary is within `Core.Tolerance.Distance` of the record's plane (checked live: a Stage A move of 2e-6 invalidates, 0.5e-6 does not),
  and no grow/extend/trim writer has rewritten its in-plane boundary (`GrowOutward`, `Adopt` clear it; `Absorb` clears it).
- `ContactVoidGuard.Releases` (conditions 1-5, in the first panel's record frame, true polygons): P' = OriginA n OriginB n currentFaceA n currentFaceB (current
  external faces, so trimmed-away material does not count); coverage `area(P') / max(bbox areas) >= COPARALLEL_SNAP_MIN_OVERLAP_RATIO` (**0.5**, owner correction 1; the
  0.97 `OPPOSED_PARTITION_MIN_OVERLAP_RATIO` would reject D203 at 0.87); no certified-thickness gap inside P' (`area(P') - area(K') <= Tolerance.Distance * perimeter(P')`);
  and `gap(v) = |t_B| - gB/(2|c|) - gA/2 <= Tolerance.Distance` at every vertex of P' (outer and inner loops). A `null` from the intersection, an empty P', planes not
  opposed or `|c| < 0.1` all refuse.
- Guard sites (the only two predicate edits): `SnapOpposedPartitions` skips a contact-proven pair silently (no "real void" message, no collapse onto the smaller skin);
  `Snap` (weighted) keeps its `continue` unless `overlap && ContactVoidGuard.Releases(...)`. **`overlap &&` is required** (owner correction 1): the `abut` branch alone can never release.
  `RejectedCollapse` remains only for actual rejections (owner correction 4); a release is recorded by the existing weighted-snap `CleanRecord` (`SnappedToBacker`, 0.162 m for D203).
- Unchanged: the 0.3 constant, the `<= 0.3` band, the same-facing path, `ConsolidateWallStacks`, inset logic, `StackedSlabInterfaceDetector`.

## `face3D` writers re-confirmed (design section 1)
Writers in `SnappedPanel.cs`: `SnapToBacker` (plane move; caught by live check c), `StripInternalEdges` (inner loops only; record stays live, tested), `GrowOutward`
and `Adopt` (in-plane rewrites; clear the record). Callers of the rewriting family: `ExtendWalls`/`ExtendWallToNearestCap` and `FillCore` (Stage B, after the guard),
`DragAbuttingWallEnds`/`DragAbuttingCapEdges`/`MoveInsetCapEdge` (reached only from `ConsolidateWallStacks`, Stage A step 2a, after `SnapOpposedPartitions` and
`SnapToFixedPoint`, and not users of this guard). So Stage A reaches the guard sites with only in-plane-boundary-preserving writes, as the design states.

## Tests
New (63): `ContactVoidGuardTests` (predicate), `ContactVoidGuardStageATests` (Stage A, through `SnapOpposedPartitions` + `SnapToFixedPoint` and the whole `SnapStage.Clean`; record
lifetime), `SourceEvidenceWiringTests` (`Modify.Clean3D` end to end with records carried on the panels' JSON; invalid/moved/other-footprint records ignored; adapter), shared
`ContactFixtures`. Controls: A (both orientations, midpoint 3.341 +- 1e-6, record 0.162, no `RejectedCollapse`; 87 % coverage), B, C (60/40, 40 % middle, 90/10, sliver below
tolerance*perimeter), T1, T2, W (0.05 m both directions reject, 5e-7 admit, planes crossing inside the region), F (aligned openings), D (wiring side), same-facing regression pins,
`abut`-only pair stays, partial overlap below eligibility stays, thickness-bound edges (+0.5e-6 admit, +2e-6 reject), thickness along the normal for tilted skins, null intersection,
no region operation, perpendicular planes. Written first: against a stub that never releases, exactly the 13 admit tests failed; the reject tests pass trivially by design.

Mutation matrix (each change applied alone, guard + wiring tests run, then reverted): forced true kills 29 tests; forced false 17; no clipping by current faces (T1, T2); coverage floor 0.97
(A with 87 %); no coverage check; no gap check (C, F); no separation bound (W, tolerance edge, crossing planes, 1 mm apart); separation at one vertex only (W); weighted site without `overlap`
gate (abut-only test); weighted site never releases; opposed site never releases; live plane check removed; one-source/Absorb check removed; in-plane writers keep the record;
null intersection read as empty. All 16 killed; the one that first survived (thickness `g / (2|c|)` -> `g / 2`) is now killed by the tilted-skin test.
Output: `SAM_daily/.../evidence/2026-11-04-contact-guard-chains/logs/mutation-results.txt`.

## Local results (Windows, APPDATA redirected for every build; frozen native `SAM.Occt.Native.dll` `438089F8`, TKBO `1FA732FE`, hash-checked before native runs)
Build inputs: SAM `sow/2026-Q4` `7c4591cd` + SAM#230 (`aeafa38f`) built in `SAM-wt-source-evidence`, copied to an isolated `SAM\build`, with the 42 Grasshopper/other assemblies the #230
build lacks taken from the plain Q4 SAM build (compile only); SAM_Solver `fd63b46` build. Branch and baseline built side by side (`_contact_guard_build\SAM_OCCT`, `...\SAM_OCCT_base` at `e654095`).

| Check | Baseline `e654095` | Branch `06743da` |
| --- | --- | --- |
| Build (Debug sln + Grasshopper project; Release for chains) | 0 errors | 0 errors |
| `SAM.OCCT.UnitTests` | 1140 / 0 | **1203 / 0** (+63) |
| `SAM.OCCT.GrasshopperTests` | 38 (reference) | **38 / 38** |
| Native integration (real `%APPDATA%`, `--no-build`) | 25 failed / 478 / 2 not executed | **25 / 478 / 2, per-test outcome and first message identical** (`trxcmp.sh`) |

Towers goldens and pins: untouched (no file in those tests changed; native outcomes identical by name).

Softhouse chains (machine-local, `SAM_daily/.../evidence/2026-11-04-contact-guard-chains/`; tool `tools-2026-11-03-d203`, `BBL=0.21 FILL=0.5`; `SAMDIRS` in `logs/samdirs.txt`; `%APPDATA%\SAM` hashes before/after identical):
- **N1 R** (re-extracted with SAM#230, `REVFACES=1`): 380 panels, Clean3D 367 -> 79, 71 spaces, 395 panels, 46 good, frozen 48 kept 46. Clean3D: the D203 `0.324 m apart` rejection is
  gone and `#228 snapped-to-backer; moved 0.162 m onto #229` appears (the second 0.324 m rejection, #250/#247, remains, unsnapped). D203 = Cell 64 closed, 13 faces, 43.677 m3; D303 = Cell 62 GOOD 44.887 m3.
  `good.tsv` vs plain Q4 R (`q4e-R`): D203 not -> GOOD, Kitchen 1 D105-6 `open(3)` cleared (still not GOOD), the rest only renumbered cell ids; not-closed spaces 8 -> 6.
- **N2 F**: `good.tsv`, `v2_spaces.tsv`, `v2_rooms.tsv` byte-identical to plain Q4 F; `clean3d.txt`/`extend3d.txt` identical after masking GUIDs; frozen 48 kept 48.
- **N0**: the saved R input (`R-control/input-panels.json`) with the records for #247/#250/#251 copied in (scratch tool, `scratchpad/inject`), Clean3D + Extend3D + native: `good.tsv` and
  `v2_spaces.tsv` byte-identical to `R-flip251-native` (the orientation replay) and to N1; 71 / 395 / 46.
  **Deviation:** the injected records are the ones SAM#230 produced in N1 (matched by `Source Name`), not the design's separately proved prover records; N1 supersedes N0 as the independent-producer check.

## Decisions and assumptions
- Carrier + adapter instead of a `SAM.Analytical` reference in the geometry solver (owner correction 2). Intersection supplied as a delegate on the carrier so `SnapOpposedPartitions`/`Snap` keep their public signatures.
- Opposed pass releases silently and relies on the weighted pass's own eligibility (`overlap`, ratio 0.5), which the guard's coverage already implies (region within both boxes); a pair the weighted pass does not take simply stays, without a message.
- `|c| < 0.1` constant is a guard against dividing by a near-zero cosine, inert for pairs that passed `IsParallelWith` (|c| >= 0.94).
- Extra conservatism beyond the design: any in-plane writer clears the record (`GrowOutward`, `Adopt`).

## CI (dependency resolution)
`.github/workflows/build.yml` clones SAM and SAM_Solver at the **newest `sow/YYYY-Qn` branch**, not by PR branch name; AGENTS.md's branch-name selection does not apply to this workflow. Until SAM#230
is merged into SAM `sow/2026-Q4`, hosted CI of this PR will not compile (`Panel.SourceEvidence`, `Query.ValidSourceEvidence`, `Query.DefiniteIntersection` missing). After the SAM merge: re-run CI at the
final head and record the run ids here. The workflow was not edited.

## Unresolved, risks, not covered
- Hosted CI at the final head, owner approval and codex comments: pending. Check for codex bot comments right before any merge.
- `Modify.AutoTune3D` is wired and compiled but has no native test with records (no existing AutoTune native fixture carries `SourceEvidence`).
- Records are validated once per `Clean3D`/`Extend3D`/`Solve3D` call (`ValidSourceEvidence` does Face2D intersections); cost not measured on a model with many records beyond the chain runs (R: 362 records).
- Same-facing asymmetry remains (pinned by tests, not fixed). Aligned openings inside P' block release (conservative). Anti-parallel pairs <= 0.3 m unchanged.
- Chains used Release builds of the branch and the baseline-pinned frozen native; the Debug unit/native runs and Release chains are different configurations of the same source.
- Residual build-input difference: Grasshopper assemblies in the isolated SAM build come from plain Q4 (compile only; the Grasshopper project is not changed by this PR).

## Next step
Owner review of this draft; merge SAM#230 first, then re-run hosted CI here at the final head, then owner approval and merge, then the `PROJECT_PROGRESS.md` closeout on `sow/2026-Q4` with the merge SHA.
Commit and push before switching computers; the chain evidence under `SAM_daily` and `_contact_guard_build` is machine-local.
