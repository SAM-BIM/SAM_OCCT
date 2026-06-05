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
using System.Diagnostics;
using System.Linq;

namespace SAM.Analytical.Grasshopper.OCCT
{
    public class SAMOCCTCreateAdjacencyCluster : GH_SAMComponent
    {
        public override Guid ComponentGuid => new Guid("c104f272-9b10-454f-9825-16e2d41adfa6");

        public override string LatestComponentVersion => "0.1.0";

        public SAMOCCTCreateAdjacencyCluster()
          : base("SAMOCCT.CreateAdjacencyCluster", "SAMOCCT.CreateAdjacencyCluster", "Create a SAM AdjacencyCluster from analytical Panels using OCCT cell building", "SAM", "OCCT")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager inputParamManager)
        {
            int index = inputParamManager.AddParameter(new GooPanelParam(), "_panels", "_panels", "Analytical Panels that define closed cell boundaries. Use this when your model starts from panels rather than closed Shells.", GH_ParamAccess.list);
            inputParamManager[index].DataMapping = GH_DataMapping.Flatten;

            GooSpaceParam gooSpaceParam = new GooSpaceParam();
            gooSpaceParam.Optional = true;
            index = inputParamManager.AddParameter(gooSpaceParam, "spaces_", "spaces_", "Optional existing Spaces to match into the OCCT-created cells.", GH_ParamAccess.list);
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

            List<string> diagnostics = new List<string>();
            Stopwatch stopwatch_Total = Stopwatch.StartNew();
            Stopwatch stopwatch = Stopwatch.StartNew();
            Log log = new Log();
            diagnostics.Add(string.Format("SAM_OCCT_ANALYTICAL_PANEL_METADATA: Supplied {0} panel(s) and {1} existing space(s). Panel OCCT path will match supplied spaces after OCCT cell creation and otherwise use auto-generated names.", panels?.Count ?? 0, spaces?.Count ?? 0));
            AdjacencyCluster adjacencyCluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(spaces, panels, out OcctCellComplexResult result, log, new OcctBuildOptions { Tolerance = tolerance, FuzzyTolerance = fuzzyTolerance });
            diagnostics.Add(string.Format("SAM_OCCT_TIMING_OCCT_AND_ADJACENCY: {0:0.000}s.", stopwatch.Elapsed.TotalSeconds));

            if (result?.Diagnostics != null)
            {
                diagnostics.AddRange(result.Diagnostics.Select(x => x.ToString()));
            }

            if (adjacencyCluster == null)
            {
                diagnostics.Add("SAM_OCCT_ANALYTICAL_PANEL_REBUILD_FAILED: OCCT could not create a valid adjacency cluster from the supplied panels.");
            }
            else
            {
                diagnostics.Add(string.Format("SAM_OCCT_ANALYTICAL_PANEL_SUCCESS: Created adjacency cluster with {0} space(s) and {1} panel(s).", adjacencyCluster.GetSpaces()?.Count ?? 0, adjacencyCluster.GetPanels()?.Count ?? 0));
            }
            diagnostics.Add(string.Format("SAM_OCCT_TIMING_TOTAL: {0:0.000}s.", stopwatch_Total.Elapsed.TotalSeconds));

            dataAccess.SetData(0, adjacencyCluster == null ? null : new GooAdjacencyCluster(adjacencyCluster));
            dataAccess.SetDataList(1, diagnostics);
            dataAccess.SetData(2, adjacencyCluster != null);

            foreach (string diagnostic in diagnostics)
            {
                AddRuntimeMessage(adjacencyCluster == null ? GH_RuntimeMessageLevel.Warning : GH_RuntimeMessageLevel.Remark, diagnostic);
            }
        }
    }
}
