# SAM_OCCT

**SAM_OCCT** is the Open CASCADE Technology integration layer for the
SAM (Sustainable Analytical Model) Toolkit.

The purpose of this repository is to replace the heavy/legacy `SAM_Topologic`
cell-complex dependency for workflows that create closed analytical geometry
from panels or `Face3D` surfaces.

## Current scope

This repository currently provides the first migration foundation:

- `SAM.Core.OCCT`: shared options and diagnostics.
- `SAM.Geometry.OCCT`: SAM `Face3D`/`Panel` input collection, native bridge boundary, and `Shell` result model.
- `SAM.Analytical.OCCT`: analytical entry point for future `AdjacencyCluster` rebuild.
- `SAM.Geometry.Grasshopper.OCCT`: `SAMOCCT.CreateShells`.
- `SAM.Analytical.Grasshopper.OCCT`: `SAMOCCT.CreateAdjacencyCluster`.
- `native/SAM.Occt.Native`: C ABI and CMake stub for the future OCCT implementation.

The native bridge is intentionally small. SAM business logic stays in C#; C++
should only convert input loops into OCCT faces, run OCCT volume/cell builders,
and return shell geometry plus diagnostics.

## Native implementation target

The planned native implementation should use OCCT algorithms in this order:

1. `BOPAlgo_MakerVolume` for panel/face sets that should form closed solids.
2. `BOPAlgo_CellsBuilder` only when split-cell selection, internal-boundary
   removal, or better source mapping is required.

The managed API should remain stable while the native implementation evolves.

## License and third-party notes

SAM_OCCT is licensed as LGPL-3.0-or-later, matching the rest of SAM.

OCCT is distributed under LGPL-2.1 with an additional exception. Distribute OCCT
dynamically, include OCCT notices, and document how users can replace OCCT DLLs.
Do not static-link OCCT into SAM assemblies or the native bridge unless legal
review explicitly approves that distribution model.

See:

- `LICENSE`
- `NOTICE`
- `THIRD_PARTY.md`
- `COPYRIGHT_HEADER.txt`

## Development

Build:

```powershell
dotnet build SAM_OCCT.sln /p:RestorePackages=false
```

The current C# solution builds without the native OCCT DLL. Runtime calls to the
OCCT builder return diagnostics until `SAM.Occt.Native` is implemented and
deployed beside the managed assemblies.
