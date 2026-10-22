# Sew guard against invalid cells (Scope 1) - branch record

Branch: `fix/sew-guard-invalid-cells-q4` (local only: not pushed, no PR, not merged). Base: `sow/2026-Q4` at
`dc871f7e6ee7d0e53adb826a2b597645e2eab4cb` (fetched 2026-10-22, unchanged). SAM and SAM_Solver are not changed.
Commits: `63be627` (guard), `ac212bd` (replacement judged on its own merits, after the real-fixture validation).
Tested content SHA: `ac212bdf0e379ab0ab3c2e5c696efb859f2f571a`. This record is the only later change (docs only).
Source of the task: Scope 1 of `.review-occt-integration19-20261022/ATTRIBUTION.md` (machine-local, owner workspace).
`PROJECT_PROGRESS.md` is not touched (post-merge closeout only).

## Status

Implementation, tests and local validation are complete. Awaiting independent review. Not pushed; no hosted CI has
run. Open owner decisions are listed under "Unresolved".

## Problem

`SAMOCCT.CreateAdjacencyCluster` builds with sew-before-build and the opt-in `ValidateSewCoverage` guard (#76). On
the fixed Revit-home Solve3D output, a rigid translation of the input made the sew build chaotic: merged rooms,
inverted envelope cells (-216, -192, -85.7 m3) and a duplicated envelope (431.985 m3 = 2 x 215.993), all with
`Success=True`. The coverage-only guard rescued 4 of 8 offsets, kept a negative cell with a Warning on 2, and
missed 2 silently: an envelope cell, inverted or duplicated, covers every input face, so coverage looks clean and
the early return adopted the sew build. The direct build is translation-invariant on the same panels.

## Work completed

`SAM_OCCT/SAM.Geometry.OCCT/Native/OcctUnderSplitGuard.cs` (pure managed):
- `CheckSignedVolumes`: signed volumes must be finite and positive. This runs before any absolute value is taken,
  for sew and direct cells. `ValidateCells` now rejects negative, zero and non-finite signed volumes first: `|-216|`
  previously passed as a valid 216 m3 cell.
- `CheckVolumeAgreement`: total signed volume, sew vs direct, within 0.1 % plus `Tolerance` times the input area.
  It reports a conflict; it does not say which build is right.
- `CheckOverlap`: every cell's internal point, plus extra probe points (the direct cells' internal points, used only
  as sample locations), must lie in at most one sew cell. A point in two cells proves an overlap, such as a
  duplicated envelope.
- `CheckCoverageComplete` and `EvaluateReplacement`: a replacement for an invalid reference stands on its own.
  It needs `ValidateCells` (closed, positive, non-sliver, non-overlapping) and complete coverage (every face Covered
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
5. Overlap check when anything is in doubt: coverage suspect, volume conflict, no direct volume, or coverage
   unavailable.
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

The direct-build delegate (`OcctCellComplexBuilder.DirectBuild`) is a test seam. Production passes the unchanged
`TryDirectBuild`; unit tests pass a stand-in, so every path, including fail-closed, runs without OCCT.

Tests and inputs:
- `Testing/SAM.OCCT.UnitTests/SewInvalidCellGuardTests.cs` (37 tests).
- `Testing/SAM.OCCT.IntegrationTests/SewGuardInvalidCellIntegrationTests.cs` (34 tests).
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
- **Why the overlap check can be skipped** on a clean, positive sew build whose volume agrees with the direct build:
  overlapping cells add volume, and only uncovered (missing) space could offset it, which coverage would show.
  Assumption: the direct build is not missing the same volume.
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

## Validation (local, Windows, this machine; no hosted CI)

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

| Suite | Base `dc871f7` | Head `ac212bd` |
|---|---|---|
| Build | 0 errors | 0 errors. Clean rebuild: 243 warnings (unit) and 284 (integration), pre-existing analyzer, XML-doc and MSB3277 warnings; base warning counts were not captured. Only new-file warnings: 7 CA1305 on assertion-message `string.Format` in the integration test, the existing tests' idiom. None in production code. |
| Unit | 700 passed / 0 failed | **737 / 0** (+37 `SewInvalidCellGuardTests`) |
| Native integration | 261 passed / 19 failed / 2 skipped (12 min 6 s) | **295 / 19 / 2** (10 min 37 s; +34 `SewGuardInvalidCellIntegrationTests`) |

Comparison by identity, from the TRX files:
- The 19 failures are the same test names with byte-identical failure messages.
- The 2 skips are the same: the large-panel sew-guard skip and the inverse-gated native-missing test.
- No test that passed on base fails on head, in either suite.
- `PerformanceGuardIntegrationTests` (Benchmark1500) passes.

As expected, Scope 1 clears none of the 19 failures: none of them sets `ValidateSewCoverage`. They are the solver
golden and parity tests explained by SAM 173ababc in the attribution. Grasshopper contract tests and the full
solution were not built (out of scope).

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

| Panel set | Mode | Direct cells / m3 | Base: match, non-positive cell returned, silent deviation | Head |
|---|---|---|---|---|
| Revit-home current (131) | shells | 14 / 215.993 | 4/8, 3, 2 | **8/8**, 0, 0 |
| Revit-home July (105) | shells | 14 / 215.993 | 8/8, 0, 0 | 8/8, 0, 0 |
| AdjacencyCluster-home solved | shells | 18 / 601.300 | 0/8, 6, 4 | **7/8**, 0, 0 |
| Face3D-home solved | shells | 20 / 601.301 | 1/8, 3, 1 | **7/8**, 0, 0 |
| PR49-Test2 solved | shells | 44 / 6674.678 | 8/8, 0, 0 | 8/8, 0, 0 |
| Revit-home current (131) | GH path | 22 / 215.993 | 3/8, 1, 1 | **6/8**, 0, 0 |
| Revit-home July (105) | GH path | 14 / 215.993 | 8/8, 0, 0 | 8/8, 0, 0 |
| AdjacencyCluster-home solved | GH path | 18 / 601.300 | 8/8, 0, 0 | 8/8, 0, 0 |
| Face3D-home solved | GH path | 20 / 601.301 | 8/8, 0, 0 | 8/8, 0, 0 |
| PR49-Test2 solved | GH path | 44 / 6674.678 | 8/8, 0, 0 | 8/8, 0, 0 |
| **Total** | | | **56/80, 13, 8** | **76/80, 0, 0** |

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

### Runtime impact

The extra work is one native direct build per guarded call, which base made only when coverage was suspect.
The overlap check (point-in-cell, sew cells × probes) adds to it only when something is in doubt. Measured in
the same probe, sequentially, with nothing else running; build time only, coverage reporting excluded.

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

### Machine-local evidence (not committed; copy to keep)

In the owner workspace folder `SAM-BIM/.review-sew-guard-q4-20261022/` (2.4 MB, not Git-tracked): the diff and
`patches/`, the probe, solve and restore harness sources (`harness/`), raw base and head probe outputs
(`probe-runs/`: v3 head, and the final timing run for base and head), suite logs plus failure names and messages
(`suites/`), the three solved panel sets (`solved-panel-sets/`) and the staged restore copies
(`staged-sam-models-restore/`). The TRX files are in the session scratchpad only. The attribution workspace
`.review-occt-integration19-20261022/attrib/` (1.3 GB) holds `native-frozen` and `runs/{cur-1,g-1}`.

**Evidence incident (owner action needed, machine-local):** during this work an evidence-only probe run, with a
stale binary, wrote its text log over two files in `.review-occt-integration19-20261022/sam-models/`:
`02_July_Solve3D-output_105-panels.sam` and `03_Current_Solve3D-output_131-panels.sam` (now 2.5 kB text). Their
sources, `attrib/runs/{g-1,cur-1}/solved-panels.json`, are intact. Byte-equivalent regenerations, with the
exporter's own logic, names and descriptions, are staged in `SAM-BIM/.review-sew-guard-q4-20261022/staged-sam-models-restore/`: 131 and 105
panels, 0 structural differences, max vertex distance 0 against the JSON. Only the AnalyticalModel GUID differs.
Replacing the two files was blocked by the agent permission check and is left for the owner. The committed
fixtures here are separate copies of the same regeneration. The probe now refuses unknown modes and any output that
is not a new `.txt` file.

## Unresolved, risks, limitations

- **Not fully translation-invariant yet; residual cases are flagged, never silent.** On fixed panel sets, 4 of 80
  guarded builds still differ from the direct decomposition. Each is a valid sew build (positive, non-overlapping)
  that preserves volume while under-splitting. The unchanged #76 route rejects the direct build for it, either by
  its strict 1e-6 m closure test on decoded seams or by refinement ("direct cell lies in 2 sew cells"). The sew
  build is kept with `SAM_OCCT_BUILD_UNRELIABLE` (Warning):
  - AdjacencyCluster-home non-Air at (1,1,0);
  - Face3D-home non-Air at (8,0,0);
  - current Revit-home GH path at (1,1,0) and (1e-7,0,0).

  **Owner decision:** should the #76 route also judge closure at the glue band? That would very likely clear the
  first case; it relaxes a preserved check, so it is not done here.
- `ValidateSewCoverage` remains opt-in. `Solve3D`'s internal builds do not use it, so the end-to-end solver
  instability (Scope 2: 4 to 42 spaces under translation on gappy fixtures) is unchanged and was kept separate. All
  reconstruction checks here use fixed solver outputs.
- Every guarded call now pays one extra native direct build (see "Runtime impact"). Only the opt-in path pays it.
- The overlap check samples points. A partial overlap missed by every probe is still caught by the volume
  comparison when the totals disagree; it is not proven absent otherwise.
- Behaviour change for `SAMOCCT.CreateAdjacencyCluster` users: a demonstrably invalid sew build with no acceptable
  replacement now yields no cluster and a GH Error, instead of a cluster built on inverted cells. No such case
  occurred on the five fixed panel sets at the eight offsets once the replacement fixes were in.
- Fail-closed was exercised end to end on native for RetainTopology only. Missing coverage, a failed or unavailable
  direct build, and inverted or overlapping direct cells are covered with the unit-test stand-in: no natural
  native input produces them.
- Machine-local evidence (frozen native, attribution workspace, probe and solve harnesses, raw probe outputs) is
  listed below and is not committed. The cross-machine and JIT floating-point sensitivity noted in the attribution
  was not retested. The RetainTopology fail-closed integration test therefore searches the recorded offsets for an
  inverted sew cell and skips with a message if none occurs on that machine.
- Grasshopper, the full solution and the Rhino/TAS acceptance were not built or run (out of scope). The
  Grasshopper contract is unchanged.

## Next step

Independent review of this branch: diff `dc871f7..HEAD`, this record, and the evidence summary in the task
handoff. Then the owner decides on the closure band for the #76 route and on pushing and opening a PR to
`sow/2026-Q4` (hosted `build` and `spdx` must pass). Recommended reviewer: Opus, high effort, new session, Plan mode.
The review spans the decision paths, numerical tolerances and real-fixture evidence.
