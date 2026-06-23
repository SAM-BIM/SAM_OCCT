# Plan: True 3D Panel Solver in SAM_OCCT ("backer + width" in 3D)

## Context

SAM already has a panel "solver" (`SAM_Solver` repo) that cleans up analytical
geometry by snapping, trimming, extending and merging panels so they form a
consistent, closed analytical model. **But it is not actually a 3D solver.**

Today's pipeline (`SAM.Geometry.Solver/Classes/SnapSolver.cs`,
`SAM.Analytical.Solver/Classes/Solver.cs`):
1. Takes 3D panels (`Face3D`) + per-panel parameters.
2. **Slices** every panel at each floor level's section height and **projects**
   the cut down to the WorldXY plane, producing 2D wall axes (`SnappedWall`,
   a `Segment2D`).
3. Does all the real work — snapping, bucket-merging, trim/extend, graph build,
   colinear merge, naked-node detection — **in 2D, per level**.
4. Re-lofts the resolved 2D axes back up to `Face3D` between level elevations.

Two per-panel parameters drive the logic:
- **`Weight`** — priority/dominance. Higher-weight elements "win"; lower-weight
  neighbours snap onto them. This is the user's **"backer"** (the element others
  align to).
- **`BucketSize`** — a tolerance band around an axis; neighbours inside the band
  are captured/merged. This is the user's **"width"**.
- (`MaxExtension` drives trim/extend reach.)

This 2D-per-level reduction means the solver cannot correctly handle sloped
panels, roofs, panels that span/cross levels, or anything where the "right"
alignment is genuinely out-of-plane. The goal of this work is a **true 3D
solver**: snap/align panels as faces in full 3D space, reusing the proven
backer(`Weight`) + width(`BucketSize`) semantics, hosted in the `SAM_OCCT`
repo.

### Decisions locked with the user
- **Core capability:** true 3D snapping (faces in 3D, not per-level 2D slices).
- **Parameter mapping:** backer = existing `Weight`; width = existing `BucketSize`.
- **Host repo:** `SAM_OCCT`.
- **Deliverable for now:** this design plan only (no code yet).
- **Target branch (for the eventual implementation):** `sow/2026-Q2`
  (current shared development branch; everything merges to `master` end of June).

## Open items to confirm before coding

1. **Branch `sow/2026-Q2` is not present in this clone** (only `master` exists
   locally and on origin for SAM_OCCT). Before implementation, fetch/confirm the
   exact branch name and base off it. Do **not** use the
   `claude/3d-panel-solver-t0cvlt` branch named in the harness defaults — the
   user overrode it.
2. **No geometry kernel is wired up in SAM_OCCT.** Despite the name (OCCT =
   OpenCascade Technology), the repo is a bare `netstandard2.0` library with only
   `AssemblyInfo` stubs and no OpenCascade reference. This is the single biggest
   architectural fork — see "Kernel choice" below.

## Kernel choice (recommended)

**Recommendation: build the 3D solver on SAM's existing managed
`SAM.Geometry.Spatial` types first (`Face3D`, `Plane`, `Polygon3D`,
`Segment3D`, `Shell`, the existing `Intersecting`/section helpers), and treat a
native OpenCascade binding as a later, optional acceleration — not a
prerequisite.**

Rationale:
- The current solver already does all 3D I/O with `SAM.Geometry.Spatial`
  (`Face3D.Intersecting(plane, out segments)`, `Polygon3D`, `Plane.Convert`).
  Reusing it keeps the new solver consumable from Rhino/Grasshopper (.NET
  Framework) without dragging in native x64 OpenCascade binaries, which do not
  fit `netstandard2.0` and complicate the GH plugin deployment.
- A real OpenCascade.NET integration is a substantial, separate workstream
  (native libs, platform targeting, packaging) and would block delivering the
  solver logic. It can be added behind the same API later if boolean-solid
  robustness demands it.
- This keeps "what is SAM_OCCT" honest: we add the 3D solver namespace now; the
  OCCT kernel binding becomes a follow-up if/when needed.

> If the user actually wants the OpenCascade binding stood up as part of this
> work, that is a materially larger effort and should be split into its own SOW
> item. Flag at implementation kickoff.

## Design

### New projects (mirror SAM_Solver's layering)
Add to `SAM_OCCT.sln`, following the existing SAM project conventions
(`netstandard2.0`, SPDX header from `COPYRIGHT_HEADER.txt`, output to `build\`):

- `SAM_OCCT/SAM.Geometry.OCCT.Solver` — core 3D solver (no Rhino/GH refs).
- `SAM_OCCT/SAM.Analytical.OCCT.Solver` — analytical wrapper over panels.
- `Grasshopper/SAM.Analytical.Grasshopper.OCCT.Solver` — GH component
  (follow-up; not required for the core deliverable).

### Core class: `SnappedPanel` (3D analogue of `SnappedWall`)
Model each panel as its supporting **`Plane`** + a planar boundary
(`Polygon3D`/`Face3D`) instead of a `Segment2D` axis. Carry the same fields as
`SnappedWall`: `Weight` (backer), `BucketSize` (width), `MaxExtension`,
source indices/faces, naked-edge status.

Direct analogues of the 2D methods, lifted to 3D:
| 2D today (`SnappedWall`)            | 3D analogue (`SnappedPanel`)                                   |
|-------------------------------------|----------------------------------------------------------------|
| colinear test on `Segment2D`        | **coplanar** test (plane normal angle + plane-to-plane dist)   |
| `BucketContains` (band around axis) | panel inside a **3D slab** of half-thickness `BucketSize` around backer plane |
| `TryBucketSnap` (snap axis→axis)    | snap candidate panel onto backer **plane** (project boundary)  |
| `ExplodeWallsAtIntersections` (pt)  | split panels along **plane∩plane intersection lines/edges**     |
| trim/extend axes in-plane           | trim/extend panel **boundaries** to neighbour planes (3-way edges) |
| naked **end points**                | naked **boundary edges**                                       |

### Core class: `Panel3DSnapSolver` (3D analogue of `SnapSolver`)
Same orchestration sequence as `SnapSolver.Execute()`, but grouping by
**plane/orientation cluster** instead of by elevation level:
1. Register panels → `SnappedPanel` list.
2. Snap & merge coplanar panels within `BucketSize` slab, backer (`Weight`) wins.
3. Trim/extend to neighbouring planes; resolve **3-way junctions** where three
   panels share an edge (the case "on 3 elements").
4. Explode at plane intersections; mark naked edges; output resolved `Face3D`s
   plus source mapping.

Reuse the existing `Range<double>`, tolerance constants, and the
`AdjustListLength` parameter-padding pattern from `SnapSolver`.

### Analytical wrapper (`SAM.Analytical.OCCT.Solver`)
Mirror `SAM.Analytical.Solver/Classes/Solver.cs` and `Modify.Snap`: accept
`IFace3DObject`/`Panel`, pull `Weight`/`BucketSize` via the existing
`SolverParameter` enum and `SetWeights`/`SetBucketSizes`, optionally derive width
from real construction thickness via the existing `Thickness` query
(`SAM.Analytical.Solver/Query/Thickness.cs`) as a future enhancement.

## Critical files to study/reuse during implementation
- `SAM_Solver/SAM.Geometry.Solver/Classes/SnapSolver.cs` — orchestration to port.
- `SAM_Solver/SAM.Geometry.Solver/Classes/SnappedWall.cs` — per-element logic to lift to 3D.
- `SAM_Solver/SAM.Geometry.Solver/Classes/{ExtensionSolver,GraphSolver}.cs` — trim/extend + graph patterns.
- `SAM_Solver/SAM.Analytical.Solver/Classes/Solver.cs` + `Modify/Snap.cs`,
  `Modify/{SetWeights,SetBucketSizes,SetMaxExtends}.cs`,
  `Enums/Parameter/SolverParameter.cs`, `Query/Thickness.cs` — analytical wrapper + params.
- `SAM_OCCT/SAM_OCCT.sln`, `SAM_OCCT/SAM.Core.OCCT/SAM.Core.OCCT.csproj`,
  `COPYRIGHT_HEADER.txt` — project conventions to copy.

## Verification (when code lands)
- Solution builds on Windows (CI workflow `build.yml`) after adding the new
  projects to `SAM_OCCT.sln`.
- Unit-style smoke test: feed a small set of panels forming a 3-way junction
  (two walls + a floor sharing an edge) with differing `Weight`/`BucketSize` and
  assert the lower-weight panels snap to the backer plane and the shared edge is
  resolved once (no duplicate/gapped edges, naked edges only at true boundaries).
- Compare against the 2D solver on a vertical-walls-only case: results should
  match the current per-level output (regression guard).
- Eventually exercise via the Grasshopper component in Rhino on a real model.

## Out of scope (call out, do not build now)
- Native OpenCascade.NET binding / boolean-solid kernel (separate SOW item).
- Grasshopper/Rhino UI components (follow-up after core is proven).
- Deriving width from construction thickness (future enhancement).
