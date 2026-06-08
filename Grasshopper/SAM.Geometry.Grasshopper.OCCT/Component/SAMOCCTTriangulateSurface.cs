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

            List<Face3D> face3Ds = new List<Face3D>();
            foreach (GH_ObjectWrapper objectWrapper in objectWrappers)
            {
                if (Query.TryGetSAMGeometries(objectWrapper, out List<Face3D> face3Ds_Temp) && face3Ds_Temp != null)
                {
                    face3Ds.AddRange(face3Ds_Temp);
                }
            }

            if (face3Ds.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not convert any input into SAM Face3Ds");
                return;
            }

            List<Face3D> triangles = Geometry.OCCT.Create.Triangulate(face3Ds, out OcctCellComplexResult result, linearDeflection, angularDeflection, false, new OcctBuildOptions { Tolerance = tolerance });

            int skippedSmall = 0;
            if (triangles != null && minArea > 0)
            {
                List<Face3D> filtered = new List<Face3D>();
                foreach (Face3D triangle in triangles)
                {
                    if (triangle == null)
                    {
                        continue;
                    }

                    if (triangle.GetArea() < minArea)
                    {
                        skippedSmall++;
                        continue;
                    }

                    filtered.Add(triangle);
                }

                triangles = filtered;
            }

            List<string> diagnostics = result?.Diagnostics?.Select(x => x.ToString()).ToList() ?? new List<string>();
            diagnostics.Add(string.Format("SAM_OCCT_TRIANGULATE_SURFACE: Produced {0} planar Face3D(s) from {1} source face(s); skipped {2} below minArea.", triangles?.Count ?? 0, face3Ds.Count, skippedSmall));

            index = Params.IndexOfOutputParam("Face3Ds");
            if (index != -1)
            {
                dataAccess.SetDataList(index, triangles);
            }

            index = Params.IndexOfOutputParam("Diagnostics");
            if (index != -1)
            {
                dataAccess.SetDataList(index, diagnostics);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, triangles != null && triangles.Count != 0);
            }
        }
    }
}
