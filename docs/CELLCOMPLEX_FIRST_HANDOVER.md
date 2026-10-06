# PR #48 — CellComplex-first Solver: Design and Merge Record

`SAM-BIM/SAM_OCCT` · branch `fix/solver-raw-first` (PR #48, base `sow/2026-Q3`)
Status: approved 2026-07-06

File note: the phases P1–P5 in this document are the *CellComplex-first reshape phases* — they
are distinct from (and follow) phases 0–9 of `docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md`.

Architecture review accepted (sections A–B below record the decisions). This document holds the
phase plan, per-phase specifications, the merge checklist and the merge-decision record.

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
  gated on ABI v5 (sew-path history, review-finding-#8 ordinal fix, populate `OcctCell.SourceFaceIndexes`).
- **Diagnosed seam:** solver decodes the complex then flattens `shells.SelectMany(Face3Ds)`
  (shared separators duplicated per cell) and disposes; `FaceAdjacencies`/`TopologyKey` never
  read by the solver; workflow A then re-builds blind — production GH with
  `AvoidInternalShapes=true, SewBeforeBuild=false` (test uses solver-matched → test≠production).
  Known deferred review findings #3 (rebuild gate vs pre-append cells) and #7 (under-split
  adoption) are the watertight-but-wrong holes; `MergeCoplanarPanels(AdjacencyCluster)` has zero
  E2E coverage.
- **Raw-first stays** as the conditioning ladder; `Clean3D`/`Extend3D` stay as optional
  pre-conditioners and managed-fallback internals.
- Full analysis, 12 CellComplex Q&A, risks: see review record (Appendix A/B at end).

## B. Phase merge strategy (confirmed; amended 2026-07-07 for the E-track)

- Continue **PR #48**.
- Implement **P1, then E1, then P2 inside PR #48**. E1 is the robust-Extend3D primitive rebuild
  (plane-ops, all goldens frozen) from `docs/EXTEND3D_ROBUST_HANDOVER.md` — it supersedes PR #49.
- **Merge PR #48 only after P2 passes** its acceptance gate and the merge checklist (§4,
  now including the E1 items 11–13).
- **P3, P4, P5 are follow-up PRs** (new branches off `sow/2026-Q3` after the merge), joined by
  **E2** (plane-target extension, the managed-pin re-baseline PR) and **E3** (extend
  observability). Combined order: **merge #48 → E2 → {P3 ∥ E3-after-E2} → P4 → P5.**
  **P4 is blocked until E2 merges** — gate hardening pushes more input onto the managed
  pipeline, so the managed extend quality must improve first.
- **P5 is the separate re-baseline PR** — never enters #48, ships with its own re-baseline table.

---

# Phase specifications and records

## 1. Technical context

```
Context — SAM_OCCT CellComplex-first solver work
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

Conventions: SPDX header `// SPDX-License-Identifier: LGPL-3.0-or-later` + copyright
on every new .cs; xUnit, Method_State_Expected, Arrange/Act/Assert; two-tier test rule per
TESTING.md (unit = no native, integration = native-gated); update TESTING.md with a phase
section.
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

| Phase | Scope (one line) | Branch / PR | Merge condition |
|---|---|---|---|
| P1 | Options-parity bugfix + parity diagnostic + workflow A/B E2E harness (observational otherwise) | `fix/solver-raw-first` / PR #48 | Review approves; goldens byte-identical; harness table committed |
| P2 | Retain `ResolvedCellComplex` at adoption; DTO on report; cluster overload; single-build `Create.Spaces` | `fix/solver-raw-first` / PR #48 | P2 gate green → run §4 checklist → **merge PR #48** |
| P3 | GH handoff: DTO goo, SolveId stamps, roster gate, cell/adjacency outputs (D) | new `feat/cellcomplex-gh-handoff` / new PR | Review approves; canvas back-compat proven |
| P4 | Gate hardening: review finding #3 (appended-set cells) + #7 (under-split), fail-before/pass-after fixtures | new `feat/solver-gate-hardening` / new PR | Review approves; goldens unchanged; new fixtures prove both gates |
| P5 | B proper: ABI v5 + panels from unique cell faces + retire solver geometric merge; re-baseline | new `feat/solver-cellface-panels` / new PR (separate re-baseline PR) | Both reviewers approve; full re-baseline table justified fixture-by-fixture |

E-track note (2026-07-07): the robust-Extend3D phases E1–E3 of
`docs/EXTEND3D_ROBUST_HANDOVER.md` interleave with this table — E1 sits between P1 and P2
inside PR #48; E2 lands right after the #48 merge and **gates P4**; E3 follows E2 (parallel to
P3). See §B for the combined order.

## 3. Phase specifications

**P1 — options parity, parity diagnostic, workflow A/B harness (in PR #48).** Goal: make workflow A
measurable against the OCCT CellComplex and fix the production options mismatch. No solver-geometry or
gate changes.
- Bugfix (labelled as such): `SAMOCCTCreateAdjacencyCluster` and `SAMOCCTCreateAdjacencyClusterByShells`
  pass the solver-matched options (`AvoidInternalShapes=false, SewBeforeBuild=true, SewingTolerance=0.01`)
  instead of defaults.
- `DirectAdjacencyCluster`: parity counters + `SAM_OCCT_ANALYTICAL_PARITY` diagnostic — relations added vs
  expected (2×`FaceAdjacencies` + envelope faces), `TopologyKey==0` face count, panels with zero
  relations. Counting only.
- `WorkflowParityIntegrationTests`: all 9 fixtures through (a) workflow A with old default options,
  (b) A with solver-matched options, (c) workflow B (Clean3D → Extend3D → CreateAdjacencyCluster), each
  through `MergeCoplanarPanels(AdjacencyCluster)`. Assert spaces == solver `ResolvedCellCount`, parity
  clean, relation multiset invariant across the merge; where broken today keep the run and pin the CURRENT
  wrong value with a tracking comment — never skip silently. Per-fixture markdown table in test output and
  TESTING.md.
- Not changed: `Panel3DSnapSolver`, `ResolveStage`, `HealStage`, adoption gates, `PanelReconstruction`,
  `Solve3DReport` shape, golden-master tests.
- Acceptance: goldens byte-identical; full suite green; table in TESTING.md; the only behaviour change is
  the GH options bugfix.
- Review focus: every GH cluster call site passes the matched options; expected relation count =
  2×|`FaceAdjacencies`| + envelope faces, `TopologyKey==0` counted not skipped, no key comparison across
  two decodes; expected-fail cases assert current wrong values (not Skip, no loosened tolerances); relation
  multiset invariance asserted on relations, not counts only; no solver drift outside GH components,
  `AdjacencyCluster.cs` diagnostics, tests and TESTING.md.

**P2 — the validated complex becomes a first-class product (in PR #48).** Purely additive to adopted
geometry; goldens byte-identical.
- `ResolvedCellComplex` DTO: cells (index/volume/centre), unique faces (Face3D, owner cell indices,
  per-decode key, flat ordinals), adjacency pairs, naked wires, SolveId GUID. Pure managed, serializable.
- Managed-only decode extension emits the flat ordinal alongside each `OcctCellFace` so the
  ordinal↔(cell,face) bridge is explicit (never derived arithmetically from per-cell counts); unit-tested
  against filter drift (a cell with an undecodable face).
- Captured at adoption, before `Dispose()`: `TryRawResolve` (raw) and `FinalizeAndValidate` (managed, from the
  same decode that produced the adopted cells/signature); exposed as `Solve3DReport.ResolvedCellComplex`.
- Public `Create.AdjacencyCluster(panels, ResolvedCellComplex, ...)` consumes the supplied complex with no
  rebuild; panel identity taken from the supplied panels where attributable, defaulted-and-diagnosed
  otherwise (never silent).
- `Create.Spaces` uses the report's complex instead of its `CellComplexByPanels` rebuild; non-Interior cells
  excluded by CELL INDEX, not centre-distance matching.
- Not changed: adopted geometry/face sets, adoption gates, `PanelReconstruction` output, GH components (P3),
  `MergeCoplanarPanels`.
- Acceptance: full suite green; report carries the complex on both paths; `Create.Spaces` does exactly one
  native build per solve; `CreateSpacesIntegrationTests` counts unchanged (22 / 43 / managed refusal).
- Merge decision for PR #48 (also covers E1): adopted geometry provably unchanged (goldens byte-identical,
  determinism tests green, no gate touched); DTO soundness (bridge from emitted ordinals, per-decode dedup,
  shared face = one entry/two owners, serialization round-trip); `Create.Spaces` single build and index
  exclusion; overload identity enrichment cannot mis-attribute silently; the §4 checklist walked item by
  item with evidence, including E1 items 11–13.

**P3 — Grasshopper handoff (new PR, `feat/cellcomplex-gh-handoff` after the #48 merge).**
- Goo wrapper for `ResolvedCellComplex` (existing `Goo*` pattern; serializable/internalizable).
- `SAMOCCTSolve3D` stamps every output panel with SolveId (`PanelProvenanceParameter`) and gains outputs:
  CellComplex (goo), per-cell faces, adjacency pairs, naked wires, parity/closure summary text.
- `SAMOCCTCreateAdjacencyCluster`: optional `cellComplex_` input; direct consume ONLY when every incoming
  panel's SolveId matches and the roster equals the report's (count + Guid set); otherwise rebuild with
  solver-matched options and emit a drift diagnostic naming why. The rebuild fallback is the NORMAL path for
  saved definitions, not an error.
- Append-only parameters (existing canvases load unchanged); solver, adoption gates and cluster algorithms
  unchanged.
- Tests/acceptance: direct handoff gives a cluster identical to the P2 library overload; a rewired-panels case
  (one panel deleted) takes the rebuild path + diagnostic; goo round-trip; goldens byte-identical.
- Review focus: edited/reordered/subset/superset panel lists fall back to rebuild; a stale complex is never
  consumed silently; SolveId collision across two solves handled by roster equality, not id alone; no native
  handle in GH state; direct-consume and rebuild paths produce identical clusters (asserted on the cluster,
  not counts).

**P4 — gate hardening (new PR, `feat/solver-gate-hardening`, only after E2 has merged).** Close the two
watertight-but-wrong gate holes (deferred review findings #3 and #7) without regressing the five golden
fixtures.
- Finding #3: in `FinalizeAndValidate` the consolidation-rebuild acceptance compares the rebuilt cell count
  against the APPENDED set's own decoded cell count (not the pre-append `resolveCellCount`); one extra decode
  only when a rebuild is attempted.
- Finding #7: `EvaluateRawAdoption` gains a conservative under-split check — reject raw adoption when a dropped
  input face lies strictly interior to a single adopted cell and fills its cross-section (see TESTING.md
  "Adoption-gate hardening" for the shipped rule). Pure/unit-testable; every rejection emits a diagnostic with
  the measured values. `IsRepresented` upgraded from centre-point to area-coverage only where needed to make the
  gate sound, scoped to gate measurement.
- Fail-before/pass-after fixtures: (a) door-cut two-room model raw-adopted as one cell today → must reject raw,
  managed separates; (b) separator-dissolving consolidation rebuild → must keep the appended set.
- Not changed: golden fixtures' outcomes (all five adopt exactly as today), DTO, cluster, GH,
  `PanelReconstruction`, `SnappedPanel` extend/trim primitives and `ConditionStage` (E-track territory).
- Acceptance: goldens byte-identical (verified, not assumed); new fixtures prove both gates; every new
  rejection path emits a coded diagnostic.
- Review focus (adversarial): realistic well-modelled inputs the gate could FALSELY reject — courtyard rings,
  atria spanning floors, deliberate double-height spaces, a largest room that legitimately dwarfs the median;
  a false rejection pushes input onto the managed pipeline and is judged against the managed baselines
  current at review time (`GoldenMasterIntegrationTests.ManagedFixtures`, the P1 parity table), so a false
  positive is a regression, not a safety win; thresholds/diagnostics visible and tunable; the #3 fix decodes the
  appended set (not a cached count); the new fixtures genuinely failed before the fix.

**P5 — panels from unique cell faces (design first, then a separate re-baseline PR; the only phase allowed to
re-baseline).** Design document `docs/P5B_CELLFACE_PANELS_DESIGN.md` must cover:
1. ABI v5 (native): compose sew-path history the way `merge_coplanar` already does (`sewing_history`) so
   `TrySewThenMakeVolume` captures history; fix review finding #8 (`finalize_history` publishes shifted ordinals
   when `make_face` skips an input — publish against the caller's original `face_count`); populate the dormant
   `OcctCell.SourceFaceIndexes`; additive ABI with probe/degrade rules per the ABI v4 precedent.
2. Managed derivation: output faces from unique cell faces (one separator = one panel, owner-cell tags);
   SourceMap projection = union of sources across a shared face's flat ordinals; fix
   `PanelReconstruction.isSplitPiece` to count UNIQUE faces (today a shared separator's two ordinals falsely
   classify a 1:1 source as a split → fresh Guid).
3. Merge relocation: remove/flag the solver's post-resolve `MergeCoplanarFace3Ds`; define how workflow A reaches
   equivalent panel granularity via `MergeCoplanarPanels(AdjacencyCluster)` and the behaviour for panels-only
   consumers who never build a cluster.
4. Aperture policy under finer faces: piece selection, expected orphaned-aperture deltas, how they are pinned.
5. Full expected re-baseline table: per golden fixture which assertions move (managed volumes, panel counts,
   SourceMapMapping/Determinism/AperturePreservation tests) and why raw cells/volumes should NOT move.
6. Compatibility: transition flag (flatten path) yes/no and its removal criterion; API surface changes; risk
   register with mitigations.
- Implementation: native first (ABI v5 + native-side tests), then managed derivation (behind the transition flag
  if the design kept one), then test re-baselining. The P1 parity harness is the measuring stick — if parity
  math itself must change, stop and escalate.
- Acceptance: full suite green; every re-baselined assertion has a table row (old value, new value, fixture,
  justification); raw goldens re-verified with evidence; parity validator green on ALL 9 fixtures for workflow A
  with solver-matched options; orphaned-aperture deltas pinned; benchmark inside the 90 s `Benchmark1500`
  ceiling; no silent behaviour change outside the table.
- Review focus: DTO and SourceMap contracts after projection; `isSplitPiece` Guid semantics (no collisions, no
  spurious fresh Guids); merge relocation leaves panels-only consumers a defined path; every re-baseline row
  justified by a mechanism; parity metric untouched; native `History.cpp`/`CellComplexBuilder.cpp` v5 changes
  (ordinal accounting under skipped faces — finding #8 scenario unit-proven; sew history composition;
  `SourceFaceIndexes` population); ABI probe/degrade on old DLLs; managed projection edge cases (undecodable
  face, `TopologyKey==0`, single-cell models); aperture piece selection.

**P3 outcome note (2026-07-09):** implemented on branch `feat/cellcomplex-gh-handoff`
off the post-#48/E2-merged `sow/2026-Q3` (HEAD `1713a5d`).

- **Roster carrier.** The P2 `ResolvedCellComplex` had no panel-roster concept (the solver has no
  `Panel`/Guid knowledge). Added `ResolvedCellComplex.PanelGuids` (additive field, JSON round-trips)
  and `WithPanelGuids(IEnumerable<Guid>)` (mirrors the existing `WithNakedWires`), empty until attached
  by the analytical/GH layer — never a silent "empty roster matches an empty incoming set" trap (the
  gate explicitly refuses a complex with no recorded roster).
- **Roster gate extracted as a testable unit.** Rather than inlining the SolveId-stamp + roster-set
  comparison inside the GH component (hard to unit-test without a live Grasshopper document), it is a
  new pure-managed static class `CellComplexHandoff` (`SAM.Analytical.OCCT.Solver`):
  `StampSolveId(panels, solveId)` and `TryDirectConsume(panels, resolvedCellComplex, out reason)`. This
  is what made the roster-gate edge cases (task spec's own concern) directly unit-testable: order
  reversal, missing stamp, SolveId collision (a Guid coincidentally in the roster but stamped by a
  DIFFERENT solve), no roster recorded, count mismatch vs Guid-set mismatch (same count, swapped
  panel — the count check alone misses this).
- **`PanelProvenanceParameter.SolveId`** added (string-valued, mirrors `SourceGuid`/`MergedSourceGuids`).
- **`SAMOCCT.Solve3D`** (0.4.0→0.5.0): stamps `SolveId` on every output panel, attaches the SAME roster
  to its `CellComplex` output, plus `ComplexFaces`/`ComplexFaceOwners`/`ComplexAdjacencies`/
  `ComplexAdjacencyFaces`/`ComplexSummary` (all append-only Voluntary). Formatters added to the existing
  `SolverReportFormat` (Grasshopper-facing text formatter class), not a new file, to stay consistent
  with `FormatSourceMap`/`FormatCells`/`FormatLevelFrames`.
- **`SAMOCCT.CreateAdjacencyCluster`** (0.1.0→0.2.0): one new Voluntary/Optional `cellComplex_` input.
  Unwired, the rebuild code path is the EXACT pre-P3 code now guarded by `if (!directConsumed)` —
  byte-identical diagnostics, in the same order, for every existing saved definition. Wired: the gate
  runs, approved → direct P2-overload consume (no native call), refused → same rebuild + one
  `SAM_OCCT_ANALYTICAL_COMPLEX_REBUILD:` diagnostic naming why (suppressed when nothing was wired in —
  the normal quiet default, not a drift).
- **Goo wrapper.** `GooResolvedCellComplex : GooJSAMObject<ResolvedCellComplex>` (mirrors `GooResult`) +
  `GooResolvedCellComplexParam : GH_PersistentParam<GooResolvedCellComplex>` — a thin JSON pass-through,
  no native handle, in a new `Grasshopper/SAM.Analytical.Grasshopper.OCCT/Classes/` folder (this GH
  project had none yet).
- **Test-tier note:** `Testing/SAM.OCCT.UnitTests` has no Grasshopper/Rhino package reference (by
  design, per the two-tier convention), so `IGH_Goo.Write/Read` itself isn't unit-testable there; the
  "goo round-trip" acceptance is satisfied at the DTO level (`ResolvedCellComplexTests` — `Goo*`'s
  `Write`/`Read` is a direct pass-through to `ToJsonObject`/`FromJsonObject`, unchanged from the
  existing `GooJSAMObject<T>` base). The roster-gate and cluster-equivalence tests the task asked for
  are covered end-to-end in `CellComplexHandoffIntegrationTests` (native-gated, against a real solve),
  and the gate logic itself in `CellComplexHandoffTests` (pure managed, fabricated Guids).
- **Verification.** Full suite green with native present — **470 unit / 171 integration** (+1
  native-missing skip, unchanged). All raw/managed goldens and E1/E2 pins byte-identical (P3 touches no
  solver/geometry code).

## 4. Final PR #48 merge checklist (run at the P2 review)

1. All P1+P2 acceptance gates green (full unit + integration suite, native present).
2. Golden masters: **raw** pins byte-identical vs pre-P1 baseline (the production raw-first path).
   **Managed** tripwire pins byte-identical EXCEPT where a phase legitimately changes the managed
   conditioning — those are re-baselined with a per-fixture mechanism table (E1 re-baselined
   whole-level-tilted/towers/two-level-tilted; see item 12 and TESTING.md "E1" section).
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
8. TESTING.md updated (P1/P2 sections); SPDX headers on all new files.
9. PR description updated to state the success metric (SAM-vs-OCCT adjacency parity) and the
   P3–P5 follow-up plan.
10. Gatekeeper verdict recorded: MERGE.
11. **(E1)** Profile-preservation tests green (gable/M-top/stepped/hole/tilted-normal + the
    three cherry-picked PR #49 tests); ExtendBottomTo has dedicated coverage.
12. **(E1)** RAW goldens byte-identical (production path). The fast path could NOT hold the
    managed pins (the golden fixtures contain the non-rectangular walls E1 fixes — verified
    structural, not tunable — so the managed tripwire and workflow B share code+geometry). Per the
    owner decision (2026-07-07) the three moved managed pins are re-baselined in #48 with the
    per-fixture mechanism table (TESTING.md "E1"); two managed pins unchanged.
    `Extend3DCensusIntegrationTests` records the fast/plane-ops split; `SAM_OCCT_EXTEND3D_HOLE_DROPPED`
    diagnostic wired and unit-tested.
13. **(E1)** WorkflowParityIntegrationTests rows changed by E1 carry updated pins + tracking
    comments naming the mechanism; no silent skips introduced.

## 5. PR #48 merge-decision record (2026-07-08, gatekeeper review)

**Verdict: MERGE.** P2 line-level review passed on all four review axes; all 13 §4 items verified
with evidence at commit `0de4fc3` (P1 `112ba61`, E1 `d459562`, P2 `0de4fc3`, pushed to
`origin/fix/solver-raw-first`).

P2 review findings (none blocking):
1. **Observationality — clean.** The P2 diff adds only pure-managed reads: `Project()` before
   `Dispose()` on both paths (raw `TryRawResolve`; managed rebuild-adopted from `rebuildResult`,
   appended/rejected from the same `DecodeCellVolumes` decode that feeds the Signature). No new
   native calls, no call-order change, no gate logic touched. Raw golden pins byte-identical vs
   pre-P1 baseline `c68e200` (only the three owner-approved E1 managed pins moved — verified by
   diff). Suites green at HEAD: 451/451 unit, 163/164 integration (1 skip =
   `NativeMissingIntegrationTests`, the inverse-gated native-absent test — by design).
2. **DTO soundness — sound, one recorded dependency.** Bridge is emitted by replicating the
   solver flatten filter (never arithmetic); unit-tested against filter drift; dedup is
   per-decode key only; shared face = one entry/two owners (unit + integration asserted);
   round-trip green. Recorded dependency: filter equivalence rests on SAM-core behaviour —
   `Shell.Add` drops a face only when `GetBoundingBox()==null`, and `IsValid()==true` implies a
   non-null bbox (valid plane + valid edge ⇒ `Plane.Convert` non-null) — verified in SAM core at
   review time. A SAM-core change to `Shell.Add`/`IsValid` could skew ordinals silently; P5 (the
   ordinal consumer) must add an end-to-end ordinal↔face assertion before consuming ordinals.
   Doc nuance: on the appended (no-rebuild) managed path the ordinals index the decode's own
   flatten, which is not the published `ResolvedFace3Ds` (= appended input faces) — the DTO
   remark's "the ordering ResolvedFace3Ds is built from" holds on the raw and rebuild-adopted
   paths only. Ordinals stay well-defined per decode; P5 concern, noted here so it is not
   rediscovered.
3. **Create.Spaces equivalence — verified.** Second `CellComplexByPanels` rebuild removed
   (grep-clean); the one native build left in Spaces is the classifier's envelope decode
   (`CellClassifier.ClassifyCells`, which reads only Volume/Centre/Index — null `Shell` in the
   DTO-sourced `SolverCell` is safe). Exclusion is by cell index. 22 / 43 / managed-refusal pins
   green (refusal fires at the pre-existing closure gate, Warning severity, before the new
   no-complex Error check).
4. **Overload contract — never silent.** Identity inherited only on a sole
   coplanar+interior-point match; zero/ambiguous ⇒ default + counted in
   `SAM_OCCT_ANALYTICAL_PANEL_IDENTITY`. Intentional divergence from the private
   `DirectAdjacencyCluster` (no `UpdatePanelTypes(0)`/`SetDefaultConstructionByPanelType()`
   finalisers) is documented in-code — inherited identity must survive. Two watch-notes for
   P3/P5: the match is point-containment (a cell face spanning two coplanar source panels
   inherits from the one containing its internal point — rare at imprint granularity, diagnosed
   only in aggregate), and a `Create.Panel` null return drops a face uncounted (decode-valid
   faces make this practically unreachable).

§4 walk (evidence one-liner each): (1) suites green as above; (2) raw pins byte-identical vs
`c68e200`, managed re-baseline = exactly the three E1 rows with mechanism table (TESTING.md
"E1"); (3) `WorkflowParityIntegrationTests` runs all 9 fixtures through `MergeCoplanarPanels`,
table at TESTING.md §P1, TrackingComment fields present, no silent skips; (4) GH options fix in
both components (ByShells resolves `SewBeforeBuild` from sew/mesh-input detection, labelled
bugfix in-code) — release-noted in the PR description below; (5) report carries the complex on
both paths (integration tests), round-trip + ordinal-bridge unit tests green; (6) single build
by inspection + removed call site, counts pinned green; (7) no native/.cpp/ABI/
PanelReconstruction changes in `112ba61..0de4fc3`; golden edit = the approved E1 managed
re-baseline only; (8) TESTING.md P1/E1/P2 sections present, SPDX on all six new files, all
three commits signed; (9) PR description below states metric + follow-ups; (10) this record;
(11) gable/M-top/tilted-normal/hole/ExtendBottomTo×2 + PR #49 cherry-picks (`26ac05a`) present
and green — "stepped" is covered via the M-top family (R2 groups M/stepped tops) and the
shifted-top exact-foot `SetVerticalFootprint` trims; no literal staircase-profile fixture (minor
gap, non-blocking — E2/E3 may add one); (12) raw goldens byte-identical, census test present,
`SAM_OCCT_EXTEND3D_HOLE_DROPPED` unit-tested
(`SetVerticalFootprint_ShortenThroughWindow_EmitsHoleDroppedDiagnostic`); (13) E1-changed parity
rows carry mechanism-naming tracking comments (`towers 26 -> 28`, `Face3D-home 25-cell managed
fallback`, `AdjacencyCluster-home 18/18 clean`).

Post-merge order (unchanged): **E2** (`feat/extend3d-plane-targets` off `sow/2026-Q3`; scope in
`docs/EXTEND3D_ROBUST_HANDOVER.md` §4) → {P3 ∥ E3-after-E2} → P4 (blocked on E2) → P5. The PR
description update:

```markdown
## CellComplex-first solver: P1 + E1 + P2

**New success metric:** SAM adjacency parity against OCCT CellComplex adjacency — per shared
cell face exactly one SAM panel related to both spaces; per envelope face exactly one space.
Watertight is necessary, not sufficient. `Panel soup → reliable OCCT CellComplex →
adjacency-ready SAM panels → correct SAM adjacency cluster`.

**P1 — options-parity bugfix + measurement (the one behaviour change, release note):**
`SAMOCCTCreateAdjacencyCluster` / `SAMOCCTCreateAdjacencyClusterByShells` now build with the
solver-matched recipe (`AvoidInternalShapes=false, SewBeforeBuild=true, SewingTolerance=0.01`)
instead of defaults (`true`/`0.0`) — production GH clusters previously used different options
than every validated solver build. Plus: `SAM_OCCT_ANALYTICAL_PARITY` diagnostic (relations
added vs expected, TopologyKey==0 count, zero-relation panels) and
`WorkflowParityIntegrationTests` — all 9 fixtures through workflows A/A-matched/B and
`MergeCoplanarPanels`, per-fixture table in TESTING.md, broken cases pinned with tracking
comments, never skipped.

**E1 — robust Extend3D primitives (supersedes PR #49):** `SnappedPanel` extend/trim rebuilt as
profile-preserving plane-ops (SAM-core `Query.Extend`/`Query.Cut`); gables, M-tops, sloped and
tilted walls keep their profile and plane; `ExtendHorizontal` retired into
`SetVerticalFootprint`; `SAM_OCCT_EXTEND3D_HOLE_DROPPED` when a trim consumes an opening. RAW
goldens (production path) byte-identical; three managed tripwire pins re-baselined per owner
decision 2026-07-07 with a per-fixture mechanism table (TESTING.md "E1"); two managed pins
unchanged; `Extend3DCensusIntegrationTests` records the fast/plane-ops split.

**P2 — the complex becomes the product:** `ResolvedCellComplex` DTO (cells; unique faces with
owner cells, per-decode keys, flat ordinals; adjacency pairs; naked wires; SolveId) captured at
adoption on both solver paths, surfaced as `Solve3DReport.ResolvedCellComplex` — pure managed,
serializable, round-trips. Public `Create.AdjacencyCluster(panels, complex, out diagnostics,
..., excludeCellIndices)` consumes it with NO rebuild; panel identity inherited only on an
unambiguous match, defaulted-and-diagnosed otherwise. `Create.Spaces` consumes the report's
complex (single native build — the classifier's envelope decode); non-Interior cells excluded
by cell index. Invariants hold: flat 22, two-level raw 43, managed refusal.

**Follow-ups (separate PRs):** E2 plane-target extension (gates P4) → P3 GH handoff (DTO goo,
SolveId roster gate, cell/adjacency outputs) ∥ E3 extend observability → P4 gate hardening
(review findings #3/#7) → P5 panels from unique cell faces + ABI v5 (the only re-baselining phase).

Suites: 451 unit / 163 integration green (native present); raw goldens byte-identical;
determinism green; merge-decision record in `docs/CELLCOMPLEX_FIRST_HANDOVER.md` §5.
```

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
   paths: no history until ABI v5; coarse geometric fallback; review finding #8 is a known native gap.
9. **`AvoidInternalShapes` removing needed separators?** Full separators survive; dangling
   partial partitions are removed — production cluster ran with `true` (the P1 bugfix); solver
   paths use `false`.
10. **Face keys geometric or true topology?** Geometric signature — not TShape identity.
11. **Safe enough for SAM analytical adjacency?** Within one decode as used by the direct path:
    yes. As cross-stage identity: no — hence DTO + parity validation.
12. **Stronger native face-ownership map needed?** Not for P1–P4 (ownership already decodes).
    For P5-grade provenance: yes — ABI v5 (sew history, finding #8 fix, SourceFaceIndexes).

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
