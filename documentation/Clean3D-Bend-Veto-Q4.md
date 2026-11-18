# Clean3D rule (b) bend veto, Q4

Record document for the local branch `fix/clean3d-bend-veto-q4`, created 2026-11-18 from `a28c8bc` (head of SAM-BIM/SAM_OCCT#84,
`fix/extend3d-no-invented-walls-q4`). **Not pushed, no PR.** It depends on #84: the intended PR base is `fix/extend3d-no-invented-walls-q4`
(stacked) or `sow/2026-Q4` once #84 has merged. That is the owner's decision.

## Current status
Implemented, tested locally, and independently reviewed locally (ACCEPT WITH CHANGES; the changes are made). Stopped before push/PR, as instructed.
- Tested content: `5a4d63d` (implementation `fb52032` + review follow-ups: tests and comments only).
- The full harness ran on both SHAs.
- Nothing changes for users until the Grasshopper deploy is rebuilt (not redeployed).

## What it does
Clean3D's weighted snap (`Panel3DSnapSolver.Snap`) rule (b) aligns consecutive wall pieces offset by up to 0.3 m. On a genuine bend (the Softhouse A/B 2.15 degree
and B/C 3.64 degree party-wall joints), the heavier angled piece is the backer. Rule (b), using the 5 degree fully-captured allowance, turns the straight
continuation onto it, which leaves a 3-116 mm step to the next straight piece, so the houses leak into one cell.

The veto: a pair that rule (b) **alone** admits (`abut && !overlap`) is left unmoved when **both** of these hold:
- The move turns the candidate by more than `toleranceArcAngle`, tested as `!candidate.IsParallelWith(backer, toleranceArcAngle)`. A translation is never refused.
- A run-end boundary segment of the candidate (`SnappedPanel.RunEndSegments`, the solver's model-up wall frame) has a finite contact with a third panel that is
  near-parallel to it within `toleranceAngle`. Projecting the contact onto the move's target plane would take it off that panel's plane by more than
  `toleranceDistance`.

The target is the plane the existing move gives the candidate: `backer.Plane`, or for an equal-weight pair `SnappedPanel.MidplaneWith`. That is the plane
`MoveToMidplaneWith` would move the backer to, including its "within tolerance, stay" case.

Contact (`BendVeto.Contact`) is an exact clip of the end segment by the third face, restricted to the stretch within `toleranceDistance` of its plane:
- The segment is broken at each outline crossing and at each outline vertex lying on it.
- Each piece is classified with `Face2D.On || Face2D.Inside` (tolerant), so holes, notches and disconnected stretches are respected.
- Pieces are merged and anything no longer than the tolerance is dropped (`PlanIntervals.Normalise`): a corner touch is no contact.

Rule (a), the overlap and void guards, `ContactVoidGuard`, public APIs and defaults are unchanged. There is no new constant (two `1e-12` literals guard divisions
only), no carry, no connected-run planner, no environment switch and no fixture identifier.

### Decisions and assumptions
- **Variant 3 of the prototype** (`2026-11-19-bend-veto`): only near-parallel third panels count. A perpendicular return slides along itself and is not counted.
  Variant 1 (any panel) changed nine-space no-capture from 11 to 9 cells.
- **Polygon overlay replaced.** A first implementation used SAM's NTS `Face2D ∩ Segment2D`. It lost every contact on the 10 degree tilted level: NTS rounds 2D
  coordinates to the 1 µm grid, so an edge-on-edge contact that is collinear in 3D is not collinear after rounding once it is not axis-aligned in the plane frame.
  It was replaced by the breakpoint clip above (geometry fix, no tolerance change).
- Only the candidate is tested. In an equal-weight pair the backer is only translated along its own normal, which the veto never refuses.
- The diagnostic `reason` is computed but not recorded. Like the #89 S1 precedent, a refused move leaves no `CleanRecord` or diagnostic: **open owner
  decision** under DIAGNOSTICS_STANDARD.

## Files changed (`a28c8bc..5a4d63d`)
- `SAM_OCCT/SAM.Geometry.OCCT.Solver/Classes/BendVeto.cs` (new, internal): `Refuses`, `Contact`.
- `SAM_OCCT/SAM.Geometry.OCCT.Solver/Classes/SnappedPanel.cs`: internal `MidplaneWith`, `RunEndSegments`.
- `SAM_OCCT/SAM.Geometry.OCCT.Solver/Classes/Panel3DSnapSolver.cs`: the gate in `Snap` and a doc paragraph.
- Unit tests (new):
  - `Testing/SAM.OCCT.UnitTests/WallFixtures.cs`: helpers adapted from the 54efe89 comparison copy, plus shaped outlines and the model-up tilt.
  - `BendVetoSnapTests.cs`: public API only, so it also runs on #84.
  - `BendVetoContactTests.cs`
  - `Clean3DBendVetoTests.cs`
- Native test (new): `Testing/SAM.OCCT.IntegrationTests/BendVetoTwoRoomIntegrationTests.cs`.
- This record.

## Tests and results (local, machine `michal`; no GitHub checks run)
Harness: as `2026-11-19-midplane-candidate`.
- `git archive <SHA>` into a clean `_kink_build/SAM_OCCT_bvfinal[2]`.
- `safebuild.sh` with redirected seeded profiles; SAM `aa1cb777` and SAM_Solver `fd63b467` build pins; frozen native `438089F8` / TKBO `1FA732FE`.
- All pins checked OK (`provenance/pins-check.txt`). One native process at a time.

Reproduce: `sh <E>/scripts/run-final.sh <SHA>` (or `run-final-r2.sh`).

`<E>` = `SAM_daily/2026-10-25-Softthouse/handover/evidence/2026-11-19-bend-veto-impl` (machine-local; `r2/` = `5a4d63d`).

### Positive tests, before (#84 `a28c8bc` + the public-API tests only) / after
| Test | #84 | veto |
| --- | --- | --- |
| Snap fixtures (block, first-floor over the joint, 80 mm partial-height contact, finite notch) x winding / loop start / order x level and 10 degree tilt (32) | 32 fail | pass |
| Snap fixtures that must stay free to move (corner-only, notch corner) (16) | pass | pass |
| Equal-weight bend pair (midplane move refused) | fail | pass |
| Equal-weight pair whose held piece is the backer (still moved) | pass | pass |
| Clean3D oracle, block and first-floor x 4 axes (8) | 8 fail | pass |
| Clean3D above-arc "slanted" control x 4 axes (#84 leaves a 10 mm step) | 4 fail | pass |
| Clean3D noisy straight wall (skew) and jog controls x 4 axes (8) | pass | pass |
| Parallel jog with a continuation; rule (a) 1 degree double wall | pass | pass |
| Contact / RunEndSegments / MidplaneWith unit tests (12) | n/a (internal API) | pass |
| Native two rooms, bent party wall | **1 room, 360 closed** | **2 rooms, 178.592 + 181.408, closed** |

- Run on #84: `_kink_build/SAM_OCCT_bv84` (a28c8bc + `WallFixtures`, `BendVetoSnapTests`, `Clean3DBendVetoTests`, the native test). Unit: 45 failed / 27 passed of 72.
- The native fixture's volumes are calculated independently: west = (6 x 10 - 0.5 x 5^2 x tan 2.15 deg) x 3.
- The native fixture also asserts membership: each probe point lies in exactly one room, and the two rooms are different.

### Suites at the tested SHAs vs #84 (`2026-11-16-pr84-q4-remeasure/suites`, compared by test name)
- **Managed unit:** 1411/1411 at `fb52032` and 1416/1416 at `5a4d63d`. All 1332 #84 tests are unchanged; the rest are new.
- Softhouse `good.tsv` / `v2_spaces.tsv` are byte-identical between the two SHAs.
- **Grasshopper:** 43/43, identical to `pr84-gh.trx`. The towers sew-guard pins pass: 24 shells at bucketBetweenLevels 0, 27 at 0.21.
- **Native:** 24 failed / 488 passed / 2 not executed. All 513 #84 tests have the same outcome and failure message as `pr84-int3.trx`; the one new test passes.
- **Nine-space / North probes** (separate probe copy, SHA + diagnostic `RuleBProbeTests` only): byte-identical to #84 (`2026-11-19-bend-veto/probes/off-*`).
  - Capture 0.05: North1|North2 adjacent, 9 cells.
  - No capture: 11 cells.

### Softhouse, both face orders (`chains/bv-F`, `chains/bv-R`)
| | #84 | veto, F | veto, R |
| --- | --- | --- | --- |
| Ground floor A / B / C | one open cell (42 naked) | 207.295 / 207.519 / 208.048, separate, closed | same |
| House D ground floor | 216.913 F / 217.02 R | 216.913 | 217.02 |
| A102 | GOOD | GOOD | GOOD |
| Frozen GOOD | 47 F / 45 R | 47 | 45 |

- Gates: F `G1 47/47 G2 48/48 G3a 18/18`; R `G1 45/47 G2 46/48 G3a 16/18`.
- Frozen rooms by identity (status, closure, volume) are identical to #84 except one: Dining 4 A107 is still "not" (it shares the house cell by design), and its
  cell is now closed at 207.295 instead of open.
- Non-frozen gains: Bed 13 2OG A304 and Bed 11 2OG B304.
- These outcomes equal the sampled prototype v3 (`2026-11-19-bend-veto/veto13-softhouse.txt`), so exact contact changed no outcome and no trace was needed.

Other checks:
- `%APPDATA%\SAM` is unchanged.
- The #84, midplane-carry and kink-wallframe worktrees, the scratch trees `SAM_OCCT_vt` / `cand54` / `a28p`, and the branch list are all unchanged (`provenance/`).

## Independent review (local, 2026-11-18)
A fresh read-only reviewer was given `a28c8bc..fb52032` and the specification, not this record. Verdict: **ACCEPT WITH CHANGES, no blockers.**

Verified:
- The gate scope: rule (a), translations and the void guard are untouched.
- The frame matches `RunHeightBounds` for null and tilted up.
- The clip maths and the hole handling.
- The exactness of the endpoint displacement test.
- The non-vacuity of the fixtures.

Findings:
- **Should-fix, done in `5a4d63d`:**
  - The skew control never reached the veto (its turns are below 0.3 degrees). Added the above-arc "slanted" control: kept continuous, order-independent.
  - Equal-weight swapped roles: added a test that the backer is translated and the angled piece turned.
  - A recessed end is not a run end: documented, see the limits below.
- **Nits:**
  - N4: `MidplaneWith` differs from `MoveToMidplaneWith` only if the backer's projected face is invalid. Documented.
  - N5: contact pieces no longer than 1e-6 are dropped per segment before merging. Negligible.
  - N6: the 1e-6 contact band.
  - N7: no trace of a refused move (open decision above).
  - N8: performance is low risk (a refused pair is rechecked each fixed-point pass against all panels; the timing was not measured separately).
  - N9: `1e-12` is a numerical guard.

## Remaining limits, risks
- **#84 limitations, unchanged:** the deliberate 0.5 degree splay with a perpendicular return is still rotated onto the wall. Its geometry is identical to a
  17.5 mm noise piece into a return, so keeping it needs source evidence of intent. The 0.2 m void between facing walls is collapsed by rule (a) and the opposed pass.
- **Above-arc noise held at both ends** by straight pieces (above 0.3 degrees) is kept as authored (continuous, slanted) instead of straightened. This is a
  deliberate trade-off, pinned by the "slanted" control.
- **Run ends are the segments at the extreme run coordinate.** A continuation meeting a recessed upper end of a stepped (L-shaped) wall is not checked.
- **The contact band is `toleranceDistance` (1e-6 m).** A continuation 0.5 mm off the end edge is not a contact. This follows the specification but is a
  robustness risk on noisy imports.
- **Only Softhouse has a real bend.** The native suite has no other veto case. It exercises the keep side; the prototype trace found 0 vetoes there. The
  product build has no trace switch, and the identical native outcomes are the evidence.
- Machine-local evidence and prerequisites: the `<E>` folder, `_kink_build` (SAM and SAM_Solver builds, `native-frozen`, profiles), and the tools folder
  `tools-2026-10-27-combined`. Copy them before switching machines.

## Recommended next step
Owner review of this record and the branch. If approved, push `fix/clean3d-bend-veto-q4` to `SAM-BIM/SAM_OCCT` and open a draft PR. The base is the owner's
choice:
- `fix/extend3d-no-invented-walls-q4`, stacked on #84; or
- `sow/2026-Q4` after #84 merges, rebased onto the merge.

Then run the hosted CI at the final head and check the codex bot comments before any merge.

Recommended handling: an existing session, Opus at high effort, Build mode. The work is mechanical push/PR plus CI follow-up, with the full context already here.

Generated by Michal Dengusiak and CodeClaude
