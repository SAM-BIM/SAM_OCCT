# SAM Grasshopper icon redesign — SAM_OCCT PR record

Branch `feature/sam-gh-icon-redesign-q4`, based on `sow/2026-Q4` @ `01a528fa`. PR: SAM-BIM/SAM_OCCT#73. Q4 migration of SAM-BIM/SAM_OCCT#71 (`feature/sam-gh-icon-redesign` @ `5f2f9ab`, based on `sow/2026-Q3` @ `e9b453b`, kept open for provenance): the same commits replayed onto `sow/2026-Q4`; Q3 history was not imported.
Propagates the SAM icon design system from SAM-BIM/SAM#166 (head `cf4d924a`, open, not merged) to this repository.

## Current status
All **37** Grasshopper objects in this repo (36 components + 1 params) use redesigned icons: **37 / 37**.
Built and validated; ready for review. **Not merged.**

## Work completed
- `design/grasshopper-icons/`: the shared SAM-BIM icon kit. `icons.py`, `render.py`, `sam_classify.py` and `ICON_DESIGN_SYSTEM.md` are vendored **verbatim** from SAM#166 (hash-checked). `icons_ext.py` and `ICON_DESIGN_SYSTEM_EXT.md` are the frozen SAM-BIM extension v1 (identical in every SAM-BIM repo). `tools/repo_rules.py` holds this repo's explicit decisions.
- **Inventory**: `tools/inventory.py` parses C# source (every non-abstract class declaring `ComponentGuid`).
- **Manifest** (source of truth): `manifest.json` / `manifest.csv` — per object: GUID, class, source, project, object glyph, operation, modifiers, icon id, resource, glyph/badge origin.
- **Generation**: 30 canonical SVGs → 24×24 PNGs; review sheet `review/contact_sheet.png` (native 24 px on GH normal / orange-warning / dark bodies + 3×) and `review/REVIEW.md`.
- **Integration**: each project's existing mechanism; only the icon token inside each `Icon` getter changes.

| Project | Objects | Icon resources | Mechanism |
|---|---|---|---|
| `SAM.Analytical.Grasshopper.OCCT` | 14 | 12 | holder / SAMOCCTIcon |
| `SAM.Geometry.Grasshopper.OCCT` | 23 | 18 | holder / SAMOCCTIcon |

## Design reuse
- **Reused SAM object families (6)**: `cluster`, `face`, `panel`, `sectionBox`, `shell`, `space`
- **New SAM-BIM ext v1 families used (2)**: `algorithm`, `cellComplex`
- **Verbs**: `calculate`, `create`, `difference`, `export`, `extend`, `get`, `import`, `intersect`, `merge`, `modify`, `offset`, `run`, `section`, `split`, `triangulate`, `union`, `update`, `validate`; new ext verb: `run`
- Distinct icons: **30** (27 on SAM glyphs, 3 on ext glyphs). Icon ids shared with SAM render pixel-identically to SAM's.

## Decisions and assumptions
- Grammar, palette, badge families and construction rules are unchanged (SAM#166). No text, no new colours.
- Qualifier variants (`…By<X>`) share an icon intentionally (see `review/REVIEW.md`).
- Interop direction: external → SAM = import ↓, SAM → external = export ↑.
- Integration follows this repo's own icon mechanism (`SAMOCCTIcon` manifest-resource holder, no resx): generated `SAM_GH_*` properties plus a `LoadIcon` helper inside `// <sam-gh-icons>` markers, and one `EmbeddedResource` line for `Resources/Icons/SAM_GH_*.png` per GH csproj. The existing `SAM_OCCT24` icon/resource is kept.
- Shell booleans reuse SAM's own geometry mapping (union plural, intersect, difference, split, section, offset); STEP/IGES = shell import / export (the format is the qualifier).
- 3D panel solver: `Solve3D` = cluster + ext `run`; `AutoTune3D` = ext `algorithm` + run; `AutoTune3D (Discover)` = algorithm + get; `Clean3D` / `Extend3D` = panels modify / extend. `CellComplex` param = ext `cellComplex` (shared with SAM_Topologic).
- Legacy icon resources are kept (still referenced by context menus / AssemblyInfo); no GUID, name, nickname, category, subcategory, parameter or behaviour change.

## Files changed
- New: `design/grasshopper-icons/**`, `<project>/Resources/Icons/SAM_GH_*.png`, `docs/GH-IconRedesign.md`.
- Modified: 39 component/param `.cs` files (one icon token each), 2× `.csproj` (one EmbeddedResource line for the icon folder).

## Validation
| Check | Result |
|---|---|
| `tools/classify.py` | 37 classified, 0 unclassified |
| `tools/build.py` identical-pixel collision check | 0 groups (30 distinct icons; 5 intentionally shared icon(s) for qualifier variants, listed in `review/REVIEW.md`) |
| Icon ids shared with SAM#166 vs SAM's `png/24` | 16 shared, 16 byte-identical |
| `tools/integrate.py` re-parse | 37/37 objects reference their `SAM_GH_*` resource; every PNG exists |
| `tools/check_source.py` vs `origin/sow/2026-Q3` | vendored files OK; icon-token swaps: 37, non-icon changes: 0; base 37, now 37 -> UNCHANGED |
| `dotnet build SAM_OCCT.sln -c Debug` | Build succeeded, 0 errors |
| `tools/check_assemblies.py` | every assembly embeds every required 24×24 icon → OK |
| `tests/GhIconTest` (real Rhino 8 / Grasshopper, Rhino.Testing) | 37/37 objects verified: 37 load in real Rhino 8 / Grasshopper by GUID (name/category match); icon = manifest PNG (max diff 2 levels: the holder's `new Bitmap(bitmap)` copy adds GDI+ premultiplied rounding; tolerance 2) |
| `Testing/SAM.OCCT.UnitTests` | 631/631 passed |
| `Testing/SAM.OCCT.GrasshopperTests` | **not completed**: the test host hangs idle (about 1 s CPU) until the timeout. Identical on the untouched `sow/2026-Q3` base, checked out and run the same way (exit 124 after 15 min). Pre-existing/environmental, not caused by this PR; the real-Rhino `GhIconTest` covers every OCCT GH object instead. |
| Visual review (`review/contact_sheet.png`, 24 px on normal / warning / dark bodies) | all icons legible; no collisions |

## Unresolved issues / risks
- `SAM.OCCT.GrasshopperTests` hangs in this environment on both the base branch and this branch (pre-existing); worth its own investigation.
- Built against sibling repos as checked out locally (SAM on `feature/sam-gh-icon-redesign` = SAM#166); icon changes are API-neutral.

## Recommended next step
Review this PR (compare `review/contact_sheet.png`), then merge by the maintainer. After merge, add the `PROJECT_PROGRESS.md` closeout entry on `sow/2026-Q4` with the merge SHA. SAM#166 (the reference design system) remains open.

## SPDX header policy (CI `spdx-check`)
The repository SPDX check requires the LGPL-3.0-or-later SPDX line and the copyright line in every `.cs` file a PR changes. The icon-token swaps touched 0 older files that predated the policy (components, `Resources.Designer.cs`), and the kit test `IconTests.cs` had no header. The standard 2-line header was added to them; nothing else changed. `tools/check_source.py` accepts exactly this header as the only non-icon addition and compares against the merge base.

## Q4 migration validation
Re-validated on `sow/2026-Q4` @ `01a528fa`: `design/grasshopper-icons/tools/check_source.py origin/sow/2026-Q4` OK (icon-token swaps: 37, SPDX headers added: 0, non-icon changes: 0, ComponentGuid declarations unchanged); `dotnet build SAM_OCCT.sln -c Debug` with `SAM_OCCT_SKIP_NATIVE_BUILD=true` (as in CI) succeeded with 0 errors; `check_assemblies.py build` OK; tests: `SAM.OCCT.UnitTests` 631/631 passed; `SAM.OCCT.IntegrationTests` 31 passed, 232 skipped (native OCCT library absent, as in CI). The Q4 feature diff (before this record commit) has the same patch-id, file set and blobs as the Q3 PR's feature diff.
