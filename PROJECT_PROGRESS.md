# Project Progress

## Branch
`feat/h12-occt-provenance-stamping` (base `sow/2026-Q3` @ `2afaf029`)

## Last updated
2026-09-17

## Current status
H12 release-provenance fix implemented and locally verified (managed + native). Not yet
opened as a PR pending final report review.

## Completed
- Root `Directory.Build.targets`: stamps `AssemblyFileVersion`/`AssemblyInformationalVersion`
  only, only when CI/release supplies `SAMVersion` (validated `^\d+\.\d+\.\d+\.\d+$`, fails
  clearly if malformed), only for non-test / `GenerateAssemblyInfo=false` projects. Never
  emits `AssemblyVersionAttribute`; never touches `AssemblyVersion`/`Deterministic`/
  `GenerateAssemblyInfo`. Defines `SAM_FILEVERSION_STAMPED` in the same conditioned target
  that writes the generated file (coupled by construction).
- Guarded the 5 legacy `AssemblyFileVersion("1.0.*")` lines with
  `#if !SAM_FILEVERSION_STAMPED` in: `SAM_OCCT/SAM.Analytical.OCCT`,
  `SAM_OCCT/SAM.Geometry.OCCT`, `Grasshopper/SAM.Analytical.Grasshopper.OCCT`,
  `Grasshopper/SAM.Core.Grasshopper.OCCT`, `Grasshopper/SAM.Geometry.Grasshopper.OCCT`.
  Their `AssemblyVersion("1.0.*")` lines are untouched. The other 3 in-scope assemblies
  (`SAM.Core.OCCT`, `SAM.Analytical.OCCT.Solver`, `SAM.Geometry.OCCT.Solver`) had no
  `AssemblyFileVersion` attribute at all and needed no source edit.
- `native/SAM.Occt.Native/src/version.rc.in` (new): Windows VERSIONINFO template
  (`FileVersion`, `ProductVersion`, `ProductName=SAM_OCCT`,
  `OriginalFilename=SAM.Occt.Native.dll`, `FileDescription`). `CMakeLists.txt`: reads
  `SAMVersion`/`InformationalVersion` from the environment; when `SAMVersion` is set (and
  MSVC), validates the format, `configure_file`s the template and adds it to
  `target_sources`. Absent `SAMVersion` -> resource not built at all, zero change to local
  dev builds. No native `.cpp`/`.h`/exports/ABI/linking touched.
- `.github/workflows/build.yml`: added a `Compute SAMVersion` step (same convention as
  `SAM_Deploy/installer.yml` and sibling repos), threaded `SAMVersion`/
  `InformationalVersion` into the Restore+Rebuild step and both `dotnet test` invocations
  (tests rebuild the referenced SAM.*.OCCT projects and would otherwise silently overwrite
  the CI-stamped Release DLLs with local-dev-fallback ones), and added a final
  **Assert H12 provenance** step that checks, for all 8 SAM-owned managed assemblies:
  `FileVersion == SAMVersion`, and that `AssemblyVersion` was *not* changed (3 stay
  `0.0.0.0`, 5 stay shaped `1.0.x.y` and are never equal to `SAMVersion`). Native DLL is
  not built in this CI (`SAM_OCCT_SKIP_NATIVE_BUILD=true`, unchanged) - native provenance
  is checked later in SAM_Deploy per the approved plan.

## Decisions / assumptions
- Did not import the standard SAM `Directory.Build.props` (it also drives
  `AssemblyVersion`/`Deterministic`, forbidden here) - wrote a narrower
  `Directory.Build.targets` instead, matching the plan exactly.
- `AssemblyInformationalVersion` falls back to `$(SAMVersion)+$(SAMSourceRevision)` only
  when CI didn't already supply `InformationalVersion` directly (mirrors the standard SAM
  `_GenerateSAMVersionFile` pattern).

## Files changed
- `Directory.Build.targets` (new)
- `SAM_OCCT/SAM.Analytical.OCCT/Properties/AssemblyInfo.cs`
- `SAM_OCCT/SAM.Geometry.OCCT/Properties/AssemblyInfo.cs`
- `Grasshopper/SAM.Analytical.Grasshopper.OCCT/Properties/AssemblyInfo.cs`
- `Grasshopper/SAM.Core.Grasshopper.OCCT/Properties/AssemblyInfo.cs`
- `Grasshopper/SAM.Geometry.Grasshopper.OCCT/Properties/AssemblyInfo.cs`
- `native/SAM.Occt.Native/CMakeLists.txt`
- `native/SAM.Occt.Native/src/version.rc.in` (new)
- `.github/workflows/build.yml`

## Validation (all run locally on this machine, real builds - not simulated)
- **Baseline capture**: snapshotted the pre-existing `build/` output (matches
  `sow/2026-Q3` @ `2afaf029`, the frozen SAM_OCCT pin) before any rebuild. Confirmed the
  defect exactly as scoped: 5 assemblies report `FileVersion`/`ProductVersion` as the
  literal string `"1.0.*"` (CLR does not expand the wildcard for `AssemblyFileVersion`),
  `AssemblyVersion` `1.0.9755.33908`/`...33909` (time-derived); 3 assemblies are
  `0.0.0.0`/`0.0.0.0`/`0.0.0.0` for all three fields.
- **Local dev regression check** (`msbuild SAM_OCCT.sln /t:Rebuild`, no `SAMVersion`):
  build succeeded (exit 0, no errors). All 8 assemblies unchanged in *kind* of value: 5
  still literal `"1.0.*"` FileVersion/ProductVersion with a new time-derived
  `AssemblyVersion`; 3 still `0.0.0.0` across the board. Public-API reflection dump
  (types + public/protected members via `System.Reflection`, all 8 assemblies) is
  **byte-identical** to the baseline dump except for the expected time-derived
  `AssemblyRef` version numbers on `SAM.Analytical.OCCT`/`SAM.Geometry.OCCT` - zero type,
  member, or signature differences.
- **Release stamping check** (`msbuild SAM_OCCT.sln /t:Rebuild
  /p:SAMVersion=2026.3.214.0 /p:SAMSourceRevision=testh12abc`): build succeeded. All 8
  assemblies: `FileVersion=2026.3.214.0`, `ProductVersion=2026.3.214.0+testh12abc`.
  `AssemblyVersion` unchanged in kind (3x `0.0.0.0`, 5x `1.0.x.y`, none equal to
  `SAMVersion`). Same public-API reflection diff against baseline: zero differences beyond
  the expected time-derived `AssemblyRef` numbers.
- **Native regression check** (`build-native.ps1`, no `SAMVersion`): build succeeded.
  `dumpbin /exports` and `/dependents` identical to the pre-existing baseline
  `SAM.Occt.Native.dll` (byte-for-byte tool output, ignoring the dumped file path itself).
- **Native release stamping check** (`build-native.ps1`, `SAMVersion=2026.3.214.0`,
  `InformationalVersion=2026.3.214.0+testh12abc`): build succeeded (re-linked to add the
  resource). `FileVersionInfo` on the output: `FileVersion=2026.3.214.0`,
  `ProductVersion=2026.3.214.0+testh12abc`, `ProductName=SAM_OCCT`,
  `OriginalFilename=SAM.Occt.Native.dll`. `dumpbin /exports` identical to baseline (same
  ordinal/name list). `dumpbin /dependents` identical to baseline (same DLL import list).
  Only the added `.rsrc`/VERSIONINFO differs, as expected. No `.cpp`/`.h` source touched
  (confirmed via `git diff --stat sow/2026-Q3` - only `CMakeLists.txt` and the new
  `version.rc.in`).
- **CI assertion dry-run**: extracted the new "Assert H12 provenance" PowerShell block and
  ran it standalone against the real local build tree - PASSES with
  `SAMVersion=2026.3.214.0` (as above), and correctly FAILS (throws, lists every mismatch)
  when given a deliberately wrong `SAMVersion=9999.9.999.0`.
- Full `git diff --stat sow/2026-Q3`: 6 files modified (+46/-0 total in the 5
  `AssemblyInfo.cs` + `CMakeLists.txt`), 2 new files. No application `.cs`/`.cpp`/`.h`
  source touched anywhere.

## Known non-blocking issue found during validation (OUT OF SCOPE - not fixed)
`Grasshopper/SAM.Core.Grasshopper.OCCT/SAM.Core.Grasshopper.OCCT.csproj` has a
pre-existing (not introduced by this branch - confirmed via
`git diff sow/2026-Q3 -- <path>` = empty) `OutputPath` typo for `Release|AnyCPU`:
`..\build\` (i.e. `Grasshopper\build\`) instead of `..\..\build\` (i.e. repo-root
`build\`, which is what its own `Debug|AnyCPU` config and every sibling project uses).
Effect: a Release rebuild of this one project lands in `Grasshopper\build\`, not
`SAM_OCCT\build\`, so a stale/older copy can be left sitting in `SAM_OCCT\build\`.
This fix's stamping mechanism works correctly on the project regardless (verified above by
reading its actual Release output location) - this is a separate, unrelated packaging-path
defect, not a provenance defect, and per the H12 scope lock it was **not** fixed here.
Flagged for the owner / a separate follow-up; the SAM_OCCT CI's new H12 assertion step
searches recursively so it is not fooled by this. **This may also affect what SAM_Deploy's
installer actually packages for this one DLL - worth checking when SAM_Deploy's
whole-payload audit script runs.**

## Issues / blockers
- None blocking. Native SAM_OCCT_SKIP_NATIVE_BUILD stays `true` in CI (native not built
  there); native provenance is proven locally here and will be checked again by the
  SAM_Deploy audit script against the real installer payload.
- See "Known non-blocking issue" above - not a blocker for this PR, flagged for follow-up.

## Next step
- Open PR `feat/h12-occt-provenance-stamping` -> `sow/2026-Q3` in `SAM-BIM/SAM_OCCT`.
- After merge: SAM_Deploy follow-up (bump SAM_OCCT pin, add
  `.github/scripts/audit-payload-versions.ps1`, wire it into `installer.yml`, correct
  `RELEASE_VALIDATION.md` H12 wording + run-210 erratum) per the approved plan.
