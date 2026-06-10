// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

// Persistent sam_occt_shape handle family (issue #14). The opaque Shape struct
// (OcctNativeCore.h) owns a live TopoDS_Shape so operations can chain natively
// without managed round-trips. Issue #20 (STEP/IGES via XCAF) can extend the
// struct without breaking the ABI - only void* crosses the boundary.

#include "sam_occt.h"
#include "OcctNativeCore.h"

#include <BRep_Builder.hxx>
#include <TopAbs.hxx>
#include <TopExp_Explorer.hxx>
#include <TopTools_ListOfShape.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Compound.hxx>
#include <TopoDS_Shape.hxx>
#include <TopoDS_Solid.hxx>

#include <memory>
#include <vector>

using namespace sam_occt;

namespace
{
    int wrap_shape(const TopoDS_Shape& shape, void** shape_handle)
    {
        TopExp_Explorer solid_explorer(shape, TopAbs_SOLID);
        if (!solid_explorer.More())
        {
            return 40;
        }

        std::unique_ptr<Shape> result(new Shape());
        result->shape = shape;
        *shape_handle = result.release();
        return 0;
    }
}

extern "C" {

int sam_occt_abi_version(void)
{
    return 2;
}

void sam_occt_shape_release(void* shape_handle)
{
    Shape* shape = static_cast<Shape*>(shape_handle);
    if (shape == nullptr || shape->magic != shape_magic)
    {
        // Null, already released or not a shape handle - never delete through
        // a mistyped pointer.
        return;
    }

    shape->magic = 0;
    delete shape;
}

int sam_occt_shape_create_cell_complex(
    const double* coordinates,
    int point_count,
    const int* loop_point_counts,
    int loop_count,
    const int* face_loop_counts,
    int face_count,
    double fuzzy_tolerance,
    int run_parallel,
    int avoid_internal_shapes,
    void** shape_handle)
{
    if (shape_handle != nullptr)
    {
        *shape_handle = nullptr;
    }

    if (coordinates == nullptr || loop_point_counts == nullptr || face_loop_counts == nullptr || shape_handle == nullptr)
    {
        return 10;
    }

    if (point_count <= 0 || loop_count <= 0 || face_count <= 0)
    {
        return 11;
    }

    try
    {
        TopTools_ListOfShape faces;
        if (!make_faces_from_arrays(coordinates, loop_point_counts, face_loop_counts, face_count, faces))
        {
            return 20;
        }

        TopoDS_Shape shape;
        const int volume_status = make_volume_from_faces(faces, fuzzy_tolerance, run_parallel, avoid_internal_shapes, shape);
        if (volume_status != 0)
        {
            return volume_status;
        }

        return wrap_shape(shape, shape_handle);
    }
    catch (...)
    {
        return 99;
    }
}

int sam_occt_shape_create_shells(
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
    void** shape_handle)
{
    if (shape_handle != nullptr)
    {
        *shape_handle = nullptr;
    }

    if (coordinates == nullptr || loop_point_counts == nullptr || face_loop_counts == nullptr || shell_face_counts == nullptr || shape_handle == nullptr)
    {
        return 10;
    }

    if (point_count <= 0 || loop_count <= 0 || face_count <= 0 || shell_count <= 0)
    {
        return 11;
    }

    try
    {
        std::vector<TopoDS_Solid> solids;
        if (!build_shell_solids(
                coordinates,
                loop_point_counts,
                face_loop_counts,
                shell_face_counts,
                shell_count,
                fuzzy_tolerance,
                run_parallel,
                solids))
        {
            return 20;
        }

        TopoDS_Compound compound;
        BRep_Builder builder;
        builder.MakeCompound(compound);
        for (const TopoDS_Solid& solid : solids)
        {
            builder.Add(compound, solid);
        }

        return wrap_shape(compound, shape_handle);
    }
    catch (...)
    {
        return 99;
    }
}

int sam_occt_shape_solid_count(void* shape_handle)
{
    const Shape* shape = as_shape(shape_handle);
    if (shape == nullptr)
    {
        return -1;
    }

    try
    {
        int count = 0;
        for (TopExp_Explorer solid_explorer(shape->shape, TopAbs_SOLID); solid_explorer.More(); solid_explorer.Next())
        {
            ++count;
        }

        return count;
    }
    catch (...)
    {
        return -99;
    }
}

int sam_occt_shape_decode(
    void* shape_handle,
    double tolerance,
    void** result_handle)
{
    if (result_handle != nullptr)
    {
        *result_handle = nullptr;
    }

    if (result_handle == nullptr)
    {
        return 10;
    }

    Shape* shape = as_shape(shape_handle);
    if (shape == nullptr)
    {
        return 50;
    }

    try
    {
        // Handles store already fixed shapes (ShapeFix runs when the shape is
        // created), so extraction here is direct - matching what the legacy
        // entry points decode after their own per-op ShapeFix.
        std::unique_ptr<Result> result(new Result());
        for (TopExp_Explorer solid_explorer(shape->shape, TopAbs_SOLID); solid_explorer.More(); solid_explorer.Next())
        {
            append_solid_to_result(TopoDS::Solid(solid_explorer.Current()), *result, tolerance);
        }

        if (result->cells.empty())
        {
            return 40;
        }

        *result_handle = result.release();
        return 0;
    }
    catch (...)
    {
        return 99;
    }
}

}
