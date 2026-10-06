// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

#include "History.h"

#include <BOPAlgo_MakerVolume.hxx>
#include <BOPAlgo_GlueEnum.hxx>
#include <ShapeFix_Shape.hxx>
#include <ShapeBuild_ReShape.hxx>
#include <ShapeAnalysis_ShapeTolerance.hxx>
#include <TopAbs.hxx>
#include <TopoDS.hxx>
#include <NCollection_DataMap.hxx>
#include <NCollection_List.hxx>
#include <TopTools_ShapeMapHasher.hxx>

namespace sam_occt
{
    namespace
    {
        BOPAlgo_GlueEnum history_to_glue(int glue)
        {
            switch (glue)
            {
                case 1: return BOPAlgo_GlueShift;
                case 2: return BOPAlgo_GlueFull;
                default: return BOPAlgo_GlueOff;
            }
        }

        // Output-face -> flat ordinals, keyed orientation-independently (IsSame):
        // the history's Modified() faces may carry a different orientation than
        // the decoded solid faces, and a face shared by two cells is stored under
        // both its ordinals.
        using OrdinalMap = NCollection_DataMap<TopoDS_Shape, NCollection_List<int>, TopTools_ShapeMapHasher>;

        void add_ordinal(OrdinalMap& map, const TopoDS_Shape& face, int ordinal)
        {
            TopoDS_Shape key = face.Oriented(TopAbs_FORWARD);
            if (map.IsBound(key))
            {
                map.ChangeFind(key).Append(ordinal);
            }
            else
            {
                NCollection_List<int> ordinals;
                ordinals.Append(ordinal);
                map.Bind(key, ordinals);
            }
        }

        void collect_ordinals(const OrdinalMap& map, const TopoDS_Shape& face, std::vector<int>& out)
        {
            TopoDS_Shape key = face.Oriented(TopAbs_FORWARD);
            if (!map.IsBound(key))
            {
                return;
            }

            for (NCollection_List<int>::Iterator it(map.Find(key)); it.More(); it.Next())
            {
                out.push_back(it.Value());
            }
        }
    }

    int make_volume_with_history(
        const TopTools_ListOfShape& faces,
        double fuzzy_tolerance,
        int run_parallel,
        int avoid_internal_shapes,
        int glue,
        TopoDS_Shape& out_shape,
        Handle(BRepTools_History)& out_history)
    {
        out_history.Nullify();

        BOPAlgo_MakerVolume maker;
        maker.SetArguments(faces);
        maker.SetIntersect(true);
        maker.SetFuzzyValue(fuzzy_tolerance);
        maker.SetRunParallel(run_parallel != 0);
        maker.SetAvoidInternalShapes(avoid_internal_shapes != 0);
        maker.SetGlue(history_to_glue(glue));
        maker.SetToFillHistory(true);
        maker.Perform();

        if (maker.HasErrors())
        {
            return 30;
        }

        // input faces -> MakerVolume output faces (splits, same-domain merges,
        // deletions). Kept alive by the returned handle after `maker` dies.
        Handle(BRepTools_History) composed = maker.History();

        ShapeFix_Shape shape_fix(maker.Shape());
        shape_fix.Perform();
        out_shape = shape_fix.Shape();

        // Bridge MakerVolume output faces -> ShapeFix'd output faces so the
        // history targets are the faces the caller actually decodes. For clean
        // cell complexes ShapeFix keeps face identity and this history is empty
        // (a no-op merge); when it rebuilds a face the merge follows it through.
        if (!composed.IsNull() && !shape_fix.Context().IsNull())
        {
            Handle(BRepTools_History) fix_history = shape_fix.Context()->History();
            if (!fix_history.IsNull())
            {
                composed->Merge(fix_history);
            }
        }

        out_history = composed;
        return 0;
    }

    Handle(BRepTools_History) sewing_history(
        BRepBuilderAPI_Sewing& sewing,
        const TopTools_ListOfShape& input_faces)
    {
        Handle(BRepTools_History) history = new BRepTools_History();
        bool any = false;
        for (TopTools_ListOfShape::Iterator it(input_faces); it.More(); it.Next())
        {
            const TopoDS_Shape& face = it.Value();
            TopoDS_Shape modified = sewing.ModifiedSubShape(face);
            if (!modified.IsNull() && modified.ShapeType() == TopAbs_FACE && !modified.IsSame(face))
            {
                history->AddModified(face, modified);
                any = true;
            }
        }

        return any ? history : Handle(BRepTools_History)();
    }

    void finalize_history(
        Result& result,
        const TopTools_ListOfShape& input_faces,
        const Handle(BRepTools_History)& composed,
        const std::vector<TopoDS_Face>& ordinal_faces)
    {
        OrdinalMap ordinal_map;
        for (std::size_t k = 0; k < ordinal_faces.size(); ++k)
        {
            add_ordinal(ordinal_map, ordinal_faces[k], static_cast<int>(k));
        }

        result.history.clear();
        result.history.reserve(input_faces.Extent());

        for (TopTools_ListOfShape::Iterator it(input_faces); it.More(); it.Next())
        {
            const TopoDS_Shape& input = it.Value();
            HistoryRecord record;

            if (!composed.IsNull() && composed->IsRemoved(input))
            {
                record.deleted = true;
                result.history.push_back(record);
                continue;
            }

            // BRepTools_History records only CHANGES: an unchanged input face has
            // an empty Modified() list and survives as itself, so it must be
            // mapped to its own ordinal(s).
            bool mapped_any = false;
            if (!composed.IsNull())
            {
                const NCollection_List<TopoDS_Shape>& modified = composed->Modified(input);
                if (modified.IsEmpty())
                {
                    collect_ordinals(ordinal_map, input, record.modified);
                }
                else
                {
                    for (NCollection_List<TopoDS_Shape>::Iterator mit(modified); mit.More(); mit.Next())
                    {
                        collect_ordinals(ordinal_map, mit.Value(), record.modified);
                    }
                }

                const NCollection_List<TopoDS_Shape>& generated = composed->Generated(input);
                for (NCollection_List<TopoDS_Shape>::Iterator git(generated); git.More(); git.Next())
                {
                    collect_ordinals(ordinal_map, git.Value(), record.generated);
                }

                mapped_any = true;
            }
            else
            {
                // No history object at all: fall back to identity-by-geometry.
                collect_ordinals(ordinal_map, input, record.modified);
            }

            (void)mapped_any;
            result.history.push_back(record);
        }

        result.history_available = true;
    }

    void capture_tolerance(Result& result, const TopoDS_Shape& shape)
    {
        if (shape.IsNull())
        {
            return;
        }

        ShapeAnalysis_ShapeTolerance analyzer;
        result.max_tolerance = analyzer.Tolerance(shape, 1);     // > 0 : maximum
        result.average_tolerance = analyzer.Tolerance(shape, 0); // 0   : average
        result.tolerance_available = true;
    }
}
