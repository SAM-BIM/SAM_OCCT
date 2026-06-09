// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Analytical;
using SAM.Analytical.OCCT;
using SAM.Core;
using SAM.Core.Grasshopper;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.Grasshopper.OCCT
{
    public class SAMOCCTMergeCoplanarAdjacencyCluster : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("d5b9f7e2-3e6c-4a1b-9d28-6c4a0e1f8b37");

        public override string LatestComponentVersion => "0.1.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTMergeCoplanarAdjacencyCluster()
          : base("SAMOCCT.MergeCoplanarAdjacencyCluster", "SAMOCCT.MergeCoplanarAdjacencyCluster", "Merge adjacent coplanar panels of a SAM AdjacencyCluster into fewer, larger panels using the OCCT engine. Only panels sharing the same type, construction, and space adjacency merge, so the analytical topology is preserved; apertures are re-hosted.", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                result.Add(new GH_SAMParam(new GooAdjacencyClusterParam() { Name = "_adjacencyCluster", NickName = "_adjacencyCluster", Description = "SAM Analytical AdjacencyCluster whose coplanar panels should be merged.", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number angleTolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "angleTolerance_", NickName = "angleTolerance_", Description = "Maximum angle (radians) between face normals still treated as coplanar.", Access = GH_ParamAccess.item };
                angleTolerance.SetPersistentData(Tolerance.Angle);
                result.Add(new GH_SAMParam(angleTolerance, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number tolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "tolerance_", NickName = "tolerance_", Description = "Sewing/linear tolerance used to make coincident edges shared before merging.", Access = GH_ParamAccess.item };
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
                result.Add(new GH_SAMParam(new GooAdjacencyClusterParam() { Name = "AdjacencyCluster", NickName = "AdjacencyCluster", Description = "AdjacencyCluster with coplanar panels merged.", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
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

            index = Params.IndexOfInputParam("_adjacencyCluster");
            GooAdjacencyCluster gooAdjacencyCluster = null;
            if (index == -1 || !dataAccess.GetData(index, ref gooAdjacencyCluster) || gooAdjacencyCluster?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid AdjacencyCluster");
                return;
            }

            double angleTolerance = Tolerance.Angle;
            index = Params.IndexOfInputParam("angleTolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref angleTolerance);
            }

            double tolerance = Tolerance.Distance;
            index = Params.IndexOfInputParam("tolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref tolerance);
            }

            AdjacencyCluster adjacencyCluster = gooAdjacencyCluster.Value.MergeCoplanarPanels(out List<string> diagnostics, tolerance, angleTolerance);

            index = Params.IndexOfOutputParam("AdjacencyCluster");
            if (index != -1)
            {
                dataAccess.SetData(index, adjacencyCluster == null ? null : new GooAdjacencyCluster(adjacencyCluster));
            }

            index = Params.IndexOfOutputParam("Diagnostics");
            if (index != -1)
            {
                dataAccess.SetDataList(index, diagnostics);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, adjacencyCluster != null);
            }
        }
    }
}
