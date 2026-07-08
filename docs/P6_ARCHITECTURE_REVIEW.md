# P6 Architecture Review — Per-Level Frames (True 3D Panel Solver)

Status: review checkpoint · Date: 2026-07-04 · Branch: `fix/solver-raw-first` (PR #48)

This document records the architecture review performed after Phase 6 (per-level frames) landed and
before Phase 7 (cell classification, Spaces handoff) begins. Where this review and
`docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md` differ, **this review wins**. It is read-only in
origin (no code was changed to produce it); the 7-pre cleanup items it recommends are tracked and
executed separately.

---

## A. Executive verdict

**GO WITH CHANGES.** Proceed to Phase 7 after two small, bounded pre-flight items (pin the managed
golden-master numbers in the test; give frame-aware NormalizeCaps diagnostics parity). Phase 6 as landed
is architecturally sound, honestly scoped, fully committed, and regression-locked. The per-frame
extend/fill deferral was the right call (its prototype regressed a RAW golden 22→8 cells) and is NOT a
Phase 7 prerequisite — Phase 7 consumes the final cell complex, not the conditioning frames. No
architectural correction is needed.

## B. Current repo state (verified 2026-07-04)

- Branch `fix/solver-raw-first`, clean tree, in sync with origin. Host PR #48 (OPEN).
- Phase 6 commits, all committed: 6a `b1b643b` (LevelFrame, 554 lines + 337 test lines), 6b `df054ac`
  (frame-aware classification + FaceRole), 6c `0cac0b9` (frame-aware NormalizeCaps), 6d `a24545c`
  (StackedSlabInterfaceDetector, observational), 6e `2e4e6df` (docs-only wrap-up). Phase 6 complete per
  its re-scoped definition; per-frame extend/fill explicitly deferred with stop rules (TESTING.md §6e).
- Verification run (TESTING.md §6e): build 0 errors; unit 400/400; integration 126 pass / 1 skip
  (inverse-gated native-missing test); raw goldens byte-identical 5/5 (22/0, 2/0, 22/0, 43/0, 32/0);
  managed = documented 6c baseline (22/0, 2/0, 22/0, **29/29**, **22/12**); Benchmark1500 ~6.4 s.
- No uncommitted/risky work.

## C. What Phase 6 got right

1. Sub-phase discipline: five commits, goldens re-run per commit, raw signatures byte-identical
   throughout — the Phase 5 stop-rule contract was honored to the letter.
2. The deferral itself: the per-frame extend/fill prototype regressed `whole-level-tilted` RAW 22→8
   cells; work stopped per stop rules, was root-caused (one analytical level spanning ~34°/~56° cap
   orientations; splitting conditioning severs walls/caps that must meet), and is documented in three
   places (TESTING.md 6c deviation + §6e stop rules, plan §E, code TODO Panel3DSnapSolver.cs:403-412).
3. LevelFrame determinism is engineered, not accidental: seed sort Area↓ → Elevation↑ → Centroid XYZ →
   index; frames output-sorted; member lists sorted; no unordered collections; shuffle-permutation unit
   test locks it.
4. 6d judgment call: stacked-slab handling is detection + provenance verification, not a second collapse
   mechanism — a new collapse would be redundant ≤0.3 m (native sew / SnapOpposedPartitions already merge
   there) and unsafe beyond (cavities/shafts). This protected the raw goldens.
5. Split-level landing: 0.15 m band calibrated between real landings (0.18–0.25 m) and import noise;
   preserved + legacy-merges-it contrast lock test.
6. Every ambiguity/rejection is diagnosed (AmbiguousLevelFrame, RejectedCollapse, DuplicateFace,
   represented/missing provenance events).
7. All hard constraints honored: no ABI change, no Grasshopper change, no fuzzy widening, no
   SourceMap weakening, managed re-baseline documented with exact before/after numbers and rationale.

## D. What is risky or wrong

1. **Phase 6's headline is partially delivered by design.** The pipeline still conditions in ONE global
   frame: `Modify.Solve.cs:384-393` (`ResolveUp` = average floor normal) → whole-model rotation at
   Panel3DSnapSolver.cs:419-430; every pipeline `IsVertical` call site is the single-arg world-Z overload
   (Panel3DSnapSolver.cs:1192, 1321, 1425, 1570, 1574, 1887, 1957; SnapStage.cs:137), evaluated inside the
   rotated frame. Frame-aware classification is a parallel layer consumed only by NormalizeCaps 6c and the
   6d detector. Mixed-orientation levels / differently-tilted storeys remain limited by the single
   conditioning frame. Documented honestly, but the "removes the single-global-Up limitation" objective is
   roughly the cap-grouping half, not the conditioning half.
2. **Managed multi-level closure debt.** `two-level-tilted` managed final truth is 29 cells / 29 naked
   (pre-6c: 13/14). Cells nearly match raw (43); naked doubled — the intended cost of preserving frames
   while extend/fill stays single-frame. Models that fail raw adoption AND are multi-level get more honest
   but less closed managed output until the deferred follow-up.
3. **Managed golden numbers are prose-pinned only.** `Solve3D_ManagedPath_ClosureSignatureIsRecorded`
   asserts `cells >= 1`; the 29/29 and 22/12 baselines live only in TESTING.md. The §6e stop rule is not
   machine-enforced — Phase 7/8 work could silently drift managed behaviour without failing a test.
4. **Frame-aware NormalizeCaps is silent and frames are discarded.** The 6c overload emits no diagnostics
   (the legacy overload does); frames are computed in SnapStage.Clean, re-computed in the detector, exposed
   nowhere. Debugging exactly the models Phase 6 targets (multi-level managed) lacks the frame report.
5. **Production provenance for the core resolve is geometric attribution on BOTH paths** (verified:
   `SewBeforeBuild = true` at Panel3DSnapSolver.cs:2096 (TryRawResolve), ResolveStage.cs:95,
   AutoTune3DSolver.cs:583, HealStage.cs:318 — the §7.1 ABI v4 scope cut). Exact history is captured only
   on the FinalizeAndValidate consolidation rebuild (direct build, Panel3DSnapSolver.cs:662-668). Not a
   Phase 6 change, but a standing limitation Phase 7's spaces handoff inherits: split/merge Guid policy
   fidelity on complex models rests on the geometric heuristic.
6. Minor: `capsByFrame.Values` insertion-order iteration in the 6c overload (safe today, implicit);
   ConditionStage still carries blind `FillMargin` 0.5 m and `RoofOvershoot` 0.5 m unconditionally (the
   plan's "Fill v2 measured-first" was never carried into the P5 review's 5a–5f sub-phases).

## E. LevelFrame architecture review

Sound. Minimal (pure managed, native-free, ~715 lines incl. 6b additions), deterministic (engineered +
tested under permutation), testable (19 unit tests), correctly integrated where routed (NormalizeCaps,
detector), and not fixture-overfitted in structure — constants are named and configurable
(DEFAULT_NormalConeTolerance 5°, DEFAULT_ElevationBand 0.15 m, DEFAULT_VerticalAngleTolerance 20°),
though their VALUES are calibrated against the current 5 fixtures + synthetics (see N.1). Clustering is
seed-datum-anchored (non-transitive) — correct anti-drift design; elevation is measured along the seed
normal, so frames are true (normal cone, offset band) plane clusters, not world-Z bands. Safety: tilted
levels (25° unit cases; whole-level-tilted end-to-end), stacked levels (two-frame tests + integration),
split-level landings (own frame at 0.18–0.25 m), shafts (detector rejects, SewV2 shaft tests green),
multi-storey walls (list-valued frame membership; storey-height wall spans floor+ceiling frames). Atria
resolve as ordinary tall cells — consistent with the plan's out-of-scope note.

## F. Frame-aware classification review

Math correct (|n·frameUp| vs sin(20°) in-frame). The legacy world-Z single-arg `IsVertical` is retained
deliberately and all pipeline call sites still use it (see D.1) — inside the global-Up-rotated frame, so
uniformly tilted models of ANY tilt work end-to-end (that capability predates Phase 6 and is proven by
whole-level-tilted at ~34°/~56°). What Phase 6b adds is the frame-parametric layer that removes the 20°
ceiling wherever it is routed; flat-model parity is locked
(`IsVertical_WorldZOverload_ByteIdenticalToNoArg`, flat-frame classification tests, byte-identical
goldens). `ClassifyFace` falls back to world-Z only when no frames exist. Robust enough for Phase 7,
which consumes cells and the final face set, not per-face roles.

## G. Per-frame normalization / extend / fill review

- NormalizeCaps per frame: delivered (Panel3DSnapSolver.cs:2020-2077, consumed by SnapStage.Clean).
  Datum = largest-area cap per frame — an area-dominance proxy for the plan's Weight-backer law;
  acceptable now, revisit only if explicit Weight semantics reach caps. Legacy 0.3 m world-band overload
  retained as fallback when no frames form. Geometry-only mutation; SourceMap/provenance untouched.
- Extend/fill: still single-frame (global Up), measured-first for extend-to-caps (cap Z at wall
  centroid), blind FillMargin 0.5 m / RoofOvershoot 0.5 m / overshoots 0.05 m unconditional.
- AutoTune3D interaction: escalation re-solves run the managed pipeline, which re-clusters frames inside
  SnapStage.Clean — frame handling is consistent across rounds without AutoTune knowing about frames.
- GapFill v2 interaction: frame-agnostic (operates on final naked wires) — correct.
- Final-truth ordering intact: FinalizeAndValidate (Panel3DSnapSolver.cs:614-746) is the only outward
  producer of naked count/wires/Signature; the only thing after it is the observational detector.

## H. Stacked slab / inter-storey review

Correct and conservative. Detection gates: both caps, plan-bbox prefilter, anti-parallel ≤5°
(winding-independent storey-boundary discriminator), separation ≤0.3 m, overlap ≥0.8, both frames
unambiguous; co-parallel double-skin/split-level, wide cavity (>0.3 m), partial-step landing, ambiguous
frame all rejected WITH diagnostics. Confirmed purely observational — no geometry, no SourceMap mutation.
Duplicate skins are geometrically unified by the pre-existing proven mechanisms (native sew on raw;
SnapOpposedPartitions ≤0.3 m on managed); both analytical sources stay represented (VerifyRepresented
reads `SourceMap.FacesOf` for both; Warning if one missing) and reconstruction keeps both Guids (dominant
+ MergedSourceGuids). Genuine cavities/shafts/thin voids preserved (0.4 m cavity integration test; SewV2
shaft protection). Runs on both raw and managed paths.

## I. SourceMap and provenance review

- Phase 6 operations are non-mutating on the map (verified for clustering, 6c overload, detector).
- Invariants hold: no orphan outputs (fabricated marker or geometric backfill; `NearestSourceIndex`
  demoted to per-face last resort in PanelReconstruction); patches recorded `RecordFabricated(GapFill)`
  (Panel3DSnapSolver.cs:644-647); retained faces recorded against the EXACT dropped sources
  (RecordRetainedProvenance, line 649) so retained walls keep their Guid; the consolidation rebuild
  composes history onto the existing map and backfills ONLY unmapped faces, never overwriting (owner
  caution #2, honored per code comment 608-611 and implementation).
- Phase 4 reconstruction intact: 1:1 keeps Guid/params/construction; split = fresh Guids + SourceGuid
  stamp; merge = dominant Guid + MergedSourceGuids; apertures re-hosted via `Create.Panel`, orphans
  surfaced, never silent; GapFill faces → air panels stamped Provenance=GapFill.
- Standing limitation (pre-dates Phase 6): core-resolve provenance is geometric on both default paths
  (§7.1 scope cut; see D.5). Exact history covers only the consolidation-rebuild hop.

## J. Phase 5 regression review

No regression. The Phase 6 diffs touched no Phase 5 mechanism: GapFill.FromNakedWires, FinalizeAndValidate,
RetainDroppedV2, SewV2 (cap + fusion veto), AutoTune3D (measured-first, both acceptance modes),
ClosureSignature3D — all files untouched by 6a–6e except Panel3DSnapSolver (+84 NormalizeCaps overload,
+37 detector wiring) and SnapStage (+21 consumption). Raw goldens byte-identical 5/5 through every
sub-phase; Benchmark1500 unchanged (~6.4 s, 250/250 cells, 0 naked, 0 rounds); diagnostics honesty
maintained — with the one new gap that the 6c overload itself is silent (D.4).

## K. Test coverage gaps

Existing coverage matches TESTING.md's §6e audit table (verified against the code). Gaps, in priority order:

1. **Managed golden signatures unpinned** — test asserts `cells >= 1` only. Pin the exact 6c/6d numbers.
2. **No end-to-end managed-path split-level integration test** — landing preservation is proven at the
   NormalizeCaps unit level and detector-rejection level, but no test runs a synthetic split-level model
   through full `Solve3D` (ForceManagedPipeline) and asserts the landing survives into the final cell
   complex.
3. **Real gappy multi-storey fixture absent** — the plan's own "as they become available" item; the
   synthetic perturbed-towers route was invalidated in 5f (RetainDropped absorbs perturbations). Blocked
   on real models; tie to the per-frame extend/fill resume, not to Phase 7.
4. Integration-level input-permutation determinism (unit shuffle + same-input-twice integration exist) —
   low priority.

## L. Readiness for Phase 7

**Ready, with pre-flight items O1–O2.** Per input:
- Cell count/volumes: reliable and deterministic (Signature.CellVolumes; DeterminismIntegrationTests).
- Cell CENTRES: not retained on the solver today (volumes survive; centres discarded after the build) —
  Phase 7 must surface them. Managed-only work; the ABI already exports `sam_occt_result_cell_center`.
- Interior/exterior inputs: shells are decoded per cell; envelope rule (face adjacent to exactly one
  cell) derivable managed-side; `Query.IsPointInside` available as verifier.
- Source panel mapping: stable, never-orphaned (geometric attribution caveat D.5 noted).
- Air/gap-fill semantics: stamped and separable; codification is Phase 7's own scope.
- Sliver diagnostics: SliverCellCount + MinCellVolume + SliverCell taxonomy present.
- Stable final face set: FinalizeAndValidate single-truth, deterministic.
Prerequisite design note for Phase 7: `Create.Spaces` must gate on closure (Signature.naked == 0, or
per-cell validity) so degraded managed multi-level solves (29-naked two-level-tilted) yield diagnostics,
not bogus spaces.

## M. Readiness for Phase 8

Most §J staged outputs already exist on `Panel3DSnapSolver`: CleanFace3Ds, BucketMergedFace3Ds,
ResolvedFace3Ds, HoleFillFace3Ds, NakedWires + NakedEdgePoint3Ds, OpenWallEndPoint3Ds/OpenWallFace3Ds,
SourceMap, ResolveHistorySourceMap, Diagnostics, Signature, RawAttemptSignature, StackedSlabInterfaces.
Missing/weak for Phase 8: conditioned-faces staged output (today only via a StopAfterExtend re-run),
cells object with centres (L above), consolidated adopted-level/rounds closure report (data exists across
diagnostics + AutoTune result), retained-dropped as a distinct list (derivable via provenance —
acceptable), and **LevelFrames are not exposed at all**. Recommendation: yes — expose frame info (count,
per-frame elevation/tilt/cap+wall membership) in Phase 8 as a diagnostics-tree branch and optional plane
outputs; prerequisite is cleanup O3 (compute frames once, expose on the solver).

## N. Risks and mitigations

1. **Frame mis-clustering on real noisy models** (0.15 m / 5° calibrated on 5 fixtures + synthetics) →
   AmbiguousLevelFrame diagnostics exist; constants configurable; add real multi-storey fixtures with
   golden signatures as they arrive; O2 surfaces the frame count per solve.
2. **Over-splitting (the inverse of over-normalizing)** — the 0.15 m band creates more frames than the
   0.3 m band merged, raising managed naked counts until per-frame extend/fill lands → AutoTune3D +
   GapFill absorb; O1's pinned managed goldens make any drift loud.
3. **Losing source mapping** → no evidence of loss; guards: SourceMapMappingIntegrationTests, the
   never-orphan invariant, owner caution #2 in the rebuild. Keep both in force for Phase 7.
4. **Merging stacked panels too aggressively** → detector is observational; actual merges bounded to
   ≤0.3 m by pre-existing gates; 0.4 m cavity + shaft tests green.
5. **Breaking apertures** → Phase 6 made no reconstruction-visible geometry change;
   AperturePreservationIntegrationTests green. Watch again when extend/fill work resumes.
6. **Misleading diagnostics** → 6c overload silent (fix O2); detector's `DuplicateFace` Info on an
   ACCEPTED interface could read as "action taken" — review message wording during Phase 8 exposure.
7. **Performance regression** → clustering is O(n²) on caps only; detector runs once per adopted path
   with bbox prefilter; Benchmark1500 unchanged; Phase 9 guard exists.
8. **Brittle fixture-specific logic** → main exposure is calibrated constants; they are named,
   configurable, and documented with rationale; re-measure on each new fixture (existing practice).

## O. Required cleanup tasks

Must fix before Phase 7 (both small, one session total, bundled as "7-pre"):
- **O1.** Pin the managed golden-master signatures in `GoldenMasterIntegrationTests` to the documented
  6c/6d baseline (flat 22/0, tilted-two 2/0, whole-tilted 22/0, two-level-tilted 29/29, towers 22/12;
  volumes per the TESTING.md table within ClosureSignature3D tolerance). Machine-enforces the §6e stop rule.
- **O2.** Diagnostics for frame-aware NormalizeCaps: frame count, caps-per-frame, legacy-fallback event
  (Info severity), on `Panel3DSnapSolver.Diagnostics` — parity with the legacy overload.

Should fix before Phase 8:
- **O3.** Compute LevelFrames once per Execute; expose `LevelFrames` on `Panel3DSnapSolver`; reuse in
  NormalizeCaps + StackedSlabInterfaceDetector (today clustered independently in each).
- **O4.** Make the 6c overload's `capsByFrame` iteration explicitly ordered (OrderBy frame index).
- **O5.** Managed-path split-level end-to-end integration test (synthetic; ForceManagedPipeline; assert
  the landing survives to the final cell complex).

Can defer to Phase 9:
- **O6.** Gate FillMargin/RoofOvershoot measured-first with diagnostic-tagged blind fallback (touches
  conditioning — goldens at risk; do under Phase 9 discipline).
- **O7.** Slim the 2304-line Panel3DSnapSolver façade (extract FinalizeAndValidate + legacy statics;
  behaviour-preserving).

Per-frame extend/fill remains deferred on its own track — resume only per the TESTING.md §6e stop rules
(dominant frame; split only across proven-separate storeys) and preferably once real gappy multi-storey
fixtures exist. It is NOT a Phase 7 prerequisite.

## P. Final next-step implementation prompt (Phase 7, with 7-pre)

Model: **Sonnet 5** (plan §E assignment; additive classification over a proven substrate). Escalate to
Opus 4.8 only if the adjacency-equivalence acceptance proves geometrically subtle.

---

Work in C:\Users\michal.dengusiak\Documents\GitHub\SAM-BIM\SAM_OCCT on branch `fix/solver-raw-first`
(PR #48). Read `docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md` §E Phase 7 (your scope) and §F–§N as
reference; `TESTING.md` ("Per-level frames" and golden-master sections); and this document. Do not touch
other repos except read-only. Every new `.cs` gets the SPDX header from COPYRIGHT_HEADER.txt. xUnit
`Method_State_Expected`, Arrange/Act/Assert. Build `SAM_OCCT.sln`; unit suite green; run the integration
suite locally against the committed `build/` DLLs and paste the pass/skip summary into the PR.

**Hard scope boundaries:** no native ABI changes (cell volume/centre/count are already exported); no
Grasshopper changes (Phase 8); no OCCT fuzzy/sew tolerance widening; no SourceMap/provenance weakening
(never overwrite a mapped entry); no geometry fabrication — Phase 7 is classification + handoff over the
existing final face set; do NOT touch ConditionStage or the deferred per-frame extend/fill (stop rules in
TESTING.md §6e). Raw golden signatures must stay byte-identical; managed signatures must equal the values
pinned in 7-pre; any delta is a stop-and-report, never a silent re-baseline.

**Implementation order** (one commit per sub-step; both suites green + goldens re-run before each):

1. **7a — cell surface:** retain per-cell volume + centre on `Panel3DSnapSolver` from whichever build
   produced the final faces (TryRawResolve result ~2083+, ResolveStage result, or the FinalizeAndValidate
   consolidation rebuild ~614-746) as an additive `Cells` property (e.g. `SolverCell { Volume, Center }`).
2. **7b — classification:** `Query.CellClassification` — interior vs exterior; envelope = faces adjacent
   to exactly one cell (derive from the per-cell shell decode with geometric shared-face matching at SAM
   tolerance), `Query.IsPointInside` as verifier/tie-breaker; sliver cells (< MinCellVolume) classified
   out and diagnosed (`SliverCell`), never spaced.
3. **7c — spaces handoff:** `Create.Spaces(...)` — one `Space` per interior cell (location = cell
   centre), panels linked via the EXISTING `SAM.Analytical.OCCT.Create.AdjacencyCluster` (reuse the Tower
   path; do not reimplement adjacency). Codify the air policy: input air panels bypass solving unchanged
   and re-join the cluster; GapFill patches join as `PanelType.Air` with Provenance=GapFill. **Closure
   gate:** spaces are produced only when the final `Signature` has naked == 0 (or per-cell closure is
   proven); a degraded solve returns diagnostics and no spaces.
4. **7d — docs:** TESTING.md Phase 7 section (coverage, how to run) + plan §E Phase 7 completion note.

**Tests.** Unit: classification truth table (envelope/interior/sliver on stubbed cells); air-policy;
closure-gate truth table. Integration (native-gated): flat fixture — `Create.Spaces` yields exactly 22
spaces whose adjacency equals `SAMOCCTCreateAdjacencyCluster` on the same solved panels, exterior cell(s)
excluded; two-level-tilted RAW (43 cells) — space count == interior cell count and each space centre lies
inside its cell (`IsPointInside`); two-level-tilted MANAGED (29 naked) — the gate refuses spaces with
diagnostics; sliver-cell case reported-not-spaced; air passthrough round-trip.

**Acceptance:** flat-fixture spaces equivalence exactly as above; all raw goldens byte-identical; managed
goldens equal the pinned values; Benchmark1500 inside its ceiling; no new native symbols; no GH edits;
integration summary pasted into PR #48.

**Stop conditions (stop and report):** any raw golden change; any pinned managed value change; interior/
exterior classification turning out to require new native queries beyond IsPointInside + cell metadata;
adjacency equivalence unreachable without modifying AdjacencyCluster itself; Benchmark1500 approaching
90 s.

**Commit strategy:** one commit per sub-step (7a, 7b, 7c, 7d) in the existing
`feat(solver)/test(solver)/docs(...)` style, footer "Generated by Michal Dengusiak & Claude Code",
goldens re-run before each commit.

---

## Supporting evidence index (for the record)

- Global-Up still load-bearing: Modify/Solve.cs:98,280,357 + ResolveUp:384-393; rotation
  Panel3DSnapSolver.cs:419-430; world-Z IsVertical call sites listed in D.1.
- Sew-before-build (no history) on all core builds: Panel3DSnapSolver.cs:626 (FinalizeAndValidate
  validate options), 2096 (TryRawResolve), ResolveStage.cs:95, AutoTune3DSolver.cs:583, HealStage.cs:318;
  direct-build history only at Panel3DSnapSolver.cs:662-668.
- FinalizeAndValidate contract: Panel3DSnapSolver.cs:601-746 (single outward Validate at 728; provenance
  compose + backfill-only-unmapped per owner caution #2).
- LevelFrame: Cluster 276-390; AssignCapToFrame 403-476; AssignWallToFrames 487-573; ClassifyFace 586-631.
- Frame-aware NormalizeCaps: Panel3DSnapSolver.cs:2020-2077; deferral TODO 403-412.
- StackedSlabInterfaceDetector: gates 102-189; VerifyRepresented 229-258; deterministic sort 215-219.
- Solver public surface (Phase 8 baseline): Panel3DSnapSolver.cs:162-303.
