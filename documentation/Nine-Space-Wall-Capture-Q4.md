# Nine-space wall-capture acceptance test - branch record (2026-10-22)

Branch: `test/nine-space-wall-capture-q4` (local only: not pushed, no PR, not merged). Based on the reviewed,
unpushed sew-guard head `3523ed264438f9fc7f1dd7a9eb5ee90c71ca8ee2` (guard code `a135c5f`), not on remote
`sow/2026-Q4` (`dc871f7`, which lacks the guard). Test-only: no production, fixture, project or workflow change.
Commits: `ef9bf81` (tests), `8210c85` (record), `30e0e27` (review finding N1: gate rejects
`SAM_OCCT_BUILD_UNRELIABLE`, negative control), then this record update. Tested content: `30e0e27`.
Specification: machine-local `SAM-BIM/.review-nine-space-bucket-followup-20261022/OPUS_NEXT_TEST.md`, owner images
and `source-manifest.json` beside it; review `REVIEW-20261023.md`, follow-up `CLAUDE-TEST-GATE-FOLLOWUP-20261023.md`.
`PROJECT_PROGRESS.md` is not touched (post-merge closeout only).

## Status

**The nine-space target is not met at `3523ed2`.** The test family is committed with its hard failures kept
reproducible (no skip, no relaxed expectation, no re-pinned golden). No production fix was attempted; a bounded
proposal for the North0 loss is below ("Proposed production correction").

Owner visual review, 2026-10-23 (machine-local `owner-visual-review-20261023/OWNER-CLARIFICATION.md`): "0.45m model
correct and 0.55 missing space". The 0.45 output is the accepted visual below-gap baseline: its West3/North0 gap
(annotated about 0.473 m) lies beyond that capture, so leaving it open is correct for that setting, not a failure of
the control. The 0.55 output losing North0 is rejected. This does not say the gap must stay open at every capture,
approve a shared-wall plane, or authorize production work. The above-gap target is unchanged: West3/North0 share a
boundary while staying two rooms (architect's recommendation for this fixture from the owner's arrow, review
2026-10-23; not a recorded owner approval, not a rule for other 0.474 m voids).

| Capture c (wall stamp) | Cells | Outcome against the full contract |
| --- | --- | --- |
| none (existing chain) | 11 | 9 matched + 2 Extra cells in the East1/South1 band; East1|South1 and West3|North0 not shared |
| 0.20 | 9 | no Extra, but East1|South1, East1|South2, West3|North0 not shared; one partial input face |
| 0.45 | 9 | **owner-accepted below-gap baseline**: every check passes, West3|North0 (0.474 m) correctly still apart |
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
   Earliest divergence: Clean3D. Consolidation moves North0's west wall ca160254 0.474 m onto edb39f15 (x 0.129 ->
   -0.345). Established on 2026-10-23 by a lineage trace and test-side interventions on input clones (no production
   change; "Lineage and intervention evidence" below), correcting the earlier cap-only reading:
   - **North wall end not dragged.** `DragAbuttingWallEnds` moves the south wall b03bc2a4's west end to -0.345 but
     not the north wall a577d53d's. The earlier align snap moved a577d53d 0.046 m (y -2.306 -> -2.26, onto North1's
     north wall), so its end lies 0.046 m beyond the end of the moved wall's old foot, over the 0.02 m
     `STACK_FOLLOW_END_TOLERANCE`. Extend3D cannot bridge 0.474 m with maxExtend 0.4: the NW corner stays open (2 open
     wall ends instead of 1).
   - **Roof edge 0.062 m short.** `DragAbuttingCapEdges` skips the roof 770b8239 (its edge starts 0.088 m inside the
     old plane; touch test 1e-6). Extend3D's directional cap growth is capped by fillMargin 0.5 < 0.474 + 0.088, so
     the roof's west edge stops at x -0.283.
   - **The floor is not a cause.** It is skipped by the drag (collapsed onto the slab skin at z 12.436, below the wall
     base 12.503), but Extend3D's fill already carries it past the new plane (x -0.894).
   Interventions at 0.55: roof edge alone, floor alone, floor+roof, north wall end alone -> North0 still
   Missing; north wall end + roof edge (0.55 and 0.65) -> 9 cells, all ten shared boundaries, no Extra, cells
   valid, coverage complete; with only West3|North0 stamped, North0 is kept and the unstamped band keeps its 2 Extra. Moving the roof edge by just the wall's 0.474 m (leaving the authored 0.088 m for Extend3D's fill) gives
   the same result. Both corrections are needed; together they suffice on this fixture.
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

`Testing/SAM.OCCT.IntegrationTests/NineSpaceWallCaptureIntegrationTests.cs` (new; 31 cases):

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
| `ContractGate_AcceptedBelowGapResultWithUnreliableWarning_IsRejected` (N1 negative control) | 5 | pass |

The contract (all violations are collected and reported together): 9 cells and 9 cluster spaces; every expected
space Matched one-to-one, no Extra/Missing/Merged/Split/IncorrectlyBounded; West3 double height; West1/West2 two
stacked cells; shared boundaries (cell face adjacencies named through the matcher) exactly North0|West3,
South2|West1, North0|North1, North1|North2, East1|South2, South1|South2, East1|South1, West1|West2, West1|West3,
West2|West3; finite positive volumes; `OcctUnderSplitGuard.ValidateCells` at max(Tolerance, Fuzzy) (closed,
non-sliver, disjoint); complete input-face coverage; no orphan cluster panel; no Error and none of
`BUILD_UNRELIABLE`/`SEW_BUILD_INVALID`/`SEW_VOLUME_CONFLICT`/`CELL_SPACE_DELTA`/`COVERAGE_UNAVAILABLE` (any severity;
producers emit them only as Warnings; other Info/Warning diagnostics allowed); no conditioning Error; original
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

Results at tested content `30e0e27` (2026-10-23): requested filter 33 = 25 passed, 8 failed, 0 skipped. The 8 are
the genuine model failures: `AcceptanceChain_CaptureAboveEveryPairGap_NineSpacesWithIntendedSharedBoundaries` at
0.55 and 0.65 (8 cells, North0 Missing, North0|North1 and North0|West3 not shared, `CELL_SPACE_DELTA`) and
`AcceptanceChain_RigidMotion_SameSpatialResultAfterInverseMotion` at 0.45/0.55/0.65 x translate/rotate Z 45. All 5
negative-control cases pass. Versus 2026-10-22 (28 = 20/8/0) the only change is the 5 new passing cases; both
existing `ControlledWorkflowAcceptanceIntegrationTests` pass unchanged (two-Extra allowance untouched). Mutation
check: with BUILD_UNRELIABLE excluded from the gate (the old behaviour) its control case fails ("expected
[diagnostic Warning SAM_OCCT_BUILD_UNRELIABLE], actual []"), reproducing the review probe; restored and rebuilt
before the final run. Integration build 0 errors / 124 warnings. 2026-10-22 results, not rerun: unit 975 passed;
related integration suites 203 passed. Full native suite not run.

Machine-local evidence (copy to keep), all under `SAM-BIM/.review-nine-space-bucket-followup-20261022/`:
`wall-capture-run/` (2026-10-22 logs and sweeps) and `wall-capture-exports/` (original input, seed spaces, processed
panels / adjacency cluster / cells for none, 0.20, 0.45, 0.55, 0.65, summaries, `SHA256SUMS.txt`, `README.md`; all
17 hashes re-verified unchanged 2026-10-23). Exported counts: none 43 panels / 11 cells / 114 cluster panels; 0.20
43 / 9 / 101; 0.45 39 / 9 / 88; 0.55 and 0.65 38 / 8 / 80. New 2026-10-23 evidence in `gate-followup-20261023/`:
build/test logs and TRX, mutation log, re-run exports (separate folder), lineage traces `lineage.txt`,
`lineage2.txt`, `lineage3.txt` and the probe source `ZzScratchNorth0Lineage.cs.txt` (not committed).

## Lineage and intervention evidence (2026-10-23)

Trace through Clean3D (SourceMap, CleanRecords) and Extend3D (its report's SourceMap, ExtendRecords) for North0's
walls, floor, slab-skin roof below, roof, North1's west wall/caps and the West block's east walls, at 0.45 (control),
0.55 global, 0.55 with only edb39f15/ca160254 stamped, and with these test-side edits on input clones before Clean3D
(original files and objects unchanged): floor/roof west edge moved to x -0.345, roof edge moved by 0.474 only,
north wall a577d53d west end moved to x -0.345.

| Case | North0 | Shared boundaries | Notes |
| --- | --- | --- | --- |
| 0.45 | kept, 78.543 m3 | 9 of 10 (no North0|West3) | 1 open wall end |
| 0.55 / 0.55 pair-only | Missing | North0|North1, North0|West3 absent | 2 open wall ends; roof stops at x -0.283 |
| 0.55 + floor, roof or floor+roof moved | Missing | same | 2 open wall ends |
| 0.55 + north wall end moved | Missing | same | 1 open wall end; roof still short |
| 0.55 / 0.65 + north wall end + roof (to plane or by 0.474) | kept, 88.373 m3 | all 10 | 9 cells, no Extra, ValidateCells ok, coverage complete |
| 0.55 pair-only + north wall end + roof | kept | all 10 | band unstamped: its 2 Extra remain |

The edited inputs also shift unrelated snaps slightly (a577d53d becomes longer, so its derived weight changes; North1
79.5 m3 instead of 80.047), so they establish the cause, not exact target volumes.

## Proposed production correction (not implemented; needs owner authorization)

Scope: the two private follow helpers of `Panel3DSnapSolver.ConsolidateWallStacks` only. No public signature change,
no new parameter, no tolerance increase, no change to which walls consolidate or to the chosen plane.

1. **Wall ends: decide the junction from lineage, not only from snapped geometry.** In `DragAbuttingWallEnds`, also
   treat a perpendicular wall's end as abutting when its source end (from the partner's `SourceFace3Ds`) lay within
   `STACK_FOLLOW_END_TOLERANCE` of the moved wall's source foot, then slide its current end onto the new plane as
   now. Here a577d53d's source end (0.129, -2.306) lies on ca160254's source foot. Earlier snaps (align, bucket) can
   move an end off the old foot by up to their own reach; the source test is independent of those moves.
2. **Cap edges: transport only the edge that faced the moved wall, by the move distance.** In
   `DragAbuttingCapEdges`, replace the uniform `GrowOutward(distance)` (which grows every edge of the cap) with an
   edge-local move of the boundary vertices of edges parallel to the old plane, on the room side, overlapping the
   wall's run, within an authored-offset band of the old plane; move them by exactly the wall's distance and leave
   the authored offset to Extend3D's fill (shown sufficient above). Eligibility must admit the roof's 0.088 m offset
   and compare storeys by level group rather than the exact wall base (the floor's 12.436 vs 12.503). The floor is
   not needed on this fixture; include it only if a regression shows it.

Open decisions for the owner/implementer: the authored-offset band (recommended: the solver's existing 0.3 m
`OPPOSED_PARTITION_MAX_SEPARATION`, never across another parallel wall), whether item 2 also replaces the uniform
growth for caps that are eligible today, and the storey test (level-group band vs the current z-band).

Independent regressions to add with the change:
- This family unchanged as acceptance: 0.55/0.65 pass the full contract (nine rooms, North0|West3 shared, North0 and
  West3 distinct, West3 double height); the 0.45 output spatially unchanged (owner-accepted baseline); the legacy
  `ControlledWorkflowAcceptanceIntegrationTests` unchanged.
- Isolated pair: only edb39f15/ca160254 stamped at 0.55 keeps North0 and adds North0|West3; the band is untouched.
- Unit (managed): a partner end snapped 0.046 m past the old foot is dragged; a wall whose source end lies 0.1 m
  beyond the foot is not; a cap edge 0.088 m inside the old plane moves by exactly the distance while its other
  edges stay; a neighbouring room's cap across the new plane, beyond the band, behind another wall or on another
  storey stays put.
- Real gap: a 0.474 m void with capture below it, or without stamps, is untouched (identical output).
- Existing consolidation/doubleWallGap suites (towers, ControlledWorkflow*, golden masters) and the full native suite
  compared against the known 19 failures.

Separate bounded issue (not part of this correction): pose dependence (finding 2). Proposal: make the two
`OrderByDescending(area)` selections (opposed collapse, stack dominant) treat areas equal within a relative 1e-9 as a
tie broken by source index, which is pose-invariant; trace the rotation difference before any change there. No
solver rewrite.

## Unresolved and owner decisions

- Authorize, or not, the bounded correction above as a separate production task; choose the authored-offset band and
  storey test. No shared-wall plane is approved; the proposal keeps the consolidation's existing dominant plane.
- Equal-area tie-breaks make results pose-dependent (finding 2); the rotation path is untraced.
- Not verified: full native suite; behaviour with explicit `doubleWallGap` beyond the sweep; any model other than this
  fixture. The 1.0 m unrelated-wall bound is specific to this fixture.

## Next step

Independent review of `30e0e27` and the proposal. Then, if the owner authorizes it, a separate production task
(Claude Code / Claude Opus 4.6 / High / new session / Build) for items 1-2 with the listed regressions, keeping this
family unchanged as acceptance; pose determinism as its own bounded task afterwards. No push, PR or merge is
authorized by this branch.
