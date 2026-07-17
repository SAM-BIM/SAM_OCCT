# Manual Extend3D replication report

## Workflow

The diagnostic uses the Grasshopper component sequence exactly:

`Panels -> Extend3D -> CreateAdjacencyCluster -> MergeCoplanarAdjacencyCluster`

`bucket = 0.5`, `doubleWallGap = 0.5`, `bucketBetweenLevels = 0.5`, and the production component defaults for the remaining inputs (`fillMargin = 0.5`, non-directional cap growth, sewing enabled at `0.01`). No production solver code is changed.

## Baseline

| Fixture | Input | Extend3D/build | Cells/spaces | Merged panels | Floor area (m²) | Volume (m³) |
|---|---:|---:|---:|---:|---:|---:|
| whole-level-towers | 215 | 131 | 30 | 196 | 3171.798879 | 9641.151347 |
| Panels-9SpacesModel | 66 | 38 | 6 | 42 | 381.873711 | 1492.056471 |
| two-level-tilted | 287 | 183 | 27 | 192 | 562.010109 | 4469.798915 |
| whole-level-tilted control | 148 | 106 | 22 | 146 | 553.651846 | 3377.906622 |

## Manual-boundary replication

### Towers

The anchored source floor is input panel 205 (`37ba6b21-8be6-4973-aa51-70baeb9ed66d`). Extending its east side far enough for `Extend3D` output floor panel 5 to cross the north ends of output walls 125 and 105 creates the missing cell. The equivalent output edit moves the slanted edge from `(23.193952,-0.778020,12.24) -> (18.347091,-0.755919,12.24)` by `(0,+0.08,0)`.

Result: 31 spaces and 3210.934713 m², matching the requested count and area within 0.000004 m². Native volume is 9760.515642 m³, 0.014593 m³ above the requested 9760.501049 m³, so the exact Rhino boundary is not yet replicated.

### Nine spaces

At the `Extend3D` output, cap pieces 4 (z=12.24) and 26 (z=15.29) must both grow outward by 0.5 m per edge. Neither cap alone is sufficient. This covers the fragmented east/south wall bands at the lower level and the full-height wall envelope around the anchored upper roof. Result: 9 spaces, 468.105936 m² and 1755.064582 m³.

This proves the required output geometry, but the exact source-stage Rhino panel edit is not yet mapped: growing the anchored source roof alone does not reproduce it.

### Two-level tilted

The anchor lies on two overlapping source walls, input panels 49 and 125. `Extend3D` combines their extents into output wall 65, a ten-vertex profile with normal `(-0.975006,0,-0.222177)`. The raw rooms on its two sides are cells 4 (75.442752 m³) and 17 (111.767040 m³); both are absent from the 27-cell `Extend3D` result.

Measured plane intersections show caps 8/9 cover the upper intersection and caps 27/30 cover the lower intersection. Growing every single cap by 0.5 m, every subset of those four output caps by 0.5 m, and every subset of the four corresponding source caps (45, 46, 123, 124) by 0.5 m or 1.0 m leaves 27 spaces. Therefore the nearest-facing-edge and simple uniform/full-profile expansion hypotheses are rejected for this fixture. The exact successful manual boundary has not yet been recreated.

## Pattern and rule status

The successful towers and nine-space edits both enlarge horizontal cap coverage across transverse/irregular wall ends, and sometimes require more than one coplanar cap piece. That supports containment of legitimate wall footprints more strongly than nearest-facing-wall growth. However, the tilted counterexample shows that cap containment alone is not sufficient: the profiled wall already intersects covered cap regions, yet its adjacent rooms remain open.

No production rule is recommended until the two-level manual edit and the exact towers volume are reproduced. Implementing uniform expansion, per-edge nearest-wall expansion, or projected-profile containment now would overstate the evidence.

## Working status

- Diagnostic branch: `debug/manual-extend3d-replication` from `be96134`.
- Production code: unchanged.
- Diagnostic integration test and JSON/Markdown evidence: added.
- Previous cap/sewing/MakerVolume work: preserved separately in a stash and excluded from this branch.

MANUAL CORRECTION INCOMPLETE — two-level-tilted successful boundary edit and exact towers volume still not replicated
