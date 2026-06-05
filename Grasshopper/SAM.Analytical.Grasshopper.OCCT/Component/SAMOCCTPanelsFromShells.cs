// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using SAM.Analytical;
using SAM.Core;
using SAM.Core.Grasshopper;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.Grasshopper.OCCT
{
    public class SAMOCCTPanelsFromShells : GH_SAMComponent
    {
        public override Guid ComponentGuid => new Guid("1b63ad05-61c6-4d7b-b4eb-525c9abceac2");

        public override string LatestComponentVersion => "0.1.0";

        public SAMOCCTPanelsFromShells()
          : base("SAMOCCT.PanelsFromShells", "SAMOCCT.PanelsFromShells", "Create SAM Panels from SAM Shells", "SAM", "OCCT")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager inputParamManager)
        {
            int index = inputParamManager.AddGenericParameter("_shells", "_shells", "SAM Geometry Shells", GH_ParamAccess.list);
            inputParamManager[index].DataMapping = GH_DataMapping.Flatten;

            inputParamManager.AddNumberParameter("silverSpacing_", "silverSpacing_", "Silver spacing", GH_ParamAccess.item, Tolerance.MacroDistance);
            inputParamManager.AddNumberParameter("tolerance_", "tolerance_", "Tolerance", GH_ParamAccess.item, Tolerance.Distance);
            inputParamManager.AddBooleanParameter("_run", "_run", "Run", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager outputParamManager)
        {
            outputParamManager.AddParameter(new GooPanelParam(), "Panels", "Panels", "SAM Analytical Panels", GH_ParamAccess.list);
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

            double silverSpacing = Tolerance.MacroDistance;
            dataAccess.GetData(1, ref silverSpacing);

            double tolerance = Tolerance.Distance;
            dataAccess.GetData(2, ref tolerance);

            List<Shell> shells = new List<Shell>();
            foreach (GH_ObjectWrapper objectWrapper in shellWrappers)
            {
                if (global::SAM.Geometry.Grasshopper.Query.TryGetSAMGeometries(objectWrapper, out List<Shell> shells_Temp) && shells_Temp != null)
                {
                    shells.AddRange(shells_Temp);
                }
            }

            List<Panel> panels = new List<Panel>();
            foreach (Shell shell in shells)
            {
                List<Panel> panels_Temp = global::SAM.Analytical.Create.Panels(shell, silverSpacing, tolerance);
                if (panels_Temp != null)
                {
                    panels.AddRange(panels_Temp);
                }
            }

            List<string> diagnostics = new List<string>
            {
                string.Format("SAM_OCCT_PANELS_FROM_SHELLS_SUCCESS: Created {0} panel(s) from {1} shell(s).", panels.Count, shells.Count)
            };

            dataAccess.SetDataList(0, panels.Select(x => new GooPanel(x)));
            dataAccess.SetDataList(1, diagnostics);
            dataAccess.SetData(2, panels.Count != 0);
        }
    }
}
