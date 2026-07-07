# PR #48 — CellComplex-first Solver: Implementation Handover

`SAM-BIM/SAM_OCCT` · branch `fix/solver-raw-first` (PR #48, base `sow/2026-Q3`)
Status: approved 2026-07-06 · Owner: Michal Dengusiak

File note: the phases P1–P5 in this document are the *CellComplex-first reshape phases* — they
are distinct from (and follow) phases 0–9 of `docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md`.

Architecture review accepted (sections A–B below record the decisions). This document is the
handover: phase plan, model plan, and paste-ready prompts for implementation/review agents.

**Core rule (stated once, applies everywhere):**
`Panel soup → reliable OCCT CellComplex → adjacency-ready SAM panels → correct SAM adjacency cluster`
OCCT CellComplex is the topology source of truth. Watertightness is necessary but not
sufficient. **The main success metric is SAM adjacency parity against OCCT CellComplex
adjacency.**

---

## A. Accepted decisions (summary — do not re-litigate)

- **Design: B + D, E as a thin overload, sequenced E-first.** Solve3D adopts and exposes the
  complex it validated (`ResolvedCellComplex` DTO on `Solve3DReport`); `CreateAdjacencyCluster`
  consumes it directly when supplied (promoting the existing private `DirectAdjacencyCluster`),
  otherwise rebuilds with **solver-matched options** (`AvoidInternalShapes=false,
  SewBeforeBuild=true, SewingTolerance=0.01`) and parity-checks. GH exposes cell/adjacency
  diagnostics pre-cluster (D). Panel derivation from unique cell faces (B proper) is last (P5),
  gated on ABI v5 (sew-path history, codex-#8 ordinal fix, populate `OcctCell.SourceFaceIndexes`).
- **Diagnosed seam:** solver decodes the complex then flattens `shells.SelectMany(Face3Ds)`
  (shared separators duplicated per cell) and disposes; `FaceAdjacencies`/`TopologyKey` never
  read by the solver; workflow A then re-builds blind — production GH with
  `AvoidInternalShapes=true, SewBeforeBuild=false` (test uses solver-matched → test≠production).
  Known deferred codex findings #3 (rebuild gate vs pre-append cells) and #7 (under-split
  adoption) are the watertight-but-wrong holes; `MergeCoplanarPanels(AdjacencyCluster)` has zero
  E2E coverage.
- **Raw-first stays** as the conditioning ladder; `Clean3D`/`Extend3D` stay as optional
  pre-conditioners and managed-fallback internals.
- Full analysis, 12 CellComplex Q&A, risks: see review record (Appendix A/B at end).

## B. Phase merge strategy (confirmed; amended 2026-07-07 for the E-track)

- Continue **PR #48**.
- Implement **P1, then E1, then P2 inside PR #48**. E1 is the robust-Extend3D primitive rebuild
  (plane-ops, all goldens frozen) from `docs/EXTEND3D_ROBUST_HANDOVER.md` — it supersedes PR #49.
- **Merge PR #48 only after P2 passes** its acceptance gate and the merge checklist (§14,
  now including the E1 items 11–13).
- **P3, P4, P5 are follow-up PRs** (new branches off `sow/2026-Q3` after the merge), joined by
  **E2** (plane-target extension, the managed-pin re-baseline PR) and **E3** (extend
  observability). Combined order: **merge #48 → E2 → {P3 ∥ E3-after-E2} → P4 → P5.**
  **P4 is blocked until E2 merges** — gate hardening pushes more input onto the managed
  pipeline, so the managed extend quality must improve first.
- **P5 is the separate re-baseline PR** — never enters #48, ships with its own re-baseline table.

---

# AI implementation prompts and model plan

## 1. Shared context for all agents

Paste this block at the top of every phase prompt below.

```
SHARED CONTEXT — SAM_OCCT CellComplex-first solver work
Repo: SAM-BIM/SAM_OCCT (Windows; native OCCT via P/Invoke; committed DLL at build/SAM.Occt.Native.dll).
Core rule: Panel soup → reliable OCCT CellComplex → adjacency-ready SAM panels → correct SAM
adjacency cluster. The OCCT CellComplex is the topology source of truth. Watertight (0 naked
edges) is necessary but NOT sufficient. Success metric: SAM adjacency parity vs OCCT
CellComplex adjacency (per shared cell face: exactly one SAM panel related to both spaces;
per envelope face: exactly one space).

Solver-matched build options (the only options the cluster rebuild may use):
AvoidInternalShapes=false, SewBeforeBuild=true, SewingTolerance=0.01 (+ caller Tolerance/Fuzzy).

Key files:
- Solver: SAM_OCCT/SAM.Geometry.OCCT.Solver/Classes/Panel3DSnapSolver.cs (TryRawResolve ~:2138,
  FinalizeAndValidate ~:649), ResolveStage.cs, HealStage.cs, SourceMap.cs, HistorySourceMap.cs
- Analytical: SAM_OCCT/SAM.Analytical.OCCT.Solver/Modify/Solve.cs (Solve3D/Clean3D/Extend3D),
  Classes/Solve3DReport.cs, Classes/PanelReconstruction.cs, Create/Spaces.cs
- Cluster: SAM_OCCT/SAM.Analytical.OCCT/Create/AdjacencyCluster.cs (DirectAdjacencyCluster :356 —
  1 Space/cell, 1 Panel per unique OcctCellFace.TopologyKey, relations from cell-face ownership),
  SAM_OCCT/SAM.Analytical.OCCT/Modify/MergeCoplanarPanels.cs (adjacency-safe merge)
- CellComplex decode: SAM_OCCT/SAM.Geometry.OCCT/Classes/OcctCellComplexResult.cs (BuildFaceAdjacencies),
  OcctCell.cs, OcctCellFace.cs, Native/OcctCellComplexBuilder.cs
- Native: native/SAM.Occt.Native/src/CellComplexBuilder.cpp (get_face_key :172 — TopologyKey is a
  per-result quantized-vertex signature, valid ONLY within one decode), src/History.cpp
- GH: Grasshopper/SAM.Analytical.Grasshopper.OCCT/Component/SAMOCCTSolve3D.cs,
  SAMOCCTCreateAdjacencyCluster.cs, SAMOCCTCreateAdjacencyClusterByShells.cs
- Tests: Testing/SAM.OCCT.UnitTests (pure managed), Testing/SAM.OCCT.IntegrationTests
  (native-gated via Skip.IfNot(NativeProbe.Available)); fixtures under
  Testing/SAM.OCCT.IntegrationTests/Fixtures (9 .sam files); GoldenMasterIntegrationTests pins
  raw goldens (whole-level-flat 22 cells/0 naked; tilted-two-spaces 2/0; whole-level-tilted 22/0;
  two-level-tilted ~43/0; whole-level-towers ≥31/0) and exact managed baselines
  (two-level-tilted 29c/29n; towers 22c/12n).

Conventions (mandatory): SPDX header `// SPDX-License-Identifier: LGPL-3.0-or-later` + copyright
on every new .cs; xUnit, Method_State_Expected, Arrange/Act/Assert; two-tier test rule per
TESTING.md (unit = no native, integration = native-gated); update TESTING.md with a phase
section; commit messages and PR descriptions signed "Generated by Michal Dengusiak & Claude Code".
Golden masters must stay byte-identical in every phase except P5 (which re-baselines explicitly).
Definitions:
- ResolvedCellComplex (DTO, introduced P2): cells (index/volume/centre/role), unique faces
  (Face3D + owner cell indices + per-decode key + flat ordinals), adjacency pairs, naked wires,
  SolveId (GUID). Pure managed, serializable, no native lifetime.
- SolveId roster gate (P3): cluster consumes a supplied complex ONLY if every incoming panel
  carries the matching SolveId stamp and the panel roster equals the report's; else rebuild
  with solver-matched options + emit drift diagnostic.
- Parity validator (P1): compares AdjacencyCluster space↔panel relations against
  OcctCellComplexResult.FaceAdjacencies; also counts TopologyKey==0 faces (silently skipped
  today) and panels with no relation.
```

## 2. Phase table P1–P5

| Phase | Scope (one line) | Branch / PR | Implementation | Review | Why this model/effort | Merge condition |
|---|---|---|---|---|---|---|
| P1 | Options-parity bugfix + parity diagnostic + workflow A/B E2E harness (observational otherwise) | `fix/solver-raw-first` / PR #48 | **Sonnet 5, High** | **Opus 4.8, xhigh** | Impl is contained plumbing+tests; review must catch parity-math and test-honesty errors | Review approves; goldens byte-identical; harness table committed |
| P2 | Retain `ResolvedCellComplex` at adoption; DTO on report; cluster overload; single-build `Create.Spaces` | `fix/solver-raw-first` / PR #48 | **Opus 4.8, xhigh** | **Fable 5, Max** (merge decision) | Core seam change with ordinal-bridge subtlety → strong impl; merge decision locks the abstraction → top model, max effort | P2 gate green → run §14 checklist → **merge PR #48** |
| P3 | GH handoff: DTO goo, SolveId stamps, roster gate, cell/adjacency outputs (D) | new `feat/cellcomplex-gh-handoff` / new PR | **Sonnet 5, High** | **Opus 4.8, xhigh** | GH plumbing is mechanical; review focuses on serialization/roster-gate edge cases | Review approves; canvas back-compat proven |
| P4 | Gate hardening: codex #3 (appended-set cells) + #7 (under-split), fail-before/pass-after fixtures | new `feat/solver-gate-hardening` / new PR | **Opus 4.8, Max** | **Fable 5, Max** | Gate logic risk = false positives push good input onto weaker managed path; adversarial review required | Review approves; goldens unchanged; new fixtures prove both gates |
| P5 | B proper: ABI v5 + panels from unique cell faces + retire solver geometric merge; re-baseline | new `feat/solver-cellface-panels` / new PR (separate re-baseline PR) | Design: **Fable 5, Max** → Impl: **Opus 4.8, Max** | **Fable 5 + Opus 4.8, both Max** (dual, independent) | Contract + native ABI redesign is the highest-stakes step; dual review: architecture (Fable) + line-level incl. C++ (Opus) | Both reviewers approve; full re-baseline table justified fixture-by-fixture |

Model-strategy note: adopted as given. Where alternatives were offered I pinned: P2 impl =
xhigh (Max reserved for its merge review), P2 merge decision = Fable 5 Max, P4 impl = Max.
No strong disagreement anywhere.

E-track note (2026-07-07): the robust-Extend3D phases E1–E3 of
`docs/EXTEND3D_ROBUST_HANDOVER.md` interleave with this table — E1 sits between P1 and P2
inside PR #48; E2 lands right after the #48 merge and **gates P4**; E3 follows E2 (parallel to
P3). See §B for the combined order.

## 3. P1 implementation prompt (Sonnet 5, High → PR #48)

```
[paste Shared context]
GOAL: Make workflow A measurable against the OCCT CellComplex, and fix the production options
mismatch. No solver-geometry or gate changes.
SCOPE: GH cluster components' options; parity diagnostic inside DirectAdjacencyCluster; new E2E
integration tests + per-fixture diagnostic table.
TASKS:
1. Bugfix (label as such): SAMOCCTCreateAdjacencyCluster.cs (~:124) and
   SAMOCCTCreateAdjacencyClusterByShells.cs — pass solver-matched options
   (AvoidInternalShapes=false, SewBeforeBuild=true, SewingTolerance=0.01) instead of defaults.
2. In Create.AdjacencyCluster's DirectAdjacencyCluster (AdjacencyCluster.cs:356-436): add parity
   counters + diagnostics (SAM_OCCT_ANALYTICAL_PARITY): relations added vs expected
   (2×FaceAdjacencies + envelope faces), TopologyKey==0 face count, panels with zero relations.
   Counting only — no behaviour change.
3. New Testing/SAM.OCCT.IntegrationTests/WorkflowParityIntegrationTests.cs: for ALL 9 fixtures,
   run (a) workflow A with old default options, (b) A with solver-matched options, (c) workflow B
   (Clean3D → Extend3D → CreateAdjacencyCluster), each through MergeCoplanarPanels(AdjacencyCluster).
   Assert: spaces == solver ResolvedCellCount, parity clean, relation multiset invariant across
   merge. Where broken today, keep the run + mark expected-fail (assert the CURRENT wrong value
   with a tracking comment) — never skip silently.
4. Emit a per-fixture markdown table (test output + TESTING.md new section): input panels; OCCT
   cells/faces/shared-adjacencies/naked; SAM panels internal/external; orphan panels;
   dropped/retained; parity result; merge result; verdict.
DO NOT CHANGE: Panel3DSnapSolver, ResolveStage, HealStage, any adoption gate, PanelReconstruction,
Solve3DReport shape, golden-master tests.
TESTS: new integration tests native-gated; all existing tests stay green.
ACCEPTANCE GATE: goldens byte-identical; full suite green; table present in TESTING.md; the only
behaviour change is the documented GH options bugfix.
```

## 4. P1 review prompt (Opus 4.8, xhigh)

```
[paste Shared context]
Review the P1 diff on PR #48 (options-parity bugfix + parity diagnostic + WorkflowParityIntegrationTests).
CHECK, in order:
1. Options flip: every GH cluster component call site now passes solver-matched options; no other
   call site silently changed; the flip is labelled a bugfix in commit/PR text.
2. Parity math: expected relation count = 2×|FaceAdjacencies| + envelope faces (faces owned by
   exactly one cell); TopologyKey==0 faces counted, not silently skipped; no key comparison
   across two different decodes (keys are per-result only).
3. Test honesty: expected-fail cases assert current wrong values with tracking comments (not
   Skip, not loosened tolerances); all 9 fixtures actually load; MergeCoplanarPanels really runs;
   relation-multiset invariance is asserted on relations, not counts only.
4. Zero solver-behaviour drift: grep the diff for changes outside GH components, AdjacencyCluster.cs
   diagnostics, tests, TESTING.md. Confirm goldens byte-identical (run integration suite).
5. Conventions: SPDX headers, Method_State_Expected, native gating, TESTING.md section.
OUTPUT: findings ranked by severity with file:line, each with a concrete failure scenario;
explicit verdict: safe to keep in PR #48 yes/no.
```

## 5. P2 implementation prompt (Opus 4.8, xhigh → PR #48)

```
[paste Shared context]
GOAL: The complex the solver validated becomes a first-class product; Create.Spaces consumes it
(single build). Purely additive to adopted geometry — goldens stay byte-identical.
SCOPE: DTO + capture in solver; Solve3DReport; public cluster overload; Create.Spaces rewire.
TASKS:
1. New ResolvedCellComplex DTO (SAM.Geometry.OCCT.Solver or SAM.Geometry.OCCT): cells
   (index/volume/centre), unique faces (Face3D, owner cell indices, per-decode key, flat
   ordinals), adjacency pairs, naked wires, SolveId GUID. Pure managed/serializable.
2. Managed-only decode extension: emit the flat ordinal alongside each OcctCellFace so the
   ordinal↔(cell,face) bridge is explicit (both flatten sites filter invalid faces — do NOT
   derive the bridge arithmetically from per-cell counts). Unit-test the bridge against filter
   drift (a cell with an undecodable face).
3. Capture the DTO at adoption time, before Dispose(): TryRawResolve (raw path),
   FinalizeAndValidate (managed path, from the same decode that produced the adopted
   cells/signature). Expose as Solve3DReport.ResolvedCellComplex (additive ctor param ok).
4. Promote DirectAdjacencyCluster: public overload
   Create.AdjacencyCluster(IEnumerable<Panel> panels, ResolvedCellComplex complex, ...) that
   consumes the supplied complex (no rebuild) and takes panel identity (construction/type/Guid
   linkage) from the supplied panels where attributable, defaults otherwise.
5. Create.Spaces: use the report's complex instead of its CellComplexByPanels rebuild; exclude
   non-Interior cells by CELL INDEX (relation filtering), not centre-distance matching.
DO NOT CHANGE: adopted geometry/face sets, adoption gates, PanelReconstruction output, GH
components (P3), MergeCoplanarPanels.
TESTS: DTO projection unit tests (shared face appears once with two owners; ordinals map);
CreateSpacesIntegrationTests counts unchanged (22 / 43 / managed refusal); P1 parity results
unchanged or improved; goldens byte-identical.
ACCEPTANCE GATE: full suite green; report carries the complex on both paths; Create.Spaces does
exactly one native build per solve.
```

## 6. P2 review / PR #48 merge decision prompt (Fable 5, Max)

```
[paste Shared context]
You are the merge gatekeeper for PR #48 (phases 0-9 + P1 + E1 + P2). Two jobs: line-level review
of the P2 diff, then the merge decision for the whole PR. Note the PR also contains E1 (robust
Extend3D primitives, docs/EXTEND3D_ROBUST_HANDOVER.md) — walk its checklist items 11-13 in §14
and confirm its golden-freeze evidence as part of the merge decision.
P2 REVIEW:
1. Observationality: prove adopted geometry unchanged — goldens byte-identical, determinism
   tests green, no gate touched.
2. DTO soundness: ordinal↔cell-face bridge built from OcctCell.Faces with emitted ordinals (not
   arithmetic); unique-face dedup keyed per-decode only; shared face = one entry, two owners;
   DTO serializes round-trip.
3. Create.Spaces equivalence: single build, cell-index exclusion; 22/43/refusal counts hold;
   no second CellComplexByPanels call left on that path.
4. Overload contract: panels-identity enrichment cannot mis-attribute (verify fallback to
   defaults is explicit, diagnosed, never silent).
MERGE DECISION (only if review passes): walk §14 Final PR #48 merge checklist item by item with
evidence (test run output, grep results). Verdict: MERGE / DO NOT MERGE + blocking items.
Update the PR description: state the new success metric (SAM-vs-OCCT adjacency parity), the P1
options bugfix, the P2 complex product, and that P3-P5 follow. Sign
"Generated by Michal Dengusiak & Claude Code".
```

## 7. P3 implementation prompt (Sonnet 5, High → new PR)

```
[paste Shared context]
GOAL: Expose the CellComplex in Grasshopper (D) and wire the value-based handoff (E in GH).
Branch feat/cellcomplex-gh-handoff off sow/2026-Q3 (post-#48-merge).
SCOPE: GH goo + params; SolveId stamps; roster gate; diagnostics outputs. Library semantics
unchanged except consuming the P2 overload.
TASKS:
1. Goo wrapper for ResolvedCellComplex (pattern: existing Goo* params); serializable/internalizable.
2. SAMOCCTSolve3D: stamp every output panel with SolveId (PanelProvenanceParameter); new outputs:
   CellComplex (goo), per-cell faces, adjacency pairs (as cell-index pairs + face geometry),
   naked wires, parity/closure summary text.
3. SAMOCCTCreateAdjacencyCluster: optional cellComplex_ input. Consume directly ONLY when every
   incoming panel's SolveId matches and the roster equals the report's (count + Guid set);
   otherwise rebuild with solver-matched options and emit a drift diagnostic naming why
   (missing stamp / roster mismatch / no complex supplied). Rebuild fallback is the NORMAL path
   for saved definitions — treat it as first-class, not an error.
4. Docs: README/TESTING.md component table rows.
DO NOT CHANGE: solver, adoption gates, cluster library algorithms, existing component parameter
order/names (append-only — existing canvases must load unchanged).
TESTS: integration — direct handoff produces identical cluster to the P2 library overload;
rewired-panels case (delete one panel) takes rebuild path + drift diagnostic; goo round-trip
unit test.
ACCEPTANCE GATE: suite green; goldens byte-identical; back-compat: existing component tests
untouched and green.
```

## 8. P3 review prompt (Opus 4.8, xhigh)

```
[paste Shared context]
Review the P3 diff (GH handoff). Focus on contract edges:
1. Roster gate: edited/reordered/subset/superset panel lists all fall back to rebuild + drift
   diagnostic; stale complex can never be consumed silently; SolveId collision across two solves
   on one canvas handled (roster equality, not id alone).
2. Serialization: goo internalize/bake/file round-trip preserves the DTO; no native handle
   captured anywhere in GH state.
3. Back-compat: parameter append-only, old canvases load; defaults preserve pre-P3 behaviour
   when new input unconnected.
4. The direct-consume path and rebuild path produce identical clusters on unmodified rosters
   (test actually asserts cluster equality, not counts).
OUTPUT: findings with file:line + failure scenario; verdict merge yes/no.
```

## 9. P4 implementation prompt (Opus 4.8, Max → new PR)

```
[paste Shared context]
GOAL: Close the two watertight-but-wrong gate holes (deferred codex findings #3 and #7) without
regressing the five golden fixtures. Branch feat/solver-gate-hardening off sow/2026-Q3.
PRECONDITION: branch only after E2 (feat/extend3d-plane-targets, docs/EXTEND3D_ROBUST_HANDOVER.md)
has merged — E2 re-baselines the managed pins this phase's risk math depends on.
SCOPE: adoption-gate logic + targeted fixtures only.
TASKS:
1. Codex #3: in Panel3DSnapSolver.FinalizeAndValidate (~:722), the consolidation-rebuild
   acceptance must compare rebuilt cell count against the APPENDED set's own decoded cell count
   (not pre-append resolveCellCount). One extra decode only when a rebuild is attempted.
2. Codex #7: extend EvaluateRawAdoption with a conservative under-split check: reject raw
   adoption when a dropped input face lies strictly interior to a single adopted cell whose
   volume exceeds a threshold multiple of the fixture median (tune on fixtures), OR raw cell
   count falls short of a cheap managed-snap expected-cell lower bound. Keep it pure/unit-testable
   (extend the existing static gate signature). Every rejection emits a diagnostic with the
   measured values.
3. New fixtures + integration tests, fail-before/pass-after: (a) door-cut partition two-room
   model raw-adopted as one cell today → must reject raw, managed/AutoTune separates; (b)
   separator-dissolving consolidation rebuild → must keep appended set.
4. Upgrade IsRepresented callers used by these gates from centre-point to area-coverage
   (sampled grid or planar boolean ≥ threshold) IF needed to make (2) sound — keep the change
   scoped to gate measurement, not RetainDropped dedup (note follow-up if wider).
DO NOT CHANGE: golden fixtures' outcomes (all five must adopt exactly as today), DTO, cluster,
GH, PanelReconstruction, SnappedPanel extend/trim primitives and ConditionStage (E-track
territory — docs/EXTEND3D_ROBUST_HANDOVER.md).
TESTS: unit tests for the pure gate rule (all branches); the two new integration fixtures;
full suite green.
ACCEPTANCE GATE: goldens byte-identical (the findings do not fire on them — verify, don't
assume); new fixtures prove both gates; every new rejection path emits a coded diagnostic.
```

## 10. P4 review prompt (Fable 5, Max)

```
[paste Shared context]
Adversarial review of the P4 gate-hardening diff. Your job is to break the under-split gate.
(Precondition check first: P4 branches only after E2 merged — verify the diff is based on a
post-E2 sow/2026-Q3.)
1. Construct (on paper, or as a quick fixture) realistic well-modelled inputs that the new gate
   would FALSELY reject — courtyard rings, atria spanning floors, deliberate double-height
   spaces, models where the biggest room legitimately dwarfs the median. A false rejection
   pushes input onto the managed pipeline — judge that cost against the managed baselines
   CURRENT at review time (read GoldenMasterIntegrationTests.ManagedFixtures and the P1
   WorkflowParityIntegrationTests table; the E-track, docs/EXTEND3D_ROBUST_HANDOVER.md,
   re-baselined them after this doc was written) — a false positive is still a regression, not
   a safety win. Verify the gate's thresholds/diagnostics make this visible and tunable.
2. Verify #3 fix actually decodes the appended set (not a cached count) and only when a rebuild
   is attempted (perf).
3. Verify the five goldens adopt identically (run the suite; byte-identical signatures).
4. Check the new fixtures genuinely failed before the fix (git stash / revert-run evidence in PR).
OUTPUT: findings + explicit false-positive analysis; verdict merge yes/no.
```

## 11. P5 design prompt (Fable 5, Max → design doc only, no code)

```
[paste Shared context]
GOAL: Produce the design document for phase P5 (B proper): Solve3D derives panels from unique
cell faces; solver's geometric coplanar post-merge retires in favour of the cluster's
adjacency-aware merge; ABI v5 provides exact provenance. Output: docs/P5B_CELLFACE_PANELS_DESIGN.md.
MUST COVER:
1. ABI v5 (native): compose sew-path history the way merge_coplanar already does
   (sewing_history at CellComplexBuilder.cpp:1184) so TrySewThenMakeVolume captures history;
   fix codex #8 (finalize_history publishes shifted ordinals when make_face skips an input —
   publish against the caller's original face_count); populate the dormant
   OcctCell.SourceFaceIndexes. Additive ABI, probe/degrade rules per ABI v4 precedent.
2. Managed derivation: output faces from unique cell faces (one separator = one panel,
   owner-cell tags); SourceMap projection = union of sources across a shared face's flat
   ordinals; fix PanelReconstruction.isSplitPiece to count UNIQUE faces (today a shared
   separator's two ordinals falsely classify a 1:1 source as a split → fresh Guid).
3. Merge relocation: remove/flag the solver's post-resolve MergeCoplanarFace3Ds; define how
   workflow A reaches equivalent panel granularity via MergeCoplanarPanels(AdjacencyCluster);
   define behaviour for panels-only consumers who never build a cluster.
4. Aperture policy under finer faces: piece selection, expected orphaned-aperture deltas, and
   how they are pinned in tests.
5. Full expected re-baseline table: per golden fixture, which assertions move (managed volumes,
   panel counts, SourceMapMapping/Determinism/AperturePreservation tests) and why raw
   cells/volumes should NOT move (verify plan, not assumption).
6. Compatibility: transition flag (flatten path) yes/no and its removal criterion; API surface
   changes; risk register with mitigations.
Assume P1-P4 are merged. Do not write implementation code. End with a phase-gated
implementation checklist for the P5 implementation agent.
```

## 12. P5 implementation prompt (Opus 4.8, Max → new PR)

```
[paste Shared context]
GOAL: Implement docs/P5B_CELLFACE_PANELS_DESIGN.md (approved) on branch
feat/solver-cellface-panels off sow/2026-Q3. This is the ONLY phase allowed to re-baseline.
SCOPE: exactly the design doc — native ABI v5 + managed unique-face derivation + merge
relocation + reconstruction fixes.
TASKS: follow the design doc's implementation checklist in order; native first (ABI v5 +
native-side tests), then managed derivation behind the transition flag if the design kept one,
then test re-baselining.
DO NOT CHANGE: anything the design doc does not name; P1 parity harness semantics (it is the
measuring stick — if parity math itself must change, stop and escalate).
TESTS: every re-baselined assertion gets a row in the PR's re-baseline table (old value, new
value, fixture, justification); raw goldens re-verified with evidence, not assumed; parity
validator green on ALL 9 fixtures for workflow A with solver-matched options; orphaned-aperture
deltas pinned.
ACCEPTANCE GATE: full suite green; re-baseline table complete; benchmark within the 90 s
Benchmark1500 ceiling; no silent behaviour change outside the table.
```

## 13. P5 final review prompt (Fable 5 + Opus 4.8, both Max, independent)

```
[paste Shared context]
Dual independent review of the P5 PR (unique-cell-face panels + ABI v5). Do not read each
other's findings before writing your own.
REVIEWER A (Fable 5, Max) — architecture/contract: DTO and SourceMap contracts after
projection; isSplitPiece Guid semantics (no collisions, no spurious fresh Guids); merge
relocation leaves panels-only consumers a defined path; re-baseline table complete and each
row justified by a mechanism, not "expected"; parity metric untouched.
REVIEWER B (Opus 4.8, Max) — line-level + native: History.cpp/CellComplexBuilder.cpp v5 changes
(ordinal accounting under skipped faces — codex #8 scenario unit-proven; sew history
composition; SourceFaceIndexes population); ABI probe/degrade on old DLLs; managed projection
edge cases (undecodable face, TopologyKey==0, single-cell models); aperture piece selection.
BOTH: run the full suite + parity harness on all 9 fixtures; verdict merge yes/no with blocking
findings. Merge requires BOTH approvals.
```

## 14. Final PR #48 merge checklist (run at P2 review, §6 prompt)

1. All P1+P2 acceptance gates green (full unit + integration suite, native present).
2. Golden masters byte-identical vs pre-P1 baseline (raw AND managed pins).
3. WorkflowParityIntegrationTests present, running all 9 fixtures through
   MergeCoplanarPanels; per-fixture table committed to TESTING.md; expected-fail markers carry
   tracking comments (no silent skips).
4. GH options bugfix present, release-noted in PR description (behaviour change called out).
5. `Solve3DReport.ResolvedCellComplex` populated on both raw and managed paths; DTO round-trip
   test green; ordinal-bridge unit test green.
6. `Create.Spaces` performs exactly one native build; 22/43/refusal counts unchanged;
   cell exclusion by index.
7. No P5-scope material in the PR (no panel-derivation change, no native ABI change, no
   golden re-baseline).
8. TESTING.md updated (P1/P2 sections); SPDX headers on all new files; commits signed
   "Generated by Michal Dengusiak & Claude Code".
9. PR description updated to state the success metric (SAM-vs-OCCT adjacency parity) and the
   P3–P5 follow-up plan.
10. Gatekeeper (Fable 5, Max) verdict recorded: MERGE.
11. **(E1)** Profile-preservation tests green (gable/M-top/stepped/hole/near-vertical + the
    three cherry-picked PR #49 tests); ExtendBottomTo has dedicated coverage.
12. **(E1)** Five managed pins byte-identical post-E1 (fast-path census evidence, not
    assumption); raw goldens byte-identical; `SAM_OCCT_EXTEND3D_HOLE_DROPPED` diagnostic wired.
13. **(E1)** WorkflowParityIntegrationTests rows flipped by E1 carry updated pins + tracking
    comments naming the mechanism; no silent skips introduced.

---

## Appendix A — the 12 CellComplex questions (accepted answers, unchanged)

1. **Native builder can already produce the required topology?** Yes — MakerVolume with
   `AvoidInternalShapes=false` yields the zoned complex; cells/faces/keys/adjacencies decode
   today (goldens: 22/2/22/~43/≥31 cells, 0 naked).
2. **Adjacent cells share reliable native face identity?** Within one decode, yes (same TShape ⇒
   identical quantized signature). Across calls/stages, no (per-result sequential ints).
3. **`TopologyKey` reliable for shared internal faces?** Yes within one result; theoretical
   same-vertex-set collision; identity destroyed by any managed flatten/re-merge.
4. **`BuildFaceAdjacencies()` correct?** Yes given (3); skips key==0 faces (count them — they
   become silent relation gaps downstream).
5. **Shared faces lost converting cells → SAM panels?** In `Solve3D`: yes (per-cell duplication →
   geometric merge → per-ordinal panels; plus the isSplitPiece false-split Guid bug). In
   `DirectAdjacencyCluster`: no — but it discards solver provenance (default constructions).
6. **`Solve3D` ignoring useful adjacency info?** Yes — never reads it (grep-verified).
7. **Cluster consumes OCCT adjacency directly vs Solve3D returns metadata?** Both, value-based:
   DTO + SolveId roster gate; rebuild-with-matched-options is the normal fallback (saved
   definitions), direct consume the optimization.
8. **History/SourceMap reliable?** Direct-build path: yes (real BRepTools_History). Raw/sew
   paths: no history until ABI v5; coarse geometric fallback; codex #8 is a known native gap.
9. **`AvoidInternalShapes` removing needed separators?** Full separators survive; dangling
   partial partitions are removed — production cluster ran with `true` (the P1 bugfix); solver
   paths use `false`.
10. **Face keys geometric or true topology?** Geometric signature — not TShape identity.
11. **Safe enough for SAM analytical adjacency?** Within one decode as used by the direct path:
    yes. As cross-stage identity: no — hence DTO + parity validation.
12. **Stronger native face-ownership map needed?** Not for P1–P4 (ownership already decodes).
    For P5-grade provenance: yes — ABI v5 (sew history, codex #8 fix, SourceFaceIndexes).

## Appendix B — fixture baselines known today (P1 harness fills the rest)

| Fixture | Raw Solve3D | Managed (forced) | A end-to-end parity |
|---|---|---|---|
| whole-level-flat | 22 cells / 0 naked | 22 / 0 (vol 3479.90) | spaces=22 count-checked only |
| tilted-two-spaces | 2 / 0 | 2 / 0 (723.65) | unmeasured |
| whole-level-tilted | 22 / 0 | 22 / 0 (3377.83) | unmeasured |
| two-level-tilted | ~43 / 0 | **29 / 29 naked** | unmeasured (Spaces refused on managed) |
| whole-level-towers | ≥31 / 0 | **22 / 12 naked** | unmeasured |
| three-spaces / Revit-home-panels / AdjacencyCluster-home / Face3D-home | none | none | none |

`MergeCoplanarPanels(AdjacencyCluster)`: zero integration coverage today.
Benchmark1500: 250 cells / 0 naked, ~6 s (90 s ceiling) — performance not a blocker.

---

Generated by Michal Dengusiak & Claude Code
