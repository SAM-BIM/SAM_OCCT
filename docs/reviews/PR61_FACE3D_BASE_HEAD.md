# PR #61 Face3D-Home Base/Head Parity Evidence

**Review Date:** 2026-07-13
**Base SHA:** c643b29c55fb5ce84f74521e236a80204109ba25
**Head run SHA:** 171e4a54a53644b1effded5638b5a4c894eba899 (code-complete PR commit; the
final PR head adds only documentation/evidence files and comment-only test doc corrections
on top of this SHA — no assertion or production-code changes; verify with
`git diff 171e4a5..HEAD -- '*.cs'`)
**Status:** SOLVER AND A-WORKFLOW PARITY CONFIRMED; B-WORKFLOW DELTA DISCLOSED (20 → 19)

---

## Method

Both runs executed the identical measurement pipeline
(`PR61Face3DHomeParityTests.Face3DHome_Parity_PR_Branch_RecordsCurrentState`:
Solve3D raw → A-solver-matched rebuild → B-clean-extend with band=0.21) in two separate
git worktrees against the **same copied native DLL**. PR #61 contains no native-source
changes (`git diff c643b29..head -- '*.cpp' '*.h' CMakeLists.txt build-native.ps1` is
empty), so a single kernel binary is the correct control.

The test file was copied verbatim from the PR head into the base worktree; the head copy
additionally pins the head-state expectations (`solver=20`, `A=19`, `B=19`). The base run
records `B=20` in its console output (pinning `B=19` at base would — correctly — fail).

## Environment (identical for both runs)

| Item | Value |
|------|-------|
| Native DLL SHA256 | `81CDABA60E5E0CF270371FDB3DED4C7E6201EF38B23D0D56AF9EEFD508DBBD4F` |
| SAM dependency SHA | `a31e996f103e769cde616a8680a3512e56fcf2ef` (`..\SAM`, `sow/2026-Q3`) |
| SAM_Solver dependency SHA | `3155a13135f5abf5afbace29f6a8b72a79d52dcc` (`..\SAM_Solver`, `sow/2026-Q3`) |

## Evidence logs (committed, real executions)

| Run | Commit | Log |
|-----|--------|-----|
| Base | c643b29 | `docs/reviews/evidence/PR61_FACE3D_BASE.log` |
| Head | 171e4a5 | `docs/reviews/evidence/PR61_FACE3D_HEAD.log` |

---

## Results

| Metric | Base (c643b29) | Head (171e4a5) | Verdict |
|--------|----------------|----------------|---------|
| Solver raw cell count | 20 | 20 | **Parity** |
| Solver naked points | 4 | 4 | Parity |
| A-solver-matched spaces | 19 | 19 | **Parity** (under-close pre-existing) |
| B-clean-extend spaces (band=0.21) | **20** | **19** | **PR #61 delta — disclosed** |

- The A-workflow under-close (19 spaces vs 20 solver cells) exists identically at base and
  head: it was introduced by the E2 plane-targeting change merged at c643b29 (PR #60) and
  is **not** a PR #61 regression.
- The B-workflow (Clean3D → Extend3D → Create.AdjacencyCluster) yields **20 spaces at base
  and 19 at head**: the coplanar-cap coalescing pass added in PR #61 under-closes one
  Face3D-home space on this path. An earlier revision of this document claimed "PR #61
  does not alter the Extend3D/B-clean-extend path" — that claim was wrong and is
  withdrawn. The head value is pinned (`Assert.Equal(19, spacesB)`) so further movement
  is caught, and the delta is disclosed in the PR body.

---

## Towers Quantitative Validation

Test: `PR61TowersQuantitativeValidationTests` — 4 passed / 0 failed / 0 skipped.
Log: `docs/reviews/evidence/PR61_TOWERS_VALIDATION.log`
Pipeline: `Extend3D(band=0.4, fill=0.4, dir=false, bucket=0.4, align=0.3, doubleWallGap=g)`
→ `Create.AdjacencyCluster` on `whole-level-towers.sam`.

| Metric | Gap 0 (baseline) | Gap 0.4 (**accepted**) | Gap 0.5 (over-aggressive) |
|--------|------------------|------------------------|---------------------------|
| Cell count | 31 | 30 | 29 |
| Total volume (m³) | 9605.396 | 9620.406 | 9552.766 |
| Volume drift vs baseline | — | +0.156% | −0.548% |
| Cell-derived floor area (m²) | 3160.075 | 3168.220 | 3142.820 |
| Level-datum floor area (m², Z≈12.24) | 1336.727 | 1335.811 | 1330.101 |
| Floor-area drift vs baseline (level-datum) | — | −0.069% | −0.496% |
| Sliver count (<3 m³) | 2 (1.095 / 2.738) | 0 | 0 |
| 22↔26 adjacency | False | True | True |

**Gap 0.4 (+15.010 m³) — geometric attribution.** The two sliver cells are absorbed
volume-preservingly into their neighbour (70.628 → 74.461 = +3.833 ≈ 1.095 + 2.738); the
oversized 157.574 m³ north-strip cell splits **exactly** into the two legitimate pair rooms
(78.049 + 79.526 = 157.574); and the net volume increase is reclaimed double-wall void
volume of two legitimate rooms: +7.610 m³ (east-tower room) and +7.427 m³ (block-room 22 —
the growth that makes it share a face with tower 26). Every removed cell is either a
sub-threshold sliver or the volume-preserving split parent: **no legitimate room is lost,
room topology is preserved (and gained by the split)**. All of this is asserted, not
narrated (`Towers_Gap04_NoCollapse_QuantitativeValidation`).

**Gap 0.5 — why it is over-aggressive.** The north-strip wall pair sits 0.474 m apart
(`Towers_NorthStripPair_GapSweep_MergedAt05_NearMissAt047`; gap 0.47 leaves 30 cells, gap
0.5 merges the pair). Consolidating that pair leaves the **west north-strip room (78.049 m³,
28.809 m² floor — 26× the sliver threshold) unclosed: it drops out of the cell complex
entirely**, while its pair sibling survives with unchanged volume (79.526 m³) — the room is
destroyed, not merged, and raising `fillMargin` to 0.5 does not rescue it (still 29 cells,
`Towers_DoubleWallGap_FillMargin_DefectDiagnostic`). The −67.640 m³ delta vs gap 0.4 is
fully attributed: −78.049 (destroyed room) + 10.408 (unrelated southeast consolidation
growth). Floor area at the affected level drops −0.496% vs the baseline's −0.069% at
gap 0.4. Gap 0.4, by contrast, touches only artifacts (sub-3 m³ slivers and one exact
volume-preserving split). Asserted in `Towers_Gap05_OverAggressive_QuantitativeValidation`
and `Towers_Gap04Vs05_QuantitativeDelta`. **The accepted towers setting remains gap 0.4.**

---

## Commands

```powershell
# Base worktree
git worktree add ..\SAM_OCCT-wt61-base c643b29c55fb5ce84f74521e236a80204109ba25
robocopy build ..\SAM_OCCT-wt61-base\build /E          # identical native DLL
copy Testing\SAM.OCCT.IntegrationTests\PR61Face3DHomeParityTests.cs `
     ..\SAM_OCCT-wt61-base\Testing\SAM.OCCT.IntegrationTests\
cd ..\SAM_OCCT-wt61-base
dotnet build Testing/SAM.OCCT.IntegrationTests -c Debug
dotnet test Testing/SAM.OCCT.IntegrationTests -c Debug --no-build --filter "FullyQualifiedName~PR61Face3DHomeParity" --logger "console;verbosity=detailed"

# Head worktree (same steps at the head SHA, no test-file copy needed)
git worktree add ..\SAM_OCCT-wt61-head <head SHA>
robocopy build ..\SAM_OCCT-wt61-head\build /E
cd ..\SAM_OCCT-wt61-head
dotnet build Testing/SAM.OCCT.IntegrationTests -c Debug
dotnet test Testing/SAM.OCCT.IntegrationTests -c Debug --no-build --filter "FullyQualifiedName~PR61Face3DHomeParity" --logger "console;verbosity=detailed"
```

---

Generated by Michal Dengusiak & Codex
