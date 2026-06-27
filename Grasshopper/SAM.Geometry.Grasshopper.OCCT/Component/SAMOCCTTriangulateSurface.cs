// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using SAM.Core;
using SAM.Core.Grasshopper;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.Grasshopper.OCCT
{
    public class SAMOCCTTriangulateSurface : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("8d2f6c1a-4b7e-49a2-9c3f-1e6b5a0d7f42");

        public override string LatestComponentVersion => "0.1.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTTriangulateSurface()
          : base("SAMOCCT.TriangulateSurface", "SAMOCCT.TriangulateSurface", "Triangulate possibly non-planar surfaces into planar SAM Face3Ds with OCCT, ready for panelling. OCCT meshes coplanar faces as planes and spans non-planar (warped) boundaries with a filling surface; every output triangle is guaranteed planar. Deflection and area controls keep panels from getting too tiny.", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                global::Grasshopper.Kernel.Parameters.Param_GenericObject surfaces = new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_surfaces", NickName = "_surfaces", Description = "Surfaces to triangulate into planar Face3Ds. Accepts SAM Face3Ds and geometry that converts to Face3Ds (Rhino surfaces, Breps/polysurfaces); boundaries may be non-planar (e.g. warped facade panels).", Access = GH_ParamAccess.list };
                surfaces.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(surfaces, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number linearDeflection = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "linearDeflection_", NickName = "linearDeflection_", Description = "OCCT linear meshing deflection in model units: the maximum distance a planar panel may deviate from the true surface. Larger values give fewer, bigger planar panels; smaller values hug curvature more closely.", Access = GH_ParamAccess.item };
                linearDeflection.SetPersistentData(0.1);
                result.Add(new GH_SAMParam(linearDeflection, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number angularDeflection = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "angularDeflection_", NickName = "angularDeflection_", Description = "OCCT angular meshing deflection in radians, used along curved boundaries.", Access = GH_ParamAccess.item };
                angularDeflection.SetPersistentData(0.5);
                result.Add(new GH_SAMParam(angularDeflection, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number minArea = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "minArea_", NickName = "minArea_", Description = "Discard any triangle whose area (model units squared) is below this value, so no too-tiny panels are produced.", Access = GH_ParamAccess.item };
                minArea.SetPersistentData(0.01);
                result.Add(new GH_SAMParam(minArea, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean nonPlanarOnly = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "nonPlanarOnly_", NickName = "nonPlanarOnly_", Description = "When true, only non-planar (warped) surfaces are triangulated; planar surfaces pass through unchanged as a single Face3D. This keeps the face count down by not splitting flat surfaces into triangles. A surface counts as planar when every boundary point lies within tolerance_ of its best-fit plane.", Access = GH_ParamAccess.item };
                nonPlanarOnly.SetPersistentData(false);
                result.Add(new GH_SAMParam(nonPlanarOnly, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number tolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "tolerance_", NickName = "tolerance_", Description = "Distance tolerance (model units). It is also the planarity threshold used by nonPlanarOnly_: a surface counts as planar - and is passed through unchanged as one Face3D - when every boundary point lies within this distance of the surface's best-fit plane. Surfaces with any corner farther than this are triangulated. Larger values let more (slightly warped) surfaces pass through as single faces.", Access = GH_ParamAccess.item };
                tolerance.SetPersistentData(Tolerance.Distance);
                result.Add(new GH_SAMParam(tolerance, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean run = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "_run", NickName = "_run", Description = "Run", Access = GH_ParamAccess.item };
                run.SetPersistentData(false);
                result.Add(new GH_SAMParam(run, ParamVisibility.Binding));

                return result.ToArray();
            }
        }

        protected override GH_SAMParam[] Outputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "Face3Ds", NickName = "Face3Ds", Description = "Planar triangular SAM Face3Ds. Feed these into SAMOCCT.CreateShells / SAMOCCT.PanelsFromShells or any SAM panelling workflow.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Diagnostics", NickName = "Diagnostics", Description = "OCCT diagnostics", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "Successful", NickName = "Successful", Description = "Run successfully?", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                return result.ToArray();
            }
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            int index_Successful = Params.IndexOfOutputParam("Successful");
            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, false);
            }

            int index;

            bool run = false;
            index = Params.IndexOfInputParam("_run");
            if (index == -1 || !dataAccess.GetData(index, ref run) || !run)
            {
                return;
            }

            List<GH_ObjectWrapper> objectWrappers = new List<GH_ObjectWrapper>();
            index = Params.IndexOfInputParam("_surfaces");
            if (index == -1 || !dataAccess.GetDataList(index, objectWrappers) || objectWrappers == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid surfaces");
                return;
            }

            double linearDeflection = 0.1;
            index = Params.IndexOfInputParam("linearDeflection_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref linearDeflection);
            }

            double angularDeflection = 0.5;
            index = Params.IndexOfInputParam("angularDeflection_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref angularDeflection);
            }

            double minArea = 0.01;
            index = Params.IndexOfInputParam("minArea_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref minArea);
            }

            double tolerance = Tolerance.Distance;
            index = Params.IndexOfInputParam("tolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref tolerance);
            }

            bool nonPlanarOnly = false;
            index = Params.IndexOfInputParam("nonPlanarOnly_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref nonPlanarOnly);
            }

            // Extract each surface's outer boundary as raw 3D points. We deliberately avoid
            // converting to a SAM Face3D first: Face3D is planar, so a warped/non-planar surface
            // would be flattened (or fail) before OCCT could span and mesh it. Keeping the true
            // corners lets OCCT triangulate the real surface.
            List<IReadOnlyList<Point3D>> allLoops = new List<IReadOnlyList<Point3D>>();
            foreach (GH_ObjectWrapper objectWrapper in objectWrappers)
            {
                if (AppendBoundaryLoops(objectWrapper?.Value, allLoops) > 0)
                {
                    continue;
                }

                // Fallback: SAM geometry that converts to planar Face3Ds.
                if (Query.TryGetSAMGeometries(objectWrapper, out List<Face3D> face3Ds_Temp) && face3Ds_Temp != null)
                {
                    foreach (Face3D face3D in face3Ds_Temp)
                    {
                        List<Point3D> external = ExternalLoopPoints(face3D);
                        if (external != null && external.Count >= 3)
                        {
                            allLoops.Add(external);
                        }
                    }
                }
            }

            if (allLoops.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not extract any surface boundary loops from the input");
                return;
            }

            // When nonPlanarOnly_ is set, planar boundaries pass straight through as one Face3D
            // each, so flat surfaces are not split into triangles and the face count stays low.
            List<IReadOnlyList<Point3D>> loopsToTriangulate = new List<IReadOnlyList<Point3D>>();
            List<Face3D> planarPanels = new List<Face3D>();
            foreach (IReadOnlyList<Point3D> loop in allLoops)
            {
                if (nonPlanarOnly && TryGetPlanarFace3D(loop, tolerance, out Face3D planarFace))
                {
                    planarPanels.Add(planarFace);
                }
                else
                {
                    loopsToTriangulate.Add(loop);
                }
            }

            OcctCellComplexResult result = null;
            List<Triangle3D> triangles = null;
            if (loopsToTriangulate.Count != 0)
            {
                triangles = Geometry.OCCT.Create.Triangulate(loopsToTriangulate, out result, linearDeflection, angularDeflection, false, new OcctBuildOptions { Tolerance = tolerance });
            }

            int skippedSmall = 0;
            List<Face3D> panels = new List<Face3D>();

            if (triangles != null)
            {
                foreach (Triangle3D triangle in triangles)
                {
                    if (triangle == null)
                    {
                        continue;
                    }

                    if (minArea > 0 && triangle.GetArea() < minArea)
                    {
                        skippedSmall++;
                        continue;
                    }

                    panels.Add(new Face3D(triangle));
                }
            }

            foreach (Face3D planarFace in planarPanels)
            {
                if (minArea > 0 && planarFace.GetArea() < minArea)
                {
                    skippedSmall++;
                    continue;
                }

                panels.Add(planarFace);
            }

            List<string> diagnostics = result?.Diagnostics?.Select(x => x.ToString()).ToList() ?? new List<string>();
            diagnostics.Add(string.Format("SAM_OCCT_TRIANGULATE_SURFACE: Produced {0} planar Face3D(s) from {1} surface boundary loop(s) ({2} passed through as planar, {3} triangulated); skipped {4} below minArea.", panels.Count, allLoops.Count, planarPanels.Count, loopsToTriangulate.Count, skippedSmall));

            index = Params.IndexOfOutputParam("Face3Ds");
            if (index != -1)
            {
                dataAccess.SetDataList(index, panels);
            }

            index = Params.IndexOfOutputParam("Diagnostics");
            if (index != -1)
            {
                dataAccess.SetDataList(index, diagnostics);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, panels.Count != 0);
            }
        }

        private static int AppendBoundaryLoops(object value, List<IReadOnlyList<Point3D>> boundaryLoops)
        {
            Rhino.Geometry.Brep brep = ToBrep(value);
            if (brep == null)
            {
                return 0;
            }

            int added = 0;
            foreach (Rhino.Geometry.BrepLoop brepLoop in brep.Loops)
            {
                if (brepLoop == null || brepLoop.LoopType != Rhino.Geometry.BrepLoopType.Outer)
                {
                    continue;
                }

                List<Point3D> points = LoopPoints(brepLoop);
                if (points != null && points.Count >= 3)
                {
                    boundaryLoops.Add(points);
                    added++;
                }
            }

            return added;
        }

        private static Rhino.Geometry.Brep ToBrep(object value)
        {
            switch (value)
            {
                case null:
                    return null;
                case GH_Brep ghBrep:
                    return ghBrep.Value;
                case GH_Surface ghSurface:
                    return ghSurface.Value;
                case Rhino.Geometry.Brep brep:
                    return brep;
                case Rhino.Geometry.Surface surface:
                    return surface.ToBrep();
            }

            Rhino.Geometry.Brep converted = null;
            if (GH_Convert.ToBrep(value, ref converted, GH_Conversion.Both))
            {
                return converted;
            }

            return null;
        }

        private static List<Point3D> LoopPoints(Rhino.Geometry.BrepLoop brepLoop)
        {
            Rhino.Geometry.Curve curve = brepLoop.To3dCurve();
            if (curve == null)
            {
                return null;
            }

            List<Point3D> points = new List<Point3D>();

            if (curve.TryGetPolyline(out Rhino.Geometry.Polyline polyline) && polyline != null && polyline.Count >= 2)
            {
                int count = polyline.Count;
                if (count >= 2 && polyline[0].DistanceTo(polyline[count - 1]) <= Rhino.RhinoMath.ZeroTolerance)
                {
                    count--;
                }

                for (int i = 0; i < count; i++)
                {
                    points.Add(new Point3D(polyline[i].X, polyline[i].Y, polyline[i].Z));
                }

                return points;
            }

            // Curved boundary: sample the loop so OCCT can span and mesh it.
            double length = curve.GetLength();
            int divisions = length > 0 ? System.Math.Max(8, (int)System.Math.Ceiling(length)) : 8;
            if (curve.DivideByCount(divisions, true, out Rhino.Geometry.Point3d[] samples) != null && samples != null)
            {
                foreach (Rhino.Geometry.Point3d sample in samples)
                {
                    points.Add(new Point3D(sample.X, sample.Y, sample.Z));
                }
            }

            return points;
        }

        private static List<Point3D> ExternalLoopPoints(Face3D face3D)
        {
            IClosedPlanar3D externalEdge = face3D?.GetExternalEdge3D();
            if (externalEdge is ISegmentable3D segmentable3D)
            {
                return segmentable3D.GetPoints();
            }

            return null;
        }

        private static bool TryGetPlanarFace3D(IReadOnlyList<Point3D> boundaryLoop, double tolerance, out Face3D face3D)
        {
            face3D = null;
            if (boundaryLoop == null || boundaryLoop.Count < 3 || !IsPlanar(boundaryLoop, tolerance))
            {
                return false;
            }

            try
            {
                face3D = new Face3D(new Polygon3D(new List<Point3D>(boundaryLoop)));
            }
            catch
            {
                return false;
            }

            return face3D != null;
        }

        private static bool IsPlanar(IReadOnlyList<Point3D> points, double tolerance)
        {
            int count = points.Count;
            if (count < 3)
            {
                return false;
            }

            // Newell's method gives a robust average normal even for slightly non-convex loops.
            double normalX = 0;
            double normalY = 0;
            double normalZ = 0;
            double centroidX = 0;
            double centroidY = 0;
            double centroidZ = 0;
            for (int i = 0; i < count; i++)
            {
                Point3D current = points[i];
                Point3D next = points[(i + 1) % count];
                normalX += (current.Y - next.Y) * (current.Z + next.Z);
                normalY += (current.Z - next.Z) * (current.X + next.X);
                normalZ += (current.X - next.X) * (current.Y + next.Y);
                centroidX += current.X;
                centroidY += current.Y;
                centroidZ += current.Z;
            }

            double normalLength = System.Math.Sqrt((normalX * normalX) + (normalY * normalY) + (normalZ * normalZ));
            if (normalLength <= 1e-12)
            {
                // Degenerate / collinear loop: nothing to triangulate, treat as planar.
                return true;
            }

            normalX /= normalLength;
            normalY /= normalLength;
            normalZ /= normalLength;
            centroidX /= count;
            centroidY /= count;
            centroidZ /= count;

            double safeTolerance = tolerance > 0 ? tolerance : Tolerance.Distance;
            foreach (Point3D point in points)
            {
                double distance = ((point.X - centroidX) * normalX) + ((point.Y - centroidY) * normalY) + ((point.Z - centroidZ) * normalZ);
                if (System.Math.Abs(distance) > safeTolerance)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
