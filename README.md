[![Build (Windows)](https://github.com/SAM-BIM/SAM_OCCT/actions/workflows/build.yml/badge.svg?branch=sow/2026-Q2)](https://github.com/SAM-BIM/SAM_OCCT/actions/workflows/build.yml)
[![Installer (latest)](https://img.shields.io/github/v/release/SAM-BIM/SAM_Deploy?label=installer)](https://github.com/SAM-BIM/SAM_Deploy/releases/latest)

# SAM_OCCT

<a href="https://github.com/SAM-BIM/SAM">
  <img src="Grasshopper/SAM.Core.Grasshopper.OCCT/Resources/SAM_OCCT.png"
       align="left" hspace="10" vspace="6">
</a>

**SAM_OCCT** is part of the **SAM (Sustainable Analytical Model) Toolkit** -
an open-source collection of tools designed to help engineers create, manage,
and process analytical building models for energy and environmental analysis.

This repository provides **integration between SAM analytical/geometry workflows
and Open CASCADE Technology (OCCT)**, enabling robust closed-shell, boolean,
cell-complex, and adjacency-cluster operations to be executed through SAM and
Grasshopper components.

The integration supports native OCCT solid creation, shell operations, result
decoding, and direct SAM analytical topology reconstruction, and is intended to
be used alongside the SAM core libraries and related SAM-BIM modules.

Welcome - and let's keep the open-source journey going.

---

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
OCCT controls. `SAMOCCT.CreateAdjacencyClusterByShells` also retains advanced
SAM rebuild inputs such as `maxDistance_`, `maxAngle_`, and `minArea_` for
compatibility with older Grasshopper definitions and fallback rebuilds.
The shell-native direct topology path normally uses `maxAngle_` and `minArea_`,
but not `maxDistance_`.

The current analytical workflow uses OCCT to create closed cells, decodes
cell-face ownership into SAM geometry, and builds the analytical
`AdjacencyCluster` directly from that OCCT topology when possible.
Decoded `OcctCell`s contain keyed `OcctCellFace`s, and matching face keys are
reported as `FaceAdjacencies`. The current Grasshopper diagnostics include
`SAM_OCCT_TOPOLOGY` so large models can show how many shared OCCT face
relations were decoded before SAM creates spaces, panels, and relations.

`SAM.Analytical.OCCT` now tries to build the analytical `AdjacencyCluster`
directly from this OCCT topology first. If successful, diagnostics include
`SAM_OCCT_ANALYTICAL_DIRECT_SUCCESS`; if not, the component falls back to the
older SAM geometric rebuild and reports `SAM_OCCT_ANALYTICAL_DIRECT_FALLBACK`.

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

## Tests

The `SAM_OCCT.Tests` project is set up for OCCT macro tests and uploaded SAM
geometry fixtures:

```powershell
dotnet test SAM_OCCT.Tests
```

Drop exported `*.sam` examples into `SAM_OCCT.Tests/Fixtures`. The fixture test
loads each file, extracts usable `Face3D` geometry from common SAM object types,
and runs the faces through the OCCT cell-complex builder.

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
