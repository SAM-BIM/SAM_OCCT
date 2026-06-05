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

        public override string LatestComponentVersion => "0.1.0";

        public SAMOCCTShellsSectionByPlane()
          : base("SAMOCCT.ShellsSectionByPlane", "SAMOCCT.ShellsSectionByPlane", "Create section Face3Ds from SAM Shells", "SAM", "OCCT")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager inputParamManager)
        {
            int index = inputParamManager.AddGenericParameter("_shells", "_shells", "SAM Geometry Shells", GH_ParamAccess.list);
            inputParamManager[index].DataMapping = GH_DataMapping.Flatten;

            inputParamManager.AddGenericParameter("plane_", "plane_", "SAM/Rhino plane. Uses shell centroid XY plane if omitted.", GH_ParamAccess.item);
            inputParamManager[1].Optional = true;
            inputParamManager.AddNumberParameter("tolerance_", "tolerance_", "Tolerance", GH_ParamAccess.item, Tolerance.Distance);
            inputParamManager.AddBooleanParameter("_run", "_run", "Run", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager outputParamManager)
        {
            outputParamManager.AddGenericParameter("Face3Ds", "Face3Ds", "Section SAM Face3Ds", GH_ParamAccess.list);
            outputParamManager.AddTextParameter("Diagnostics", "Diagnostics", "Diagnostics", GH_ParamAccess.list);
            outputParamManager.AddBooleanParameter("Successful", "Successful", "Run successfully?", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            dataAccess.SetData(2, false);

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
            foreach (Shell shell in shells)
            {
                Plane plane_Temp = plane ?? new Plane(shell.GetBoundingBox().GetCentroid(), Vector3D.WorldZ);
                List<Face3D> face3Ds_Temp = shell.Section(plane_Temp, true, Tolerance.Angle, tolerance, Tolerance.MacroDistance);
                if (face3Ds_Temp != null)
                {
                    face3Ds.AddRange(face3Ds_Temp);
                }
            }

            List<string> diagnostics = new List<string>
            {
                string.Format("SAM_OCCT_SECTION_SUCCESS: Created {0} section Face3D(s).", face3Ds.Count)
            };

            dataAccess.SetDataList(0, face3Ds);
            dataAccess.SetDataList(1, diagnostics);
            dataAccess.SetData(2, face3Ds.Count != 0);
        }
    }
}
