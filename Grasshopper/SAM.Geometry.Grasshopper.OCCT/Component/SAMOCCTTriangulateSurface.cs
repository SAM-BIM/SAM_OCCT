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

                global::Grasshopper.Kernel.Parameters.Param_Number tolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "tolerance_", NickName = "tolerance_", Description = "Tolerance", Access = GH_ParamAccess.item };
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

            // Extract each surface's outer boundary as raw 3D points. We deliberately avoid
            // converting to a SAM Face3D first: Face3D is planar, so a warped/non-planar surface
            // would be flattened (or fail) before OCCT could span and mesh it. Keeping the true
            // corners lets OCCT triangulate the real surface.
            List<IReadOnlyList<Point3D>> boundaryLoops = new List<IReadOnlyList<Point3D>>();
            foreach (GH_ObjectWrapper objectWrapper in objectWrappers)
            {
                if (AppendBoundaryLoops(objectWrapper?.Value, boundaryLoops) > 0)
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
                            boundaryLoops.Add(external);
                        }
                    }
                }
            }

            if (boundaryLoops.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not extract any surface boundary loops from the input");
                return;
            }

            List<Triangle3D> triangles = Geometry.OCCT.Create.Triangulate(boundaryLoops, out OcctCellComplexResult result, linearDeflection, angularDeflection, false, new OcctBuildOptions { Tolerance = tolerance });

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

            List<string> diagnostics = result?.Diagnostics?.Select(x => x.ToString()).ToList() ?? new List<string>();
            diagnostics.Add(string.Format("SAM_OCCT_TRIANGULATE_SURFACE: Produced {0} planar Face3D(s) from {1} surface boundary loop(s); skipped {2} below minArea.", panels.Count, boundaryLoops.Count, skippedSmall));

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
    }
}
