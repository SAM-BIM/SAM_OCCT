# OCCT SAM fixtures

Place exported Face3D, shell, panel, adjacency cluster, or analytical model
fixtures here as `*.sam` files.

The test project copies these files to the test output and runs each one through
the OCCT cell-complex builder.

Prefer `.sam` (the compressed zip container `SAM.Core.Convert`/`.ToFile` already
writes/reads) over raw `.json` model exports - typically ~80% smaller for the
same content and it's the format every fixture-loading test already expects.

Subfolders are for fixtures a *dedicated* test loads by exact path, kept OUT of
`OcctFixtureIntegrationTests.Shells_UploadedSamFixtures_BuildCells`'s top-level
scan (which asserts every top-level `.sam` raw-builds a single watertight
shell) - each subfolder must also be added to the `.csproj`'s
`CopyToOutputDirectory` item group or its fixtures never make it to the test
output.

- `Robustness/`: pathological models that must not crash, not necessarily close.
- `Extend3D-Regression/`: PR #49's original bug-report models (real exports with
  gaps between panels - "walls disappear after SAMOCCT.Extend3D") - deliberately
  not watertight raw, exercised by `Extend3DRegressionIntegrationTests`.
