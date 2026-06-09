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
using System.Diagnostics;
using System.Linq;

namespace SAM.Geometry.Grasshopper.OCCT
{
    public class SAMOCCTMergeSmallShells : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("6c1a7e3d-2f4b-4a9c-bd8e-1f0a2b3c4d5e");

        public override string LatestComponentVersion => "0.1.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTMergeSmallShells()
          : base("SAMOCCT.MergeSmallShells", "SAMOCCT.MergeSmallShells", "Merge unwanted tiny closed shells/cells into the best adjacent larger shell using OCCT union", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                global::Grasshopper.Kernel.Parameters.Param_GenericObject shells = new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_shells", NickName = "_shells", Description = "Closed volumes to clean. Accepts SAM Shells or closed Rhino Breps/polysurfaces that convert to SAM Shells.", Access = GH_ParamAccess.list };
                shells.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(shells, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number minArea = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "minArea_", NickName = "minArea_", Description = "Minimum acceptable floor footprint in m². Cells below this become merge candidates.", Access = GH_ParamAccess.item };
                minArea.SetPersistentData(0.3);
                result.Add(new GH_SAMParam(minArea, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number minVolume = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "minVolume_", NickName = "minVolume_", Description = "Optional minimum acceptable volume in m³ (true OCCT cell volume). Cells below this become merge candidates. Leave unset to ignore volume.", Access = GH_ParamAccess.item, Optional = true };
                minVolume.SetPersistentData(0.5);
                result.Add(new GH_SAMParam(minVolume, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number tolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "tolerance_", NickName = "tolerance_", Description = "Distance tolerance for adjacency and footprint comparisons, and OCCT build tolerance", Access = GH_ParamAccess.item };
                tolerance.SetPersistentData(Tolerance.Distance);
                result.Add(new GH_SAMParam(tolerance, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number fuzzyTolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "fuzzyTolerance_", NickName = "fuzzyTolerance_", Description = "OCCT fuzzy tolerance for the union step", Access = GH_ParamAccess.item };
                fuzzyTolerance.SetPersistentData(Tolerance.MacroDistance);
                result.Add(new GH_SAMParam(fuzzyTolerance, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_String mergeMode = new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "mergeMode_", NickName = "mergeMode_", Description = "Target selection strategy: LongestSharedBoundary or LargestNeighbour.", Access = GH_ParamAccess.item };
                mergeMode.SetPersistentData(MergeShellsMode.LongestSharedBoundary.ToString());
                result.Add(new GH_SAMParam(mergeMode, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_GenericObject protectedShells = new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "protectedShells_", NickName = "protectedShells_", Description = "Optional Shells/Breps that must never be merged away or used as a merge target (e.g. shafts and risers). Matched to decoded cells by containment.", Access = GH_ParamAccess.list, Optional = true };
                protectedShells.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(protectedShells, ParamVisibility.Voluntary));

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
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "Shells", NickName = "Shells", Description = "Cleaned shells. Small shells are fused into the best adjacent larger shell.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "mergedSmallShells", NickName = "mergedSmallShells", Description = "Small shells that were merged away", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "unmergedSmallShells", NickName = "unmergedSmallShells", Description = "Small shells that could not be merged", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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

            List<GH_ObjectWrapper> shellWrappers = new List<GH_ObjectWrapper>();
            index = Params.IndexOfInputParam("_shells");
            if (index == -1 || !dataAccess.GetDataList(index, shellWrappers) || shellWrappers == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid shells");
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

            double fuzzyTolerance = Tolerance.MacroDistance;
            index = Params.IndexOfInputParam("fuzzyTolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref fuzzyTolerance);
            }

            MergeShellsMode mergeMode = MergeShellsMode.LongestSharedBoundary;
            string mergeModeText = null;
            index = Params.IndexOfInputParam("mergeMode_");
            if (index != -1 && dataAccess.GetData(index, ref mergeModeText) && !string.IsNullOrWhiteSpace(mergeModeText))
            {
                if (!Enum.TryParse(mergeModeText.Trim(), true, out mergeMode))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Format("Unrecognised mergeMode_ '{0}'. Using {1}.", mergeModeText, MergeShellsMode.LongestSharedBoundary));
                    mergeMode = MergeShellsMode.LongestSharedBoundary;
                }
            }

            List<GH_ObjectWrapper> protectedWrappers = new List<GH_ObjectWrapper>();
            index = Params.IndexOfInputParam("protectedShells_");
            if (index != -1)
            {
                dataAccess.GetDataList(index, protectedWrappers);
            }

            List<Shell> shells = new List<Shell>();
            foreach (GH_ObjectWrapper objectWrapper in shellWrappers)
            {
                if (Query.TryGetSAMGeometries(objectWrapper, out List<Shell> shells_Temp) && shells_Temp != null)
                {
                    shells.AddRange(shells_Temp);
                }
            }

            List<Shell> protectedShells = new List<Shell>();
            foreach (GH_ObjectWrapper objectWrapper in protectedWrappers)
            {
                if (Query.TryGetSAMGeometries(objectWrapper, out List<Shell> shells_Temp) && shells_Temp != null)
                {
                    protectedShells.AddRange(shells_Temp);
                }
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            List<Shell> resultShells = Geometry.OCCT.Modify.MergeSmallShells(
                shells,
                out List<Shell> mergedSmallShells,
                out List<Shell> unmergedSmallShells,
                out List<string> report,
                minArea: minArea,
                minVolume: minVolume,
                tolerance: tolerance,
                fuzzyTolerance: fuzzyTolerance,
                mergeMode: mergeMode,
                protectedShells: protectedShells);
            report?.Add(string.Format("SAM_OCCT_MERGE_SHELLS_TIMING_TOTAL: {0:0.000}s.", stopwatch.Elapsed.TotalSeconds));

            index = Params.IndexOfOutputParam("Shells");
            if (index != -1)
            {
                dataAccess.SetDataList(index, resultShells);
            }

            index = Params.IndexOfOutputParam("mergedSmallShells");
            if (index != -1)
            {
                dataAccess.SetDataList(index, mergedSmallShells);
            }

            index = Params.IndexOfOutputParam("unmergedSmallShells");
            if (index != -1)
            {
                dataAccess.SetDataList(index, unmergedSmallShells);
            }

            index = Params.IndexOfOutputParam("report");
            if (index != -1)
            {
                dataAccess.SetDataList(index, report);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, resultShells != null && resultShells.Count != 0);
            }

            if (report != null)
            {
                foreach (string line in report)
                {
                    AddRuntimeMessage(resultShells == null ? GH_RuntimeMessageLevel.Warning : GH_RuntimeMessageLevel.Remark, line);
                }
            }
        }
    }
}
