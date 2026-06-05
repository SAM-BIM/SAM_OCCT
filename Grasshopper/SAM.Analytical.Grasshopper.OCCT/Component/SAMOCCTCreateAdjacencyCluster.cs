// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Analytical;
using SAM.Analytical.OCCT;
using SAM.Core;
using SAM.Core.Grasshopper;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.Grasshopper.OCCT
{
    public class SAMOCCTCreateAdjacencyCluster : GH_SAMComponent
    {
        public override Guid ComponentGuid => new Guid("c104f272-9b10-454f-9825-16e2d41adfa6");

        public override string LatestComponentVersion => "0.1.0";

        public SAMOCCTCreateAdjacencyCluster()
          : base("SAMOCCT.CreateAdjacencyCluster", "SAMOCCT.CreateAdjacencyCluster", "Create SAM AdjacencyCluster from panels using OCCT", "SAM", "OCCT")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager inputParamManager)
        {
            int index = inputParamManager.AddParameter(new GooPanelParam(), "_panels", "_panels", "SAM Analytical Panels", GH_ParamAccess.list);
            inputParamManager[index].DataMapping = GH_DataMapping.Flatten;

            GooSpaceParam gooSpaceParam = new GooSpaceParam();
            gooSpaceParam.Optional = true;
            index = inputParamManager.AddParameter(gooSpaceParam, "spaces_", "spaces_", "SAM Analytical Spaces", GH_ParamAccess.list);
            inputParamManager[index].DataMapping = GH_DataMapping.Flatten;

            inputParamManager.AddNumberParameter("tolerance_", "tolerance_", "OCCT build tolerance", GH_ParamAccess.item, Tolerance.Distance);
            inputParamManager.AddNumberParameter("fuzzyTolerance_", "fuzzyTolerance_", "OCCT fuzzy tolerance", GH_ParamAccess.item, Tolerance.MacroDistance);
            inputParamManager.AddBooleanParameter("_run", "_run", "Run", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager outputParamManager)
        {
            outputParamManager.AddParameter(new GooAdjacencyClusterParam(), "AdjacencyCluster", "AdjacencyCluster", "SAM AdjacencyCluster", GH_ParamAccess.item);
            outputParamManager.AddTextParameter("Diagnostics", "Diagnostics", "OCCT diagnostics", GH_ParamAccess.list);
            outputParamManager.AddBooleanParameter("Successful", "Successful", "Run successfully?", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            dataAccess.SetData(2, false);

            bool run = false;
            if (!dataAccess.GetData(4, ref run) || !run)
            {
                return;
            }

            List<Panel> panels = new List<Panel>();
            if (!dataAccess.GetDataList(0, panels) || panels == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid panels");
                return;
            }

            List<Space> spaces = new List<Space>();
            dataAccess.GetDataList(1, spaces);

            double tolerance = Tolerance.Distance;
            dataAccess.GetData(2, ref tolerance);

            double fuzzyTolerance = Tolerance.MacroDistance;
            dataAccess.GetData(3, ref fuzzyTolerance);

            Log log = new Log();
            AdjacencyCluster adjacencyCluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(spaces, panels, out OcctCellComplexResult result, log, new OcctBuildOptions { Tolerance = tolerance, FuzzyTolerance = fuzzyTolerance });
            List<string> diagnostics = result?.Diagnostics?.Select(x => x.ToString()).ToList();

            dataAccess.SetData(0, adjacencyCluster == null ? null : new GooAdjacencyCluster(adjacencyCluster));
            dataAccess.SetDataList(1, diagnostics);
            dataAccess.SetData(2, adjacencyCluster != null);

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
