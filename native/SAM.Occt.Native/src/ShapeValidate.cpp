// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

// Native validation & watertightness diagnostics (issue #37 follow-on). A single
// entry point, sam_occt_shape_validate, runs the three OCCT analysers that
// between them locate why a face soup fails to close:
//   * ShapeAnalysis_FreeBounds  - free (naked) boundary edges => the shell is
//                                 NOT watertight; reported with an XYZ location
//                                 and the gap length.
//   * BRepCheck_Analyzer        - topological validity; invalid faces are
//                                 reported with their centroid and area.
//   * BOPAlgo_ArgumentAnalyzer  - self-intersections / too-small edges that
//                                 silently break a boolean / MakerVolume (opt-in
//                                 because the self-intersection test is costly).
// The located, categorised issues are returned through an opaque Validation
// handle queried with the sam_occt_validation_* accessors. This is the native
// safety net the BOP-glue path is gated on (glue corrupts merely-near-coincident
// faces) and gives OcctBuildOptions.ValidateInput teeth, backing the managed
// OcctOpenShellAnalysis with kernel-grade, XYZ-located diagnostics.

#include "sam_occt.h"
#include "OcctNativeCore.h"

#include <BOPAlgo_ArgumentAnalyzer.hxx>
#include <BOPAlgo_CheckResult.hxx>
#include <BOPAlgo_CheckStatus.hxx>
#include <BRepBndLib.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepGProp.hxx>
#include <Bnd_Box.hxx>
#include <GProp_GProps.hxx>
#include <OSD.hxx>
#include <ShapeAnalysis_FreeBounds.hxx>
#include <TopAbs.hxx>
#include <TopExp_Explorer.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Edge.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Shape.hxx>
#include <gp_Pnt.hxx>

#include <memory>

using namespace sam_occt;

namespace
{
    // See ShapeOffset.cpp / ShapeSew.cpp: convert OS-level faults raised deep
    // inside the analysers on pathological input into catchable exceptions so a
    // fault surfaces as a clean status instead of tearing down the host process.
    void arm_signal_translation()
    {
        OSD::SetSignal(Standard_False);
    }

    // Bounding-box centre of any shape - a robust representative location for an
    // issue whose offending sub-shape may be a vertex, edge or face.
    void shape_location(const TopoDS_Shape& shape, double& x, double& y, double& z)
    {
        x = y = z = 0.0;
        if (shape.IsNull())
        {
            return;
        }

        Bnd_Box box;
        BRepBndLib::Add(shape, box);
        if (box.IsVoid())
        {
            return;
        }

        double xmin, ymin, zmin, xmax, ymax, zmax;
        box.Get(xmin, ymin, zmin, xmax, ymax, zmax);
        x = 0.5 * (xmin + xmax);
        y = 0.5 * (ymin + ymax);
        z = 0.5 * (zmin + zmax);
    }

    ValidationIssue edge_issue(const TopoDS_Edge& edge)
    {
        ValidationIssue issue;
        issue.category = SAM_OCCT_VALIDATION_NAKED_EDGE;

        GProp_GProps properties;
        BRepGProp::LinearProperties(edge, properties);
        issue.size = properties.Mass(); // edge length

        const gp_Pnt centre = properties.CentreOfMass();
        issue.x = centre.X();
        issue.y = centre.Y();
        issue.z = centre.Z();
        return issue;
    }

    double face_area(const TopoDS_Face& face, gp_Pnt& centre)
    {
        GProp_GProps properties;
        BRepGProp::SurfaceProperties(face, properties);
        centre = properties.CentreOfMass();
        return properties.Mass();
    }

    // Free (naked) boundary edges: an edge bounding only one face. Their presence
    // means the shape is not watertight, which is the #1 reason MakerVolume fails
    // to build a volume. Reported with the edge midpoint and length.
    void collect_free_bounds(const TopoDS_Shape& shape, Validation& validation)
    {
        // The already-connected constructor uses the shape's existing edge
        // sharing (a built handle is already sewn) instead of re-sewing by
        // tolerance.
        ShapeAnalysis_FreeBounds free_bounds(shape, Standard_False, Standard_True, Standard_False);

        int free_edge_count = 0;
        const TopoDS_Shape wire_sets[2] = { free_bounds.GetOpenWires(), free_bounds.GetClosedWires() };
        for (const TopoDS_Shape& wires : wire_sets)
        {
            if (wires.IsNull())
            {
                continue;
            }

            for (TopExp_Explorer edge_explorer(wires, TopAbs_EDGE); edge_explorer.More(); edge_explorer.Next())
            {
                validation.issues.push_back(edge_issue(TopoDS::Edge(edge_explorer.Current())));
                ++free_edge_count;
            }
        }

        validation.watertight = free_edge_count == 0;
    }

    // Topological validity (BRepCheck_Analyzer) plus sliver faces below the area
    // threshold. Invalid faces are reported with their centroid; sets is_valid.
    void collect_brep_check(const TopoDS_Shape& shape, double tolerance, Validation& validation)
    {
        const double area_threshold = tolerance > 0 ? tolerance * tolerance : 1e-12;

        BRepCheck_Analyzer analyzer(shape, Standard_True);
        const bool brep_valid = analyzer.IsValid() == Standard_True;

        for (TopExp_Explorer face_explorer(shape, TopAbs_FACE); face_explorer.More(); face_explorer.Next())
        {
            const TopoDS_Face& face = TopoDS::Face(face_explorer.Current());

            gp_Pnt centre;
            const double area = face_area(face, centre);

            const bool face_valid = analyzer.IsValid(face) == Standard_True;
            if (!face_valid)
            {
                ValidationIssue issue;
                issue.category = SAM_OCCT_VALIDATION_INVALID_FACE;
                issue.x = centre.X();
                issue.y = centre.Y();
                issue.z = centre.Z();
                issue.size = area;
                validation.issues.push_back(issue);
            }
            else if (area < area_threshold)
            {
                ValidationIssue issue;
                issue.category = SAM_OCCT_VALIDATION_SMALL_FACE;
                issue.x = centre.X();
                issue.y = centre.Y();
                issue.z = centre.Z();
                issue.size = area;
                validation.issues.push_back(issue);
            }
        }

        validation.is_valid = brep_valid;
    }

    int map_check_status(BOPAlgo_CheckStatus status)
    {
        switch (status)
        {
            case BOPAlgo_SelfIntersect: return SAM_OCCT_VALIDATION_SELF_INTERSECTION;
            case BOPAlgo_TooSmallEdge: return SAM_OCCT_VALIDATION_SMALL_EDGE;
            case BOPAlgo_NonRecoverableFace: return SAM_OCCT_VALIDATION_INVALID_FACE;
            default: return SAM_OCCT_VALIDATION_INVALID_SHAPE;
        }
    }

    // Self-intersections / too-small edges that a boolean or MakerVolume cannot
    // recover from. Costly (pairwise face interference), so opt-in. Any fault
    // here also clears is_valid - the glue path must not run on such input.
    void collect_argument_analysis(const TopoDS_Shape& shape, double tolerance, Validation& validation)
    {
        BOPAlgo_ArgumentAnalyzer analyzer;
        analyzer.SetShape1(shape);
        // ArgumentTypeMode validates a shape as a *boolean operand* and needs a
        // second operand - left off here, where we only check the shape against
        // itself for self-interferences and degenerate edges.
        analyzer.ArgumentTypeMode() = Standard_False;
        analyzer.SelfInterMode() = Standard_True;
        analyzer.SmallEdgeMode() = Standard_True;
        if (tolerance > 0)
        {
            analyzer.SetFuzzyValue(tolerance);
        }

        analyzer.Perform();

        // auto + range-based for so this does not depend on the exact spelling of
        // the BOPAlgo_ListOfCheckResult typedef / its nested iterator (which vary
        // across OCCT versions); NCollection_List provides begin()/end().
        for (const auto& check : analyzer.GetCheckResult())
        {
            ValidationIssue issue;
            issue.category = map_check_status(check.GetCheckStatus());
            // Locate from the shape operand the fault was attributed to.
            shape_location(check.GetShape1(), issue.x, issue.y, issue.z);
            validation.issues.push_back(issue);
        }

        if (analyzer.HasFaulty())
        {
            validation.is_valid = false;
        }
    }
}

extern "C" {

int sam_occt_shape_validate(
    void* shape_handle,
    double tolerance,
    int check_self_intersections,
    void** validation_handle)
{
    if (validation_handle != nullptr)
    {
        *validation_handle = nullptr;
    }

    if (validation_handle == nullptr)
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
        std::unique_ptr<Validation> validation(new Validation());

        collect_brep_check(shape->shape, tolerance, *validation);
        collect_free_bounds(shape->shape, *validation);
        if (check_self_intersections != 0)
        {
            collect_argument_analysis(shape->shape, tolerance, *validation);
        }

        *validation_handle = validation.release();
        return 0;
    }
    catch (...)
    {
        return 99;
    }
}

int sam_occt_validation_is_valid(void* validation_handle)
{
    const Validation* validation = as_validation(validation_handle);
    if (validation == nullptr)
    {
        return -1;
    }

    return validation->is_valid ? 1 : 0;
}

int sam_occt_validation_is_watertight(void* validation_handle)
{
    const Validation* validation = as_validation(validation_handle);
    if (validation == nullptr)
    {
        return -1;
    }

    return validation->watertight ? 1 : 0;
}

int sam_occt_validation_issue_count(void* validation_handle)
{
    const Validation* validation = as_validation(validation_handle);
    if (validation == nullptr)
    {
        return -1;
    }

    return static_cast<int>(validation->issues.size());
}

int sam_occt_validation_issue(
    void* validation_handle,
    int index,
    int* category,
    double* x,
    double* y,
    double* z,
    double* size)
{
    const Validation* validation = as_validation(validation_handle);
    if (validation == nullptr)
    {
        return 50;
    }

    if (category == nullptr || x == nullptr || y == nullptr || z == nullptr || size == nullptr)
    {
        return 10;
    }

    if (index < 0 || index >= static_cast<int>(validation->issues.size()))
    {
        return 40;
    }

    const ValidationIssue& issue = validation->issues[static_cast<std::size_t>(index)];
    *category = issue.category;
    *x = issue.x;
    *y = issue.y;
    *z = issue.z;
    *size = issue.size;
    return 0;
}

void sam_occt_free_validation(void* validation_handle)
{
    Validation* validation = static_cast<Validation*>(validation_handle);
    if (validation == nullptr || validation->magic != validation_magic)
    {
        // Null, already freed or not a validation handle - never delete through
        // a mistyped pointer.
        return;
    }

    validation->magic = 0;
    delete validation;
}

}
