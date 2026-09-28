# SAM Grasshopper Icon Design System

Status: **frozen v1** (2026-09-28). It builds on the approved exploratory direction ("Opus 5.5 proposal", 5 icons),
refined toward the SAM-BIM identity and scaled to the whole library.
`tools/icons.py` implements this file. If they disagree, fix whichever one is wrong. Don't hand-edit generated SVG/PNG.

## 1. Grammar

```
icon = object glyph  +  SAM-green subject  +  operation badge
       (what)           (what is produced     (what the component does)
                         or affected)
```

* **Object glyph**: one per SAM object family (§3). It sits in the top-left ~18×18 px and has a strong silhouette.
* **Subject**: the part the component produces, returns or changes is drawn in SAM green. Context is neutral grey/white.
* **Operation badge**: a 10 px disc, always at the bottom-right (centre 19,19). Its colour gives the operation *family*, its glyph the *verb* (§4).
* **Parameters** (`GH_PersistentParam`): the object glyph alone, all green, with no badge. A missing badge means "this is data, not an operation".

A user learns roughly 40 object shapes and 7 badge colours, and can then read any SAM component.

## 2. Palette (from https://sambim.xyz, inspected 2026-09-28)

| Role | Hex | Source |
|---|---|---|
| Ink (outlines, dark glyphs) | `#111111` | site `--text` / `--line` |
| **SAM emblem green** (subject) | `#81BC44` | `assets/SAM_BIM_logo.png`, averaged tile pixels |
| Subject light / shade faces | `#ADD485` / `#659335` | derived from emblem green (35 % tint / 22 % shade) |
| SAM deep green | `#277A45` | site `--green` |
| Blue | `#1565C0` | site `--blue` |
| Yellow | `#F6C90E` | site `--yellow` |
| Red | `#D32F2F` | site `--red` |
| Muted | `#424242` | site `--muted` |
| Neutral faces | `#FFFFFF` / `#E3E3E3` / `#BDBDBD` | site `--bg` / derived / site border grey |

Rules:
- Flat fills only: no gradients, glows or text.
- Every icon uses the emblem green for its subject. This is the SAM identity.
- The site's primary accents (blue, yellow, red, deep green, black) are used **only** in operation badges, plus two semantic exceptions: the sun is yellow, and the colour glyph uses the site's red/yellow/blue stripe.
- 3D glyphs use 3-tone flat shading (top / left / right), in green when the part is the subject and neutral when it's context.

## 3. Object families

| Family | Glyph | Motif |
|---|---|---|
| AnalyticalModel / BuildingModel | `model` | isometric house block (gable roof) |
| AdjacencyCluster | `cluster` | two joined rooms with a green shared-wall seam (the adjacency) |
| Case / scenario | `case` | two models (variants) |
| Space | `space` | isometric room cube |
| ExternalSpace | `externalSpace` | dashed ghost cube |
| Floor (area) | `floor` | cutaway room corner with a green floor (approved exploratory icon) |
| Zone | `zone` | green floor plate carrying two rooms |
| IZAM (inter-zone air) | `izam` | two rooms with an air arc between them |
| Panel | `panel` | upright wall plane |
| Aperture / opening | `aperture` | wall plane with a green window |
| OpeningProperties | `openingProperties` | window with an open green sash |
| Shade | `shade` | overhang plate on a wall |
| Normal | `normal` | wall with a normal arrow |
| Construction (+ Wall/Floor/Roof/HostPartitionType) | `construction` | layered wall block: neutral, green core, neutral |
| ConstructionLayer / MaterialLayer | `constructionLayer` / `materialLayer` | single layer slab (material layer is hatched) |
| ApertureConstruction / OpeningType | `apertureConstruction` | double-glazing unit, green glass |
| ConstructionManager | `constructionManager` | construction + glazing unit together |
| Material (opaque / transparent / gas) | `material` / `transparentMaterial` / `gasMaterial` | sample sphere: solid / clear / dotted |
| Profile | `profile` | step schedule on axes |
| InternalCondition, occupancy, NCM | `internalCondition` | person |
| DegreeOfActivity | `degreeOfActivity` | person with motion ticks |
| Lighting / equipment / infiltration / pollutant gains | `lighting` / `equipment` / `infiltration` / `pollutant` | bulb / monitor / wind / cloud with particles |
| Set-point, comfort, TM52, adaptive | `thermometer` | thermometer |
| Ventilation system | `fan` | fan |
| Airflow | `airflow` | three air arrows |
| AirHandlingUnit, equipment object | `ahu` | box with fan port |
| System / SystemType | `system` | duct unit |
| Heat transfer | `heat` | arrow through a wall |
| Daylight | `daylight` | sun rays onto a floor |
| Level | `level` | datum line with a level marker |
| Shell | `shell` | hexagonal prism (closed solid) |
| Face3D / PlanarBoundary3D | `face` | flat polygon plate |
| Geometry (generic) / Geometry2D | `geometry` / `geometry2D` | hexagon with vertices / flat L-polygon |
| Plane, Transform3D, Mesh, Polyloop, Segment, Points, Vector, Grid | same names | plane + normal, axis triad, fan mesh, loop, segment, 3 points, arrow, dotted floor |
| Section | `sectionBox` | solid split open, green cut face (approved exploratory icon) |
| Weather / WeatherData / WeatherYear / DesignDay | `weather` / `weatherData` / `weatherYear` / `designDay` | sun + cloud / sun + bars / calendar + sun / sun on day arc |
| Location / Address / direction | `location` / `address` / `compass` | map pin / pin + card / compass |
| Time / day | `clock` / `calendar` | clock / calendar |
| Result, DesignExplorer | `result` | bar chart |
| SAM object (generic) | `object` | the SAM emblem tile (green rounded square), no lettering |
| RelationCluster / Group / IndexedObjects / Type | `relation` / `group` / `indexed` / `type` | node graph / dashed group / indexed tiles / shape trio |
| Value / parameter, Name, Text, Number | `value` / `name` / `text` / `number` | tag / label / quotes / ruler |
| Table (DelimitedFileTable, Csv, TableModifier), TextMap | `table` / `textMap` | grid with a green header / two mapped columns |
| JSON, File, Folder, Log, List, DataTree | `json` / `file` / `folder` / `log` / `list` / `tree` | braces / page / folder / log page / bullets / tree |
| Colour | `colour` | the site's red/yellow/blue squares |
| Settings (merge settings, combine type) | `settings` | sliders |
| Matrix | `matrix` | bracketed dot grid |
| About / Version | `info` | emblem tile with "i" (identity exception) |

## 4. Operation families (badges)

| Family | Disc | Verbs → glyph |
|---|---|---|
| **Construct** | blue `#1565C0`, white glyph | Create `+` · Add `+` · Copy/Duplicate ⧉ |
| **Query** | deep green `#277A45`, white glyph | Get ↗ (out) · Filter/Select funnel · Inspect magnifier · Report/List ≡ · Display/Label eye |
| **Change** | yellow `#F6C90E`, ink glyph | Set ↙ (in) · Modify/Fix/Adjust/Rename Δ · Update/Map/Replace ↻ · Merge/Join/Combine ⋎ · Split/Cut/Trim ⋏ · Section ⊟ · Union ∪ · Intersect ∩ · Difference ⌐ · Offset ⧈ · Transform/Move ↔ · Snap •\| · Align · Extend →\| · Flip ⇅ · Triangulate · Sort |
| **Remove** | red `#D32F2F`, white glyph | Remove − · Error ! |
| **Evaluate** | ink `#111111`, white glyph | Calculate `=` · Sum `+` · Multiply × · Divide ÷ · Analyse bars · Validate/Check/Is ✓ |
| **Transfer** | white, ink ring and glyph | Convert/To ⇄ · Import/Read/Load/From ↓ · Export/Write/Save/Bake/To-file ↑ |
| **Value** | white, ink ring and glyph | enum value lists (`GH_SAMEnumComponent`) ≡ |

The badge carries shape **and** colour, so it stays readable with colour-blindness and in greyscale. Mirrored pairs are intentional: Get ↗ out / Set ↙ in, Merge ⋎ / Split ⋏, Union ∪ / Intersect ∩, Import ↓ / Export ↑.

## 5. Modifiers (object-layer composition)

| Modifier | Drawing | Used for |
|---|---|---|
| plural | two instances (0.78 scale, fixed offsets) | `GetSpaces` vs `GetSpaceByName`, `…Panels` vs `…Panel` |
| library | two cards behind the object (0.74) | `*Library` objects, default libraries |
| add | neutral existing instance + green new instance | every `Add…` (plurality not drawn) |
| library + add | green item going into a neutral library | `AddMaterials`, `AddMissingProfiles` |
| split effect | glyph cut diagonally and pulled apart | Split/Cut/Trim |
| intersect effect | two neutral instances, overlap in green | Intersection |
| difference effect | green instance with a dashed white cutter | Difference |

Outline weight stays 1.1 px under every scale: `scaled()` compensates the stroke width.

## 6. Construction rules

1. Canvas 24×24, one outline weight (1.1 px ink), round joins.
2. Isometric glyphs use 2:1 pixel isometry, so every solid shares one camera.
3. The bottom-right quadrant (the badge disc, r = 5 with a white ring) is reserved. Glyphs may pass under it, but can't rely on anything there.
4. Minimal detail: no element under ~1.5 px, and no text inside icons.
5. On light Grasshopper bodies the ink outline carries the silhouette. On dark bodies the light/green fills carry it. The white badge ring separates the badge on both.
6. Qualifiers (`…By<X>`) are not drawn. Variants that differ only by qualifier share one icon **intentionally**.
7. **Badge glyph weight** (from the live Rhino review): line glyphs use a stroke of at least 1.6 px (outline shapes at least 1.3 px). Arrows use filled heads. Transfer badges use one bold solid arrow (↓ import, ↑ export, ⇄ convert) and no base line. Thinner glyphs collapsed to a dot at native size.
8. Review every icon on all three Grasshopper body states: normal grey, **orange warning** (the default for a freshly placed component with unconnected inputs) and a dark skin. The yellow Change disc keeps its white ring, so it still reads on the orange warning body.

## 7. Collision rules

* Singular vs plural of the same query/change always differ (the plural modifier).
* Create vs Add always differ (single green object vs neutral + green).
* Get vs Set always differ (colour family **and** mirrored arrow).
* `tools/build.py` fails the review if two different icon ids rasterise to identical pixels. The current result is 0 identical groups.

## 8. Exceptions

* `SAM.About` / `SAM.Version`: SAM emblem tile + info mark (identity, not an analytical object).
* `SAM.Test` (`A_SAMAnalytical`): it had no Icon override. It gets `object + validate`. This is the only class that gains a new `Icon` line.
* `AssemblyInfo.Icon` / `AssemblyIcon` (the Grasshopper tab/plugin identity) are **not** component icons and keep the SAM logo.
* Hidden/obsolete components get the same grammar icon as their live equivalent. They appear only in old definitions.

## 9. Pipeline (deterministic)

```
tools/inventory.py   -> manifest_raw.json     (parse C#: every class with ComponentGuid)
tools/classify.py    -> manifest.json / .csv  (rules + explicit OVERRIDES table)
tools/build.py       -> svg/  png/24/  review/batch_*.png  SAM_GH_ICON_LIBRARY.png  (+ identical-pixel check)
tools/integrate.py   -> Grasshopper/<proj>/Resources/Icons/*.png, Resources.resx, Resources.Designer.cs, Icon getters
tools/catalogue.py   -> review/_catalogue/   (all glyphs, badges and modifiers)
tools/check_assemblies.py <build dir>        (every required icon embedded at 24x24 in the built DLLs)
tools/live_review_rhino.py                   (real Rhino 8 / GH canvas capture, light + dark; see file header)
```

The rasteriser renders each SVG alone at a fixed page position, so an icon's PNG depends only on its own SVG. Adding icons never changes existing PNGs.

Requirements: Python 3 with Pillow, and Microsoft Edge (headless) as the SVG rasteriser.
`svg/` is the canonical editable source. The PNGs are generated, never hand-edited.
