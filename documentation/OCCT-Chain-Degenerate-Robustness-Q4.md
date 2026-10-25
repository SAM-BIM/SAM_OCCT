# OCCT chain robustness: zero-area faces in Clean3D, non-convex cells in the direct rebuild — Q4 (2026-10-25)

Status: implemented locally on `fix/occt-chain-degenerate-robustness-q4` (base `sow/2026-Q4` at `ac9bbc9`). PR SAM-BIM/SAM_OCCT#83 into `sow/2026-Q4` (opened 2026-10-25 on owner go-ahead). Tested content `506fda7` (after the review correction below; previously `5f2a8d7`); later commits change this record only, and the PR description holds the final head and hosted checks. Step C of the Softhouse "SAM model from solids" task. No dependency on the SAM
branches: the unit tests run against the current `..\SAM\build` (sow/2026-Q4 SAM).

## Problems (both "one bad item cancels everything")

1. **Clean3D merged nothing** when one input face had no area. A face collapsed to a line (~1e-14 m wide, still
   `IsValid`) has no internal point; SAM's `Face3D` Union then returns null and SnapStage stage 3 falls back to the
   unmerged faces. Softhouse (BySolids, minThickness 0.04): Clean3D 324 -> 324, OCCT rejected the build.
2. **The direct OCCT topology rebuild was abandoned for one non-convex cell.** `CreateSpaces` returned null when a cell had
   no centre inside it and SAM's `Shell.InternalPoint3D` returned null (it requires `Shell.IsClosed`, which small misfits
   between decoded faces defeat). The geometric fallback then lost cells: Softhouse 70 OCCT cells -> 62 spaces, the two
   L-shaped kitchen + circulation cells of houses A and B among the losses, and 14 spaces failing SAM Check
   ("not enclosed").

## Fix

1. `SnapStage.Clean` (`SAM.Geometry.OCCT.Solver`): a face with area <= the distance tolerance counts as degenerate. It is
   left out of the coplanar union, recorded `DroppedInvalid` like an invalid face, and reported once as a `SliverFace`
   Warning ("Dropped N panel face(s) with no area ..."). Inputs without such faces are unchanged.
2. `Create.AdjacencyCluster` direct rebuild (`SAM.Analytical.OCCT`): a last location fallback, `FaceOffsetLocation`, tries
   each face's internal point moved 0.05 / 0.01 m along its normal (both ways, largest faces first) and takes the first
   the shell contains. Info `SAM_OCCT_ANALYTICAL_FACE_OFFSET_LOCATIONS` names the cells that needed it. The existing
   `SAM_OCCT_ANALYTICAL_SPACE_LOCATIONS` text is unchanged.

## Files changed

- `SAM_OCCT/SAM.Geometry.OCCT.Solver/Classes/SnapStage.cs`
- `SAM_OCCT/SAM.Analytical.OCCT/Create/AdjacencyCluster.cs`
- `Testing/SAM.OCCT.UnitTests/SnapStageTests.cs` (+1 test), `Testing/SAM.OCCT.UnitTests/FaceOffsetLocationTests.cs` (new, 2 tests)
- this record

## Validation (local; native SAM.Occt.Native 438089F8..., copied from the main checkout's `build`)

```powershell
dotnet build SAM_OCCT.sln -c Debug
dotnet build Grasshopper/SAM.Analytical.Grasshopper.OCCT/SAM.Analytical.Grasshopper.OCCT.csproj -c Debug
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug --no-build
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug --no-build
dotnet test Testing/SAM.OCCT.GrasshopperTests/SAM.OCCT.GrasshopperTests.csproj -c Debug --no-build
```

- Unit 1035 / 1035 (baseline 1032 + 3). The zero-area test fails without the SnapStage guard (3 faces returned, nothing
  merged). The face-offset test asserts its preconditions (centre outside, `Shell.InternalPoint3D` null).
- Integration 469 passed / 25 failed / 2 not executed: the same 27 non-passing names with identical messages as the
  SAM_OCCT#82 baseline (`SAM-BIM/.evidence-towers-managed-tests-20261023/native-full-non-passing.txt`). Evidence
  (machine-local): `SAM-BIM/.evidence-softhouse-20261025/occt-integration-final.trx` and `...-final-non-passing.txt`.
- Grasshopper tests 33 / 33.
- Always `dotnet test --no-build` after a redirected build: a plain `dotnet test` rebuilds and the OCCT Grasshopper
  PostBuild deploys to `%APPDATA%\SAM`.

## Softhouse evidence (machine-local)

With the SAM branches (UnitScale, PanelsBySolids, Face3D Union) and this branch, GH chain
CreatePanelsBySolids (minThickness_ 0.04) -> Clean3D -> Extend3D -> CreateAdjacencyCluster: 70 spaces (GF 27 / 1F 22 /
2F 21; reference 28 / 22 / 20), direct rebuild success (parity 671 / 673, 0 zero-relation panels), 41 windows hosted.
69 of 70 reference rooms lie inside a model space; 9 spaces hold two reference rooms each (open plan). SAM Check:
6 "not enclosed (1e-6)" warnings, all for the house A/B open-plan cells (GF kitchen + circulation, 1F double-height
region, two 2F rooms), whose SAM shells are not closed at 1e-6 (volume NaN) although OCCT closes them.

## Open

- Parity 671 / 673 (2 relations short) and the 6 not-enclosed cells: not diagnosed further.
- Without the SAM `SnapToJunctions` fix, Softhouse still needs this SnapStage guard; with it, the guard is a safety net.

## Next step

Owner review of the PR and its hosted checks; merge only on owner approval, then the PROJECT_PROGRESS.md closeout on `sow/2026-Q4`. Independent of the SAM PRs for CI.
