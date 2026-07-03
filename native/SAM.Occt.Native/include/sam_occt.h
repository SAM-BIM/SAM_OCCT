// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

#pragma once

#ifdef _WIN32
#  ifdef SAM_OCCT_NATIVE_EXPORTS
#    define SAM_OCCT_API __declspec(dllexport)
#  else
#    define SAM_OCCT_API __declspec(dllimport)
#  endif
#else
#  define SAM_OCCT_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* ABI revision of this library. 1 = the original decode-and-free ABI (implicit,
   never exported); 2 = adds this probe and the persistent sam_occt_shape handle
   family. Managed callers probe this once to detect a stale native build. */
SAM_OCCT_API int sam_occt_abi_version(void);

/* ---- persistent shape handles (issue #14) ----
   A sam_occt_shape handle owns a live OCCT TopoDS_Shape (a compound of solids,
   or a single solid). Every function returning a handle transfers ownership to
   the caller, who must release it exactly once with sam_occt_shape_release.
   Operations never consume or mutate their input handles. Handles are opaque;
   issue #20 (STEP/IGES via XCAF) can extend the underlying struct without
   breaking this ABI. Shape handles and result handles are distinct types -
   passing one where the other is expected fails with status 50 instead of
   crashing. Use shapes from a single thread; releasing from another thread
   (e.g. a finalizer) is safe. */

/* Frees a shape handle. Safe no-op on null or foreign pointers. */
SAM_OCCT_API void sam_occt_shape_release(void* shape_handle);

/* BOPAlgo_MakerVolume over independent faces (the cell-complex semantics of
   sam_occt_build_cell_complex) returning the fixed shape instead of a decoded
   result. Status: 0 ok, 10/11 input, 20 no valid faces, 30 BOP error,
   40 no solids, 99 exception. */
SAM_OCCT_API int sam_occt_shape_create_cell_complex(
    const double* coordinates,
    int point_count,
    const int* loop_point_counts,
    int loop_count,
    const int* face_loop_counts,
    int face_count,
    double fuzzy_tolerance,
    int run_parallel,
    int avoid_internal_shapes,
    void** shape_handle);

/* Per-shell MakerVolume (the semantics feeding sam_occt_shells_union et al.);
   the handle owns a compound of the resulting solids. Status codes as above. */
SAM_OCCT_API int sam_occt_shape_create_shells(
    const double* coordinates,
    int point_count,
    const int* loop_point_counts,
    int loop_count,
    const int* face_loop_counts,
    int face_count,
    const int* shell_face_counts,
    int shell_count,
    double fuzzy_tolerance,
    int run_parallel,
    void** shape_handle);

/* ---- shape-in / shape-out operations ----
   All return a NEW handle via shape_handle_out; input handles stay valid and
   caller-owned. Status: 0 ok, 10 null output pointer, 30 BOP error, 40 result
   has no solids, 50 invalid input handle, 99 exception. */

/* BRepAlgoAPI_Fuse of all solids inside the handle (sam_occt_shells_union
   semantics, including the single-solid pass-through). */
SAM_OCCT_API int sam_occt_shape_union(
    void* shape_handle,
    double fuzzy_tolerance,
    int run_parallel,
    void** shape_handle_out);

/* BRepAlgoAPI_Cut of each target solid against all cutter solids. */
SAM_OCCT_API int sam_occt_shape_difference(
    void* target_shape_handle,
    void* cutter_shape_handle,
    double fuzzy_tolerance,
    int run_parallel,
    void** shape_handle_out);

/* BRepAlgoAPI_Common of each target solid against all tool solids. */
SAM_OCCT_API int sam_occt_shape_intersection(
    void* target_shape_handle,
    void* tool_shape_handle,
    double fuzzy_tolerance,
    int run_parallel,
    void** shape_handle_out);

/* Per-solid defeaturing of faces smaller than min_area + UnifySameDomain
   (sam_occt_shells_repair semantics); solids that cannot be repaired are kept
   unchanged rather than dropped. */
SAM_OCCT_API int sam_occt_shape_repair(
    void* shape_handle,
    double fuzzy_tolerance,
    int run_parallel,
    double min_area,
    void** shape_handle_out);

/* General Fuse (BOPAlgo_Builder) over every solid in the handle: imprints the
   solids against one another so coincident regions of touching faces are split
   into matching sub-faces shared by both neighbours. This is what gives an
   energy model "second-level" space boundaries - after imprinting, a wall that
   abuts two spaces decodes into sub-faces that key-match each neighbour, so
   sam_occt_shape_decode reports them as FaceAdjacencies. Solid count is
   preserved for non-overlapping (merely touching) inputs; genuinely
   overlapping inputs are resolved like any General Fuse. A single solid is a
   pass-through (nothing to imprint). Status: 0 ok, 10 null output pointer,
   30 BOP error, 40 result has no solids, 50 invalid input handle, 99 exception. */
SAM_OCCT_API int sam_occt_shape_imprint(
    void* shape_handle,
    double fuzzy_tolerance,
    int run_parallel,
    void** shape_handle_out);

/* BOPAlgo_MakerVolume over the faces of shape_handle plus optional extra
   faces given as flattened arrays (coordinates may be null / face_count 0).
   The shape-backed core of plane sectioning and of re-celling a shape. */
SAM_OCCT_API int sam_occt_shape_make_volume(
    void* shape_handle,
    const double* coordinates,
    int point_count,
    const int* loop_point_counts,
    int loop_count,
    const int* face_loop_counts,
    int face_count,
    double fuzzy_tolerance,
    int run_parallel,
    int avoid_internal_shapes,
    void** shape_handle_out);

/* Footprint extrusion (issue #30): builds planar boundary loops (the same
   flattened-array contract as sam_occt_shape_create_cell_complex) into faces
   and linearly extrudes each by the direction vector via BRepPrimAPI_MakePrism,
   producing one closed solid per footprint wrapped in a new caller-owned
   handle. Intended for the 2-D-footprint-plus-storey-height workflow that feeds
   CreateShells / the adjacency pipeline. Status: 0 ok, 10/11 input,
   12 zero-length direction, 20 no valid faces, 30 prism failed, 40 no solids,
   99 exception. */
SAM_OCCT_API int sam_occt_extrude(
    const double* coordinates,
    int point_count,
    const int* loop_point_counts,
    int loop_count,
    const int* face_loop_counts,
    int face_count,
    double direction_x,
    double direction_y,
    double direction_z,
    void** shape_handle);

/* Offsets each solid's skin outward (positive) or inward (negative) by `offset`
   via BRepOffsetAPI_MakeOffsetShape (issue #29) - moving between analytical
   centre-line and physical construction faces. Returns a NEW caller-owned
   handle. Offsetting is the most failure-prone OCCT op, so each solid is
   processed independently. Status: 0 ok, 10 null output pointer,
   12 zero offset, 30 offset failed, 40 no solids, 50 invalid handle, 99 exception. */
SAM_OCCT_API int sam_occt_shape_offset(
    void* shape_handle,
    double offset,
    double tolerance,
    void** shape_handle_out);

/* Hollows each solid into a wall of the given `thickness` via
   BRepOffsetAPI_MakeThickSolid (issue #29) - e.g. plenum / air-cavity volumes.
   Returns a NEW caller-owned handle. Status codes as sam_occt_shape_offset
   (12 = zero thickness). */
SAM_OCCT_API int sam_occt_shape_thick_solid(
    void* shape_handle,
    double thickness,
    double tolerance,
    void** shape_handle_out);

/* ---- watertight sew-and-heal (issue #37) ----
   Sew a face soup into the tightest closed shell/solid OCCT can make BEFORE
   BOPAlgo_MakerVolume: BRepBuilderAPI_Sewing -> ShapeFix_Shell / ShapeFix_Wireframe
   (FixSmallEdges / FixWireGaps close micro-gaps that the sewing tolerance alone
   misses) -> ShapeFix_Solid -> ShapeUpgrade_UnifySameDomain. Unlike
   sam_occt_(shape_)create_cell_complex this does NOT require the input to already
   bound a volume; it is the closing step that makes it bound one, so it is the
   recovery path for the triangulated / near-touching face soups that cause most
   MakerVolume status-40 (no volume) failures on real models.

   make_solid=1 attempts BRepBuilderAPI_MakeSolid on every closed shell and the
   handle is wrapped like any solid-producing op (status 40 when no closed shell
   was formed). make_solid=0 returns the healed - and possibly still open - shell
   in a relaxed handle (no solid required) so it can be re-fed to MakerVolume; such
   a handle has zero solids and must not be treated as a finished cell complex.

   Status: 0 ok, 10/11 input, 20 no valid faces, 30 sew/heal failed,
   40 no closed shell (make_solid=1 only), 50 invalid input handle, 99 exception. */
SAM_OCCT_API int sam_occt_sew_faces(
    const double* coordinates,
    int point_count,
    const int* loop_point_counts,
    int loop_count,
    const int* face_loop_counts,
    int face_count,
    double sewing_tolerance,
    int run_parallel,
    int make_solid,
    void** shape_handle);

/* Sew+heal the faces already inside a handle (after import / offset / boolean).
   Returns a NEW caller-owned handle; the input stays valid. Status as above. */
SAM_OCCT_API int sam_occt_shape_sew(
    void* shape_handle,
    double sewing_tolerance,
    int run_parallel,
    int make_solid,
    void** shape_handle_out);

/* ---- validation & watertightness diagnostics (issue #37 follow-on) ----
   Runs BRepCheck_Analyzer + ShapeAnalysis_FreeBounds (+ optional
   BOPAlgo_ArgumentAnalyzer self-intersection test) over the shape and returns a
   caller-owned validation handle listing the located, categorised issues
   (naked edge w/ XYZ + gap length, self-intersection, small/invalid face).
   Query it with the sam_occt_validation_* accessors and free it with
   sam_occt_free_validation. check_self_intersections=1 enables the costly
   pairwise self-interference test. A status of 0 means the report was produced,
   NOT that the shape is valid - inspect is_valid / is_watertight / the issues.
   This is the native safety net the BOP-glue path is gated on and the teeth
   behind OcctBuildOptions.ValidateInput. Status: 0 ok, 10 null output pointer,
   50 invalid shape handle, 99 exception. */
SAM_OCCT_API int sam_occt_shape_validate(
    void* shape_handle,
    double tolerance,
    int check_self_intersections,
    void** validation_handle);

/* 1 valid / 0 invalid / -1 invalid handle (BRepCheck + argument analyzer). */
SAM_OCCT_API int sam_occt_validation_is_valid(void* validation_handle);

/* 1 watertight / 0 has free (naked) bounds / -1 invalid handle. */
SAM_OCCT_API int sam_occt_validation_is_watertight(void* validation_handle);

/* Number of located issues; -1 on an invalid handle. */
SAM_OCCT_API int sam_occt_validation_issue_count(void* validation_handle);

/* Reads one issue: category (see the managed OcctValidationIssueCategory), an
   XYZ location and a size (naked-edge length / face area / 0). All out pointers
   are required. Status: 0 ok, 10 null out pointer, 40 index out of range,
   50 invalid handle. */
SAM_OCCT_API int sam_occt_validation_issue(
    void* validation_handle,
    int index,
    int* category,
    double* x,
    double* y,
    double* z,
    double* size);

/* Frees a validation handle. Safe no-op on null or foreign pointers. */
SAM_OCCT_API void sam_occt_free_validation(void* validation_handle);

/* ---- BOP glue (issue #37 follow-on, ABI v3) ----
   Glue-enabled overloads of the cell-complex / volume builders. glue_mode:
   0 off (identical to the non-_ex entry point), 1 shift (BOPAlgo_GlueShift),
   2 full (BOPAlgo_GlueFull). Glue assumes coincident faces are truly shared and
   skips their pairwise intersection - a large throughput win for cell complexes
   with thousands of coincident shared walls - but it CORRUPTS merely-near-
   coincident faces, so a caller must gate it on a clean validation report. A
   stale native build that predates ABI v3 lacks these symbols, so managed
   callers probe sam_occt_abi_version and degrade to the non-_ex path.
   Status as the corresponding non-_ex entry point. */
SAM_OCCT_API int sam_occt_shape_create_cell_complex_ex(
    const double* coordinates,
    int point_count,
    const int* loop_point_counts,
    int loop_count,
    const int* face_loop_counts,
    int face_count,
    double fuzzy_tolerance,
    int run_parallel,
    int avoid_internal_shapes,
    int glue_mode,
    void** shape_handle);

SAM_OCCT_API int sam_occt_shape_make_volume_ex(
    void* shape_handle,
    const double* coordinates,
    int point_count,
    const int* loop_point_counts,
    int loop_count,
    const int* face_loop_counts,
    int face_count,
    double fuzzy_tolerance,
    int run_parallel,
    int avoid_internal_shapes,
    int glue_mode,
    void** shape_handle_out);

/* Number of solids in the shape; negative on an invalid handle. */
SAM_OCCT_API int sam_occt_shape_solid_count(void* shape_handle);

/* BRepClass3d_SolidClassifier against every solid in the shape.
   Returns 1 when the point is inside or on the boundary (within tolerance) of
   any solid, 0 when outside all solids, -1 on an invalid handle and -99 on an
   exception. */
SAM_OCCT_API int sam_occt_shape_point_in_solid(
    void* shape_handle,
    double x,
    double y,
    double z,
    double tolerance);

/* Minimum distance between two shapes via BRepExtrema_DistShapeShape, plus the
   closest point on each (the gap/proximity primitive of issue #28). Read-only:
   both input handles stay valid and caller-owned, and no new handle is created.
   Use it for tolerance-true adjacency detection, watertightness QA (locate the
   actual gap before MakerVolume fails to close a cell) and clash reporting.
   `distance` is required; the six closest-point out doubles are optional (pass
   null to skip). Status: 0 ok, 10 null distance pointer, 30 extrema not done,
   40 no solution, 50 invalid shape handle (either operand), 99 exception. */
SAM_OCCT_API int sam_occt_shape_distance(
    void* shape_handle_a,
    void* shape_handle_b,
    double* distance,
    double* ax,
    double* ay,
    double* az,
    double* bx,
    double* by,
    double* bz);

/* Decodes every solid of the shape into a regular result handle consumed by
   the sam_occt_result_* accessors and freed with sam_occt_free_result. Face
   topology keys are quantized with `tolerance` and are only comparable within
   one decoded result. Status: 0 ok, 10 input, 40 no cells, 50 invalid shape
   handle, 99 exception. */
SAM_OCCT_API int sam_occt_shape_decode(
    void* shape_handle,
    double tolerance,
    void** result_handle);

/* ---- STEP / IGES file import & export (issue #20) ----
   File round-trip through OCCT's Data Exchange module, operating on the
   persistent sam_occt_shape handle. Paths are UTF-8 const char*.

   Export serialises the shape (its solids/shells) to an open-format file. STEP
   preserves the BRep solids; IGES is written in BRep mode so closed solids
   round-trip rather than degrading to bare surfaces. Input handles stay valid
   and caller-owned.

   Import reads the file, transfers the roots and wraps the resulting
   TopoDS_Shape into a NEW caller-owned handle (release with
   sam_occt_shape_release). The imported shape is whatever the file describes
   (a compound of solids for a BRep STEP/IGES); decode it with
   sam_occt_shape_decode like any other handle.

   The Data Exchange toolkits (TKDESTEP.dll / TKDEIGES.dll) are delay-loaded, so
   this library keeps loading and every other operation keeps working even when
   those DLLs are not deployed; the STEP/IGES calls below probe for them first
   and return status 64 instead of faulting when they are absent.

   Status: 0 ok, 50 invalid shape handle, 60 null/empty path,
   61 file open/read failed (missing or unreadable), 62 write/transfer failed
   (IFSelect status not RetDone), 63 read/transfer produced no shape (empty or
   unsupported file), 64 Data Exchange runtime (TKDESTEP/TKDEIGES) not available,
   99 exception. */
SAM_OCCT_API int sam_occt_shape_export_step(void* shape_handle, const char* path);

SAM_OCCT_API int sam_occt_shape_export_iges(void* shape_handle, const char* path);

SAM_OCCT_API int sam_occt_shape_import_step(const char* path, void** shape_handle);

SAM_OCCT_API int sam_occt_shape_import_iges(const char* path, void** shape_handle);

SAM_OCCT_API int sam_occt_build_cell_complex(
    const double* coordinates,
    int point_count,
    const int* loop_point_counts,
    int loop_count,
    const int* face_loop_counts,
    int face_count,
    double tolerance,
    double fuzzy_tolerance,
    int run_parallel,
    int avoid_internal_shapes,
    void** result_handle);

SAM_OCCT_API int sam_occt_shells_difference(
    const double* target_coordinates,
    int target_point_count,
    const int* target_loop_point_counts,
    int target_loop_count,
    const int* target_face_loop_counts,
    int target_face_count,
    const int* target_shell_face_counts,
    int target_shell_count,
    const double* cutter_coordinates,
    int cutter_point_count,
    const int* cutter_loop_point_counts,
    int cutter_loop_count,
    const int* cutter_face_loop_counts,
    int cutter_face_count,
    const int* cutter_shell_face_counts,
    int cutter_shell_count,
    double tolerance,
    double fuzzy_tolerance,
    int run_parallel,
    void** result_handle);

SAM_OCCT_API int sam_occt_shells_intersection(
    const double* target_coordinates,
    int target_point_count,
    const int* target_loop_point_counts,
    int target_loop_count,
    const int* target_face_loop_counts,
    int target_face_count,
    const int* target_shell_face_counts,
    int target_shell_count,
    const double* tool_coordinates,
    int tool_point_count,
    const int* tool_loop_point_counts,
    int tool_loop_count,
    const int* tool_face_loop_counts,
    int tool_face_count,
    const int* tool_shell_face_counts,
    int tool_shell_count,
    double tolerance,
    double fuzzy_tolerance,
    int run_parallel,
    void** result_handle);

SAM_OCCT_API int sam_occt_shells_union(
    const double* coordinates,
    int point_count,
    const int* loop_point_counts,
    int loop_count,
    const int* face_loop_counts,
    int face_count,
    const int* shell_face_counts,
    int shell_count,
    double tolerance,
    double fuzzy_tolerance,
    int run_parallel,
    void** result_handle);

SAM_OCCT_API int sam_occt_shells_repair(
    const double* coordinates,
    int point_count,
    const int* loop_point_counts,
    int loop_count,
    const int* face_loop_counts,
    int face_count,
    const int* shell_face_counts,
    int shell_count,
    double tolerance,
    double fuzzy_tolerance,
    int run_parallel,
    double min_area,
    void** result_handle);

SAM_OCCT_API int sam_occt_triangulate(
    const double* coordinates,
    int point_count,
    const int* loop_point_counts,
    int loop_count,
    const int* face_loop_counts,
    int face_count,
    double linear_deflection,
    double angular_deflection,
    int relative_deflection,
    double tolerance,
    void** result_handle);

SAM_OCCT_API int sam_occt_merge_coplanar(
    const double* coordinates,
    int point_count,
    const int* loop_point_counts,
    int loop_count,
    const int* face_loop_counts,
    int face_count,
    double tolerance,
    double angular_tolerance,
    void** result_handle);

SAM_OCCT_API void sam_occt_free_result(void* result_handle);

SAM_OCCT_API int sam_occt_result_cell_count(void* result_handle);

SAM_OCCT_API int sam_occt_result_cell_face_count(void* result_handle, int cell_index);

SAM_OCCT_API double sam_occt_result_cell_volume(void* result_handle, int cell_index);

SAM_OCCT_API int sam_occt_result_cell_center(
    void* result_handle,
    int cell_index,
    double* x,
    double* y,
    double* z);

SAM_OCCT_API int sam_occt_result_face_loop_count(void* result_handle, int cell_index, int face_index);

SAM_OCCT_API int sam_occt_result_face_key(void* result_handle, int cell_index, int face_index);

SAM_OCCT_API int sam_occt_result_loop_point_count(void* result_handle, int cell_index, int face_index, int loop_index);

SAM_OCCT_API int sam_occt_result_point(
    void* result_handle,
    int cell_index,
    int face_index,
    int loop_index,
    int point_index,
    double* x,
    double* y,
    double* z);

/* ---- ABI v4: observational history / naked wires / tolerance drift ----
   Additive (probe/degrade like the v3 glue path): a stale pre-v4 native build
   lacks these symbols, so managed callers probe sam_occt_abi_version >= 4 and
   fall back to the geometric NearestSourceIndex heuristic. None of these change
   the geometry the ops produce - they only expose provenance already computed.
   Design record: docs/P3_ABI_V4_NATIVE_HISTORY_DESIGN_REVIEW.md.

   HISTORY (on the existing result handle; captured by sam_occt_build_cell_complex
   and sam_occt_merge_coplanar). Output faces are addressed by FLAT ORDINAL - the
   cell-major, face-minor enumeration of the result (cell 0's faces, then cell 1's,
   ...), identical to walking sam_occt_result_cell_count / _cell_face_count. A face
   shared by two cells has two ordinals and is listed under both. Input faces are
   addressed by the caller's flattened-array face order. */

/* 1 = history captured, 0 = not captured, -1 = invalid handle. */
SAM_OCCT_API int sam_occt_result_history_available(void* result_handle);

/* Number of input faces the producing op saw. -1 invalid handle, 0 unavailable. */
SAM_OCCT_API int sam_occt_result_history_input_count(void* result_handle);

/* Per-input-face record sizes. deleted 1/0. Status: 0 ok, 10 null out pointer,
   40 index out of range / history unavailable, 50 invalid handle. */
SAM_OCCT_API int sam_occt_result_history_face(
    void* result_handle,
    int input_index,
    int* deleted,
    int* modified_count,
    int* generated_count);

/* Fills caller-allocated arrays with the flat output ordinals this input face was
   modified into / generated. Capacities are explicit - native writes at most
   *_capacity entries and returns 11 when a capacity is below the corresponding
   count (re-query counts and retry). An array may be null only when its count is
   0. Status: 0 ok, 10 null array with non-zero count, 11 insufficient capacity,
   40 index out of range / history unavailable, 50 invalid handle. */
SAM_OCCT_API int sam_occt_result_history_entries(
    void* result_handle,
    int input_index,
    int* modified_ordinals,
    int modified_capacity,
    int* generated_ordinals,
    int generated_capacity);

/* Max / average sub-shape tolerance stored at op time for the one-shot face-list
   builders. Both 0 when the op did not capture it. Status: 0 ok, 10 null out
   pointer, 50 invalid handle. */
SAM_OCCT_API int sam_occt_result_max_tolerance(
    void* result_handle,
    double* max_tolerance,
    double* average_tolerance);

/* NAKED WIRES (on the existing validation handle; grouped from the same
   ShapeAnalysis_FreeBounds pass sam_occt_shape_validate already runs). */

/* Number of free-boundary wires (open + closed). -1 invalid handle. */
SAM_OCCT_API int sam_occt_validation_wire_count(void* validation_handle);

/* point_count = ordered polyline vertices (closing vertex NOT duplicated);
   edge_count = point_count for a closed wire, point_count - 1 for an open wire;
   is_closed 1/0. Status: 0 ok, 10 null out pointer, 40 index out of range,
   50 invalid handle. */
SAM_OCCT_API int sam_occt_validation_wire_info(
    void* validation_handle,
    int wire_index,
    int* point_count,
    int* edge_count,
    int* is_closed);

/* Ordered polyline vertex. Status: 0 ok, 10 null out pointer, 40 index out of
   range, 50 invalid handle. */
SAM_OCCT_API int sam_occt_validation_wire_point(
    void* validation_handle,
    int wire_index,
    int point_index,
    double* x,
    double* y,
    double* z);

/* Owning face index of free edge `edge_index` (its position in the validated
   shape's face enumeration order), or -1 when unknown. Best-effort - managed
   callers must tolerate -1. Status: 0 ok, 10 null out pointer, 40 index out of
   range, 50 invalid handle. */
SAM_OCCT_API int sam_occt_validation_wire_edge_owner(
    void* validation_handle,
    int wire_index,
    int edge_index,
    int* input_face_index);

/* TOLERANCE DRIFT on a live shape handle. subshape_type: 0 any, 1 vertex,
   2 edge, 3 face. Status: 0 ok, 10 null out pointer, 50 invalid handle,
   99 exception. */
SAM_OCCT_API int sam_occt_shape_max_tolerance(
    void* shape_handle,
    int subshape_type,
    double* max_tolerance,
    double* average_tolerance);

#ifdef __cplusplus
}
#endif
