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
- `native/SAM.Occt.Native`: C ABI and CMake implementation for the first OCCT cell builder.

The native bridge is intentionally small. SAM business logic stays in C#; C++
only converts input loops into OCCT faces, runs OCCT volume/cell builders, and
returns shell geometry plus diagnostics.

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

Native build:

```powershell
.\build-native.ps1
dotnet build SAM_OCCT.sln /p:RestorePackages=false
```

The native build writes `SAM.Occt.Native.dll` to `build\`. The Grasshopper
post-build event then copies `build\*.dll` to `%APPDATA%\SAM`, so Rhino can load
the managed GH components and the native OCCT bridge from the same folder.

If vcpkg fails while running downloaded tools from `%LOCALAPPDATA%\vcpkg`, use an
approved Ninja 1.13.1+ executable and pass it explicitly:

```powershell
.\build-native.ps1 -NinjaPath "C:\tools\ninja\ninja.exe"
```

On the current workstation, Visual Studio's bundled Ninja is `1.12.1`, while the
selected vcpkg baseline requires `1.13.1+`. If Windows policy blocks vcpkg's
downloaded `ninja.exe`, install or approve Ninja separately, then rerun the
native build.

Alternative without vcpkg package install:

```powershell
.\build-native.ps1 `
  -SkipVcpkgInstall `
  -OpenCascadeDir "C:\OpenCASCADE\cmake" `
  -OpenCascadeRuntimeBin "C:\OpenCASCADE\bin"
```

Use this when OCCT headers/libs/runtime DLLs come from an approved internal SDK
instead of vcpkg.
