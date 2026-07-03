# P5 Design Review — Diagnosis-Driven Closure (AutoTune3D / per-loop sew / GapFill v2 / RetainDropped v2)

Status: approved (GO WITH CHANGES) · Date: 2026-07-03 · Host branch: `fix/solver-raw-first` (HEAD `741c5b9`, Phases 0–4 complete)

Pre-implementation design review of Phase 5 (`docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md`
§E Phase 5, §F Stage C, §M, §N). **Where this review and the plan differ, this review wins.**
Verified against the repo as of Phase 4 completion: `Panel3DSnapSolver.cs` (Execute flow,
`EvaluateRawAdoption`, `IsRepresented`), `HealStage.cs`, `GapFill.cs`, `ConditionStage.cs`,
`ClosureSignature3D.cs`, `SolverContext.cs`, `SnappedPanel.cs` (reach surface), `ResolveStage.cs`
(P3 history composition), `OcctNakedWire.cs`/`ShapeValidate.cpp` (wire owner semantics),
`Query/ShellsDistance.cs`, `Query/ShellsImprint.cs`, `Query/Sew.cs`, `OcctBuildOptions.cs`,
`sam_occt.h` (ABI v4 surface), the P4 `PanelReconstruction.cs`, and SAM_Solver's 2D
`AutoTuneSolver.cs` (the contract being mirrored).

---

## Binding owner cautions (supersede anything below where they conflict)

1. **Review-before-code gate:** this document must be committed and pushed BEFORE any Phase 5
   implementation sub-phase starts. No 5a code until the review is durable in the repo.
2. **Provenance preservation in the consolidation rebuild:** the rebuild must NEVER discard existing
   SourceMap provenance (`Resolved`, `GapFill`, `DroppedRetained` entries all survive). When the
   rebuild captures native history, compose it onto the existing map (existing source →
   pre-rebuild ordinal → rebuild-history → post-rebuild ordinal). When rebuild history is
   unavailable, geometric backfill is applied ONLY to faces that end up unmapped — it never
   overwrites or replaces an existing mapped entry.
3. **CellIncreaseAdjacent is conservative:** if a new cell's adjacency to a closed naked loop cannot
   be positively proven, the AutoTune round is REJECTED and an `EscalatedPanel` Warning diagnostic
   is emitted naming the unproven cell. Unprovable = reject, never benefit-of-the-doubt.
4. **Managed-side only:** Phase 5 uses only existing native wrappers. No ABI v5. No new native entry
   points. No Grasshopper component changes. No widening of OCCT fuzzy tolerance anywhere.
5. **Sub-phase commit discipline:** each of 5a–5f is a separate commit with both suites green and
   golden masters re-run first. **Stop immediately** if any golden-master signature changes — report
   the exact delta; never re-baseline, never continue past it.

---

## A. Executive verdict

**GO WITH CHANGES.** The plan's core shape is right (attributed local escalation,
measured-to-target, signature-gated acceptance, final-validate-last) and — critically — **no new
native ABI is required**. Four design gaps must be fixed before code starts:

1. **`ShellsImprint` cannot do what §F asks.** `Query.ShellsImprint(IEnumerable<Shell>)` mutually
   imprints *shells*; there is no entry point to surgically imprint patch faces into a resolved face
   set, and imprint exposes no history. **Fix: replace both imprint steps (patches + retained faces)
   with ONE signature-gated "consolidation rebuild"** — `Create.Shells(resolved + patches +
   retained)` (MakerVolume IS the canonical mutual imprint, and the direct build path already
   captures ABI v4 history). If the rebuild regresses the signature, keep the appended-unimprinted
   set + Warning.
2. **"Per-loop sew" cannot be a per-loop native operation.** `sam_occt_sew_faces` is global and
   captures no history (the ABI v4 §F.4 scope cut). **Fix: capped-tolerance global sew + per-loop
   bookkeeping** (match wires before/after by proximity, classify Closed/Persisting/New) **+ fusion
   veto** (reject the whole sew if any pre-sew near-parallel pair fused) — per-loop *acceptance
   evidence*, not per-loop application. This matches the §F pseudocode, which also accepts/rejects
   the sew result as a whole.
3. **`ShellsDistance` is the wrong tool for the sew cap.** It takes `Shell`s only and costs a native
   round-trip + 2 topology builds per call. **Fix: a managed `MinPairSeparation`** over near-parallel
   face pairs (|normal·normal| ≥ 0.99, overlapping bboxes, plane distance) — O(n²) with bbox
   prefilter, exact enough for a tolerance cap; `ShellsDistance` reserved for future ambiguous cases.
4. **Bucket ×1.25 escalation is a partition-collapse hazard.** Bucket changes Stage-A snapping
   globally for that panel (can merge a genuine double wall). **Fix: implement the option, default
   OFF** (`AutoTune3DOptions.EscalateBucket = false`); the weld/shaft fixtures guard it if enabled.

Also required: `ClosureSignature3D` gains a sliver term (additive; `IsRegressionOf` untouched); the
managed pipeline must populate `Signature` (today only the adopted raw path does); trim-preference
is scoped to measured-to-target semantics (§E).

## B. Current repo readiness after Phase 4

| Capability | Verdict | Evidence / gap |
|---|---|---|
| SourceMap | **Sufficient** | Always populated (exact via P3 history bridge where available, geometric backfill otherwise). Dropped detection = `FacesOf(source).Count == 0`. `Provenance.DroppedRetained`/`GapFill` exist; P4 `PanelReconstruction` already consumes the map. |
| Naked wires | **Sufficient** | `OcctNakedWire`: ordered `Point3Ds` (closing vertex not duplicated), `IsClosed`, `EdgeOwnerFaceIndices`. **Confirmed:** owner index aligns with the face list passed to `Query.Validate` (ShapeValidate.cpp:112–134 — the face map mirrors input order through the internal sew); −1-tolerant fallback required (midpoint-on-face). Exposed on `ResolveStage.Result.NakedWires` / `Panel3DSnapSolver.NakedWires` since P3. |
| Diagnostics | **Sufficient — zero enum churn** | `EscalatedPanel`, `RejectedSew`, `NakedLoop`, `Gap`, `DroppedFace`, `ToleranceDrift`, `BudgetExceeded`, `SliverCell` all exist (P1 reserved them). Rejected escalation = `EscalatedPanel` at Warning severity. |
| ResolveStage decomposition | **Adequate, needs 2 seams** | The adaptive sew is inline (global naked-count acceptance) — extract to `SewV2`; add `FinalizeAndValidate` so the final naked count is measured AFTER patches/retains (fixes the "pre-patch naked-count lie" — ResolveStage currently validates before GapFill). |
| Analytical reconstruction | **Stable** | P4 `PanelReconstruction.Build` handles 1:1/split/merge + orphaned apertures; retained faces carry `DroppedRetained` provenance → dominant-source Guid policy applies unchanged; patches stay air panels (`Provenance=GapFill`). |
| Fixtures/test utilities | **Partial** | TestGeometry factories + the RawGateMergedCells synthetic pattern + golden masters exist. Missing: gappy perturbation generator, parallel-pair weld fixture, ~1,500-face benchmark. All buildable synthetically (no new `.sam` files). |
| SolverContext | Exists, **unused** | Built for P5; recommendation: keep explicit `maxExtends` lists in AutoTune3DSolver (smaller diff); SolverContext adoption deferred. |
| Native ABI | **No v5 needed** | Everything Phase 5 needs exists after the §A changes (no surgical imprint, no sew history, no native distance in the hot path). |

## C. What is correct in the current Phase 5 plan

- Attributed local escalation over global tolerance widening; the ladder raises *permission*, the
  extension is measured-to-target (+overshoot 0.05) — the anti-tunneling core is right and
  `SnappedPanel.GrowOutwardTo` (measured) already exists.
- ≤3 rounds (not 2D's 6): MakerVolume is the cost driver — the right budget.
- Signature-gated acceptance with `ClosureSignature3D`; atomic reject-and-stop mirrors the proven 2D
  `AutoTuneSolver` contract (ladder [0.5,0.75,1.0,1.5], gap-adjacent panels only, rollback on
  over-merge).
- Sew tolerance capped below measured min pair separation — the right law; only the measurement tool
  changes (managed, not `ShellsDistance`).
- GapFill v2 consuming native wires kills the managed loop-walk's real fragilities (tightest-turn
  junction walk, multiplicity-3 blindness, hard-coded 0.5 m anchor radius — all confirmed in
  GapFill.cs).
- RetainDropped v2 re-adding ORIGINAL CLEAN geometry: today `HealStage.RetainDropped` re-adds the
  extended overshooting set verbatim and the Execute call site doesn't even pass the SourceMap —
  both defects confirmed (HealStage.cs:37 / Panel3DSnapSolver.cs:462–466).
- Final Validate after all topology-changing ops; best-effort result always returned with
  diagnostics.
- Performance ceiling checked NOW (benchmark fixture created in P5; Phase 9 reuses it).

## D. What is risky or wrong

1. **[WRONG] §F's `Imprint(r, patches)` + `ShellsImprint` for retained faces** — no such native
   capability exists. → Consolidation rebuild (§A.1). Deviation note: §F imprints patches *before*
   `RetainDroppedV2`; the consolidation rebuild does both at once — one bounded MakerVolume run
   instead of two, same end state. Review-sanctioned.
2. **[WRONG] Per-loop sew as an operation** — native sew is global and history-less. → §A.2
   bookkeeping + veto.
3. **[RISK] Bucket escalation** → default OFF (§A.4).
4. **[RISK] Trim-cheaper-than-extend is underspecified in 3D.** 2D's trim=d/2 cost law has no direct
   3D face analogue; MakerVolume already trims at intersections. → Scope: measured-to-target
   inherently *includes* trim (if the diagnosed target is closer than the current reach, the
   footprint is set to target+overshoot — shrink via `SetVerticalFootprint`, which re-extrudes on an
   arbitrary foot). Where a primitive cannot shrink: do-not-grow + diagnostic. No separate trim
   planner in P5.
5. **[RISK] RetainDropped v2 golden-master exposure.** Raw-path re-adds get duplicate/degenerate
   filtering; the flat fixture currently re-adds 16 dropped faces — filtering may change the solved
   face set and `CaptureSignature`'s independent rebuild. → Explicit stop-and-report checkpoint at
   sub-phase 5c; a raw-path signature delta requires justification or scope-back.
6. **[RISK] Attribution wrongness** (−1 owners, merged sources) → midpoint-on-face fallback + unit
   tests with hand-built wires/maps; a wrongly escalated culprit is bounded by measured-to-target
   (it can only grow to a *diagnosed* meeting object) + the signature gate.
7. **[RISK] Signature blind spots:** `IsRegressionOf` has no sliver term and compares total volume
   only → additive `SliverCellCount` + a P5 acceptance helper (§I); do NOT change `IsRegressionOf`'s
   locked semantics (ClosureSignature3DTests).
8. **[RISK] Managed-path `Signature` is not populated today** — AutoTune's loop condition reads it →
   5a fix.
9. **[RISK] Diagnostics lying via ordering** — enforce in ONE place: `FinalizeAndValidate` is the
   only producer of the final naked count/wires; mid-pipeline validates become intermediate
   diagnostics only.
10. **[MINOR] Wires on tilted models:** attribution happens in the world frame post-rotation — owner
    indices refer to the face list passed to Validate (world frame) — consistent, but assert with
    the tilted fixtures.

## E. Recommended AutoTune3D algorithm

New `Classes/AutoTune3DSolver.cs` in **SAM.Geometry.OCCT.Solver** (geometric core; Face3D domain),
plus a thin `Modify.AutoTune3D(this IEnumerable<Panel> …)` entry in **SAM.Analytical.OCCT.Solver**
that wraps it and reconstructs panels via P4 `PanelReconstruction` (mirrors the
Solve3D/Panel3DSnapSolver split; the core has no Panel dependency).

```
Execute(occtOptions, tune):
  baseline = Panel3DSnapSolver(face3Ds, buckets, weights, maxExtends).Execute(occtOptions)  // raw-first
  sig = baseline.Signature                                  // managed-path population added in 5a
  if sig.NakedEdgeCount == 0: return baseline
  rounds = 0
  while sig.NakedEdgeCount > 0 && rounds < tune.MaxRounds (3):
    wires = solver.NakedWires
    if wires empty: Diagnostics.Warning("naked>0 but no wires"); break
    culprits = AttributeLoopsToSources(wires, resolvedFace3Ds, solver.SourceMap)
        // per wire edge: owner = EdgeOwnerFaceIndices[i]; if -1 → nearest coplanar resolved face
        //   by edge midpoint; ownerFaceIndex → SourceMap.SourcesOf(FaceKey(owner)) → source indices
        //   (skip FabricatedSource)
    escalatedMaxExtends = clone(maxExtends); for s in culprits: next ladder value > current
        // ladder [0.5, 0.75, 1.0, 1.5]; Diagnostics: EscalatedPanel Info (source, old→new, round);
        // culprits already at 1.5 drop out; ALL culprits exhausted → break (ladder exhausted)
    roundSolver = new Panel3DSnapSolver(face3Ds, buckets, weights, escalatedMaxExtends)
        { ForceManagedPipeline = true, …same knobs }        // raw re-attempt is pointless per round
    roundSolver.Execute(occtOptions)
        // anti-tunneling: conditioning is measured-to-target (ExtendWalls → 2D ExtensionSolver,
        // Extend → cap planes, Fill → GrowOutwardTo after 5b); the ladder only raises the permission
        // cap. Escalated panels' conditioned faces run Query.SelfIntersectionFace3Ds;
        // self-intersecting → revert that panel's escalation for this round + Warning.
    newSig = roundSolver.Signature
    accept iff:
        newSig.NakedEdgeCount < sig.NakedEdgeCount
        && !newSig.IsRegressionOf(sig)
        && newSig.SliverCellCount <= sig.SliverCellCount
        && CellIncreaseAdjacent(newSig, closedLoops)
        // CONSERVATIVE (owner caution 3): every NEW cell must be POSITIVELY proven adjacent to a
        // loop that closed this round (cell centroid within reach of the closed loop's bbox);
        // unprovable adjacency = REJECT the round + EscalatedPanel Warning naming the unproven cell.
    accept: adopt roundSolver state (faces, SourceMap, wires, diagnostics merge);
            maxExtends = escalatedMaxExtends; rounds++
    reject: EscalatedPanel Warning ("round rejected: <reason>", signature pair); break  // §F semantics
  return best state + rounds + residual wires (best-effort, never throws)
```

Notes: a full-pipeline re-run per round is deterministic (Snap is a fixed-point iteration; unchanged
parameters ⇒ unchanged geometry — locked by a unit test), so "only culprit panels move" holds
without surgical re-condition state. `PreferTrimOverExtend` = measured-to-target with
shrink-capable `SetVerticalFootprint` (D.4). Bucket escalation behind `EscalateBucket=false`.

## F. Recommended per-loop sew acceptance algorithm

Extract from ResolveStage into `HealStage.SewV2(resolved, occtOptions, sewOptions, diagnostics)`:

```
wiresBefore, nakedBefore = Validate(resolved)
minSep = MinPairSeparation(resolved)     // managed: |n·n| ≥ 0.99, bbox-overlapping pairs,
                                         // plane distance in [Tolerance.Distance, 0.5]; min or null
sewTol = min(SewExpandTolerance (0.1), hard clamp 0.3, minSep × SewSafetyFactor (0.5) when minSep != null)
if sewTol <= pre-build sew (0.01): Diagnostics Info "sew skipped (cap below floor)"; return unchanged
sewn = Query.Sew(resolved, options{SewingTolerance = sewTol}, makeSolid: false)
wiresAfter, nakedAfter = Validate(sewn)
loop bookkeeping: match before↔after wires by centroid distance < max(2·sewTol, 0.1) + bbox
                  similarity → Closed / Persisting / New (each recorded: NakedLoop Info/Warning)
fusion veto: for every pre-sew near-parallel pair with separation ≤ 2·sewTol:
                  both faces must survive in sewn (IsRepresented both directions, area conserved
                  ±5 %); any fusion → REJECT whole sew, RejectedSew(pair, "fused distinct parallel faces")
accept iff nakedAfter < nakedBefore && no veto && New loops justified
                  (New loops allowed only if net naked strictly drops; each New gets a Warning)
reject → RejectedSew + keep pre-sew faces
```

The cap alone makes fusion near-impossible (an 0.08 m pair ⇒ cap 0.04 ⇒ an unrelated 0.09 m gap is
*also* unsewable — it closes via AutoTune extension instead, which is exactly the weld fixture's
expected behavior); the veto is belt-and-braces. Acceptance is whole-result (matching the §F
pseudocode) with per-loop *evidence and diagnostics*.

## G. Recommended GapFill v2 algorithm

`GapFill.FromNakedWires(wires, diagnostics, sourceMap, baseOrdinal)` — a new entry alongside the
legacy walk:

- Input: `report.NakedWires` (ordered, closed-flagged). The legacy `NakedLoopFace3Ds` walk is
  retained ONLY as the pre-v4-native fallback (wires empty but naked points exist) + an Info
  diagnostic saying the fallback ran.
- Per closed wire (≥3 points): planarity = max deviation from the best-fit plane ≤ max(0.01 m,
  10×tolerance).
  - Planar → `Face3D.Create(Polygon3D(points))`; safety: valid, area ≥ 1e-4 m² (the existing
    air-panel floor), `Query.SelfIntersectionFace3Ds` empty. Pass → patch.
  - Non-planar / failed planar → centroid-fan triangles (reuse the existing fan code) **tagged
    `NakedLoop` Warning "non-planar loop; fan-patched — inspect"** — last-resort diagnostic output,
    never silent success.
- Open wires → no patch; `NakedLoop` Warning with the polyline (residual; GH display in Phase 8).
- Nested/hole loops: NOT handled in P5 (rare; emit a Warning when a closed wire's plane coincides
  with and contains another) — documented scope cut.
- Every patch: `sourceMap.RecordFabricated(FaceKey(ordinal), Provenance.GapFill)`; Solve3D continues
  to emit patches as air panels stamped `Provenance="GapFill"` (P4).
- Imprint: via the consolidation rebuild in `FinalizeAndValidate` (§H), not per-patch.

## H. Recommended RetainDropped v2 + FinalizeAndValidate

`HealStage.RetainDroppedV2(resolved, cleanFace3Ds, sourceIndicesPerCleanFace, sourceMap, diagnostics)`:

- Dropped sources = `sourceMap.FacesOf(s).Count == 0` (map-driven; the geometric backfill guarantees
  outputs have sources, so a source with zero faces is genuinely unrepresented).
- Candidates = clean faces (SnapStage output — "original clean geometry") whose
  `SourceIndicesPerFace` intersects the dropped set. Raw path (no clean set exists): raw input
  faces, **filtering only** (per plan wording).
- Filters: `IsValid()`, area ≥ 1e-4, `!IsRepresented(candidate, resolved ∪ alreadyRetained)` (kills
  duplicates/degenerates/double-cover).
- Append + `sourceMap.Record(source, FaceKey(ordinal), Provenance.DroppedRetained)` + `DroppedFace`
  Info ("retained clean geometry for source N"). The Execute call site finally passes the SourceMap
  (fixing the current null).

`FinalizeAndValidate(resolved, patches, retained, occtOptions, diagnostics)` — the single
final-truth step:

```
finalFaces = resolved + patches + retained
if (patches ∪ retained ≠ ∅) && ConsolidateRebuild (default true):
    rebuilt = Create.Shells(finalFaces, options{SewBeforeBuild=false, AvoidInternalShapes=false})
    // the direct build path — captures ABI v4 history
    accept iff rebuiltCells ≥ preCells && rebuiltNaked ≤ preNaked (via Validate) → adopt rebuilt faces
    reject → keep the appended set + Warning("consolidation rebuild regressed; patches appended unimprinted")

    // PROVENANCE PRESERVATION (owner caution 2 — binding):
    //  - the pre-rebuild SourceMap (Resolved / GapFill / DroppedRetained entries) is NEVER discarded
    //    or rebuilt from scratch;
    //  - rebuild history available → compose:
    //      existingMap.Compose(HistorySourceMap.ToSourceMap(rebuildHistory))
    //    so every existing entry is carried pre-rebuild-ordinal → post-rebuild-ordinal (fabricated
    //    GapFill patches keep their RecordFabricated identity across the hop);
    //  - rebuild history unavailable (or a HistoryGap) → geometric backfill runs ONLY for output
    //    faces that end up with no source — it never overwrites, replaces, or re-derives an
    //    already-mapped entry.
finalReport = Validate(finalFaces) → FINAL naked count + wires   (the only numbers reported outward)
Signature = ClosureSignature3D(final)                            (managed path now populates it)
```

## I. Acceptance rules & ClosureSignature3D changes

**ClosureSignature3D (additive only):** new `SliverCellCount` (count of cells < MinCellVolume,
supplied at construction); a `FromCellComplexResult` overload accepting `minCellVolume`; `ToString`
extended. **`IsRegressionOf` semantics untouched** (locked by ClosureSignature3DTests). New static
helper `AutoTune3DSolver.IsAcceptableRound(previous, candidate)` implements the composite rule.

| Decision | Accept iff |
|---|---|
| AutoTune round | naked strictly ↓ AND `!IsRegressionOf(prev)` AND sliver not ↑ AND every new cell POSITIVELY proven adjacent to a closed loop (unprovable = reject + diagnostic) |
| Adaptive sew (v2) | naked strictly ↓ AND no fusion veto AND New loops justified (§F) |
| Gap-fill patch | closed wire, planarity/self-intersection/area checks pass (else fan+Warning or residual Warning) |
| Retained dropped face | source unmapped AND `!IsRepresented` AND valid AND area ≥ 1e-4 |
| Consolidation rebuild | cells ≥ pre AND naked ≤ pre (else append-unimprinted + Warning) |
| Final result | always returned best-effort; `AdoptedLevel` + rounds + residual `NakedLoop`s + final Signature recorded |

## J. Required tests and fixtures (all synthetic — no new .sam files)

Unit (`Testing/SAM.OCCT.UnitTests`):
1. `LoopAttributionTests` — hand-built `OcctNakedWire`s + SourceMap: owner→source, −1 midpoint
   fallback, merged-source loops, fabricated exclusion.
2. `MinPairSeparationTests` — parallel pairs at 0.08/0.30/none; non-parallel ignored; bbox prefilter.
3. `AutoTuneAcceptanceTests` — `IsAcceptableRound` truth table (naked ↓ but cells ↓ → reject;
   sliver ↑ → reject; unproven cell increase → reject; boundary cases).
4. `ClosureSignature3DTests` additions — sliver term construction + old `IsRegressionOf` unchanged.
5. `SewBookkeepingTests` — Closed/Persisting/New wire matching on synthetic wire sets;
   tolerance-cap math.
6. `GapFillV2Tests` — planar wire → single patch; non-planar → fan + Warning; open wire → residual
   Warning; self-intersecting loop rejected.
7. `RetainDroppedV2Tests` — dropped-source detection from the map; duplicate/degenerate filtering;
   provenance recording.
8. Determinism lock — same inputs → identical resolved faces across two runs (AutoTune's
   culprit-only-movement invariant).

Integration (`Testing/SAM.OCCT.IntegrationTests`, native-gated):

| # | Fixture | Geometry | Expected | Guards |
|---|---|---|---|---|
| 1 | **GappyFlat** | 3×3-room single level, seeded generator; 3–4 walls pulled 0.05–0.30 m short of caps/neighbours + one cap tilted ~0.5° | raw rejected (naked>0); AutoTune closes ≤3 rounds; cells = room count; naked 0; `EscalatedPanel` ONLY for perturbed walls' sources | escalation reach + attribution precision |
| 2 | **ParallelPairWeld** | two rooms separated by an 0.08 m double wall; one unrelated wall 0.09 m short | sew capped ≤0.04 (no fusion); AutoTune extends the short wall (measured 0.09 m); cells ≥ 3 (2 rooms + cavity 0.96 m³ > MinCellVolume); partition survives; naked 0 | fusion veto + cap law + anti-tunneling |
| 3 | **ShaftProtection** | P2's 0.3–0.4 m shaft-void geometry through AutoTune rounds | shaft cell survives; no bucket escalation (default OFF) | cavity collapse |
| 4 | **GapFillPatch** | open-top box (5 faces) | 1 closed wire → 1 planar patch → consolidation rebuild → 1 cell; FINAL naked 0; air panel `Provenance=GapFill` | patch + final-validate ordering |
| 5 | **NonPlanarLoop** | box with one rim corner raised 0.1 m | fan fallback + `NakedLoop` Warning; final validate truthful | silent-fabrication ban |
| 6 | **DroppedRetain** | floating interior shelf face bounding no cell | CLEAN shelf re-added (not extended), `DroppedRetained` provenance, no duplicates; solved panel keeps source Guid (P4) | RetainDropped v2 |
| 7 | **ResidualDiagnostics** | isolated unclosable wall | best-effort result, residual `NakedLoop` wires, rounds recorded, no throw | boundedness/honesty |
| 8 | **Benchmark1500** | seeded generator ≈1,500 faces (e.g., 10×8 rooms × 2 levels) + perturbations forcing 3 rounds | wall-clock < 90 s (soft ceiling; `SAM_OCCT_SKIP_PERF` env-var skip seam) + closure sanity | performance; Phase 9 reuses |
| 9 | **Golden masters** | existing 10 signatures | unchanged at every sub-phase checkpoint | stop-and-report protocol |

## K. Implementation sub-phases (each builds + both suites green before the next)

| # | Objective | Model | Effort | Files (primary) | Checkpoint |
|---|---|---|---|---|---|
| **5a** | Foundations: `SliverCellCount` on ClosureSignature3D (+managed-path `Signature` population), `MinPairSeparation`, `AttributeLoopsToSources`, determinism lock test | Opus 4.8 | S | ClosureSignature3D.cs, Panel3DSnapSolver.cs, new LoopAttribution.cs, unit tests | No behavior change; goldens byte-identical |
| **5b** | GapFill v2 + `FinalizeAndValidate` (final-truth ordering, consolidation rebuild + provenance preservation) | Opus 4.8 | M | GapFill.cs, ResolveStage.cs, HealStage.cs, Panel3DSnapSolver.cs; fixtures 4/5 | Goldens: naked counts must not change (all currently 0); report any face-count drift |
| **5c** | RetainDropped v2 (clean geometry, filters, provenance, SourceMap threading; raw-path filtering) | Opus 4.8 | S–M | HealStage.cs, Panel3DSnapSolver.cs; fixture 6 | **Highest golden-master risk** — stop-and-report any signature delta |
| **5d** | Sew v2 (cap + bookkeeping + fusion veto) replacing the inline adaptive sew | Opus 4.8 | M | HealStage.cs (SewV2), ResolveStage.cs; fixtures 2/3 partial | Goldens unchanged (sew adopted only when naked↓ today; the cap can only make adoption rarer; flat/tilted adopt raw anyway) |
| **5e** | AutoTune3DSolver core + AutoTune3DOptions + `Modify.AutoTune3D` analytical entry | Opus 4.8 | L | new AutoTune3DSolver.cs, new Modify/AutoTune3D.cs; fixtures 1/2/3/7 | Goldens unchanged (AutoTune engages only when naked>0 post-solve — all goldens are 0-naked) |
| **5f** | Benchmark fixture + perf guard + TESTING.md Phase 5 section + plan status | Sonnet 5 | S | BenchmarkFixture generator, PerformanceGuardIntegrationTests, TESTING.md, plan doc | <90 s asserted; full suites green; docs complete |

**Commit discipline (owner caution 5 — binding):** each of 5a–5f is its OWN commit (conventional
message + "Generated by Michal Dengusiak & Claude Code"), made only after both suites are green AND
the golden masters have been re-run for that sub-phase. **Any golden-master signature change = stop
immediately**, report the exact delta (fixture, number, cause), and wait — never re-baseline, never
proceed past it. Sub-phase 5c is pre-flagged as the highest-risk checkpoint. Prerequisite gate
(owner caution 1): this review is committed and pushed BEFORE 5a begins.

## L. Data model / API changes (minimal, additive)

- `AutoTune3DOptions` (Geometry.OCCT.Solver): `MaxRounds=3`, `MaxExtendLadder=[0.5,0.75,1.0,1.5]`,
  `EscalateBucket=false`, `BucketFactor=1.25`, `PreferTrimOverExtend=true`, `SewSafetyFactor=0.5`,
  `ConsolidateRebuild=true`.
- `AutoTune3DSolver` (core): ctor mirrors Panel3DSnapSolver inputs;
  `Execute(OcctBuildOptions, AutoTune3DOptions)`; exposes `ResolvedFace3Ds`, `SourceMap`,
  `Signature`, `Rounds`, `NakedWires`, `Diagnostics`, `HoleFillFace3Ds`.
- `Modify.AutoTune3D(this IEnumerable<Panel>, out naked, out diagnostics, out orphanedApertures, …)`
  (Analytical): wraps the core + P4 `PanelReconstruction`.
- `HealStage`: `RetainDroppedV2(...)`, `SewV2(...)`; existing `RetainDropped` kept (the raw-path
  filter-only variant delegates).
- `GapFill`: `FromNakedWires(...)` + small `GapFillResult { Patches, LoopOutcomes }`.
- `ClosureSignature3D`: `SliverCellCount` + overloads (additive).
- No new `DiagnosticCode` values; no `Solver3DResult` type (P4 precedent: additive out-params);
  **no native ABI change; no GH changes** (Phase 8).

## M. Performance strategy

Budget per solve: 1 baseline build + ≤3 round builds + ≤1 sew + ≤1 consolidation rebuild =
**≤6 MakerVolume-class ops** (validates are sew+free-bounds, cheaper). `MinPairSeparation` is
managed O(n²) with a bbox prefilter (fine at 2k faces). Attribution is O(wires×edges). No native
distance calls in the loop. GlueMode=Shift on re-runs and spatial indexing stay in Phase 9 (the
fixture is built now). If Benchmark1500 blows 90 s: first lever = MaxRounds 3→2; second = enable
GlueMode=Shift early on watertight re-runs; then the redesign checkpoint per the plan.

## N. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Closing true gaps while preserving partitions | Sew cap from measured minSep ×0.5 + fusion veto + weld fixture; escalation is measured-to-target so an extended wall stops at its diagnosed object |
| False merges from sew tolerance | Cap below minSep; skip sew when the cap ≤ the 0.01 floor |
| Wrong culprit attribution | Owner-index alignment confirmed (ShapeValidate.cpp:112–134); −1 midpoint fallback; attribution unit tests; wrong culprits bounded by measured-to-target + the signature gate |
| AutoTune moving too much geometry | Deterministic full re-run (unchanged params ⇒ identical faces — unit-locked); only culprit maxExtends change; SelfIntersection check reverts bad escalations |
| Invalid gap-fill patches | Planarity/self-intersection/area gates; fan = tagged Warning, never silent; consolidation rebuild signature-gated |
| Retaining bad geometry | Clean-geometry source + IsRepresented/area/validity filters; raw path filter-only; 5c golden-master checkpoint |
| Performance blow-up | ≤6 native-op budget; benchmark asserted in 5f; managed hot path |
| Diagnostics lying (validation order) | Single `FinalizeAndValidate` producer of the final counts; mid-pipeline validates demoted to intermediate |
| Golden-master drift | Re-run at every sub-phase; stop-and-report protocol; raw-path deltas require explicit justification |
| Provenance loss in rebuild | Owner caution 2: compose rebuild history onto the existing map; backfill only unmapped faces, never overwrite |

## O. Final implementation prompt (for Opus 4.8 Max, one sub-phase at a time)

> You are implementing **Phase 5 (Diagnosis-Driven Closure)** of
> `docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md` in SAM_OCCT, branch `fix/solver-raw-first`.
> The binding design authority is `docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md` — where it and
> the plan differ, the review wins. **Verify that review document exists in the repo before writing
> any code; if it is missing, stop.**
>
> **Scope boundaries (binding owner cautions):** Managed-side only — use only existing native
> wrappers; no ABI v5, no new native entry points (ABI stays v4). No Grasshopper changes. No
> per-level frames (Phase 6). No spatial indexing / GlueMode tuning (Phase 9, but create the
> benchmark fixture). Do not widen OCCT fuzzy tolerance anywhere. Never fabricate or retain geometry
> silently — every fabricated/retained/rejected item gets a diagnostic. The consolidation rebuild
> must never discard existing SourceMap provenance: compose rebuild history when available;
> geometric backfill ONLY for faces left unmapped, never overwriting mapped entries.
> `CellIncreaseAdjacent` is conservative: adjacency of a new cell to a closed loop that cannot be
> positively proven ⇒ reject the AutoTune round + emit a diagnostic. Golden masters (10 signatures)
> must stay unchanged; any delta = STOP immediately and report the exact numbers; do not
> re-baseline; do not proceed.
>
> **Order:** implement sub-phases 5a → 5f exactly as specified in review §K, **one separate commit
> per sub-phase**, both suites green + golden masters re-run before moving on (stop immediately on
> any signature change). 5a: ClosureSignature3D sliver term (additive; `IsRegressionOf` semantics
> untouched) + managed-path Signature population + `MinPairSeparation` + `AttributeLoopsToSources` +
> determinism test. 5b: `GapFill.FromNakedWires` (planar patch / tagged fan fallback / residual
> warnings) + `FinalizeAndValidate` (consolidation rebuild via `Create.Shells` over
> resolved+patches+retained, signature-gated, provenance-preserving, final Validate LAST — the only
> source of the final naked count). 5c: `RetainDroppedV2` (clean geometry from SnapStage, map-driven
> dropped detection, IsRepresented/area filters, `DroppedRetained` provenance, SourceMap finally
> threaded at the call site; raw path = filtering only). 5d: `SewV2` (tolerance capped at
> min(0.1, 0.3, minSep×0.5), per-loop Closed/Persisting/New bookkeeping, fusion veto, whole-result
> acceptance with per-loop diagnostics) replacing ResolveStage's inline sew. 5e: `AutoTune3DSolver`
> (ladder [0.5,0.75,1.0,1.5] on attributed culprit sources only, ≤3 rounds, ForceManagedPipeline
> re-runs, `IsAcceptableRound` gate: naked strictly ↓ ∧ no regression ∧ sliver not ↑ ∧ proven
> new-cell adjacency, EscalateBucket default false, SelfIntersection revert, best-effort return) +
> `Modify.AutoTune3D` analytical wrapper via `PanelReconstruction`. 5f: seeded Benchmark1500
> generator + perf guard (<90 s, env-var skip seam) + TESTING.md Phase 5 section + plan status
> update.
>
> **Tests:** the unit + integration fixtures of review §J are required per sub-phase, xUnit
> `Method_State_Expected`, SPDX headers, Arrange/Act/Assert.
>
> **Stop and ask** when: a golden-master signature changes; the 90 s ceiling is blown after trying
> MaxRounds=2; native behavior contradicts the review's assumptions (e.g., wire owner order); or a
> sub-phase's acceptance cannot be met without widening scope.
>
> Sign commits: "Generated by Michal Dengusiak & Claude Code".

---

## Appendix — verified-today assertions (checked during this review, no code required)

- `HealStage.RetainDropped` re-adds extended geometry and its Execute call site passes no SourceMap
  (HealStage.cs:37, Panel3DSnapSolver.cs:462–466).
- `GapFill.NakedLoopFace3Ds` walks loops managed-side with tightest-turn junction resolution and a
  hard-coded 0.5 m anchor radius; non-planar loops silently fan-triangulate (GapFill.cs:19–142).
- `ClosureSignature3D.IsRegressionOf` = more naked OR fewer cells OR smaller total volume; no sliver
  term (ClosureSignature3D.cs:67–90).
- `OcctNakedWire.EdgeOwnerFaceIndices` aligns with the face list passed to `Query.Validate`
  (ShapeValidate.cpp:112–134); −1-tolerant by design.
- `sam_occt_sew_faces` captures no history (ABI v4 §F.4 scope cut, P3 design review).
- `Query.ShellsImprint` accepts shells only; no patch-face imprint entry point; imprint exposes no
  history (ShellsImprint.cs:24–44, sam_occt.h:119–123).
- `Query.ShellsDistance` accepts shells only; BRepExtrema-backed with closest points
  (ShellsDistance.cs:24–55).
- `Query.SelfIntersectionFace3Ds(this Face3D, double maxLength, double tolerance)` exists in
  SAM.Geometry (managed 2D check).
- 2D `AutoTuneSolver`: ladder [0.5,0.75,1.0,1.5], MaxRounds 6, gap-adjacent-only escalation, accept
  iff rooms not reduced AND (naked reduced OR rooms increased), atomic rollback on over-merge
  (SAM_Solver AutoTuneSolver.cs:57–257).
- `SolverContext` (per-source WeightOf/BucketSizeOf/MaxExtensionOf) exists and is unused by the
  managed pipeline.
- Golden masters at P4 HEAD `741c5b9`: flat 22/0, tilted-two-spaces 2/0, whole-level-tilted 22/0,
  two-level-tilted 43/0, towers 32/0 (raw); managed-path signatures recorded; unit 271/271,
  integration 108 pass / 1 skip.
