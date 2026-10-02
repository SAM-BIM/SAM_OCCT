# SAM-BIM icon design system: extension v1

Status: **frozen v1** (2026-09-28). It extends the SAM Grasshopper icon design system (`ICON_DESIGN_SYSTEM.md`,
vendored verbatim from SAM-BIM/SAM#166 @ `cf4d924a`) to the SAM-BIM plugin repositories.
`tools/icons_ext.py` implements this file and is **byte-identical in every SAM-BIM repository**.

## What the extension may and may not do

* It **adds** object glyphs for domains where SAM has no noun. It also adds **one** verb, `run`.
* It does **not** change the grammar (object glyph + SAM-green subject + operation badge), the palette, the 7 badge
  families and their colours, any SAM glyph, badge or modifier, or the construction rules (24×24, one 1.1 px outline,
  flat fills, no text, bottom-right badge quadrant reserved).
* SAM glyphs are reused wherever the noun already exists: space, panel, construction, shell, model, system, fan,
  weather, file, table, result and so on. An object that SAM already draws is never redrawn.
* An icon id that also exists in SAM (e.g. `model_import`, `shell_union_plural`) renders **pixel-identically** to
  SAM's icon, because `icons.py` and `render.py` are the same bytes.

## New object glyphs (v1)

| Glyph | Motif | Used for |
|---|---|---|
| `mollierChart` | psychrometric chart axes + saturation curve, green area | Mollier/psychrometric chart, diagram, generic Mollier object |
| `mollierPoint` | chart (thin) + green state point | MollierPoint |
| `mollierProcess` | chart (thin) + two green states joined | MollierProcess (generic, undefined, by two points); MollierGroup uses SAM `group` |
| `processHeating` / `processCooling` | chart + bold green arrow → / ← (mirrored pair) | heating / cooling processes |
| `processHumidification` | chart + arrow ↑ | isothermal and steam humidification |
| `processAdiabatic` | chart + arrow ↖ | adiabatic humidification |
| `processRoom` | chart + arrow ↗ | room (load) process |
| `processMixing` | chart + two arrows converging on a state | mixing process |
| `processHeatRecovery` | chart + ⇄ arrows | heat recovery process |
| `processFan` | chart + fan disc + short arrow → | fan process |
| `cellComplex` | isometric solid divided into 2×2×2 cells | Topologic/OCCT CellComplex, Topology |
| `algorithm` | objective curve with the green optimum | GenOpt / solver algorithms |
| `objective` | target | optimisation objective |
| `script` | terminal `>_` (dark body, green prompt) | Python / GenOpt / Excel macro scripts |
| `tasks` | three green lanes running in parallel | Multitasker |
| `sound` | speaker + sound waves | acoustic band values and absorption |
| `view` | drawing sheet: green plan + title block | Revit views and sheets |
| `element` | green cube on a neutral base plate | Revit/BIM element instances and ids |
| `energyCentre` | plant block with a stack | SystemEnergyCentre |
| `plantRoom` | SAM `floor` cutaway room with a green plant unit | SystemPlantRoom |
| `connector` | two green units joined by a pipe | system connectors, connected objects |
| `sunPath` | sun + green direction arrow to the ground | sun direction / sun position |
| `solar` | sun + parallel rays onto a green surface | irradiance, sun exposure, solar simulation |

Process arrows use the mid subject green `#659335` (SAM's `G_RIGHT`) at 2.2 px. It stays readable on the normal,
orange-warning and dark Grasshopper bodies, where the deep green `#277A45` disappears on dark.
Chart glyphs never take the plural modifier (`classify.py` `NO_PLURAL`): stacked charts do not read at 24 px.

## New verb (v1)

| Verb | Family | Glyph | Leading words |
|---|---|---|---|
| `run` | **Evaluate** (existing ink disc, white glyph) | ▶ filled play triangle | Run, Simulate, Execute, Solve, Workflow |

## Classification (tools/classify.py)

Rules, in order: `repo_rules.OVERRIDES` → SAM `OVERRIDES` → verbs (repo, SAM, extension) → nouns
(repo, extension, SAM) → params (repo, extension, SAM). Plugin name prefixes (`SAMMollier.`, `Tas.`, `SAMOCCT.`, `Revit.` …)
are stripped the way SAM strips `SAMAnalytical.`. Two conventions apply to interop components:

* external → SAM (`X.SAMAnalytical`, `FromX`, `Open…`) = **import ↓**; SAM → external (`SAMAnalytical.X`, `ToX`, `Save…`) = **export ↑**.
* The object glyph is the SAM object that is read or written (model, geometry, result…), never a vendor logo.

`repo_rules.py` holds a repository's explicit decisions (overrides, param nouns). Each entry is one line with a reason when
it is not obvious.

## Tooling (identical in every repo, except `repo_rules.py`)

```
tools/inventory.py        -> manifest_raw.json   (every non-abstract class declaring ComponentGuid, any project layout)
tools/classify.py         -> manifest.json/.csv  (source of truth: object, op, modifiers, icon id, resource, origin)
tools/build.py            -> svg/ png/24/ review/contact_sheet.png review/REVIEW.md (+ identical-pixel check)
tools/integrate.py        -> Resources/Icons/*.png, resx + Designer (or the project's icon holder class), Icon getters
tools/check_source.py     -> vendored-file hashes, icon-token-only C# diff, ComponentGuid set unchanged
tools/check_assemblies.py -> built .gha/.dll embed every required 24x24 icon
tests/GhIconTest/         -> real Rhino 8 / Grasshopper: every object loads by GUID with its redesigned icon
```
Vendored verbatim from SAM#166 (hash-checked by `check_source.py`): `icons.py`, `render.py`, `sam_classify.py`
(SAM's `classify.py`), and `ICON_DESIGN_SYSTEM.md`. Requirements: Python 3 + Pillow, Microsoft Edge (headless rasteriser).
