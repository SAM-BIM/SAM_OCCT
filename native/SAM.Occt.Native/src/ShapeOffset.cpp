// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

// Surface offset & wall-thickening (issue #29). Two persistent-handle ops:
//   * sam_occt_shape_offset      - grow/shrink a solid's skin by a signed
//                                  distance (BRepOffsetAPI_MakeOffsetShape).
//   * sam_occt_shape_thick_solid - hollow a solid into a wall of the given
//                                  thickness (BRepOffsetAPI_MakeThickSolid).
// These move between analytical centre-line geometry and physical construction
// thickness for energy models. Offsetting is the most failure-prone OCCT
// operation, so each solid is processed independently and a failure surfaces as
// a clean status 30 rather than corrupting the batch.

#include "sam_occt.h"
#include "OcctNativeCore.h"

#include <BRepOffsetAPI_MakeOffsetShape.hxx>
#include <BRepOffsetAPI_MakeThickSolid.hxx>
#include <BRep_Builder.hxx>
#include <ShapeFix_Shape.hxx>
#include <TopExp_Explorer.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Compound.hxx>
#include <TopoDS_Shape.hxx>
#include <TopoDS_Solid.hxx>

#include <memory>
#include <vector>

using namespace sam_occt;

namespace
{
    // Local mirrors of the ShapeHandle.cpp helpers (internal linkage), kept here
    // so this translation unit stays self-contained without exporting them.
    std::vector<TopoDS_Solid> collect_solids_offset(const TopoDS_Shape& shape)
    {
        std::vector<TopoDS_Solid> solids;
        for (TopExp_Explorer solid_explorer(shape, TopAbs_SOLID); solid_explorer.More(); solid_explorer.Next())
        {
            solids.push_back(TopoDS::Solid(solid_explorer.Current()));
        }

        return solids;
    }

    void collect_fixed_solids_offset(const TopoDS_Shape& shape, std::vector<TopoDS_Solid>& solids)
    {
        ShapeFix_Shape shape_fix(shape);
        shape_fix.Perform();
        TopoDS_Shape fixed_shape = shape_fix.Shape();

        for (TopExp_Explorer solid_explorer(fixed_shape, TopAbs_SOLID); solid_explorer.More(); solid_explorer.Next())
        {
            solids.push_back(TopoDS::Solid(solid_explorer.Current()));
        }
    }

    int wrap_solids_offset(const std::vector<TopoDS_Solid>& solids, void** shape_handle)
    {
        if (solids.empty())
        {
            return 40;
        }

        TopoDS_Compound compound;
        BRep_Builder builder;
        builder.MakeCompound(compound);
        for (const TopoDS_Solid& solid : solids)
        {
            builder.Add(compound, solid);
        }

        std::unique_ptr<Shape> result(new Shape());
        result->shape = compound;
        *shape_handle = result.release();
        return 0;
    }

    int validate_offset_arguments(void** shape_handle_out, void* shape_handle, Shape*& shape)
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

int sam_occt_shape_offset(
    void* shape_handle,
    double offset,
    double tolerance,
    void** shape_handle_out)
{
    Shape* shape = nullptr;
    const int argument_status = validate_offset_arguments(shape_handle_out, shape_handle, shape);
    if (argument_status != 0)
    {
        return argument_status;
    }

    if (offset == 0.0)
    {
        return 12;
    }

    try
    {
        std::vector<TopoDS_Solid> solids = collect_solids_offset(shape->shape);
        if (solids.empty())
        {
            return 40;
        }

        const double safe_tolerance = tolerance > 0 ? tolerance : 1e-7;

        std::vector<TopoDS_Solid> result_solids;
        for (const TopoDS_Solid& solid : solids)
        {
            BRepOffsetAPI_MakeOffsetShape make_offset;
            make_offset.PerformBySimple(solid, offset);
            if (!make_offset.IsDone())
            {
                return 30;
            }

            (void)safe_tolerance;
            collect_fixed_solids_offset(make_offset.Shape(), result_solids);
        }

        return wrap_solids_offset(result_solids, shape_handle_out);
    }
    catch (...)
    {
        return 99;
    }
}

int sam_occt_shape_thick_solid(
    void* shape_handle,
    double thickness,
    double tolerance,
    void** shape_handle_out)
{
    Shape* shape = nullptr;
    const int argument_status = validate_offset_arguments(shape_handle_out, shape_handle, shape);
    if (argument_status != 0)
    {
        return argument_status;
    }

    if (thickness == 0.0)
    {
        return 12;
    }

    try
    {
        std::vector<TopoDS_Solid> solids = collect_solids_offset(shape->shape);
        if (solids.empty())
        {
            return 40;
        }

        const double safe_tolerance = tolerance > 0 ? tolerance : 1e-7;

        std::vector<TopoDS_Solid> result_solids;
        for (const TopoDS_Solid& solid : solids)
        {
            BRepOffsetAPI_MakeThickSolid make_thick;
            make_thick.MakeThickSolidBySimple(solid, thickness);
            if (!make_thick.IsDone())
            {
                return 30;
            }

            (void)safe_tolerance;
            collect_fixed_solids_offset(make_thick.Shape(), result_solids);
        }

        return wrap_solids_offset(result_solids, shape_handle_out);
    }
    catch (...)
    {
        return 99;
    }
}

}
