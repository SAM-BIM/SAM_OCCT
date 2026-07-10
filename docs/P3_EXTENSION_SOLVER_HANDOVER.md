# P3 Extend3D — decisions & outcome

**Branch:** `feat/cw-p3-extend3d` off `sow/2026-Q3` · **Base:** `c00e3d3` (P2, PR #59)
**Status:** complete — all suites green (unit 572, integration 200 + 2 native-inverse skips, golden masters
15/15 byte-identical with flags off). This note records the two decisions that shaped the phase; the
per-parameter behaviour lives in `docs/Modeling-Guide.md` and the test contract in `TESTING.md`.

## What shipped

1. **Directional cap growth** (`SnappedPanel.GrowEdgesToWalls`, gated by `Panel3DSnapSolver.DirectionalCapGrow`
   / GH `directionalCapGrow_`, default **false**): per-edge, evidence-based cap growth. Each straight external
   cap edge grows only by its own measured gap to a wall that actually faces it; an edge with no facing wall in
   reach grows **exactly 0** (the D4 false-floor guard — a cap bordering a double-height void is never pushed
   into it). Fail-closed: on no evidence or a failed/self-intersecting reconstruction it falls back to the
   legacy uniform grow and flags the record `LegacyUniformCapGrow`. Holes are preserved.
2. **Extend skip/risk diagnostics** (`ExtendRecord.Outcome`, `ExtendSkipReason`, `ExtendRiskFlag`): real
   decision points that leave a panel untouched are recorded as `SAM_OCCT_EXTEND3D_SKIP:` (never a silent
   no-op); applied moves keep the **frozen** `SAM_OCCT_EXTEND3D_PANEL:` line and gain
   `SAM_OCCT_EXTEND3D_RISKY:` metadata lines.
3. **Clean-side over-merge warning** (`LevelFrame.OverMergeSpreadWarning` = 0.25 m, `DiagnosticCode.LevelGroupOverMerge`):
   a level group whose member frames span ≥ 0.25 m gets a warning — visibility on a wide `bucketBetweenLevels`
   merge, never a block.
4. **Input-effect transparency** (`SAM_OCCT_EXTEND3D_INPUT_INERT` / `_INPUT_OVERRIDDEN`): a changed GH input
   that had no geometric effect on a run is reported with why — `inputAlreadyClean = true` makes the
   clean-stage inputs inert and `bucketBetweenLevels_` reporting-only; a per-panel `BucketSize` stamp overrides
   `minBucketSize_`/`thicknessFactor_`. Pinned by `InputEffect_ChainedRunInputAlreadyClean_…`.

## Decision 1 — MaxExtend stays the flat 0.4 m default (derivation rejected)

The plan's first §5.6 wording asked `Modify.ResolveMaxExtends` to mirror `ResolveWeights` and derive an
unstamped panel's reach via SAM_Solver's `SetMaxExtends`. That derivation **pre-caps the reach at 0.49× the
panel's own in-plane length** (`EXTENSION_LIMIT_LENGTH_RATIO`, measured on a horizontal slice), which crushes
short/segmented wall panels to ~0.1 m and **regressed managed-pipeline closure** on the golden masters
(`whole-level-tilted` 22 → 16 cells, new naked edges; confirmed across `two-level-tilted`,
`whole-level-towers`, and the parity/plane-target suites).

Per the plan's own step-5 rule (*regression → keep the default*) and the "do not change defaults" directive,
the derivation was **not adopted**: the unstamped fallback stays the flat **0.4 m** default, byte-identical to
pre-P3. MaxExtend is a **per-panel tuning knob** (`SolverParameter.MaxExtend` via SolverProperties), and the
0.49× length-ratio cap applies only at the lateral extension operation itself — its intended home, and where
it already lived. The `EXTEND3D_SKIP`/`_RISKY` records (`CappedByLengthRatio` / `MaxExtendLimited` /
`LengthRatioLimited`) tell a user whether raising the stamp will help, so the tuning is never a guess. Guarded
by `MaxExtendDerivationTests` (unstamped → 0.4/`Default`) and the byte-identical golden masters. The workaround
for SAM_Solver's `Thickness()` null-`Construction` bug was dropped with the derivation (a separate report to
that repo is still warranted for its own sake).

## Decision 2 — merged-plane targeting is asserted on caps, not on overshooting wall tops

The 9-space fixture proves the D3 fix by asserting every floor/roof **cap** normalizes onto a group datum
(12.24 / 15.29 / 18.34) and never a raw skin (12.436 / 15.473). A wall whose original modelled top sat at a
raw skin (e.g. North0's 15.473 roof, now normalized to the 15.29 cap) legitimately **overshoots** its lowered
cap in the pre-resolve Extend3D view — Extend3D only grows walls, never shortens them, and the native
MakerVolume split in Solve3D trims that overshoot back to the cap. So the guarantee lives on the caps (the
extend targets), not on overshooting wall tops; `ExtendTargets_FixtureChainedRun_CapsNormalizeToGroupDatumsAndWallsReachThem`
pins it. `directionalCapGrow = true` additionally keeps the double-height column free of any false floor at
15.29 inside West3's footprint.

## Follow-ups for later phases

- P4 hard nine-space acceptance runs the full chain to the adjacency cluster + SpaceMatcher; the overshoot
  above is expected to be trimmed by the native split there.
- SAM_Solver `Thickness()` inverted null-check (non-air panel, null `Construction`) — report/fix in that repo.
