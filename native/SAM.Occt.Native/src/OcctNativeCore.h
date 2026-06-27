// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

// Private shared core for the SAM.Occt.Native translation units. Declares the
// decoded-result data model plus the geometry helpers shared between the
// legacy array-based entry points (CellComplexBuilder.cpp) and the persistent
// shape-handle entry points (ShapeHandle.cpp).

#pragma once

#include <TopTools_ListOfShape.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Shape.hxx>
#include <TopoDS_Solid.hxx>

#include <cstdint>
#include <map>
#include <string>
#include <vector>

namespace sam_occt
{
    struct Point
    {
        double x = 0;
        double y = 0;
        double z = 0;
    };

    struct Loop
    {
        std::vector<Point> points;
    };

    struct Face
    {
        std::vector<Loop> loops;
        int key = 0;
    };

    struct Cell
    {
        std::vector<Face> faces;
        double volume = 0;
    };

    struct Result
    {
        std::vector<Cell> cells;
        std::map<std::string, int> face_keys;
        int next_face_key = 1;
    };

    // Persistent topology handle (issue #14). Owns a live TopoDS_Shape (a
    // compound of solids, or a single solid) so operations can chain natively.
    // The struct is opaque at the ABI - only void* crosses the boundary - so
    // issue #20 can add an XCAF/XDE document member (names/colours, STEP/IGES)
    // without breaking existing callers.
    constexpr std::uint32_t shape_magic = 0x53414D54; // "SAMT"

    struct Shape
    {
        std::uint32_t magic = shape_magic;
        TopoDS_Shape shape;
    };

    // Validates a void* shape handle. Returns nullptr when the pointer is null,
    // carries the wrong magic tag (e.g. a result handle was passed by mistake)
    // or holds a null TopoDS_Shape.
    inline Shape* as_shape(void* shape_handle)
    {
        Shape* shape = static_cast<Shape*>(shape_handle);
        if (shape == nullptr || shape->magic != shape_magic || shape->shape.IsNull())
        {
            return nullptr;
        }

        return shape;
    }

    // ---- validation & watertightness diagnostics (issue #37 follow-on) ----
    // Categorises the issues BRepCheck_Analyzer / BOPAlgo_ArgumentAnalyzer /
    // ShapeAnalysis_FreeBounds locate so a failed close is actionable instead of
    // an opaque status code. These integer values are part of the ABI - the
    // managed OcctValidationIssueCategory mirrors them one-for-one.
    enum SamOcctValidationCategory
    {
        SAM_OCCT_VALIDATION_UNKNOWN = 0,
        SAM_OCCT_VALIDATION_NAKED_EDGE = 1,        // free boundary edge (open shell)
        SAM_OCCT_VALIDATION_SELF_INTERSECTION = 2, // BOPAlgo_ArgumentAnalyzer self-intersection
        SAM_OCCT_VALIDATION_INVALID_FACE = 3,      // BRepCheck_Analyzer invalid face
        SAM_OCCT_VALIDATION_SMALL_FACE = 4,        // sliver face below the area threshold
        SAM_OCCT_VALIDATION_SMALL_EDGE = 5,        // degenerate / too-small edge
        SAM_OCCT_VALIDATION_INVALID_SHAPE = 6      // other invalid sub-shape
    };

    struct ValidationIssue
    {
        int category = SAM_OCCT_VALIDATION_UNKNOWN;
        double x = 0; // representative location of the issue
        double y = 0;
        double z = 0;
        double size = 0; // naked-edge length / face area / 0 when not applicable
    };

    // Opaque validation report handle (issue #37 follow-on). Owns the located,
    // categorised issues; queried with the sam_occt_validation_* accessors and
    // freed with sam_occt_free_validation. Distinct magic from Shape / Result so
    // a mistyped pointer fails cleanly instead of crashing.
    constexpr std::uint32_t validation_magic = 0x53414D56; // "SAMV"

    struct Validation
    {
        std::uint32_t magic = validation_magic;
        bool is_valid = false;   // BRepCheck valid AND no argument-analyzer faults AND no free bounds
        bool watertight = false; // no free (naked) boundary edges
        std::vector<ValidationIssue> issues;
    };

    inline Validation* as_validation(void* validation_handle)
    {
        Validation* validation = static_cast<Validation*>(validation_handle);
        if (validation == nullptr || validation->magic != validation_magic)
        {
            return nullptr;
        }

        return validation;
    }

    bool make_face(
        const double* coordinates,
        const int* loop_point_counts,
        int& point_offset,
        int& loop_offset,
        int loop_count,
        TopoDS_Face& face);

    // Builds every valid face described by the flattened arrays. Returns false
    // when no face could be built (status 20 at the ABI).
    bool make_faces_from_arrays(
        const double* coordinates,
        const int* loop_point_counts,
        const int* face_loop_counts,
        int face_count,
        TopTools_ListOfShape& faces);

    // BOPAlgo_MakerVolume over independent faces followed by ShapeFix_Shape.
    // Returns 0 on success and 30 on BOP errors; out_shape receives the fixed
    // shape (which may still contain zero solids - callers map that to 40).
    // `glue` selects the BOPAlgo glue mode (0 off, 1 shift, 2 full; issue #37
    // follow-on) - a throughput win on cell complexes with many truly coincident
    // shared walls, but only safe on validated-clean input.
    int make_volume_from_faces(
        const TopTools_ListOfShape& faces,
        double fuzzy_tolerance,
        int run_parallel,
        int avoid_internal_shapes,
        int glue,
        TopoDS_Shape& out_shape);

    bool build_shell_solids(
        const double* coordinates,
        const int* loop_point_counts,
        const int* face_loop_counts,
        const int* shell_face_counts,
        int shell_count,
        double fuzzy_tolerance,
        int run_parallel,
        std::vector<TopoDS_Solid>& solids);

    bool append_solid_to_result(const TopoDS_Solid& solid, Result& result, double tolerance);

    // ShapeFix_Shape on the whole shape, then per-solid extraction. Used by the
    // legacy entry points whose boolean output has not been fixed yet; the
    // shape-handle decode path extracts directly because handles store already
    // fixed shapes.
    void append_shape_solids_to_result(const TopoDS_Shape& shape, Result& result, double tolerance);

    TopoDS_Shape remove_small_faces(const TopoDS_Shape& solid, double min_area, int run_parallel);

    // True when the named delay-loaded Data Exchange DLL (issue #20) can be
    // resolved - looking next to SAM.Occt.Native.dll first, because the host
    // process (e.g. Rhino) does not have that directory on its DLL search
    // path. On success the module is left loaded so the delay-load helper
    // binds to it by name. Lets the STEP/IGES entry points fail with a clean
    // status 64 instead of faulting on the delay-load thunk when the Data
    // Exchange runtime is not deployed. Implemented in DataExchangeRuntime.cpp
    // (the only translation unit that includes windows.h); always true off
    // Windows (no delay-loading there).
    bool data_exchange_runtime_available(const char* dll_name);
}
