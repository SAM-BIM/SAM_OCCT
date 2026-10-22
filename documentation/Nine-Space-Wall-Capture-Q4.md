# Nine-space wall-capture acceptance test - branch record (2026-10-22)

Branch: `test/nine-space-wall-capture-q4` (local only: not pushed, no PR, not merged). Based on the reviewed,
unpushed sew-guard head `3523ed264438f9fc7f1dd7a9eb5ee90c71ca8ee2` (guard code `a135c5f`), not on remote
`sow/2026-Q4` (`dc871f7`, which lacks the guard). Test-only: no production, fixture, project or workflow change.
Tested content: commit `ef9bf81` (the test file); the commit after it adds only this record.
Specification: machine-local `SAM-BIM/.review-nine-space-bucket-followup-20261022/OPUS_NEXT_TEST.md`, owner images
and `source-manifest.json` beside it. `PROJECT_PROGRESS.md` is not touched (post-merge closeout only).

## Status

**The nine-space target is not met at `3523ed2`.** The test family is committed with its hard failures kept
reproducible (no skip, no relaxed expectation, no re-pinned golden). The gap goes back to the owner as a separate
decision; no production fix was attempted.

| Capture c (wall stamp) | Cells | Outcome against the full contract |
| --- | --- | --- |
| none (existing chain) | 11 | 9 matched + 2 Extra cells in the East1/South1 band; East1|South1 and West3|North0 not shared |
| 0.20 | 9 | no Extra, but East1|South1, East1|South2, West3|North0 not shared; one partial input face |
| 0.45 | 9 | **meets every check except West3|North0 (0.474 m gap, below this capture)** |
| 0.55, 0.65 | 8 | **North0 Missing**; North0|North1 and West3|North0 not shared; `CELL_SPACE_DELTA` |

Rigid motion: at 0.45, 0.55 and 0.65 both a translation and a 45-degree Z rotation change the geometry.

## The capture control

`Clean3D` resolves each panel's capture half-width as stamp > max(minBucketSize, 0.6 x thickness). Every wall here
is 0.208 m thick, so the unstamped value is the 0.4 m floor (not `bucketBetweenLevels`, which only groups level
datums). A stamped `BucketSize` is also that panel's consolidation range in `ConsolidateWallStacks`, which an
unstamped run enters only with `doubleWallGap > 0`. The test therefore stamps c on every wall of a fresh clone
(caps stay unstamped, so caps never consolidate). The exploratory sweep showed `doubleWallGap = c` without stamps
gives the same outcome at 0.35, 0.45, 0.55, 0.65 and 0.8. `Extend3D(inputAlreadyClean: true)` skips Stage A, so the
stamps it receives are inert there.

Held fixed: weights (derived length), maxExtend 0.4, alignColinearOffset 0.3, normalizeCapOffset 0.3, fillMargin
0.5, directionalCapGrow, bucketBetweenLevels 0.21, build options exactly as
`ControlledWorkflowAcceptanceIntegrationTests` (Tolerance 1e-6, Fuzzy 1e-3, AvoidInternalShapes false, sew 0.01,
MergeCoplanarBeforeBuild) plus `ValidateSewCoverage = true` (the reviewed guard). Matcher: `ExpectedSpaceSet` from
the cleaned panels, LevelBand/LevelGroupBand 0.21, West3 GUID-backed double height.

Stages that can close a wall pair in Stage A, in order: (1) opposed-partition collapse (anti-parallel, within the
bucket, separation <= 0.3 m, footprint ratio >= 0.97); (2) the bucket snap to a fixed point (within the half-width,
overlap >= 0.5 of the larger footprint, angle 0.3 degrees unless wholly in the slab, then 5 degrees; equal weights
move both to the midplane and grow both buckets) or the colinear-align test (offset <= 0.3 m, independent of the
bucket); the 0.3 m void guard blocks any anti-parallel pair wider than that; (3) wall-stack consolidation (stamp or
`doubleWallGap`; overlap >= 0.75 of the smaller; each move capped at the range; dominant = largest area).

## Intended wall pairs (authored geometry)

Separation is the solver's `PerpendicularSeparation` (plane of A to the centroid of B). All are plane gaps with
in-plane overlap; the only in-plane edge gaps are exterior jogs (north row south/north walls, 0.10-0.14 m along the
run), which are not room separators. Room pairs come from the seed locations and the fixture record
`docs/CONTROLLED_WORKFLOW_BASELINE.md`; the two owner arrows mark West3|North0 and East1|South1.

| Rooms | Panel A / B | Gap (m) | Angle | Overlap/smaller | Run x height (m) | Stage that closes it |
| --- | --- | --- | --- | --- | --- | --- |
| West3|North0 | edb39f15 / ca160254 | 0.4738 | 0 | 0.912 | 7.20 x 2.71 | consolidation only (anti-parallel > 0.3) |
| West1|South2 | edb39f15 / 0664c970 | 0.1633 | 0 | 0.853 (0.227 of larger) | 6.64 x 2.74 | align test, any c |
| North0|North1 | d5c1f0bc / 61ba52da | 0.0981 | 0.49 deg | 0.960 | 7.02 x 2.93 | bucket, c >= 0.2 (slanted, must lie wholly in the slab) |
| North1|North2 | 9701dd76 / 1b2ef1d0 | 0.1354 | 0 | 0.961 | 7.02 x 2.93 | align test, any c |
| East1|South2 | aa6f86f4 / 5d97f223 | 0.2242 | 0.47 deg | 0.622 | 3.15 x 2.87 | bucket, closed at 0.45 not 0.20 |
| South1|South2 | 3b032875 / 5d97f223 | 0.1189 | 0.47 deg | 0.967 | 3.38 x 2.87 | bucket, closed at 0.20 not 0.05 |
| East1|South1 | 20fe83aa / 5b9dbfd6 | 0.4278 | 0 | 0.983 | 5.97 x 2.97 | opposed + snap leave 0.3804, then consolidation |

East1|South1 is a four-skin band (skins 20fe83aa, 31f97c71, 763f6aa3, 5b9dbfd6 at y = -23.006/-23.228/-23.339/
-23.434) plus a closed 0.111 m box (end walls ae9d38d0, 5ec136d2, caps 2a293c59, 2f01b2fa). The opposed pass folds
31f97c71 onto 763f6aa3; 20fe83aa|31f97c71 misses the 0.97 floor (0.968); the bucket snap then pulls 5b9dbfd6
onto the merged plane; the residue 0.380 m needs consolidation. Doubled exterior skins (no room behind):
b7b8011e/67adbfb7 0.084 m, 51a74459/1be9bfbd 0.170 m, 51a74459/631c4c4a 0.275 m. Every other facing wall pair is at
least 3.484 m apart, so the test asserts a 1.0 m unrelated-wall bound; 0.55 and 0.65 are well inside it. Two GUIDs
repeat in the fixture (74eeefd7, f2142a9f); no pair panel is one of them. Ambiguity: West3|North0 is an intended
single wall on the owner's evidence only (the images give no distance); 0.474 m exceeds the 0.3 m wall-thickness
ceiling the solver otherwise uses to keep real voids.

Capture choices: below-gap controls 0.05 (North0|North1, South1|South2), 0.20 (East1|South2, East1|South1), 0.35
(East1|South1), 0.45 (West3|North0); above every gap 0.55 and 0.65 (>= 0.076 m above 0.474, <= 0.65 m below the
bound). Between the band (0.428 authored, 0.380 effective) and West3|North0 (0.474) only 0.45 has >= 2 cm margins,
so that window has one value, used as a model-level below-gap control, not as the acceptance.

## Findings

1. **North0 is lost whenever West3|North0 is captured** (0.55, 0.65 and with only that pair stamped at 0.55).
   Earliest divergence: Clean3D. Consolidation moves ca160254 0.474 m onto edb39f15 and drags the abutting wall end
   (`StackConsolidated`, "0 cap edge(s) dragged"). North0's floor 4eeae908 was first collapsed onto 9016714c at
   z 12.436, below the wall's base 12.503, so `DragAbuttingCapEdges`' storey test skips it; its roof 770b8239 starts
   0.088 m inside the old plane, beyond the 1e-6 touch test. Extend3D then reports 2 open wall ends (1 at 0.45) and
   the build closes 8 cells. Reading of the code and diagnostics; not instrumented further.
2. **Rigid motion is not invariant.** Translation by (123.4, -56.7, 7.8): the equal-area roof/floor pair
   5941f5cf/286dce8e (205.2 m2 each) collapses the other way (West2/West3 top at 18.26 instead of 18.34, -7.8 and
   -8.6 m3), and the equal-area band skins (18.046 m2) choose another consolidation dominant (East1|South1 boundary
   at y -23.006 instead of -23.387, East1 -7.5 m3, South1 +7.5 m3). Both tie-breaks are `OrderByDescending(area)`
   over floating-point areas. Rotation: North0/North2 south edges move 46 mm (y -9.46 vs -9.414) and the band moves
   0.214 instead of 0.380; not traced further. Matched rooms, cell counts and shared-boundary sets do not change.
3. The two baseline Extra cells sit inside the band: (6.58, -23.173) 6.573 m3 between 20fe83aa and the folded
   skins, (6.581, -23.363) 0.935 m3 against 5b9dbfd6. Not monotone in c: 0.05, 0.20 and 0.45 and above leave none,
   0.35 leaves one (7.509 m3, exploratory sweep; stamp and doubleWallGap alike).
4. 0.55 and 0.65 give the same spatial result (test passes), so the failure is not an unlucky value.

## Tests

`Testing/SAM.OCCT.IntegrationTests/NineSpaceWallCaptureIntegrationTests.cs` (new; 26 cases):

| Test | Cases | Result at `3523ed2` |
| --- | --- | --- |
| `IntendedPairs_AuthoredGeometry_MatchRecordedSeparationsAndNoUnrelatedWallNearby` | 1 | pass |
| `CleanStage_CaptureBelowPairGap_PairStaysFartherApartThanCapture` | 6 | pass |
| `CleanStage_AlignEligiblePair_ClosedByAlignPassEvenAtSmallCapture` | 2 | pass |
| `CleanStage_CaptureAboveEveryPairGap_ClosesEveryIntendedPairWithinRequestedRange` | 2 | pass |
| `AcceptanceChain_CaptureAboveEveryPairGap_NineSpacesWithIntendedSharedBoundaries` | 2 | **fail** (finding 1) |
| `AcceptanceChain_CaptureBetweenBandAndWestNorth_NineSpacesWithOnlyWestNorthLeftApart` | 1 | pass |
| `AcceptanceChain_TwoCapturesAboveEveryPairGap_SameSpatialResult` | 1 | pass |
| `AcceptanceChain_RigidMotion_SameSpatialResultAfterInverseMotion` | 6 | **fail** (finding 2) |
| `Export_PrimarySetting_WritesReadableSamFilesForOwnerReview` | 5 | pass |

The contract (all violations are collected and reported together): 9 cells and 9 cluster spaces; every expected
space Matched one-to-one, no Extra/Missing/Merged/Split/IncorrectlyBounded; West3 double height; West1/West2 two
stacked cells; shared boundaries (cell face adjacencies named through the matcher) exactly North0|West3,
South2|West1, North0|North1, North1|North2, East1|South2, South1|South2, East1|South1, West1|West2, West1|West3,
West2|West3; finite positive volumes; `OcctUnderSplitGuard.ValidateCells` at max(Tolerance, Fuzzy) (closed,
non-sliver, disjoint); complete input-face coverage; no orphan cluster panel; no Error and no
`SEW_BUILD_INVALID`/`SEW_VOLUME_CONFLICT`/`CELL_SPACE_DELTA`/`COVERAGE_UNAVAILABLE`; no conditioning Error; original
panels unchanged (JSON compared). Motion comparison by expected room: volume within b x surface area and bounding
box (vertices taken back through the inverse motion) within 2b, b = 1e-3 m fuzzy band; Extra cells and shared
boundaries compared too. Exports go to `SAM_OCCT_NINE_SPACE_EXPORT_DIR` (default `%TEMP%\SAM_OCCT_NineSpaceWallCapture`)
labelled with `SAM_OCCT_TEST_CODE_SHA`, are read back and their object counts compared.

## Validation (local, Windows; no hosted CI)

Native: frozen `build/` copied from the guard worktree (ignored), `SAM.Occt.Native.dll` SHA256 438089F8...,
`TKBO.dll` 1FA732FE.... Managed dependencies read-only: SAM `ba686d24`, SAM_Solver `fd63b46`. Builds with
`-p:SAM_OCCT_SKIP_NATIVE_BUILD=true`: integration 0 errors / 124 warnings, unit 0 errors / 83 warnings (incremental).
No test was skipped.

```powershell
dotnet build Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug -p:SAM_OCCT_SKIP_NATIVE_BUILD=true
$env:SAM_OCCT_NINE_SPACE_EXPORT_DIR = "<dir>"; $env:SAM_OCCT_TEST_CODE_SHA = "3523ed264438f9fc7f1dd7a9eb5ee90c71ca8ee2"
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug --no-build --filter "FullyQualifiedName~NineSpaceWallCaptureIntegrationTests|FullyQualifiedName~ControlledWorkflowAcceptanceIntegrationTests" --logger "console;verbosity=detailed"
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug --no-build
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug --no-build --filter "FullyQualifiedName~ControlledWorkflow|FullyQualifiedName~Clean3DDiagnostics|FullyQualifiedName~BucketMergeDiagnostic|FullyQualifiedName~EastSouth|FullyQualifiedName~SewGuardInvalidCell|FullyQualifiedName~StageReport"
```

Results: requested filter 28 = 20 passed, 8 failed (the 2 + 6 above), 0 skipped; both existing
`ControlledWorkflowAcceptanceIntegrationTests` pass unchanged (their two-Extra allowance is untouched). Unit 975
passed. Related integration suites 203 passed, 0 failed, 0 skipped. Full native suite not run.

Machine-local evidence (copy to keep): `SAM-BIM/.review-nine-space-bucket-followup-20261022/wall-capture-run/`
(build and test logs, the exploratory sweeps `sweep1.txt`, `sweep2.log`) and `wall-capture-exports/` (original
input, seed spaces, processed panels / adjacency cluster / cells for none, 0.20, 0.45, 0.55, 0.65, per-setting
summaries, `SHA256SUMS.txt`, `README.md`). Exported counts: none 43 panels / 11 cells / 114 cluster panels; 0.20
43 / 9 / 101; 0.45 39 / 9 / 88; 0.55 and 0.65 38 / 8 / 80.

## Unresolved and owner decisions

- Is West3|North0 (0.474 m, beyond the solver's 0.3 m wall-thickness ceiling) to become one shared boundary? If yes,
  consolidation must carry North0's caps with its wall (finding 1) - a production change for a separate task.
- Equal-area tie-breaks in the opposed collapse and stack consolidation make results pose-dependent (finding 2).
- Not verified: the rotation divergence path; full native suite; behaviour with explicit `doubleWallGap` beyond the
  sweep; any model other than this fixture.

## Next step

Owner review of the exports and decision on finding 1 (and 2). Then, if approved, a separate production task (Claude
Code / Claude Opus 4.6 / High / new session / Build): make the stack consolidation drag each abutting cap of the
moved wall whatever its storey normalisation, and make the area tie-breaks deterministic, re-running this family
unchanged as acceptance. No push, PR or merge is authorized by this branch.
