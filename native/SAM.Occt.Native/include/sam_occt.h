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

SAM_OCCT_API void sam_occt_free_result(void* result_handle);

SAM_OCCT_API int sam_occt_result_cell_count(void* result_handle);

SAM_OCCT_API int sam_occt_result_cell_face_count(void* result_handle, int cell_index);

SAM_OCCT_API double sam_occt_result_cell_volume(void* result_handle, int cell_index);

SAM_OCCT_API int sam_occt_result_face_loop_count(void* result_handle, int cell_index, int face_index);

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
