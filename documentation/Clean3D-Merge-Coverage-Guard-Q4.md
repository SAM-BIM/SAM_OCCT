# Clean3D coplanar-merge coverage guard — Q4 (2026-10-27)

Status: revision 1 implemented and validated locally on `fix/clean3d-merge-coverage-guard-q4` (base `sow/2026-Q4` at
`22e4fd0`; SAM `sow/2026-Q4` at `91f25290`). Draft PR SAM-BIM/SAM_OCCT#85 into `sow/2026-Q4`; not merged.
**Tested content `6f781e3`** (revision 1, after the review below; previously `81e8a24`). Later commits change this
record only, and the PR description holds the final head and hosted checks. Implementation authorised by the owner on
2026-10-27. Independent of SAM_OCCT#84 (draft, `a77ef7d`) in solver code; the Grasshopper version lines overlap (see
"Coordination with #84").

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
   (`Face3D.InternalPoint3D(tol)`) lies on no clean face of **its own union plane**: the plane identity the union itself
   uses (`SamePlane`: SAM `Plane.Coplanar(tol)`, which compares normals only, plus the point's offset within `tol`), and
   inside/on within `tol`. A face without an internal point is never reported lost. When none is lost the guard returns
   nothing: the same faces in the same order, no diagnostic, no record (the legacy union call is kept).
2. **Recovery.** Lost panels are grouped seed-first exactly as `Spatial.Query.Union` groups them (same `SamePlane` test
   at the internal point, so different levels are never projected together). Each group of two or more is re-unioned
   with SAM `Planar.Query.Union(face2Ds, tol)` in the seed plane. The **only** loops removed are inner loops with fewer
   than 3 points (null, or what `LinearRing.ToSAM` leaves of a sliver whose points fall within `tol` of each other): they
   bound no area, so nothing is filled. Every loop with 3 or more points is a genuine opening and is kept, whatever its
   size; no area is compared with a length. The re-merge is accepted when every face is valid and the group's internal
   points are covered again; otherwise (and for a single lost face) the group's faces are kept unmerged. The result is
   appended to `cleanFace3Ds`, so the existing attribution, `CoplanarMerged` records and SourceMap apply unchanged.
3. **Diagnostic.** New `DiagnosticCode.CoplanarMergeRecovered` (appended last; existing ordinals unchanged). One
   **Warning** per recovered group at `SolverStage.Snap`, e.g.
   `Coplanar merge lost 31 panel face(s) (278.824 m2) on the plane with normal (0, 0, -1) at elevation 6.475 m; recovered: 1 degenerate loop(s) removed.`
   or `...; recovered: kept unmerged.` Warnings survive every detail level (DIAGNOSTICS_STANDARD 2.2); Clean3D shows it as
   `SAM_OCCT_CLEAN3D_DIAGNOSTIC: [Snap/CoplanarMergeRecovered/Warning] ...`.
4. **Grasshopper versions** (GH_COMPONENT_STANDARDS GH-F2/H2: a changed computed result is a behavioural change, minor
   bump; GUIDs and parameters unchanged). Every component that runs the managed clean stage:
   `SAMOCCT.Clean3D` 0.6.0 -> 0.7.0, `Extend3D` 0.10.0 -> 0.11.0 (Stage A runs unless `inputAlreadyClean`), `Solve3D`
   0.7.1 -> 0.8.0, `AutoTune3D` (Discover) 0.4.0 -> 0.5.0, `AutoTune3D` (Legacy) 0.1.0 -> 0.2.0. Not affected (no clean
   stage): CreateAdjacencyCluster, CreateShells, PanelsFromShells and the geometry components.

## Review correction (revision 1) — 2026-10-27

Independent review of head `8111069` (machine-local
`Documents/Codex/2026-10-27/referenced-chatgpt-conversation-this-is-an/outputs/SAM_OCCT-85-review/REVIEW.md`, managed
probe included) returned REVISE with three findings; the first two were also in the implementation brief:

1. **Another plane could hide a loss.** Coverage used the attribution thresholds (|n.n'| >= 0.99, offset <= 0.05 m,
   inside/on 0.01 m): a missing 2 x 2 m face at z 3 counted as covered by a face at z 3.03, with no Warning. Now the
   union's own plane identity and `tol` (point 1 above).
2. **Recovery filled a genuine opening.** Loops with area <= `tol` were removed, comparing m2 with m: a valid
   1 x 1 mm, four-vertex opening (area 1e-6 m2) was filled and reported as "1 degenerate loop(s) removed". Now only
   loops with fewer than 3 points are removed (point 2).
3. **Grasshopper versions were not raised.** Now bumped and pinned (point 4).

The Softhouse failing/preservation cases, the Warning and the unmerged fallback are retained.

## Coordination with #84

#84 (`a77ef7d`) raises Extend3D, Solve3D and both AutoTune3D variants to the **same** target versions for its own
unpublished change; it does not touch Clean3D. One minor bump per component covers both unreleased changes.
- Component files: the version lines are identical changes on both branches, so they merge without conflict.
- Contract tests: `GHComponentContractTests.cs` (Legacy pin and test name `..._Is020`, same as #84) and
  `DiagnosticsContractTests.cs` (Solve3D pin) differ from #84 only in the comment line beside the same assertion, so
  whichever PR merges second gets a small textual conflict there. Resolution: keep the shared version and mention both
  changes in the comment; #84's separate `AutoTune3D_Discover_Version_Is050` duplicates a row of
  `CoplanarMergeGuardVersionTests` and can stay.
- If a release deploys the first PR's bump before the second merges, the second must bump again (next minor).
Neither PR needs to merge first.

## Decisions and assumptions

- Grouping and coverage mirror `Spatial.Query.Union`'s plane identity rather than `Plane.Coplanar(tol)` alone (which
  compares normals only) or the attribution thresholds (which let a parallel face 50 mm away mask a loss).
- Inside/on is tested within `tol`. A false "lost" would need a point more than `tol` outside every same-plane clean
  face, i.e. a real loss, so recovery cannot duplicate an existing face of the same plane.
- Repair is proven safe only when it removed nothing but zero-area (fewer than 3 points) loops, every face is valid and
  every member is covered; otherwise unmerged. A single lost face is kept as it is.
- The planar union is injectable on the internal `RecoverLostPanels` (test seam, default SAM's union); no static or
  global state.
- The new code is appended to `DiagnosticCode` (`SliverFace` means "collapsed input face dropped" and is not reused).
  Its detail is the default (Standard), which applies to Info only; the Warning survives every level.
- The Clean3D-level test is in `SnapStageTests.cs` (unit project): no Clean3D unit-test class exists, the test is pure
  managed, and it shares the seed-400 fixture. `Clean3DDiagnosticsIntegrationTests` is unchanged.
- The version pins use component names as strings in the theory data: a Grasshopper type in a test attribute loads
  Grasshopper during xUnit discovery, before the test assembly's resolver is installed.
- No tolerance, golden, fixture, native or `PROJECT_PROGRESS.md` change. The legacy union call and its whole-list fallback
  are kept.

## Files changed

- `SAM_OCCT/SAM.Geometry.OCCT.Solver/Classes/SnapStage.cs`: step 3a and helpers (`RecoverLostPanels`, `Covered`,
  `SamePlane`, `OnSeedPlane`, `Repair`, `DegenerateLoop`).
- `SAM_OCCT/SAM.Geometry.OCCT.Solver/Enums/DiagnosticCode.cs`: `CoplanarMergeRecovered` appended.
- `Grasshopper/SAM.Analytical.Grasshopper.OCCT/Component/SAMOCCT{Clean3D,Extend3D,Solve3D,AutoTune3DDiscover,AutoTune3DLegacy}.cs`:
  `LatestComponentVersion` only.
- `Testing/SAM.OCCT.UnitTests/SnapStageTests.cs`: seed-400 fixture (bit-exact, from the investigation's
  `synth/seed400/fixture-seed400-exact.txt`) and 9 tests (14 rows):
  `Clean_CoplanarMergeLeavesDegenerateHoleLoop_KeepsMergedSlabAndWarns` (bbl 0 / 0.21),
  `Clean_SameSlabDominantPieceReversed_MergesWithoutRecovery` (bbl 0 / 0.21),
  `RecoverLostPanels_RepairRejected_KeepsGroupUnmergedAndWarns`, `RecoverLostPanels_NothingLost_ReturnsEmptyWithoutDiagnostics`,
  `RecoverLostPanels_CleanFaceOnCloseParallelPlane_DoesNotHideTheLoss` (30 mm, 2 um),
  `RecoverLostPanels_CleanFaceWithinThePlaneTolerance_CoversTheFace` (0, 0.5 um),
  `RecoverLostPanels_GroupWithGenuineSmallOpening_KeepsTheOpening` (1 mm input opening; 1 mm opening formed by four
  pieces), `RecoverLostPanels_ReMergeLeavesAMemberUncovered_KeepsGroupUnmerged`,
  `Clean3D_CoplanarMergeRecovered_WarningSurvivesEveryDetailLevel`.
- `Testing/SAM.OCCT.GrasshopperTests/CoplanarMergeGuardVersionTests.cs` (new, 5 rows: version and GUID per component);
  `GHComponentContractTests.cs` (Legacy 0.2.0), `DiagnosticsContractTests.cs` (Solve3D 0.8.0).
- this record.

## Validation (local, machine `michal`)

Builds: managed only (`SAM_OCCT_SKIP_NATIVE_BUILD=true`), Release, in an isolated root
(`C:\Users\michal\AppData\Local\Temp\cmg`) against copies of SAM `91f25290` (product tree) and SAM_Solver `fd63b46`
builds, through `handover/tools-2026-10-27-combined/scripts/safebuild.sh` (APPDATA/USERPROFILE redirected). Tests with
`--no-build`. `%APPDATA%\SAM` before/after both rounds: identical (2125 files). Every chain lists its loaded assemblies
with SHA256 (0 `FALLBACK`).

```sh
sh safebuild.sh build SAM_OCCT.sln -c Release
sh safebuild.sh build Testing/SAM.OCCT.GrasshopperTests/SAM.OCCT.GrasshopperTests.csproj -c Release
sh safebuild.sh test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Release --no-build
sh safebuild.sh test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Release --no-build
sh safebuild.sh test Testing/SAM.OCCT.GrasshopperTests/SAM.OCCT.GrasshopperTests.csproj -c Release --no-build
sh scripts/run-chains.sh     # evidence folder below; softhouse.sh per variant, F and REVFACES=1 (R)
```

**Revision 1 (`6f781e3`)**

| Check | Result |
| --- | --- |
| Unit | **1052 / 1052** (base 1038 + 14 rows) |
| New revision-1 tests on the previous implementation (`81e8a24`'s `SnapStage.cs`) | 4 fail (close plane 30 mm and 2 um; both genuine-opening rows), 22 SnapStage rows pass |
| Mutation: guard call removed | 3 fail |
| Mutation: Warning mapped to Info | 6 fail |
| Mutation: loop removal by area <= 1e-6 m2 (the old rule) | 2 fail (both genuine-opening rows) |
| Mutation: plane offset 0.05 m (the old band) | 2 fail (both close-plane rows) |
| Integration, native (438089F8, as the #83 baseline) | 460 / 34 failed / 2 not executed, **identical to base** by name, outcome and message; the 9 resource-dependent `HouseAirAndAttic` tests pass 9 / 9 with real `%APPDATA%` (`--no-build`) -> 469 / 25 / 2, the #83 baseline set |
| Grasshopper tests | **38 / 38** (33 + 5 version rows), towers pins 24 cells (bbl 0) and 27 (0.21) |
| Detail levels | `DiagnosticsDetailIntegrationTests` 3 / 3 pass; the Clean3D test keeps the Warning at levels 0/1/2 with identical panels |

**Softhouse combined chains** (current SAM, no kink; PanelsBySolids -> SnapToJunctions 0.3 -> Clean3D bbl 0.21 ->
Extend3D -> OCCT -> Rooms2 v2 -> good; native A939AA72 as in the investigation):

| Run | Clean3D | Spaces | Good | Frozen 48 retained | `CoplanarMergeRecovered` |
| --- | --- | --- | --- | --- | --- |
| base F | 371 -> 80 | 71 | 48 | 48 | 0 |
| base + guard F (rev 1) | 371 -> 80 | 71 | 48 | 48 | 0 |
| base R | 367 -> 77 | **0** (harness stops on the empty space list) | - | 0 | 0 |
| base + guard R (rev 1) | 367 -> 78 | 74 | 39 | 39 | 1 |
| #84 F | 371 -> 80 | 58 | 48 | 47 | 0 |
| #84 + guard F (rev 1) | 371 -> 80 | 58 | 48 | 47 | 0 |
| #84 R | 367 -> 77 | **0** | 0 | 0 | 0 |
| #84 + guard R (rev 1) | 367 -> 78 | **59** | **45** | **44** | 1 |

- F with and without the guard: `v2_rooms.tsv` (70 rooms), `v2_spaces.tsv` and `good.tsv` byte-identical on both bases;
  `clean3d.txt`, the panel files and `model.json` identical apart from per-run panel Guids. #84 F equals the SAM#225
  acceptance (`r225-nokink-F`: 58 / 48 / 47 of 48).
- R with the guard: exactly one Warning, `Coplanar merge lost 31 panel face(s) (278.824 m2) on the plane with normal (0, 0, -1) at elevation 6.475 m; recovered: 1 degenerate loop(s) removed.`
  #84 + guard R reproduces the prototype (59 spaces, 45 good, 44 of 48).
- Revision 1 against the first implementation: all four guarded runs (F and R, base and #84) give identical room
  tables and identical Clean3D/panel/model output modulo Guids, so the Softhouse recovery is unchanged.

**First implementation (`81e8a24`)**: unit 1045 / 1045; new tests failed on base (1 face kept); mutations 3 / 4
failures; integration identical to base; Grasshopper 33 / 33; the same Softhouse table.

Evidence (machine-local, copy when switching machines):
`SAM_daily/2026-10-25-Softthouse/handover/evidence/2026-10-27-clean3d-merge-guard/` (`README.md`; first round `unit/`,
`mutation/`, `native-suite/`, `chains/`; revision 1 `rev1/` and `chains/{fix2,84fix2}-{F,R}`; `provenance/`,
`scripts/`). Prerequisites: the `cmg` isolated root (rebuild with the scripts), `cv27\SAM_OCCT\build` (native wrapper),
the main checkout's `build` (native 438089F8), Rhino 8.

Not run locally: hosted CI (on the PR; native tests skip there), interactive Grasshopper canvas.

## Unresolved issues and risks

- R still differs from F by the upstream extraction differences (`PanelsBySolid` face order: 44 vs 47 of the 48 on
  #84); base + guard R (39 good) is lower because base lacks #84's Extend3D work. Neither is this PR's scope.
- Recovered geometry comes from the same frame-dependent SAM union; the unmerged fallback guarantees coverage with
  lower downstream quality (prototype: 37 vs 45 good on R).
- The `CoplanarMergeRecovered` Warning wording is not pinned in a released golden; it is asserted by substring in tests.
- The root cause is in SAM (`LinearRing.ToSAM` re-check after dropping the closing duplicate; `Face2D.Create` /
  `ToSAM_Polygon2Ds` dropping degenerate loops; `Spatial.Query.Union` returning a group's members instead of skipping
  an invalid result; `Analytical.Create.PanelsBySolids` has the same exposure). **Follow-up: a separate SAM PR, owner
  decision.** This guard stays as defence and diagnostic.
- Out of scope and unchanged: Dining 4 A107 (#84), the kink candidate and its towers pin, the LevelFrame datum margin.

## Next step

Owner re-review of the draft PR at its final head and hosted checks; merge only on owner approval, then the
`PROJECT_PROGRESS.md` closeout on `sow/2026-Q4`. When #84 merges after this PR (or vice versa), resolve the
contract-test comment conflict as described under "Coordination with #84".
