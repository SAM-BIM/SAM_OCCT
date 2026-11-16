# Clean3D contact-aware void guard (SAM_OCCT consumer of SAM#230), Q4

Record document for the draft PR `fix/clean3d-contact-void-guard-q4` -> `sow/2026-Q4` (base `e6540954`). Design: rev 5 section 3,
owner-signed 2026-11-03 (machine-local: `SAM_daily/2026-10-25-Softthouse/handover/evidence/2026-11-03-d203-trace/DESIGN-contact-aware-void-guard.md`).
Producer: SAM-BIM/SAM#230 (`feat/panelsbysolid-source-evidence-q4`), **merged 2026-11-04 as `3a029e37`** (final head `1aa1c3a2`, tested content `6eddd56d`); SAM `sow/2026-Q4` is now `aa1cb777`.

## Current status
Implemented and tested locally on merged SAM; **draft, not merged**. Tested content: `5faac61` (the B1 fix from the independent review on top of the
original `06743da`; production code otherwise unchanged since `68b678f`). Nothing changes for users until the Grasshopper deploy is rebuilt; without a valid
record on both panels Stage A is byte-for-byte today's behaviour. Hosted CI at the final head: pending the push (see "CI").

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
  opposed or `|c| < 0.1` all refuse. Both records' `Support` and `Origin` keep their inner loops (openings), whichever panel is passed first (B1 below).
- Guard sites (the only two predicate edits): `SnapOpposedPartitions` skips a contact-proven pair silently (no "real void" message, no collapse onto the smaller skin);
  `Snap` (weighted) keeps its `continue` unless `overlap && ContactVoidGuard.Releases(...)`. **`overlap &&` is required** (owner correction 1): the `abut` branch alone can never release.
  `RejectedCollapse` remains only for actual rejections (owner correction 4); a release is recorded by the existing weighted-snap `CleanRecord` (`SnappedToBacker`, 0.162 m for D203).
- Unchanged: the 0.3 constant, the `<= 0.3` band, the same-facing path, `ConsolidateWallStacks`, inset logic, `StackedSlabInterfaceDetector`.

## `face3D` writers re-confirmed (design section 1)
Writers in `SnappedPanel.cs`: `SnapToBacker` (plane move; caught by live check c), `StripInternalEdges` (inner loops only; record stays live, tested), `GrowOutward`
and `Adopt` (in-plane rewrites; clear the record). Callers of the rewriting family: `ExtendWalls`/`ExtendWallToNearestCap` and `FillCore` (Stage B, after the guard),
`DragAbuttingWallEnds`/`DragAbuttingCapEdges`/`MoveInsetCapEdge` (reached only from `ConsolidateWallStacks`, Stage A step 2a, after `SnapOpposedPartitions` and
`SnapToFixedPoint`, and not users of this guard). So Stage A reaches the guard sites with only in-plane-boundary-preserving writes, as the design states.
The independent review (2026-11-16) re-checked this independently and found no missed writer.

## Independent review (2026-11-16) and B1 fix
A fresh reviewer (read-only, given `e654095..06743da` and the design, not this record) checked unchanged behaviour without records, the geometry solver's
independence from `SAM.Analytical`, record lifetime, the guard sites, the guard maths, the wiring, SPDX/diagnostics and tests. Verified OK: decisions unchanged when
no valid record is on both panels; no `SAM.Analytical` reference and no public signature removed; lifetime (plane move, `Absorb`, `GrowOutward`, `Adopt`); weighted site
gated by `overlap && Releases`; coverage, gap and thickness-along-the-normal maths; all five callers wired with index-aligned sources; SPDX on the new `.cs` files.

- **B1 (blocker, fixed, `5faac61`):** `ContactVoidGuard.FromFrame` projected the second panel's `Support` and `Origin` with `externalOnly = true`, dropping their
  openings while the first panel's record was used as is. An opening on the second panel's support only gave P' - K' = 0 and released a pair that must be refused;
  `Releases(a, b)` and `Releases(b, a)` disagreed. Fix: `Project(to, face3D, externalOnly: false)`. Tests (4, all failing before the fix): opening in the support / the origin of one
  panel only, on the first or the second panel, refusal asserted in both argument orders.
- **S1 (accepted by the owner, not changed):** a released pair emits no diagnostic of its own; the "real void" Info simply no longer appears, and the weighted-snap `CleanRecord`
  (`SnappedToBacker`) is the visible trace. DIAGNOSTICS_STANDARD section 2 rule 5 would suggest an Info line; owner chose to skip it for this PR.
- **S2 (not changed):** if the opposed site releases a pair and the weighted site (different argument order, weight/bucket order) then refuses it, the pair stays unmoved and the
  `RejectedCollapse` Info is not emitted. Geometry is unchanged; only a message is lost. Documented, not fixed.
- N2: `ParameterDiscoverySolver` builds its solver without `ContactEvidences`, so the discover path ignores records (consistent, undocumented before). N3: `Area()` assumes the producer's
  Support/Origin pieces are disjoint (true today). N4: `|c| < 0.1` is inert. N5: record validation cost acceptable.
- Test gaps the reviewer noted, not added: `Solve3D`/`Extend3D`/`AutoTune3D` wiring beyond `Clean3D` and the adapter; a third partner after a midpoint move; a non-vertical (cap/roof) pair.

## Grasshopper component versions
This PR changes no Grasshopper file, but the computed result of Clean3D, Extend3D, Solve3D, OpenPanels3D and AutoTune3D changes when records are present (GH-F2/H2: behavioural, minor bump).
`sow/2026-Q4` already carries unreleased minor bumps from SAM_OCCT#85 over the Q3 release (Clean3D 0.6.0 to 0.7.0, Extend3D 0.10.0 to 0.11.0, Solve3D 0.7.0 to 0.8.0, AutoTune3DDiscover
0.4.0 to 0.5.0, AutoTune3DLegacy 0.1.0 to 0.2.0); #84 shares them (`26ddbaf`). Checked 2026-11-16: SAM_Deploy's latest release is `v20261006.1` (2026-10-06), before #85 merged (2026-10-27), so
those bumps are still unreleased and **this PR shares them; no further bump is made**. Not done: naming #89 in the version-test comments (as #84 does); owner to confirm that is wanted.

## Tests
New (67): `ContactVoidGuardTests` (predicate, including the 4 B1 tests), `ContactVoidGuardStageATests` (Stage A, through `SnapOpposedPartitions` + `SnapToFixedPoint` and the whole `SnapStage.Clean`; record
lifetime), `SourceEvidenceWiringTests` (`Modify.Clean3D` end to end with records carried on the panels' JSON; invalid/moved/other-footprint records ignored; adapter), shared
`ContactFixtures`. Controls: A (both orientations, midpoint 3.341 +- 1e-6, record 0.162, no `RejectedCollapse`; 87 % coverage), B, C (60/40, 40 % middle, 90/10, sliver below
tolerance*perimeter), T1, T2, W (0.05 m both directions reject, 5e-7 admit, planes crossing inside the region), F (aligned openings), D (wiring side), same-facing regression pins,
`abut`-only pair stays, partial overlap below eligibility stays, thickness-bound edges (+0.5e-6 admit, +2e-6 reject), thickness along the normal for tilted skins, null intersection,
no region operation, perpendicular planes, one-sided openings (support, origin) in both argument orders. Written first: against a stub that never releases, exactly the 13 admit tests failed; the reject tests pass trivially by design.

Mutation matrix (run on `06743da`, before B1; each change applied alone, guard + wiring tests run, then reverted): forced true kills 29 tests; forced false 17; no clipping by current faces (T1, T2); coverage floor 0.97
(A with 87 %); no coverage check; no gap check (C, F); no separation bound (W, tolerance edge, crossing planes, 1 mm apart); separation at one vertex only (W); weighted site without `overlap`
gate (abut-only test); weighted site never releases; opposed site never releases; live plane check removed; one-source/Absorb check removed; in-plane writers keep the record;
null intersection read as empty. All 16 killed; the one that first survived (thickness `g / (2|c|)` -> `g / 2`) is now killed by the tilted-skin test. The B1 change is covered by its own four tests (they fail on the previous code).
Output: `SAM_daily/.../evidence/2026-11-04-contact-guard-chains/logs/mutation-results.txt`.

## Local results (Windows, APPDATA redirected for every build; frozen native `SAM.Occt.Native.dll` `438089F8`, TKBO `1FA732FE`, hash-checked before and after native runs; `%APPDATA%\SAM` file hashes before/after identical)
Build inputs (2026-11-16): **merged SAM `aa1cb777`** exported with `git archive` and fully built (`SAM.sln`, Release, 0 errors) into an isolated `_contact_guard_build\SAM\build` (replaces the earlier
hybrid of the SAM#230 build plus 42 plain-Q4 assemblies; kept as `_contact_guard_build\SAM_hybrid_2026-11-04`); SAM_Solver `fd63b467`. Branch and baseline built side by side
(`_contact_guard_build\SAM_OCCT`, `...\SAM_OCCT_base` at `e654095`).

| Check | Baseline `e654095` | Branch `5faac61` (merged SAM) |
| --- | --- | --- |
| Build (Debug sln + Grasshopper project; Release for chains) | 0 errors | 0 errors |
| `SAM.OCCT.UnitTests` | 1140 / 0 | **1207 / 0** (+67) |
| `SAM.OCCT.GrasshopperTests` | 38 (reference) | **38 / 38** |
| Native integration (real `%APPDATA%`, `--no-build`) | 25 failed / 478 / 2 not executed | **25 / 478 / 2, per-test outcome and first message identical** (`trxcmp.sh`) |

Towers goldens and pins: untouched (no file in those tests changed; native outcomes identical by name). The same runs were also made on `06743da` before the B1 fix with identical results (unit 1203 / 0).

Softhouse chains (machine-local; tool `tools-2026-11-03-d203`, `BBL=0.21 FILL=0.5`, R = `REVFACES=1`; evidence `SAM_daily/.../evidence/2026-11-16-contact-guard-merged-sam/`: `m1-R/F` on `06743da`, `m2-R/F` on `5faac61`, logs and `run-chains.sh` in `logs/`, `logs-fix/`):
- **R** (`m2-R`): 380 panels, Clean3D 367 -> 79, 71 spaces, 395 panels, 46 good. D203 = Cell 64 closed, 13 faces, 43.677 m3; D303 = Cell 62 GOOD 44.887 m3.
- **F** (`m2-F`): 384 panels, 72 spaces, 397 panels, 48 good.
- For R and F, `good.tsv`, `v2_spaces.tsv` and `v2_rooms.tsv` are byte-identical across `m1`, `m2` and the 2026-11-04 `n1-R`/`n1-F` runs (SAM#230 at `aeafa38f`, hybrid inputs). Stage JSON differs only by run-specific GUIDs (not masked here).
- Earlier (2026-11-04, `06743da`, SAM#230 `aeafa38f`, `2026-11-04-contact-guard-chains`): Clean3D shows the D203 `0.324 m apart` rejection gone and `#228 snapped-to-backer; moved 0.162 m onto #229` (the second 0.324 m rejection,
  #250/#247, remains, unsnapped); `good.tsv` vs plain Q4 R: D203 not -> GOOD, Kitchen 1 D105-6 `open(3)` cleared (still not GOOD), the rest only renumbered cell ids; not-closed spaces 8 -> 6; F byte-identical to plain Q4 F;
  N0 (saved R input with the records for #247/#250/#251 injected from the N1 producer output, not the design's separate prover records) identical to N1.

## Decisions and assumptions
- Carrier + adapter instead of a `SAM.Analytical` reference in the geometry solver (owner correction 2). Intersection supplied as a delegate on the carrier so `SnapOpposedPartitions`/`Snap` keep their public signatures.
- Opposed pass releases silently and relies on the weighted pass's own eligibility (`overlap`, ratio 0.5), which the guard's coverage already implies (region within both boxes); a pair the weighted pass does not take simply stays, without a message (S2).
- `|c| < 0.1` constant is a guard against dividing by a near-zero cosine, inert for pairs that passed `IsParallelWith` (|c| >= 0.94).
- Extra conservatism beyond the design: any in-plane writer clears the record (`GrowOutward`, `Adopt`).
- 2026-11-16 owner decisions: fix B1 with tests; skip S1.

## CI (dependency resolution)
`.github/workflows/build.yml` clones SAM and SAM_Solver at the **newest `sow/YYYY-Qn` branch**, not by PR branch name; AGENTS.md's branch-name selection does not apply to this workflow. The workflow was not edited.
After SAM#230 merged, the hosted Build run `41794950301` was re-run (attempt 2, 2026-11-04) at the then head `b85825e` and passed (unit 1203 / 1203; integration 31 passed / 474 skipped, no native on the runner);
SPDX run `41794950328` (attempt 1) passed. **Those runs predate the B1 fix** (`5faac61`); hosted Build and SPDX must be re-run at the final head after the push, and their run ids recorded in the PR description.

## Unresolved, risks, not covered
- Hosted CI at the final head, owner approval and codex comments: pending. Check for codex bot comments right before any merge.
- `Modify.AutoTune3D` is wired and compiled but has no native test with records (no existing AutoTune native fixture carries `SourceEvidence`).
- Records are validated once per `Clean3D`/`Extend3D`/`Solve3D` call (`ValidSourceEvidence` does Face2D intersections); cost not measured on a model with many records beyond the chain runs (R: 362 records).
- Same-facing asymmetry remains (pinned by tests, not fixed). Aligned openings inside P' block release (conservative). Anti-parallel pairs <= 0.3 m unchanged.
- S1 (no diagnostic on release) and S2 (lost "real void" Info when the two guard sites disagree) are accepted limits; see above.
- Chains used Release builds of the branch and the baseline-pinned frozen native; the Debug unit/native runs and Release chains are different configurations of the same source.
- The deployed Grasshopper needs a coherent redeploy (`BuildAlls_v5.bat`) now that SAM#230 and this PR have both landed; nothing was deployed from this work.

## Next step
Owner approval to push the head (hosted checks run at the final head, no `[skip ci]`), then PR description update and mark ready, then owner approval for the exact head, codex-comment check, `gh pr merge 89 --repo SAM-BIM/SAM_OCCT --merge --match-head-commit <sha>`,
then the `PROJECT_PROGRESS.md` closeout on `sow/2026-Q4` with the merge SHA (`[skip ci]`) and remote branch cleanup.
Commit and push before switching computers; the chain evidence under `SAM_daily` and `_contact_guard_build` is machine-local.
