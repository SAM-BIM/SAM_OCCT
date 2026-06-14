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
- `SAMOCCT.ExtrudeFootprints`
- `SAMOCCT.ShellsUnion`
- `SAMOCCT.ShellsDifference`
- `SAMOCCT.ShellsIntersection`
- `SAMOCCT.ShellsRepair`
- `SAMOCCT.ShellsSplit`
- `SAMOCCT.ShellsOffset`
- `SAMOCCT.ShellsThicken`
- `SAMOCCT.ShellsImprint`
- `SAMOCCT.ShellsSectionByPlane`
- `SAMOCCT.MergeSmallShells`
- `SAMOCCT.MergeCoplanarFace3Ds`
- `SAMOCCT.MergeCoplanarShells`
- `SAMOCCT.TriangulateSurface`
- `SAMOCCT.ExportSTEP`
- `SAMOCCT.ExportIGES`
- `SAMOCCT.ImportSTEP`
- `SAMOCCT.ImportIGES`

Analytical:

- `SAMOCCT.CreateAdjacencyCluster`
- `SAMOCCT.CreateAdjacencyClusterByShells`
- `SAMOCCT.MergeSmallSpaces`
- `SAMOCCT.MergeCoplanarPanels`
- `SAMOCCT.MergeCoplanarAdjacencyCluster`
- `SAMOCCT.PanelsFromShells`

`SAMOCCT.TriangulateSurface` takes possibly non-planar surfaces and
triangulates them into planar SAM `Face3D`s ready for panelling, using OCCT for
the meshing. Coplanar boundaries are meshed as a plane; non-planar (warped)
boundaries are spanned with an OCCT filling surface (`BRepOffsetAPI_MakeFilling`)
and meshed with `BRepMesh_IncrementalMesh`, so every output triangle is
guaranteed planar. `linearDeflection_` is the maximum distance a panel may
deviate from the true surface — larger values give fewer, bigger panels —
while `minArea_` discards too-tiny panels. The resulting `Face3D`s feed straight
into `SAMOCCT.CreateShells` / `SAMOCCT.PanelsFromShells`. To turn the triangulated
panels into **watertight** shells you must use one consistent `linearDeflection_`
across all surfaces and a matching downstream `fuzzyTolerance_`; see the
"Triangulating Non-Planar Surfaces Into Planar Panels" section of
[`docs/Modeling-Guide.md`](docs/Modeling-Guide.md) for the full recipe.

Both adjacency components expose `tolerance_` and `fuzzyTolerance_` as the main
OCCT controls. `SAMOCCT.CreateAdjacencyClusterByShells` also retains advanced
SAM rebuild inputs such as `maxDistance_` and `maxAngle_` for compatibility with
older Grasshopper definitions and fallback rebuilds. The shell-native direct
topology path normally uses `maxAngle_` but not `maxDistance_`. `minArea_` is a
**post-build panel filter** (issue #11): every shell face is kept for the OCCT
volume build so the cell never opens, then faces below `minArea_` are simply not
turned into SAM panels (e.g. tiny triangulation slivers). It cannot reopen a
shell, so it is safe to raise - e.g. `0.01` drops sub-0.01 m² sliver panels while
all spaces still build.

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

`SAMOCCT.MergeSmallSpaces` is a clean-up step that runs on an existing
`AdjacencyCluster`. It finds spaces whose floor area is below `minArea_` (or
volume below `minVolume_`) and merges each into the best adjacent larger space
across a shared internal boundary. The now-internal shared panels are dropped,
the surviving space inherits the small space's remaining panels, volume, and
adjacency, and panel types and constructions are recalculated. `mergeMode_`
chooses the merge target (`LongestSharedBoundary`, `LargestNeighbour`, or
`SameTypeFirst`), `protectedSpaces_` keeps shafts/risers intact, and
vertically stacked neighbours on a different level are never used as targets.
The component reports merged and unmerged spaces and a coded `report` of every
decision (`SAM_OCCT_MERGE_*`).

`SAMOCCT.MergeSmallShells` is the shell/Brep counterpart of the same clean-up.
It runs before any analytical model exists. The supplied shells are decoded into
an OCCT cell complex, so merging uses real per-cell volumes and real shared-face
adjacency (not bounding-box estimates). It finds tiny cells (floor footprint
below `minArea_` or volume below `minVolume_`), groups each with its best
face-adjacent neighbour (`LongestSharedBoundary` or `LargestNeighbour`), and
fuses each group with OCCT `ShellsUnion`. Cells that are large, isolated, or
listed in `protectedShells_` pass through unchanged. The component reports merged
and unmerged cells and a coded `report` (`SAM_OCCT_MERGE_SHELLS_*`).

## Component Reference

Every component shares the `_run` boolean (nothing happens until it is `true`)
and a `Successful` output. Most also expose a `Diagnostics` (codes/messages)
output; the clean-up components instead emit a coded `report`. Inputs starting
with `_` are required; inputs ending with `_` are optional. Tolerance inputs are
covered under [Tolerances](#tolerances-and-key-inputs) below.

### Geometry (`SAM.Geometry.Grasshopper.OCCT`)

| Component | What it does | Key inputs | Main output |
| --- | --- | --- | --- |
| `SAMOCCT.CreateShells` | Builds closed SAM `Shell` volumes from boundary faces/surfaces using OCCT. | `_face3Ds`, `tolerance_`, `fuzzyTolerance_` | `Shells` |
| `SAMOCCT.ExtrudeFootprints` | Extrudes planar footprint Face3Ds vertically (via OCCT `BRepPrimAPI_MakePrism`) into one closed shell each - the draw-outline-plus-storey-height workflow feeding `CreateShells`. | `_footprints`, `_height`, `tolerance_` | `Shells` |
| `SAMOCCT.ShellsUnion` | Merges touching or overlapping closed shells into combined solids. | `_shells`, `tolerance_`, `fuzzyTolerance_` | `Shells` |
| `SAMOCCT.ShellsDifference` | Subtracts closed cutter volumes from target shells. | `_shells`, `_cutterShells`, `tolerance_`, `fuzzyTolerance_` | `Shells` |
| `SAMOCCT.ShellsIntersection` | Keeps only the volume where target shells overlap tool shells. | `_shells`, `_toolShells`, `tolerance_`, `fuzzyTolerance_` | `Shells` |
| `SAMOCCT.ShellsRepair` | Rebuilds/repairs each closed shell through OCCT (heals gaps, bad faces) and defeatures away tiny sliver faces below `minArea_` left by sectioning, extending neighbours to keep the shell closed. | `_shells`, `tolerance_`, `fuzzyTolerance_`, `minArea_` | `Shells` |
| `SAMOCCT.ShellsSplit` | Splits overlapping/touching shells into cleaner adjacent pieces. | `_shells`, `silverSpacing_`, `tolerance_` | `Shells` |
| `SAMOCCT.ShellsOffset` | Offsets each closed shell's skin outward (positive) or inward (negative) via OCCT `BRepOffsetAPI_MakeOffsetShape` - centre-line vs physical face. | `_shells`, `_offset`, `tolerance_` | `Shells` |
| `SAMOCCT.ShellsThicken` | Hollows each closed shell into a genuine wall of the given thickness - the material between the boundary and a parallel offset surface, with an inner cavity (built as outer solid − inner solid), unlike `ShellsOffset` which moves the whole skin. Positive thickens outward, negative inward (construction / plenum shells). | `_shells`, `_thickness`, `tolerance_` | `Shells` |
| `SAMOCCT.ShellsImprint` | Imprints touching shells against each other via OCCT General Fuse so partly-shared boundary faces are split into matching sub-faces (second-level space boundaries). Volumes stay separate (not a union); the matched faces decode into `FaceAdjacencies`. | `_shells`, `tolerance_`, `fuzzyTolerance_` | `Shells` |
| `SAMOCCT.ShellsSectionByPlane` | Sections shells by one or more planes (supply many level planes to cut many levels at once), returning the cut faces and the split shells. | `_shells`, `planes_`, `tolerance_` | `Face3Ds`, `Shells` |
| `SAMOCCT.MergeSmallShells` | Fuses tiny closed shells into their best face-adjacent neighbour via OCCT cell topology. | `_shells`, `minArea_`, `minVolume_`, `mergeMode_`, `protectedShells_`, `fuzzyTolerance_`, `tolerance_` | `Shells` (+ `mergedSmallShells`, `unmergedSmallShells`, `report`) |
| `SAMOCCT.MergeCoplanarFace3Ds` | Merges adjacent coplanar Face3Ds into fewer, larger faces via OCCT `ShapeUpgrade_UnifySameDomain`. | `_face3Ds`, `angleTolerance_`, `tolerance_` | `Face3Ds` |
| `SAMOCCT.MergeCoplanarShells` | Merges each shell's coplanar faces into fewer faces (volume preserved) via OCCT. | `_shells`, `angleTolerance_`, `tolerance_` | `Shells` |
| `SAMOCCT.TriangulateSurface` | Triangulates possibly non-planar surfaces into planar `Face3D` panels via OCCT meshing. `nonPlanarOnly_` passes flat surfaces through as a single face to save face count. | `_surfaces`, `linearDeflection_`, `angularDeflection_`, `minArea_`, `nonPlanarOnly_`, `tolerance_` | `Face3Ds` |
| `SAMOCCT.ExportSTEP` | Exports closed shells to a STEP file via OCCT (exact BRep solids). | `_shells`, `_path`, `tolerance_`, `fuzzyTolerance_` | `Successful`, `Diagnostics` |
| `SAMOCCT.ExportIGES` | Exports closed shells to an IGES file via OCCT (BRep mode; surface-oriented). | `_shells`, `_path`, `tolerance_`, `fuzzyTolerance_` | `Successful`, `Diagnostics` |
| `SAMOCCT.ImportSTEP` | Imports a STEP file into closed SAM `Shell` volumes via OCCT. | `_path`, `tolerance_` | `Shells`, `Diagnostics`, `Successful` |
| `SAMOCCT.ImportIGES` | Imports an IGES file into SAM `Shell` volumes via OCCT (lenient; may decode to no shells if the file has no closed solids). | `_path`, `tolerance_` | `Shells`, `Diagnostics`, `Successful` |

### Analytical (`SAM.Analytical.Grasshopper.OCCT`)

| Component | What it does | Key inputs | Main output |
| --- | --- | --- | --- |
| `SAMOCCT.CreateAdjacencyCluster` | Builds a SAM `AdjacencyCluster` from analytical `Panels` via OCCT cell building. | `_panels`, `spaces_`, `tolerance_`, `fuzzyTolerance_` | `AdjacencyCluster` |
| `SAMOCCT.CreateAdjacencyClusterByShells` | Builds an `AdjacencyCluster` from closed shell space volumes, reusing space metadata. | `_shells`, `spaces_`, `names_`, `elevationGround_`, `fuzzyTolerance_`, `maxDistance_`, `maxAngle_`, `minArea_`, `tolerance_` | `AdjacencyCluster` |
| `SAMOCCT.MergeSmallSpaces` | Merges tiny spaces of an `AdjacencyCluster` into the best adjacent larger space. | `_adjacencyCluster`, `minArea_`, `minVolume_`, `mergeMode_`, `allowMergeExternal_`, `protectedSpaces_`, `tolerance_` | `adjacencyCluster` (+ `mergedSpaces`, `unmergedSmallSpaces`, `report`) |
| `SAMOCCT.MergeCoplanarPanels` | Merges coplanar `Panels` of the same type+construction into fewer panels via OCCT; apertures are re-hosted. | `_panels`, `angleTolerance_`, `tolerance_` | `Panels` |
| `SAMOCCT.MergeCoplanarAdjacencyCluster` | Merges coplanar cluster panels of the same type+construction+space-adjacency via OCCT, preserving topology and apertures. | `_adjacencyCluster`, `angleTolerance_`, `tolerance_` | `AdjacencyCluster` |
| `SAMOCCT.PanelsFromShells` | Creates analytical SAM `Panels` from the faces of closed shells. | `_shells`, `silverSpacing_`, `tolerance_` | `Panels` |

For full input/output descriptions, hover the component parameters in
Grasshopper; for recommended settings and end-to-end workflows (including
watertight panelling) see `docs/Modeling-Guide.md`.

### Tolerances And Key Inputs

- `tolerance_`: base model tolerance (e.g. 1 mm / `0.001` for meters).
- `fuzzyTolerance_`: OCCT tolerance for fusing near-touching faces/edges.
- `silverSpacing_`: snap distance used to remove sliver geometry.
- `linearDeflection_` / `angularDeflection_`: OCCT meshing deflection used by
  `SAMOCCT.TriangulateSurface` (panel size / curvature control).
- `minArea_` / `minVolume_`: discard faces / cells below these thresholds.

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

## Testing

SAM_OCCT uses a two-tier test method (xUnit):

- **Unit tests** (`Testing/SAM.OCCT.UnitTests`) — fast, no native library
  required, and independent of host DLL state. They cover build options,
  diagnostics, the managed native-input serializer, and the `Query` input guard
  clauses.
- **Integration tests** (`Testing/SAM.OCCT.IntegrationTests`) — drive the real
  OCCT boolean/cell-complex operations, plus the graceful "native missing"
  contract. Each test is gated on native availability, so the suite never fails
  an OCCT-less agent (boolean-op tests skip without the library; the
  native-missing test skips with it). This project also runs uploaded `*.sam`
  geometry fixtures (see `Testing/SAM.OCCT.IntegrationTests/Fixtures`) through
  the OCCT cell-complex builder.

```powershell
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj
```

Unit tests run on every PR via the Build workflow (coverage is collected and
uploaded as an artifact). See `TESTING.md` for the conventions and how to run
the integration tests against a built native library.

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
- `BRepPrimAPI_MakePrism` for footprint extrusion.
- `BOPAlgo_Builder` (General Fuse) for shell imprinting / second-level space boundaries.
- `BRepOffsetAPI_MakeOffsetShape` (PerformByJoin) for shell offset, and the same offset cut against the original (`BRepAlgoAPI_Cut`) for wall thickening.
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
