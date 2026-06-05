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
    public class SAMOCCTShellsSplit : GH_SAMComponent
    {
        public override Guid ComponentGuid => new Guid("fb08a424-65d4-4b4e-8f8a-7af2d13de524");

        public override string LatestComponentVersion => "0.1.0";

        public SAMOCCTShellsSplit()
          : base("SAMOCCT.ShellsSplit", "SAMOCCT.ShellsSplit", "Split overlapping or touching shell volumes into cleaner adjacent pieces", "SAM", "OCCT")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager inputParamManager)
        {
            int index = inputParamManager.AddGenericParameter("_shells", "_shells", "Closed volumes to split against each other. Accepts SAM Shells or closed Rhino Breps/polysurfaces that convert to SAM Shells.", GH_ParamAccess.list);
            inputParamManager[index].DataMapping = GH_DataMapping.Flatten;

            inputParamManager.AddNumberParameter("silverSpacing_", "silverSpacing_", "Snap tolerance", GH_ParamAccess.item, Tolerance.MacroDistance);
            inputParamManager.AddNumberParameter("tolerance_", "tolerance_", "Tolerance", GH_ParamAccess.item, Tolerance.Distance);
            inputParamManager.AddBooleanParameter("_run", "_run", "Run", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager outputParamManager)
        {
            outputParamManager.AddGenericParameter("Shells", "Shells", "Shells split at mutual intersections/shared faces.", GH_ParamAccess.list);
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
                if (Query.TryGetSAMGeometries(objectWrapper, out List<Shell> shells_Temp) && shells_Temp != null)
                {
                    shells.AddRange(shells_Temp);
                }
            }

            List<Shell> resultShells = shells.Split(silverSpacing, Tolerance.Angle, tolerance);
            List<string> diagnostics = new List<string>
            {
                string.Format("SAM_OCCT_SPLIT_SUCCESS: Split {0} input shell(s) into {1} shell(s).", shells.Count, resultShells?.Count ?? 0)
            };

            dataAccess.SetDataList(0, resultShells);
            dataAccess.SetDataList(1, diagnostics);
            dataAccess.SetData(2, resultShells != null && resultShells.Count != 0);
        }
    }
}
