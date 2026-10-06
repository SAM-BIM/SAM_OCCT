# PR #49 → E-track — Robust Extend3D: Implementation Handover

`SAM-BIM/SAM_OCCT` · supersedes branch `feature/extend3D` (PR #49, WIP commit `26ac05a`)
Status: approved 2026-07-07

File note: phases E1–E3 in this document are the *robust-Extend3D phases*. They interleave with
the CellComplex-first phases P1–P5 of `docs/CELLCOMPLEX_FIRST_HANDOVER.md`; the combined order is
pinned in §A below. E1 lands **inside PR #48**; E2/E3 are follow-up PRs. PR #49 is closed as
superseded once E1 lands (its three profile-preservation unit tests are cherry-picked into E1).

**Core rule (stated once, applies everywhere):**
Extend3D is the *controlled pre-conditioner*: it must grow panels toward their real neighbours
while preserving each panel's profile (non-rectangular boundaries, holes) and plane — never
rebuild a panel as a flat rectangle. Workflow B (`Clean3D → Extend3D → CreateAdjacencyCluster`)
and the managed fallback of `Solve3D` both stand on these primitives. **Raw goldens stay
byte-identical in every E phase; managed movement is allowed only via an explicit re-baseline
table, and only in E2.**

---

## A. Decision record (summary — do not re-litigate)

- **Sequencing (owner decision 2026-07-07):** combined into the CellComplex-first program:
  **P1 → E1 (inside PR #48) → P2 → merge #48 → E2 → {P3 ∥ E3-after-E2} → P4 → P5.**
  Rationale: P1's `WorkflowParityIntegrationTests` harness is the before/after evidence machine
  for the extend fix; E1 is engineered to keep every golden pin frozen so it may live in #48
  without violating merge-checklist item 7 (no golden re-baselines in #48); E2 *does* move
  managed pins, so it is quarantined as the first post-merge PR; **P4 is blocked until E2
  merges** (gate hardening pushes more input onto the managed pipeline, so the managed extend
  quality must improve first); E-track settles before P5 so P5's re-baseline stays single-cause.
- **Mechanism (owner decision 2026-07-07): plane-ops rebuild.** Extend = union the face up to a
  target plane; trim = cut the face by a plane. Reuses SAM-core `Query.Extend(Face3D, Plane)`
  (`SAM\SAM\SAM.Geometry\Geometry\Spatial\Query\Extend.cs:11`) and `Query.Cut(Face3D, Plane,
  out above, out below)` (`...\Spatial\Query\Cut.cs:44`). The WIP edge-move implementation
  (`ExtendBoundaryEdgeToZ`/`BoundaryEdgeIndex`/`MovePlanEndEdge`/`ReplaceExternalBoundary` in
  `26ac05a`) is **not** ported — see risk register §C — but its three new unit tests are kept
  as acceptance targets.
- **Diagnosed defect being fixed:** `SnappedPanel.ExtendTopTo`/`ExtendBottomTo`/
  `ExtendHorizontal`/`SetVerticalFootprint` re-extrude a flat rectangle from `GetBaseSegment`,
  collapsing sloped-base / shifted-top / gable walls — the "walls disappear after
  SAMOCCT.Extend3D" bug of PR #49.
- Per-panel *control* already exists (`SolverParameter.MaxExtend` read off each panel, AutoTune
  `MaxExtendLadder`). The E-track adds what is missing: robustness (E1), target quality (E2),
  visibility (E3).

## B. State of the art (reviewed 2026-07-07; why this design)

Four approach families for closing gaps in a planar-panel soup before topology extraction:

1. **Kernel tolerance** — fuzzy booleans / `BOPAlgo_MakerVolume` fuzzy + glue, tolerant sewing.
   Already SAM's raw-first ladder (`AvoidInternalShapes=false, SewBeforeBuild=true,
   SewingTolerance=0.01`, AutoTune escalation). Right tool for *small* gaps; large fuzzy values
   fuse real double walls (guarded today by `MinPairSeparation`). Gaps of 0.1–0.5 m — the
   real-Revit-model case — need geometric conditioning first. **Kept as-is.**
2. **Local target-driven extension to neighbour planes** — the CAD "extend surface to surface"
   idiom; standard practice in BIM→BEM space-boundary generation and healing (CBIP algorithm,
   LBNL BIM→BEM geometry transformation, space-boundary simplification literature — refs below).
   Preserves panel identity and provenance; degrades gracefully (an unextended panel is a
   diagnosed open end, not a destroyed panel). **Adopted — this is the E-track.**
3. **Global plane-arrangement + cell selection** — build the arrangement of all supporting
   planes, choose interior cells by optimization: PolyFit (Nan & Wonka, ICCV 2017), Kinetic
   Shape Reconstruction (Bauchet & Lafarge, ACM TOG 2020), Concise Plane Arrangements (ECCV
   2024), ArrangementNet (SIGGRAPH 2023), volumetric indoor reconstruction (Ochmann et al.).
   Watertight by construction, but destroys panel identity (SAM needs `SourceMap`/aperture
   provenance), over-splits at every plane crossing, and scales poorly. SAM's
   MakerVolume-over-*conditioned*-panels is exactly the bounded, provenance-keeping middle
   ground. **Not adopted as the main path** — but it is why extend targets should be *real
   neighbour planes* (family 2 borrows family 3's key insight locally).
4. **Non-manifold topology libraries** — Topologic `CellComplex.ByFaces` (Jabi et al.).
   In-house prior art (`SAM_Topologic`); its tolerance fragility on dirty input is the reason
   SAM_OCCT exists. **Not adopted.**

References: [Kinetic Shape Reconstruction](https://dl.acm.org/doi/abs/10.1145/3376918) ·
PolyFit (Nan & Wonka, ICCV 2017) ·
[Concise Plane Arrangements](https://arxiv.org/pdf/2404.06154) ·
[ArrangementNet](https://dl.acm.org/doi/10.1145/3592122) ·
[Fully volumetric building reconstruction](https://arxiv.org/pdf/1907.00631) ·
[LBNL BIM→BEM geometry transformation](https://simulationresearch.lbl.gov/sites/all/files/lbnl-6033e.pdf) ·
[CBIP space-boundary algorithm](https://www.researchgate.net/publication/259633402_An_Algorithm_to_generate_space_boundaries_for_building_energy_simulation) ·
[Space-boundary topology simplification](https://www.researchgate.net/publication/335676310_Space_Boundary_Topology_Simplification_for_Building_Energy_Performance_Simulation_Speedup) ·
[BEM geometry from BIM volumes (2026)](https://www.sciencedirect.com/science/article/pii/S0926580526000816) ·
[Topologic](https://github.com/wassimj/Topologic) / [NMT for design–simulation linking](https://www.tandfonline.com/doi/full/10.1080/00038628.2015.1117959)

## C. Risk register — why the WIP edge-move implementation is superseded

All verified in code on `feature/extend3D` (`26ac05a`) and current `fix/solver-raw-first`.
E1's reviewer re-checks that none of these can recur in the plane-ops implementation.

| # | Verified defect |
|---|---|
| R1 | Gable top has two slope edges; `BoundaryEdgeIndex` picks one by iteration order (strict `>` on equal means) and translates it rigidly — shears the shared apex, strands the other slope below target, returns `true`. |
| R2 | M-shaped / stepped tops: only the extreme-mean edge moves; bbox `Max.Z` reaches target so the call reports success while the rest of the top chain stays short. |
| R3 | Sloped single top edge is translated rigidly — its low end lands at `targetZ − span`, still short under a differently-sloped roof. |
| R4 | `SetVerticalFootprint` measures current extent on the *foot* (longest plan edge) but moves the extreme-mean *boundary* edge — on stepped ends the two disagree; target not reached, returns `true`. |
| R5 | Walls up to 20° off vertical count as vertical (`ToleranceBudget.VerticalAngle`), but `MovePlanEndEdge` translates by world-plan `(ux,uy,0)` — out of plane; `Polygon3D(IEnumerable<Point3D>)` then silently refits a best-fit plane (`SAM\...\Spatial\Classes\Polygon3D.cs:23-36`) and the wall's plane rotates. |
| R6 | Hole loss: `Face2D.Create` silently skips internal edges not fully `Inside` the new boundary (`SAM\...\Planar\Classes\Face2D.cs:124-133`); `Face3D.Create(loops)` picks the max-area loop as external (`Face3D.cs:401-414`) — a heavily-glazed wall trimmed small enough promotes its window to the external boundary. |
| R7 | WIP `GetBaseSegment` returns the longest plan edge's own span, not the wall's full plan extent — `ExtendWalls`/`OpenWallEnds` (`Panel3DSnapSolver.cs:1242/1371`) then feed a wrong foot-line to the 2D `ExtensionSolver`; parallelogram base/top tie resolved by point order. |
| R8 | `Extend` targets `nearestCap.Max.Z + roofOvershoot` (ridge + 0.5, `Panel3DSnapSolver.cs:1495-1537`) — always above the apex, so an already-roof-conforming gable gets "extended" (and, under R1, mangled). Fixed properly in E2 (no-op when conforming). |
| R9 | Managed blast radius: any primitive change can move `Solve3D_ManagedPath_ClosureSignatureMatchesGoldenMaster` plus every `ForceManagedPipeline`/`StopAfterExtend` test — full list in the shared context block. |
| R10 | The WIP's three new tests assert bbox spans / point counts only — they pass under R1/R3 shear. E-track tests must assert *shape* (both slopes preserved, plane normal unchanged, exact target parameter). |

---

# E-track phases, outcomes and review records

## 1. Technical context

```
Call graph (verified):
- RAW path never touches the extend primitives: Panel3DSnapSolver.Execute (Panel3DSnapSolver.cs:394)
  gates on TryRawResolve (:2138) which only calls the native kernel. Raw goldens are immune.
- MANAGED path (raw failed, ForceManagedPipeline, StopAfterClean or StopAfterExtend) reaches them
  via ConditionStage.Condition (ConditionStage.cs:60/66/72):
    ExtendWalls (Panel3DSnapSolver.cs:1224)  -> GetBaseSegment + SetVerticalFootprint
                                                (plan loop closed by 2D ExtensionSolver, :1280)
    Extend      (Panel3DSnapSolver.cs:1452)  -> ExtendTopTo / ExtendBottomTo
                                                (targets from CapZAtPlan :1583 + overshoot)
    Fill        (Panel3DSnapSolver.cs:1605)  -> GrowOutwardTo / GrowOutward (caps to walls, measured)
  Plus OpenWallEnds (:1352) -> GetBaseSegment (diagnostic only).
- Primitives live in SAM_OCCT/SAM.Geometry.OCCT.Solver/Classes/SnappedPanel.cs:
  GrowOutward :486, GrowOutwardTo :531, ExtendTopTo :639, ExtendBottomTo :684,
  GetBaseSegment :735, ExtendHorizontal :769 (ZERO production callers), SetVerticalFootprint :830.
- Analytical facade: SAM_OCCT/SAM.Analytical.OCCT.Solver/Modify/Solve.cs — Extend3D :344/:366
  (StopAfterExtend=true, native-free), OpenPanels3D :474, Clean3D :256/:277 (StopAfterClean; never
  reaches the extend primitives). GH: SAMOCCTExtend3D.cs (calls Extend3D :191, OpenPanels3D :222).
  Workflow B = Clean3D -> Extend3D -> CreateAdjacencyCluster. Extend3D output must NEVER be piped
  into Solve3D (it re-extends internally) — preserve this doc'd rule.
- SAM-core primitives to reuse (do not reinvent): Query.Extend(Face3D, Plane, tolAngle, tolDist)
  (SAM\SAM\SAM.Geometry\Geometry\Spatial\Query\Extend.cs:11 — extends a face to an INFINITE plane,
  carries holes via Face3D.Create(plane, polygon2D, internalEdge2Ds)); Query.Cut(Face3D, Plane,
  out above, out below) (Query\Cut.cs:44); Create.PlanarIntersectionResult(Plane, Plane, tol)
  (Create\PlanarIntersectionResult.cs:184).

Managed blast radius (run ALL of these when primitives change):
GoldenMasterIntegrationTests.Solve3D_ManagedPath_ClosureSignatureMatchesGoldenMaster (:156,
ManagedFixtures :145) and Solve3D_RawPath_... (:112, must NEVER move); ExecuteResetIntegrationTests;
DroppedRetainIntegrationTests; Solve3DReportIntegrationTests; DeterminismIntegrationTests;
SourceMapMappingIntegrationTests; SolverCellIntegrationTests; CreateSpacesIntegrationTests (managed
refusal cases); Clean3DDiagnosticsIntegrationTests.Extend3D_HomeFixture_... (native-free);
StageReportIntegrationTests (native-free); Panel3DSnapSolverTests incl. Execute_StopAfterExtend_...
Managed pins today (broken, fixed only in E2's re-baseline): two-level-tilted 29 cells/29 naked;
whole-level-towers 22 cells/12 naked; healthy managed pins: whole-level-flat 22/0 (vol 3479.90),
tilted-two-spaces 2/0 (723.65), whole-level-tilted 22/0 (3377.83).
P1 harness: Testing/SAM.OCCT.IntegrationTests/WorkflowParityIntegrationTests.cs runs ALL 9 fixtures
through workflow A and workflow B with per-fixture table in TESTING.md; expected-fail rows assert
CURRENT wrong values with tracking comments. E-phases that flip a row update the pinned value +
tracking comment, citing the mechanism.
```

## 2. Phase table E1–E3 (and where they sit among P1–P5)

Combined order: **P1 → E1 → P2 → merge #48 → E2 → {P3 ∥ E3-after-E2} → P4 → P5.**

| Phase | Scope (one line) | Branch / PR | Merge condition |
|---|---|---|---|
| E1 | Plane-ops rebuild of the extend/trim primitives (profile + hole preserving), scalar targets unchanged | `fix/solver-raw-first` / **PR #48** (after P1) | **DONE (2026-07-07).** Raw goldens byte-identical; the fast path could NOT hold the managed pins (structural — the golden fixtures contain the non-rectangular walls E1 fixes, so the managed tripwire and workflow B share code+geometry). Per owner decision, three managed pins re-baselined in #48 with a mechanism table (TESTING.md "E1"); two unchanged. Workflow-B harness rows re-pinned. See §3 note. |
| E2 | Plane-intersection cap targets (walls extend to actual roof/floor planes; gable policy; no-op when conforming); managed re-baseline | new `feat/extend3d-plane-targets` off `sow/2026-Q3` / new PR | Review approves; managed re-baseline table row-by-row justified via P1 harness; naked count must not increase on ANY fixture in either workflow; raw goldens byte-identical; **unblocks P4** |
| E3 | Per-panel extend observability (which edge, how far, toward what), workflow-B pins for the two new fixtures, GH polish, docs | new `feat/extend3d-observability` off `sow/2026-Q3` (after E2) / new PR | Review approves; zero behaviour change (all goldens + E2 pins byte-identical); TESTING.md E-track section committed |

## 3. E1 — scope, acceptance and outcome

**Scope.** Replace the flat-rectangle re-extrusion inside `SnappedPanel`'s extend/trim primitives with plane-ops (union-to-plane / cut-by-plane) so non-rectangular walls survive extension. Same scalar targets as before — target *policy* changes are E2. Confined to the `SnappedPanel.cs` primitives, unit tests and workflow-B harness row updates; supersedes WIP commit `26ac05a` (PR #49), whose three profile-preservation unit tests are cherry-picked as acceptance targets (its implementation is not ported — see risk register R1–R7).

- **Characterization first:** unit-test SAM-core `Query.Extend(Face3D, Plane)` union semantics on a gable and an M-top wall; if unsuitable, build the in-plane extension band explicitly (2D frame + `Planar.Query.Union`). Record the choice in the PR.
- **`ExtendTopTo` / `ExtendBottomTo` classification ladder:** (a) rectangular hole-free vertical face → legacy base-segment re-extrusion, numerically identical to before; (b) any other vertical-planar face → extend to the horizontal plane at the target Z (flat top/bottom at the target across the current plan span; rest of the boundary, all holes and the supporting plane preserved); (c) already reaching the target → return false, no mutation.
- **`SetVerticalFootprint`:** per-end decomposition — lengthen via the extend mechanism, shorten via `Query.Cut` keeping the piece that contains the foot midpoint. A hole clipped or dropped by the trim emits the coded diagnostic `SAM_OCCT_EXTEND3D_HOLE_DROPPED` (never silent — R6). Results are built only via the explicit external/internal `Face3D.Create` overload, never the loops overload (R6).
- **`GetBaseSegment`:** direction = longest plan-projected external edge (tie-break: longer, then lower mean Z, then lower index); extent = min/max parameter of ALL external boundary points along that direction (R7); endpoints at bbox Min.Z.
- **`ExtendHorizontal`:** zero production callers — removed (or delegated to `SetVerticalFootprint`).
- **Tests (pure managed, shape-asserting — R10):** dedicated `ExtendBottomTo` coverage; `SetVerticalFootprint` lengthen / shorten / shorten-through-a-window (diagnostic asserted); gable extended to target keeps BOTH slopes below a flat top; M-top fully reaches the target; stepped end reaches the exact target plan parameter; a wall with a window keeps it intact; a 10°-off-vertical wall keeps its plane normal (R5).
- **Not changed:** `ExtendWalls`/`Extend`/`Fill` orchestration and targets, `CapZAtPlan`, `GrowOutward`/`GrowOutwardTo`, `ConditionStage` order/settings, adoption gates, `Solve.cs` signatures, GH components.
- **Acceptance gate:** raw goldens byte-identical; the five managed pins frozen (see the outcome note for how this resolved); `WorkflowParityIntegrationTests` rows that improve get an updated pin + tracking comment naming the mechanism; TESTING.md gets the E1 section.
- **Review focus:** the golden freeze is proven (per-fixture wall census), not claimed; hole handling (window touching the trim plane, window larger than the surviving piece, glazed wall trimmed until the hole dominates); `GetBaseSegment` tie determinism and full-extent measurement; tests assert shape, not bbox/point counts; diff confined to `SnappedPanel.cs` + tests + TESTING.md + harness rows, with R1–R5 mechanisms structurally impossible in the new code (no single-edge selection, no out-of-plane translation, no `Polygon3D` best-fit refit on moved points).

**E1 outcome note (2026-07-07):** the acceptance gate's "if any managed pin moves,
STOP and escalate" clause fired. A fast-path census (`Extend3DCensusIntegrationTests`) proved the
freeze could not hold: the five golden fixtures themselves contain the non-rectangular walls E1
fixes (37–107 plane-ops operations each), and the managed golden path and workflow B run the same
`ConditionStage` primitives on the same geometry — so the correctness fix (tilt preservation R5,
sloped-wall no-collapse) necessarily changes the managed signatures. Loosening the vertical-sides
tolerance did not move the failures (structural, not float noise). Escalated to the owner, who chose
to **re-baseline the three moved managed tripwire pins inside PR #48** (raw/production goldens stay
byte-identical). Implemented: plane-ops via SAM-core `Query.Extend`/`Query.Cut` (union-to-plane /
cut-keep-body-side); rectangular fast path frozen behind a private legacy helper; `GetBaseSegment`
full-extent (R7); `ExtendHorizontal` removed; `SAM_OCCT_EXTEND3D_HOLE_DROPPED` recorded on the panel
(surfacing it to `Solve3DReport` deferred to E3). The E1 review treats the golden-freeze check as
"raw goldens byte-identical + the managed re-baseline table is justified fixture-by-fixture" rather
than "all pins frozen".

## 4. E2 — scope, acceptance and outcome

**Scope.** Walls extend to the ACTUAL neighbouring cap planes (sloped roofs, floors) instead of a flat target Z + fixed overshoot. Target selection + application inside `Panel3DSnapSolver.Extend` (incl. `CapZAtPlan` use) and the `SnappedPanel` extend entry points it needs; `ExtendWalls`, `Fill`/`GrowOutwardTo` and `ConditionStage` stay byte-identical. This is the phase that re-baselines the broken managed pins.

- **Target selection (as planned):** candidate caps = non-vertical panels overlapping the wall in plan; each cap's surface elevation evaluated at three plan samples (both foot endpoints and the centre); extension to each covering cap plane offset by the overshoot along the plane normal; rectangular fast path only when the target plane is horizontal. (The multi-sample/multi-plane parts were implemented and rejected — see the outcome note.)
- **Policy pins:** vertical extension stays `MaxExtend`-uncapped (`MaxExtension` governs lateral reach only); overshoot constants keep their values, reinterpreted as plane-normal offsets; a parallel/degenerate `PlanarIntersectionResult` falls back to the E1 scalar-Z path and never throws.
- **Tests:** a wall under a single sloped roof ends exactly on the offset plane (assert distance-to-plane, not bbox); wall parallel to the roof slope falls back to scalar Z; integration — two-level-tilted forced-managed before/after.
- **Re-baseline table (the deliverable that lets this merge):** per moved managed pin — old value, new value, fixture, mechanism — cross-referenced to the workflow-parity harness table before and after. Raw goldens byte-identical (verified, not assumed).
- **Adversarial counterexamples used in review:** low shed roof beside a tall wall (must not be selected; no downward/degenerate extension); roof plane that dives below the wall base at one end (no material added below the base); ridge exactly over a wall end; steep roof where the overshoot offset sign could flip the intersection line downward; conforming gable (must not collapse); parallel-plane fallback reachable and tested.
- **Not changed:** `ExtendWalls`, `Fill`, `GrowOutwardTo`, `ConditionStage`, adoption gates, `Solve.cs` signatures, GH components, `PanelReconstruction`.
- **Acceptance gate:** full suite green; re-baseline table complete; naked-edge rule (see the outcome note: net non-increasing, every workflow parity-clean) holds; raw goldens byte-identical; P4 may branch only after this PR merges.

**E2 outcome note (2026-07-08):** implemented on branch `feat/extend3d-plane-targets`
off the merged `sow/2026-Q3`. Two findings reshaped the phase:

1. **The named target fixtures were misdiagnosed.** two-level-tilted and whole-level-towers are
   *rigidly tilted flat levels*, not sloped-roof models. A naive world-Z plane target collapsed
   two-level-tilted to **3 cells / 254 dropped** (walls extended to the wrong tilted cap in world-Z).
   The fix is a discriminator (`IsCapFlatRelativeToWall`): a cap whose normal aligns (within 15°) with
   the wall's own in-plane up-axis is "flat relative to the wall" — a level slab, *including a tilted
   level* — and keeps the pre-E2 scalar target (**byte-identical**). Only a cap genuinely *pitched
   relative to the wall* (a real roof over a vertical wall) takes the sloped plane. Cap SELECTION stays
   the pre-E2 single-nearest-over-centre rule (the multi-sample/multi-cap covering test of task 1 was
   implemented and **rejected** — it selected farther caps in world-Z and broke the tilted fixtures).
   So all 5 golden pins (raw AND managed) are byte-identical; the tilted fixtures do **not** improve —
   that needs *frame-aware* extension (extend along the level up-axis), deferred as a follow-up (call
   it E4 or fold into a frame pass).

2. **The improvement lands on the real-export fixtures**, which have genuine pitched roofs
   (final numbers as corrected by the E2 review record below): Revit-home-panels naked 4 → **0**
   (cells 12 → 14), AdjacencyCluster-home naked **25 → 4** (same 18-cell decomposition),
   Face3D-home 25/3 → 20/**4** (+1 solver-internal naked). Net across fixtures **−24 naked**, but the
   +1 on Face3D-home violated the literal "no naked increase on ANY fixture" gate. Exhaustive tuning
   (flatness cone 8–90°, foot-sampling, plan-footprint guard, roofs-only) could not remove that +1
   without regressing a *different* fixture — plane-targeting messy real geometry is intrinsically a
   mixed bag. On the **workflow** metric every fixture stays parity-clean with zero orphans (no
   workflow-level naked regression). **Owner decision (2026-07-08): ship the net-win, relax the gate
   from "any fixture" to "net non-increasing."** The E2 review below should treat the naked-count check
   as "net non-increasing + every workflow parity-clean," and confirm the discriminator keeps the
   tilted/flat goldens byte-identical, rather than "no increase on any fixture."

Re-baseline table: TESTING.md "E2" section (managed goldens byte-identical; real-export pins in
`Extend3DPlaneTargetIntegrationTests`; harness rows re-pinned in `WorkflowParityIntegrationTests`).

**E2 review record (2026-07-08, adversarial review, amended per the outcome note):
blocking findings found, fixed in-review, re-verified. Verdict: MERGE.**

The review attacked the committed implementation (`92e8b9b`) with the adversarial counterexamples listed above as runnable
fixtures. Findings, most severe first:

1. **F1 (blocking, fixed): the plane-target application was geometrically wrong.** The primitive
   reused SAM-core `Query.Extend` (`SnappedPanel.cs:1113@92e8b9b`), which extends a face by
   projecting boundary points onto the target line PERPENDICULARLY. That construction is correct only
   for a horizontal line (E1's exclusive use); on an inclined line the perpendicular is oblique, so
   the union (a) **spilled sideways in plan** past the wall's ends — the room-merge vector — even for
   a perfectly legitimate 26.6° gable (measured: 1.2 m past the wall end; 2.45 m for a shed clipping
   a long wall's bbox corner), and (b) **under-covered the high side as pitch grew** — at 70°+ the
   "extension" barely rose above the original top while returning success (wall left ~6 m short of
   its roof; E1 would have closed it). (c) The target line was the cap plane **extrapolated without
   bound** past the cap's physical extent (E1's scalar target was bounded by the cap bbox). The
   fixture wins of `92e8b9b` were partly artifacts of (a). **Fix (in-review):** the extension is now
   built column-wise over exactly the wall's own plan extent, clamped at the cap's real extreme ±
   overshoot, with all degenerate cases (parallel, diving, wall-parallel slope, failed union) falling
   back to the scalar path — never a silent no-grow swallow (`92e8b9b`'s no-op guard returned false
   without the fallback).
2. **F2 (fixed): the roof overshoot was wrong for a surface-following target.** `roofOvershoot`
   (0.5 m) exists so E1's flat-at-ridge target clears the pitch from below; applied to a plane target
   it pierced 0.5 m past the roof everywhere and shredded adjacent geometry into naked fragments
   (AdjacencyCluster-home degraded to 46 cells/10 naked under the corrected geometry until the plane
   target switched to the small wall overshoot).
3. **F3 (fixed, docs): net-naked arithmetic** said −22 in TESTING.md/outcome note; the rows give −24.
   Also the offset comment claimed the surface shifts "by exactly overshoot" (it is overshoot/|n·Z|,
   now bounded by the clamp anyway).
4. **F4 (recorded): policy-pin deviations, owner-approved by the outcome note** — task 1's
   multi-sample covering test and task 2's multi-plane unions were implemented and rejected (tilted
   fixtures); R8's "conforming gable no-op" is reinterpreted as "non-collapse + overshoot-then-trim"
   (same in kind as E1, which also overshoot-extends conforming walls).
5. **F5 (residual, accepted): single-nearest under multi-pitch caps** — a descending pitch A beside a
   higher cap B leaves the wall short of B where E1's flat-at-ridge target incidentally covered it
   (demonstrated at 31°). This is the mechanism class behind Face3D-home's +1 and is covered by the
   owner's net rule; revisit under a future frame-aware/multi-cap phase.

False-extension analysis (corrected build): shed-beside-wall (flat and sloped variants) → not
selected / bounded at the cap's real extreme, zero plan growth; diving plane → scalar, base
preserved; ridge-over-wall-end → covered by the single slope; stacked floors → nearest only;
parallel cap → scalar fallback, no throw; pitch sweep 20–75° → full coverage, top = clamp exactly.

Re-verified evidence: 5 raw + 5 managed golden pins byte-identical; 457 unit / 167 integration green;
all 9 harness fixtures parity-clean, zero orphans; final managed matrix vs E1: Revit-home 12/4 →
14/**0**, AdjacencyCluster-home 18/25 → 18/**4**, Face3D-home 25/3 → 20/4, net **−24**, tilted/flat
byte-identical. **Verdict: MERGE — and merging E2 unblocks P4** (gate hardening branches off the
post-E2 `sow/2026-Q3`).

## 5. E3 — scope, acceptance and outcome

**Scope.** Make every extend observable and pin the two new fixtures. Zero geometry change: diagnostics records + GH outputs + harness pins + docs.

- **`ExtendRecord` diagnostics:** for every applied primitive operation record panel Guid + solver index, operation kind (top / bottom / plan-start / plan-end / cap-grow), measured from → to, target identity (cap panel index or plane description), overshoot applied and the lateral-only `MaxExtend`-capped flag; surfaced as coded `SAM_OCCT_EXTEND3D_PANEL:` lines through the existing report path. Recording only.
- **`SAMOCCTExtend3D`:** append-only Voluntary outputs — per-panel extend summary (text) and moved-edge preview geometry; existing canvases load unchanged.
- **Baselines:** workflow-B harness rows for `three-spaces.sam` and `Revit-home-panels.sam` converted from expected-fail pins to their post-E1/E2 values, each citing the phase that flipped it; TESTING.md per-fixture table refreshed and E-track section added.
- **Tests:** diagnostics round-trip through `Solve3DReport`; records match actual moves on a hand-checked fixture (native-free via `Extend3D`); GH back-compat; full suite green.
- **Acceptance gate:** all goldens AND E2 pins byte-identical; harness table current; TESTING.md section committed.

**E3 outcome note (2026-07-09):** implemented on branch `feat/extend3d-observability`
off the post-E2 `sow/2026-Q3` (HEAD `1713a5d`). Zero geometry change, as specified.

- **Recording (task 1).** New `ExtendRecord` + `ExtendOperationKind` (`SAM.Geometry.OCCT.Solver`); the
  extend statics (`ExtendWalls`/`Extend`/`Fill` → `ExtendWallToNearestCap`) gain an optional
  `List<ExtendRecord>` sink threaded via `ConditionStage.Condition`. A **null sink is byte-identical**
  to the pre-E3 path (asserted). Records are captured in the canonical frame during conditioning and
  back-transformed with the faces (`fromCanonical`) so preview points land in the world frame. Each
  record carries panel Guid (resolved analytically — the solver has no Guids) + solver index, op kind,
  measured from→to, target (cap index + `cap-scalar`/`cap-plane` branch, or `walls`/`fixed-margin`),
  overshoot and the lateral-cap flag. Only **actual** moves are recorded (bbox/area delta > tol); no
  record on a no-op.
- **Carrier decision.** Records ride a new `Panel3DSnapSolver.ExtendRecords` / `Solve3DReport.ExtendRecords`
  (trailing optional ctor param) rather than `SolverDiagnostics`, so `solver.Diagnostics`,
  `ClosureReportText` and the shared `FormatDiagnostics` stay **byte-identical** (protecting the report
  golden tests). Coded `SAM_OCCT_EXTEND3D_PANEL:` lines are emitted via a new
  `SolverReportFormat.FormatExtendRecords(records, sources)` into the same `diagnostics` list on
  `Extend3D`/`OpenPanels3D`/managed `Solve3D`.
- **Hole-drop surfaced.** The E1-deferred `SAM_OCCT_EXTEND3D_HOLE_DROPPED` (recorded on the panel, never
  surfaced) now flows out via `SolverReportFormat.FormatExtendPanelDiagnostics(solver.SnappedPanels)` on
  the same runs.
- **GH (task 2).** `SAMOCCTExtend3D` gains two append-only Voluntary outputs — `ExtendReport` (text) and
  `ExtendPreview` (moved-edge `Segment3D`s) — sourced from `report.FormatExtendRecords()` /
  `report.ExtendPreviewSegment3Ds()`. Component `0.5.0 → 0.6.0`; existing canvases load unchanged.
- **Baselines (task 3).** The `WorkflowParityIntegrationTests` `Expectations` were **verified consistent**
  with the post-E2 status (all 9 fixtures green, no re-baseline needed). TESTING.md's per-fixture table
  (stale at 2026-07-07) was refreshed to the current harness output and the E-track summary + diagnostics
  code list added.
- **Verification.** Raw + managed goldens and E2 real-export pins **byte-identical**; full blast radius
  green with native present — **467 unit / 168 integration** (+1 native-missing skip). New tests:
  `SolverReportFormatTests`, `Solve3DReportTests` (format + round-trip), `Panel3DSnapSolverTests` (records
  match moves; null recorder byte-identical), `Extend3DObservabilityIntegrationTests` (native-free
  end-to-end). Note for P4: judge false-positives against the **post-E2** managed baselines (Revit 14/0,
  AdjacencyCluster-home 18/4, Face3D-home 20/4), not the plan's original snapshot.

## 6. PR mechanics

**PR #49 closing comment (posted when E1 landed in #48):**

> Superseded by the E-track of `docs/EXTEND3D_ROBUST_HANDOVER.md`. The three profile-preservation
> unit tests from `26ac05a` were cherry-picked into PR #48 as the E1 acceptance targets; the
> edge-move implementation was replaced by a plane-ops rebuild (union-to-plane / cut-by-plane on
> SAM-core `Query.Extend`/`Query.Cut`) — see the risk register (§C) for the verified failure
> modes of the edge-move approach (gable shear, M-top false success, near-vertical plane drift,
> silent hole loss). Plane-target selection (sloped roofs, gables) follows as E2
> (`feat/extend3d-plane-targets`), observability as E3.

**PR #48 description addition (add when E1 lands):**

> **E1 (robust Extend3D primitives)** — `SnappedPanel` extend/trim primitives rebuilt as
> plane-ops (profile + hole preserving); rectangular fast path keeps all golden pins
> byte-identical; workflow-B parity rows updated with tracking comments. Target-policy changes
> (sloped-roof planes) intentionally deferred to E2 post-merge. See
> `docs/EXTEND3D_ROBUST_HANDOVER.md`.

## 7. PR #48 merge-checklist additions (E1)

Appended to §4 of `docs/CELLCOMPLEX_FIRST_HANDOVER.md` (the gatekeeper walks them with the rest):

11. E1 profile-preservation tests green (gable/M-top/stepped/hole/near-vertical + the three
    cherry-picked PR #49 tests); ExtendBottomTo has dedicated coverage.
12. Five managed pins byte-identical post-E1 (fast-path census evidence, not assumption); raw
    goldens byte-identical; `SAM_OCCT_EXTEND3D_HOLE_DROPPED` diagnostic wired.
13. WorkflowParityIntegrationTests rows flipped by E1 carry updated pins + tracking comments
    naming the mechanism; no silent skips introduced.
