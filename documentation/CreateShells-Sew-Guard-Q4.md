# SAMOCCT.CreateShells sew guard - change record (2026-10-23)

Branch `fix/towers-residual-closure-q4`, published to draft SAM-BIM/SAM_OCCT#82 (base `sow/2026-Q4` at `819962df`;
ancestry in `Towers-Residual-Closure-Q4.md`, "Dependency"). Owner-approved for publication on 2026-10-23, version
0.4.0 kept. Not merged.
- **Commit `804ead6` is this change and the tested content.** It is `229f971` plus the managed-path revision
  `9441a07` (the sealed three-file diff, SHA256 `4d7ad14ab2b495211d247ee10df8cee6dfda6093358c64391a908083e78d3a18`,
  committed byte-identical) plus this change.
- That seal does not cover the `TESTING.md` edits made here (see "Documentation revision").
- Later commits change records only.

`PROJECT_PROGRESS.md` is not touched.

Evidence (machine-local, outside the repository; copy it before switching machines):
`SAM-BIM/.createshells-sew-guard-20261023/` (README, harness, before/after exports, logs, TRX).

## Problem

`SAMOCCTCreateShells` built its options from the user's sewing inputs without `ValidateSewCoverage`, so a sew build
was adopted unchecked. `SAMOCCTCreateAdjacencyCluster` already enables the SAM_OCCT#81 guard. On the forced-managed
towers outputs (the golden masters' call, non-air panel faces), with the component's own options (every voluntary
input at its default, `sewBeforeBuild_ = true`):

| Chain | Component before | Guarded reference (same options + guard) |
| --- | --- | --- |
| bucketBetweenLevels 0 | 20 shells, 7002.716 m³; **west room (14, -5, 13.8) in no shell**; `Successful` true, no warning | 24 cells, 9039.793 m³; west 159.479 m³ and middle 113.717 m³ in separate valid cells |
| bucketBetweenLevels 0.21 | 22 shells, 6271.173 m³ (five cells lost; the six named rooms survive, the west room with a different boundary); `Successful` true, no warning | 27 cells, 9400.475 m³ |

The guard rejects both sew builds (incomplete coverage and a volume conflict) and adopts the direct build, which
passes its own closure, volume, sliver, overlap and coverage checks (`SAM_OCCT_BUILD_FALLBACK_DIRECT`, a Warning).

## Change

`Grasshopper/SAM.Geometry.Grasshopper.OCCT/Component/SAMOCCTCreateShells.cs` only:
- `ValidateSewCoverage = true` in the component's `OcctBuildOptions` (consulted only when `sewBeforeBuild_` is true);
- `LatestComponentVersion` 0.3.1 -> 0.4.0 (a changed computed result is a behavioural change, GH_COMPONENT_STANDARDS
  GH-F2/H2; minor chosen, owner to confirm);
- the `sewBeforeBuild_` description (GH-D5) states the check and each outcome: conditional direct replacement,
  retention of a valid suspect sew build with a warning, fail-closed on an intrinsically invalid one, and the
  unchecked automatic retry with sewing off. The production comment states the same.

Unchanged: GUID, name, category, every input and output (name, type, order, access, optional, visibility,
persistent default), `OcctBuildOptions` defaults, the guard itself, closure rules, tolerances, goldens, SAM_OCCT
library code, native.

## Behaviour after the change

- Sewing off (input absent, or `false`): unchanged; output key-identical to the direct build.
- Valid sew build: adopted unchanged (`SAM_OCCT_SEW_SUCCESS`, no warning); two boxes sharing a wall.
- Lossy sew build with a valid direct build: the direct build, `Successful` true and a runtime Warning
  (`SAM_OCCT_BUILD_FALLBACK_DIRECT`). Towers: 24 / 27 shells, key-identical to the guarded reference.
- Invalid sew build with no valid replacement: the guard returns an Error (`SAM_OCCT_BUILD_REJECTED`) and no cells,
  so the component reports an Error, no shells and `Successful` false. The component's mapping of a failed build is
  tested (boxes without tops: `SAM_OCCT_NATIVE_FAILED`, no shells, `Successful` false); the guard's fail-closed
  branch itself is covered by the SAM_OCCT#81 library tests (no component-level input for it was found).
- A valid but suspect sew build (faces cutting through or outside its cells, or a volume conflict) is replaced
  only when the guard proves the direct build acceptable. Otherwise it is kept and `Successful` stays true, as in
  `SAMOCCT.CreateAdjacencyCluster`: with a runtime Warning (`SAM_OCCT_BUILD_UNRELIABLE`,
  `SAM_OCCT_SEW_VOLUME_CONFLICT`), or only an Info (`SAM_OCCT_SEW_UNUSED_FACES`) when the sole issue is unused
  faces. The guard therefore does not guarantee that no room is lost; it guarantees that no invalid cell is
  returned and that a kept suspect build is reported in Diagnostics.
- `sewBeforeBuild_` false: the automatic sew retry after a hard direct-build failure stays unchecked.

## Tests (`Testing/SAM.OCCT.GrasshopperTests`)

- **`CreateShellsSewGuardComponentTests`** (new; the native cases skip without `SAM.Occt.Native`). The real
  component runs its own `SolveInstance` on Grasshopper's own data access. Only what needs a running Rhino is
  replaced: input values are given as volatile data, and output writes are recorded as the component passed them.
  Voluntary inputs are inserted from the component's own declaration. The reference is `Create.Shells` with the
  component's options plus the guard. Cases:
  - towers b = 0 / 0.21: west and middle rooms in separate shells at 159.479 / 113.717 and 174.145 / 119.364 m³;
    24 / 27 shells key-identical to the reference; `OcctUnderSplitGuard.ValidateCells` (by reflection, internal)
    passes; `Successful`, the fallback Warning;
  - sewing off, absent and `false`: 24 shells, key-identical to the direct build, no warning;
  - two boxes sharing a wall: the valid sew build is kept, key-identical to the unguarded build, no warning;
  - boxes without tops: Error, no shells, `Successful` false;
  - declared inputs: names, access, visibility and persistent defaults unchanged.
- `DiagnosticsContractTests.LatestComponentVersions_AreBumpedForTheChange`: CreateShells pinned at 0.4.0.
- Test infrastructure (needed to run unattended):
  - `GrasshopperTestAssembly` installs an empty `Instances.ComponentServer` when Grasshopper loads (by reflection).
    Outside Rhino, constructing any SAM component otherwise scans the Rhino plug-in folders through `rhcommon_c`
    and shows a modal "Grasshopper Loading Error" dialog. At `229f971` that dialog blocked this project's run: one
    test passed, then the host was killed at the 10-minute timeout ("Test host process crashed").
  - `NativeRuntime` widens PATH to `<repo>/build`, like the integration project, and probes the native library.
  - The csproj adds `Xunit.SkippableFact` and links the towers fixture.

**Fails before the correction** (production file restored from HEAD, `before-component-dll.sha256`): 33 tests,
30 passed, 3 failed, each for the intended reason:
- b = 0: "the west room point (14, -5, 13.8) lies in 0 of 20 shell(s), expected exactly one";
- b = 0.21: "the west room's shell (10 faces) is not a cell of the guarded reference";
- the version pin.

All controls passed. **After:** 33 / 33.

## Validation (local, Windows; native frozen, ABI 4)

Tested content: `229f971` + the reviewed three-file revision + this diff. Native `SAM.Occt.Native.dll` 438089F8...,
`TKBO.dll` 1FA732FE..., unchanged throughout. Component DLL before `3c8293c9...`, after `2feee4aa...`.

| Check (local) | Result |
| --- | --- |
| Builds (`--no-incremental -m:1`, skip native): Grasshopper tests / unit / integration | 0 errors; 173 / 243 / 284 warnings (baseline counts; nothing added) |
| `SAM.OCCT.GrasshopperTests` before (production file = HEAD) | 33 = 30 passed / 3 failed (intended, above) |
| `SAM.OCCT.GrasshopperTests` after | **33 / 33** |
| Unit, full | 1032 / 0; identical to the reviewed revision run (`compare-trx.ps1`) |
| Native integration, full (10.2 min) | 496 = 469 / 25 / 2 not executed; vs the reviewed revision run: nothing new, nothing resolved, 27 non-passing with the same name and message |
| Component harness, towers b = 0 / 0.21, sewBeforeBuild_ true | before 20 / 22 shells (unguarded, west lost at 0); after 24 / 27, geometry-identical to the guarded reference, west + middle separate, `ValidateCells` 0 failures, `Successful` + fallback Warning |
| Harness controls | sewing off (absent / false) 24 / 27, identical to the direct build before and after; boxes: valid sew kept, identical before and after |
| Exports before vs after by geometry (`samkeys-before-after.txt`) | only `component-sew-shells` on towers differs; solved panels, references, sewing-off and boxes outputs identical |

The towers inputs are the non-air panel faces of `Solve3D(forceManagedPipeline: true, bucketBetweenLevels: b)`, all
other arguments default (the golden masters' call); solved panels are geometry-identical between the runs. The
integration project does not reference the Grasshopper assemblies, so its result is an unchanged-library check.
Commands and evidence: `.createshells-sew-guard-20261023/README.md`.

## Compatibility

- Saved definitions keep their wires and values (GUID, inputs and outputs unchanged). They pick up the guard on
  recompute; the version bump only adds the *Update component* offer (`ObsoleteSeverity.Standard`).
- Results change only with `sewBeforeBuild_` true and only when the guard rejects the sew build. The sew build is
  then replaced by the direct build (different cells, a Warning), or the run becomes an Error with no shells, which
  was previously reported as success. A valid sew build is unchanged. A valid but suspect sew build that the guard
  keeps is unchanged geometrically, but now carries a Warning (or an Info for unused faces only).
- Cost: a sewing run now also makes a direct build and the guard's checks (towers: about a second per chain here).
- Not covered: the automatic sew retry after a hard direct-build failure (sewing off, status 30/40) remains
  unguarded. There is no direct build to compare against there, and changing it would alter the sewing-off path.

## Documentation revision (after the independent review, 2026-10-23)

Review: `SAM-BIM/.review-createshells-independent-20261023/REVIEW.md` (machine-local). Verdict REVISE, for wording
and testing documentation only; the option change was supported, with no algorithmic finding.
1. The `sewBeforeBuild_` description and the production comment overstated the protection. They now state the
   conditional replacement, the retention of a valid suspect sew build with a warning, fail-closed on an
   intrinsically invalid one, and the unchecked automatic retry with sewing off. Guard semantics are unchanged.
2. `TESTING.md` contradicted the change. Now updated:
   - the layout row: six native-gated behaviour cases, the headless component-server substitution, not run by CI;
   - the run command comment and the CI paragraph;
   - the guard section: its consumers (CreateAdjacencyCluster; CreateShells from 0.4.0 with `sewBeforeBuild_`),
     its outcomes, a `CreateShellsSewGuardComponentTests` entry with the harness limits, and a filter command.

Unchanged by this revision: the option, version 0.4.0, guard semantics, parameters, defaults, geometry, tests,
goldens, CI.

The original seal is preserved and does not cover this revision. `REVIEWED-REVISION-preserved.diff` (SHA256
`4d7ad14a...`) and `doc-revision/TESTING.sealed.md` (the sealed `TESTING.md`) are in the evidence folder. The
`TESTING.md` delta of this revision is `doc-revision/TESTING-delta.diff`. Hashes:
- sealed `TESTING.md` `f346d15a...`; revised `TESTING.md` `e497bdb1...`; delta `829c6786...`;
- the current three-file diff (sealed files with the revised `TESTING.md`) `b64dd5ba...`. The towers test blob is
  still `b029443f...`, and the towers-record part of the diff is identical to the seal (`d7960983...`);
- component DLL after this revision `60290eeb...` (description text only; native `438089f8...` unchanged).

Validation of this revision: Grasshopper test build 0 errors / 173 warnings (baseline); `SAM.OCCT.GrasshopperTests`
**33 / 33** (`gh-docrev.trx`). Only description and comment text changed in production code, so the full unit and
native runs above were not repeated.

## Open

- Reviews (machine-local): `SAM-BIM/.review-createshells-independent-20261023/REVIEW.md` (REVISE, wording only,
  resolved) and `DOC-REVISION-REVIEW.md` (ACCEPT). The owner then approved publication with version 0.4.0.
- `SAM.OCCT.GrasshopperTests` is not run by CI (`build.yml` runs the unit and integration projects only). CI builds
  `SAM_OCCT.sln`, which compiles the project, so the hosted Build check covers compilation and package restore
  only.
- The towers record's unguarded-consumer risk is reconciled: explicit sewing in CreateShells is guarded; the
  automatic retry with sewing off, the golden recount and direct API callers without `ValidateSewCoverage` remain
  unchecked.
- Not covered by a component fixture: the guard's no-valid-replacement branch (library-level only), `ComputeData`
  scheduling, output Goo conversion, saved-definition round trips and the canvas.
- Next step: owner review of draft #82 and its hosted checks at the final head. No merge or deployment is authorised.
