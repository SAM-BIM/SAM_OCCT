# Sew-before-build under-split guard — Q4 (2026-10-13)

Status: implemented on `fix/sew-under-split-q4`; draft PR, owner review required. Not merged.
Base: `sow/2026-Q4` at `f584e94ee2abd0ebb1622c0ee614b1dc520c81c0`.
Tested content SHA: `f446731cf5cb2ce4e7d4d0fc82c04e7d71c2122c`. This record is the only later change.
PR: the live draft PR for this branch in `SAM-BIM/SAM_OCCT`; its description holds the final head and hosted checks.
Dependency: none. It builds against SAM `sow/2026-Q4` (local SAM build `69f999cf`).

## Problem (evidence)

A two-storey house (`SAM_daily/2026-10-13-House`: Untitled2.3dm + test2.gh, machine-local) has:

- 23 thin solids → GH mid-surface script → `CreatePanelBy3DGeometry` → `SAMOCCT.CreateAdjacencyCluster`;
- 10 labelled rooms plus a closed attic.

The component reported success with **4 spaces / 21 panels**:

- GF Living + Study merged; GF Kitchen + Bathroom merged; UF Bedroom 1 + 2 merged;
- UF Landing, Bedroom 3, Bathroom and the attic were missing.

Replaying the identical 23 panels gives:

| Build | Cells |
|---|---|
| sew on | 4 |
| sew off | 11 (one per room label, plus the attic) |

Mechanism (code-read):

- `CreateAdjacencyCluster` hard-codes `SewBeforeBuild=true`.
- `native/.../ShapeSew.cpp` uses manifold `BRepBuilderAPI_Sewing` and keeps only shells.
- Partitions meeting walls at T-junctions are lost without an error.
- The parity diagnostics only check internal consistency, so they still read "success".

This is deferred finding #7 ("under-split adoption") in `docs/CELLCOMPLEX_FIRST_HANDOVER.md`.

## Change

- `Query.FaceCoverage` (+ `FaceCoverageReport`, `FaceCoverageEntry`, `FaceCoverageVerdict`) is pure managed. For each input face:
  - Coverage by coplanar cell faces is measured by area (polygon difference), so a correctly subdivided face is still covered. The noise band is max(tolerance, fuzzy).
  - Each uncovered piece is classified by two point tests just either side of the face:
    - exterior (legitimate trim);
    - interior of one cell (cuts through without bounding it: under-split);
    - unresolved.
  - Verdicts: Covered / Trimmed / Unused / Partial / Missing / Unresolved.
- `OcctBuildOptions.ValidateSewCoverage` is **opt-in, default false**. When set with `SewBeforeBuild`, the sew build becomes an isolated candidate.
  - If coverage shows interior or unused faces, a direct MakerVolume candidate is built in isolation (`OcctUnderSplitGuard`).
  - It is adopted only if **all** of these hold:
    - its cells are closed, positive and not slivers (mean thickness and bounding-box extent at least fuzzy);
    - no cell's internal point lies in another cell;
    - every sew cell is exactly partitioned by direct cells (volume within 0.1 % + tolerance × area);
    - covered area strictly increases, with no face losing coverage and no growth in unresolved area.
  - `RetainTopology` keeps the sew build (the direct path frees its handle) and warns.
- Diagnostics (always Warning):
  - `SAM_OCCT_SEW_UNDER_SPLIT_SUSPECTED`;
  - `SAM_OCCT_BUILD_FALLBACK_DIRECT` (with both coverage summaries);
  - `SAM_OCCT_BUILD_UNRELIABLE` (naming the failed criteria);
  - `SAM_OCCT_COVERAGE_UNAVAILABLE`.
- `OcctCellComplexBuilder`:
  - the direct native build is extracted to `TryDirectBuild`, unchanged;
  - `OcctCellComplexResult.AdoptFrom` (internal) moves a candidate in.
- `SAMOCCTCreateAdjacencyCluster` sets `ValidateSewCoverage = true` and shows the codes above as Warning runtime messages. GUID, inputs and outputs are unchanged.

Decisions:

- **Opt-in.** A first version that always ran changed 19 Solve3D golden/parity tests. Those are the solver's own sew builds, and on investigation the same 19 already fail on the base, so their outcome isn't something this PR can validate. Opt-in keeps solver behaviour byte-identical. Enabling it for the solver is a separate decision.
- No native ABI change; public API is additive only.

## Validation (local, Windows, this machine; native built in `build/`)

```powershell
dotnet build SAM_OCCT.sln -c Debug
dotnet build Grasshopper/SAM.Analytical.Grasshopper.OCCT/SAM.Analytical.Grasshopper.OCCT.csproj -c Debug
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug
dotnet test Testing/SAM.OCCT.GrasshopperTests/SAM.OCCT.GrasshopperTests.csproj -c Debug
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug
```

- Unit: **648 passed / 0 failed**, including the 16 new `FaceCoverageTests` and the `ValidateSewCoverage` option test.
- Grasshopper contract: **15 / 0**.
- Integration: **249 passed / 19 failed / 2 skipped** (270).
  - All 7 new `HouseUnderSplitIntegrationTests` pass:
    - 11 cells, one per seed (10 rooms + attic);
    - total volume 564.1 ± 0.5 m³;
    - suspicion + fallback warnings and no errors;
    - no interior coverage after adoption;
    - RetainTopology keeps the sew build and warns;
    - the legacy path without the option still gives 4 cells;
    - determinism.
- **Baseline comparison:** the base `f584e94` (same machine, same SAM build) gives 242 passed / 19 failed / 2 skipped. The **19 failures are identical by name**, so they pre-exist and are unrelated:
  - Solve3D `GoldenMaster` managed/raw signatures;
  - `Extend3DPlaneTarget` ×4;
  - `WorkflowParity` ×4;
  - `PR61Face3DHomeParity`;
  - `Extend3DRegression` PR49-Test2;
  - `CreateSpaces` two-level-tilted raw.
  - They need their own investigation (likely SAM `sow/2026-Q4` drift against the pinned goldens).
- Fixture: `Testing/SAM.OCCT.IntegrationTests/Fixtures/House-Regression/House-TwoStorey-23panels.sam`.
  - Contains the 23 metre panels exactly as the GH script and `CreatePanelBy3DGeometry` produced them, plus 11 seed spaces.
  - Exported by a machine-local Rhino 8 harness (not committed).
- Not run: hosted CI on this head (see PR description); manual Rhino/Grasshopper test.

## Remaining / next

- Owner: review, then merge. Hosted `build` and `spdx` checks must pass at the final head.
- Separate follow-ups:
  - `MergeCoplanarBeforeBuild=true` drops this house to 6 spaces (not investigated here);
  - the 19 pre-existing integration failures;
  - whether Solve3D's internal builds should opt in.
- Dependent work: Air panels / unconditioned attic (SAM_OCCT, stacked on this branch) and solid → panel conversion (SAM).
