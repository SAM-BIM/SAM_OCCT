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

## Rule Of Thumb

If the goal is an analytical building model, start with `Panel` or `Face3D`.

If the goal is sculpting space volumes, start with `Shell`.

For best OCCT success, generate clean planar faces with snapped coordinates,
then let OCCT create the closed cells.
