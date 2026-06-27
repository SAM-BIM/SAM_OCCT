# Plan: True 3D Panel Solver in SAM_OCCT ("backer + width" in 3D)

## Context

SAM has a panel "solver" (`SAM_Solver` repo) that cleans up analytical geometry by
snapping, trimming, extending and merging panels into a consistent closed model.
**But it is not a 3D solver.** Today's pipeline
(`SAM.Geometry.Solver/Classes/SnapSolver.cs`, `SAM.Analytical.Solver/Classes/Solver.cs`):

1. Takes 3D panels (`Face3D`) + per-panel parameters.
2. **Slices** every panel at each level's section height and **projects** the cut to
   WorldXY, producing 2D wall axes (`SnappedWall` wrapping a `Segment2D`).
3. Does all the real work — snap, bucket-merge, trim/extend, graph dedup, colinear
   merge, naked-node detection — **in 2D, per level**.
4. Re-lofts the resolved 2D axes back to `Face3D` between level elevations.

Two per-panel parameters drive the logic:
- **`Weight`** — priority/dominance ("backer"): higher-weight elements win; lower
  ones snap onto them.
- **`BucketSize`** — tolerance band around an axis ("width"): neighbours inside the
  band are captured/merged.
- (`MaxExtend` drives trim/extend reach.)

The 2D-per-level reduction cannot correctly handle sloped panels, roofs, panels that
span/cross levels, or any case where the right alignment is out-of-plane. The goal is
a **true 3D solver**: snap/align panels as faces in full 3D space, reusing the proven
backer(`Weight`) + width(`BucketSize`) semantics, hosted in `SAM_OCCT`.

### Decisions locked with the user
- **Core capability:** true 3D snapping (faces in 3D, not per-level 2D slices).
- **Parameter mapping:** backer = existing `Weight`; width = existing `BucketSize`.
- **Host repo:** `SAM_OCCT`, branch `sow/2026-Q2`.
- **Engine:** **OCCT-native first.** Route junction resolution, intersection
  splitting, coplanar merge and naked-edge detection through the existing native
  OCCT kernel. Managed code is limited to backer/width clustering + plane projection.
- **Deliverable for this task:** this design plan (no code yet).

## Current state of SAM_OCCT (the foundation already exists)

Unlike when the first draft of this plan was written, SAM_OCCT now ships a
**complete native OpenCascade 8.0 kernel** (`SAM.Occt.Native.dll`, ABI v3) wired via
P/Invoke, with a managed API and 30 integration-test classes. The 3D solver builds on
this — it does **not** need a new kernel and does **not** fall back to managed-only
geometry.

Relevant existing building blocks (reuse, do not reinvent):

| Need in 3D solver | Existing native/managed entry point | File |
|---|---|---|
| Section a shell by planes / build cells from faces | `Query.ShellSectionByPlanes(shell, planes, out sectionFace3Ds, out result, options, angleTol, distTol)` | `SAM.Geometry.OCCT/Query/ShellSectionByPlanes.cs` |
| Build shells/solids from a face set (MakerVolume) | `Create.Shells(...)`; native `sam_occt_shape_make_volume(_ex)` | `SAM.Geometry.OCCT/Create/Shells.cs` |
| Split coincident faces into shared sub-faces (junctions) | `Query.ShellsImprint(...)` | `SAM.Geometry.OCCT/Query/ShellsImprint.cs` |
| Merge adjacent coplanar faces (colinear/coplanar dedup) | `Query.MergeCoplanar(...)` (UnifySameDomain) | `SAM.Geometry.OCCT/Query/MergeCoplanar.cs` |
| Sew near-touching face soup + heal | `Query.Sew(...)` | `SAM.Geometry.OCCT/Query/Sew.cs` |
| Naked/free boundary edges, watertightness | `Query.Validate(...)`, `Query.Watertightness(...)` | `SAM.Geometry.OCCT/Query/Validate.cs`, `Watertightness.cs` |
| Gap/clash distance + closest points | `Query.ShellsDistance(...)` | `SAM.Geometry.OCCT/Query/ShellsDistance.cs` |
| Boolean union/diff/intersect | `Query.ShellsUnion/Difference/Intersection(...)` | `SAM.Geometry.OCCT/Query/Shells*.cs` |
| Persistent shape handle (chain ops natively) | `OcctTopology : SafeHandle` | `SAM.Geometry.OCCT/Classes/OcctTopology.cs` |
| Build options / tolerances / glue mode | `OcctBuildOptions`, `OcctGlueMode` | `SAM.Core.OCCT/Classes/OcctBuildOptions.cs` |
| Triangulate non-planar quads (native make_face is planar-only) | `Create.Triangulate(...)` | `SAM.Geometry.OCCT/Create/Triangulate.cs` |
| Analytical adjacency cluster from topology | `SAM.Analytical.OCCT.Create.AdjacencyCluster(...)` | `SAM.Analytical.OCCT/Create/AdjacencyCluster.cs` |

**Key consequence:** most of the 2D solver's hand-rolled steps map onto native ops
the kernel already performs. `ExplodeWallsAtIntersections`, `TrimAndExtendWalls`
(3-way junctions), `CreateGraph`/`MergeColinearWalls` and naked-node detection are
covered by MakerVolume + `ShellsImprint` + `MergeCoplanar` + `Validate` free-bounds.
The only genuinely new logic is the **backer/width snapping** that pre-conditions the
face set. We therefore do **not** port `ExtensionSolver.cs` or `GraphSolver.cs`.

## Implemented pipeline — two steps

The solver runs in two explicit steps so the clean geometry can be reviewed and the bucket
tuned **before** any extend, mirroring `SAM_Solver`'s AutoTuneSolver shape (bucket-driven
snap/merge that finds and outputs clean panels).

**Step 1 — clean bucket (managed, native-free): `Panel3DSnapSolver.CleanBucket`**
1. **Strip internal edges** (`SnappedPanel.StripInternalEdges`) — reduce every panel to its
   external shape; window/door openings are discarded (not carried forward).
2. **Bucket snap** (`Snap` + `SnappedPanel.SnapToBacker`) — project each lower-`Weight`
   panel within a higher-`Weight` backer's `BucketSize` slab, and near-parallel, onto the
   backer plane so within-bucket parallels become coplanar.
3. **Coplanar merge** — managed `SAM.Geometry.Spatial.Query.Union(IEnumerable<Face3D>)`:
   coplanar/overlapping faces (incl. a smaller panel contained in a larger one) collapse
   into single panels.
Output: `CleanFace3Ds` — clean single panels. Run in isolation via the analytical
`Modify.Clean3D` entry point (or `Panel3DSnapSolver.StopAfterClean = true`) to tune bucket
values and review the result before Step 2.

**Step 2 — extend + resolve (consumes Step 1):**
- **Fill** floors/roofs out to the walls. `Fill` measures the actual gap from each cap to the
  walls it is short of and grows it to *meet* them (`SnappedPanel.GrowOutwardTo`), instead of a
  blind fixed margin; a cap with no wall in reach falls back to the fixed `GrowOutward(margin)`.
- **Extend** walls between floors and up to roofs (`Extend`/`ExtendTopTo`/`ExtendBottomTo`).
- **Resolve** through the native kernel: coplanar pre-merge → MakerVolume (`Create.Shells`)
  → `MergeCoplanar` → **adaptive sew** (re-`Query.Sew` the resolved faces at an expanded,
  clamped tolerance to stitch residual floor/wall slot gaps; kept only when it reduces the naked
  count) → `Validate`. Residual naked-boundary loops are then closed by `GapFill` (air-panel
  candidates) — planar loops as a single face, non-planar loops fan-triangulated so the warped
  floor/wall perimeter still closes. `Modify.Solve3D` runs Step 1 then Step 2.

Sloped-roof–specific heuristics remain out of scope; the kernel does all cutting (Step 1
never splits faces).

## Design

### New projects (mirror SAM_OCCT conventions: `netstandard2.0`, SPDX header, output to `build/`)
- `SAM_OCCT/SAM.Geometry.OCCT.Solver` — core 3D panel solver (no Rhino/GH refs);
  references `SAM.Core.OCCT`, `SAM.Geometry.OCCT` and SAM core/geometry.
- `SAM_OCCT/SAM.Analytical.OCCT.Solver` — analytical wrapper over `Panel`/`IFace3DObject`.
- `Grasshopper/SAM.Analytical.Grasshopper.OCCT.Solver` (`net8.0-windows`) — GH
  component (follow-up; not required for the core deliverable).
Add all to `SAM_OCCT.sln`.

### Core class: `SnappedPanel` (3D analogue of `SnappedWall`)
Model each panel as its supporting **`Plane`** + planar boundary (`Face3D`) instead of
a `Segment2D` axis. Carry the same fields as `SnappedWall`: `Weight` (backer),
`BucketSize` (width), `MaxExtension`, source index/indices, source `Face3D`s, naked
status — but on faces, not segments.

| 2D today (`SnappedWall`)                | 3D analogue (`SnappedPanel`)                                              | Provided by |
|-----------------------------------------|---------------------------------------------------------------------------|-------------|
| colinear test on `Segment2D`            | **coplanar** test: normal-angle ≤ tol AND plane-to-plane distance ≤ tol   | managed (`Plane`) |
| `BucketContains` (band around axis)     | panel center/boundary inside a **3D slab** of half-width `BucketSize` about backer plane | managed |
| `TryBucketSnap` (snap axis→axis)        | project lower-`Weight` panel boundary onto the backer **plane**           | managed (`Plane.Project`) |
| `ExplodeWallsAtIntersections`           | split faces along plane∩plane lines                                        | **native** MakerVolume / `ShellsImprint` |
| `TrimAndExtendWalls` (3-way junctions)  | resolve 3-way edges where panels meet                                      | **native** MakerVolume |
| `CreateGraph` + `MergeColinearWalls`    | merge coplanar adjacent faces                                              | **native** `MergeCoplanar` |
| naked **end points**                    | naked **boundary edges**                                                   | **native** `Validate` free-bounds |

### Core class: `Panel3DSnapSolver` (3D analogue of `SnapSolver`)
Same backer/width semantics as `SnapSolver`, but grouped by **plane/orientation
cluster** instead of by elevation level. `Execute()`:

1. **Register** panels → `SnappedPanel` list; `AdjustListLength` for params (reuse the
   pad pattern; defaults BucketSize 0.3, Weight 1.0, MaxExtension 0.5).
2. **Cluster** panels into near-coplanar groups (normal angle + plane offset within
   tolerance). This replaces "group by elevation".
3. **Snap (managed):** within each cluster, sort by `Weight` desc; for each
   lower-weight panel whose boundary lies within the `BucketSize` slab of a higher
   `Weight` "backer", project its boundary onto the backer plane (analogue of
   `TryBucketSnap` / `BucketContains`).
4. **Resolve (native):** hand the snapped `Face3D` set to the native engine —
   `Create.Shells` / `ShellSectionByPlanes` (MakerVolume) splits at mutual
   intersections and resolves 3-way junctions; `ShellsImprint` for coincident-face
   sub-splitting; `MergeCoplanar` to merge resolved coplanar faces.
5. **Report:** run `Validate`/`Watertightness` to flag naked (free) boundary edges
   (true boundaries vs gaps); emit resolved `Face3D`s + source mapping + naked-edge
   diagnostics. Use `ShellsDistance` to quantify residual gaps if needed.

Reuse `Core.Range<double>`, the tolerance pattern, and `AdjustListLength` from
`SnapSolver`. Drive native tolerances via `OcctBuildOptions` (distance/fuzzy/glue).

### Analytical wrapper (`SAM.Analytical.OCCT.Solver`)
Mirror `SAM.Analytical.Solver/Classes/Solver.cs` and `Modify/Snap.cs`: accept
`IFace3DObject`/`Panel`; pull `Weight`/`BucketSize`/`MaxExtend` via the existing
`SolverParameter` enum and `SetWeights`/`SetBucketSizes`/`SetMaxExtends` helpers
(`SAM_Solver/SAM.Analytical.Solver/Modify/*`); skip air panels via `Query.Air`; derive
width from real construction thickness via `Query/Thickness.cs` (thickness × 0.6,
min 0.2) — directly, since 3D needs no per-level re-derivation.

## Critical files to study/reuse during implementation
- Port: `SAM_Solver/SAM.Geometry.Solver/Classes/SnapSolver.cs` (orchestration),
  `SnappedWall.cs` (per-element logic to lift to faces).
- Params: `SAM_Solver/SAM.Analytical.Solver/{Classes/Solver.cs, Modify/Snap.cs,
  Modify/SetWeights.cs, Modify/SetBucketSizes.cs, Modify/SetMaxExtends.cs,
  Enums/Parameter/SolverParameter.cs, Query/Thickness.cs, Query/Air.cs}`.
- Native engine to consume: `SAM_OCCT/SAM.Geometry.OCCT/Query/{ShellSectionByPlanes,
  ShellsImprint, MergeCoplanar, Sew, Validate, Watertightness, ShellsDistance}.cs`,
  `Create/{Shells, Triangulate}.cs`, `Classes/OcctTopology.cs`,
  `SAM.Core.OCCT/Classes/OcctBuildOptions.cs`.
- Conventions: `SAM_OCCT/SAM_OCCT.sln`,
  `SAM_OCCT/SAM.Core.OCCT/SAM.Core.OCCT.csproj`, `COPYRIGHT_HEADER.txt`, `TESTING.md`.
- Do **not** port `ExtensionSolver.cs` / `GraphSolver.cs` — superseded by the native engine.

## Verification (when code lands)
- Solution builds on Windows (CI `build.yml`) after adding the new projects to
  `SAM_OCCT.sln`; every new `.cs` has the SPDX header.
- **Unit tests** (`Testing/SAM.OCCT.UnitTests`, pure managed, no native DLL): coplanar
  clustering, `BucketSize`-slab containment, backer-plane projection — assert
  lower-`Weight` panels project onto the backer plane and clustering groups expected
  faces.
- **Integration tests** (`Testing/SAM.OCCT.IntegrationTests`, gated by
  `NativeProbe.Available`, bootstrapped by `NativeRuntimeBootstrap`): feed a 3-way
  junction (two walls + a floor sharing an edge) with differing `Weight`/`BucketSize`;
  assert the shared edge resolves once (no duplicate/gapped edges) and `Validate`
  reports naked edges only at true boundaries.
- **Regression vs 2D solver:** on a vertical-walls-only model, the 3D solver's
  resolved faces should match the current per-level output.
- Run the real native engine locally via committed `build/` DLLs + the integration
  bootstrap (see `reference_sam_occt_local_native_repro`).
- Eventually exercise via the Grasshopper component in Rhino on a real model.

## Out of scope (call out, do not build now)
- Grasshopper/Rhino UI component (follow-up after core is proven).
- Sloped-roof–specific heuristics beyond generic 3D snapping (separate task; see
  `Create/ShellsFromVerticalFace3Ds.cs` and OcctRoofMode for prior art).
- Deriving width from construction thickness in the analytical wrapper is included
  (not deferred) since 3D needs no per-level re-derivation.
