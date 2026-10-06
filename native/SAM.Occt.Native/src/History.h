// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

// ABI v4 (Phase 3): native BRepTools_History capture helpers. The producing ops
// (BOPAlgo_MakerVolume cell build, sew + ShapeUpgrade_UnifySameDomain coplanar
// merge) build a composed history from INPUT faces to their decoded OUTPUT
// faces; finalize_history translates the output faces to flat ordinals (the
// cell-major decode order) and fills Result::history. Observational only - none
// of this changes the geometry the ops produce.
//
// Design record: docs/P3_ABI_V4_NATIVE_HISTORY_DESIGN_REVIEW.md.

#pragma once

#include "OcctNativeCore.h"

#include <BRepBuilderAPI_Sewing.hxx>
#include <BRepTools_History.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Shape.hxx>
#include <TopTools_ListOfShape.hxx>

#include <vector>

namespace sam_occt
{
    // BOPAlgo_MakerVolume + ShapeFix_Shape, returning the fixed shape and the
    // composed history (input faces -> fixed output faces). The MakerVolume
    // history (splits, same-domain merges, deletions) is merged with the
    // ShapeFix ReShape history so the output faces are the ones the caller then
    // decodes. Status: 0 ok, 30 BOP error. out_history is null on 30.
    int make_volume_with_history(
        const TopTools_ListOfShape& faces,
        double fuzzy_tolerance,
        int run_parallel,
        int avoid_internal_shapes,
        int glue,
        TopoDS_Shape& out_shape,
        Handle(BRepTools_History)& out_history);

    // Hand-built 1:1 history for a performed BRepBuilderAPI_Sewing: for each
    // input face, AddModified(face, sewing.ModifiedSubShape(face)) when the sewn
    // sub-shape is a face. BRepBuilderAPI_Sewing is not a BRepBuilderAPI_MakeShape
    // and exposes no History(), so this reconstructs one. Returns null when
    // nothing mapped.
    Handle(BRepTools_History) sewing_history(
        BRepBuilderAPI_Sewing& sewing,
        const TopTools_ListOfShape& input_faces);

    // Fills result.history from the composed input->output history and the decoded
    // output faces (ordinal_faces[k] = the TopoDS_Face at flat ordinal k). A
    // shared face appears at several ordinals. Sets result.history_available.
    void finalize_history(
        Result& result,
        const TopTools_ListOfShape& input_faces,
        const Handle(BRepTools_History)& composed,
        const std::vector<TopoDS_Face>& ordinal_faces);

    // Max / average sub-shape tolerance (ShapeAnalysis_ShapeTolerance over all
    // vertices, edges and faces). Sets result.tolerance_available.
    void capture_tolerance(Result& result, const TopoDS_Shape& shape);
}
