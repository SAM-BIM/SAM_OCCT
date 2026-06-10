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
    int make_volume_from_faces(
        const TopTools_ListOfShape& faces,
        double fuzzy_tolerance,
        int run_parallel,
        int avoid_internal_shapes,
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
}
