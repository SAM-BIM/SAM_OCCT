# Sew guard against invalid cells (Scope 1) - branch record

Branch: `fix/sew-guard-invalid-cells-q4`, pushed to `SAM-BIM/SAM_OCCT`; PR
[SAM-BIM/SAM_OCCT#81](https://github.com/SAM-BIM/SAM_OCCT/pull/81) into `sow/2026-Q4` is open and ready for review
(not a draft); not merged. Base: `sow/2026-Q4` at `dc871f7e6ee7d0e53adb826a2b597645e2eab4cb` (unchanged when the
hosted checks ran on 2026-10-23). SAM and SAM_Solver are not changed. Hosted checks passed on PR head
`0718dbcf58cc196f1bd71b30a3ca689b8af3e7ea` (section "Hosted CI"). The PR head after this record commit is stated in
the PR description, not here (a record cannot name its own SHA).
Commits: `63be627` (guard), `ac212bd` (replacement judged on its own merits, after the real-fixture validation),
`599ed75` (record), then the R1/R2 corrections from the independent review: `45e307f` (always check sew-cell
disjointness; establish disjointness soundly) and `8d4beff` (unit test pinning the separating-face-plane path),
`e7bb5e2` (record), then the R3 correction from the second review: `cb1a719` (one glue-band policy for every
overlap test, with tests), `1726f0d` (record), then the R4 correction from the third review: `63067b5`, `0498a24`,
`a135c5f` (glue-band proof replaces the coarse kernel pass).
Tested production content SHA: `a135c5f4a6e25dd1c40a11a4881219c1271bc4a3` (see "R4 correction"; local native and
managed evidence). Commits after it, none of which changes production code, tests or workflows:
`3523ed264438f9fc7f1dd7a9eb5ee90c71ca8ee2` (record and `TESTING.md`; it carried a CI-skip marker, so it had no
checks), `0718dbcf58cc196f1bd71b30a3ca689b8af3e7ea` (empty commit, tree identical to `3523ed2`, raised the hosted
checks) and the record-only commit that updates this file with the hosted results. The earlier tested content SHAs
`ac212bd`, `8d4beff` and `cb1a719` are superseded; their results are kept below where marked.
Source of the task: Scope 1 of `.review-occt-integration19-20261022/ATTRIBUTION.md` (machine-local, owner workspace).
Independent reviews: `SAM-BIM/.review-sew-guard-independent-20261022/REVIEW.md` (findings R1, R2), `REVIEW-R2.md`
(finding R3) and `REVIEW-R3.md` (finding R4), machine-local. An independent review of the CI-trigger commit
(2026-10-23, identical-tree check; not a new geometry review) accepted it and found no new code defect, and asked for
this record and the PR description to be corrected. `PROJECT_PROGRESS.md` is not touched (post-merge closeout only).

## Status

Implementation, tests and local validation are complete, including the corrections for review findings R1, R2
(section "R1/R2 corrections"), R3 ("R3 correction") and R4 ("R4 correction"). The branch is pushed and PR #81 is
open and ready for review. Hosted Build (Windows) and SPDX passed on PR head `0718dbc` (section "Hosted CI"); the
hosted run exercises managed code only, so the native guard behaviour still rests on the machine-local evidence
below. No merge is authorised: the PR awaits a final review and the owner decisions listed under "Next step". No
owner acceptance of any of those decisions is recorded.

## Hosted CI (PR #81, 2026-10-23)

Both runs were triggered by the `pull_request` `synchronize` event of `0718dbc` (an empty commit whose parent is
`3523ed2` and whose tree `2cd205d901a01ce06efbecaa3872fd216a8c4f2f` is identical to it). `3523ed2` itself had no runs:
its message carried a CI-skip marker and `pull_request` triggers do not include `ready_for_review`.

| Check | Result | Run |
|---|---|---|
| Build (Windows) | success | https://github.com/SAM-BIM/SAM_OCCT/actions/runs/39920956168 |
| SPDX + Copyright header check | success | https://github.com/SAM-BIM/SAM_OCCT/actions/runs/39920956200 |

- **PR head versus merge ref.** PR head `0718dbcf58cc196f1bd71b30a3ca689b8af3e7ea`. The runner checked out the PR
  merge ref `c8ff83b962676f7f00cc48b9a06bd3865497acc8` (parents `dc871f7` and `0718dbc`, tree `2cd205d9...`, the same
  as the PR head's). The PR head is not what was literally checked out; the merge ref is.
- **Unit tests:** 975 passed, 0 failed, 0 skipped, including 275 managed guard and proof cases
  (`OcctGlueBandProofTests`, `SewInvalidCellGuardTests`).
- **Integration tests:** 31 passed, 0 failed, 422 skipped. Every skip reports "Native SAM.Occt.Native library is not
  available." (`SAM_OCCT_SKIP_NATIVE_BUILD=true`; the runner has no native layer), including all 171
  `SewGuardInvalidCellIntegrationTests` cases. None of the guard's native integration tests ran on GitHub.
- **Build and provenance:** SAM, SAM_Solver and SAM_OCCT rebuilt with `msbuild /t:Rebuild` in Release; the H12
  provenance assertion passed for the 8 managed assemblies at `2026.4.296.0+c8ff83b`.
- **Dependency refs.** The workflow clones SAM and SAM_Solver at the newest `sow/yyyy-Qn` branch; the log shows
  `Cloning SAM @ sow/2026-Q4` and `Cloning SAM_Solver @ sow/2026-Q4`, with no fallback warning, but not their commit
  SHAs. **Inferred, not proven by those logs:** the `sow/2026-Q4` tips at the clone time (2026-10-23 07:54:57Z), read
  afterwards from the GitHub API, were SAM `0168d03f7e9e56775868aecaca73d8224a2034c5` and SAM_Solver
  `fd63b4670cd4baeb20a74312aacc17eb177ecc18`. The local validation used SAM `ba686d24` (an unstamped local build)
  and the same SAM_Solver `fd63b467`, so the hosted SAM differs from the local one.
- **Not covered by the hosted run:** the native kernel (fixtures, rigid-motion invariance, fail-closed integration
  test, the 19 historical native failures below), Grasshopper, and the full solution. The 19 full-native failures
  were recorded as identical by name and message to the base `dc871f7` and earlier saved runs (local, see "Test
  suites"); the guard neither fixes nor adds any, and fixing them is outside this PR.
- Hosted results for the final PR head (after the record-only commit that adds this section) are reported in the PR
  description; the content is identical to `0718dbc` apart from this file.

## R4 correction (2026-10-22)

**R4 (P1): the coarse kernel pass accepted an overlap beyond its own band rule.** When the fine kernel pass
(fuzzy b/10) left a common piece unclassified, `CommonVolume` asked the kernel again at fuzzy = b and took "no
common solid" as a seam. Third review reproduction (machine-local `.review-sew-guard-independent-20261022/
REVIEW-R3.md`, `r3-probe/`): unit cube against the prism over (0.998, 1), (1, 0.998), (2, 2), z 0.998..2. Their
common region is a right isosceles prism, legs 2 mm, 2 mm high (4e-9 m3), whose incentre (1 - r, 1 - r, 0.999),
r = 0.002 / (2 + sqrt 2) = 0.5858 mm, lies 0.5858 mm inside both cells: a ball of diameter 1.17 mm > b = 1 mm. The
fine pass found the piece (undecided), the coarse pass glued it away: `CommonVolume` 0 and the full
`AdoptSewCandidate` two cells with `SEW_SUCCESS`. The 1.75 mm and 1.8 mm corner prisms were accepted the same way.

**Fix: the coarse pass is removed; undecided pieces are decided by a proof on the two cells**
(`SAM_OCCT/SAM.Geometry.OCCT/Native/OcctGlueBandProof.cs`, new; called from `CommonVolume`). The policy is
unchanged: overlap iff the common region holds a ball of diameter > b. For x inside both cells the largest ball
centred at x inside the common region has radius d(x), the distance from x to the union of the two boundaries.
Branch and bound over boxes covering the undecided piece's region (its extent in a frame taken from its own
faces, enlarged by the fine fuzzy value plus Tolerance, since the decoded piece may differ from the exact region by
that much):
- Upper bound (sound): d(x) is at most the distance from x to any convex subset of the boundary. The subsets are
  convex faces, triangles of non-convex faces (SAM's triangulation, each triangle shrunk 1e-9 m and kept only if
  its centroid lies inside the face and no face edge meets it, so a faulty triangulation can only weaken the bound)
  and face edges. Each distance is convex in x, so its maximum over a box is at a corner; the smallest such corner
  maximum, and d(centre) + half the diagonal, bound d on the box. A box bounded by b/2 holds no centre of a ball
  wider than b.
- Empty box (sound): a box that, enlarged by b/8, meets no face of one cell (exact box-section-versus-polygon test,
  or centre farther than half the diagonal + b/8) lies inside or outside that cell as its centre does; outside one
  cell it holds no common point. The inside test is ray parity that discards rays passing within b/8 of a face edge;
  cells must be closed at b/8.
- Witness: a box centre inside both cells with d > b/2 proves the overlap.
- Answers: overlap (piece volume counted), seam (every box bounded), or undecided (budget of 20,000 boxes spent, a
  cell not closed at b/8, unusable geometry). Undecided keeps the pair failing closed, as before. The threshold b/2
  carries a relative 1e-9 allowance (5e-13 m) so that a slab exactly b thick is a seam despite rounding; the same
  threshold is used for both answers.
- How boxes are cut (at planes of faces perpendicular to a frame axis and b/2 either side of them; otherwise where
  the proof's own rules then resolve a half; otherwise across the faces crossing the box or along the longest side)
  affects only the work, never the answer. The rejected alternatives and why: more sample points (misses are not
  proof), mean thickness 2V/S, and kernel disappearance at a coarser fuzzy value (the R4 defect).

**Behaviour change, by the documented criterion (not a widening):** an L-shaped seam of width t holds a ball of
diameter 2 sqrt 2 t / (1 + sqrt 2) = 1.1716 t at its reflex corner, so it is an overlap once t > 0.8536 mm. The R3
code accepted L seams up to 1 mm through the kernel's gluing; L seams of 0.86 to 1 mm are now overlaps (tested). A
seam is accepted only through the proof (or the unchanged piece tests); a pair the R3 code left undecided (both
kernel passes keeping an unclassified piece) can now be proven a seam and accepted.

Commits: `63067b5` (proof replaces the coarse pass), `0498a24` (robust to faces off the frame axes: verified
triangles of non-convex faces, lookahead steering; optional proof budget parameter on `CommonVolume` as a test seam),
`a135c5f` (redundant pair bound removed after mutation testing). No native, ABI, SAM, solver, Grasshopper, golden,
closure-tolerance or fixture change. Files: `OcctGlueBandProof.cs` (new), `OcctCellComplexBuilder.cs`
(`CommonVolume`), `OcctUnderSplitGuard.cs` (doc only), `Testing/SAM.OCCT.UnitTests/OcctGlueBandProofTests.cs` (new),
`SewInvalidCellGuardTests.cs` (comment), `SewGuardInvalidCellIntegrationTests.cs` (+65 rows), `TESTING.md`.

**Tested content SHA `a135c5f4a6e25dd1c40a11a4881219c1271bc4a3`** (`1726f0d..a135c5f`). Local only, same machine,
same frozen native (SAM.Occt.Native.dll 438089F8..., TKBO.dll 1FA732FE..., verified) and managed dependencies as in
"Validation"; `SAM_OCCT_SKIP_NATIVE_BUILD=true`. Raw evidence, machine-local:
`SAM-BIM/.review-sew-guard-q4-20261022/r4-corrections/` (new folder; nothing earlier modified).

| Check | Reviewed `1726f0d` (content `cb1a719`) | Corrected `a135c5f` |
|---|---|---|
| Review reproduction, 2 mm corner prism | `CommonVolume` 0, full guard True, Success True, 2 cells, `SEW_SUCCESS` | overlap 4.0e-9 m3; full guard False, Success False, 0 cells, `SEW_BUILD_INVALID` + `BUILD_REJECTED` |
| Same, 1.75 / 1.8 mm prisms | `CommonVolume` 0 (accepted) | overlaps 3.06e-9 / 3.24e-9 m3 |
| Unit suite | 903 / 0 | **975 / 0** (+72 `OcctGlueBandProofTests`) |
| Native, focused (`SewGuardInvalidCell`, `HouseUnderSplit`, `HouseAirAndAttic`) | 122 / 0 / 0 | **187 / 0 / 0** (+65) |
| Native integration, full | 367 / 19 / 2 | **432 / 19 / 2** (6 min 56 s) |
| Real fixed panel sets, guarded builds (6 sets x 2 modes x 8 offsets) | - | **96 / 96 identical outcomes**, codes included; no `Success=False` |

- Builds (`--no-incremental`): 0 errors; 243 (unit) / 284 (integration) warnings, the same pre-existing counts; none
  in the changed files. Logs: `suites/build-*-a135c5f.log`.
- Full native comparison by identity (`Compare-Trx.ps1` against `r3-corrections/suites/native-full-r3.trx`;
  `suites/compare-native-full-r3-vs-a135c5f.txt`): the same 19 failures by name and message, none new, none
  resolved; the same 2 skips (`LargePanelSewGuard...SkipsNativeSew`, `NativeMissing...ReportsNativeMissing`). They
  are the solver golden and parity tests explained by SAM 173ababc (unchanged since base).
- Review reproduction: the reviewer's `r3-probe` needs one change for the new optional `CommonVolume` argument
  (reflection does not fill optional parameters); port in `reviewer-probe-port/` (diff: those two calls only), run with
  the frozen native copied next to it. Outputs `probe-runs/reviewer-probe-cb1a719.txt` (reproduces R4 exactly) and
  `reviewer-probe-a135c5f.txt`. `reviewer-probe-a135c5f-NO-NATIVE-invalid.txt` is a first run without the native
  runtime (every exact test unavailable), kept and labelled, not evidence.
- Proof decisions against the analytic thresholds (scratch probe, four poses; the committed tests pin a subset):
  corner prisms 1.705 mm seam / 1.71 mm overlap, L seams 0.853 mm seam / 0.855 mm overlap, in every pose, 50 to
  130 boxes, under 60 ms. L seams with the notch box turned 1e-6 to 3e-4 rad: 170 to 3,573 boxes, under 120 ms;
  beside non-convex floor/ceiling faces up to 5,808 boxes, 130 ms. Saved scans: `r4-corrections/proof-scans/`.
- Real panel sets (`run-probes.sh`, probe v5 unchanged; `Summarize-Probes.ps1 -Head a135c5f`,
  `probe-runs/summary-1726f0d-vs-a135c5f.txt`): every guarded build identical to `1726f0d`. Median build time
  deltas are within run-to-run noise (largest: PR49-Test2 GH path 680 -> 746.5 ms; g-1 GH path 546 -> 510 ms).
  **No real-model outcome changed, so there are no before/after `.sam` exports.**
- Pair level: probe v5 calls `CommonVolume` by reflection with three arguments, which throws on the new head and was
  reported as undecided, so its R4-head pair files are invalid and renamed `*-INVALID-reflection-arity.txt`. Probe
  v5.1 (`harness/Probe-v5.1-*`, only that call changed) on both heads (`run-pairs-v51.sh`): identical cells, exact
  calls and failure lists on all six sets. Exact-test time, summed over 8 offsets (`exact-time-*.txt`): no piece was
  undecided on these sets (next item), so both heads execute the same path (neither the coarse pass nor the proof
  runs) and the differences are run-to-run noise: AdjacencyCluster-home direct 1242 -> 1274 ms; Face3D-home sew
  910 -> 1073 ms (+18 %), direct 992 -> 1071 ms; cur-1 sew 848 -> 902 ms, direct 170 -> 142 ms.
- **The proof never ran on the real sets** (probe v5.2, `harness/Probe-v5.2-*`, `proof-stats-*`): of the exact-test
  pairs, cur-1 had 5 fine-pass pieces, all proven overlaps by the unchanged piece tests; every other call had no
  common solid (status 40, so 0) or a kernel failure (AdjacencyCluster-home, 20 calls, undecided before the proof as
  at R3). The proof is exercised by synthetic geometry only.
- Mutation checks (`run-mutations.sh`, `mutations.txt`; each defect injected by script, target suite rebuilt and
  run, source restored with `git checkout` and verified clean):

| Injected defect | Target suite | Failed tests |
|---|---|---|
| R4-M1 coarse kernel disappearance proves a seam (the R4 defect) | `SewGuardInvalidCellIntegrationTests` (171) | 39 |
| R4-M2 undecided piece counted as seam in `CommonVolume` | same | 1 |
| R4-M3 witness never reported | `OcctGlueBandProofTests` + `SewInvalidCellGuardTests` (275) | 38 |
| R4-M5 empty box judged by its centre only (no clearance) | same | 33 |
| R4-M6 spent budget reported as seam | same | 1 |
| R4-M7 closure precondition removed | same | 1 |
| R4-M8 witness without the inside test | same | 23 |
| R4-M9 non-convex faces bounded by edges only | `OcctGlueBandProofTests` (72) | 2 |
| R4-M10 triangles used without verification | same | **0** (survives: SAM's triangulation is correct on every test face; the check guards against a faulty one) |
| R4-M11 cut steering without lookahead | same | 8 (budget spent: steering affects work, not answers) |

  Earlier runs with script defects are kept and labelled (`mutations-63067b5-first-run.txt`,
  `mutations-0498a24-script-defects.txt`); the first also showed the pair bound (since removed) to be redundant.

## R3 correction (2026-10-22)

**R3 (P2): the glue-band allowance changed under a rigid rotation.** Boxes x 0..1 and 0.9995..2 (unit depth and
height, Tolerance 1e-6, FuzzyTolerance 1e-3: a 0.5 mm seam, band 1 mm) were kept unrotated (`Success=True`, two
cells, `SEW_SUCCESS`) but failed closed rotated 45 degrees about Z (`SEW_BUILD_INVALID` + `BUILD_REJECTED`). The
axis-aligned box test allowed a band-thick overlap; the rotated pair skipped it, and the centre of the bounding-box
overlap, inside both cells but only 0.25 mm from each boundary, was taken as proof of overlap. The face-plane and
native tests used still other band rules.

**One policy, used by every test** (`OcctUnderSplitGuard.CheckDisjoint`, documented there): two cells overlap when
their common region holds a ball of diameter greater than the band b = max(Tolerance, FuzzyTolerance), i.e. some
point lies deeper than b/2 inside both; otherwise they touch. This is invariant under rigid motions.
- Point samples prove an overlap only at depth > b/2 in both cells (`IsDeepInside`: inside, and farther than b/2
  from every face). A shallower point inside both decides nothing.
- Touching is proven by a direction along which the projections of the two cells overlap by at most b: the axes
  (bounding boxes, unchanged) and now the face normals of either cell measured the same way
  (`ThinAlongFaceNormal`, replacing the one-sided face-plane rule, which allowed up to 2b for non-convex cells).
- The native exact test classifies each common piece by the same rule (`OcctUnderSplitGuard.ExceedsBand`): a
  piece in a band-thin slab (axis or own face normal) is a seam; a piece with a candidate point (decoded centre,
  internal point, vertex centroid) deeper than b/2 is an overlap; otherwise it is undecided. Mean thickness 2V/S
  is no longer used: a seam attached to a compact lump can have a small mean thickness.
- Undecided pairs still fail closed; nothing is accepted for want of a point hit.

**Native exact test (`OcctCellComplexBuilder.CommonVolume`).** Measuring the kernel while fixing R3 showed two
further inconsistencies in the old exact test, both able to accept a material overlap in some poses only:
- the decoder drops solids whose volume is at most the `tolerance` argument (Tolerance, 1e-6, applied to m3), so a
  compact 9 mm corner overlap (7.3e-7 m3) was reported as no common volume;
- at fuzzy = b the kernel glues thin seams reliably (slab and L seams up to b vanish in every pose; 1.2 mm stay),
  but it also glues away compact pieces up to about 3b, depending on orientation (a 2 mm corner cube vanished in
  every pose, a 3 mm one in the oblique pose).

It now runs the kernel first at fuzzy max(Tolerance, b/10) with a decode threshold of min(Tolerance, 1e-3 b^3),
far below the volume of a band-wide ball, and classifies the pieces. Only when a piece is undecided (in practice
an L-shaped seam thicker than b/10) does it ask the kernel at fuzzy = b: no common solid there means a seam;
otherwise the pieces are classified again and an undecided one makes the pair undecided (fail closed). Kernel
scan with this decode threshold, two poses (axis-aligned and oblique; machine-local
`r3-corrections/probe-runs/kernel-scan-decode-1e-12.txt`): at b/10 = 0.1 mm the kernel keeps every compact
corner piece of 0.5 mm or more (0.3 mm only axis-aligned) and every slab or L seam above 0.1 mm, and glues seams
of 0.1 mm or less; at b = 1 mm it glues slab and L seams of up to 1 mm in both poses and keeps 1.2 mm ones, but
loses compact cubes of up to 2 mm (3 mm oblique). No native or ABI change: only the fuzzy and decode values
passed to the existing `sam_occt_shells_intersection` differ.

**Test-pose note.** SAM's axis-angle `Transform3D.GetRotation(axis, angle)` is not orthonormal (two entries carry
the wrong sign; it scales volume by 0.728 for the pose first tried here). The tests compose the oblique pose from
the elementary `GetRotationZ/X/Y`, and `Moved_EveryPose_IsRigid` checks every pose. SAM is not changed; this is
reported for a separate SAM fix.

**Tested content SHA `cb1a71952909afec3ddc670dd61982806b8dd5df`** (`e7bb5e2..cb1a719`: the two guard sources and
the two sew-guard test files). Results, local only (same machine, frozen native and managed dependencies as in
"Validation"):

| Check | Reviewed `e7bb5e2` (= `8d4beff` content) | Corrected `cb1a719` |
|---|---|---|
| Review reproduction, 0 degrees | Returned True, Success True, 2 cells, `SEW_SUCCESS` | same |
| Review reproduction, 45 degrees | Returned False, Success False, 0 cells, `SEW_BUILD_INVALID`, `BUILD_REJECTED` ("cells 0 and 1 overlap") | Returned True, Success True, 2 cells, `SEW_SUCCESS`, no failures |
| Unit suite | 758 / 0 | **903 / 0** (+145 rows: R3 pair, builder, piece and pose tests) |
| Native, focused (`SewGuardInvalidCell`, `HouseUnderSplit`, `HouseAirAndAttic`) | 58 / 0 / 0 | **122 / 0 / 0** (+64 kernel rows) |
| Native integration, full | 303 / 19 / 2 (at `45e307f`) | **367 / 19 / 2** (10 min 35 s; +64) |

- Full native comparison by identity (`r1r2-corrections/Compare-Failures.ps1` against the recorded base list): the
  same 19 failures by name and message as base, `ac212bd` and `45e307f`; none new, none resolved; the same 2 skips.
  The R1/R2 regressions all pass.
- Reproduction: the review's `reproduce-rotation.ps1` needs PowerShell 7 (not installed here); a faithful C# port
  (`r3-corrections/repro-rotation-src/`) reproduces the reviewed failure exactly on a `git archive e7bb5e2` export
  (`SAM-BIM/SAM_OCCT-reviewed-e7bb5e2`) and passes on `cb1a719`. Outputs: `r3-corrections/repro-rotation-*.txt`.
- Translation probe (v5, same eight offsets, six panel sets, shells and GH path, interleaved `e7bb5e2` /
  `cb1a719`, same frozen native): 95 of 96 guarded builds identical, codes included. The one difference is
  current Revit-home, GH path, offset (100,-50,0): `e7bb5e2` called the sew build invalid ("sew cells 3 and 5
  overlap") and adopted the direct build (22 cells, exact match); `cb1a719` finds those cells valid and keeps the
  sew build as a volume-preserving under-split with `SEW_UNDER_SPLIT_SUSPECTED` + `BUILD_UNRELIABLE` (20 cells,
  215.993 m3). Diagnosis (`probe-runs/diag-cb1a719-cur-1-gh-100_-50_0.txt`): the overlap "proof" was a direct
  cell's internal point lying 3.4e-9 m from both cells' boundaries, on their shared face: the R3 defect itself.
  The earlier match was therefore reached through a false rejection. Count against the direct decomposition: 76/80
  (was 77/80); still no non-positive cell, no failed build, every residual flagged `BUILD_UNRELIABLE`.
- Pair-level probe (`pairs` mode, non-Air faces): identical cells, exact-test calls and failure lists on five sets.
  On AdjacencyCluster-home at (100,-50,0) an already invalid sew build (proven overlaps 2/8, 3/8, 4/8) gets a
  different undecided pair (2/4 instead of 5/6); its outcome is unchanged. Direct cells were never undecided.
- Exact-test cost (same calls, kernel time only, sum over 8 offsets): AdjacencyCluster-home direct 1047 -> 2230 ms
  (48 calls), sew 665 -> 850 ms; Face3D-home direct 776 -> 1720 ms, sew 898 -> 1187 ms; other sets unchanged
  within noise. The extra time is the kernel-at-band pass for pieces that stay undecided at b/10. Build medians
  are noisy on this run except the AdjacencyCluster-home GH path: +350 to +725 ms per build at 7 of 8 offsets
  (median 1487 -> 2104 ms). Raw: `r3-corrections/probe-runs/`.
- Build at `cb1a719` (clean, `--no-incremental`): 0 errors, the same 243 (unit) / 284 (integration) pre-existing
  warnings; none in the changed files.
- Mutation checks (`r3-corrections/run-mutations.sh`; each defect injected by script, target suite rebuilt and
  run, source restored with `git checkout` and verified clean; `r3-corrections/mutations.txt`):

| Injected defect | Target suite | Failed tests |
|---|---|---|
| M1 any point inside both cells proves overlap (pre-R3) | `SewInvalidCellGuardTests` (203) | 15 |
| M2 face-normal touching test disabled | same | 18 |
| M3 undecided common piece treated as a seam | same | 6 |
| M4 native exact test only at fuzzy = band | `SewGuardInvalidCellIntegrationTests` (106) | 4 |
| M5 native decode threshold = Tolerance (pre-R3) | same | 8 |
| M6 no kernel-at-band pass for undecided pieces | same | 8 |

## R1/R2 corrections (2026-10-22)

- **R1 (P1): matching volumes skipped intrinsic overlap validation.** `AdoptSewCandidate` ran the overlap check only
  when coverage, volumes or the direct build were in doubt. Two identical unit cubes in both candidates (clean
  coverage, equal totals 2 m3) returned `Success=True` with two identical cells and only `SAM_OCCT_SEW_SUCCESS`.
  Fixed: the sew cells' disjointness is checked on every validated call (`OcctCellComplexBuilder.cs`). The case now
  fails closed (`SEW_BUILD_INVALID` + `BUILD_REJECTED`, no cells, returns false); with a valid direct build it
  recovers through the replacement route. The same pattern occurs on real data: current Revit-home, GH path, offset
  (100,-50,0): a 20-cell sew build totalling exactly 215.993 m3 with proven overlapping cells.
- **R2 (P1): an overlapping replacement passed its independent checks.** `ValidateCells` tested only each cell's
  internal point against the other cells. Boxes x 0..2 and 1.5..3.5 (0.5 m3 overlap) passed; the orchestration
  adopted them with `Success=True` and 4 m3 for a 3.5 m3 union. Fixed: `CheckDisjoint` (see Work completed and
  Decisions) replaces the point test in `CheckOverlap` and `ValidateCells`, so it covers the sew check, the #76
  refinement route and the replacement route. Samples now only prove overlaps; disjointness must be shown by a
  bounding-box gap, a separating face plane or the kernel's exact common volume; otherwise the candidate fails.
- Unchanged: the opt-in, the opt-out path, RetainTopology handling (an invalid sew build still fails closed; a
  valid one keeps its handle), the #76 route's strict closure tolerance, native ABI, Grasshopper contracts, SAM,
  SAM_Solver, solver algorithms and golden expectations. No recursive guard call: the exact test calls the native
  boolean directly.
- One test-support change in production code: `SAM.Geometry.OCCT` grants `InternalsVisibleTo` to
  `SAM.OCCT.IntegrationTests` (as `SAM.Geometry.OCCT.Solver` already does), so the native exact test is tested
  against the real kernel.
- **Behaviour change to note:** a sew build whose cells the kernel cannot decide (no separating plane and the
  native Common fails, status 20/21: one cell cannot be rebuilt as a solid) is now treated as invalid. It is
  replaced by an independently valid direct build or fails closed, where before it could be kept. On the recorded
  panel sets this happened only on AdjacencyCluster-home (sew cells not closed at the fuzzy tolerance, some with
  proven overlaps too); every such build was replaced by the direct decomposition and none failed closed.

## Problem

`SAMOCCT.CreateAdjacencyCluster` builds with sew-before-build and the opt-in `ValidateSewCoverage` guard (#76). On
the fixed Revit-home Solve3D output, a rigid translation of the input made the sew build chaotic: merged rooms,
inverted envelope cells (-216, -192, -85.7 m3) and a duplicated envelope (431.985 m3 = 2 x 215.993), all with
`Success=True`. The coverage-only guard rescued 4 of 8 offsets, kept a negative cell with a Warning on 2, and
missed 2 silently: an envelope cell, inverted or duplicated, covers every input face, so coverage looks clean and
the early return adopted the sew build. The direct build is translation-invariant on the same panels.

## Work completed

`SAM_OCCT/SAM.Geometry.OCCT/Native/OcctUnderSplitGuard.cs` (managed, except the exact overlap test of pairs the
managed tests cannot decide):
- `CheckSignedVolumes`: signed volumes must be finite and positive. This runs before any absolute value is taken,
  for sew and direct cells. `ValidateCells` now rejects negative, zero and non-finite signed volumes first: `|-216|`
  previously passed as a valid 216 m3 cell.
- `CheckVolumeAgreement`: total signed volume, sew vs direct, within 0.1 % plus `Tolerance` times the input area.
  It reports a conflict; it does not say which build is right.
- `CheckOverlap` / `CheckDisjoint` (rewritten in the R1/R2 round, policy unified in the R3 round): every pair of
  cells must be shown to touch or be disjoint, or the check fails. Overlap means a common region holding a ball
  wider than the glue band. A sample point (each cell's internal point, the probes, the centre of the pair's
  bounding-box intersection) deeper than half the band inside both cells proves an overlap. Touching is shown by
  a direction (an axis, or a face normal of either cell) along which the cells' projections overlap by at most
  the band (sound: a cell lies in the convex hull of its vertices), or, failing both, by the exact native test
  (`OcctCellComplexBuilder.CommonVolume`), which classifies its common pieces by the same rule (`ExceedsBand`).
  `ValidateCells` uses the same check, so it covers every adoption route.
- `CheckCoverageComplete` and `EvaluateReplacement`: a replacement for an invalid reference stands on its own.
  It needs `ValidateCells` (closed, positive, non-sliver, disjoint) and complete coverage (every face Covered
  or Trimmed). It does not need refinement of, or coverage gain over, the invalid reference. Closure is tested at
  max(Tolerance, FuzzyTolerance), see Decisions.
- `InternalPoint` fallback: when the native centre and `Shell.InternalPoint3D` both fail, use the vertex centroid,
  but only if `Shell.Inside` confirms it. Points found before are unchanged.

`SAM_OCCT/SAM.Geometry.OCCT/Native/OcctCellComplexBuilder.cs`, `AdoptSewCandidate` (only with
`ValidateSewCoverage`; the opt-out path is byte-for-byte the old behaviour):
1. Coverage of the sew build, as before.
2. Signed-volume check of the sew cells, before and independent of coverage.
3. The direct build is made on every validated call. Before, it was made only when coverage was suspect.
4. Volume agreement when both builds have valid volumes.
5. Disjointness check of the sew cells, on every validated call (R1 correction: it used to run only when coverage,
   volumes or the direct build were in doubt). A pair that cannot be shown disjoint makes the sew build invalid.
6. **Invalid sew** (step 2 or 5 failed), handled by `ReplaceInvalidSew`. A Warning `SAM_OCCT_SEW_BUILD_INVALID`
   lists the reasons and the totals. The direct build is adopted only if `EvaluateReplacement` passes, with Warning
   `SAM_OCCT_BUILD_FALLBACK_DIRECT`. Otherwise the build **fails closed**: Error `SAM_OCCT_BUILD_REJECTED`, no cells,
   `Success=false`, the method returns false and `TryBuild` returns false without trying another build.
   `Create.Shells` then returns null and `Create.AdjacencyCluster` returns null; the GH component reports a GH
   Error and `Successful=false`. With `RetainTopology` an invalid sew build always fails closed (the direct build
   frees its handle); the sew handle is disposed and `result.Topology` stays null.
7. **Valid sew, clean coverage, volumes agree**: kept, Info `SAM_OCCT_SEW_SUCCESS`, exactly as before.
8. **Valid sew, suspect** (coverage interior or unused, or volume conflict): first the unchanged #76 route
   (`Evaluate`: cells, refinement, strict coverage gain). If that rejects and the sew build is both suspect and in
   volume conflict, it is not a pure under-split, because merging rooms keeps the volume. The direct build may then
   replace it only through `EvaluateReplacement`. Otherwise the sew build is kept with the existing Warnings, plus
   `SAM_OCCT_SEW_VOLUME_CONFLICT` (Warning) when the volumes disagree. A valid sew build with clean coverage in
   volume conflict is always kept with that Warning.

`OcctCellComplexBuilder.CommonVolume` (R1/R2 round; reworked in the R3 round): the exact test, through the
existing native `sam_occt_shells_intersection` (boolean Common) on one pair of shells. It returns the volume of the
common pieces that exceed the band by `ExceedsBand`, 0 when none does, and null ("cannot decide") on any failure
or a piece that stays undecided. Kernel at fuzzy band/10 with a decode threshold far below a band-wide ball; since
R4, pieces `ExceedsBand` leaves undecided are decided by `OcctGlueBandProof` and the kernel is not asked again at
fuzzy = band (see "R4 correction").

The direct-build delegate (`OcctCellComplexBuilder.DirectBuild`) is a test seam. Production passes the unchanged
`TryDirectBuild`; unit tests pass a stand-in, so every path, including fail-closed, runs without OCCT. The exact
test is a second seam (`OcctUnderSplitGuard.CommonVolume`, a parameter of `CheckDisjoint` and `ValidateCells`);
unit tests pass stand-ins or `NoExactTest`. Their box-cell cases are decided by the managed tests, so the unit suite
makes no native call; the native exact test is covered by the integration tests.

Tests and inputs:
- `Testing/SAM.OCCT.UnitTests/SewInvalidCellGuardTests.cs` (58 tests; 37 before the R1/R2 round, one of which was
  rewritten: a partial overlap is now reported without probes).
- `Testing/SAM.OCCT.IntegrationTests/SewGuardInvalidCellIntegrationTests.cs` (42 tests; 34 before: +7
  `CommonVolume` cases against the real kernel and +1 L-shape notch/intrusion test).
- `SAM_OCCT/SAM.Geometry.OCCT/Properties/AssemblyInfo.cs`: `InternalsVisibleTo("SAM.OCCT.IntegrationTests")`.
- `Testing/SAM.OCCT.IntegrationTests/Fixtures/Revit-Reconstruction/`: two fixed Revit-home `Solve3D` outputs, one
  AnalyticalModel each. `Revit-home-solved-current-131panels.sam` (59,572 B) is the current stack's output, SHA256
  82015AD8...; `Revit-home-solved-july-105panels.sam` (48,161 B) is the July stack's, SHA256 C35C66BB.... Both were
  re-exported from the attribution's `runs/{cur-1,g-1}/solved-panels.json`, vertex-exact (0 structural differences,
  max vertex distance 0). The csproj copies them; `Fixtures/README.md` documents them.
- `TESTING.md`: new section "Sew guard against invalid cells".

## Decisions and assumptions

- **Opt-in unchanged.** Only `ValidateSewCoverage` callers (today `SAMOCCT.CreateAdjacencyCluster`) are affected.
  No native ABI, Grasshopper contract, SAM Plane/Convert, solver algorithm, fixture expectation or golden change.
- **Volume disagreement is a conflict, not proof.** A valid sew build is never replaced just because volumes differ.
  The direct build must pass its own cell and coverage checks, and replaces a valid sew build only when that sew
  build is also suspect. On gappy input the sew build can be the correct one, because it closes small gaps that a
  direct build cannot.
- **Disjointness is always checked and must be established (R1/R2 correction).** The earlier rule skipped the
  overlap check when coverage was clean and the totals agreed; R1 showed that both builds can hold the same
  duplicated cells. The earlier check also used point samples as if they proved disjointness; R2 showed two boxes
  overlapping by 0.5 m3 that passed. Now every candidate the guard accepts (sew, #76 refinement, replacement)
  goes through `CheckDisjoint`: samples only *prove* overlaps; disjointness comes from a bounding-box gap, a
  separating face plane, or the kernel's exact common volume; anything undecided fails.
- **Glue band for disjointness (one policy since R3).** Overlaps no thicker than b = max(Tolerance,
  FuzzyTolerance) count as touching, where "thicker" means the common region holds a ball of diameter greater than
  b. Every test applies this rule (see "R3 correction"), so the decision does not depend on the pose. The kernel
  glues geometry within the fuzzy tolerance, and decoded real cells have seams of 1e-6 to 1e-4 m. A compact
  overlap counts once it is wider than b in every direction. (Before R3: band-thick slabs in the managed tests,
  mean thickness 2V/S in the exact test, and any shallow point in both cells as proof of overlap.)
- **Undecided pieces (R4; replaces the R3 residual assumption).** A common piece that the piece tests cannot
  classify is decided by `OcctGlueBandProof` on the two cells (see "R4 correction"), not by the kernel at fuzzy = b,
  whose gluing removed a piece holding a 1.17 mm ball. A piece the proof cannot decide within its budget fails
  closed. What remains trusted to the kernel: the fine pass (fuzzy b/10) finds every common piece that holds a
  band-wide ball, and its decoded pieces locate the exact common region to within that fuzzy value.
- **Exact test = existing native boolean Common.** `OcctCellComplexBuilder.CommonVolume` calls the existing
  `sam_occt_shells_intersection` entry point (no ABI change) on one pair, frees its own handle, and never enters
  the guard or touches a candidate's retained topology. It runs only for pairs the managed tests cannot decide.
  Native missing, a kernel error or a non-finite result means "not established", so the candidate is rejected.
- **Closure band for replacements.** `EvaluateReplacement` tests closure at max(Tolerance, FuzzyTolerance), the band
  `FaceCoverage` already uses ("geometry closer than the fuzzy tolerance is the same geometry"). Decoded direct cells
  on AdjacencyCluster-home and Face3D-home have seams of 1e-6 to 1e-4 m (closed at 1e-4, not at 1e-6), far below the
  1e-3 m glue tolerance. The #76 under-split route keeps `Tolerance` (1e-6), per the scope rule to preserve its
  checks. See Unresolved.
- **Internal-point fallback.** Thin roof wedges of a direct build (exact geometry pinned in a unit test) defeat both
  the native centre and `Shell.InternalPoint3D`. The guard then rejected a valid build as "cell 5 has no internal
  point". The verified vertex-centroid fallback also applies in the #76 route, but only where no point was found
  before.
- **New codes** (each with a single, producer-owned severity; Warnings and Errors are never filtered, so no detail
  table change): `SAM_OCCT_SEW_BUILD_INVALID` (Warning), `SAM_OCCT_BUILD_REJECTED` (Error),
  `SAM_OCCT_SEW_VOLUME_CONFLICT` (Warning). The `SAM_OCCT_COVERAGE_UNAVAILABLE` text now says what was still
  checked. Other existing texts are unchanged.
- 14 cells is the direct build's observed decomposition on Revit-home, not design truth: five cells are 1.2 m3 or
  smaller. Tests compare against the direct build (cell by cell, after inverse translation), not a room count. The
  observed total of 215.993 m3 is pinned with a comment.

## Validation (local, Windows, this machine; hosted CI is in "Hosted CI")

Environment:
- **Native:** frozen copy of `SAM_OCCT\build` (SAM.Occt.Native.dll SHA256 438089F8..., TKBO.dll 1FA732FE..., ABI 4), the
  same files as the attribution's `attrib/native-frozen`. It was copied into the worktree's ignored `build/`, and
  every probe logged the loaded module path and hash.
- **Managed dependencies, read-only:** `..\SAM\build` at SAM `ba686d24` (SAM.Core E6195140..., SAM.Geometry
  6FDF2968..., SAM.Analytical 311F379D..., unstamped local build) and `..\SAM_Solver\build` at `fd63b467`
  (C1338585...).
- **Builds:** `SAM_OCCT_SKIP_NATIVE_BUILD=true`. Base for comparison: a plain `git archive dc871f7` export
  (`SAM-BIM\SAM_OCCT-base-dc871f7`, not a worktree) with the same native and managed binaries.

### Test suites

Commands, run from the worktree with `SAM_OCCT_SKIP_NATIVE_BUILD=true`; the base used the same commands in its
export:

```powershell
dotnet build Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug --no-incremental
dotnet build Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug --no-incremental
dotnet test Testing/SAM.OCCT.UnitTests/SAM.OCCT.UnitTests.csproj -c Debug --no-build --logger trx
dotnet test Testing/SAM.OCCT.IntegrationTests/SAM.OCCT.IntegrationTests.csproj -c Debug --no-build --logger trx
```

| Suite | Base `dc871f7` | Head `ac212bd` (superseded) | Corrected `45e307f` / `8d4beff` |
|---|---|---|---|
| Build | 0 errors | 0 errors. Clean rebuild: 243 warnings (unit) and 284 (integration), pre-existing analyzer, XML-doc and MSB3277 warnings; base warning counts were not captured. Only new-file warnings: 7 CA1305 on assertion-message `string.Format` in the integration test, the existing tests' idiom. None in production code. | 0 errors; same 243 / 284 warnings at `8d4beff` (the new code adds none; an intermediate CS1573 and two CA1305 were fixed before the commit) |
| Unit | 700 passed / 0 failed | 737 / 0 (+37 `SewInvalidCellGuardTests`) | **758 / 0** at `8d4beff` (+21) |
| Native, focused (`SewGuardInvalidCell`, `HouseUnderSplit`, `HouseAirAndAttic`) | - | 50 / 0 / 0 | **58 / 0 / 0** at `8d4beff` |
| Native integration, full | 261 passed / 19 failed / 2 skipped (12 min 6 s) | 295 / 19 / 2 (10 min 37 s; +34) | **303 / 19 / 2** at `45e307f` (8 min 2 s; +8) |

The full native suite ran on the `45e307f` content; `8d4beff` adds one unit test and no production or integration
change, and was rerun with the unit suite and the focused native set.

Comparison by identity, from the TRX files (corrected head against the recorded base and `ac212bd` lists, by test
name plus normalised message, `r1r2-corrections/Compare-Failures.ps1`; the script reproduces the recorded 19/19 on
the `ac212bd` TRX):
- The same 19 failures, by name and identical message, as base and as `ac212bd`. None is resolved, none is new.
- The 2 skips are the same: the large-panel sew-guard skip and the inverse-gated native-missing test.
- No test that passed on base fails on the corrected head, in either suite.

As expected, Scope 1 clears none of the 19 failures: none of them sets `ValidateSewCoverage`. They are the solver
golden and parity tests explained by SAM 173ababc in the attribution. Grasshopper contract tests and the full
solution were not built (out of scope).

### Review reproduction (R1, R2)

The review's `reproduce-findings.ps1` needs PowerShell 7, which is not installed here; Windows PowerShell 5.1
cannot load the net8.0 test assembly (`System.Runtime 8.0.0.0` not found). A faithful C# port (same reflection
calls, helper names and inputs; `r1r2-corrections/repro-port-src/`) was run instead, first against a `git archive`
export of the reviewed head `599ed75` to show that it reproduces the findings, then against `8d4beff`:

| Case | `599ed75` | `8d4beff` |
|---|---|---|
| R1: duplicate cells, equal totals | Returned True, Success True, 2 cells, `SEW_SUCCESS` only | Returned False, Success False, 0 cells, `SEW_BUILD_INVALID`, `BUILD_REJECTED` |
| R2: overlapping replacement | `EvaluateReplacement` Adopt True with no failure; orchestration Success True, 2 cells, 4 m3 | Adopt False ("cells 0 and 1 overlap"); Success False, 0 cells, `BUILD_REJECTED` |

Outputs: `r1r2-corrections/repro-port-599ed75.txt` and `repro-port-8d4beff.txt`.

### Translation invariance on fixed panel sets (machine-local probe)

The probe is an evidence-only console tool, not committed. It loads a fixed panel set and applies the eight
recorded rigid offsets: 0; 0.5x; 0.25y; (1,1); 8x; 1z; 1e-7x; (100,-50). At each offset it builds with exactly
`SAMOCCT.CreateAdjacencyCluster`'s options, in two modes:
- non-Air panel faces through `Create.Shells` (the attribution's method);
- the Grasshopper path, `Analytical.OCCT.Create.AdjacencyCluster` with all panels, Air included.

Per offset it records the signed cell volumes, coverage, codes, time, and the deviation of the inverse-translated
cells (volume and bounding box) from offset 0. The fixed panel sets are:
- the two Revit-home Solve3D outputs (committed fixtures);
- one `Solve3D` (default arguments) run on each of AdjacencyCluster-home (106 to 204 panels, 6 naked),
  Face3D-home (124 to 162, 0 naked) and PR49-Test2 (266 to 262, 0 naked), made on the base stack and saved
  machine-locally.

Results are counted against the direct decomposition, which is translation-invariant on every set
(dV 0, dBox 0 at all offsets). Base and head unguarded sew and direct builds are identical.

| Panel set | Mode | Direct cells / m3 | Base: match, non-positive cell returned, silent deviation | Head `ac212bd` | Corrected `8d4beff` |
|---|---|---|---|---|---|
| Revit-home current (131) | shells | 14 / 215.993 | 4/8, 3, 2 | **8/8**, 0, 0 | 8/8, 0, 0 |
| Revit-home July (105) | shells | 14 / 215.993 | 8/8, 0, 0 | 8/8, 0, 0 | 8/8, 0, 0 |
| AdjacencyCluster-home solved | shells | 18 / 601.300 | 0/8, 6, 4 | **7/8**, 0, 0 | **8/8**, 0, 0 |
| Face3D-home solved | shells | 20 / 601.301 | 1/8, 3, 1 | **7/8**, 0, 0 | 7/8, 0, 0 |
| PR49-Test2 solved | shells | 44 / 6674.678 | 8/8, 0, 0 | 8/8, 0, 0 | 8/8, 0, 0 |
| Revit-home current (131) | GH path | 22 / 215.993 | 3/8, 1, 1 | **6/8**, 0, 0 | 6/8, 0, 0 |
| Revit-home July (105) | GH path | 14 / 215.993 | 8/8, 0, 0 | 8/8, 0, 0 | 8/8, 0, 0 |
| AdjacencyCluster-home solved | GH path | 18 / 601.300 | 8/8, 0, 0 | 8/8, 0, 0 | 8/8, 0, 0 |
| Face3D-home solved | GH path | 20 / 601.301 | 8/8, 0, 0 | 8/8, 0, 0 | 8/8, 0, 0 |
| PR49-Test2 solved | GH path | 44 / 6674.678 | 8/8, 0, 0 | 8/8, 0, 0 | 8/8, 0, 0 |
| **Total** | | | **56/80, 13, 8** | **76/80, 0, 0** | **77/80, 0, 0** |

R1/R2 round (2026-10-22): the same probe (v4: `r1r2-corrections/harness/`) was rerun for the reviewed head
`599ed75` (export, content = `ac212bd` + docs) and the corrected `8d4beff`, interleaved per panel set, same frozen
native (module hashes logged). `599ed75` reproduced the 76/80 above exactly. On `8d4beff`: 77/80, no non-positive
cell, no fail-closed build (every guarded build `Success=True`). Of the 96 guarded builds (6 sets including House,
2 modes, 8 offsets), 89 are identical to `599ed75` including the Warning codes. The 7 differences are all on
AdjacencyCluster-home, and only one changes the cells:
- shells at (1,1,0): previously a 13-cell sew build kept with `BUILD_UNRELIABLE`; its cells cannot be shown
  disjoint, so it is now invalid and replaced by the exact direct decomposition (18 cells, dV 0);
- shells at (1e-7,0,0) and GH path at (0,0.25,0), (1,1,0), (0,0,1), (1e-7,0,0), (100,-50,0): the same 18 cells as
  before, now reached through `SEW_BUILD_INVALID` + `BUILD_FALLBACK_DIRECT` instead of the #76 route or
  `EnsureAirRepresented`'s direct rebuild.
The reasons: on that set the kernel cannot rebuild some sew cells as solids (Common status 20/21; several such
cells are not closed at the fuzzy tolerance), and other pairs are proven overlaps. Direct cells were never
undecided on any set. Summaries: `r1r2-corrections/Summarize-Probes.ps1`; raw outputs in `r1r2-corrections/probe-runs/`.

How the base was "rescued" on the GH path: an inverted sew build (for example 13 cells, 8.087 m3, against
601.3 m3) was sometimes replaced later by `EnsureAirRepresented`'s unguarded direct rebuild. That was luck, not
the guard. Head made no rejections in these 80 builds. All head matches have zero deviation after the inverse
translation (dV at most 3.6e-5 m3, dBox at most 1e-6 m). The 4 head residuals are valid, volume-preserving
under-splits, flagged with `BUILD_UNRELIABLE` (see Unresolved).

On the Revit-home sets with non-Air faces, every head result is exactly the direct decomposition: 14 cells
(57.771, 53.087, 32.157, 22.124, 10.687, 9.898, 9.388, 9.169, 8.880, 1.210, 0.444, 0.422, 0.387, 0.368 m3),
coverage 113/113 (current set) and 105/105 (July set). The committed integration tests pin this per cell.

House-TwoStorey-23panels (existing `HouseUnderSplitIntegrationTests`, run on the tested SHA): 11 cells, one per seed
(10 rooms plus the attic). Total 564.1 +/- 0.5 m3, suspicion and fallback Warnings, no Error, 11 seed-named spaces.
The legacy path without the option still gives 4 cells, and RetainTopology keeps the sew build with
`BUILD_UNRELIABLE`.

Mutation checks (each injected defect fails its target unit tests; source restored and verified byte-identical
with `cmp`):

| Injected defect | Failed tests |
|---|---|
| Signed-volume check removed | 1 (the overlap check still caught the inverted envelope: defence in depth) |
| Fail-open (keep the invalid sew build) | 6 |
| Old coverage early return before the comparison build | 3 |
| No vertex-centroid fallback | 1 |
| Strict closure in the replacement route | 1 |
| No conflict route | 1 |

R1/R2 round, against `SewInvalidCellGuardTests` (58 tests); each mutation applied with a scripted replacement,
then restored with `git checkout` and verified clean:

| Injected defect | Failed tests |
|---|---|
| R1 skip restored (sew overlap check only when in doubt) | 1 (the R1 regression) |
| Point samples only (the R2 semantics) | 9 |
| An undecided pair treated as disjoint | 3 |
| Face plane never separates | 1 (the wedge control) |
| Face plane always separates | 3 |
| A positive exact common volume ignored | 1 |
| Bounding-box-centre sample removed | 6 (still rejected, but as "not established" instead of "overlap") |

### Runtime impact

The extra work is one native direct build per guarded call, which base made only when coverage was suspect.
At `ac212bd` the overlap check (point-in-cell, sew cells × probes) added to it only when something was in doubt;
the R1/R2 round replaced it (see "Stronger validation" below). The two tables below are the `ac212bd`
measurements: same probe, sequentially, with nothing else running; build time only, coverage reporting excluded.

Medians in ms over the 8 offsets of the non-Air `Create.Shells` builds:

| Panel set | Sew only | Direct only | Guarded, base | Guarded, head | Delta |
|---|---|---|---|---|---|
| House-TwoStorey (23) | 39.5 | 33 | 145 | 133.5 | -11.5 |
| Revit-home current (113 faces) | 127 | 218.5 | 827 | 835.5 | +8.5 |
| Revit-home July (105) | 99.5 | 209 | 568.5 | 656.5 | +88 |
| AdjacencyCluster-home solved (186) | 251.5 | 427.5 | 1704 | 1682.5 | -21.5 |
| Face3D-home solved (160) | 178.5 | 354 | 1300 | 1320.5 | +20.5 |
| PR49-Test2 solved (262) | 192 | 398.5 | 1118 | 1046 | -72 |

GH path (`Create.AdjacencyCluster`, Air included), medians in ms:

| Panel set | Base | Head |
|---|---|---|
| House | 161.5 | 169 |
| Revit-home current | 1565.5 | 1451.5 |
| Revit-home July | 907.5 | 922.5 |
| AdjacencyCluster-home | 3300.5 | 2845 |
| Face3D-home | 2164.5 | 1906 |
| PR49-Test2 | 1252 | 1291.5 |

Head is sometimes faster on the GH path: replacing an invalid sew build early avoids `EnsureAirRepresented`'s
second full build.

- **Where the cost lands:** calls whose sew coverage is clean. On the July Revit-home set: base 383 to 419 ms,
  head 600 to 634 ms, about **+200 to +230 ms per call** (+55 %), one direct build. Calls that were already
  suspect cost the same as before (median deltas within run-to-run noise of about +/-90 ms).
- **Scale:** the cost grows with the native direct build. It applies only to `ValidateSewCoverage` callers.
- The headline timing is the probe's single-run median, not a benchmark; single runs vary by about 10 to 20 %.

**Stronger validation (R1/R2 round).** Same probe, reviewed head `599ed75` against corrected `8d4beff`, run
interleaved per panel set in one session (Release build of the probe; another session's SAM_OpenStudio tests ran
concurrently, CPU about 14 %). Absolute times are not comparable with the tables above (different run and load);
compare within this table only. Medians over the 8 offsets, ms:

| Panel set | Shells `599ed75` | Shells `8d4beff` | Delta | GH path `599ed75` | GH path `8d4beff` | Delta |
|---|---|---|---|---|---|---|
| House-TwoStorey | 131.5 | 144 | +12.5 | 161.5 | 170.5 | +9 |
| Revit-home current | 376.5 | 491.5 | +115 | 706 | 1130 | +424 |
| Revit-home July | 346 | 376.5 | +30.5 | 477.5 | 509.5 | +32 |
| AdjacencyCluster-home | 759 | 891.5 | +132.5 | 1454.5 | 1520 | +65.5 |
| Face3D-home | 631 | 829.5 | +198.5 | 925.5 | 1193 | +267.5 |
| PR49-Test2 | 494.5 | 510 | +15.5 | 669.5 | 681.5 | +12 |

The deltas are consistent offset by offset (not noise), about +2 % to +60 %. The cost is the exact test: the
probe's `pairs` mode counts it per build (`r1r2-corrections/probe-runs/pairs-8d4beff-*.txt`). The managed tests
decide almost every pair (House: all 55 direct pairs; PR49-Test2: 946 of 946 direct pairs). The native Common
runs for 0 to 6 direct pairs per build (up to about 130 ms, AdjacencyCluster-home) and 0 to 8 sew pairs (up to
about 200 ms), at roughly 15 to 35 ms per pair. Every guarded call checks the sew cells; the direct cells are
checked again for each route that evaluates them, and the GH path can build more than once, which is why its
delta is larger. Only `ValidateSewCoverage` callers pay this.

### Machine-local evidence (not committed; copy to keep)

In the owner workspace folder `SAM-BIM/.review-sew-guard-q4-20261022/` (2.4 MB, not Git-tracked): the diff and
`patches/`, the probe, solve and restore harness sources (`harness/`), raw base and head probe outputs
(`probe-runs/`: v3 head, and the final timing run for base and head), suite logs plus failure names and messages
(`suites/`), the three solved panel sets (`solved-panel-sets/`) and the staged restore copies
(`staged-sam-models-restore/`). The TRX files are in the session scratchpad only. The attribution workspace
`.review-occt-integration19-20261022/attrib/` (1.3 GB) holds `native-frozen` and `runs/{cur-1,g-1}`.

R1/R2 round, in `SAM-BIM/.review-sew-guard-q4-20261022/r1r2-corrections/` (new files only; nothing earlier was
modified): `harness/` (probe v4 with the `pairs` mode and undecided-pair diagnosis), `run-probes.sh`,
`probe-runs/` (shells, GH-path and pairs outputs for `599ed75` and `8d4beff`), `Summarize-Probes.ps1`,
`Compare-Failures.ps1`, `repro-port-src/` with its outputs, `suites/` (the TRX files and suite logs of this round)
and `PROVENANCE-sam-models.md`. Exports used: `SAM-BIM/SAM_OCCT-reviewed-599ed75` (`git archive 599ed75`, with the
frozen native copied into its ignored `build/`). The independent review and its reproduction script are in
`SAM-BIM/.review-sew-guard-independent-20261022/`.

R3 round, in `SAM-BIM/.review-sew-guard-q4-20261022/r3-corrections/` (new files only; nothing earlier was
modified): `repro-rotation-src/` (C# port of `reproduce-rotation.ps1`) with outputs for `e7bb5e2` and `cb1a719`;
`harness/` (probe v5: v4 plus `scan`, `synthetic`, `diag` modes and per-piece kernel diagnostics);
`run-probes.sh`, `Summarize-Probes.ps1`, `run-mutations.sh` and `mutations.txt`; `probe-runs/` (shells, GH-path
and pairs outputs for both heads, the kernel scans, and the (100,-50,0) diagnosis); `suites/` (unit, focused and
full native TRX files and log of `cb1a719`). Export used: `SAM-BIM/SAM_OCCT-reviewed-e7bb5e2` (`git archive
e7bb5e2`, frozen native copied into its ignored `build/`).

R4 round, in `SAM-BIM/.review-sew-guard-q4-20261022/r4-corrections/` (new files only; nothing earlier was
modified): `suites/` (fresh build logs, `unit-a135c5f.trx`, `native-focused-a135c5f.trx`, `native-full-a135c5f.trx`
and log, `tested-sha.txt`, `compare-native-full-r3-vs-a135c5f.txt`), `Compare-Trx.ps1`, `reviewer-probe-port/`
(the review's `r3-probe` with one call adapted), `harness/` (probe v5 unchanged; v5.1 fixes the pairs-mode
reflection call; v5.2 adds evidence-only proof statistics), `run-probes.sh`, `run-pairs-v51.sh`,
`Summarize-Probes.ps1`, `run-mutations.sh` with `mutations*.txt` and logs, and `probe-runs/` (reviewer probe on
`cb1a719` and `a135c5f`, shells and GH-path outputs for both heads, v5.1 pairs, v5.2 proof statistics, summaries;
files marked INVALID are kept but are not evidence). Export used: `SAM-BIM/SAM_OCCT-reviewed-1726f0d` (`git archive
1726f0d`, frozen native copied into its ignored `build/`). `proof-scans/`: sources and outputs of the
analytic-threshold, tilt and non-convex-face scans, run on a public copy of the `a135c5f` proof (the committed unit
tests pin the decisive cases).

**Evidence incident (owner action needed, machine-local):** during this work an evidence-only probe run, with a
stale binary, wrote its text log over two files in `.review-occt-integration19-20261022/sam-models/`:
`02_July_Solve3D-output_105-panels.sam` and `03_Current_Solve3D-output_131-panels.sam` (now 2.5 kB text). Their
sources, `attrib/runs/{g-1,cur-1}/solved-panels.json`, are intact. **Correction (2026-10-22, R1/R2 round):** an
earlier version of this record called the regenerations "byte-equivalent". They are not. They are
geometry-equivalent reconstructions, regenerated with the exporter's own logic, names and descriptions and staged
in `SAM-BIM/.review-sew-guard-q4-20261022/staged-sam-models-restore/`: 131 and 105 panels, 0 structural differences,
max vertex distance 0 against the JSON. Their bytes differ from the lost originals (whose hashes were never
recorded) because the AnalyticalModel GUID, and so the compressed content, changes per regeneration. The committed
fixtures are further, separate regenerations from the same JSON, not copies of the staged files: same geometry,
different bytes (staged 48,160 B / 59,572 B vs fixtures 48,161 B / 59,572 B, all four hashes differ). Hashes and
provenance: `SAM-BIM/.review-sew-guard-q4-20261022/r1r2-corrections/PROVENANCE-sam-models.md`. The damaged files
and the staged copies were left untouched. Replacing the damaged files was blocked by the agent permission check and
is left for the owner; a restored file must be described as a reconstruction. The probe now refuses unknown modes
and any output that is not a new `.txt` file.

## Unresolved, risks, limitations

- **Not fully translation-invariant yet; residual cases are flagged, never silent.** On fixed panel sets, 4 of 80
  guarded builds (4 at `ac212bd`, 3 at `8d4beff`) still differ from the direct decomposition. Each is a valid sew
  build (positive, shown disjoint) that preserves volume while under-splitting. The unchanged #76 route rejects the
  direct build for it, by its strict 1e-6 m closure test on decoded seams or by refinement ("direct cell lies in 2
  sew cells"). The sew build is kept with `SAM_OCCT_BUILD_UNRELIABLE` (Warning):
  - Face3D-home non-Air at (8,0,0) (direct cells 4, 6, 11 and 19 "not closed" at 1e-6 m);
  - current Revit-home GH path at (1,1,0), (1e-7,0,0) and, since R3, (100,-50,0) (refinement). At `8d4beff` the
    last one was replaced only because a point on a shared face was taken as an overlap (see "R3 correction").

  The AdjacencyCluster-home non-Air (1,1,0) residual is gone: its sew cells cannot be shown disjoint, so the
  replacement route now takes it. **Owner decision (unchanged):** should the #76 route also judge closure at the
  glue band? That would very likely clear the Face3D-home case; it relaxes a preserved check, so it is not done here.
- **Undecidable sew cells now go to replacement or fail closed.** Where the kernel cannot rebuild a sew cell as a
  solid (Common status 20/21) and no face plane separates the pair, the sew build is not accepted. On the recorded
  sets this only rerouted AdjacencyCluster-home builds to the identical direct result; on other inputs it could turn
  a previously kept build (with a Warning) into an Error with no cluster when the direct build also fails its checks.
  Owner judgement: accept this fail-closed stance, or treat kernel-undecidable sew pairs differently.
- `ValidateSewCoverage` remains opt-in. `Solve3D`'s internal builds do not use it, so the end-to-end solver
  instability (Scope 2: 4 to 42 spaces under translation on gappy fixtures) is unchanged and was kept separate. All
  reconstruction checks here use fixed solver outputs.
- Every guarded call now pays one extra native direct build, plus the exact test for pairs the managed tests cannot
  decide (+2 % to +60 % on the recorded sets, see "Runtime impact"). Only the opt-in path pays it. Since R4 the
  exact test runs the kernel once per pair (the coarse pass is gone); exact-test time on the recorded sets is
  unchanged from `cb1a719` within noise. The proof costs up to its 20,000-box budget per undecided piece (measured
  on synthetic pieces: 2 to 5,808 boxes, under 130 ms; a spent budget took 0.6 to 1.5 s); it never ran on the real sets.
- Disjointness is exact only up to the glue band (see Decisions). Pairs with no separating face plane depend on
  the kernel's boolean Common. Direct cells were never undecided on the recorded sets and offsets; no guarded build
  failed closed there.
- **R3 residual assumption: resolved by R4.** Undecided pieces are no longer accepted through the kernel's gluing at
  fuzzy = b (that acceptance was the R4 false success); they are proven seams or overlaps by `OcctGlueBandProof`, or
  fail closed.
- **R4 limits (reviewer/owner judgement).**
  - The proof never ran on the six real panel sets (no fine-pass piece was left undecided), so its cost and its
    answers on real undecided pieces are known from synthetic geometry only. A real piece too complex for the budget
    fails closed (the pair is "not established"): on other inputs this can turn a previously accepted sew build into
    a replacement or a `BUILD_REJECTED`.
  - Behaviour change by the criterion: L-shaped seams 0.86 to 1 mm wide (accepted at R3) are now overlaps; the
    criterion holds a 1.17 t ball at the reflex corner. Whether that strict reading is wanted at the band edge is an
    owner decision; widening it would be a change of the documented criterion, not of this fix.
  - Still trusted to the kernel (unchanged since R3): the fine pass (fuzzy b/10) finds every common piece that holds
    a band-wide ball, and locates it to within that fuzzy value (the proof's region margin). A deep region glued away
    already at b/10 is not searched.
  - Ray parity needs cells closed at b/8 (checked; otherwise undecided). The threshold carries a relative 1e-9
    allowance (5e-13 m). Mutation R4-M10 (triangle verification removed) survives because SAM's triangulation is
    correct on every test face.
- **Policy change at the band edge.** The face-normal test now measures the overlap of the projections (at most
  b) instead of "own vertices within b on one side, the other's within b on the other" (up to 2b for non-convex
  cells), and point samples need depth > b/2. Within-band seams are accepted in every pose; nothing that the old
  rules proved an overlap with depth is now accepted. Mean thickness 2V/S is no longer used.
- **SAM observation, not changed here:** `Transform3D.GetRotation(axis, angle)` in SAM is not orthonormal (two
  matrix entries have the wrong sign; "TODO: Revise this method" in the source). Worth a separate SAM issue.
- The face-plane test assumes planar polygon faces (true for decoded cells); a face whose boundary is not a polygon
  disables it for that cell, and the pair falls to the exact test.
- Behaviour change for `SAMOCCT.CreateAdjacencyCluster` users: a demonstrably invalid sew build with no acceptable
  replacement now yields no cluster and a GH Error, instead of a cluster built on inverted cells. No such case
  occurred on the five fixed panel sets at the eight offsets once the replacement fixes were in.
- Fail-closed was exercised end to end on native for RetainTopology only (`CellComplexByPanels` returns null and
  `Create.AdjacencyCluster` returns null with `BUILD_REJECTED`; it ran, not skipped, at `8d4beff`). Missing
  coverage, a failed or unavailable direct build, and inverted or overlapping (partial, duplicate, containment)
  direct cells are covered through the builder's return and `Success` with the unit-test stand-in: no natural native
  input produces them. `TryBuild` returns the guard's result unchanged, so the downstream null cluster follows from
  the same path the RetainTopology native test exercises.
- Machine-local evidence (frozen native, attribution workspace, probe and solve harnesses, raw probe outputs) is
  listed below and is not committed. The cross-machine and JIT floating-point sensitivity noted in the attribution
  was not retested. The RetainTopology fail-closed integration test therefore searches the recorded offsets for an
  inverted sew cell and skips with a message if none occurs on that machine.
- Grasshopper, the full solution and the Rhino/TAS acceptance were not built or run (out of scope). The
  Grasshopper contract is unchanged.

## Next step

Final review, then the owner's merge decision. Do not merge before both. PR #81 is open and ready for review; the
hosted `build` and `spdx` checks passed on `0718dbc` and must also be green on the final PR head (named in the PR
description). After a merge, `PROJECT_PROGRESS.md` gets its post-merge closeout with the merge SHA (CI-skip
marker as in AGENTS.md; direct docs-only commit on `sow/2026-Q4`); not before.

Owner decisions still open (none is recorded as accepted):
1. **Ball-diameter tolerance semantics**, including the strict reading of L-shaped seams 0.86 to 1 mm wide (accepted
   at R3, overlaps since R4). Reviewer recommendation, not an owner acceptance: keep the reviewed rule.
2. **Fail-closed stance for undecidable pairs** (kernel-undecidable sew pairs, a spent proof budget, unusable
   geometry) and its runtime cost (a spent budget took 0.6 to 1.5 s on synthetic pieces; the proof never ran on the
   six real sets). Reviewer recommendation: keep fail-closed.
3. **Fine-kernel completeness assumption:** the fine pass (fuzzy b/10) is trusted to find and locate every common
   piece holding a band-wide ball. Reviewer recommendation: acknowledge the documented limit.
4. **Closure band for the #76 route** (judge closure at the glue band?). Out of this PR; the reviewer recommends
   leaving it unchanged.
5. **Damaged machine-local evidence files** (`02_July_...105-panels.sam`, `03_Current_...131-panels.sam`): restore
   from the staged reconstructions, described as reconstructions.

Out of scope here and unchanged: the 19 historical native failures (same by name and message as base; separate
follow-up), `Solve3D` end-to-end instability (Scope 2) and the nine-space/North0 branch. The native behaviour of the
guard has no hosted evidence; it rests on the machine-local runs recorded above.
