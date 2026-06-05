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
    public class SAMOCCTShellsSectionByPlane : GH_SAMComponent
    {
        public override Guid ComponentGuid => new Guid("67f00e15-259c-4080-8b34-efb4cd59cb8c");

        public override string LatestComponentVersion => "0.1.1";

        public SAMOCCTShellsSectionByPlane()
          : base("SAMOCCT.ShellsSectionByPlane", "SAMOCCT.ShellsSectionByPlane", "Create section Face3Ds and split SAM Shells by plane", "SAM", "OCCT")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager inputParamManager)
        {
            int index = inputParamManager.AddGenericParameter("_shells", "_shells", "Closed volumes to section and split. Accepts SAM Shells or closed Rhino Breps/polysurfaces that convert to SAM Shells.", GH_ParamAccess.list);
            inputParamManager[index].DataMapping = GH_DataMapping.Flatten;

            inputParamManager.AddGenericParameter("plane_", "plane_", "SAM/Rhino plane. Uses shell centroid XY plane if omitted.", GH_ParamAccess.item);
            inputParamManager[1].Optional = true;
            inputParamManager.AddNumberParameter("tolerance_", "tolerance_", "Tolerance", GH_ParamAccess.item, Tolerance.Distance);
            inputParamManager.AddBooleanParameter("_run", "_run", "Run", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager outputParamManager)
        {
            outputParamManager.AddGenericParameter("Face3Ds", "Face3Ds", "Section Face3Ds created where each shell crosses the plane.", GH_ParamAccess.list);
            outputParamManager.AddGenericParameter("Shells", "Shells", "Input shells split by the section plane. Use these directly for atrium/level division workflows.", GH_ParamAccess.list);
            outputParamManager.AddTextParameter("Diagnostics", "Diagnostics", "Diagnostics", GH_ParamAccess.list);
            outputParamManager.AddBooleanParameter("Successful", "Successful", "Run successfully?", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            dataAccess.SetData(3, false);

            bool run = false;
            if (!dataAccess.GetData(3, ref run) || !run)
            {
                return;
            }

            List<GH_ObjectWrapper> shellWrappers = new List<GH_ObjectWrapper>();
            if (!dataAccess.GetDataList(0, shellWrappers))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid shells");
                return;
            }

            GH_ObjectWrapper planeWrapper = null;
            Plane plane = null;
            if (dataAccess.GetData(1, ref planeWrapper) && planeWrapper != null)
            {
                if (Query.TryGetSAMGeometries(planeWrapper, out List<Plane> planes) && planes != null && planes.Count != 0)
                {
                    plane = planes[0];
                }
            }

            double tolerance = Tolerance.Distance;
            dataAccess.GetData(2, ref tolerance);

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

            dataAccess.SetDataList(0, face3Ds);
            dataAccess.SetDataList(1, shells_Split);
            dataAccess.SetDataList(2, diagnostics);
            dataAccess.SetData(3, face3Ds.Count != 0 || shells_Split.Count != 0);
        }
    }
}
