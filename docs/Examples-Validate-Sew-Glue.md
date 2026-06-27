<!-- SPDX-License-Identifier: LGPL-3.0-or-later -->
<!-- Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors -->

# Worked Examples — Validate, Sew, Glue (issue #37)

Hands-on Grasshopper recipes for the watertight-recovery toolkit added in issue
#37 and its follow-ons:

- **`SAMOCCT.Validate`** — locate *why* and *where* geometry will not close.
- **`SAMOCCT.Sew`** (or `SAMOCCT.CreateShells` with `sewBeforeBuild_`) — close
  triangulated / near-touching faces.
- **`SAMOCCT.CreateShells` `glueMode_`** — speed up large cell complexes with
  many coincident shared walls.

Every component has a **`Diagnostics`** output — wire it to a **Panel** to read
the `SAM_OCCT_*` status codes that tell you which path ran. All components have a
**`_run`** Boolean (set `true`) and need the native `SAM.Occt.Native` library
(build it once with `build-native.ps1`, or via the Grasshopper project's
pre-build step in Visual Studio).

> Build the model in metres. Default `tolerance_ = 0.001` (1 mm),
> `fuzzyTolerance_ = 0.1`.

---

## Example 1 — Validate an open vs. closed box

The fastest sanity check: prove `SAMOCCT.Validate` flags an open shell and
points at the hole.

```
Box ──▶ Deconstruct Brep ──(Faces)──▶ SAMOCCT.Validate ──▶ IsValid / IsWatertight
                                       (_run = true)        IssueLocations ──▶ Point preview
                                                            Issues / Diagnostics ──▶ Panel
```

**Closed box (all 6 faces):**

| Output | Expected |
| --- | --- |
| `IsValid` | `true` |
| `IsWatertight` | `true` |
| `IssueLocations` | empty |

**Open box** — insert a `Cull Index` (or `List Item`) between *Faces* and
*Validate* to drop the top face (5 faces in):

| Output | Expected |
| --- | --- |
| `IsWatertight` | `false` |
| `IssueLocations` | **4 points** around the open top rim — bake / preview them to *see* the hole |
| `Issues` | `Naked edge of length 1 m at (…)` ×4 |
| `Diagnostics` | contains `SAM_OCCT_VALIDATE_NOT_WATERTIGHT` |

---

## Example 2 — Recover a gapped / triangulated surface (Validate → Sew)

Curved or independently-triangulated surfaces leave sub-tolerance seams that a
direct build cannot close. This is the everyday use case.

```
Sphere/Loft ─▶ SAMOCCT.TriangulateSurface ─▶ (planar faces) ─┬─▶ SAMOCCT.CreateShells
              (linearDeflection_ = 0.1)                       │   (often fails to close)
                                                              ├─▶ SAMOCCT.Validate ──▶ IssueLocations (the seams)
                                                              └─▶ SAMOCCT.Sew (sewingTolerance_ = 0.1)
```

1. **Diagnose:** feed the faces to `SAMOCCT.Validate`. `IsWatertight = false` and
   `IssueLocations` marks the seam gaps. A direct `SAMOCCT.CreateShells` now also
   prints a located message in `Diagnostics`
   (`SAM_OCCT_VALIDATE_INPUT … naked edge(s) … near (x,y,z)`).
2. **Close**, either:
   - **`SAMOCCT.Sew`** with `sewingTolerance_ = 0.1` (larger than the seam gap), or
   - **`SAMOCCT.CreateShells`** with `sewBeforeBuild_ = true` and
     `sewingTolerance_ = 0.1`.
3. **Expect:** one closed Shell and `Diagnostics` showing `SAM_OCCT_SEW_SUCCESS`.

> Rule: `sewingTolerance_` must be **larger than the gap** (≈ `linearDeflection_`)
> but **smaller than the smallest real feature** you want to keep separate.

---

## Example 3 — BOP glue on a multi-cell complex (the headline)

Glue makes the boolean kernel treat coincident shared walls as shared instead of
re-intersecting them — a throughput win on large adjacency clusters.

```
Box ─▶ Rectangular/Box Array (e.g. 3×3×3) ─▶ Deconstruct Brep ─▶ (flat list of faces)
                                                                 ─▶ SAMOCCT.CreateShells
                                                                    glueMode_ = 2 (full), _run = true
                                                                    Diagnostics ──▶ Panel
```

**Clean array (walls exactly coincident):**

| Output | Expected |
| --- | --- |
| `Shells` | one Shell per cell (e.g. 27 for a 3×3×3 array) |
| `Successful` | `true` |
| `Diagnostics` | contains `SAM_OCCT_GLUE_SUCCESS` |

On a **large** array (hundreds of cells) compare build time with `glueMode_ = 0`
vs `glueMode_ = 2` — that is where the speedup is visible.

**Negative test (the safety gate):** move one box's shared wall by ~5 mm (less
than `fuzzyTolerance_ = 0.1`, so faces become *near*- but not exactly
coincident), then rebuild with `glueMode_ = 2`.

| Output | Expected |
| --- | --- |
| `Diagnostics` | contains `SAM_OCCT_GLUE_SKIPPED` |
| behaviour | glue is **not** applied; the model is built via the safe glue-off path instead of being corrupted |

---

## Status codes you will see in `Diagnostics`

| Code | Meaning |
| --- | --- |
| `SAM_OCCT_VALIDATE_SUCCESS` | A validation report was produced. |
| `SAM_OCCT_VALIDATE_NOT_WATERTIGHT` | Free (naked) edges found — there is a gap. |
| `SAM_OCCT_VALIDATE_INVALID` | Self-intersections / invalid faces found. |
| `SAM_OCCT_VALIDATE_INPUT` | Located input diagnostics emitted after a build failed to close. |
| `SAM_OCCT_SEW_SUCCESS` | Sew-and-heal closed the faces into a shell/solid. |
| `SAM_OCCT_GLUE_SUCCESS` | The cell complex was built with BOP glue. |
| `SAM_OCCT_GLUE_SKIPPED` | Glue was requested but the input had gaps; built without glue. |
| `SAM_OCCT_GLUE_DEGRADED` | Glue could not run (e.g. native build predates ABI v3); built without glue. |
| `SAM_OCCT_NATIVE_MISSING` | `SAM.Occt.Native` not on the load path — run `build-native.ps1`. |

See [`Modeling-Guide.md`](Modeling-Guide.md) → *Diagnosing And Closing Failures*
for the recommended order and tolerance guidance.
