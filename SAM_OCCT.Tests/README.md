# SAM.OCCT.Tests

Macro / integration tests (xUnit, .NET 8) for the SAM_OCCT managed API and
native OCCT bridge.

## How to run

From the SAM_OCCT repository folder:

```powershell
dotnet test SAM_OCCT.Tests
```

The built-in smoke test creates a simple closed box from `Face3D` geometry and
runs it through `SAM.Geometry.OCCT.Create.Shells`.

## Fixtures

Drop exported `.sam` files into:

```text
SAM_OCCT.Tests/Fixtures
```

The fixture test scans every `*.sam` file in that folder, extracts usable
`Face3D` geometry, then runs the face set through OCCT. It accepts common SAM
exports, including:

- raw `Face3D` / `Face3DObject`
- `Shell` / `ShellObject`
- `SAMGeometry3DGroup` / `SAMGeometry3DObjectCollection`
- `Panel`, `AdjacencyCluster`, or `AnalyticalModel`

For fixture files, save with the `.sam` extension. JSON or extension-less files
are not picked up by this test project.

If a fixture fails, the test output prints OCCT diagnostics such as native
loading errors, rejected loops, cell counts, and decoded topology.
