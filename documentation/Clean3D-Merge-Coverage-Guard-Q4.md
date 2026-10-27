# Clean3D coplanar-merge coverage guard — Q4 (2026-10-27)

Status: implemented and validated locally on `fix/clean3d-merge-coverage-guard-q4` (base `sow/2026-Q4` at `22e4fd0`;
SAM `sow/2026-Q4` at `91f25290`). Draft PR into `sow/2026-Q4`; not merged. **Tested content `81e8a24`**; later commits
change this record only, and the PR description holds the final head and hosted checks. Implementation authorised by
the owner on 2026-10-27. Independent of SAM_OCCT#84 (draft, `a77ef7d`): no file overlap (`SnapStage.cs` and
`DiagnosticCode.cs` are identical on `22e4fd0` and `a77ef7d`); either can merge first.

## Problem (Softhouse R / #37)

`SnapStage.Clean` step 3 merges coplanar post-snap faces with SAM `Geometry.Spatial.Query.Union`. SAM converts each merged
NTS polygon back with `Face2D.Create`; with two or more holes, `LinearRing.ToSAM` can turn a sub-micrometre sliver hole
into a 2-point loop, which makes the whole merged face invalid, and `Spatial.Query.Union` skips it. SnapStage's only
fallback covered a fully empty union, so a whole coplanar group could vanish with no record and no diagnostic. Softhouse R
(faces reversed within each solid) lost the 2F slab (31 caps, 278.82 m2 at z 6.475): Clean3D 0 Warnings, then 0 spaces.
Pre-existing on base `22e4fd0` and on #84 `a77ef7d` (investigation: `handover/evidence/2026-10-27-r37-clean3d/README.md`,
machine-local). Which group is hit depends on the 2D union frame (the first piece's orientation/position, ULP normal
noise): 9 of 2000 synthetic slabs.

## Fix

`SAM_OCCT/SAM.Geometry.OCCT.Solver/Classes/SnapStage.cs`, `Clean`, new step 3a after the union and before attribution:

1. **Coverage check** (`RecoverLostPanels`). A usable post-snap panel is *lost by the merge* when its internal point
   (`Face3D.InternalPoint3D(tol)`) lies on no clean face of its plane, with the `Contributors` thresholds: |n.n'| >= 0.99,
   plane distance <= 0.05 m, inside/on 0.01 m. A face without an internal point is never reported lost. When none is lost
   the guard returns nothing: the same faces in the same order, no diagnostic, no record (the legacy union call is kept).
2. **Recovery.** Lost panels are grouped seed-first exactly as `Spatial.Query.Union` groups them (`Plane.Coplanar(tol)`,
   which compares normals only, plus the internal point within `tol` of the seed plane, so different levels are never
   projected together). Each group of two or more is re-unioned with SAM `Planar.Query.Union(face2Ds, tol)` in the seed
   plane; inner loops with fewer than 3 points or area <= `tol` are removed and the face rebuilt with
   `Planar.Create.Face2D`. The repair is accepted when every face is valid and the group's internal points are covered
   again; otherwise (and for a single lost face) the group's faces are kept unmerged. The result is appended to
   `cleanFace3Ds`, so the existing attribution, `CoplanarMerged` records and SourceMap apply unchanged.
3. **Diagnostic.** New `DiagnosticCode.CoplanarMergeRecovered` (appended last; existing ordinals unchanged). One
   **Warning** per recovered group at `SolverStage.Snap`, e.g.
   `Coplanar merge lost 31 panel face(s) (278.82 m2) on the plane with normal (0, 0, -1) at elevation 6.475 m; recovered: 1 degenerate loop(s) removed.`
   or `...; recovered: kept unmerged.` Warnings survive every detail level (DIAGNOSTICS_STANDARD 2.2); Clean3D shows it as
   `SAM_OCCT_CLEAN3D_DIAGNOSTIC: [Snap/CoplanarMergeRecovered/Warning] ...`.

## Decisions and assumptions

- Grouping mirrors `Spatial.Query.Union` rather than `Plane.Coplanar(tol)` alone: SAM's `Plane.Coplanar` compares normals
  only, so on its own it would project lost faces of different levels into one plane.
- Acceptance is by coverage (every face valid, every group member's internal point covered), as specified; the
  prototype's extra area check is not used. A single lost face is kept as it is ("kept unmerged").
- The planar union is injectable on the internal `RecoverLostPanels` (test seam, default SAM's union) so the rejected
  repair path is tested directly; no static or global state.
- The new code is appended to `DiagnosticCode` (the owner prompt allowed it; `SliverFace` means "collapsed input face
  dropped" and is not reused). Its detail is the default (Standard), which applies to Info only; the Warning survives
  every level.
- The Clean3D-level test is in `SnapStageTests.cs` (unit project): no Clean3D unit-test class exists, the test is pure
  managed, and it shares the seed-400 fixture. `Clean3DDiagnosticsIntegrationTests` is unchanged.
- No tolerance, golden, fixture, native, Grasshopper or `PROJECT_PROGRESS.md` change. The legacy union call and its
  whole-list fallback are kept.

## Files changed

- `SAM_OCCT/SAM.Geometry.OCCT.Solver/Classes/SnapStage.cs`: step 3a and helpers (`RecoverLostPanels`, `Covered`,
  `OnSeedPlane`, `Repair`, `DegenerateLoop`).
- `SAM_OCCT/SAM.Geometry.OCCT.Solver/Enums/DiagnosticCode.cs`: `CoplanarMergeRecovered` appended.
- `Testing/SAM.OCCT.UnitTests/SnapStageTests.cs`: seed-400 fixture (bit-exact, from the investigation's
  `synth/seed400/fixture-seed400-exact.txt`) and 5 tests (7 rows):
  `Clean_CoplanarMergeLeavesDegenerateHoleLoop_KeepsMergedSlabAndWarns` (bbl 0 / 0.21),
  `Clean_SameSlabDominantPieceReversed_MergesWithoutRecovery` (bbl 0 / 0.21),
  `RecoverLostPanels_RepairRejected_KeepsGroupUnmergedAndWarns`, `RecoverLostPanels_NothingLost_ReturnsEmptyWithoutDiagnostics`,
  `Clean3D_CoplanarMergeRecovered_WarningSurvivesEveryDetailLevel`.
- this record.

## Validation (local, machine `michal`; tested content `81e8a24`)

Builds: managed only (`SAM_OCCT_SKIP_NATIVE_BUILD=true`), Release, in an isolated root
(`C:\Users\michal\AppData\Local\Temp\cmg`) against copies of SAM `91f25290` (product tree) and SAM_Solver `fd63b46`
builds, through `handover/tools-2026-10-27-combined/scripts/safebuild.sh` (APPDATA/USERPROFILE redirected). Tests with
`--no-build`. `%APPDATA%\SAM` before/after: identical (2125 files). Every chain lists its loaded assemblies with SHA256
(0 `FALLBACK`).

```sh
sh safebuild.sh build SAM_OCCT.sln -c Release
sh safebuild.sh build Testing/SAM.OCCT.GrasshopperTests/SAM.OCCT.GrasshopperTests.csproj -c Release
sh safebuild.sh test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Release --no-build
sh safebuild.sh test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Release --no-build
sh safebuild.sh test Testing/SAM.OCCT.GrasshopperTests/SAM.OCCT.GrasshopperTests.csproj -c Release --no-build
sh scripts/run-chains.sh     # evidence folder below; softhouse.sh per variant, F and REVFACES=1 (R)
```

| Check | Base `22e4fd0` | Head (guard) |
| --- | --- | --- |
| Unit | 1038 / 1038 | **1045 / 1045** (+7 rows) |
| New tests on base (enum member only added) | test 1 (both rows) and the Clean3D test **fail** (1 face kept, no Warning); preservation passes | all pass |
| Mutation: guard call removed | - | 3 fail (test 1 x2, Clean3D) |
| Mutation: Warning mapped to Info | - | 4 fail (test 1 x2, Clean3D, rejected-repair) |
| Integration, native (438089F8, as the #83 baseline) | 460 / 34 failed / 2 not executed | identical to base by name, outcome and message |
| ... of which the #83 baseline set | 25 failed / 2 not executed | identical to `occt83-integration.trx` by name, outcome and message |
| ... 9 `HouseAirAndAtticIntegrationTests` | fail at fixture setup (APPDATA redirect hides SAM resources) | same; with real `%APPDATA%` (`--no-build`): 9 / 9 pass on both |
| Grasshopper tests | 33 / 33 | 33 / 33, incl. towers pins 24 cells (bbl 0) and 27 (0.21) |
| Detail levels | - | `DiagnosticsDetailIntegrationTests` 3 / 3 pass; the new Clean3D test keeps the Warning at levels 0/1/2 with identical panels |

So the integration result equals the #83 baseline (469 / 25 / 2) once SAM's resources are visible; no golden changed.

**Softhouse combined chains** (current SAM, no kink; PanelsBySolids -> SnapToJunctions 0.3 -> Clean3D bbl 0.21 ->
Extend3D -> OCCT -> Rooms2 v2 -> good; native A939AA72 as in the investigation):

| Run | Clean3D | Spaces | Good | Frozen 48 retained | `CoplanarMergeRecovered` |
| --- | --- | --- | --- | --- | --- |
| base F | 371 -> 80 | 71 | 48 | 48 | 0 |
| base + guard F | 371 -> 80 | 71 | 48 | 48 | 0 |
| base R | 367 -> 77 | **0** (harness stops on the empty space list) | - | 0 | 0 |
| base + guard R | 367 -> 78 | 74 | 39 | 39 | 1 |
| #84 F | 371 -> 80 | 58 | 48 | 47 | 0 |
| #84 + guard F | 371 -> 80 | 58 | 48 | 47 | 0 |
| #84 R | 367 -> 77 | **0** | 0 | 0 | 0 |
| #84 + guard R | 367 -> 78 | **59** | **45** | **44** | 1 |

- F with and without the guard: the 70-room table (`v2_rooms.tsv`), `v2_spaces.tsv` and `good.tsv` are byte-identical
  on both bases; `clean3d.txt`, `extend3d.txt`, the panel files and `model.json` are identical apart from per-run panel
  Guids. #84 F equals the SAM#225 acceptance (`r225-nokink-F`: 58 / 48 / 47 of 48).
- R with the guard: exactly one Warning, `Coplanar merge lost 31 panel face(s) (278.824 m2) on the plane with normal (0, 0, -1) at elevation 6.475 m; recovered: 1 degenerate loop(s) removed.`
  #84 + guard R reproduces the prototype exactly (59 spaces, 45 good, 44 of 48).

Evidence (machine-local, copy when switching machines):
`SAM_daily/2026-10-25-Softthouse/handover/evidence/2026-10-27-clean3d-merge-guard/` (`README.md`, `unit/`,
`mutation/`, `native-suite/`, `chains/`, `provenance/`, `scripts/`). Prerequisites: the `cmg` isolated root (rebuild
with the scripts), `cv27\SAM_OCCT\build` (native wrapper), the main checkout's `build` (native 438089F8), Rhino 8.

Not run: hosted CI (pending on the PR; native tests skip there), interactive Grasshopper canvas.

## Unresolved issues and risks

- R still differs from F by the upstream extraction differences (`PanelsBySolid` face order: 44 vs 47 of the 48 on
  #84); base + guard R (39 good) is lower because base lacks #84's Extend3D work. Neither is this PR's scope.
- Recovered geometry comes from the same frame-dependent SAM union; the unmerged fallback guarantees coverage with
  lower downstream quality (prototype: 37 vs 45 good on R).
- The root cause is in SAM (`LinearRing.ToSAM` re-check after dropping the closing duplicate; `Face2D.Create` /
  `ToSAM_Polygon2Ds` dropping degenerate loops; `Spatial.Query.Union` returning a group's members instead of skipping
  an invalid result; `Analytical.Create.PanelsBySolids` has the same exposure). **Follow-up: a separate SAM PR, owner
  decision.** This guard stays as defence and diagnostic.
- Out of scope and unchanged: Dining 4 A107 (#84), the kink candidate and its towers pin, the LevelFrame datum margin.

## Next step

Owner review of the draft PR and its hosted checks; merge only on owner approval, then the `PROJECT_PROGRESS.md`
closeout on `sow/2026-Q4`. Recommended next: Claude Code, Opus 5.5, high effort, new session, Plan mode, for the
owner's decision on the SAM root fix (SAM-wide geometry behaviour, so plan before build).
