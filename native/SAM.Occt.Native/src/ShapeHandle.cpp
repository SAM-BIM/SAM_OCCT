// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

// Persistent sam_occt_shape handle family (issue #14). The opaque Shape struct
// (OcctNativeCore.h) owns a live TopoDS_Shape so operations can chain natively
// without managed round-trips. Issue #20 (STEP/IGES via XCAF) can extend the
// struct without breaking the ABI - only void* crosses the boundary.

#include "sam_occt.h"
#include "OcctNativeCore.h"

#include <BRepAlgoAPI_Common.hxx>
#include <BRepClass3d_SolidClassifier.hxx>
#include <BRepAlgoAPI_Cut.hxx>
#include <BRepAlgoAPI_Fuse.hxx>
#include <BRep_Builder.hxx>
#include <ShapeFix_Shape.hxx>
#include <TopAbs.hxx>
#include <TopExp_Explorer.hxx>
#include <TopTools_ListOfShape.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Compound.hxx>
#include <TopoDS_Shape.hxx>
#include <TopoDS_Solid.hxx>
#include <gp_Pnt.hxx>

#include <cstddef>
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

    std::vector<TopoDS_Solid> collect_solids(const TopoDS_Shape& shape)
    {
        std::vector<TopoDS_Solid> solids;
        for (TopExp_Explorer solid_explorer(shape, TopAbs_SOLID); solid_explorer.More(); solid_explorer.Next())
        {
            solids.push_back(TopoDS::Solid(solid_explorer.Current()));
        }

        return solids;
    }

    // Mirrors append_shape_solids_to_result for shapes that stay native:
    // ShapeFix the boolean output, then keep only its solids. The handles
    // always store already fixed shapes so decode can extract directly.
    void collect_fixed_solids(const TopoDS_Shape& shape, std::vector<TopoDS_Solid>& solids)
    {
        ShapeFix_Shape shape_fix(shape);
        shape_fix.Perform();
        TopoDS_Shape fixed_shape = shape_fix.Shape();

        for (TopExp_Explorer solid_explorer(fixed_shape, TopAbs_SOLID); solid_explorer.More(); solid_explorer.Next())
        {
            solids.push_back(TopoDS::Solid(solid_explorer.Current()));
        }
    }

    TopoDS_Compound make_compound(const std::vector<TopoDS_Solid>& solids)
    {
        TopoDS_Compound compound;
        BRep_Builder builder;
        builder.MakeCompound(compound);
        for (const TopoDS_Solid& solid : solids)
        {
            builder.Add(compound, solid);
        }

        return compound;
    }

    int validate_op_arguments(void** shape_handle_out, void* shape_handle, Shape*& shape)
    {
        if (shape_handle_out != nullptr)
        {
            *shape_handle_out = nullptr;
        }

        if (shape_handle_out == nullptr)
        {
            return 10;
        }

        shape = as_shape(shape_handle);
        if (shape == nullptr)
        {
            return 50;
        }

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

int sam_occt_shape_union(
    void* shape_handle,
    double fuzzy_tolerance,
    int run_parallel,
    void** shape_handle_out)
{
    Shape* shape = nullptr;
    const int argument_status = validate_op_arguments(shape_handle_out, shape_handle, shape);
    if (argument_status != 0)
    {
        return argument_status;
    }

    try
    {
        std::vector<TopoDS_Solid> solids = collect_solids(shape->shape);
        if (solids.empty())
        {
            return 40;
        }

        // Single solid: pass through without Fuse, matching the legacy
        // sam_occt_shells_union branch.
        if (solids.size() == 1)
        {
            return wrap_shape(solids[0], shape_handle_out);
        }

        TopTools_ListOfShape arguments;
        arguments.Append(solids[0]);

        TopTools_ListOfShape tools;
        for (std::size_t i = 1; i < solids.size(); ++i)
        {
            tools.Append(solids[i]);
        }

        BRepAlgoAPI_Fuse fuse;
        fuse.SetArguments(arguments);
        fuse.SetTools(tools);
        fuse.SetFuzzyValue(fuzzy_tolerance);
        fuse.SetRunParallel(run_parallel != 0);
        fuse.Build();

        if (fuse.HasErrors())
        {
            return 30;
        }

        std::vector<TopoDS_Solid> fused_solids;
        collect_fixed_solids(fuse.Shape(), fused_solids);
        return wrap_shape(make_compound(fused_solids), shape_handle_out);
    }
    catch (...)
    {
        return 99;
    }
}

int sam_occt_shape_difference(
    void* target_shape_handle,
    void* cutter_shape_handle,
    double fuzzy_tolerance,
    int run_parallel,
    void** shape_handle_out)
{
    Shape* target_shape = nullptr;
    const int argument_status = validate_op_arguments(shape_handle_out, target_shape_handle, target_shape);
    if (argument_status != 0)
    {
        return argument_status;
    }

    Shape* cutter_shape = as_shape(cutter_shape_handle);
    if (cutter_shape == nullptr)
    {
        return 50;
    }

    try
    {
        std::vector<TopoDS_Solid> target_solids = collect_solids(target_shape->shape);
        std::vector<TopoDS_Solid> cutter_solids = collect_solids(cutter_shape->shape);
        if (target_solids.empty() || cutter_solids.empty())
        {
            return 40;
        }

        TopTools_ListOfShape tools;
        for (const TopoDS_Solid& cutter_solid : cutter_solids)
        {
            tools.Append(cutter_solid);
        }

        std::vector<TopoDS_Solid> result_solids;
        for (const TopoDS_Solid& target_solid : target_solids)
        {
            TopTools_ListOfShape arguments;
            arguments.Append(target_solid);

            BRepAlgoAPI_Cut cut;
            cut.SetArguments(arguments);
            cut.SetTools(tools);
            cut.SetFuzzyValue(fuzzy_tolerance);
            cut.SetRunParallel(run_parallel != 0);
            cut.Build();

            if (cut.HasErrors())
            {
                return 30;
            }

            collect_fixed_solids(cut.Shape(), result_solids);
        }

        return wrap_shape(make_compound(result_solids), shape_handle_out);
    }
    catch (...)
    {
        return 99;
    }
}

int sam_occt_shape_intersection(
    void* target_shape_handle,
    void* tool_shape_handle,
    double fuzzy_tolerance,
    int run_parallel,
    void** shape_handle_out)
{
    Shape* target_shape = nullptr;
    const int argument_status = validate_op_arguments(shape_handle_out, target_shape_handle, target_shape);
    if (argument_status != 0)
    {
        return argument_status;
    }

    Shape* tool_shape = as_shape(tool_shape_handle);
    if (tool_shape == nullptr)
    {
        return 50;
    }

    try
    {
        std::vector<TopoDS_Solid> target_solids = collect_solids(target_shape->shape);
        std::vector<TopoDS_Solid> tool_solids = collect_solids(tool_shape->shape);
        if (target_solids.empty() || tool_solids.empty())
        {
            return 40;
        }

        TopTools_ListOfShape tools;
        for (const TopoDS_Solid& tool_solid : tool_solids)
        {
            tools.Append(tool_solid);
        }

        std::vector<TopoDS_Solid> result_solids;
        for (const TopoDS_Solid& target_solid : target_solids)
        {
            TopTools_ListOfShape arguments;
            arguments.Append(target_solid);

            BRepAlgoAPI_Common common;
            common.SetArguments(arguments);
            common.SetTools(tools);
            common.SetFuzzyValue(fuzzy_tolerance);
            common.SetRunParallel(run_parallel != 0);
            common.Build();

            if (common.HasErrors())
            {
                return 30;
            }

            collect_fixed_solids(common.Shape(), result_solids);
        }

        return wrap_shape(make_compound(result_solids), shape_handle_out);
    }
    catch (...)
    {
        return 99;
    }
}

int sam_occt_shape_repair(
    void* shape_handle,
    double fuzzy_tolerance,
    int run_parallel,
    double min_area,
    void** shape_handle_out)
{
    (void)fuzzy_tolerance;

    Shape* shape = nullptr;
    const int argument_status = validate_op_arguments(shape_handle_out, shape_handle, shape);
    if (argument_status != 0)
    {
        return argument_status;
    }

    try
    {
        std::vector<TopoDS_Solid> solids = collect_solids(shape->shape);
        if (solids.empty())
        {
            return 40;
        }

        std::vector<TopoDS_Solid> result_solids;
        for (const TopoDS_Solid& solid : solids)
        {
            const std::size_t before = result_solids.size();

            TopoDS_Shape repaired = remove_small_faces(solid, min_area, run_parallel);
            collect_fixed_solids(repaired, result_solids);

            // Healing may yield nothing usable; keep the original solid so a
            // shell is never silently dropped during repair.
            if (result_solids.size() == before)
            {
                result_solids.push_back(solid);
            }
        }

        return wrap_shape(make_compound(result_solids), shape_handle_out);
    }
    catch (...)
    {
        return 99;
    }
}

int sam_occt_shape_make_volume(
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
    void** shape_handle_out)
{
    Shape* shape = nullptr;
    const int argument_status = validate_op_arguments(shape_handle_out, shape_handle, shape);
    if (argument_status != 0)
    {
        return argument_status;
    }

    const bool has_extra_faces = coordinates != nullptr && loop_point_counts != nullptr && face_loop_counts != nullptr
        && point_count > 0 && loop_count > 0 && face_count > 0;

    try
    {
        TopTools_ListOfShape faces;
        for (TopExp_Explorer face_explorer(shape->shape, TopAbs_FACE); face_explorer.More(); face_explorer.Next())
        {
            faces.Append(face_explorer.Current());
        }

        if (has_extra_faces && !make_faces_from_arrays(coordinates, loop_point_counts, face_loop_counts, face_count, faces))
        {
            return 20;
        }

        if (faces.IsEmpty())
        {
            return 20;
        }

        TopoDS_Shape volume_shape;
        const int volume_status = make_volume_from_faces(faces, fuzzy_tolerance, run_parallel, avoid_internal_shapes, volume_shape);
        if (volume_status != 0)
        {
            return volume_status;
        }

        return wrap_shape(volume_shape, shape_handle_out);
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

int sam_occt_shape_point_in_solid(
    void* shape_handle,
    double x,
    double y,
    double z,
    double tolerance)
{
    const Shape* shape = as_shape(shape_handle);
    if (shape == nullptr)
    {
        return -1;
    }

    try
    {
        const gp_Pnt point(x, y, z);
        const double safe_tolerance = tolerance > 0 ? tolerance : 1e-9;

        for (TopExp_Explorer solid_explorer(shape->shape, TopAbs_SOLID); solid_explorer.More(); solid_explorer.Next())
        {
            BRepClass3d_SolidClassifier classifier(TopoDS::Solid(solid_explorer.Current()));
            classifier.Perform(point, safe_tolerance);

            const TopAbs_State state = classifier.State();
            if (state == TopAbs_IN || state == TopAbs_ON)
            {
                return 1;
            }
        }

        return 0;
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
