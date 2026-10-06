// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Analytical;
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
    public class SAMOCCTCreateTower : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("2e41f077-e07f-41a5-83bc-d1a28d58a25b");

        public override string LatestComponentVersion => "0.1.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTCreateTower()
          : base("SAMOCCT.CreateTower", "SAMOCCT.CreateTower", "Create a watertight, zoned, multi-level tower AdjacencyCluster using OCCT cell building (issue #12 test component). Each floor is split into four perimeter zones plus a central core; floors are rotated progressively to form the twist. Spaces are named Floor_{level}_Zone_{NORTH|EAST|SOUTH|WEST|CORE}.", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                global::Grasshopper.Kernel.Parameters.Param_Number height = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_height", NickName = "_height", Description = "Total tower height [m]", Access = GH_ParamAccess.item };
                height.SetPersistentData(30.0);
                result.Add(new GH_SAMParam(height, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number twistAngle = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "twistAngle_", NickName = "twistAngle_", Description = "Total twist over the full height [radians]. Applied linearly per floor; 0 gives an untwisted prism.", Access = GH_ParamAccess.item };
                twistAngle.SetPersistentData(0.0);
                result.Add(new GH_SAMParam(twistAngle, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Integer floors = new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "_floors", NickName = "_floors", Description = "Number of storeys (at least 1). Each storey produces five zones: NORTH, EAST, SOUTH, WEST, and CORE.", Access = GH_ParamAccess.item };
                floors.SetPersistentData(3);
                result.Add(new GH_SAMParam(floors, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number width = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "width_", NickName = "width_", Description = "Outer square plan dimension [m]", Access = GH_ParamAccess.item };
                width.SetPersistentData(20.0);
                result.Add(new GH_SAMParam(width, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number coreInset = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "coreInset_", NickName = "coreInset_", Description = "Inward offset of the core from the perimeter [m]. Must be greater than 0 and less than half the width.", Access = GH_ParamAccess.item };
                coreInset.SetPersistentData(5.0);
                result.Add(new GH_SAMParam(coreInset, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number fuzzyTolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "fuzzyTolerance_", NickName = "fuzzyTolerance_", Description = "OCCT fuzzy tolerance [m]", Access = GH_ParamAccess.item };
                fuzzyTolerance.SetPersistentData(Tolerance.MacroDistance);
                result.Add(new GH_SAMParam(fuzzyTolerance, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number tolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "tolerance_", NickName = "tolerance_", Description = "OCCT and SAM model tolerance [m]", Access = GH_ParamAccess.item };
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
                result.Add(new GH_SAMParam(new GooAdjacencyClusterParam() { Name = "AdjacencyCluster", NickName = "AdjacencyCluster", Description = "SAM Analytical AdjacencyCluster", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Diagnostics", NickName = "Diagnostics", Description = "Creation diagnostics", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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

            int index_Diagnostics = Params.IndexOfOutputParam("Diagnostics");

            int index;

            bool run = false;
            index = Params.IndexOfInputParam("_run");
            if (index == -1 || !dataAccess.GetData(index, ref run) || !run)
            {
                return;
            }

            double height = 30.0;
            index = Params.IndexOfInputParam("_height");
            if (index == -1 || !dataAccess.GetData(index, ref height))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid height");
                return;
            }

            int floors = 3;
            index = Params.IndexOfInputParam("_floors");
            if (index == -1 || !dataAccess.GetData(index, ref floors))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid floors");
                return;
            }

            double twistAngle = 0.0;
            index = Params.IndexOfInputParam("twistAngle_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref twistAngle);
            }

            double width = 20.0;
            index = Params.IndexOfInputParam("width_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref width);
            }

            double coreInset = 5.0;
            index = Params.IndexOfInputParam("coreInset_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref coreInset);
            }

            double fuzzyTolerance = Tolerance.MacroDistance;
            index = Params.IndexOfInputParam("fuzzyTolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref fuzzyTolerance);
            }

            double tolerance = Tolerance.Distance;
            index = Params.IndexOfInputParam("tolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref tolerance);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            List<string> diagnostics = new List<string>();
            AdjacencyCluster adjacencyCluster = global::SAM.Analytical.OCCT.Create.Tower(
                height,
                twistAngle,
                floors,
                out OcctCellComplexResult cellComplexResult,
                new Log(),
                new OcctBuildOptions { Tolerance = tolerance, FuzzyTolerance = fuzzyTolerance },
                width,
                coreInset);
            diagnostics.Add(string.Format("SAM_OCCT_TIMING_TOWER: {0:0.000}s.", stopwatch.Elapsed.TotalSeconds));

            if (cellComplexResult?.Diagnostics != null)
            {
                diagnostics.AddRange(cellComplexResult.Diagnostics.Select(x => x.ToString()));
            }

            if (adjacencyCluster == null)
            {
                diagnostics.Add("SAM_OCCT_TOWER_FAILED: OCCT could not create a valid tower adjacency cluster.");
            }
            else
            {
                diagnostics.Add(string.Format("SAM_OCCT_TOWER_SUCCESS: Created tower adjacency cluster with {0} space(s) and {1} panel(s).", adjacencyCluster.GetSpaces()?.Count ?? 0, adjacencyCluster.GetPanels()?.Count ?? 0));

                adjacencyCluster.Cut(0, null, tolerance);
                adjacencyCluster.UpdatePanelTypes(0);
                adjacencyCluster.SetDefaultConstructionByPanelType();
            }

            index = Params.IndexOfOutputParam("AdjacencyCluster");
            if (index != -1)
            {
                dataAccess.SetData(index, adjacencyCluster == null ? null : new GooAdjacencyCluster(adjacencyCluster));
            }

            if (index_Diagnostics != -1)
            {
                dataAccess.SetDataList(index_Diagnostics, diagnostics);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, adjacencyCluster != null);
            }

            foreach (string diagnostic in diagnostics)
            {
                AddRuntimeMessage(adjacencyCluster == null ? GH_RuntimeMessageLevel.Warning : GH_RuntimeMessageLevel.Remark, diagnostic);
            }
        }
    }
}
