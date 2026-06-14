// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

// Footprint extrusion (issue #30). Builds planar boundary loops into faces
// (the same flattened-array contract as sam_occt_shape_create_cell_complex)
// and linearly extrudes each face by a direction vector with
// BRepPrimAPI_MakePrism, producing one closed solid per footprint. The result
// is wrapped in a persistent sam_occt_shape handle (issue #14) so it feeds
// straight into decode / CreateShells / the boolean + adjacency pipeline.

#include "sam_occt.h"
#include "OcctNativeCore.h"

#include <BRepPrimAPI_MakePrism.hxx>
#include <BRep_Builder.hxx>
#include <ShapeFix_Shape.hxx>
#include <TopExp_Explorer.hxx>
#include <TopTools_ListIteratorOfListOfShape.hxx>
#include <TopTools_ListOfShape.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Compound.hxx>
#include <TopoDS_Shape.hxx>
#include <TopoDS_Solid.hxx>
#include <gp_Vec.hxx>

#include <memory>
#include <vector>

using namespace sam_occt;

namespace
{
    // Local mirrors of the ShapeHandle.cpp helpers (internal linkage), kept here
    // so this translation unit stays self-contained without exporting them.
    void collect_fixed_solids_extrude(const TopoDS_Shape& shape, std::vector<TopoDS_Solid>& solids)
    {
        ShapeFix_Shape shape_fix(shape);
        shape_fix.Perform();
        TopoDS_Shape fixed_shape = shape_fix.Shape();

        for (TopExp_Explorer solid_explorer(fixed_shape, TopAbs_SOLID); solid_explorer.More(); solid_explorer.Next())
        {
            solids.push_back(TopoDS::Solid(solid_explorer.Current()));
        }
    }

    int wrap_solids_extrude(const std::vector<TopoDS_Solid>& solids, void** shape_handle)
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
}

extern "C" {

int sam_occt_extrude(
    const double* coordinates,
    int point_count,
    const int* loop_point_counts,
    int loop_count,
    const int* face_loop_counts,
    int face_count,
    double direction_x,
    double direction_y,
    double direction_z,
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

    if (direction_x == 0.0 && direction_y == 0.0 && direction_z == 0.0)
    {
        return 12;
    }

    try
    {
        TopTools_ListOfShape faces;
        if (!make_faces_from_arrays(coordinates, loop_point_counts, face_loop_counts, face_count, faces))
        {
            return 20;
        }

        const gp_Vec direction(direction_x, direction_y, direction_z);

        std::vector<TopoDS_Solid> solids;
        for (TopTools_ListIteratorOfListOfShape face_iterator(faces); face_iterator.More(); face_iterator.Next())
        {
            BRepPrimAPI_MakePrism prism(face_iterator.Value(), direction);
            if (!prism.IsDone())
            {
                return 30;
            }

            collect_fixed_solids_extrude(prism.Shape(), solids);
        }

        return wrap_solids_extrude(solids, shape_handle);
    }
    catch (...)
    {
        return 99;
    }
}

}
