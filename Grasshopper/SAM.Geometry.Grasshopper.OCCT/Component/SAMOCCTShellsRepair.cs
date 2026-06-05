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
    public class SAMOCCTShellsRepair : GH_SAMComponent
    {
        public override Guid ComponentGuid => new Guid("6bc5378d-8759-43ae-93f7-e89c652fbf91");

        public override string LatestComponentVersion => "0.1.0";

        public SAMOCCTShellsRepair()
          : base("SAMOCCT.ShellsRepair", "SAMOCCT.ShellsRepair", "Rebuild and repair each closed shell volume through OCCT", "SAM", "OCCT")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager inputParamManager)
        {
            int index = inputParamManager.AddGenericParameter("_shells", "_shells", "Closed volumes to repair. Accepts SAM Shells or closed Rhino Breps/polysurfaces that convert to SAM Shells.", GH_ParamAccess.list);
            inputParamManager[index].DataMapping = GH_DataMapping.Flatten;

            inputParamManager.AddNumberParameter("tolerance_", "tolerance_", "OCCT build tolerance", GH_ParamAccess.item, Tolerance.Distance);
            inputParamManager.AddNumberParameter("fuzzyTolerance_", "fuzzyTolerance_", "OCCT fuzzy tolerance", GH_ParamAccess.item, Tolerance.MacroDistance);
            inputParamManager.AddBooleanParameter("_run", "_run", "Run", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager outputParamManager)
        {
            outputParamManager.AddGenericParameter("Shells", "Shells", "Repaired shell volumes. Each input is rebuilt independently so adjacent rooms are not merged.", GH_ParamAccess.list);
            outputParamManager.AddTextParameter("Diagnostics", "Diagnostics", "OCCT diagnostics", GH_ParamAccess.list);
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

            double tolerance = Tolerance.Distance;
            dataAccess.GetData(1, ref tolerance);

            double fuzzyTolerance = Tolerance.MacroDistance;
            dataAccess.GetData(2, ref fuzzyTolerance);

            List<Shell> shells = new List<Shell>();
            foreach (GH_ObjectWrapper objectWrapper in shellWrappers)
            {
                if (Query.TryGetSAMGeometries(objectWrapper, out List<Shell> shells_Temp) && shells_Temp != null)
                {
                    shells.AddRange(shells_Temp);
                }
            }

            List<Shell> resultShells = Geometry.OCCT.Query.ShellsRepair(shells, out OcctCellComplexResult result, new OcctBuildOptions { Tolerance = tolerance, FuzzyTolerance = fuzzyTolerance });
            List<string> diagnostics = result?.Diagnostics?.Select(x => x.ToString()).ToList();

            dataAccess.SetDataList(0, resultShells);
            dataAccess.SetDataList(1, diagnostics);
            dataAccess.SetData(2, resultShells != null && resultShells.Count != 0);

            if (diagnostics != null)
            {
                foreach (string diagnostic in diagnostics)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, diagnostic);
                }
            }
        }
    }
}
