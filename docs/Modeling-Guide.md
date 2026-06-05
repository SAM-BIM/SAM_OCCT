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

- `SAM_OCCT_ANALYTICAL_SHELL_METADATA`: reports supplied spaces/names, matched
  existing spaces, supplied names used, and auto-named spaces.
- `SAM_OCCT_ANALYTICAL_SHELL_PANELS`: reports extracted panels and seed spaces.
- `SAM_OCCT_ANALYTICAL_INPUT`: reports the panel and seed-space count sent into
  the OCCT adjacency builder.
- `SAM_OCCT_ANALYTICAL_CELL_SPACE_DELTA`: warns when seed-space count differs
  from OCCT cell count.
- `SAM_OCCT_ANALYTICAL_REBUILD_SPACE_DELTA`: warns when final SAM space count
  differs from OCCT shell count.
- `SAM_OCCT_TIMING_PANEL_EXTRACTION`: time spent converting shells into panels
  and seed spaces.
- `SAM_OCCT_TIMING_OCCT_AND_ADJACENCY`: time spent in the OCCT cell build and
  SAM adjacency creation.
- `SAM_OCCT_TIMING_POST_PROCESS`: time spent cutting at ground elevation,
  updating panel types, and assigning default constructions.
- `SAM_OCCT_TIMING_TOTAL`: total component time.

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
  finding shell internal points and matching existing space locations.

The other numeric inputs are advanced SAM rebuild controls, not OCCT build
tolerances:

- `maxDistance_`: how far SAM may search when matching rebuilt panels.
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

## Rule Of Thumb

If the goal is an analytical building model, start with `Panel` or `Face3D`.

If the goal is sculpting space volumes, start with `Shell`.

For best OCCT success, generate clean planar faces with snapped coordinates,
then let OCCT create the closed cells.
