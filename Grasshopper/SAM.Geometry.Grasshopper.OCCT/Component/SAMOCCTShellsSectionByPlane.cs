// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using SAM.Core;
using SAM.Core.Grasshopper;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Geometry.Grasshopper.OCCT
{
    public class SAMOCCTShellsSectionByPlane : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("67f00e15-259c-4080-8b34-efb4cd59cb8c");

        public override string LatestComponentVersion => "0.1.1";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTShellsSectionByPlane()
          : base("SAMOCCT.ShellsSectionByPlane", "SAMOCCT.ShellsSectionByPlane", "Create section Face3Ds and split SAM Shells by plane", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                global::Grasshopper.Kernel.Parameters.Param_GenericObject shells = new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_shells", NickName = "_shells", Description = "Closed volumes to section and split. Accepts SAM Shells or closed Rhino Breps/polysurfaces that convert to SAM Shells.", Access = GH_ParamAccess.list };
                shells.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(shells, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "plane_", NickName = "plane_", Description = "SAM/Rhino plane. Uses shell centroid XY plane if omitted.", Access = GH_ParamAccess.item, Optional = true }, ParamVisibility.Voluntary));

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
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "Face3Ds", NickName = "Face3Ds", Description = "Section Face3Ds created where each shell crosses the plane.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "Shells", NickName = "Shells", Description = "Input shells split by the section plane. Use these directly for atrium/level division workflows.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Diagnostics", NickName = "Diagnostics", Description = "Diagnostics", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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

            List<GH_ObjectWrapper> shellWrappers = new List<GH_ObjectWrapper>();
            index = Params.IndexOfInputParam("_shells");
            if (index == -1 || !dataAccess.GetDataList(index, shellWrappers))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid shells");
                return;
            }

            Plane plane = null;
            index = Params.IndexOfInputParam("plane_");
            if (index != -1)
            {
                GH_ObjectWrapper planeWrapper = null;
                if (dataAccess.GetData(index, ref planeWrapper) && planeWrapper != null)
                {
                    if (Query.TryGetSAMGeometries(planeWrapper, out List<Plane> planes) && planes != null && planes.Count != 0)
                    {
                        plane = planes[0];
                    }
                }
            }

            double tolerance = Tolerance.Distance;
            index = Params.IndexOfInputParam("tolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref tolerance);
            }

            List<Shell> shells = new List<Shell>();
            foreach (GH_ObjectWrapper objectWrapper in shellWrappers)
            {
                if (Query.TryGetSAMGeometries(objectWrapper, out List<Shell> shells_Temp) && shells_Temp != null)
                {
                    shells.AddRange(shells_Temp);
                }
            }

            List<Face3D> face3Ds = new List<Face3D>();
            List<Shell> shells_Split = new List<Shell>();
            foreach (Shell shell in shells)
            {
                BoundingBox3D boundingBox3D = shell?.GetBoundingBox();
                if (boundingBox3D == null)
                {
                    continue;
                }

                Plane plane_Temp = plane ?? new Plane(boundingBox3D.GetCentroid(), Vector3D.WorldZ);
                List<Face3D> face3Ds_Temp = shell.Section(plane_Temp, true, Tolerance.Angle, tolerance, Tolerance.MacroDistance);
                if (face3Ds_Temp != null)
                {
                    face3Ds.AddRange(face3Ds_Temp);

                    List<Shell> shells_Split_Temp = shell.Split(face3Ds_Temp, Tolerance.MacroDistance, Tolerance.Angle, tolerance);
                    if (shells_Split_Temp != null && shells_Split_Temp.Count != 0)
                    {
                        shells_Split.AddRange(shells_Split_Temp);
                        continue;
                    }
                }

                shells_Split.Add(new Shell(shell));
            }

            List<string> diagnostics = new List<string>
            {
                string.Format("SAM_OCCT_SECTION_SUCCESS: Created {0} section Face3D(s) and {1} split shell(s).", face3Ds.Count, shells_Split.Count)
            };

            index = Params.IndexOfOutputParam("Face3Ds");
            if (index != -1)
            {
                dataAccess.SetDataList(index, face3Ds);
            }

            index = Params.IndexOfOutputParam("Shells");
            if (index != -1)
            {
                dataAccess.SetDataList(index, shells_Split);
            }

            index = Params.IndexOfOutputParam("Diagnostics");
            if (index != -1)
            {
                dataAccess.SetDataList(index, diagnostics);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, face3Ds.Count != 0 || shells_Split.Count != 0);
            }
        }
    }
}
