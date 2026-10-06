# P3 Design Review — Native History Export / ABI v4

Status: approved (GO WITH CHANGES) · Date: 2026-07-03 · Host branch: `fix/solver-raw-first`

Pre-implementation design review of Phase 3 (`docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md`
§E-P3, §F, §G, §H). Where this review and the plan differ, **this review wins**. Verified against
the repo as of Phase 2b completion: `sam_occt.h` (ABI v3, ShapeHandle.cpp:252),
`CellComplexBuilder.cpp`, `ShapeSew.cpp`, `ShapeValidate.cpp`, `ResolveStage.cs`, `SourceMap.cs`,
`Sew.cs`/`MergeCoplanar.cs`/`Shells.cs`, and the pinned OCCT **8.0.0** toolchain
(build-native.ps1:45, vcpkg baseline eb35a05).

Second-opinion corrections incorporated (binding): explicit array capacities on
`history_entries`; `edge_count` returned by `wire_info`; tolerance-drift acceptance re-based on
the stage tolerance budget for deliberate sew/heal fixtures; this review persisted to the repo
before implementation; P3 restated as strictly observational.

---

## A. Executive verdict

**GO WITH CHANGES.** The plan's core bets are right: `BRepTools_History` is the correct
abstraction, the ops are the right ones, additive ABI + probe/degrade matches the proven v3
pattern, and the 1-session USD spike with a documented fallback is the correct de-risking move.
But four design gaps must be fixed **before** code starts, or the phase will produce a history
nobody can join to the managed pipeline:

1. **Output-face identity is undefined in the proposal.** History entries must reference output
   faces by the **flat decode ordinal** (cell-major, face-minor enumeration of the result handle —
   the exact order the managed `SelectMany` walk already produces), translated natively by
   **TShape identity**, not by the quantized geometric `face_key` and not by raw `TopoDS_Shape`
   dumps.
2. **The naked-wire export is attached to the wrong handle.** `ShapeAnalysis_FreeBounds` already
   runs inside `sam_occt_shape_validate` (ShapeValidate.cpp:112). Wires belong on the
   **validation handle** as new accessors, not a new `sam_occt_result_naked_wires` on the result
   handle — zero recomputation, and it's the validate pass ResolveStage/AutoTune actually call.
3. **`OcctHistory` must be a pure managed snapshot** (arrays copied eagerly while the result
   handle is alive), not a new native handle type. This eliminates the entire disposal/soak-risk
   class the plan's acceptance criteria worry about.
4. **Composition must mirror ResolveStage's conditional hops.** The chain is not a fixed
   pipeline: pre-merge is skipped if it returns null/empty, the sew hop is adopted only when it
   strictly reduces naked edges, and a cells-empty build falls back to its own input
   (ResolveStage.cs:82–150). The managed composition must compose exactly the adopted hops — this
   is the #1 latent-bug site.

Sewing history needs a wrapper (`BRepBuilderAPI_Sewing` is not a `BRepBuilderAPI_MakeShape`; no
`History()`), and the ShapeFix heal chain inside `sam_occt_sew_faces` must be tracked through a
shared `BRepTools_ReShape` context (`History()` exists since OCCT 7.4). Both are spike items,
both have clean fallbacks.

## B. Current P3 design summary (what the plan proposes)

- ABI v4 additive exports: `sam_occt_result_history_counts/entries` (per input face: deleted
  flag, modified→output indices, generated→output indices) wrapping `BRepTools_History` composed
  across sew→MakerVolume→UnifySameDomain; `sam_occt_result_naked_wires` (FreeBounds wires);
  `sam_occt_shape_max_tolerance` (ShapeAnalysis_ShapeTolerance). Probe/degrade via
  `sam_occt_abi_version` (currently returns 3).
- Managed: `OcctHistory` in SAM.Core.OCCT; `SourceMap.Compose(OcctHistory)`; `NearestSourceIndex`
  demoted to kernel-absent/old-ABI fallback.
- Opens with a 1-session native spike on MakerVolume/Sewing/USD history on the OCCT 8.0 vcpkg
  baseline; USD-unreliable → managed same-plane merge mapping fallback.
- Out of scope: single-session native chaining (stretch, measured), CellsBuilder.

## C. What is correct

- **`BRepTools_History` as the composition vehicle** — it is OCCT's canonical cross-algorithm
  history (7.2+), faces are a supported type (`BRepTools_History::IsSupportedType`), and
  `Merge()` composes sequential ops natively. §B.4-7 of the plan already records this correctly.
- **MakerVolume history is the reliable anchor.** `BOPAlgo_MakerVolume` → `BOPAlgo_Builder` →
  `BOPAlgo_BuilderShape::History()`; history filling is default-on
  (`BOPAlgo_Options::SetToFillHistory`). BOP history is the most mature in OCCT: `Modified`
  covers splits *and* same-domain merges (two coincident input faces both report Modified → same
  result face), `IsDeleted` covers drops. This alone covers the splitting/deletion cases that
  matter most — which is exactly why the plan's USD fallback is viable.
- **Additive-only ABI + version probe** — matches the proven v3 `_ex` precedent; the managed
  layer already tolerates `EntryPointNotFoundException` (OcctNativeMethods.cs:35) and stamps
  `AbiVersionString` on every result.
- **Spike-first with a hard timebox and a pre-written pivot** — correct for the one genuine
  unknown (USD history quality on 8.0.0).
- **`ShapeAnalysis_FreeBounds` for wires** and **`ShapeAnalysis_ShapeTolerance` for drift** —
  right tools; FreeBounds is already the naked-edge ground truth in ShapeValidate.cpp, and
  ShapeTolerance's mode parameter gives max/avg/min in one API.
- **Not exposing CellsBuilder** — MakerVolume + `AvoidInternalShapes=false` already yields the
  zoned complex; correct scope cut.
- **`SourceMap.Compose(SourceMap)` algebra** — already implemented and unit-tested
  (SourceMapTests); the adapter approach `attempt.sourceMap.Compose(history.ToSourceMap())` is
  structurally sound.

## D. What is risky or wrong

1. **[WRONG] No output-face identity convention.** `sam_occt_result_history_entries` says
   "modified→output face indices" without defining the index space. The result handle addresses
   faces as `(cell_index, face_index)`; a shared internal wall appears in **two** cells
   (CellComplexBuilder.cpp:334–345 enumerates faces per solid), and the managed
   `shells.SelectMany(Face3Ds)` (ResolveStage.cs:101–103) therefore yields duplicates. The
   existing `face_key` is a **quantized geometric signature** (get_face_key,
   CellComplexBuilder.cpp:171–187) — collision-prone by design (it exists to *dedup* coincident
   faces) and wrong for identity. **Fix:** define the history output index as the **flat
   ordinal** in cell-major enumeration order (= the managed decode order), translate natively at
   decode time via a `TopTools_IndexedMapOfShape` TShape-identity lookup; a shared face maps to
   both its ordinals.
2. **[WRONG] Sewing has no `History()`.** `BRepBuilderAPI_Sewing` provides only
   `Modified()/ModifiedSubShape()/IsModified*()` (1:1, faces never split). The plan says "via
   ModifiedSubShape/wrapper" — correct but underspecified: the wrapper must hand-build a
   `BRepTools_History` (AddModified per input face). Additionally `sam_occt_sew_faces` is a
   **chain** (Sewing → ShapeFix_Wireframe → ShapeFix_Shell → ShapeFix_Solid → USD,
   ShapeSew.cpp:109–234): the ShapeFix links replace shapes through a
   `ShapeBuild_ReShape`/`BRepTools_ReShape` context, whose `History()` (7.4+) must be merged in,
   or the sew-stage mapping silently breaks whenever a fixer rebuilds a face. Spike item S3.
3. **[RISK] USD history quality on 8.0.0** — known-buggy historically (mostly edge history; face
   merge history generally OK ≥7.4). Plan already mitigates (spike + managed same-plane
   fallback). Keep.
4. **[RISK] Conditional-hop composition.** ResolveStage adopts hops conditionally (pre-merge only
   if non-empty; sew only if naked count strictly decreases; empty cells → identity fallback). A
   naïve "compose all four hops" produces a map that disagrees with the faces actually returned.
   The managed design must compose **per adopted hop**, at the ResolveStage level, not inside the
   wrappers.
5. **[RISK] `Provenance` cannot express deletion.** `enum Provenance { Input, Snapped,
   Conditioned, Resolved, DroppedRetained, GapFill }` — an input with no record is ambiguous
   (deleted vs history-gap). Plan §E-P3 requires history-gap to be "a warning + heuristic
   fallback, never silent", so the two must be distinguishable. **Fix:** deletion lives on
   `OcctHistory` as an explicit `DeletedInputs` set (no SourceMap schema change); the adapter
   emits a `HistoryGap` diagnostic for any input that is neither mapped nor deleted, and a
   reverse-gap diagnostic for any output ordinal no input maps to.
6. **[RISK] New native handle type = new lifetime-bug class.** The plan's acceptance already
   fears this ("every native result/history handle is disposed"). Don't create the class: copy
   history to managed arrays eagerly; no `OcctHistory` finalizer, nothing to leak.
7. **[MINOR] `sam_occt_shape_max_tolerance` alone doesn't fit the solver's flow.** The solver
   path uses one-shot face-list entry points that free their shape internally
   (`sam_occt_build_cell_complex`, `sam_occt_merge_coplanar`, `sam_occt_sew_faces`+decode). A
   shape-handle-only tolerance export can't measure those stages. **Fix:** also store max/avg
   tolerance on the result at op time (`sam_occt_result_max_tolerance`).
8. **[MINOR] Wire export placement** — see A.2; putting it on the result handle would force a
   second FreeBounds run and a second shape build per validate.

## E. Recommended ABI v4 function list and signatures

All: `extern "C"`, Cdecl, additive, `sam_occt_abi_version()` returns **4**. Status codes follow
the existing convention (0 ok, 10 null out-pointer, 11 bad input/capacity, 40 index out of range,
50 invalid handle, 99 exception). No new handle types; no caller-freed memory; all data returned
through caller-allocated out-params (existing idiom, P/Invoke-safe, no marshaller ambiguity).

History (on the existing result handle; captured internally by the ops):

```c
/* 1 = history captured for this result, 0 = not captured (op predates v4 logic
   or history disabled), -1 = invalid handle. */
int sam_occt_result_history_available(void* result_handle);

/* Number of input faces the producing op saw (the caller's flattened-array
   face order). -1 invalid handle, 0 when unavailable. */
int sam_occt_result_history_input_count(void* result_handle);

/* Per-input-face record sizes. deleted: 1/0. Status: 0/10/40/50. */
int sam_occt_result_history_face(void* result_handle, int input_index,
    int* deleted, int* modified_count, int* generated_count);

/* Fills caller-allocated arrays with FLAT OUTPUT ORDINALS (cell-major, face-minor
   enumeration of this result — identical to the managed decode order; a face
   shared by two cells has two ordinals and appears under both). Capacities are
   explicit: native never assumes caller buffer size, writes at most *_capacity
   entries, and fails with status 11 when a capacity is smaller than the
   corresponding count. Arrays may be null only when the corresponding count is
   0. Status: 0/10/11/40/50. */
int sam_occt_result_history_entries(void* result_handle, int input_index,
    int* modified_ordinals, int modified_capacity,
    int* generated_ordinals, int generated_capacity);
```

Naked wires (on the existing validation handle; FreeBounds already computed there):

```c
/* Number of free-bound wires (closed + open). -1 invalid handle. */
int sam_occt_validation_wire_count(void* validation_handle);

/* point_count = polyline vertices (closing vertex NOT duplicated); edge_count
   returned explicitly (= point_count for closed wires, point_count - 1 for
   open wires) so polyline building and edge-owner indexing never guess the
   convention. is_closed 1/0. Status: 0/10/40/50. */
int sam_occt_validation_wire_info(void* validation_handle, int wire_index,
    int* point_count, int* edge_count, int* is_closed);

/* Ordered polyline vertex. Status: 0/10/40/50. */
int sam_occt_validation_wire_point(void* validation_handle, int wire_index,
    int point_index, double* x, double* y, double* z);

/* Owning face of wire edge i (the single face a free edge bounds), as an index
   into the face list the caller passed to sam_occt_shape_validate's shape —
   resolved through the internal sew's 1:1 ModifiedSubShape map; -1 when
   unknown. Optional accessor: managed code must tolerate -1. Status: 0/10/40/50. */
int sam_occt_validation_wire_edge_owner(void* validation_handle, int wire_index,
    int edge_index, int* input_face_index);
```

Tolerance drift:

```c
/* Live shape handles (persistent-topology paths). subshape_type: 0 any,
   1 vertex, 2 edge, 3 face. Status: 0/10/50/99. */
int sam_occt_shape_max_tolerance(void* shape_handle, int subshape_type,
    double* max_tolerance, double* average_tolerance);

/* Stored at op time for the one-shot face-list ops the solver actually uses.
   Status: 0/10/50; max/avg are 0 when the op predates capture. */
int sam_occt_result_max_tolerance(void* result_handle,
    double* max_tolerance, double* average_tolerance);
```

**Explicitly not added:** history on shape-handle ops (stretch; input-ordinal convention
documented as TopExp face order if ever needed), CSR bulk-dump variants (per-item accessors match
the `sam_occt_validation_issue` idiom; ≤ ~4k calls at 2k faces is noise), `sam_occt_session_*`
chaining (Phase 3 stretch, only on measurement), CellsBuilder.

**Ownership/lifetime:** history and tolerance data are owned by the result/validation handle and
freed with it (`sam_occt_free_result`/`sam_occt_free_validation`). No new free functions. Wires
owned by the validation handle.

**Fallback:** managed probes `AbiVersion >= 4` once; on v3 (or `EntryPointNotFoundException`)
every new accessor path degrades to `OcctHistory == null` → composition skipped →
`NearestSourceIndex` heuristic as today. `history_available == 0` must degrade identically
per-result.

## F. Recommended native implementation strategy

1. **Capture inside the three one-shot entry points the solver uses** —
   `sam_occt_build_cell_complex(_ex)`, `sam_occt_merge_coplanar`, `sam_occt_sew_faces`
   (+`sam_occt_shape_decode` when sew returns a handle first — see 4):
   - While building input faces from the flattened arrays, append each `TopoDS_Face` to a
     `TopTools_IndexedMapOfShape` (index = input ordinal).
   - Run the op with history: MakerVolume → `History()`; merge-coplanar → hand-built sew map
     merged with `USD.History()`; sew chain → hand-built sew map + shared `ReShape` `History()` +
     `USD.History()`, merged via `BRepTools_History::Merge`.
   - At decode, build a TShape-identity map from result faces → flat ordinals (a shared face →
     both ordinals). For each input ordinal: `IsRemoved/IsDeleted` → deleted; else
     `Modified(face)` (and `Generated(face)`) → translate each returned face to ordinals. Store
     the per-input records + max/avg `ShapeAnalysis_ShapeTolerance` on the existing `Result`
     struct. New `src/History.cpp` holds the wrapper/merge/translate helpers.
2. **Same-domain merge nuance:** after MakerVolume, two coincident inputs both report `Modified`
   → the same ordinal — that IS the merge record; no special casing.
3. **Generated:** keep in the ABI for future ops but expect empty for faces under BOPs (BOPs
   generate edges/vertices from faces, not faces). Document honestly.
4. **Sew returns a shape handle then decodes** (`TrySew` → `sam_occt_sew_faces` → decode): carry
   the pending history + the input map inside the (extensible-by-design, sam_occt.h:31)
   shape-handle struct; `sam_occt_shape_decode` transfers and translates it onto the result. If
   that plumbing exceeds the session, scope v4 history to `build_cell_complex` +
   `merge_coplanar` first — they cover pre-merge, build, and post-merge; the sew hop is
   1:1-dominant and can fall back to identity mapping with a diagnostic.
5. **Wires:** in `sam_occt_shape_validate`, keep the existing `ShapeAnalysis_FreeBounds` call;
   additionally walk `GetClosedWires()/GetOpenWires()` compounds, store ordered vertex polylines
   + closed flags + per-edge owning face (via `TopExp::MapShapesAndAncestors(EDGE→FACE)` on the
   validated shape, mapped back through the internal sew's `ModifiedSubShape` where possible,
   else -1).
6. **Threading/exceptions:** same posture as every existing export — catch-all → 99,
   single-thread handles.

## G. Recommended managed API / P/Invoke shape

- `OcctHistory` (colocate with `OcctCellComplexResult` in the Geometry.OCCT native layer — it
  consumes result handles; keep the plan's type name):
  - **Pure managed snapshot**: `int InputCount; IReadOnlyList<int> DeletedInputs;
    IReadOnlyList<IReadOnlyList<int>> ModifiedOrdinals; ... GeneratedOrdinals` — fetched eagerly
    right after the op while the handle is alive. No finalizer, no `IDisposable`, nothing to
    soak-leak.
  - Fetched by the existing wrapper methods (`TryBuild`/`TryMergeCoplanar`/`TrySew`) into a new
    optional property on `OcctCellComplexResult` — signatures gain nothing; ResolveStage reads
    `result.History`.
  - `ToSourceMap(Provenance.Resolved)` adapter: source = input ordinal, `FaceKey` = output flat
    ordinal. Emits `SolverDiagnostics` entries: `HistoryGap` (input neither mapped nor deleted →
    warning + that input falls back to heuristic) and reverse-gap (output ordinal with no source
    → warning; candidate for fabricated/heuristic).
- P/Invoke: `[DllImport("SAM.Occt.Native", CallingConvention = Cdecl)]` +
  `EntryPointNotFoundException` guard — existing idiom exactly; pre-sized `int[]` buffers, no
  custom marshalling.
- `Query.Validate` overload surfaces `NakedWirePolyline3Ds` (+ per-edge owner indices where ≥0)
  on `OcctValidationReport`.
- ABI probe: one cached `AbiVersion >= 4` check in `OcctNativeMethods`, mirroring
  `AbiVersionString`.

## H. SourceMap composition strategy

Per adopted hop, in ResolveStage (owner of adoption decisions):

```text
map = attempt.sourceMap                                  # clean-face keyed (FaceKey = list index)
if preMerge adopted:   map = map.Compose(preMergeHistory.ToSourceMap())
map = map.Compose(cellBuildHistory.ToSourceMap())        # MakerVolume — always
if postMerge adopted:  map = map.Compose(postMergeHistory.ToSourceMap())
if sew adopted:        map = map.Compose(sewHistory.ToSourceMap())
```

**Invariant to document and assert (debug-only):** a stage's history output ordinals index the
same list the managed decode produced and fed to the next hop — i.e., composition is only valid
because decode order == SelectMany order == next hop's input order. One unit test locks this
invariant with a fake 2-cell shared-face result.

Edge cases:

- **1→N split:** input maps to N ordinals — `Compose` already fans out.
- **N→1 merge:** N inputs map to the same ordinal — `SourcesOf` returns all.
- **Deleted:** in `DeletedInputs`; excluded from the map; `ClosureSignature3D.DroppedCount` =
  deleted + unmapped (distinguished in diagnostics).
- **Generated with no source:** reverse-gap diagnostic; face gets `NearestSourceIndex` locally
  (never silent).
- **Retained dropped face:** managed re-add after resolve — recorded managed-side as
  `DroppedRetained` (Phase 5 semantics unchanged).
- **Gap-fill fabricated:** `RecordFabricated` — orthogonal, unchanged.
- **Native reorder:** immune — ordinals are per-handle enumeration order, translated by TShape
  identity, never positional across calls.
- **USD merges after MakerVolume:** that is the *post-merge hop's own history*, composed as its
  own step (and USD inside one entry point is merged natively via `BRepTools_History::Merge`).
- **ABI v3 / no history:** `OcctHistory == null` → the hop is skipped entirely (NOT composed as
  identity) and the final `BuildPanels` keeps the `NearestSourceIndex` heuristic for faces the
  composed map cannot resolve (exactly today's behavior). Degrade is *per result*, so a v4 build
  with one history-less op still uses history for the other hops.

## I. Naked-wire and tolerance export strategy

- **Wires:** validation handle, ordered polylines + closed flag + optional per-edge owner
  (−1-tolerant). This feeds all four consumers: diagnostics (points already there), AutoTune3D
  (`AttributeLoopsToSources` = owner face → SourceMap → culprit sources; falls back to managed
  midpoint-on-face matching when owner = −1), GapFill v2 (ordered loops directly, killing the
  managed loop-walk), GH display (polylines). Edge tolerances per wire: **not** in v4 —
  `sam_occt_result_max_tolerance` covers the drift signal; per-edge tolerance is YAGNI until
  AutoTune shows it needs it.
- **Tolerance:** max + average, both handles (§E). Worst-sub-shape location: defer — the
  validation handle's issue locations already localize problems; add later only if
  ToleranceDrift diagnostics prove blind.

## J. Minimum native spike plan (1 session, timeboxed — GO/NO-GO per item)

Throwaway gated integration tests (or a scratch native exe), on the pinned 8.0.0 baseline:

| # | Proves | Pass criterion |
|---|---|---|
| S1 | MakerVolume history | wall × crossing floor: wall `Modified` → 2 result faces; dropped sliver → `IsDeleted` |
| S2 | Sewing mapping | 2 near-touching faces: `ModifiedSubShape` non-null per input; hand-built `BRepTools_History` round-trips |
| S3 | ShapeFix chain tracking | shared `ReShape` context through Wireframe/Shell/Solid compiles on 8.0 and `History()` maps a rebuilt face |
| S4 | USD history | 2 adjacent coplanar faces: both `Modified` → same output face |
| S5 | Cross-op merge | `BRepTools_History::Merge`(sew, USD) equals manual composition |
| S6 | FreeBounds wires | open box (5 faces): 1 closed wire, 4 ordered edges, owner faces resolvable via MapShapesAndAncestors |
| S7 | ShapeTolerance | max/avg on a sewn-at-0.05 shape ≥ input tolerance and sane |

S1, S6, S7 are near-certain (mature APIs). S3/S4 are the real unknowns. **S4 fails →** pivot
(already in plan §O-1): run merge-coplanar before history-critical ops, map merges managed-side
by same-plane grouping; MakerVolume+sew history still covers split/delete. **S3 fails →** sew hop
degrades to 1:1-by-`ModifiedSubShape`-only with a `HistoryGap` diagnostic when a fixer rebuilt a
face. Record results in the plan doc either way.

## K. Required tests and fixtures

Unit (`Testing/SAM.OCCT.UnitTests`, pure managed):

- `OcctHistory_ToSourceMap` algebra: split, merge, deleted, deleted-then-regenerated chain,
  gap → diagnostic.
- `SourceMap_ComposeWithHistory_SharedFaceTwoOrdinals` — the decode-order invariant lock.
- v3 fallback: null history → composition skipped, no throw, heuristic path taken.

Integration (`Testing/SAM.OCCT.IntegrationTests`, native-gated):

- **Split fixture:** wall crossed by floor → history 1→2; composed SourceMap agrees with
  geometry.
- **Merge fixture:** two overlapping coplanar walls → 2→1.
- **Deleted fixture:** sliver/degenerate face dropped by MakerVolume → `DeletedInputs`,
  `DroppedCount` correct.
- **Generated/no-source fixture:** assert the reverse-gap diagnostic fires (or that no such face
  exists on the fixtures — either outcome recorded).
- **Naked-wire fixture:** open box → 1 closed wire, ordered polyline, closed flag; owner
  attribution where available.
- **Tolerance-drift fixture (two-part):** (a) non-healing op (e.g. merge-coplanar on clean
  input) → `result_max_tolerance` stays within the drift ceiling (< 10× input tolerance — the
  plan's bound applies here); (b) deliberate sew/heal at 0.05 → max/average tolerance is
  reported, finite, non-negative, and ≤ the active sew tolerance × a safety factor. A 0.05 m sew
  legitimately inflates tolerance far beyond 10× a 1e-6 input — for healing ops the stage
  tolerance budget, not the input tolerance, is the bound.
- **Old-ABI fallback:** ABI-v3 stub (or a forced-version test seam) → full solve green,
  `NearestSourceIndex` used, no new symbols touched.
- **Soak:** 50 successive solves of a fixture → private-bytes plateau (< 10% delta runs 10→50) —
  covers result/validation handles; `OcctHistory` has no native lifetime by design.
- **Golden masters:** all 5 fixtures unchanged (history capture must not perturb geometry).

## L. Risks and mitigations

| Risk | Mitigation |
|---|---|
| USD history unreliable on 8.0.0 | Spike S4 + documented managed same-plane fallback (plan §O-1) — pivot immediately, don't debug OCCT |
| ShapeFix rebuilds faces untracked | Shared ReShape `History()` (S3); degrade sew hop to ModifiedSubShape-only + `HistoryGap` diagnostic |
| Ambiguous ownership after merge | By design: N sources → 1 ordinal is the record; Phase 4's dominant-source Guid policy consumes it |
| Generated faces with no source | Reverse-gap diagnostic + local heuristic; never silent (plan requirement) |
| ABI memory-ownership bugs | No new handle types, no caller-freed memory, out-param idiom with explicit capacities; history freed with its result handle; soak test |
| Performance | BOP history fill is already default-on today; USD/sew maps are O(faces); FreeBounds already computed in validate; accessors trivial. Expected net ≈ 0; assert no golden-master perf regression |
| Face identity instability | TShape-identity translation at decode + per-handle ordinals; never geometric matching, never cross-handle indices |
| Composition drift vs conditional hops | Composition lives in ResolveStage next to the adoption decisions; invariant unit-locked |
| v3/v4 skew in the field | Single cached probe + per-call EntryPointNotFound guard + `history_available` per-result flag |

**P3 is strictly observational:** history capture, naked-wire export, and tolerance export must
not change solver geometry. Golden-master signatures must remain unchanged; if a test reveals an
existing non-determinism, stop and report rather than re-baselining.

---

Verified-today assertions (checked during this review, no code required): ABI is 3
(ShapeHandle.cpp:252), FreeBounds already runs in validate (ShapeValidate.cpp:112), the sew entry
point ends in UnifySameDomain (ShapeSew.cpp:234). Every other OCCT-behavior claim above is
checked empirically by spike S1–S7.
