# `SnappedPanel.GrowOutward` keeps openings - Q4 (2026-11-18)

Status: draft PR into `sow/2026-Q4` (base `83143ca`), branch `fix/extend3d-grow-outward-holes-q4`. **Not ready to merge: on the
Softhouse model the fix turns a working chain into a failing one (see "Softhouse result") and 28 native integration tests newly fail. Owner decision required.** PR #84 is
untouched; the shared SAM offset API is untouched.

**Tested content `5a94440`.** The commit that adds this record changes this record only.

## Problem

`SnappedPanel.GrowOutward` (default cap fill: `FillCore` -> `GrowOutwardTo` -> `GrowOutward`) called the shared SAM
`Query.Offset(face2D, margin, includeExternalEdge: true, includeInternalEdges: true, tol)`. In SAM `Offset.cs` the internal-edge
loop offsets the **external** edge, so each opening became a copy of the grown outline and the face lost every opening. No
`SAM_OCCT_EXTEND3D_HOLE_DROPPED` was emitted. The method's own contract says "only the external boundary is offset; holes are
preserved".

Probe before the fix: 10 x 10 m slab, 2 x 2 m opening, 0.5 m growth -> 11 x 11 m, 0 openings, 121 m2.

## Change

`SAM_OCCT/SAM.Geometry.OCCT.Solver/Classes/SnappedPanel.cs`, `GrowOutward` only:

- offsets only the external boundary (`includeInternalEdges: false`) and takes the largest result by external area;
- rebuilds the face with `Face3D.Create(plane, grownExternal, face2D.InternalEdge2Ds)`, so the openings keep their original
  position and shape;
- reports any residual loss on the adopted face through the existing `RecordHoleDrop(holesBefore, grown3D, "cap grow")`;
- adopts through `Adopt` (same effect as before: clears `contactEvidence`).

`GrowOutwardTo` and the directional grow already end in `GrowOutward` / `RecordHoleDrop`; their code is unchanged and covered by tests.

## Tests (permanent, pure managed)

`Testing/SAM.OCCT.UnitTests/GrowOutwardHoleTests.cs` (new, 5 tests):

| test | before fix | after fix |
| --- | --- | --- |
| `GrowOutward_SlabWithOpening_GrowsPerimeterAndKeepsOpeningInPlace` (10 x 10 slab, 2 x 2 opening at 4..6, 0.5 m: outline -0.5..10.5 = 11 x 11, 121 m2; opening 4..6 x 4..6, 4 m2; net 117 m2) | **fail** (opening lost) | pass |
| `GrowOutwardTo_SlabWithOpeningShortOfWall_GrowsToWallAndKeepsOpeningInPlace` (wall 0.3 m outside + 0.05 overshoot: 10.7 x 10.7, opening unchanged, net 110.49 m2) | **fail** (opening lost) | pass |
| `GrowOutward_SlabWithOpening_PreservedOpeningEmitsNoHoleDropDiagnostic` | pass | pass |
| `GrowOutward_SolidSlab_StillGrows` | pass | pass |
| `GrowOutwardTo_SlabWithOpeningNoWallInReach_ReturnsFalseAndKeepsOpening` | pass | pass |

The two failing tests failed for the stated reason (`Assert.NotNull` on the internal edges); the three guards passed before the fix.
Not tested: a case where a grown face really drops an opening (no input found that does); `RecordHoleDrop` itself is covered by
`SetVerticalFootprint_ShortenThroughWindow_EmitsHoleDroppedDiagnostic`.

## Validation

| check | result | where |
| --- | --- | --- |
| unit, full suite | 1212 / 1212 passed (incl. the 5 new) | local, Debug, redirected APPDATA |
| native integration, fix `5a94440` | **53 failed / 450 passed / 2 skipped** (Q4 control `83143ca` on the same stack: 25 / 478 / 2); **28 tests newly fail, 0 recover**, all 25 control failures still fail | local, Release, frozen native `438089F8`, `suites/fix-integration.trx` |
| hosted CI | not yet run (draft PR) | GitHub |
| Grasshopper tests | not run | - |

## Softhouse result (baseline vs fix, both face orders)

Setup: baseline `sow/2026-Q4` `83143ca` and fix `5a94440` built from `git archive` into one isolated root, against the same SAM
`aa1cb777` build, SAM_Solver `fd63b467` build, frozen native `438089F8` / TKBO `1FA732FE`; same chain and settings
(`softhouse.sh`: CONT 0.01, MINTHK 0.04, MAXRATIO 0.2, SNAP 0.3, BBL 0.21, FILL 0.5; R adds `REVFACES=1`); APPDATA redirected;
`%APPDATA%\SAM` hashes before/after identical. Baselines reproduce the 2026-11-16 plain-Q4 control (F 72 / 397 / 48, R 71 / 395 / 46).

| chain | spaces | panels | good | frozen 48 kept |
| --- | --- | --- | --- | --- |
| baseline F | 72 | 397 | 48 | 48 |
| **fix F** | **none: cell build fails** | - | - | 0 |
| baseline R | 71 | 395 | 46 | 46 |
| **fix R** | **none: cell build fails** | - | - | 0 |

Fix F and R: `SAM_OCCT_NATIVE_FAILED` status 40; `SAM_OCCT_VALIDATE_INPUT`: input not watertight, 128 naked edges (first near
(8.289, 5.039, -0.025)), 76 self-intersections. Extraction, snapping and Clean3D give the same counts as the baseline (F: 384 panels, Clean3D 371 -> 79; R: 380, 367 -> 79) and the same 16 openings;
the Extend3D diagnostics differ only in the `cap-grow` area lines (e.g. #0 327.058 -> 384.51 on baseline, -> 360.544 on the
fix); the other 296 lines are identical after sorting. No `HOLE_DROPPED` line in any run (nothing is dropped now; the baseline
emitted none either).

### Openings, matched by level and location

Match rule: same kind, level within 0.05 m, centroid within 0.05 m, area within 2 % (0.005 m2). Sets from `scripts/holes.ps1`.

| stage | F | R |
| --- | --- | --- |
| Clean3D output (input of Extend3D) | 16 (z0.025: 3, z3.475: 8, z6.475: 5) | 16 (4 / 6 / 5 / z9.45: 1) |
| baseline Extend3D output | 0 | 0 |
| fix Extend3D output | 16, all 16 matched to the Clean3D openings | 16, all 16 matched |

So the baseline removed all 16 openings on both orders; the fix keeps all 16 at their original position and shape. F and R differ
from each other (10 of 16 match), which is Clean3D's face-order dependence, not this change.

### Where the 16 openings come from (trace to source)

- **Source panels** (`panels_extracted.json`, before snapping/Clean3D, 140 horizontal panels): 12 of the 13 openings with samples have 0 %
  of their samples on any source horizontal panel, one sliver 50 %; the 3 zero-area loops have no samples. They are gaps between source slab pieces that Clean3D's coplanar
  merge turns into openings (the loops exist in the Clean3D output; the rule that creates them was not isolated).
- **Reference gbXML** (`softhouse3.xml`): 10 openings (ground `SlabOnGrade`, first-floor `InteriorFloor`) are 100 % covered by a
  reference floor surface; the three large ones (6.30 m2 at (11.77, 0.10), 6.14 m2 at (5.95, 0.07), 8.63 m2 at (-4.40, 1.23), all
  z3.475) are 100 % covered by reference **`Air`** surfaces at z3.5 (virtual boundaries, which the pipeline drops, Clean3D takes
  non-Air panels); 3 are zero-area slivers (0.0000 to 0.0001 m2, no samples).
- Reading: on Softhouse the baseline's hole loss silently **filled the source slab gaps**, which matches the reference floors. It
  also closed the three stair-void-like openings that the reference closes with `Air`.

### Which openings break the build (bisection, fix build, OCCT stage on a filtered `panels_extended.json`)

`scripts/filterholes.ps1` keeps openings by area; `scripts/bisect.sh` runs the OCCT stage + Rooms2 + good classification.

| openings kept (F) | spaces | good / frozen 48 |
| --- | --- | --- |
| none (control = baseline) | 72 | 48 / 48 |
| 4 slivers < 0.01 m2 | 4 | 0 / 0 |
| 4 openings >= 1 m2 | 27 | 17 / 17 |
| 3 large (6.30, 6.14, 8.63 m2) | 27 | 17 / 17 |
| 8 openings 0.01 to 1 m2 | fails (128 / 76) | - |
| the 13 small ones (all but the 3 large) | fails | - |
| all 16 | fails (128 / 76) | - |

R: none 71 / 46; the two large ones (6.30, 6.14 m2) 29 spaces, 18 good; all 16 fails.

Changed rooms with the 3 large openings kept (F, `classify-F-voids3.txt`): 72 -> 27 cells; houses D (11 cells) and C (9 cells)
become single closed cells of 422 m3 and 428 m3 spanning ground and first floor; houses A and B leave their cell open
(`closed=False`, 104.8 and 129.7 naked); 31 of the frozen 48 rooms stop being GOOD. R: 71 -> 29 cells, 28 of the frozen 46 lost.
The merged D and C cells are what a real stair void gives when the Air boundary is absent, so they are geometrically consistent
with the openings but disagree with the reference, which separates the floors with `Air`.

**Verdict on Softhouse: not an improvement.** Keeping the openings is faithful to the Clean3D output and to the documented
contract of `GrowOutward`, but on this model that output contains source-slab gaps (not real openings) and Air-closed voids, so the
closure, connectivity and volumes of the baseline are lost. The baseline's result was right here for the wrong reason.

## Not established

- Whether any real model needs the fix: no case where the fix improves a room was found; the synthetic slab is the only
  positive evidence.
- Which Clean3D rule creates the 13 gap openings and whether Clean3D
  should close sub-threshold gaps; not investigated.
- Hosted CI, Grasshopper tests, GH redeploy: not done.

## Options for the owner

1. **Do not merge as is.** Keep the tests as the contract and wait for the Clean3D gap rule and Air-boundary support below.
2. Narrow alternative (not implemented): leave GrowOutward's behaviour as is and only report the loss (`RecordHoleDrop` on the
   adopted face), so the silent removal becomes a diagnostic. This changes no Softhouse result.
3. Fix together with (a) a Clean3D / Extend3D rule that closes source-slab gaps below a threshold, and (b) carrying Air panels
   (virtual boundaries) through to the cell build. Each is its own change and needs its own Softhouse measurement.

## Reproduce

```bash
git fetch origin sow/2026-Q4 && git switch fix/extend3d-grow-outward-holes-q4
dotnet test Testing/SAM.OCCT.UnitTests --filter "FullyQualifiedName~GrowOutwardHoleTests"   # redirect APPDATA first
```

Softhouse chains: `evidence/2026-11-18-grow-outward-holes/scripts/chains.sh` (`FIX=<sha>`), bisection `scripts/bisect.sh`, opening
listing `scripts/holes.ps1`, matching `scripts/matchholes.sh`, traces `scripts/tracehole.ps1` (reference) and `scripts/tracesrc.ps1`
(source). The PowerShell scripts were run with `-ExecutionPolicy Bypass` for the process only.

## Evidence (machine-local; copy before switching machines)

`C:\Users\michal\Documents\SAM_daily\2026-10-25-Softthouse\handover\evidence\2026-11-18-grow-outward-holes\`: `run.out`, `chains/{base,fix}-{F,R}`,
`holes/*.tsv`, `holes/matches.txt`, `suites/newly-failing-vs-q4.txt`, `bisect/`, `classify-*-voids3.txt`, `lost-rooms-*-voids3.txt`, `suites/`, `provenance/`.
Build root `SAM-BIM\_growholes_build` (pins: SAM `aa1cb777`, SAM_Solver `fd63b467`, native `438089F8`). Inputs and tools as in
`2026-11-16-pr84-q4-remeasure`.

## Next step

Owner reviews the verdict above and picks an option. Recommended: option 1 (do not merge), then a separate Clean3D gap-closing
task with its own Softhouse measurement. Suggested setup: Opus, high effort, new session, Plan mode first (the rule choice is a
design decision).

## Native integration: 28 new failures (2026-11-18)

Fix `5a94440` against the Q4 control `83143ca` (control = `2026-11-16-pr84-q4-remeasure/suites/q4-int3.trx`, 25 / 478 / 2, same SAM, SAM_Solver, native and
redirected profile seeded with `Documents/SAM/resources`). Compared by the set of failed test names (the `trxcmp.sh` join undercounts
parameterised cases: it showed 17 differences). Newly failing, by class: NineSpaceWallCapture 8, PR61TowersQuantitativeValidation 4,
EastSouthGapDiagnostic 3, TowersBucketLeverDiagnostic 3, GHParityMergeCoplanar 2, WorkflowParity 2, and one each in
ControlledWorkflowAcceptance (fixture nine spaces), EastSouthExtendDiagnostic, FullFixtureFixedConfig, North0ConsolidationFollow,
TowersCollapseZoneDiagnostic, TowersResidualContactClosure. Full list: `suites/newly-failing-vs-q4.txt`. The failure kinds (spaces missing, cell
counts, unreliable-build warnings) match the Softhouse cell-build failure; this is consistent with, but does not by itself prove, the same
cause (openings left in the extended caps). Not individually traced. Tests that pass on the control and fail here are regressions of the
fix on the current native fixtures; they were not run on a second build to rule out flakiness (the known flaky
`Extend_LevelCeilingVsPitchedRoof_RoutesScalarVsPlaneOps` is not among them).
