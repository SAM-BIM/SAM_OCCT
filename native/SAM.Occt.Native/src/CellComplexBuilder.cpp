// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

#include "sam_occt.h"

#include <BRepAlgoAPI_Common.hxx>
#include <BRepAlgoAPI_Cut.hxx>
#include <BRepAlgoAPI_Fuse.hxx>
#include <BOPAlgo_MakerVolume.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakePolygon.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepGProp.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRepOffsetAPI_MakeFilling.hxx>
#include <BRepTools_WireExplorer.hxx>
#include <BRep_Tool.hxx>
#include <GProp_GProps.hxx>
#include <GeomAbs_Shape.hxx>
#include <Poly_Triangle.hxx>
#include <Poly_Triangulation.hxx>
#include <ShapeFix_Shape.hxx>
#include <TopAbs.hxx>
#include <TopExp_Explorer.hxx>
#include <TopLoc_Location.hxx>
#include <TopTools_ListOfShape.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Edge.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Shape.hxx>
#include <TopoDS_Solid.hxx>
#include <TopoDS_Vertex.hxx>
#include <TopoDS_Wire.hxx>
#include <gp_Pnt.hxx>
#include <gp_Trsf.hxx>

#include <cmath>
#include <algorithm>
#include <cstdint>
#include <limits>
#include <map>
#include <memory>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

namespace
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

    bool is_valid_index(int index, int count)
    {
        return index >= 0 && index < count;
    }

    TopoDS_Wire make_wire(const double* coordinates, int& point_offset, int point_count)
    {
        BRepBuilderAPI_MakePolygon polygon;
        for (int i = 0; i < point_count; ++i)
        {
            const int coordinate_index = (point_offset + i) * 3;
            polygon.Add(gp_Pnt(coordinates[coordinate_index], coordinates[coordinate_index + 1], coordinates[coordinate_index + 2]));
        }

        polygon.Close();
        point_offset += point_count;
        return polygon.Wire();
    }

    bool make_face(
        const double* coordinates,
        const int* loop_point_counts,
        int& point_offset,
        int& loop_offset,
        int loop_count,
        TopoDS_Face& face)
    {
        if (loop_count <= 0)
        {
            return false;
        }

        TopoDS_Wire outer_wire = make_wire(coordinates, point_offset, loop_point_counts[loop_offset]);
        ++loop_offset;

        BRepBuilderAPI_MakeFace face_builder(outer_wire, true);
        for (int i = 1; i < loop_count; ++i)
        {
            TopoDS_Wire inner_wire = make_wire(coordinates, point_offset, loop_point_counts[loop_offset]);
            ++loop_offset;
            face_builder.Add(inner_wire);
        }

        if (!face_builder.IsDone())
        {
            return false;
        }

        face = face_builder.Face();
        return BRepCheck_Analyzer(face).IsValid();
    }

    Loop extract_loop(const TopoDS_Wire& wire)
    {
        Loop loop;
        for (BRepTools_WireExplorer explorer(wire); explorer.More(); explorer.Next())
        {
            TopoDS_Vertex vertex = explorer.CurrentVertex();
            gp_Pnt point = BRep_Tool::Pnt(vertex);
            loop.points.push_back({ point.X(), point.Y(), point.Z() });
        }

        if (loop.points.size() > 1)
        {
            const Point& first = loop.points.front();
            const Point& last = loop.points.back();
            const double distance2 =
                (first.x - last.x) * (first.x - last.x) +
                (first.y - last.y) * (first.y - last.y) +
                (first.z - last.z) * (first.z - last.z);

            if (distance2 <= 1e-18)
            {
                loop.points.pop_back();
            }
        }

        return loop;
    }

    long long quantize(double value, double tolerance)
    {
        const double safe_tolerance = tolerance > 0 ? tolerance : 1e-9;
        return static_cast<long long>(std::llround(value / safe_tolerance));
    }

    std::string face_signature(const Face& face, double tolerance)
    {
        std::vector<std::string> points;
        for (const Loop& loop : face.loops)
        {
            for (const Point& point : loop.points)
            {
                std::ostringstream stream;
                stream << quantize(point.x, tolerance) << ','
                       << quantize(point.y, tolerance) << ','
                       << quantize(point.z, tolerance);
                points.push_back(stream.str());
            }
        }

        std::sort(points.begin(), points.end());
        points.erase(std::unique(points.begin(), points.end()), points.end());

        std::ostringstream stream;
        for (const std::string& point : points)
        {
            stream << point << ';';
        }

        return stream.str();
    }

    int get_face_key(Result& result, const Face& face, double tolerance)
    {
        std::string signature = face_signature(face, tolerance);
        if (signature.empty())
        {
            return 0;
        }

        std::map<std::string, int>::const_iterator iterator = result.face_keys.find(signature);
        if (iterator != result.face_keys.end())
        {
            return iterator->second;
        }

        const int key = result.next_face_key++;
        result.face_keys[signature] = key;
        return key;
    }

    Face extract_face(const TopoDS_Face& topods_face, Result& result, double tolerance)
    {
        Face face;
        for (TopExp_Explorer explorer(topods_face, TopAbs_WIRE); explorer.More(); explorer.Next())
        {
            Loop loop = extract_loop(TopoDS::Wire(explorer.Current()));
            if (loop.points.size() >= 3)
            {
                face.loops.push_back(loop);
            }
        }

        face.key = get_face_key(result, face, tolerance);
        return face;
    }

    bool make_meshable_face(
        const double* coordinates,
        const int* loop_point_counts,
        int& point_offset,
        int& loop_offset,
        int loop_count,
        TopoDS_Face& face)
    {
        if (loop_count <= 0)
        {
            return false;
        }

        // Consume every loop up front so the running offsets stay aligned with
        // the next face regardless of which build path succeeds below.
        std::vector<TopoDS_Wire> wires;
        wires.reserve(loop_count);
        for (int i = 0; i < loop_count; ++i)
        {
            TopoDS_Wire wire = make_wire(coordinates, point_offset, loop_point_counts[loop_offset]);
            ++loop_offset;
            if (!wire.IsNull())
            {
                wires.push_back(wire);
            }
        }

        if (wires.empty())
        {
            return false;
        }

        // Prefer a planar face (with any holes) when the boundary is coplanar.
        BRepBuilderAPI_MakeFace planar_builder(wires[0], true);
        if (planar_builder.IsDone())
        {
            for (std::size_t i = 1; i < wires.size(); ++i)
            {
                planar_builder.Add(wires[i]);
            }

            if (planar_builder.IsDone() && BRepCheck_Analyzer(planar_builder.Face()).IsValid())
            {
                face = planar_builder.Face();
                return true;
            }
        }

        // Non-planar boundary: fit a filling surface across the outer wire so the
        // mesher has a real (curved) surface to triangulate into planar facets.
        try
        {
            BRepOffsetAPI_MakeFilling filling;
            for (TopExp_Explorer explorer(wires[0], TopAbs_EDGE); explorer.More(); explorer.Next())
            {
                filling.Add(TopoDS::Edge(explorer.Current()), GeomAbs_C0);
            }

            filling.Build();
            if (!filling.IsDone())
            {
                return false;
            }

            face = TopoDS::Face(filling.Shape());
            return true;
        }
        catch (...)
        {
            return false;
        }
    }

    void append_triangles(
        const TopoDS_Face& face,
        double linear_deflection,
        double angular_deflection,
        int relative_deflection,
        Cell& cell,
        Result& result,
        double tolerance)
    {
        BRepMesh_IncrementalMesh mesher(face, linear_deflection, relative_deflection != 0, angular_deflection, Standard_True);
        mesher.Perform();

        TopLoc_Location location;
        Handle(Poly_Triangulation) triangulation = BRep_Tool::Triangulation(face, location);
        if (triangulation.IsNull())
        {
            return;
        }

        const gp_Trsf& transformation = location.Transformation();
        const bool reversed = face.Orientation() == TopAbs_REVERSED;
        const int triangle_count = triangulation->NbTriangles();
        for (int i = 1; i <= triangle_count; ++i)
        {
            int node_1 = 0;
            int node_2 = 0;
            int node_3 = 0;
            triangulation->Triangle(i).Get(node_1, node_2, node_3);
            if (reversed)
            {
                std::swap(node_2, node_3);
            }

            gp_Pnt point_1 = triangulation->Node(node_1).Transformed(transformation);
            gp_Pnt point_2 = triangulation->Node(node_2).Transformed(transformation);
            gp_Pnt point_3 = triangulation->Node(node_3).Transformed(transformation);

            Face triangle_face;
            Loop loop;
            loop.points.push_back({ point_1.X(), point_1.Y(), point_1.Z() });
            loop.points.push_back({ point_2.X(), point_2.Y(), point_2.Z() });
            loop.points.push_back({ point_3.X(), point_3.Y(), point_3.Z() });
            triangle_face.loops.push_back(loop);
            triangle_face.key = get_face_key(result, triangle_face, tolerance);
            cell.faces.push_back(triangle_face);
        }
    }

    bool append_solid_to_result(const TopoDS_Solid& solid, Result& result, double tolerance)
    {
        Cell cell;

        GProp_GProps properties;
        BRepGProp::VolumeProperties(solid, properties);
        cell.volume = properties.Mass();
        for (TopExp_Explorer face_explorer(solid, TopAbs_FACE); face_explorer.More(); face_explorer.Next())
        {
            Face face = extract_face(TopoDS::Face(face_explorer.Current()), result, tolerance);
            if (!face.loops.empty())
            {
                cell.faces.push_back(face);
            }
        }

        if (!cell.faces.empty() && std::abs(cell.volume) > tolerance)
        {
            result.cells.push_back(cell);
            return true;
        }

        return false;
    }

    void append_shape_solids_to_result(const TopoDS_Shape& shape, Result& result, double tolerance)
    {
        ShapeFix_Shape shape_fix(shape);
        shape_fix.Perform();
        TopoDS_Shape fixed_shape = shape_fix.Shape();

        for (TopExp_Explorer solid_explorer(fixed_shape, TopAbs_SOLID); solid_explorer.More(); solid_explorer.Next())
        {
            append_solid_to_result(TopoDS::Solid(solid_explorer.Current()), result, tolerance);
        }
    }

    bool build_shell_solids(
        const double* coordinates,
        const int* loop_point_counts,
        const int* face_loop_counts,
        const int* shell_face_counts,
        int shell_count,
        double fuzzy_tolerance,
        int run_parallel,
        std::vector<TopoDS_Solid>& solids)
    {
        int point_offset = 0;
        int loop_offset = 0;
        int face_offset = 0;

        for (int shell_index = 0; shell_index < shell_count; ++shell_index)
        {
            TopTools_ListOfShape arguments;
            const int shell_face_count = shell_face_counts[shell_index];

            for (int i = 0; i < shell_face_count; ++i)
            {
                TopoDS_Face face;
                if (make_face(coordinates, loop_point_counts, point_offset, loop_offset, face_loop_counts[face_offset], face))
                {
                    arguments.Append(face);
                }

                ++face_offset;
            }

            if (arguments.IsEmpty())
            {
                continue;
            }

            BOPAlgo_MakerVolume maker;
            maker.SetArguments(arguments);
            maker.SetIntersect(true);
            maker.SetFuzzyValue(fuzzy_tolerance);
            maker.SetRunParallel(run_parallel != 0);
            maker.SetAvoidInternalShapes(true);
            maker.Perform();

            if (maker.HasErrors())
            {
                return false;
            }

            ShapeFix_Shape shape_fix(maker.Shape());
            shape_fix.Perform();
            TopoDS_Shape shape = shape_fix.Shape();

            for (TopExp_Explorer solid_explorer(shape, TopAbs_SOLID); solid_explorer.More(); solid_explorer.Next())
            {
                solids.push_back(TopoDS::Solid(solid_explorer.Current()));
            }
        }

        return !solids.empty();
    }
}

int sam_occt_build_cell_complex(
    const double* coordinates,
    int point_count,
    const int* loop_point_counts,
    int loop_count,
    const int* face_loop_counts,
    int face_count,
    double tolerance,
    double fuzzy_tolerance,
    int run_parallel,
    int avoid_internal_shapes,
    void** result_handle)
{
    if (result_handle != nullptr)
    {
        *result_handle = nullptr;
    }

    if (coordinates == nullptr || loop_point_counts == nullptr || face_loop_counts == nullptr || result_handle == nullptr)
    {
        return 10;
    }

    if (point_count <= 0 || loop_count <= 0 || face_count <= 0)
    {
        return 11;
    }

    try
    {
        TopTools_ListOfShape arguments;
        int point_offset = 0;
        int loop_offset = 0;

        for (int face_index = 0; face_index < face_count; ++face_index)
        {
            TopoDS_Face face;
            if (make_face(coordinates, loop_point_counts, point_offset, loop_offset, face_loop_counts[face_index], face))
            {
                arguments.Append(face);
            }
        }

        if (arguments.IsEmpty())
        {
            return 20;
        }

        BOPAlgo_MakerVolume maker;
        maker.SetArguments(arguments);
        maker.SetIntersect(true);
        maker.SetFuzzyValue(fuzzy_tolerance);
        maker.SetRunParallel(run_parallel != 0);
        maker.SetAvoidInternalShapes(avoid_internal_shapes != 0);
        maker.Perform();

        if (maker.HasErrors())
        {
            return 30;
        }

        std::unique_ptr<Result> result(new Result());
        ShapeFix_Shape shape_fix(maker.Shape());
        shape_fix.Perform();
        TopoDS_Shape shape = shape_fix.Shape();

        for (TopExp_Explorer solid_explorer(shape, TopAbs_SOLID); solid_explorer.More(); solid_explorer.Next())
        {
            TopoDS_Solid solid = TopoDS::Solid(solid_explorer.Current());
            Cell cell;

            GProp_GProps properties;
            BRepGProp::VolumeProperties(solid, properties);
            cell.volume = properties.Mass();

            for (TopExp_Explorer face_explorer(solid, TopAbs_FACE); face_explorer.More(); face_explorer.Next())
            {
                Face face = extract_face(TopoDS::Face(face_explorer.Current()), *result, tolerance);
                if (!face.loops.empty())
                {
                    cell.faces.push_back(face);
                }
            }

            if (!cell.faces.empty() && std::abs(cell.volume) > tolerance)
            {
                result->cells.push_back(cell);
            }
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

int sam_occt_shells_difference(
    const double* target_coordinates,
    int target_point_count,
    const int* target_loop_point_counts,
    int target_loop_count,
    const int* target_face_loop_counts,
    int target_face_count,
    const int* target_shell_face_counts,
    int target_shell_count,
    const double* cutter_coordinates,
    int cutter_point_count,
    const int* cutter_loop_point_counts,
    int cutter_loop_count,
    const int* cutter_face_loop_counts,
    int cutter_face_count,
    const int* cutter_shell_face_counts,
    int cutter_shell_count,
    double tolerance,
    double fuzzy_tolerance,
    int run_parallel,
    void** result_handle)
{
    if (result_handle != nullptr)
    {
        *result_handle = nullptr;
    }

    if (target_coordinates == nullptr || target_loop_point_counts == nullptr || target_face_loop_counts == nullptr || target_shell_face_counts == nullptr || result_handle == nullptr)
    {
        return 10;
    }

    if (cutter_coordinates == nullptr || cutter_loop_point_counts == nullptr || cutter_face_loop_counts == nullptr || cutter_shell_face_counts == nullptr)
    {
        return 12;
    }

    if (target_point_count <= 0 || target_loop_count <= 0 || target_face_count <= 0 || target_shell_count <= 0)
    {
        return 11;
    }

    if (cutter_point_count <= 0 || cutter_loop_count <= 0 || cutter_face_count <= 0 || cutter_shell_count <= 0)
    {
        return 13;
    }

    try
    {
        std::vector<TopoDS_Solid> target_solids;
        if (!build_shell_solids(
                target_coordinates,
                target_loop_point_counts,
                target_face_loop_counts,
                target_shell_face_counts,
                target_shell_count,
                fuzzy_tolerance,
                run_parallel,
                target_solids))
        {
            return 20;
        }

        std::vector<TopoDS_Solid> cutter_solids;
        if (!build_shell_solids(
                cutter_coordinates,
                cutter_loop_point_counts,
                cutter_face_loop_counts,
                cutter_shell_face_counts,
                cutter_shell_count,
                fuzzy_tolerance,
                run_parallel,
                cutter_solids))
        {
            return 21;
        }

        TopTools_ListOfShape tools;
        for (const TopoDS_Solid& cutter_solid : cutter_solids)
        {
            tools.Append(cutter_solid);
        }

        std::unique_ptr<Result> result(new Result());
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

            append_shape_solids_to_result(cut.Shape(), *result, tolerance);
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

int sam_occt_shells_intersection(
    const double* target_coordinates,
    int target_point_count,
    const int* target_loop_point_counts,
    int target_loop_count,
    const int* target_face_loop_counts,
    int target_face_count,
    const int* target_shell_face_counts,
    int target_shell_count,
    const double* tool_coordinates,
    int tool_point_count,
    const int* tool_loop_point_counts,
    int tool_loop_count,
    const int* tool_face_loop_counts,
    int tool_face_count,
    const int* tool_shell_face_counts,
    int tool_shell_count,
    double tolerance,
    double fuzzy_tolerance,
    int run_parallel,
    void** result_handle)
{
    if (result_handle != nullptr)
    {
        *result_handle = nullptr;
    }

    if (target_coordinates == nullptr || target_loop_point_counts == nullptr || target_face_loop_counts == nullptr || target_shell_face_counts == nullptr || result_handle == nullptr)
    {
        return 10;
    }

    if (tool_coordinates == nullptr || tool_loop_point_counts == nullptr || tool_face_loop_counts == nullptr || tool_shell_face_counts == nullptr)
    {
        return 12;
    }

    if (target_point_count <= 0 || target_loop_count <= 0 || target_face_count <= 0 || target_shell_count <= 0)
    {
        return 11;
    }

    if (tool_point_count <= 0 || tool_loop_count <= 0 || tool_face_count <= 0 || tool_shell_count <= 0)
    {
        return 13;
    }

    try
    {
        std::vector<TopoDS_Solid> target_solids;
        if (!build_shell_solids(
                target_coordinates,
                target_loop_point_counts,
                target_face_loop_counts,
                target_shell_face_counts,
                target_shell_count,
                fuzzy_tolerance,
                run_parallel,
                target_solids))
        {
            return 20;
        }

        std::vector<TopoDS_Solid> tool_solids;
        if (!build_shell_solids(
                tool_coordinates,
                tool_loop_point_counts,
                tool_face_loop_counts,
                tool_shell_face_counts,
                tool_shell_count,
                fuzzy_tolerance,
                run_parallel,
                tool_solids))
        {
            return 21;
        }

        TopTools_ListOfShape tools;
        for (const TopoDS_Solid& tool_solid : tool_solids)
        {
            tools.Append(tool_solid);
        }

        std::unique_ptr<Result> result(new Result());
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

            append_shape_solids_to_result(common.Shape(), *result, tolerance);
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

int sam_occt_shells_union(
    const double* coordinates,
    int point_count,
    const int* loop_point_counts,
    int loop_count,
    const int* face_loop_counts,
    int face_count,
    const int* shell_face_counts,
    int shell_count,
    double tolerance,
    double fuzzy_tolerance,
    int run_parallel,
    void** result_handle)
{
    if (result_handle != nullptr)
    {
        *result_handle = nullptr;
    }

    if (coordinates == nullptr || loop_point_counts == nullptr || face_loop_counts == nullptr || shell_face_counts == nullptr || result_handle == nullptr)
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

        std::unique_ptr<Result> result(new Result());
        if (solids.size() == 1)
        {
            append_solid_to_result(solids[0], *result, tolerance);
        }
        else
        {
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

            append_shape_solids_to_result(fuse.Shape(), *result, tolerance);
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

int sam_occt_triangulate(
    const double* coordinates,
    int point_count,
    const int* loop_point_counts,
    int loop_count,
    const int* face_loop_counts,
    int face_count,
    double linear_deflection,
    double angular_deflection,
    int relative_deflection,
    double tolerance,
    void** result_handle)
{
    if (result_handle != nullptr)
    {
        *result_handle = nullptr;
    }

    if (coordinates == nullptr || loop_point_counts == nullptr || face_loop_counts == nullptr || result_handle == nullptr)
    {
        return 10;
    }

    if (point_count <= 0 || loop_count <= 0 || face_count <= 0)
    {
        return 11;
    }

    if (!(linear_deflection > 0))
    {
        linear_deflection = 1.0;
    }

    if (!(angular_deflection > 0))
    {
        angular_deflection = 0.5;
    }

    try
    {
        std::unique_ptr<Result> result(new Result());
        Cell cell;
        cell.volume = 0;

        int point_offset = 0;
        int loop_offset = 0;
        for (int face_index = 0; face_index < face_count; ++face_index)
        {
            TopoDS_Face face;
            if (make_meshable_face(coordinates, loop_point_counts, point_offset, loop_offset, face_loop_counts[face_index], face))
            {
                append_triangles(face, linear_deflection, angular_deflection, relative_deflection, cell, *result, tolerance);
            }
        }

        if (cell.faces.empty())
        {
            return 40;
        }

        result->cells.push_back(cell);
        *result_handle = result.release();
        return 0;
    }
    catch (...)
    {
        return 99;
    }
}

void sam_occt_free_result(void* result_handle)
{
    delete static_cast<Result*>(result_handle);
}

int sam_occt_result_cell_count(void* result_handle)
{
    const Result* result = static_cast<const Result*>(result_handle);
    return result == nullptr ? 0 : static_cast<int>(result->cells.size());
}

int sam_occt_result_cell_face_count(void* result_handle, int cell_index)
{
    const Result* result = static_cast<const Result*>(result_handle);
    if (result == nullptr || !is_valid_index(cell_index, static_cast<int>(result->cells.size())))
    {
        return 0;
    }

    return static_cast<int>(result->cells[cell_index].faces.size());
}

double sam_occt_result_cell_volume(void* result_handle, int cell_index)
{
    const Result* result = static_cast<const Result*>(result_handle);
    if (result == nullptr || !is_valid_index(cell_index, static_cast<int>(result->cells.size())))
    {
        return 0;
    }

    return result->cells[cell_index].volume;
}

int sam_occt_result_cell_center(
    void* result_handle,
    int cell_index,
    double* x,
    double* y,
    double* z)
{
    const Result* result = static_cast<const Result*>(result_handle);
    if (result == nullptr || x == nullptr || y == nullptr || z == nullptr || !is_valid_index(cell_index, static_cast<int>(result->cells.size())))
    {
        return 0;
    }

    const Cell& cell = result->cells[cell_index];
    bool has_point = false;
    double xmin = 0;
    double ymin = 0;
    double zmin = 0;
    double xmax = 0;
    double ymax = 0;
    double zmax = 0;
    for (const Face& face : cell.faces)
    {
        for (const Loop& loop : face.loops)
        {
            for (const Point& point : loop.points)
            {
                if (!has_point)
                {
                    xmin = xmax = point.x;
                    ymin = ymax = point.y;
                    zmin = zmax = point.z;
                    has_point = true;
                }
                else
                {
                    xmin = std::min(xmin, point.x);
                    ymin = std::min(ymin, point.y);
                    zmin = std::min(zmin, point.z);
                    xmax = std::max(xmax, point.x);
                    ymax = std::max(ymax, point.y);
                    zmax = std::max(zmax, point.z);
                }
            }
        }
    }

    if (!has_point)
    {
        return 0;
    }

    *x = (xmin + xmax) * 0.5;
    *y = (ymin + ymax) * 0.5;
    *z = (zmin + zmax) * 0.5;
    return 1;
}

int sam_occt_result_face_loop_count(void* result_handle, int cell_index, int face_index)
{
    const Result* result = static_cast<const Result*>(result_handle);
    if (result == nullptr || !is_valid_index(cell_index, static_cast<int>(result->cells.size())))
    {
        return 0;
    }

    const Cell& cell = result->cells[cell_index];
    if (!is_valid_index(face_index, static_cast<int>(cell.faces.size())))
    {
        return 0;
    }

    return static_cast<int>(cell.faces[face_index].loops.size());
}

int sam_occt_result_face_key(void* result_handle, int cell_index, int face_index)
{
    const Result* result = static_cast<const Result*>(result_handle);
    if (result == nullptr || !is_valid_index(cell_index, static_cast<int>(result->cells.size())))
    {
        return 0;
    }

    const Cell& cell = result->cells[cell_index];
    if (!is_valid_index(face_index, static_cast<int>(cell.faces.size())))
    {
        return 0;
    }

    return cell.faces[face_index].key;
}

int sam_occt_result_loop_point_count(void* result_handle, int cell_index, int face_index, int loop_index)
{
    const Result* result = static_cast<const Result*>(result_handle);
    if (result == nullptr || !is_valid_index(cell_index, static_cast<int>(result->cells.size())))
    {
        return 0;
    }

    const Cell& cell = result->cells[cell_index];
    if (!is_valid_index(face_index, static_cast<int>(cell.faces.size())))
    {
        return 0;
    }

    const Face& face = cell.faces[face_index];
    if (!is_valid_index(loop_index, static_cast<int>(face.loops.size())))
    {
        return 0;
    }

    return static_cast<int>(face.loops[loop_index].points.size());
}

int sam_occt_result_point(
    void* result_handle,
    int cell_index,
    int face_index,
    int loop_index,
    int point_index,
    double* x,
    double* y,
    double* z)
{
    const Result* result = static_cast<const Result*>(result_handle);
    if (result == nullptr || x == nullptr || y == nullptr || z == nullptr || !is_valid_index(cell_index, static_cast<int>(result->cells.size())))
    {
        return 0;
    }

    const Cell& cell = result->cells[cell_index];
    if (!is_valid_index(face_index, static_cast<int>(cell.faces.size())))
    {
        return 0;
    }

    const Face& face = cell.faces[face_index];
    if (!is_valid_index(loop_index, static_cast<int>(face.loops.size())))
    {
        return 0;
    }

    const Loop& loop = face.loops[loop_index];
    if (!is_valid_index(point_index, static_cast<int>(loop.points.size())))
    {
        return 0;
    }

    const Point& point = loop.points[point_index];
    *x = point.x;
    *y = point.y;
    *z = point.z;
    return 1;
}
