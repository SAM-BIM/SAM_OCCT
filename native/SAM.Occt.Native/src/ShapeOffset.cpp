// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

// Surface offset & wall-thickening (issue #29). Two persistent-handle ops:
//   * sam_occt_shape_offset      - grow/shrink a solid's skin by a signed
//                                  distance (BRepOffsetAPI_MakeOffsetShape,
//                                  PerformByJoin).
//   * sam_occt_shape_thick_solid - hollow a solid into a wall of the given
//                                  thickness, built as (outer solid) - (inner
//                                  solid) so the result is a genuine hollow
//                                  solid with a cavity, not just an offset.
// These move between analytical centre-line geometry and physical construction
// thickness for energy models. Offsetting is the most failure-prone OCCT
// operation, so each solid is processed independently and a failure surfaces as
// a clean status 30 rather than corrupting the batch.

#include "sam_occt.h"
#include "OcctNativeCore.h"

#include <BRepAlgoAPI_Cut.hxx>
#include <BRepOffsetAPI_MakeOffsetShape.hxx>
#include <BRepOffset_Mode.hxx>
#include <BRep_Builder.hxx>
#include <GeomAbs_JoinType.hxx>
#include <OSD.hxx>
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

    // Convert OS-level signals / hardware faults (access violations raised deep
    // inside the OCCT offset & boolean kernels on pathological input) into
    // catchable Standard_Failure exceptions on the calling thread. Combined with
    // the /EHa build option this stops such a fault from tearing down the host
    // process (e.g. Rhino) - it surfaces as a clean status instead. Idempotent
    // and cheap; called at each entry point because the translation is installed
    // per-thread.
    void arm_signal_translation()
    {
        OSD::SetSignal(Standard_False);
    }

    // Offsets a single solid's skin by a signed distance via the join
    // algorithm (mitred corners), returning false if OCCT cannot offset it.
    // Shared by the offset op and by the thick-solid op's inner/outer surface.
    bool try_offset_solid(const TopoDS_Solid& solid, double distance, double tolerance, TopoDS_Shape& result_shape)
    {
        try
        {
            BRepOffsetAPI_MakeOffsetShape make_offset;
            make_offset.PerformByJoin(
                solid,
                distance,
                tolerance,
                BRepOffset_Skin,
                Standard_False,
                Standard_False,
                GeomAbs_Intersection);

            if (!make_offset.IsDone())
            {
                return false;
            }

            result_shape = make_offset.Shape();
            return true;
        }
        catch (...)
        {
            // PerformByJoin, IsDone and Shape can all throw (or, with /EHa, fault)
            // deep inside the offset kernel on pathological input - guard the whole
            // sequence so one bad solid is skipped, not the entire batch.
            return false;
        }
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

    arm_signal_translation();

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
            // try_offset_solid uses PerformByJoin (not PerformBySimple): the
            // "simple" algorithm skips surface-intersection computation, so on a
            // closed solid adjacent offset faces are never trimmed back to a
            // shared corner - the skin tears / overlaps at every vertex (issue
            // #29 corner artefacts). PerformByJoin supports solids directly and
            // mitres the corners. Offsetting is failure-prone (self-intersections,
            // >3-edge vertices); skip a solid OCCT cannot offset rather than
            // abandoning the whole batch.
            try
            {
                TopoDS_Shape offset_shape;
                if (!try_offset_solid(solid, offset, safe_tolerance, offset_shape))
                {
                    continue;
                }

                collect_fixed_solids_offset(offset_shape, result_solids);
            }
            catch (...)
            {
                // Also guards the ShapeFix in collect_fixed_solids_offset; skip
                // this solid rather than failing the whole batch.
                continue;
            }
        }

        // Status 30 only when nothing could be offset at all.
        if (result_solids.empty())
        {
            return 30;
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

    arm_signal_translation();

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
            // Build the wall explicitly as (outer solid) - (inner solid) so the
            // result is a genuine hollow solid with both a boundary and a cavity.
            // MakeThickSolidByJoin with an empty closing-faces list does NOT carve
            // a cavity - it returns only the outward offset, so the decoded SAM
            // shell was identical to ShellsOffset (issue #29 follow-up). A boolean
            // cut between the original surface and its parallel keeps both face
            // sets, so the wall reads as a real construction / plenum shell.
            //
            //   thickness > 0 : wall on the OUTSIDE (outer = original grown,
            //                   cavity = original boundary).
            //   thickness < 0 : wall on the INSIDE  (outer = original boundary,
            //                   cavity = original shrunk inward).
            // (Matches the SAMOCCT.ShellsThicken node: positive = outward.)
            TopoDS_Shape outer_shape;
            TopoDS_Shape inner_shape;
            if (thickness > 0.0)
            {
                inner_shape = solid;
                if (!try_offset_solid(solid, thickness, safe_tolerance, outer_shape))
                {
                    continue;
                }
            }
            else
            {
                outer_shape = solid;
                if (!try_offset_solid(solid, thickness, safe_tolerance, inner_shape))
                {
                    continue;
                }
            }

            try
            {
                BRepAlgoAPI_Cut cut(outer_shape, inner_shape);
                cut.SetFuzzyValue(safe_tolerance);
                cut.Build();
                if (cut.HasErrors())
                {
                    continue;
                }

                collect_fixed_solids_offset(cut.Shape(), result_solids);
            }
            catch (...)
            {
                // Thickening is failure-prone; skip a solid it cannot hollow
                // rather than abandoning the whole batch.
                continue;
            }
        }

        // Status 30 only when nothing could be thickened at all.
        if (result_solids.empty())
        {
            return 30;
        }

        return wrap_solids_offset(result_solids, shape_handle_out);
    }
    catch (...)
    {
        return 99;
    }
}

}
