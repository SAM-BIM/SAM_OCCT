# Modeling Guide

This guide describes the recommended SAM_OCCT workflow for generating reliable
closed building geometry and analytical adjacency clusters.

## Recommended Default

For analytical building models, generate clean `Panel` or `Face3D` boundary
geometry first, then let OCCT find the closed cells.

```text
Panels / Face3Ds
-> SAMOCCT.CreateAdjacencyCluster
-> AdjacencyCluster
```

This is usually more robust than creating closed polysurfaces first because the
panels/faces describe the actual analytical boundaries: walls, floors, roofs,
partitions, shafts, and atrium boundaries.

For debugging:

```text
Panels / Face3Ds
-> SAMOCCT.CreateShells
-> inspect Shells
-> SAMOCCT.CreateAdjacencyClusterByShells
```

## When To Use Shells

Use `Shell` workflows when you are sculpting or editing closed space volumes.

Example:

```text
room box shells
-> SAMOCCT.ShellsDifference with roof cutter
-> SAMOCCT.ShellsSectionByPlane for atriums or levels
-> SAMOCCT.ShellsRepair if needed
-> SAMOCCT.CreateAdjacencyClusterByShells
```

This is a good workflow for conceptual massing, roof-shaped spaces, atrium
division, shafts, and other volume-first modeling.

### Cleaning Up Tiny Faces After Sectioning

`SAMOCCT.ShellsSectionByPlane` can leave very small sliver faces on the level
shells it produces (e.g. a stray face of `0.000079` m²). Feed those shells into
`SAMOCCT.ShellsRepair` with `minArea_` set to remove them.

Internally this uses OCCT **defeaturing** (`BRepAlgoAPI_Defeaturing`): faces
below `minArea_` (in m²) are removed and the **neighbouring faces are extended to
fill the gap**, so each shell stays a closed solid. This is why simply deleting a
face and rebuilding does not work — once a face is gone the volume is no longer
bounded and OCCT cannot reconstruct the cell. The default `minArea_` is `0.01`
m²; set it to `0` to repair without removing any face. The number of detected
sub-threshold faces is reported on the `Diagnostics` output
(`SAM_OCCT_REPAIR_SMALL_FACES`).

Do not union adjacent room shells before creating an adjacency cluster if each
room should remain a separate space. Union is for merging volumes into a larger
solid, not for preserving individual rooms.

### Mesh Input (Rhino Mesh / SAM Mesh3D)

`SAMOCCT.CreateAdjacencyClusterByShells` accepts more than SAM `Shell`s and closed
Rhino Breps on `_shells`. It also takes **Rhino Meshes** and **SAM `Mesh3D`s**, so
you can drive the analytical model straight from mesh massing (SubD output, imported
meshes, mesh-based conceptual tools) without first converting to Breps.

Each `_shells` list item is one intended space/cell. A mesh is not a closed Brep, so
its triangle faces are assembled into a single `Shell` (one mesh = one volume). You
can mix meshes, Shells, and closed Breps in the same list.

Because a mesh is a triangle soup, three things happen automatically:

- **Welding** (`weldMesh_`, default on). A mesh exported unwelded repeats each shared
  corner once per face (e.g. 1594 vertices for ~499 unique positions). The triangles
  are rebuilt through a shared vertex list at `tolerance_` so coincident corners
  become one vertex and shared edges line up exactly, and degenerate slivers are
  dropped. Reported as `SAM_OCCT_ANALYTICAL_SHELL_MESH_WELD`. This cleans unwelded
  meshes but **cannot** fix T-junctions (a vertex sitting partway along another
  triangle's edge) or self-intersecting/overlapping faces — those must be repaired on
  the source mesh (in Rhino: `Weld`, mesh check/repair, `FillMeshHoles`, or
  `QuadRemesh` to regenerate a clean conforming mesh).

- **Sewing is always on for mesh input.** The faces are run through native
  sew-and-heal before `BOPAlgo_MakerVolume`, so coincident triangle edges become
  shared topology instead of relying on MakerVolume's fuzzy-tolerance guesswork. The
  `sew_` toggle additionally forces sewing for Shell / closed Brep input.
- **A watertightness pre-check runs per mesh.** A mesh that is not a closed volume
  is the commonest cause of an opaque build failure, so each mesh shell is checked
  for naked (open) edges up-front and reported on `Diagnostics`
  (`SAM_OCCT_ANALYTICAL_SHELL_MESH_OPEN`) before the build. If a mesh is open,
  repair it so it is watertight (close holes, weld vertices) — sewing will try to
  bridge small gaps but cannot invent a missing face.

The downstream OCCT build merges the coplanar triangles back into clean planar
panels (`UnifySameDomain`), so a 600-triangle mesh box still yields 6 wall panels,
not 600. For a meshed curved surface, expect one planar panel per facet — there is
no NURBS recovery from a mesh.

#### Breps with curved faces — `meshInput_`

A Brep whose faces are curved (NURBS) often **fails** to build directly. The
Brep → SAM `Shell` conversion approximates each curved face with planar faces, and
those can come out self-intersecting and not watertight, so OCCT's `MakerVolume`
returns status 40 and the diagnostics report something like *"INVALID, NOT
watertight; 128 naked edges, 194 self-intersections"*.

Set `meshInput_ = true` to fix this: the component tessellates each Brep/surface
with Rhino's `BRepMesh`, then **welds the seams, fills small gaps, and unifies
winding** into one clean watertight planar triangle mesh **before** the OCCT build,
bypassing the lossy Brep → Shell conversion. The mesh then follows the mesh path
above (always sewn, watertightness pre-checked). `meshDeflection_` controls how
closely the mesh hugs curvature (smaller = finer; flat faces stay coarse). Rhino
Meshes and SAM Shells/Mesh3Ds are unaffected by this toggle. If a Brep will not
mesh into a closed solid even after repair (e.g. the source is open or has gaps
wider than `meshDeflection_`), that is reported as
`SAM_OCCT_ANALYTICAL_SHELL_MESH_BREP_OPEN`.

Use it whenever curved-face Breps will not close; leave it off for clean planar
Breps, where the direct Shell path is exact and cheaper.

### Space Names And Metadata

`SAMOCCT.CreateAdjacencyClusterByShells` can create spaces directly from shells,
or it can reuse metadata from existing SAM `Space` objects.

Use `spaces_` when:

- You already have SAM `Space` objects with names, internal conditions, or other
  metadata.
- Each existing space has a valid location point inside its matching shell.
- You want the OCCT shell workflow to preserve those space properties while
  rebuilding the analytical cluster.

Use `names_` when:

- You only have shells but want predictable space names.
- You do not need to preserve full existing `Space` metadata.
- The name list follows the same order as the shell list.

If both are supplied, `spaces_` matching is used first. `names_` is used only for
shells that do not match an existing space. If neither is supplied, the component
creates names like `Cell 1`, `Cell 2`, and so on.

Useful diagnostics:

- `SAM_OCCT_ANALYTICAL_SHELL_MESH_BREP`: reports how many Brep/surface inputs
  `meshInput_` tessellated with Rhino's mesher before the build, and at what
  deflection.
- `SAM_OCCT_ANALYTICAL_SHELL_MESH_INPUT`: reports how many shells were assembled
  from mesh input (Rhino Mesh / SAM Mesh3D / meshed Brep) and how many the
  watertightness pre-check flagged as open.
- `SAM_OCCT_ANALYTICAL_SHELL_MESH_WELD`: reports that mesh input was welded to a
  shared vertex set (`weldMesh_`), with the resulting vertex and triangle counts.
- `SAM_OCCT_ANALYTICAL_SHELL_MESH_OPEN`: warns that a specific mesh input shell is
  not a closed volume, with its naked-edge / non-manifold counts so you know which
  mesh to repair.
- `SAM_OCCT_ANALYTICAL_SHELL_SEW`: reports that sew-and-heal before MakerVolume is
  on (always for mesh input, or when `sew_` is set for Shell / Brep input).
- `SAM_OCCT_SEW_SKIPPED_LARGE_INPUT`: reports that panel/face input was too large
  for the pre-build native sew-and-heal safety guard, so the full model was built
  once through the direct MakerVolume path instead. This is not batching. The
  default guard is `MaxSewFaceCount = 2048`: an empirical safety value chosen well
  above the existing regression fixtures (largest current fixture is under 300
  panels) and below the Talybont crash case (6927 valid panels), where OCCT sewing
  stack-overflowed before returning to managed code. It is not an OCCT hard limit;
  managed callers can raise it or set it to `0` to force sew for controlled testing.
- `SAM_OCCT_ANALYTICAL_SHELL_METADATA`: reports supplied spaces/names and the
  shell-native matching/naming strategy.
- `SAM_OCCT_ANALYTICAL_PANEL_METADATA`: reports supplied panels/spaces and the
  panel-based matching/naming strategy.
- `SAM_OCCT_ANALYTICAL_INPUT`: reports the shell-face or panel count, seed
  space count, and optional name count sent into the OCCT adjacency builder.
- `SAM_OCCT_ANALYTICAL_BUILD`: reports that SAM adjacency creation is starting
  from decoded OCCT cells.
- `SAM_OCCT_ANALYTICAL_CELL_SPACE_DELTA`: warns when seed-space count differs
  from OCCT cell count.
- `SAM_OCCT_ANALYTICAL_REBUILD_SPACE_DELTA`: warns when final SAM space count
  differs from OCCT shell count.
- `SAM_OCCT_TOPOLOGY`: reports how many shared OCCT face adjacency relations
  were decoded from the native cell result.
- `SAM_OCCT_CELL_CENTERS`: reports how many decoded OCCT cells exported a
  native center candidate.
- `SAM_OCCT_ANALYTICAL_SPACE_LOCATIONS`: reports whether spaces used native
  OCCT centers, decoded-shell bounding-box centers, or the slower SAM internal
  point fallback.
- `SAM_OCCT_ANALYTICAL_DIRECT_SUCCESS`: reports that the SAM adjacency cluster
  was built directly from OCCT cell-face topology.
- `SAM_OCCT_ANALYTICAL_DIRECT_FALLBACK`: reports that direct topology rebuild
  could not be used and SAM fell back to geometric adjacency rebuild.
- `SAM_OCCT_ANALYTICAL_PANEL_SUCCESS`: reports final space/panel counts from
  `SAMOCCT.CreateAdjacencyCluster`.
- `SAM_OCCT_ANALYTICAL_PANEL_REBUILD_FAILED`: reports that
  `SAMOCCT.CreateAdjacencyCluster` could not produce a valid adjacency cluster.
- `SAM_OCCT_TIMING_OCCT_AND_ADJACENCY`: time spent in the OCCT cell build and
  SAM adjacency creation.
- `SAM_OCCT_TIMING_NATIVE_CELL_BUILD`: time spent in native OCCT cell creation
  and managed cell decode.
- `SAM_OCCT_TIMING_DIRECT_REBUILD`: time spent building SAM spaces, panels, and
  relations directly from OCCT topology.
- `SAM_OCCT_TIMING_GEOMETRIC_REBUILD`: time spent in fallback SAM geometric
  rebuild, only reported when direct topology rebuild cannot be used.
- `SAM_OCCT_TIMING_POST_PROCESS`: time spent cutting at ground elevation,
  updating panel types, and assigning default constructions.
- `SAM_OCCT_TIMING_TOTAL`: total component time.

## Cleaning Small Spaces

Cell-complex and shell workflows can produce unwanted tiny cells: slivers
between near-coincident faces, leftover voids, or modelling offcuts. Use
`SAMOCCT.MergeSmallSpaces` to fold these into the best adjacent larger space.

```text
Panels / Shells
-> SAMOCCT.CreateAdjacencyCluster / SAMOCCT.CreateAdjacencyClusterByShells
-> SAMOCCT.MergeSmallSpaces
-> cleaned AdjacencyCluster
```

What it does:

1. Finds spaces with floor area below `minArea_` or volume below `minVolume_`.
2. For each small space, looks at the spaces it shares an internal boundary
   with.
3. Rejects unsafe targets: protected spaces (`protectedSpaces_`, such as shafts
   and risers), volumeless/void external cells (unless `allowMergeExternal_`),
   and vertically stacked neighbours on a different level.
4. Picks the best remaining target according to `mergeMode_`:
   - `LongestSharedBoundary`: largest shared panel/boundary area (default).
   - `LargestNeighbour`: largest neighbouring floor area, then volume.
   - `SameTypeFirst`: same space type first, then largest shared boundary.
5. Merges the small space into the target, dropping the now-internal shared
   panels and re-relating the small space's remaining panels to the target.
6. Recalculates volume, panel types, and constructions on the cleaned cluster.

Inputs:

- `_adjacencyCluster`: the cluster to clean.
- `minArea_`: minimum acceptable floor area in m² (default `0.3`).
- `minVolume_`: optional minimum acceptable volume in m³ (default `0.5`; leave
  unset to ignore volume).
- `tolerance_`: distance tolerance for geometry and level comparisons.
- `mergeMode_`: target selection strategy.
- `allowMergeExternal_`: allow merging into volumeless/void external cells
  (default `false`).
- `protectedSpaces_`: spaces that must never be merged away or used as a target.

Outputs:

- `adjacencyCluster`: the cleaned cluster.
- `mergedSpaces`: small spaces that were merged away.
- `unmergedSmallSpaces`: small spaces that could not be merged.
- `report`: coded diagnostics for every decision.

Useful diagnostics:

- `SAM_OCCT_MERGE_PARAMETERS`: the thresholds and options used for the run.
- `SAM_OCCT_MERGE_CANDIDATES`: how many small spaces were found.
- `SAM_OCCT_MERGE_MERGED`: each small space that was merged, its target, and the
  shared boundary area used.
- `SAM_OCCT_MERGE_UNMERGED`: each small space that could not be merged and why
  (protected, no shared boundary, or no safe target).
- `SAM_OCCT_MERGE_DROPPED_PANELS`: how many now-internal shared panels were
  removed.
- `SAM_OCCT_MERGE_RESULT`: final space/panel counts versus the input.

Do not set `minArea_` or `minVolume_` so high that real rooms are swallowed.
Merge thresholds are for slivers and offcuts, not for combining valid spaces.

### Cleaning Small Shells (volume-first)

When you are still working with closed volumes and have not built an analytical
model yet, use `SAMOCCT.MergeSmallShells` to remove tiny shells before adjacency
creation. It is the shell/Brep counterpart of `SAMOCCT.MergeSmallSpaces`.

```text
room/cell shells (or closed Breps)
-> SAMOCCT.MergeSmallShells
-> cleaned shells
-> SAMOCCT.CreateAdjacencyClusterByShells
```

The supplied shells are decoded into an OCCT cell complex first, so the merge
works on real per-cell volumes and real shared-face adjacency rather than
bounding-box estimates.

What it does:

1. Decodes the input shells into OCCT cells (true volume, faces, and shared-face
   adjacency).
2. Finds cells with floor footprint below `minArea_` or volume below `minVolume_`.
3. For each small cell, looks at the cells it shares a face with.
4. Skips protected shells (`protectedShells_`, matched to decoded cells by
   containment) as both candidates and targets.
5. Picks the best neighbour according to `mergeMode_`:
   - `LongestSharedBoundary`: largest shared face boundary area (default).
   - `LargestNeighbour`: largest neighbouring footprint, then volume.
6. Fuses each small cell with its chosen neighbour using OCCT `ShellsUnion`
   (`tolerance_` and `fuzzyTolerance_` control the build and union).

Inputs: `_shells`, `minArea_`, `minVolume_`, `tolerance_`, `fuzzyTolerance_`,
`mergeMode_`, `protectedShells_`.

Outputs: `Shells` (cleaned), `mergedSmallShells`, `unmergedSmallShells`,
`report`.

Notes:

- Because the inputs are decoded into a cell complex, the outputs are the decoded
  (and merged) cells. OCCT may re-partition overlapping or shared faces, so the
  output cell count can differ from the input shell count; the
  `SAM_OCCT_MERGE_SHELLS_TOPOLOGY` diagnostic reports both.
- `minVolume_` and `minArea_` compare the true OCCT cell volume and floor
  footprint, so the thresholds are meaningful even for sloped or L-shaped cells.
- Merging needs native OCCT. If it is unavailable the cell decode produces no
  cells; the component then returns the input unchanged and records the OCCT
  diagnostics in `report` so no geometry is lost.

Useful diagnostics: `SAM_OCCT_MERGE_SHELLS_PARAMETERS`,
`SAM_OCCT_MERGE_SHELLS_TOPOLOGY`, `SAM_OCCT_MERGE_SHELLS_CANDIDATES`,
`SAM_OCCT_MERGE_SHELLS_MERGED`, `SAM_OCCT_MERGE_SHELLS_UNMERGED`, and
`SAM_OCCT_MERGE_SHELLS_RESULT`.

## Panels/Faces vs Shells

Use `Panel`/`Face3D` when:

- You are generating an analytical building model.
- You know the intended wall, floor, roof, and partition boundaries.
- You want OCCT to find closed cells from boundary faces.
- You need shared boundaries and adjacency relations.

Use `Shell` when:

- Each closed volume already represents one space or mass.
- You need booleans such as union, difference, or intersection.
- You need to trim spaces by roof, atrium, shaft, or void volumes.
- You need to split closed volumes before analytical conversion.

## Tool Shells and Cutter Shells

`_toolShells` and `_cutterShells` must be closed volumes.

Accepted inputs:

- SAM `Shell`
- closed Rhino Brep / polysurface that converts to a SAM `Shell`

Not accepted directly:

- single surface
- open polysurface
- `Face3D`
- loose faces

If you only have surfaces or `Face3D`s, create closed shells first:

```text
Face3Ds / surfaces
-> SAMOCCT.CreateShells
-> SAMOCCT.ShellsIntersection / ShellsDifference / ShellsUnion
```

## Tolerances

Good starting values for meter-based models:

```text
tolerance_      = 0.001
fuzzyTolerance_ = 0.01
```

Interpretation:

- `tolerance_`: regular model tolerance, for example 1 mm.
- `fuzzyTolerance_`: OCCT tolerance for near-touching edges/faces, for example
  10 mm.

Use the smallest fuzzy tolerance that closes the model reliably. Very large
fuzzy tolerances can merge geometry that should remain separate.

In `SAMOCCT.CreateAdjacencyClusterByShells`, the main geometry tolerances are:

- `tolerance_`: base OCCT/SAM model tolerance.
- `fuzzyTolerance_`: OCCT fuzzy tolerance, also used as SAM silver spacing when
  matching existing space locations and as a final fallback for shell internal
  point searches.

The other numeric inputs are advanced SAM rebuild controls, not OCCT build
tolerances:

- `maxDistance_`: compatibility input for legacy geometric rebuilds. The
  shell-native direct topology path normally does not use this value.
- `maxAngle_`: angular tolerance for panel matching.
- `minArea_`: filters tiny faces before and during analytical rebuild.

Leave these at defaults unless diagnostics show missing panels/spaces or noisy
tiny geometry.

## Geometry Rules

For the best chance of a closed OCCT model:

- Keep faces planar.
- Snap vertices to a consistent model tolerance.
- Remove duplicate faces unless they are intentional shared analytical
  boundaries.
- Avoid tiny sliver faces.
- Split faces where major boundaries intersect.
- Keep units consistent.
- Prefer clean generated geometry over manually trimmed messy Breps.
- Do not rely on tolerance to fix large modeling errors.

## Recommended Building Pipeline

For robust analytical buildings:

```text
1. Generate panels/faces from grids, levels, profiles, and roof planes.
2. Snap all vertices to the model tolerance.
3. Remove tiny or invalid faces.
4. Feed panels to SAMOCCT.CreateAdjacencyCluster.
5. Check diagnostics.
6. Debug with SAMOCCT.CreateShells if cells are missing.
7. Use ShellsRepair, ShellsSplit, or ShellsSectionByPlane for special cases.
```

For roof or atrium-heavy volume workflows:

```text
1. Generate simple closed room shells.
2. Use SAMOCCT.ShellsDifference for roof cuts.
3. Use SAMOCCT.ShellsSectionByPlane for atrium or level division.
4. Use SAMOCCT.ShellsRepair if needed.
5. Use SAMOCCT.CreateAdjacencyClusterByShells.
6. Use SAMOCCT.PanelsFromShells when panel objects are needed.
```

## Controlled 3D Workflow: Clean3D → Extend3D (level groups, clean records, exact handoff)

The inspectable staged workflow (`docs/CONTROLLED_WORKFLOW_PLAN.md`) makes every clean/extend decision
visible and tunable before the native resolve:

```text
Panels → SAMOCCT.Clean3D → SAMOCCT.Extend3D (inputAlreadyClean = true) → SAMOCCT.CreateAdjacencyCluster
```

Core APIs and existing saved GH components remain backward-compatible: when `bucketBetweenLevels_` is
absent they use `0` (off), and the other P2 controls remain off. Current/new GH components use `0.21` for
the level-group control; adding the voluntary input to an older component also supplies `0.21`.

### `bucketBetweenLevels_` — merge slab-skin datums into one storey (Clean3D / Extend3D / Solve3D)

A single physical floor is often imported as several near-coplanar cap datums (the fixture's 12.240 m and
12.436 m frames are the same slab's two skins). The raw level frames keep a **pinned 0.15 m band** so a
deliberate ~0.25 m split-level landing is never merged away — so those slab skins stay as separate frames
and walls extend to the wrong plane. `bucketBetweenLevels_` is an **optional wider grouping on top of the
frames** that merges them onto one storey datum for cap normalization and wall-to-cap extension:

- `0`: grouping off; the `LevelGroups` output equals `LevelFrames` (one group per frame). This is the core
  API default.
- `0.21` (generic GH default and the 9-space fixture): the 5 raw frames merge into **3 level groups** at datums 12.24 / 15.29 /
  18.34 — inspect `LevelFrames` (still 5) and `LevelGroups` (now 3) on the Clean3D component to confirm.
- `>= 0.25`: can eat a genuine split-level landing — the CleanReport near-miss lines tell you exactly which
  value would merge a frame that just missed, so raise it deliberately per model. For example,
  `whole-level-towers.sam` can be investigated with `fillMargin_=0.4` / `bucketBetweenLevels_=0.4`; this is
  fixture tuning, not a proposed generic default.

The grouping affects cap normalization and extend targets **only** — never wall bucket membership.
SAM_Solver uses the same parameter name and GH default `0.21`, but its operation is different: a final
cross-level **wall re-snap**. SAM_OCCT merges **level datums** and does not perform that wall re-snap.

### `inputAlreadyClean_` — the exact Clean3D → Extend3D handoff (Extend3D)

`Extend3D` normally runs its own internal clean first, so `Extend3D(Clean3D(panels))` would clean **twice** —
the second pass can move already-clean geometry and re-derive parameters. Set `inputAlreadyClean_ = true`
when you feed Extend3D the Clean3D output: Stage A (clean bucket) is **skipped**, the supplied panels are
treated as the exact clean result (stamped BucketSize/Weight/MaxExtend reused, an identity source map,
frames/groups clustered for reporting only), and only the extend/fill conditioning runs. A
`SAM_OCCT_CLEAN3D_SKIPPED` diagnostic records the bypass. Leave it `false` (default) for the standalone,
byte-identical legacy behaviour.

### `CleanReport` and `LevelGroups` outputs — what the clean bucket did, per panel

- **`LevelGroups`** — one line per storey datum: elevation, the raw frames it merged (and their elevations),
  cap count, spread and tilt.
- **`CleanReport`** — the `SAM_OCCT_CLEAN3D_LEVELS`/`_LEVELGROUP` level summary plus one
  `SAM_OCCT_CLEAN3D_PANEL` line per applied clean action (`opposed-collapsed` / `snapped-to-backer` /
  `cap-normalized` / `coplanar-merged` / `dropped-invalid`), each naming the moved distance, the backer, and
  the resolved **BucketSize / Weight / MaxExtend with their provenance** (`stamped` / `derived-length` /
  `derived-thickness` / `min-floor` / `default`). Recorded at the real decision points — never inferred from
  a geometry diff.

### `directionalCapGrow_` — grow each cap edge only toward a wall that faces it (Extend3D, P3)

When a floor/roof cap is short of its surrounding walls, `Extend3D` grows it outward to close the gap. The
legacy grow (`directionalCapGrow_ = false`, default) offsets the **whole** cap boundary uniformly by the
largest in-reach wall gap — which can push a mid-level cap that borders a double-height void **into** that
void and fabricate a false intermediate floor. Set `directionalCapGrow_ = true` to grow **each straight
edge independently**, by only its own measured gap to a wall that actually faces it; an edge with no facing
wall in reach grows **exactly 0**, so a cap bordering a void is never dragged into it. If the per-edge
reconstruction finds no evidence or fails validation, it falls back to the legacy grow and flags the record
`LegacyUniformCapGrow` (visible, never silent).

### `ExtendReport` SKIP / RISKY lines — why a panel did or didn't move (P3)

`Extend3D`'s `ExtendReport` now records every extend/fill **decision**, not only the moves:

- `SAM_OCCT_EXTEND3D_PANEL:` — an applied move (frozen format): which edge moved, from → to, toward what target.
- `SAM_OCCT_EXTEND3D_SKIP:` — a real decision point that left a panel **untouched**, with the reason:
  `NoTargetWithinReach` / `AlreadyMeetsTarget` / `CappedByLengthRatio` / `TargetAmbiguous` /
  `DegenerateGeometry` / `FillTooSmall`. A panel that stayed put is now traceable, never a silent no-op.
- `SAM_OCCT_EXTEND3D_RISKY:` — metadata on an applied move worth a look: `NearReachLimit`,
  `MaxExtendLimited`, `LengthRatioLimited`, `NewCoplanarOverlap`, `LegacyUniformCapGrow`.

**Tuning MaxExtend with these lines:** a wall's lateral reach is `min(MaxExtend, 0.49 × the wall's own
length)`. If a wall will not close its plan loop, check its line: `MaxExtendLimited` means raising
`SolverParameter.MaxExtend` (via SolverProperties) will help; `LengthRatioLimited` or `CappedByLengthRatio`
means the wall is too short for its own reach and more `MaxExtend` will **not** help — split/lengthen the
wall or fix the neighbour instead. The unstamped default reach is a flat **0.4 m** (a per-panel stamp wins).

### Parameter precedence (bake → SolverProperties → rerun)

A valid **per-panel stamp always wins** over the derived value, which wins over the solver default — for
BucketSize, Weight **and** MaxExtend. To tune a problem panel: run with defaults, read the CleanReport, bake
the panel to Rhino, assign `SolverParameter.BucketSize` / `Weight` / `Max Extend` with SAM_Solver's
**SolverProperties** component, and rerun. The provenance tag on each CleanReport line confirms your stamp
was honoured (it reads `stamped`).

### Which Extend3D input actually changes the geometry? (the input-effect matrix)

An input that has **no effect** on a given run is reported, not silently ignored — so re-running with a
different value and seeing identical geometry is explained. `Extend3D` emits a
`SAM_OCCT_EXTEND3D_INPUT_INERT:` line when the mode makes an input inert, and a
`SAM_OCCT_EXTEND3D_INPUT_OVERRIDDEN:` line when a per-panel stamp overrides one. The two paths differ
sharply:

| Input | Standalone (`inputAlreadyClean = false`) | Chained (`inputAlreadyClean = true`, the Clean3D → Extend3D handoff) |
|---|---|---|
| `minBucketSize_`, `thicknessFactor_` | live — **unless** a panel carries a `BucketSize` stamp (then that panel is overridden) | **inert** (Stage A skipped) — tune on the upstream Clean3D instead |
| `alignColinearOffset_`, `normalizeCapOffset_` | live (clean-stage) | **inert** (Stage A skipped) — tune on Clean3D |
| `bucketBetweenLevels_` | live (cap normalization + extend targets) | **reporting-only** (`LevelGroups`) — the caps were already normalized by Clean3D |
| `fillMargin_` | live (how far caps grow) | **live** |
| `directionalCapGrow_` | live (per-edge vs uniform cap grow) | **live** |
| per-panel `SolverParameter.MaxExtend` stamp | live (lateral wall reach) | live |

**Key point for the controlled chain:** on the `inputAlreadyClean = true` handoff, only `fillMargin_`,
`directionalCapGrow_` and the per-panel stamps change the Extend3D geometry. Everything that shapes the
clean bucket and the level grouping must be set on the **Clean3D** component upstream — Extend3D is only
conditioning the already-clean panels. The `INPUT_INERT` diagnostic on each run states this explicitly.

### The full chain end to end (+ CreateAdjacencyCluster + ValidateSpaces)

The complete controlled chain adds the native rebuild and a GUID-based validation stage:

```text
Panels ─▶ SAMOCCT.Clean3D ─▶ SAMOCCT.Extend3D ─▶ SAMOCCT.CreateAdjacencyCluster ─▶ SAMOCCT.ValidateSpaces
          bucketBetweenLevels  inputAlreadyClean=true   seeds = expected Spaces        _expectedSpaces (+ GUIDs)
          = 0.21               directionalCapGrow=true   (or ExpectedSpaceSet seeds)    doubleHeightSpaces_
                               bucketBetweenLevels=0.21
```

- **`SAMOCCT.CreateAdjacencyCluster`** builds the cells (the native MakerVolume split) from the extended
  panels, seeded by the Spaces you expect. Its diagnostics name two failure modes that used to be silent:
  `SAM_OCCT_ANALYTICAL_MERGED_SEED_CELL` (more than one expected seed landed in one built cell) and
  `SAM_OCCT_ANALYTICAL_ZERO_RELATION_PANELS` (generated cluster panels that bound no space).
- **`SAMOCCT.ValidateSpaces`** compares the built cells against the expected Spaces **by GUID** and reports
  the outcome. It never changes geometry — it is the scorecard.

### What to wire to `SAMAnalytical.Visualize` after each stage

Bake/visualize the panel output of each stage to *see* what it did before trusting the next one:

| After | Visualize | What you are checking |
|---|---|---|
| Clean3D | `Panels` (+ `Slits`, `SlitPanels`) | Double walls collapsed to one; `Slits` shows any parallel pair the bucket did **not** capture (gap > bucket). Panels carry BucketSize/Weight so Visualize draws the capture slab in the middle of each panel. |
| Extend3D | `Panels` (+ `OpenPanels`) | Walls reach their caps; floors/roofs grew out. `OpenPanels` are walls whose feet still do not close a loop — raise their `MaxExtend`/bucket. |
| CreateAdjacencyCluster | the cell `Shells` | One watertight cell per room, sitting on the 3 level datums. |
| ValidateSpaces | `MatchedSpaces` / `MissingSpaces` / `ExtraShells` / `SuspectedSeparatorPanels` | Which rooms matched, which are missing, which cells are spurious, and which input panels *should* have separated a merged pair but did not. |

### Reading `ValidateSpaces`

The `Report` output is the authoritative scorecard; its header is the one line to read first:

```text
SAM_OCCT_SPACEMATCH: SUMMARY expected=9 cells=8 matched=7 merged=0 missing=2 split=0 incorrect=0 extra=1
```

`Valid` is `true` only when every expected Space matched exactly one cell with a consistent span, every
requested double-height check passed, and there are no extra cells or orphan cluster panels. When it is not,
the typed outputs point at the cause: `MissingSpaces` (no cell), `MergedSpaces` (two rooms in one cell),
`SplitSpaces` / `IncorrectlyBoundedSpaces` (wrong span), `ExtraShells` (spurious cell),
`SuspectedSeparatorPanels` (an input wall that should have divided a merged pair but did not contribute),
`OrphanClusterPanels` (generated panels bounding nothing), and `DoubleHeightOk` (per requested
double-height Space, in input order).

### Worked example — the 9-space fixture, stage by stage (fixed workflow)

Fixture: `Testing/SAM.OCCT.IntegrationTests/Fixtures/ControlledWorkflow/Panels-9SpacesModel.sam` (66 panels)
and `Spaces-9SpacesModel.sam` (9 Spaces; West3 GUID `02a1ae27-5461-4b41-ad07-008ccd9d1159` is the
double-height room). Wiring `0.21 / true / true` as above:

| Stage | Expected output |
|---|---|
| Clean3D | 66 → **43** panels; `LevelFrames` = **5** raw datums; `LevelGroups` = **3** (12.24 / 15.29 / 18.34 m). |
| Extend3D | 43 panels; `inputAlreadyClean_=true`, `fillMargin_=0.5`, `directionalCapGrow_=true`. |
| CreateAdjacencyCluster | 11 cells (`SewBeforeBuild=true, SewingTolerance=0.01, MergeCoplanarBeforeBuild=true`). |
| ValidateSpaces | `matched=9`, `missing=0`, `extra=2`, `merged=0 split=0 incorrect=0`, West3 `DoubleHeightOk=true`, `0` orphan panels. |

**All 9 rooms match** — the East1/South1 corner-closure gap is resolved by coplanar-cap coalescing
(docs/CONTROLLED_WORKFLOW_BASELINE.md §8). The Extra cells are benign overshoot artefacts from the
more robust cap growth.

### Parameter discovery (AutoTune3D)

For unknown models, run `SAMOCCT.AutoTune3D` with `discoverParameters_=true` to find the optimal
`bucketBetweenLevels`, `fillMargin`, and `directionalCapGrow` automatically:

```
[SAMOCCT.AutoTune3D]  discoverParameters_=true, _panels=original
    → OptimalBand  ──→ [SAMOCCT.Extend3D]  bucketBetweenLevels_
    → OptimalFill  ──→ [SAMOCCT.Extend3D]  fillMargin_
    → OptimalDirCap ──→ [SAMOCCT.Extend3D]  directionalCapGrow_
                          inputAlreadyClean_=true  (if downstream of Clean3D)
                          → [SAMOCCT.CreateAdjacencyCluster]  MergeCoplanarBeforeBuild=true
                          → [SAMOCCT.MergeCoplanarAdjacencyCluster]
```

On the 9-space fixture this discovers `band=0.21, fill=0.5, dir=true` (the production config).
On whole-level-towers it discovers `band=0.4, fill=0.3, dir=false` (33 cells, +8 vs baseline).

**Only 3 Extend3D inputs are active** when `inputAlreadyClean_=true` (the chained workflow):
`fillMargin_`, `bucketBetweenLevels_`, `directionalCapGrow_`. The other four Stage-A inputs
(`minBucketSize_`, `thicknessFactor_`, `alignColinearOffset_`, `normalizeCapOffset_`) are INERT —
Stage A is skipped on the `inputAlreadyClean` path.

### Diagnosing a stubborn gap — the East1|South1 corner (2026-07-10, now resolved)

The East1/South1 corner had a ~0.1–0.4 m gap between fragmented cap strips at Z=15.29. The
diagnostic workflow for finding such gaps (still useful for future models):

1. Run `SAMOCCT.Extend3D` with `directionalCapGrow_=true`, inspect `Diagnostics`.
2. Look for `SAM_OCCT_EXTEND3D_SKIP: ... NoTargetWithinReach` — walls that could not find a cap.
3. Wire the extended panels into `SAMOCCT.CreateAdjacencyCluster` and `SAMOCCT.ValidateSpaces`.
4. Missing spaces with walls present → likely a cap-to-wall or cap-to-cap gap.

Fix applied: a coplanar-cap coalescing pass in Fill grows caps with coplanar neighbours uniformly,
closing inter-cap gaps that directional wall-based growth cannot reach. See
`docs/CONTROLLED_WORKFLOW_BASELINE.md` §8 for the full diagnosis trail (including the disproven
overlap-ratio and SewingTolerance theories).

## Large Building Strategy

Avoid sending very large whole-building shell sets through one interactive
adjacency operation. Even with OCCT creating the cells, the analytical model
still needs panel matching, space assignment, relation creation, normal updates,
and cleanup.

Practical guidance:

- Prefer `Panels` / `Face3Ds -> SAMOCCT.CreateAdjacencyCluster` for full
  building analytical models.
- Use `Shells -> SAMOCCT.CreateAdjacencyClusterByShells` for volume-first
  workflows, roof-cut rooms, atriums, shafts, and model chunks.
- Split very large models by block, level, wing, fire zone, or construction
  package before creating adjacency clusters.
- Avoid unioning adjacent room shells if each room should remain a separate
  space.
- Run `SAMOCCT.ShellsRepair` only on problematic chunks, not blindly on every
  shell in a large model.
- Keep shell face counts low. Many simple boxes are easier than fewer messy
  shells with many trimmed faces.

Suggested interactive scale:

```text
100-500 simple shells      comfortable
500-2,000 simple shells    possible, test by chunk
2,000-5,000 shells         chunk strongly recommended
10,000 shells              avoid as one operation
```

For large buildings, the preferred robust workflow is:

```text
1. Generate clean panels/faces per level or building zone.
2. Run SAMOCCT.CreateAdjacencyCluster per chunk.
3. Validate diagnostics and shell/cell counts.
4. Merge or join analytical results later if needed.
```

## Triangulating Non-Planar Surfaces Into Planar Panels

`SAMOCCT.TriangulateSurface` turns surfaces that may be non-planar (curved or
warped facade panels, freeform roofs) into planar triangular `Face3D`s that the
rest of the OCCT pipeline can panel and close.

OCCT meshes each surface with `BRepMesh`:

- A coplanar boundary is meshed as a flat plane.
- A non-planar (warped) boundary is first spanned by a filling surface, then
  meshed into planar facets.

Because every output is a triangle, every panel is guaranteed planar.

### How Panel Size Is Controlled

`linearDeflection_` is the **maximum distance a flat panel may deviate from the
true surface**, in model units. It is the main size control:

- Larger `linearDeflection_` -> fewer, bigger, flatter panels.
- Smaller `linearDeflection_` -> more, smaller panels that hug curvature.

Deflection is curvature-driven, not a fixed grid. A flat region deviates by
zero, so it is never subdivided and stays coarse; a high-curvature region is
subdivided automatically. This is what you want for panelling: detail where the
surface bends, large panels where it is flat.

`angularDeflection_` (radians) adds extra subdivision along tightly curved
boundaries. `minArea_` discards triangles below an area so seams do not produce
slivers.

`nonPlanarOnly_` keeps the face count down: when set, a surface whose boundary
is already planar (every point within `tolerance_` of its best-fit plane) passes
straight through as a single `Face3D` instead of being split into triangles.
Only genuinely warped surfaces are triangulated. Leave it off to triangulate
every surface uniformly; turn it on for mixed models where most surfaces are
flat and you only want the warped ones panelled.

### Settings For Watertight Shells (the hard part)

This is the difficult step. Each surface is triangulated **independently**, so
two neighbouring surfaces that share a curved edge can end up with slightly
different vertices along that edge. Those mismatches are tiny gaps and
T-junctions, and they are why a naive triangulation does not close into a
watertight shell.

The fix has four parts:

1. **Use one identical `linearDeflection_` (and `angularDeflection_`) for every
   surface in the same model.** Equal deflection makes a shared edge subdivide
   the same way on both sides, so the seams almost match.

2. **Let the downstream OCCT step sew the seams with a fuzzy tolerance.** The
   leftover gap along a shared edge is on the order of `linearDeflection_`, so
   when you feed the panels into `SAMOCCT.CreateShells` (or
   `SAMOCCT.CreateAdjacencyCluster`) set:

   ```text
   tolerance_      = 0.001                 (1 mm, normal model tolerance)
   fuzzyTolerance_ = linearDeflection_     (e.g. 0.1, or a little larger)
   ```

   `fuzzyTolerance_` is the OCCT tolerance for fusing near-touching faces. It
   must be **larger than the seam gap** (about `linearDeflection_`) but
   **smaller than the smallest real feature** you want to keep separate. Use the
   smallest value that closes the model reliably.

3. **Keep `minArea_` at 0 when watertightness matters.** Dropping sliver
   triangles can punch holes exactly along the seams you need to close. Only
   raise `minArea_` for visualisation or panel-count reduction where small gaps
   are acceptable.

4. **Pre-split shared edges where you can.** The most reliable watertight result
   comes from surfaces that already meet on exact, shared boundaries (split
   where neighbours touch) before triangulating. Deflection-consistent meshing
   of pre-split surfaces leaves almost nothing for the fuzzy step to bridge.

### Recommended Settings (meter-based models)

```text
SAMOCCT.TriangulateSurface
    linearDeflection_  = 0.1     (0.05 fine ... 0.2 coarse; SAME for all surfaces)
    angularDeflection_ = 0.5
    minArea_           = 0       (0 for watertight; raise only for visuals)
    tolerance_         = 0.001

then

SAMOCCT.CreateShells (or CreateAdjacencyCluster)
    tolerance_      = 0.001
    fuzzyTolerance_ = 0.1        (about linearDeflection_; raise if gaps remain)
```

### Pipeline

```text
non-planar surfaces
-> SAMOCCT.TriangulateSurface   (one shared linearDeflection_, minArea_ = 0)
-> planar Face3Ds
-> SAMOCCT.CreateShells         (fuzzyTolerance_ about linearDeflection_)
-> watertight Shells
-> SAMOCCT.PanelsFromShells / SAMOCCT.CreateAdjacencyClusterByShells
```

If the shells still do not close:

- Increase `fuzzyTolerance_` gradually, for example 0.1 -> 0.15 -> 0.2.
- Lower `linearDeflection_` so the seams start closer together.
- Set `minArea_` back to 0.
- Run `SAMOCCT.ShellsRepair` on the chunks that fail.
- Pre-split neighbouring surfaces so they share exact edges before triangulating.

### Diagnosing And Closing Failures (Validate, Sew, Glue)

> Step-by-step Grasshopper recipes for the three components below (with the
> expected outputs and diagnostics) are in
> [`Examples-Validate-Sew-Glue.md`](Examples-Validate-Sew-Glue.md).

When a model will not close, work it in this order (issue #37):

1. **`SAMOCCT.Validate` - find out *why* and *where*.** Feed it the same faces
   (or shells) you give `SAMOCCT.CreateShells`. It runs the OCCT kernel checks
   (`BRepCheck_Analyzer` + `ShapeAnalysis_FreeBounds` + an optional
   self-intersection test) and returns:

   - `IsValid` / `IsWatertight` - a quick yes/no on validity and gaps.
   - `IssueLocations` - a point at each problem you can bake to *see* where the
     hole / self-intersection / sliver is.
   - `Issues` - a text description of each (naked edge with its gap length,
     self-intersection, small/invalid face).

   A non-watertight report with naked edges tells you the gap is larger than the
   tolerance - either pre-split the surfaces there or raise the sewing tolerance
   below.

2. **`SAMOCCT.Sew` - close triangulated / near-touching faces.** Unlike
   `CreateShells` it does not need the faces to already bound a volume; it sews
   and heals (`BRepBuilderAPI_Sewing` + `ShapeFix`) to bridge the seams a fuzzy
   tolerance alone misses. Raise `sewingTolerance_` to bridge larger gaps. You
   can also stay in `SAMOCCT.CreateShells` and set `sewBeforeBuild_ = true`
   (with a `sewingTolerance_`); a hard close failure there also auto-retries via
   sew.

3. **`glueMode_` on `SAMOCCT.CreateShells` - speed up big cell complexes.** For
   models with thousands of *coincident shared walls* (large adjacency
   clusters), set `glueMode_ = 2` (full) to let the boolean kernel treat the
   shared walls as shared instead of re-intersecting them. It is a throughput
   optimisation, **not** a closing fix: glue is applied only when a
   watertightness check finds no gaps, and otherwise silently degrades to the
   normal build (look for a `SAM_OCCT_GLUE_SKIPPED` remark). Run
   `SAMOCCT.Validate` first and only enable glue once the input is watertight.

```text
faces that will not close
-> SAMOCCT.Validate            (IsWatertight? where are the naked edges?)
-> SAMOCCT.Sew                 (or CreateShells with sewBeforeBuild_ = true)
-> watertight Shells
-> (large clusters) SAMOCCT.CreateShells with glueMode_ = 2 on clean input
```

## Merging Coplanar Faces

`SAMOCCT.MergeCoplanarFace3Ds` and `SAMOCCT.MergeCoplanarShells` are the inverse
of panelling: they collapse adjacent faces that lie on the same plane back into
fewer, larger faces using OCCT `ShapeUpgrade_UnifySameDomain`. Use them to clean
up over-segmented geometry (for example after sectioning, boolean operations, or
imported meshes) before creating shells or analytical models, which keeps face
counts and downstream solve times down.

How it works:

1. Input faces are sewn with `tolerance_` so coincident edges become shared.
2. Neighbours whose normals agree within `angleTolerance_` are unified, and the
   now-redundant edges between them are removed.
3. Disjoint faces, and faces on different planes, are left untouched.

`SAMOCCT.MergeCoplanarShells` runs the same operation per shell and rebuilds each
closed volume, so only the face count changes - the geometry of the volume is
preserved. If a shell cannot be merged it is passed through unchanged.

Notes:

- Increase `angleTolerance_` to merge faces that are only approximately coplanar;
  keep it small to avoid flattening intentional creases.

### Analytical Coplanar Merging

`SAMOCCT.MergeCoplanarPanels` and `SAMOCCT.MergeCoplanarAdjacencyCluster` apply
the same OCCT engine at the analytical level, but with extra rules so the model
stays valid:

- Only panels with the **same panel type and construction** may merge.
- In an `AdjacencyCluster`, panels must additionally **separate the same
  space(s)** (same adjacency), so internal/external relations are preserved.
- **Apertures** (windows/doors) on the original panels are re-hosted onto the
  merged panel.

Use these to simplify over-segmented analytical models - for example after
shell-to-panel conversion or cell-complex creation produced many small coplanar
panels per wall - without changing the spaces, panel types, constructions, or
glazing.

## STEP / IGES Import And Export

Closed SAM `Shell` volumes round-trip to the open STEP and IGES interchange
formats through OCCT's Data Exchange module. Four Grasshopper nodes cover both
directions:

- `SAMOCCT.ExportSTEP` / `SAMOCCT.ExportIGES` - take `_shells` and a `_path` and
  write the volumes to a file. Outputs `Successful` and `Diagnostics`.
- `SAMOCCT.ImportSTEP` / `SAMOCCT.ImportIGES` - take a `_path` and return
  `Shells`, with `Diagnostics` and `Successful`.

Managed callers use `SAM.Geometry.OCCT.Export.ToFile(...)` and
`SAM.Geometry.OCCT.Create.Shells(path, format, ...)` /
`Create.Topology(path, format, ...)`. Diagnostics are coded `SAM_OCCT_EXPORT_*`
and `SAM_OCCT_IMPORT_*`.

**STEP is exact, IGES is lossy.** STEP (ISO 10303) preserves the BRep solids, so
a round-trip returns the same cell count and volumes. IGES is written in BRep
mode so closed solids still round-trip, but it is a surface-oriented format -
treat an imported IGES leniently (the closed volume survives within tolerance,
but exact solid counts are not guaranteed). Prefer STEP whenever the consumer
supports it.

Both formats are open and royalty-free. STL/OBJ/glTF/BREP and others are planned
for later phases using the same node pattern.

**Runtime DLLs.** STEP/IGES needs more OCCT DLLs than the core modeling nodes:
the Data Exchange + XDE/CAF + visualization stack and the third-party
`freetype.dll` / `FreeImage.dll`. These are delay-loaded, so if they are not
deployed the other nodes keep working and only STEP/IGES reports
`status 64`. Running `build-native.ps1` deploys the full set; see
`THIRD_PARTY.md` for the exact list.

## Rule Of Thumb

If the goal is an analytical building model, start with `Panel` or `Face3D`.

If the goal is sculpting space volumes, start with `Shell`.

For best OCCT success, generate clean planar faces with snapped coordinates,
then let OCCT create the closed cells.
