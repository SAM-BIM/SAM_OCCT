// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

// Watertight sew-and-heal (issue #37). Two entry points:
//   * sam_occt_sew_faces  - sew a flattened face soup (the same array contract as
//                           sam_occt_shape_create_cell_complex) into the tightest
//                           closed shell/solid OCCT can make.
//   * sam_occt_shape_sew  - sew+heal the faces already inside a persistent handle
//                           (after import / offset / boolean).
// Both run BRepBuilderAPI_Sewing -> ShapeFix_Wireframe (FixSmallEdges /
// FixWireGaps) -> ShapeFix_Shell -> ShapeFix_Solid -> ShapeUpgrade_UnifySameDomain.
// This is the closing step before BOPAlgo_MakerVolume: it turns a triangulated /
// near-touching face soup - the #1 cause of MakerVolume status-40 failures - into
// a shape that bounds a volume. Unlike create_cell_complex it does NOT require the
// input to already bound a volume.
//
// make_solid=1 wraps the result like any solid-producing op (status 40 when no
// closed shell formed). make_solid=0 returns the healed - possibly still open -
// shell through the relaxed import wrap so it can be re-fed to MakerVolume.

#include "sam_occt.h"
#include "OcctNativeCore.h"

#include <BRepBuilderAPI_MakeSolid.hxx>
#include <BRepBuilderAPI_Sewing.hxx>
#include <BRep_Builder.hxx>
#include <BRep_Tool.hxx>
#include <OSD.hxx>
#include <ShapeFix_Shell.hxx>
#include <ShapeFix_Solid.hxx>
#include <ShapeFix_Wireframe.hxx>
#include <ShapeUpgrade_UnifySameDomain.hxx>
#include <TopAbs.hxx>
#include <TopExp_Explorer.hxx>
#include <TopTools_ListOfShape.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Compound.hxx>
#include <TopoDS_Shape.hxx>
#include <TopoDS_Shell.hxx>
#include <TopoDS_Solid.hxx>

#include <memory>

using namespace sam_occt;

namespace
{
    // Convert OS-level signals / hardware faults raised deep inside the OCCT
    // sewing & healing kernels on pathological input into catchable
    // Standard_Failure exceptions. Pairs with /EHa (see CMakeLists.txt) so a
    // fault surfaces as a clean status instead of tearing down the host process.
    void arm_signal_translation()
    {
        OSD::SetSignal(Standard_False);
    }

    // Solid-producing wrap (make_solid=1): requires at least one solid, returns 40
    // otherwise - matching sam_occt_shape_offset / _create_cell_complex semantics.
    int wrap_sewn_solid(const TopoDS_Shape& shape, void** shape_handle)
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

    // Relaxed wrap (make_solid=0): the healed shell need not bound a volume yet -
    // it is meant to be re-fed to MakerVolume - so only a null shape is rejected.
    int wrap_sewn_shell(const TopoDS_Shape& shape, void** shape_handle)
    {
        if (shape.IsNull())
        {
            return 30;
        }

        std::unique_ptr<Shape> result(new Shape());
        result->shape = shape;
        *shape_handle = result.release();
        return 0;
    }

    void collect_faces(const TopoDS_Shape& shape, TopTools_ListOfShape& faces)
    {
        for (TopExp_Explorer face_explorer(shape, TopAbs_FACE); face_explorer.More(); face_explorer.Next())
        {
            faces.Append(face_explorer.Current());
        }
    }

    // The shared sew + heal sequence. Returns the ABI status; out_shape receives
    // the healed shape (a compound of solids when make_solid, else of shells).
    int sew_and_heal(const TopTools_ListOfShape& faces, double tolerance, bool make_solid, TopoDS_Shape& out_shape)
    {
        if (faces.IsEmpty())
        {
            return 20;
        }

        const double safe_tolerance = tolerance > 0 ? tolerance : 1e-6;

        // 1) Sew the independent faces so coincident / near-coincident edges
        //    become shared topology - the precondition for healing and volume.
        BRepBuilderAPI_Sewing sewing(safe_tolerance);
        for (const TopoDS_Shape& face : faces)
        {
            sewing.Add(face);
        }

        sewing.Perform();
        TopoDS_Shape sewn = sewing.SewedShape();
        if (sewn.IsNull())
        {
            return 30;
        }

        // 2) Close the micro-gaps the sewing tolerance alone misses (tiny edges
        //    and wire gaps) so the shell can actually bound a volume.
        TopoDS_Shape healed = sewn;
        try
        {
            Handle(ShapeFix_Wireframe) wireframe = new ShapeFix_Wireframe(sewn);
            wireframe->SetPrecision(safe_tolerance);
            wireframe->ModeDropSmallEdges() = Standard_True;
            wireframe->FixSmallEdges();
            wireframe->FixWireGaps();
            if (!wireframe->Shape().IsNull())
            {
                healed = wireframe->Shape();
            }
        }
        catch (...)
        {
            // Wireframe healing is best-effort; fall back to the sewn shape.
        }

        // 3) Per-shell ShapeFix_Shell (consistent orientation + closed flag) and,
        //    when requested, BRepBuilderAPI_MakeSolid + ShapeFix_Solid on every
        //    closed shell. Assemble the survivors into one compound.
        BRep_Builder builder;
        TopoDS_Compound compound;
        builder.MakeCompound(compound);

        bool any_shell = false;
        bool any_solid = false;

        for (TopExp_Explorer shell_explorer(healed, TopAbs_SHELL); shell_explorer.More(); shell_explorer.Next())
        {
            TopoDS_Shape fixed = TopoDS::Shell(shell_explorer.Current());
            try
            {
                Handle(ShapeFix_Shell) shell_fix = new ShapeFix_Shell(TopoDS::Shell(shell_explorer.Current()));
                shell_fix->SetPrecision(safe_tolerance);
                shell_fix->Perform();
                // ShapeFix_Shell::Shape() returns a single shell, or a compound
                // of shells when a non-manifold shell was split; explore it below
                // for TopAbs_SHELL either way.
                TopoDS_Shape fixed_result = shell_fix->Shape();
                if (!fixed_result.IsNull())
                {
                    fixed = fixed_result;
                }
            }
            catch (...)
            {
                // Shell fixing is best-effort; keep the unfixed shell.
            }

            for (TopExp_Explorer fixed_explorer(fixed, TopAbs_SHELL); fixed_explorer.More(); fixed_explorer.Next())
            {
                TopoDS_Shell fixed_shell = TopoDS::Shell(fixed_explorer.Current());
                any_shell = true;

                if (!make_solid)
                {
                    builder.Add(compound, fixed_shell);
                    continue;
                }

                // Only a closed shell can become a solid; an open shell is left
                // out so the op reports status 40 instead of fabricating an open
                // solid that no later stage can use.
                if (!BRep_Tool::IsClosed(fixed_shell))
                {
                    continue;
                }

                try
                {
                    BRepBuilderAPI_MakeSolid make_solid_builder(fixed_shell);
                    if (!make_solid_builder.IsDone())
                    {
                        continue;
                    }

                    ShapeFix_Solid solid_fix;
                    solid_fix.SetPrecision(safe_tolerance);
                    TopoDS_Shape solid = solid_fix.SolidFromShell(fixed_shell);
                    if (solid.IsNull())
                    {
                        solid = make_solid_builder.Solid();
                    }

                    builder.Add(compound, solid);
                    any_solid = true;
                }
                catch (...)
                {
                    // Solid building is failure-prone; skip this shell rather than
                    // abandoning the whole result.
                }
            }
        }

        if (!any_shell)
        {
            // Sewing produced no shell at all (e.g. faces too far apart to join).
            return 30;
        }

        if (make_solid && !any_solid)
        {
            return 40;
        }

        // 4) Merge faces split during sewing into single coplanar faces.
        try
        {
            ShapeUpgrade_UnifySameDomain unify(compound, Standard_True, Standard_True, Standard_False);
            unify.SetLinearTolerance(safe_tolerance);
            unify.Build();
            out_shape = unify.Shape().IsNull() ? static_cast<TopoDS_Shape>(compound) : unify.Shape();
        }
        catch (...)
        {
            out_shape = compound;
        }

        return 0;
    }
}

extern "C" {

int sam_occt_sew_faces(
    const double* coordinates,
    int point_count,
    const int* loop_point_counts,
    int loop_count,
    const int* face_loop_counts,
    int face_count,
    double sewing_tolerance,
    int run_parallel,
    int make_solid,
    void** shape_handle)
{
    (void)run_parallel; // sewing / healing are not parallelised; accepted for ABI symmetry.

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

    arm_signal_translation();

    try
    {
        TopTools_ListOfShape faces;
        if (!make_faces_from_arrays(coordinates, loop_point_counts, face_loop_counts, face_count, faces))
        {
            return 20;
        }

        TopoDS_Shape sewn;
        const int sew_status = sew_and_heal(faces, sewing_tolerance, make_solid != 0, sewn);
        if (sew_status != 0)
        {
            return sew_status;
        }

        return make_solid != 0 ? wrap_sewn_solid(sewn, shape_handle) : wrap_sewn_shell(sewn, shape_handle);
    }
    catch (...)
    {
        return 99;
    }
}

int sam_occt_shape_sew(
    void* shape_handle,
    double sewing_tolerance,
    int run_parallel,
    int make_solid,
    void** shape_handle_out)
{
    (void)run_parallel;

    if (shape_handle_out != nullptr)
    {
        *shape_handle_out = nullptr;
    }

    if (shape_handle_out == nullptr)
    {
        return 10;
    }

    Shape* shape = as_shape(shape_handle);
    if (shape == nullptr)
    {
        return 50;
    }

    arm_signal_translation();

    try
    {
        TopTools_ListOfShape faces;
        collect_faces(shape->shape, faces);
        if (faces.IsEmpty())
        {
            return 20;
        }

        TopoDS_Shape sewn;
        const int sew_status = sew_and_heal(faces, sewing_tolerance, make_solid != 0, sewn);
        if (sew_status != 0)
        {
            return sew_status;
        }

        return make_solid != 0 ? wrap_sewn_solid(sewn, shape_handle_out) : wrap_sewn_shell(sewn, shape_handle_out);
    }
    catch (...)
    {
        return 99;
    }
}

}
