# Air panels for open boundaries and the unconditioned attic — Q4 (2026-10-13)

Status: implemented on `feature/air-panels-attic-q4`; draft PR, owner review required. Not merged.
Base: `sow/2026-Q4` at `f584e94ee2abd0ebb1622c0ee614b1dc520c81c0`. **Stacked on SAM-BIM/SAM_OCCT#76** (`fix/sew-under-split-q4`): merge #76 first; this PR's diff then shrinks to its own commit.
PR: the live draft PR for this branch; its description holds the tested SHA, final head and hosted checks.
Related: SAM-BIM/SAM#194 (`CreatePanelsBySolids`). That PR creates the Air panels this one carries through. There is no build-time dependency: these tests construct the Air panel directly.

## Owner decisions implemented (2026-10-13)

1. Open internal boundaries are Air panels. The Hall/Landing stair void is the example. Both spaces stay separate, and real walls, floors and ceilings keep their physical type and construction.
2. The attic is an analytical space marked **unconditioned**. The target is 10 rooms + 1 attic = 11 spaces.

## Change

- `Create.AdjacencyCluster` (rebuild path, `DirectAdjacencyCluster` / `CreatePanels`):
  - A cell face matched to a supplied **Air** panel keeps Air identity: type Air, no construction, and the source's parameters (first piece keeps the Guid).
  - Matched Air panels are excluded from `UpdatePanelTypes` / `SetDefaultConstructionByPanelType`; everything else is classified exactly as before. Diagnostic: `SAM_OCCT_ANALYTICAL_AIR_PANELS`.
  - Previously every rebuilt face was re-typed from its normal, so Air became Floor.
- `MatchPanelByGeometry` (shared by the handoff and rebuild paths) gives **Air precedence** when a cell face matches an Air panel and solid panels, but only if:
  - the Air panel is smaller than every solid candidate (an opening cut from a solid, e.g. a stair void a solver filled); and
  - the cell face fits inside the Air panel.
  An Air panel duplicating a same-size wall (a solver gap fill) never overrides the wall.
- `EnsureAirRepresented` (with `SewBeforeBuild`): the sew path's same-domain unification can merge an Air face into the surrounding coplanar slab. If a supplied Air panel is not a cell face of its own, the build is redone directly (MakerVolume).
  - The direct build is adopted only if it represents more Air and gives at least as many cells. Warning: `SAM_OCCT_ANALYTICAL_AIR_DIRECT_BUILD`.
  - Otherwise the sew build is kept with Warning `SAM_OCCT_ANALYTICAL_AIR_NOT_REPRESENTED`.
- GH `SAMOCCT.Solve3D`:
  - Input Air panels used to be dropped by the core solve; they now **pass through** to the Panels output with a Remark.
  - They carry no SolveId and are not in the complex roster, so `CreateAdjacencyCluster` refuses the direct handoff and rebuilds with the Air faces in the cell build.
  - The library `Modify.Solve3D` and `Create.Spaces` Air policies are unchanged.
- GH `SAMOCCT.CreateAdjacencyCluster`: the two new codes are Warnings.
- **Attic unconditioned uses existing SAM functionality only.** Either option works:
  - (a) a seed space carrying the `S00_Unconditioned` internal condition (seed identity is copied into the cell), or
  - (b) a seed named with "Unconditioned" (e.g. "Attic Unconditioned"), then `SAMAnalytical.MapInternalConditions` with the default library and text map.
  - No new enum or flag.

## Validation (local, this machine)

- Unit: 648 / 0.
- House integration (`HouseUnderSplitIntegrationTests` 7 + `HouseAirAndAtticIntegrationTests` 8): **15 / 0**.
  - 11 named spaces.
  - Air relates exactly GF Hall + UF Landing, 3.325 m², no construction.
  - The rest of the shared slab stays physical: 12.175 m² with a construction.
  - Attic `S00_Unconditioned`, sharing the ceiling with UF rooms and owning the roof; mapping by name also works.
  - An Air panel duplicating a wall keeps it physical.
  - Determinism.
- Measured, not assumed:
  - Volumes are unchanged by the Air panel, because the void lies in the slab mid-plane. Total 564.1 m³; attic 112.683 m³.
  - Floor areas: GF Hall 15.5 m², and UF Landing 15.5 m² (SAM counts the Air floor).
- Full integration: **257 passed / 19 failed / 2 skipped**. The 19 failures are identical by name to base `f584e94` (242 / 19 / 2), so they pre-exist; +15 are the house tests.
- Grasshopper contract suite: **not completed tonight.** The test host hangs at start (inactivity, no CPU) on this branch **and** on the PR-1 commit `33d1079`, where the same suite passed 15/15 earlier the same evening. So the hang is environmental, not caused by this change. Hosted CI runs it.
- Aperture hosting (harness emulation of `AddAperturesByGeometryAndApertureType` on chain A: main face of each glass pane and door leaf, `maxDistance` 0.1 m): **25 / 25 hosted** on one correct wall each. 16 windows go on the rooms' external walls, the front door on the Hall, and 8 internal doors on the correct partitions. The default `maxDistance_` (0.001 m) does not reach glass 5 mm off the mid-plane.
- End-to-end on the real house (machine-local Rhino 8 harness, Untitled2.3dm in metres), with the deployed SAM#194 + this branch. Chain A is the new workflow, B the original chain, C the original chain with an Air panel through Solve3D:

| Chain | Path | Result |
|---|---|---|
| A | `PanelsBySolid` (windows/doors as apertures) + `SnapToJunctions` → `CreateAdjacencyCluster` | 11 spaces, 1 Air (Hall/Landing), attic unconditioned |
| B | original GH script → `CreateAdjacencyCluster` | 11 spaces |
| C | original script + stair Air → Solve3D (pass-through) → `CreateAdjacencyCluster` | 11 spaces, 1 Air (Hall/Landing), attic unconditioned |

- Not run: hosted CI; manual Rhino/Grasshopper.

## Remaining / next

- Owner review.
- Manual GH test: see the morning handover in the PR description.
- Aperture hosting through the GH component is still to be confirmed manually (set `maxDistance_` to 0.1).
- Solve3D over-splits the attic for snapped `PanelsBySolid` input (13 cells), so it is not used in that workflow.

## Addition (2026-10-14): source parameters survive the rebuild

Status: follow-up commit on the same branch; owner review required. Not merged.

### Why

The rebuild path classifies every cell face and gives it a default construction. Each physical panel therefore lost what it was made from:
- on the real house, 0 of 55 physical panels kept `PanelSourceParameter` (source name, id, layer);
- they also lost the new **physical thickness** of SAM-BIM/SAM#194, which the direct T3D export now uses as the element width (SAM-BIM/SAM_Tas, branch `feature/panels-by-solid-q4`).

### Change

- New internal `SourceParameterCarrier`, used by both the rebuild path (`CreatePanels`) and the `ResolvedCellComplex` handoff path:
  - **Sources:** every supplied **non-Air** panel coplanar with the cell face that overlaps it by area. A shared edge or a parallel plane is not a source.
  - **One source:** its parameter sets are copied.
  - **Several (a face spanning two coplanar elements):**
    - agreeing values are kept;
    - differing texts are joined with "; ";
    - a differing non-text value (two physical thicknesses) is **not carried**, with Warning `SAM_OCCT_ANALYTICAL_SOURCE_PARAMETER_CONFLICT` (a GH warning). Nothing is picked arbitrarily.
  - **Never carried:** `PanelParameter` values (adiabatic, colour, U-value, ...). Classification and construction stay the rebuild's, exactly as before.
  - **Air:** Air faces keep inheriting their Air source wholesale (unchanged). Air sources never feed a physical face.
  - Info diagnostic `SAM_OCCT_ANALYTICAL_SOURCE_PARAMETERS` with the counts.
- Generic by design: it reads parameter sets and names, not SAM#194's enum, so this PR still builds against `sow/2026-Q4`.
- `InternalsVisibleTo("SAM.OCCT.UnitTests")` on `SAM.Analytical.OCCT`.

### Validation (local)

- Unit: **654 / 0** (+6 `SourceParameterCarrierTests`).
- House integration (`~House`): **16 / 0**. New: `AdjacencyCluster_HouseSourceParameters_SurviveTheRebuild` — 55 rebuilt panels kept their stamp, 0 merged, no conflict; 11 spaces; the Air panel took nothing from the slab.
- Full integration: **258 passed / 19 failed / 2 skipped** (279). The 19 failures are identical by name to a run of the same six classes on this branch's previous head `f901807` with these changes stashed (38 tests: 19 / 19), so none is new.
- Grasshopper contract suite (`SAM.OCCT.GrasshopperTests`): **15 / 0**. It completed normally this time, without the earlier start-up hang.
- Real house (machine-local Rhino 8 harness, the test3.gh chain, SAM#194 + this branch):
  - 11 spaces, 56 panels, 1 Air panel;
  - all 55 physical panels carry source name, layer and `PhysicalThickness` (walls 0.25, partitions 0.10, slabs 0.20, UF ceiling 0.15, roof 0.20 m).
