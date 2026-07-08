# PR #49 → E-track — Robust Extend3D: Implementation Handover

`SAM-BIM/SAM_OCCT` · supersedes branch `feature/extend3D` (PR #49, WIP commit `26ac05a`)
Status: approved 2026-07-07 · Owner: Michal Dengusiak

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

# AI implementation prompts and model plan

## 1. Shared context for all E-track agents

Paste this block at the top of every phase prompt below (it complements, not replaces, the
CellComplex shared context — paste both when a prompt touches both tracks).

```
SHARED CONTEXT — SAM_OCCT robust-Extend3D (E-track) work
Repo: SAM-BIM/SAM_OCCT (Windows; native OCCT via P/Invoke; committed DLL at build/SAM.Occt.Native.dll).
Sibling repo SAM core: ..\SAM (referenced as built DLLs, e.g. ..\..\..\SAM\build\SAM.Geometry.dll);
sibling SAM_Solver provides the 2D SAM.Geometry.Solver.ExtensionSolver.
Core rule: Extend3D is the controlled pre-conditioner. It must grow panels toward their real
neighbours while preserving profile (non-rectangular boundaries, holes) and plane — never rebuild
a panel as a flat rectangle. Raw goldens byte-identical in every E phase; managed movement only
via an explicit re-baseline table, and only in E2.

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

Conventions (mandatory): SPDX header `// SPDX-License-Identifier: LGPL-3.0-or-later` + copyright on
every new .cs; xUnit, Method_State_Expected, Arrange/Act/Assert; two-tier test rule per TESTING.md
(unit = no native, integration = native-gated via Skip.IfNot(NativeProbe.Available)); update
TESTING.md with a phase section; commit messages and PR descriptions signed
"Generated by Michal Dengusiak & Claude Code".
Risk register R1-R10 (docs/EXTEND3D_ROBUST_HANDOVER.md §C): the failure modes of the superseded
edge-move WIP (26ac05a). Any implementation or review in this track must show these cannot recur.
```

## 2. Phase table E1–E3 (and where they sit among P1–P5)

Combined order: **P1 → E1 → P2 → merge #48 → E2 → {P3 ∥ E3-after-E2} → P4 → P5.**

| Phase | Scope (one line) | Branch / PR | Implementation | Review | Why this model/effort | Merge condition |
|---|---|---|---|---|---|---|
| E1 | Plane-ops rebuild of the extend/trim primitives (profile + hole preserving), scalar targets unchanged | `fix/solver-raw-first` / **PR #48** (after P1) | **Opus 4.8, xhigh** | **Opus 4.8, xhigh** | Geometry edge cases (holes, near-vertical planes, keep-piece selection) need a strong implementer; #48's Fable-Max merge gate (P2 review) re-walks E1, so a second Fable pass here is redundant | **DONE (2026-07-07).** Raw goldens byte-identical; the fast path could NOT hold the managed pins (structural — the golden fixtures contain the non-rectangular walls E1 fixes, so the managed tripwire and workflow B share code+geometry). Per owner decision, three managed pins re-baselined in #48 with a mechanism table (TESTING.md "E1"); two unchanged. Workflow-B harness rows re-pinned. See §3 note. |
| E2 | Plane-intersection cap targets (walls extend to actual roof/floor planes; gable policy; no-op when conforming); managed re-baseline | new `feat/extend3d-plane-targets` off `sow/2026-Q3` / new PR | **Opus 4.8, Max** | **Fable 5, Max** (adversarial) | Target-selection policy is the highest-risk E phase and moves managed pins — reviewer's job is to construct wrong-plane / runaway-extension counterexamples | Review approves; managed re-baseline table row-by-row justified via P1 harness; naked count must not increase on ANY fixture in either workflow; raw goldens byte-identical; **unblocks P4** |
| E3 | Per-panel extend observability (which edge, how far, toward what), workflow-B pins for the two new fixtures, GH polish, docs | new `feat/extend3d-observability` off `sow/2026-Q3` (after E2) / new PR | **Sonnet 5, High** | **Opus 4.8, xhigh** | Diagnostics plumbing + table pinning is mechanical; review focuses on honesty and GH append-only back-compat | Review approves; zero behaviour change (all goldens + E2 pins byte-identical); TESTING.md E-track section committed |

Model-strategy note: palette follows `CELLCOMPLEX_FIRST_HANDOVER.md` (Sonnet 5 High = mechanical,
Opus 4.8 xhigh/Max = hard implementation, Fable 5 Max = adversarial/merge gates). E1's merge
decision is owned by the existing PR #48 gatekeeper (§6 of the CellComplex handover, Fable 5 Max).

## 3. E1 implementation prompt (Opus 4.8, xhigh → PR #48, after P1)

```
[paste E-track Shared context]
GOAL: Replace the flat-rectangle re-extrusion inside SnappedPanel's extend/trim primitives with
plane-ops (union-to-plane / cut-by-plane) so non-rectangular walls survive extension. Same scalar
targets as today — target POLICY changes are E2, not E1. Every golden pin stays byte-identical.
SCOPE: SnappedPanel.cs primitives + unit tests + workflow-B harness row updates. Nothing else.
CONTEXT: This supersedes WIP commit 26ac05a on feature/extend3D (PR #49). Cherry-pick its three
unit tests (ExtendTopTo_SlopedBase_PreservesPlanFootprint, ExtendTopTo_ShiftedTop_UsesExternalEdgeNotDiagonal,
ExtendHorizontal_ShiftedTop_MovesSideEdgesWithoutFlattening — retargeted per task 5) as acceptance
targets. Do NOT port its implementation (ExtendBoundaryEdgeToZ, BoundaryEdgeIndex, MovePlanEndEdge,
PlanEndEdgeIndex, ReplaceExternalBoundary) — risk register R1-R7 documents why.
TASKS:
1. Characterization first: unit-test SAM-core Query.Extend(Face3D, Plane) union semantics on a
   gable and an M-top wall extended to a horizontal plane (flat top at the plane across the full
   plan span? base profile preserved? holes carried?). If unsuitable, build the in-plane extension
   band explicitly (convert to the face's 2D frame, union via Planar.Query.Union) — same
   principle, contained fallback. Record the choice in the PR description.
2. ExtendTopTo/ExtendBottomTo (SnappedPanel.cs:639/:684) — classification ladder:
   (a) rectangular hole-free vertical face (4 corners, horizontal top and bottom within
       tolerance) -> keep the legacy base-segment re-extrusion, numerically identical to today
       (this freezes the five managed golden pins);
   (b) any other vertical-planar face -> extend to the horizontal plane at targetZ via the task-1
       mechanism: flat top (or bottom) at targetZ across the current plan span, rest of the
       boundary and all holes preserved, supporting plane unchanged;
   (c) already reaching targetZ -> return false, no mutation (unchanged contract).
3. SetVerticalFootprint (SnappedPanel.cs:830) — per-end decomposition: for each end, target plane
   = vertical plane through the new end point with normal = the wall's plan axis; lengthen via
   the task-1 extend, shorten via Query.Cut(face3D, plane, out above, out below) keeping the
   piece that contains the foot midpoint. A hole clipped or dropped by the trim emits coded
   diagnostic SAM_OCCT_EXTEND3D_HOLE_DROPPED (never silent — R6). Construct results only via the
   explicit external/internal Face3D.Create overload, never the loops overload (max-area-external
   trap, R6). ExtendHorizontal callers' semantics (widen both ends) map onto this method.
4. GetBaseSegment (SnappedPanel.cs:735): direction = longest plan-projected external edge
   (tie-break: longer, then lower mean Z, then lower index); extent = min/max parameter of ALL
   external boundary points along that direction (R7 — the old horizontal-cut also under-measured
   walls with door notches at the base); endpoints placed at bbox Min.Z. For a vertical wall all
   boundary points project onto one plan line, so the direction choice is safe — add the unit
   test that proves it (parallelogram tie case included).
5. ExtendHorizontal (SnappedPanel.cs:769): zero production callers (grep evidence: only
   Panel3DSnapSolverTests.cs:497/:514 + doc comments). Remove it, or [Obsolete]-delegate to
   SetVerticalFootprint — implementer's choice, stated in the PR. Retarget its two existing tests
   plus the WIP's third test at SetVerticalFootprint widening.
6. Tests (all pure-managed unit tests, Method_State_Expected):
   - dedicated ExtendBottomTo coverage (existing gap — today only transitive);
   - SetVerticalFootprint: lengthen, shorten, shorten-through-a-window (diagnostic asserted);
   - shape-asserting edge cases (R10 — bbox-only assertions are banned): gable extended to
     targetZ has BOTH slopes preserved below a flat top; M-top fully reaches targetZ; stepped
     end reaches the exact target plan parameter; wall with a window survives extend with the
     window intact; a 10-degree-off-vertical wall keeps its plane normal (assert normal, R5).
DO NOT CHANGE: ExtendWalls/Extend/Fill orchestration and their targets (Panel3DSnapSolver.cs:1224/
:1452/:1605), CapZAtPlan, GrowOutward/GrowOutwardTo, ConditionStage order/settings, any adoption
gate, Solve.cs signatures, GH components.
TESTS: full blast-radius list from the shared context; all existing tests stay green.
ACCEPTANCE GATE (hard): five managed pins AND raw goldens byte-identical — if any managed pin
moves, STOP and narrow the fast path (do not re-baseline anything inside PR #48; escalate to the
owner if the fast path cannot hold). WorkflowParityIntegrationTests rows that improve get their
pinned value + tracking comment updated, each naming the mechanism ("profile preserved -> wall no
longer collapses -> N naked edges closed"). TESTING.md gets the E1 section.
```

**E1 outcome note (2026-07-07, Opus 4.8 impl):** the acceptance gate's "if any managed pin moves,
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
(surfacing it to `Solve3DReport` deferred to E3). The E1 review below should treat check #1 as
"raw goldens byte-identical + the managed re-baseline table is justified fixture-by-fixture" rather
than "all pins frozen".

## 4. E1 review prompt (Opus 4.8, xhigh)

```
[paste E-track Shared context]
Review the E1 diff on PR #48 (plane-ops SnappedPanel primitives). CHECK, in order:
1. Golden freeze is proven, not claimed: run the full blast-radius list with native present;
   raw AND managed golden signatures byte-identical. Inspect the rectangular fast-path predicate
   and verify (evidence: per-fixture wall census or targeted debug output) that it fires for
   every wall of the five golden fixtures — if any golden wall takes the new path, the freeze is
   luck, not design.
2. Attack hole handling (R6): window touching the trim plane; window larger than the surviving
   piece; glazed wall trimmed until the hole dominates. Verify SAM_OCCT_EXTEND3D_HOLE_DROPPED
   fires and the external/internal Face3D.Create overload is used everywhere (grep for the loops
   overload in the diff).
3. GetBaseSegment (R7): parallelogram base/top tie deterministic; stepped-bottom wall's extent
   equals the FULL plan extent of the boundary, not the longest edge's span; ExtendWalls and
   OpenWallEnds still receive the same foot-line they did for rectangular walls.
4. Test honesty (R10): edge-case tests assert shape (both gable slopes, plane normal, exact plan
   parameter) — reject bbox/point-count-only assertions; the three cherry-picked WIP tests are
   present and passing; ExtendBottomTo has dedicated coverage.
5. Scope discipline: diff confined to SnappedPanel.cs + tests + TESTING.md + harness row updates;
   no orchestration/target/Solve.cs/GH drift (grep the diff); R1-R5 mechanisms structurally
   impossible in the new code (no single-edge selection, no out-of-plane translation, no
   Polygon3D best-fit refit on moved points).
6. Conventions: SPDX, Method_State_Expected, native gating, TESTING.md E1 section, signature.
OUTPUT: findings ranked by severity with file:line, each with a concrete failure scenario;
explicit verdict: safe to keep in PR #48 yes/no.
```

## 5. E2 implementation prompt (Opus 4.8, Max → new PR off `sow/2026-Q3`, after #48 merges)

```
[paste E-track Shared context]
GOAL: Walls extend to the ACTUAL neighbouring cap planes (sloped roofs, floors) instead of a flat
targetZ + fixed overshoot, so sloped-roof models condition correctly on the managed path and in
workflow B. This is the phase that re-baselines the broken managed pins. Branch
feat/extend3d-plane-targets off sow/2026-Q3 (post-#48-merge; E1 is in).
SCOPE: target selection + application inside Panel3DSnapSolver.Extend (:1452-1573, incl.
CapZAtPlan use) and the SnappedPanel extend entry points it needs. ExtendWalls, Fill/GrowOutwardTo
and ConditionStage stay byte-identical.
TASKS:
1. Target selection per wall: candidate caps = non-vertical panels overlapping the wall in plan
   (keep the existing candidate gate). Evaluate each cap's surface elevation at THREE plan
   samples — both foot endpoints and the centre (centre-only is today's miss for walls spanning
   two roof planes). A cap covers the wall if its surface is above the wall top at any sample
   (mirror: below the wall base for floors).
2. Application: for each DISTINCT covering cap plane (lowest first), extend the wall to that
   plane offset by the overshoot along the plane normal, sign chosen so the intersection line
   moves along the wall's in-plane up direction; unions accumulate. Gable/ridge policy: a wall
   under two roof planes extends to both — the kernel trims the ridge crossing. Mirror for
   ExtendBottomTo with floors below. Implementation reuses the E1 mechanism with an arbitrary
   plane target (Query.Extend semantics); rectangular fast path applies only when the target
   plane is horizontal.
3. Policy pins (state each in code comments + PR):
   - vertical extension remains MaxExtend-UNCAPPED (today's behaviour; MaxExtension governs
     lateral reach only — changing that is out of scope, note it in the doc);
   - no-op when the wall already reaches every selected plane (fixes R8: conforming gables are
     untouched);
   - overshoot/roofOvershoot constants keep their current values, reinterpreted as plane-normal
     offsets;
   - PlanarIntersectionResult parallel/degenerate (wall plane parallel to cap plane) -> fall back
     to the E1 scalar-Z path for that cap; never throw.
4. Tests: unit — wall under a single sloped roof ends exactly on the offset plane (assert
   distance-to-plane, not bbox); gable wall under two planes reaches both; conforming gable
   no-op; wall parallel to the roof slope falls back to scalar-Z; integration — two-level-tilted
   forced-managed before/after captures the pin movement.
5. Managed re-baseline table (THE deliverable that lets this merge): per managed pin that moves
   (expected: two-level-tilted 29c/29n improves; possibly whole-level-towers 22c/12n), a row with
   old value, new value, fixture, and the mechanism, cross-referenced to the P1 harness table
   run before and after. HARD RULE: naked-edge count must not increase on any of the 9 fixtures
   in either workflow. Raw goldens byte-identical (verify, don't assume).
DO NOT CHANGE: ExtendWalls (:1224), Fill (:1605), GrowOutwardTo, ConditionStage, adoption gates,
Solve.cs signatures, GH components, PanelReconstruction.
ACCEPTANCE GATE: full suite green; re-baseline table complete; naked-count rule holds on all 9
fixtures; raw goldens byte-identical; P4 may branch only after this PR merges.
```

**E2 outcome note (2026-07-08, Opus 4.8 impl):** implemented on branch `feat/extend3d-plane-targets`
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

2. **The improvement lands on the real-export fixtures**, which have genuine pitched roofs:
   Revit-home-panels naked 4 → **0** (cells 12 → 18), AdjacencyCluster-home naked **25 → 4**,
   Face3D-home 25/3 → 22/**4** (+1 solver-internal naked). Net across fixtures **−22 naked**, but the
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

## 6. E2 review prompt (Fable 5, Max — adversarial)

```
[paste E-track Shared context]
Adversarial review of the E2 diff (plane-intersection cap targets). Your job is to break the
target-selection and application rules with realistic geometry, then audit the re-baseline.
1. Construct counterexamples (on paper or as quick unit fixtures):
   - low shed roof BESIDE a tall wall (in-plan overlap, surface below the wall top everywhere) —
     must not be selected; no downward/degenerate extension;
   - roof plane that, extended infinitely, dives below the wall base at one end — union must not
     add material below the base;
   - ridge exactly over a wall end; two stacked roofs over one wall (covering SET, not merely the
     nearest, must be used);
   - steep roof where the overshoot offset sign could flip the intersection line downward;
   - conforming gable (e.g. in Revit-home-panels) — must be a no-op, byte-identical face.
2. Verify the three-sample covering test against a wall spanning two roof planes whose centre
   sample sits under neither.
3. Verify the parallel-plane fallback path is reachable and tested; no throw path from
   PlanarIntersectionResult degeneracy.
4. Audit the re-baseline table: every moved pin has old/new/fixture/mechanism and a matching P1
   harness before/after run; naked-edge count non-increasing on ALL 9 fixtures in both workflows;
   raw goldens byte-identical (run the suite yourself).
5. Scope: ExtendWalls/Fill/ConditionStage byte-identical (grep the diff); no gate touched.
OUTPUT: findings with file:line + failure scenario; explicit false-extension analysis; verdict
merge yes/no. Merging this PR unblocks P4 — say so explicitly in the verdict.
```

## 7. E3 implementation prompt (Sonnet 5, High → new PR off `sow/2026-Q3`, after E2)

```
[paste E-track Shared context]
GOAL: Make every extend observable and pin the two new fixtures. Zero geometry change. Branch
feat/extend3d-observability off sow/2026-Q3 (post-E2).
SCOPE: diagnostics records + GH outputs + harness pins + docs.
TASKS:
1. ExtendRecord diagnostics: for every applied primitive operation record panel Guid + solver
   index, operation kind (top / bottom / plan-start / plan-end / cap-grow), measured from -> to
   (plan parameter or distance-to-plane), target identity (cap panel index or plane description),
   overshoot applied, MaxExtend-capped flag (lateral only). Surface as coded
   SAM_OCCT_EXTEND3D_PANEL: lines through the existing SolverDiagnostics/report path so
   Solve3DReport carries them on Extend3D/OpenPanels3D/Solve3D-managed runs. Recording only — no
   behaviour change.
2. SAMOCCTExtend3D: append-only Voluntary outputs — per-panel extend summary (text lines) and
   moved-edge preview geometry (segments from -> to). Existing canvases must load unchanged
   (parameter append-only, defaults preserve behaviour).
3. Baselines: convert the P1-harness workflow-B rows for three-spaces.sam and
   Revit-home-panels.sam from expected-fail pins to their post-E1/E2 values; every flipped row
   cites the phase that flipped it. Refresh the TESTING.md per-fixture table (all 9 fixtures) and
   add the E-track section (E1/E2/E3 summary, re-baseline table link, diagnostics code list).
4. PR mechanics: close-out comment on PR #49 (see §9 of this doc) if not already posted.
DO NOT CHANGE: any geometry code path (SnappedPanel mutations, Panel3DSnapSolver statics beyond
record emission), gates, cluster, Solve.cs semantics.
TESTS: diagnostics round-trip through Solve3DReport (unit); records match actual moves on one
hand-checked fixture (integration, native-free via Extend3D); GH back-compat test; full suite
green.
ACCEPTANCE GATE: all goldens AND E2 pins byte-identical (this phase moves nothing); harness table
current; TESTING.md section committed.
```

## 8. E3 review prompt (Opus 4.8, xhigh)

```
[paste E-track Shared context]
Review the E3 diff (extend observability). CHECK:
1. Zero drift: grep the diff — no mutation-path change outside record emission; run the full
   blast-radius list; all goldens + E2 pins byte-identical.
2. Diagnostics honesty: hand-check one fixture (three-spaces) — every emitted ExtendRecord
   matches an actual geometry delta (compare pre/post faces), and every actual delta has a
   record; no record on no-op calls.
3. GH back-compat: parameter append-only, existing component tests untouched and green, new
   outputs Voluntary.
4. Pinned rows match a fresh WorkflowParityIntegrationTests run; each flipped row cites E1 or E2.
OUTPUT: findings with file:line + failure scenario; verdict merge yes/no.
```

## 9. PR mechanics

**PR #49 closing comment (post when E1 lands in #48):**

> Superseded by the E-track of `docs/EXTEND3D_ROBUST_HANDOVER.md`. The three profile-preservation
> unit tests from `26ac05a` were cherry-picked into PR #48 as the E1 acceptance targets; the
> edge-move implementation was replaced by a plane-ops rebuild (union-to-plane / cut-by-plane on
> SAM-core `Query.Extend`/`Query.Cut`) — see the risk register (§C) for the verified failure
> modes of the edge-move approach (gable shear, M-top false success, near-vertical plane drift,
> silent hole loss). Plane-target selection (sloped roofs, gables) follows as E2
> (`feat/extend3d-plane-targets`), observability as E3.
>
> Generated by Michal Dengusiak & Claude Code

**PR #48 description addition (add when E1 lands):**

> **E1 (robust Extend3D primitives)** — `SnappedPanel` extend/trim primitives rebuilt as
> plane-ops (profile + hole preserving); rectangular fast path keeps all golden pins
> byte-identical; workflow-B parity rows updated with tracking comments. Target-policy changes
> (sloped-roof planes) intentionally deferred to E2 post-merge. See
> `docs/EXTEND3D_ROBUST_HANDOVER.md`.

## 10. PR #48 merge-checklist additions (E1)

Appended to §14 of `docs/CELLCOMPLEX_FIRST_HANDOVER.md` (the gatekeeper walks them with the rest):

11. E1 profile-preservation tests green (gable/M-top/stepped/hole/near-vertical + the three
    cherry-picked PR #49 tests); ExtendBottomTo has dedicated coverage.
12. Five managed pins byte-identical post-E1 (fast-path census evidence, not assumption); raw
    goldens byte-identical; `SAM_OCCT_EXTEND3D_HOLE_DROPPED` diagnostic wired.
13. WorkflowParityIntegrationTests rows flipped by E1 carry updated pins + tracking comments
    naming the mechanism; no silent skips introduced.

---

Generated by Michal Dengusiak & Claude Code
