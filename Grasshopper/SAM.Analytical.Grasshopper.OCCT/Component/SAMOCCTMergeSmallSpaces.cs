// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Analytical;
using SAM.Analytical.OCCT;
using SAM.Core;
using SAM.Core.Grasshopper;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SAM.Analytical.Grasshopper.OCCT
{
    public class SAMOCCTMergeSmallSpaces : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("9f0e1d2c-3b4a-4c5d-8e6f-7a8b9c0d1e2f");

        public override string LatestComponentVersion => "0.1.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTMergeSmallSpaces()
          : base("SAMOCCT.MergeSmallSpaces", "SAMOCCT.MergeSmallSpaces", "Merge unwanted tiny spaces/cells of a SAM AdjacencyCluster into the best adjacent larger space", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                result.Add(new GH_SAMParam(new GooAdjacencyClusterParam() { Name = "_adjacencyCluster", NickName = "_adjacencyCluster", Description = "SAM Analytical AdjacencyCluster to clean", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number minArea = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "minArea_", NickName = "minArea_", Description = "Minimum acceptable floor area in m². Spaces below this become merge candidates.", Access = GH_ParamAccess.item };
                minArea.SetPersistentData(0.3);
                result.Add(new GH_SAMParam(minArea, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number minVolume = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "minVolume_", NickName = "minVolume_", Description = "Optional minimum acceptable volume in m³. Spaces below this become merge candidates. Leave unset to ignore volume.", Access = GH_ParamAccess.item, Optional = true };
                minVolume.SetPersistentData(0.5);
                result.Add(new GH_SAMParam(minVolume, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number tolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "tolerance_", NickName = "tolerance_", Description = "Distance tolerance for geometry and level comparisons", Access = GH_ParamAccess.item };
                tolerance.SetPersistentData(Tolerance.Distance);
                result.Add(new GH_SAMParam(tolerance, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_String mergeMode = new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "mergeMode_", NickName = "mergeMode_", Description = "Target selection strategy: LongestSharedBoundary, LargestNeighbour, or SameTypeFirst.", Access = GH_ParamAccess.item };
                mergeMode.SetPersistentData(MergeSmallSpacesMode.LongestSharedBoundary.ToString());
                result.Add(new GH_SAMParam(mergeMode, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean allowMergeExternal = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "allowMergeExternal_", NickName = "allowMergeExternal_", Description = "Allow merging a small space into a volumeless/void (external) neighbour cell", Access = GH_ParamAccess.item };
                allowMergeExternal.SetPersistentData(false);
                result.Add(new GH_SAMParam(allowMergeExternal, ParamVisibility.Voluntary));

                GooSpaceParam protectedSpaces = new GooSpaceParam() { Name = "protectedSpaces_", NickName = "protectedSpaces_", Description = "Optional Spaces that must never be merged away or used as a merge target (e.g. shafts and risers).", Access = GH_ParamAccess.list, Optional = true };
                protectedSpaces.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(protectedSpaces, ParamVisibility.Voluntary));

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
                result.Add(new GH_SAMParam(new GooAdjacencyClusterParam() { Name = "adjacencyCluster", NickName = "adjacencyCluster", Description = "Cleaned SAM Analytical AdjacencyCluster", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooSpaceParam() { Name = "mergedSpaces", NickName = "mergedSpaces", Description = "Small spaces that were merged away", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooSpaceParam() { Name = "unmergedSmallSpaces", NickName = "unmergedSmallSpaces", Description = "Small spaces that could not be merged", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "report", NickName = "report", Description = "Merge report and diagnostics", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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

            double minArea = 0.3;
            index = Params.IndexOfInputParam("minArea_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref minArea);
            }

            double minVolumeValue = double.NaN;
            double? minVolume = null;
            index = Params.IndexOfInputParam("minVolume_");
            if (index != -1 && dataAccess.GetData(index, ref minVolumeValue) && !double.IsNaN(minVolumeValue))
            {
                minVolume = minVolumeValue;
            }

            double tolerance = Tolerance.Distance;
            index = Params.IndexOfInputParam("tolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref tolerance);
            }

            MergeSmallSpacesMode mergeMode = MergeSmallSpacesMode.LongestSharedBoundary;
            string mergeModeText = null;
            index = Params.IndexOfInputParam("mergeMode_");
            if (index != -1 && dataAccess.GetData(index, ref mergeModeText) && !string.IsNullOrWhiteSpace(mergeModeText))
            {
                if (!Enum.TryParse(mergeModeText.Trim(), true, out mergeMode))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Format("Unrecognised mergeMode_ '{0}'. Using {1}.", mergeModeText, MergeSmallSpacesMode.LongestSharedBoundary));
                    mergeMode = MergeSmallSpacesMode.LongestSharedBoundary;
                }
            }

            bool allowMergeExternal = false;
            index = Params.IndexOfInputParam("allowMergeExternal_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref allowMergeExternal);
            }

            List<Space> protectedSpaces = new List<Space>();
            index = Params.IndexOfInputParam("protectedSpaces_");
            if (index != -1)
            {
                dataAccess.GetDataList(index, protectedSpaces);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            AdjacencyCluster adjacencyCluster = gooAdjacencyCluster.Value.MergeSmallSpaces(
                out List<Space> mergedSpaces,
                out List<Space> unmergedSmallSpaces,
                out List<string> report,
                minArea: minArea,
                minVolume: minVolume,
                tolerance: tolerance,
                mergeMode: mergeMode,
                allowMergeExternal: allowMergeExternal,
                protectedSpaces: protectedSpaces);
            report?.Add(string.Format("SAM_OCCT_MERGE_TIMING_TOTAL: {0:0.000}s.", stopwatch.Elapsed.TotalSeconds));

            index = Params.IndexOfOutputParam("adjacencyCluster");
            if (index != -1)
            {
                dataAccess.SetData(index, adjacencyCluster == null ? null : new GooAdjacencyCluster(adjacencyCluster));
            }

            index = Params.IndexOfOutputParam("mergedSpaces");
            if (index != -1)
            {
                dataAccess.SetDataList(index, mergedSpaces?.Select(x => new GooSpace(x)));
            }

            index = Params.IndexOfOutputParam("unmergedSmallSpaces");
            if (index != -1)
            {
                dataAccess.SetDataList(index, unmergedSmallSpaces?.Select(x => new GooSpace(x)));
            }

            index = Params.IndexOfOutputParam("report");
            if (index != -1)
            {
                dataAccess.SetDataList(index, report);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, adjacencyCluster != null);
            }

            if (report != null)
            {
                foreach (string line in report)
                {
                    AddRuntimeMessage(adjacencyCluster == null ? GH_RuntimeMessageLevel.Warning : GH_RuntimeMessageLevel.Remark, line);
                }
            }
        }
    }
}
