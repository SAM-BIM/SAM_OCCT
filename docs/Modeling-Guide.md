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

Do not union adjacent room shells before creating an adjacency cluster if each
room should remain a separate space. Union is for merging volumes into a larger
solid, not for preserving individual rooms.

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

## Rule Of Thumb

If the goal is an analytical building model, start with `Panel` or `Face3D`.

If the goal is sculpting space volumes, start with `Shell`.

For best OCCT success, generate clean planar faces with snapped coordinates,
then let OCCT create the closed cells.
