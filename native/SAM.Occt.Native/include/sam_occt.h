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

/* Decodes every solid of the shape into a regular result handle consumed by
   the sam_occt_result_* accessors and freed with sam_occt_free_result. Face
   topology keys are quantized with `tolerance` and are only comparable within
   one decoded result. Status: 0 ok, 10 input, 40 no cells, 50 invalid shape
   handle, 99 exception. */
SAM_OCCT_API int sam_occt_shape_decode(
    void* shape_handle,
    double tolerance,
    void** result_handle);

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

#ifdef __cplusplus
}
#endif
