# OCCT engine usage review (issue #13)

Review date: 2026-06-09. Scope: every Grasshopper component and every public
method in `SAM.Geometry.OCCT` / `SAM.Analytical.OCCT`, checking that geometric
computation runs on the native OCCT engine (`SAM.Occt.Native`) rather than the
managed SAM engine wherever an OCCT path exists.

"SAM engine" below means managed geometry/analytical computation from the SAM
core repo (`SAM.Geometry.Spatial` / `SAM.Analytical` `Query`/`Create`/`Modify`).
Object construction (building `Panel`/`Space`/`Face3D` instances), Grasshopper
conversion plumbing (`TryGetSAMGeometries`), and cheap predicates
(`GetArea`, `Inside`/`On`, `Distance`) are not counted as engine computation.

## Components

| Component | Core computation | Engine |
| --- | --- | --- |
| SAMOCCTCreateAdjacencyCluster | `Analytical.OCCT.Create.AdjacencyCluster` → native cell build + direct topology rebuild | OCCT |
| SAMOCCTCreateAdjacencyClusterByShells | `Analytical.OCCT.Create.AdjacencyCluster` (shells overload) | OCCT (see notes: ground cut) |
| SAMOCCTMergeSmallSpaces | `Analytical.OCCT.Modify.MergeSmallSpaces` → native cell topology | OCCT |
| SAMOCCTPanelsFromShells | Faces come straight from the OCCT cell build; per-face `PanelType` classification + `Create.Panel` construction only | OCCT |
| SAMOCCTCreateShells | `Geometry.OCCT.Create.Shells` (BOPAlgo_MakerVolume) | OCCT |
| SAMOCCTMergeCoplanarFace3Ds | `Geometry.OCCT.Query.MergeCoplanarFace3Ds` | OCCT |
| SAMOCCTMergeCoplanarShells | `Geometry.OCCT.Query.MergeCoplanar` | OCCT |
| SAMOCCTMergeSmallShells | `Geometry.OCCT.Modify.MergeSmallShells` → native decode + union | OCCT |
| SAMOCCTShellsDifference | `Geometry.OCCT.Query.ShellsDifference` | OCCT |
| SAMOCCTShellsIntersection | `Geometry.OCCT.Query.ShellsIntersection` | OCCT |
| SAMOCCTShellsRepair | `Geometry.OCCT.Query.ShellsRepair` | OCCT |
| SAMOCCTShellsSectionByPlane | `Geometry.OCCT.Query.ShellSectionByPlanes` — one MakerVolume pass over shell faces + bounded plane patches; on-plane cell faces are the sections | OCCT (changed in this review — was managed `Shell.Section`) |
| SAMOCCTShellsSplit | `Geometry.OCCT.Create.Shells` | OCCT |
| SAMOCCTShellsUnion | `Geometry.OCCT.Query.ShellsUnion` | OCCT |
| SAMOCCTTriangulateSurface | `Geometry.OCCT.Create.Triangulate` (BRepMesh) | OCCT |

## Change made in this review

`SAMOCCTShellsSectionByPlane` previously computed the section faces with the
managed `Shell.Section` (SAM engine) and only used OCCT to partition the level
cells afterwards. It now calls the new
`SAM.Geometry.OCCT.Query.ShellSectionByPlanes`, which feeds the shell's boundary
faces plus a bounded rectangular patch per plane into a single native
BOPAlgo_MakerVolume pass: the resulting cells are the level shells and the
on-plane cell faces (trimmed to the shell by OCCT, deduped by topology key) are
the section `Face3D`s. The managed `Shell.Section` remains only as the
explicit fallback when the native library is unavailable, reported via
`SAM_OCCT_SECTION_MANAGED_FALLBACK`.

## Intentional SAM-engine call sites (kept)

| Site | Reason |
| --- | --- |
| `SAM.Analytical.OCCT/Create/AdjacencyCluster.cs` — `global::SAM.Analytical.Create.AdjacencyCluster` | Fallback only, after the OCCT direct topology rebuild fails; reported via `SAM_OCCT_ANALYTICAL_DIRECT_FALLBACK`. The OCCT path always runs first. |
| `SAMOCCTShellsSectionByPlane` — managed `Shell.Section` | Fallback only, when the native library is unavailable; reported via `SAM_OCCT_SECTION_MANAGED_FALLBACK`. |
| `SAMOCCTCreateAdjacencyClusterByShells` — `adjacencyCluster.Cut(elevationGround)` | Splits panels at the ground elevation to assign underground panel types. The native bridge has no per-face split/section entry point; feeding the ground plane into the cell build would wrongly split the *spaces* too. Candidate for a future native face-split export. |
| `Create.Panel`, `Query.PanelType`, `Query.DefaultConstruction`, `GetArea`, `Inside`/`On`, `Distance` (various) | Object construction, classification, and cheap predicates around native results — not geometric computation. |

## Future candidates (need new native exports)

- Native face-by-plane split, which would also move the ground-elevation
  `AdjacencyCluster.Cut` onto OCCT.
- Native point-in-solid classification (`BRepClass3d`) to replace the managed
  `Shell.Inside`/`On` seed-space matching in `DirectAdjacencyCluster` and
  `MergeSmallShells`.
