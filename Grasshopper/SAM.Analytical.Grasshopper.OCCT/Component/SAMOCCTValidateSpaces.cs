// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Core.Grasshopper;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.Grasshopper.OCCT
{
    /// <summary>
    /// GUID-based validation of a finished <see cref="AdjacencyCluster"/> against an expected set of
    /// <see cref="Space"/>s (docs/CONTROLLED_WORKFLOW_PLAN.md §6). Deliberately independent of whatever
    /// space matching <c>SAMOCCT.CreateAdjacencyCluster</c>'s builder performed: this component re-derives
    /// the cluster's cell shells (<see cref="CellGeometry.FromCluster"/>) and runs its own containment-based
    /// <see cref="SpaceMatcher"/> pass.
    /// </summary>
    public class SAMOCCTValidateSpaces : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("f6c9e869-7c84-4574-ae0c-d5f59276bbb3");

        public override string LatestComponentVersion => "1.0.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTValidateSpaces()
          : base("SAMOCCT.ValidateSpaces", "SAMOCCT.ValidateSpaces", "Validate an OCCT-built AdjacencyCluster against an expected set of Spaces (GUID-matched: missing/merged/split/extra cells, double-height, orphan/unused panels)", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                GooAdjacencyClusterParam adjacencyCluster = new GooAdjacencyClusterParam() { Name = "_adjacencyCluster", NickName = "_adjacencyCluster", Description = "The AdjacencyCluster to validate (e.g. from SAMOCCT.CreateAdjacencyCluster).", Access = GH_ParamAccess.item };
                result.Add(new GH_SAMParam(adjacencyCluster, ParamVisibility.Binding));

                GooSpaceParam expectedSpaces = new GooSpaceParam() { Name = "_expectedSpaces", NickName = "_expectedSpaces", Description = "The Spaces the model is expected to contain (GUID identity; Name is a report label only).", Access = GH_ParamAccess.list };
                expectedSpaces.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(expectedSpaces, ParamVisibility.Binding));

                GooPanelParam sourcePanels = new GooPanelParam() { Name = "sourcePanels_", NickName = "sourcePanels_", Description = "Optional: the original input Panels (before OCCT rebuild), for unused-panel detection and missing-separator evidence on every merged pair.", Access = GH_ParamAccess.list, Optional = true };
                sourcePanels.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(sourcePanels, ParamVisibility.Voluntary));

                GooSpaceParam doubleHeightSpaces = new GooSpaceParam() { Name = "doubleHeightSpaces_", NickName = "doubleHeightSpaces_", Description = "Optional: expected Spaces that are known to be double-height (GUID-backed - never inferred from a location).", Access = GH_ParamAccess.list, Optional = true };
                doubleHeightSpaces.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(doubleHeightSpaces, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number levelBand = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "levelBand_", NickName = "levelBand_", Description = "Perpendicular band (m) comparing a cell's vertical span against an expected level datum pair.", Access = GH_ParamAccess.item, Optional = true };
                levelBand.SetPersistentData(0.21);
                result.Add(new GH_SAMParam(levelBand, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number bucketBetweenLevels = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "bucketBetweenLevels_", NickName = "bucketBetweenLevels_", Description = "Perpendicular band (m) merging raw level frames (from sourcePanels_/_adjacencyCluster's panels) into level-group datums used to derive each expected Space's vertical span. Same knob name as Clean3D/Extend3D's future level-datum-merging parameter (semantically the same concept, applied here to validation only).", Access = GH_ParamAccess.item, Optional = true };
                bucketBetweenLevels.SetPersistentData(0.21);
                result.Add(new GH_SAMParam(bucketBetweenLevels, ParamVisibility.Voluntary));

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

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Report", NickName = "Report", Description = "The full SAM_OCCT_SPACEMATCH: report, one coded line per entry.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "Valid", NickName = "Valid", Description = "True when every expected Space matched exactly one cell with a consistent span, every requested double-height check passed, there are no extra cells and no orphan cluster panels.", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new GooSpaceParam() { Name = "MatchedSpaces", NickName = "MatchedSpaces", Description = "Expected Spaces that matched exactly one cell with a consistent span.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new GooSpaceParam() { Name = "MissingSpaces", NickName = "MissingSpaces", Description = "Expected Spaces contained in no cell.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new GooSpaceParam() { Name = "MergedSpaces", NickName = "MergedSpaces", Description = "Expected Spaces that share a cell with another expected Space.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new GooSpaceParam() { Name = "SplitSpaces", NickName = "SplitSpaces", Description = "Expected Spaces whose expected span was not covered by a single cell (partner cells found), or whose double-height check failed.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new GooSpaceParam() { Name = "IncorrectlyBoundedSpaces", NickName = "IncorrectlyBoundedSpaces", Description = "Expected Spaces whose sole matched cell's span does not match the expected span, with no partner cell explaining the mismatch.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "ExtraShells", NickName = "ExtraShells", Description = "Generated cell Shells that contain no expected location.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "SuspectedSeparatorPanels", NickName = "SuspectedSeparatorPanels", Description = "sourcePanels_ candidates that should separate a merged pair but did not contribute to the adjacency (present but unused, or a lateral near-miss).", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "OrphanClusterPanels", NickName = "OrphanClusterPanels", Description = "Generated cluster panels that bound zero spaces.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "UnusedInputPanels", NickName = "UnusedInputPanels", Description = "sourcePanels_ with no coplanar overlapping contribution to any generated cluster face.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "DoubleHeightOk", NickName = "DoubleHeightOk", Description = "Per doubleHeightSpaces_ entry (same order): true when that Space matched a single cell spanning its full expected height with no unexpected intermediate split.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "CellMatches", NickName = "CellMatches", Description = "One line per generated cell: index, outcome, hosted Space Guid(s), volume, centre.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));

                return result.ToArray();
            }
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            int index_Valid = Params.IndexOfOutputParam("Valid");
            if (index_Valid != -1)
            {
                dataAccess.SetData(index_Valid, false);
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
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid _adjacencyCluster");
                return;
            }

            AdjacencyCluster adjacencyCluster = gooAdjacencyCluster.Value;

            List<Space> expectedSpaces = new List<Space>();
            index = Params.IndexOfInputParam("_expectedSpaces");
            if (index == -1 || !dataAccess.GetDataList(index, expectedSpaces) || expectedSpaces == null || expectedSpaces.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid _expectedSpaces");
                return;
            }

            List<Panel> sourcePanels = new List<Panel>();
            index = Params.IndexOfInputParam("sourcePanels_");
            if (index != -1)
            {
                dataAccess.GetDataList(index, sourcePanels);
            }

            List<Space> doubleHeightSpaces = new List<Space>();
            index = Params.IndexOfInputParam("doubleHeightSpaces_");
            if (index != -1)
            {
                dataAccess.GetDataList(index, doubleHeightSpaces);
            }

            double levelBand = 0.21;
            index = Params.IndexOfInputParam("levelBand_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref levelBand);
            }

            double bucketBetweenLevels = 0.21;
            index = Params.IndexOfInputParam("bucketBetweenLevels_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref bucketBetweenLevels);
            }

            List<Guid> doubleHeightGuids = doubleHeightSpaces.Where(x => x != null).Select(x => x.Guid).ToList();

            SpaceMatchOptions options = new SpaceMatchOptions { LevelBand = levelBand, LevelGroupBand = bucketBetweenLevels };

            List<string> diagnostics = new List<string>();
            ExpectedSpaceSet expectedSpaceSet;
            try
            {
                List<Panel> levelSourcePanels = sourcePanels.Count > 0 ? sourcePanels : (adjacencyCluster.GetPanels() ?? new List<Panel>());
                expectedSpaceSet = ExpectedSpaceSet.Create(expectedSpaces, levelSourcePanels, options, doubleHeightGuids);
            }
            catch (ArgumentException exception)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "SAM_OCCT_SPACEMATCH_ERROR: " + exception.Message);
                return;
            }

            diagnostics.AddRange(expectedSpaceSet.Warnings.Select(x => "SAM_OCCT_SPACEMATCH_WARNING: " + x));

            List<CellGeometry> cells = CellGeometry.FromCluster(adjacencyCluster, options);
            SpaceMatchReport report = SpaceMatcher.Match(expectedSpaceSet, cells, doubleHeightGuids, adjacencyCluster, sourcePanels.Count > 0 ? sourcePanels : null);

            List<string> reportLines = new List<string>(diagnostics);
            reportLines.AddRange(report.ToLines());

            List<Space> matchedSpaces = report.SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Matched).Select(x => expectedSpaceSet.TryGetSpace(x.Guid)).Where(x => x != null).ToList();
            List<Space> missingSpaces = report.SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Missing).Select(x => expectedSpaceSet.TryGetSpace(x.Guid)).Where(x => x != null).ToList();
            List<Space> mergedSpaces = report.SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Merged).Select(x => expectedSpaceSet.TryGetSpace(x.Guid)).Where(x => x != null).ToList();
            List<Space> splitSpaces = report.SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Split).Select(x => expectedSpaceSet.TryGetSpace(x.Guid)).Where(x => x != null).ToList();
            List<Space> incorrectlyBoundedSpaces = report.SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.IncorrectlyBounded).Select(x => expectedSpaceSet.TryGetSpace(x.Guid)).Where(x => x != null).ToList();

            HashSet<int> extraCellIndices = new HashSet<int>(report.CellMatches.Where(x => x.Outcome == SpaceMatchOutcome.Extra).Select(x => x.CellIndex));
            List<Shell> extraShells = cells.Where(x => extraCellIndices.Contains(x.Index)).Select(x => x.Shell).ToList();

            List<Panel> allPanels = adjacencyCluster.GetPanels() ?? new List<Panel>();
            List<Panel> suspectedSeparatorPanels = report.SeparatorFindings
                .Where(x => x.PanelGuid.HasValue)
                .Select(x => sourcePanels.FirstOrDefault(p => p?.Guid == x.PanelGuid.Value))
                .Where(x => x != null)
                .Distinct()
                .ToList();

            List<Panel> orphanClusterPanels = allPanels.Where(x => x != null && report.OrphanClusterPanelGuids.Contains(x.Guid)).ToList();
            List<Panel> unusedInputPanels = sourcePanels.Where(x => x != null && report.UnusedInputPanelGuids.Contains(x.Guid)).ToList();

            List<bool> doubleHeightOk = doubleHeightSpaces.Where(x => x != null).Select(x => report.DoubleHeightOk.TryGetValue(x.Guid, out bool ok) && ok).ToList();

            List<string> cellMatchLines = report.CellMatches
                .OrderBy(x => x.CellIndex)
                .Select(x => string.Format(
                    "cell {0}: outcome={1} spaces=[{2}] volume={3:0.###} centre={4}",
                    x.CellIndex, x.Outcome, string.Join(",", x.SpaceGuids), x.Volume,
                    x.Center == null ? "(null)" : string.Format("({0:0.###},{1:0.###},{2:0.###})", x.Center.X, x.Center.Y, x.Center.Z)))
                .ToList();

            index = Params.IndexOfOutputParam("Report");
            if (index != -1)
            {
                dataAccess.SetDataList(index, reportLines);
            }

            if (index_Valid != -1)
            {
                dataAccess.SetData(index_Valid, report.Valid);
            }

            SetListOutput(dataAccess, "MatchedSpaces", matchedSpaces.Select(x => new GooSpace(x)).ToList());
            SetListOutput(dataAccess, "MissingSpaces", missingSpaces.Select(x => new GooSpace(x)).ToList());
            SetListOutput(dataAccess, "MergedSpaces", mergedSpaces.Select(x => new GooSpace(x)).ToList());
            SetListOutput(dataAccess, "SplitSpaces", splitSpaces.Select(x => new GooSpace(x)).ToList());
            SetListOutput(dataAccess, "IncorrectlyBoundedSpaces", incorrectlyBoundedSpaces.Select(x => new GooSpace(x)).ToList());
            SetListOutput(dataAccess, "ExtraShells", extraShells);
            SetListOutput(dataAccess, "SuspectedSeparatorPanels", suspectedSeparatorPanels.Select(x => new GooPanel(x)).ToList());
            SetListOutput(dataAccess, "OrphanClusterPanels", orphanClusterPanels.Select(x => new GooPanel(x)).ToList());
            SetListOutput(dataAccess, "UnusedInputPanels", unusedInputPanels.Select(x => new GooPanel(x)).ToList());
            SetListOutput(dataAccess, "DoubleHeightOk", doubleHeightOk);
            SetListOutput(dataAccess, "CellMatches", cellMatchLines);

            foreach (string diagnostic in reportLines)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, diagnostic);
            }

            if (!report.Valid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "SAM_OCCT_SPACEMATCH: Valid=false - see Report for details.");
            }
        }

        private void SetListOutput<T>(IGH_DataAccess dataAccess, string name, List<T> values)
        {
            int index = Params.IndexOfOutputParam(name);
            if (index != -1)
            {
                dataAccess.SetDataList(index, values);
            }
        }
    }
}
