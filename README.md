# SAM_OCCT

Open CASCADE Technology integration for the SAM Toolkit.

SAM_OCCT provides a small native OCCT bridge plus C# and Grasshopper wrappers
for closed-shell, boolean, and analytical adjacency-cluster workflows. It is
intended to replace the legacy Topologic dependency where SAM needs robust 3D
cell and shell operations.

## Projects

- `SAM.Core.OCCT`: build options and diagnostics.
- `SAM.Geometry.OCCT`: managed geometry API and native interop.
- `SAM.Analytical.OCCT`: analytical adjacency-cluster creation from OCCT/SAM geometry.
- `SAM.Geometry.Grasshopper.OCCT`: geometry Grasshopper components.
- `SAM.Analytical.Grasshopper.OCCT`: analytical Grasshopper components.
- `native/SAM.Occt.Native`: C ABI and CMake-based OCCT implementation.

The native bridge is intentionally narrow. C++ performs OCCT solid creation,
boolean operations, repair, and result decoding. SAM model reconstruction stays
in C#.

## Grasshopper Components

Geometry:

- `SAMOCCT.CreateShells`
- `SAMOCCT.ShellsUnion`
- `SAMOCCT.ShellsDifference`
- `SAMOCCT.ShellsIntersection`
- `SAMOCCT.ShellsRepair`
- `SAMOCCT.ShellsSplit`
- `SAMOCCT.ShellsSectionByPlane`

Analytical:

- `SAMOCCT.CreateAdjacencyCluster`
- `SAMOCCT.CreateAdjacencyClusterByShells`
- `SAMOCCT.PanelsFromShells`

Both adjacency components expose `tolerance_` and `fuzzyTolerance_` as the main
OCCT controls. `SAMOCCT.CreateAdjacencyClusterByShells` also keeps advanced SAM
rebuild inputs such as `maxDistance_`, `maxAngle_`, and `minArea_`; these are
used after OCCT creates cells, when SAM rebuilds spaces, panels, and adjacency
relations.

The current analytical workflow uses OCCT to create closed cells, then decodes
those cells back into SAM geometry for adjacency reconstruction. A future direct
OCCT topology workflow can reduce this rebuild step by carrying cell-face
ownership into SAM explicitly.

## Modeling Guide

See `docs/Modeling-Guide.md` for the recommended Panels/Face3D vs Shell
workflow, tolerance guidance, and volume-modeling rules for reliable OCCT cell
creation.

## Build

Close Rhino/Grasshopper before rebuilding so files in `%APPDATA%\SAM` are not
locked.

```powershell
dotnet build SAM_OCCT.sln /p:RestorePackages=false
```

`SAM.Geometry.Grasshopper.OCCT` runs `build-native.ps1` before build, so the
native `SAM.Occt.Native.dll` is rebuilt and copied with the managed assemblies.
Set `SAM_OCCT_SKIP_NATIVE_BUILD=true` to skip the native step for managed-only
builds.

The native build writes runtime files to:

```text
SAM_OCCT\build
%APPDATA%\SAM
```

## OCCT SDK

`build-native.ps1` auto-detects the default local SDK layout:

```text
C:\OCCT\occt-8.0.0\opencascade-8.0.0-vc14-64\inc
C:\OCCT\occt-8.0.0\opencascade-8.0.0-vc14-64\win64\vc14\lib
C:\OCCT\occt-8.0.0\opencascade-8.0.0-vc14-64\win64\vc14\bin
C:\OCCT\occt-8.0.0\3rdparty-vc14-64
```

Manual native build:

```powershell
.\build-native.ps1 -SkipVcpkgInstall
```

Explicit SDK paths can also be supplied:

```powershell
.\build-native.ps1 `
  -SkipVcpkgInstall `
  -OpenCascadeIncludeDir "C:\OCCT\occt-8.0.0\opencascade-8.0.0-vc14-64\inc" `
  -OpenCascadeLibraryDir "C:\OCCT\occt-8.0.0\opencascade-8.0.0-vc14-64\win64\vc14\lib" `
  -OpenCascadeRuntimeBin "C:\OCCT\occt-8.0.0\opencascade-8.0.0-vc14-64\win64\vc14\bin" `
  -ThirdPartyRuntimeRoot "C:\OCCT\occt-8.0.0\3rdparty-vc14-64"
```

If using vcpkg, `build-native.ps1` can install the `opencascade` dependency
from `vcpkg.json`. If Windows policy blocks vcpkg's downloaded tools, provide
an approved Ninja executable with `-NinjaPath`.

## Native Operations

The native bridge currently uses:

- `BOPAlgo_MakerVolume` for face/panel sets and shell repair.
- `BRepAlgoAPI_Fuse` for shell union.
- `BRepAlgoAPI_Cut` for shell difference.
- `BRepAlgoAPI_Common` for shell intersection.
- `ShapeFix_Shape` before result decoding.

## Licensing

SAM_OCCT is licensed as LGPL-3.0-or-later.

OCCT is LGPL-2.1 with an additional exception. SAM_OCCT dynamically links OCCT
through `SAM.Occt.Native`. Before distributing OCCT DLLs with any installer,
include OCCT notices, license text, source-location information, and replacement
instructions.

See `LICENSE`, `NOTICE`, `THIRD_PARTY.md`, and `COPYRIGHT_HEADER.txt`.
