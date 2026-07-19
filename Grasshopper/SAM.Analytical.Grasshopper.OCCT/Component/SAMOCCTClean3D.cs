// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Analytical.Solver;
using SAM.Core;
using SAM.Core.Grasshopper;
using SAM.Geometry.Grasshopper;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.Grasshopper.OCCT
{
    /// <summary>
    /// Step 1 of the 3D panel solver - the clean bucket. Produces clean single panels (external shape only,
    /// within-bucket parallels snapped onto one backer, contained/overlapping coplanar panels merged) without
    /// any extend or native resolve, so bucket values can be tuned and reviewed in isolation.
    /// </summary>
    public class SAMOCCTClean3D : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("8f2c1a47-6b39-4d2e-9a51-7c0e4b8d3f12");

        public override string LatestComponentVersion => "0.6.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTClean3D()
          : base("SAMOCCT.Clean3D", "SAMOCCT.Clean3D", "Step 1 of the 3D panel solver: clean bucket. Strips internal openings, snaps within-bucket near-parallel panels onto one backer, and merges contained/overlapping coplanar panels into single clean panels. No extend/resolve - use to tune the bucket and review the clean panels before SAMOCCT.Solve3D.", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                GooPanelParam panels = new GooPanelParam() { Name = "_panels", NickName = "_panels", Description = "SAM Analytical Panels to clean. Air panels are ignored.", Access = GH_ParamAccess.list };
                panels.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(panels, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number minBucketSize = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "bucket_", NickName = "bucket_", Description = "Capture half-width (m). Parallel panels within this band snap onto one backer. Larger = more merging. Renamed from minBucketSize_ in v0.6.0; reads the old name for backward compatibility.", Access = GH_ParamAccess.item };
                minBucketSize.SetPersistentData(0.4);
                result.Add(new GH_SAMParam(minBucketSize, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number thicknessFactor = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "thicknessFactor_", NickName = "thicknessFactor_", Description = "Fraction of construction thickness used as the capture half-width (floored at minBucketSize).", Access = GH_ParamAccess.item };
                thicknessFactor.SetPersistentData(0.6);
                result.Add(new GH_SAMParam(thicknessFactor, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number alignColinearOffset = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "alignColinearOffset_", NickName = "alignColinearOffset_", Description = "Max perpendicular offset (m) at which consecutive segments of one vertical wall run are aligned onto a single plane (closes small step jogs in an imported wall). Keep below the gap between genuinely separate parallel walls so those stay put. 0 = disable. Default 0.3.", Access = GH_ParamAccess.item };
                alignColinearOffset.SetPersistentData(0.3);
                result.Add(new GH_SAMParam(alignColinearOffset, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number normalizeCapOffset = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "normalizeCapOffset_", NickName = "normalizeCapOffset_", Description = "Max perpendicular offset (m) within which a level's floor/roof tiles are normalized onto one plane (the dominant cap's). Collapses the small plane differences left when several imported roof/floor tiles over one space are merged at slightly different tilts/elevations, so the kernel can close the cell. Floors and roofs separate automatically. 0 = disable. Default 0.3.", Access = GH_ParamAccess.item };
                normalizeCapOffset.SetPersistentData(0.3);
                result.Add(new GH_SAMParam(normalizeCapOffset, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number bucketBetweenLevels = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "bucketBetweenLevels_", NickName = "bucketBetweenLevels_", Description = "Level-group merge band (m, P2). GH default 0.21; 0 = off. Merges near-coplanar slab-skin datums onto one storey datum while the raw LevelFrame band stays pinned at 0.15 m. SAM_Solver uses the same name (and GH default 0.21) for a final cross-level WALL re-snap; SAM_OCCT instead merges LEVEL DATUMS and performs no cross-level wall re-snap. Values >= 0.25 can consume a genuine split-level landing and should be tuned per model.", Access = GH_ParamAccess.item };
                bucketBetweenLevels.SetPersistentData(SolverComponentDefaults.BucketBetweenLevels);
                result.Add(new GH_SAMParam(bucketBetweenLevels, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number doubleWallGap = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "doubleWallGap_", NickName = "doubleWallGap_", Description = "EXPLICIT double-wall merge gap (m), default 0 = OFF. After the bucket/align snap, every chain of overlapping parallel walls within this gap is consolidated onto ONE plane. Stamped BucketSize also acts as that panel's consolidation range (walls AND floors/roofs). The 3D analogue of the 2D SnapSolver.PerpendicularMergeTolerance.", Access = GH_ParamAccess.item };
                doubleWallGap.SetPersistentData(0.0);
                result.Add(new GH_SAMParam(doubleWallGap, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number slitMinGap = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "slitMinGap_", NickName = "slitMinGap_", Description = "Minimum perpendicular gap (m) of a remaining double-wall/slit to report. Floored at the bucket capture width so only parallel panels OUTSIDE the bucket (not captured/merged by it) are reported.", Access = GH_ParamAccess.item };
                slitMinGap.SetPersistentData(0.02);
                result.Add(new GH_SAMParam(slitMinGap, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number slitMaxGap = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "slitMaxGap_", NickName = "slitMaxGap_", Description = "Maximum perpendicular gap (m) of a remaining double-wall/slit to report in the slits diagnostics.", Access = GH_ParamAccess.item };
                slitMaxGap.SetPersistentData(0.5);
                result.Add(new GH_SAMParam(slitMaxGap, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number slitMaxOverlap = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "slitMaxOverlap_", NickName = "slitMaxOverlap_", Description = "Maximum parallel overlap length (m) reported as a slit. Longer side-by-side runs are ignored. 0 = no limit.", Access = GH_ParamAccess.item };
                slitMaxOverlap.SetPersistentData(2.0);
                result.Add(new GH_SAMParam(slitMaxOverlap, ParamVisibility.Voluntary));

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
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "Panels", NickName = "Panels", Description = "Clean single panels (Step 1 output). Carry BucketSize/Weight so SAMAnalytical.Visualize draws the capture slab (bucket) in the middle of each panel.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "Slits", NickName = "Slits", Description = "Remaining double-wall/slit diagnostics as short connector Segment3Ds between near-parallel wall axes in the clean panels. Same detection as AutoTuneSolver.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "SlitPanels", NickName = "SlitPanels", Description = "Clean panels whose section axes touch a remaining slit. Use these to spot double-wall/problem panels and target bucket-size overrides.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Diagnostics", NickName = "Diagnostics", Description = "Diagnostics", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "Successful", NickName = "Successful", Description = "Run successfully?", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                // Phase 8: Stage A reporting, append-only and Voluntary - existing saved definitions keep working.
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "SourceMap", NickName = "SourceMap", Description = "One line per input source: which clean output face(s) it contributed to.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "LevelFrames", NickName = "LevelFrames", Description = "One line per clustered RAW level datum (elevation, tilt, cap count) - the pinned 0.15 m band. Five frames on the 9-space fixture. Empty when the model formed no frames.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));

                // P2 (docs/CONTROLLED_WORKFLOW_PLAN.md §4): level groups + per-panel clean observability, Voluntary.
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "LevelGroups", NickName = "LevelGroups", Description = "One line per level GROUP (P2): the merged storey datum caps normalize onto (elevation, the raw frames it merged + their elevations, cap count, spread, tilt). With bucketBetweenLevels = 0 this equals LevelFrames (one group per frame); at 0.21 the 9-space fixture's 5 raw frames merge into 3 groups (12.24 / 15.29 / 18.34).", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "CleanReport", NickName = "CleanReport", Description = "Per-panel clean observability (P2): the SAM_OCCT_CLEAN3D_LEVELS/_LEVELGROUP level summary plus one SAM_OCCT_CLEAN3D_PANEL line per applied clean action (opposed-collapsed / snapped-to-backer / cap-normalized / coplanar-merged / dropped-invalid), each with the moved distance, the backer, and the resolved BucketSize / Weight / MaxExtend with their provenance (stamped / derived / floor / default). Recorded at the real decision points - a null recorder is geometry-identical.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));

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

            List<Panel> panels = new List<Panel>();
            index = Params.IndexOfInputParam("_panels");
            if (index == -1 || !dataAccess.GetDataList(index, panels) || panels.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid panels");
                return;
            }

            double minBucketSize = 0.4;
            index = Params.IndexOfInputParam("bucket_");
            if (index == -1) index = Params.IndexOfInputParam("minBucketSize_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref minBucketSize);
            }

            double thicknessFactor = 0.6;
            index = Params.IndexOfInputParam("thicknessFactor_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref thicknessFactor);
            }

            double alignColinearOffset = 0.3;
            index = Params.IndexOfInputParam("alignColinearOffset_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref alignColinearOffset);
            }

            double normalizeCapOffset = 0.3;
            index = Params.IndexOfInputParam("normalizeCapOffset_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref normalizeCapOffset);
            }

            // The voluntary input is absent on old saved components AND fresh placements, so this fallback is
            // the effective GH default. Version-gated: documents saved before 0.5.0 keep the core default 0.
            double bucketBetweenLevels = SolverComponentDefaults.BucketBetweenLevelsFallback(ComponentVersion, "0.5.0");
            index = Params.IndexOfInputParam("bucketBetweenLevels_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref bucketBetweenLevels);
            }

            double slitMinGap = 0.02;
            index = Params.IndexOfInputParam("slitMinGap_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref slitMinGap);
            }

            double slitMaxGap = 0.5;
            index = Params.IndexOfInputParam("slitMaxGap_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref slitMaxGap);
            }

            double slitMaxOverlap = 2.0;
            index = Params.IndexOfInputParam("slitMaxOverlap_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref slitMaxOverlap);
            }

            double doubleWallGap = 0.0;
            index = Params.IndexOfInputParam("doubleWallGap_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref doubleWallGap);
            }

            List<Panel> cleanPanels = panels.Clean3D(out List<string> diagnostics, out Solve3DReport report, weights: null, maxExtends: null, minBucketSize: minBucketSize, thicknessFactor: thicknessFactor, alignColinearOffset: alignColinearOffset, normalizeCapOffset: normalizeCapOffset, bucketBetweenLevels: bucketBetweenLevels, doubleWallGap: doubleWallGap);

            index = Params.IndexOfOutputParam("Panels");
            if (index != -1)
            {
                dataAccess.SetDataList(index, cleanPanels?.Select(x => new GooPanel(x)));
            }

            // Remaining double-wall/slit diagnostics on the clean panels (same detector as AutoTuneSolver).
            // Only surface slits the bucket did NOT close: parallel panels whose perpendicular gap is OUTSIDE
            // the capture slab (gap > bucket width). Pairs within the bucket are snapped/merged here, so
            // reporting them is noise - floor the slit gap at the bucket capture width.
            double slitGapFloor = System.Math.Max(slitMinGap, BucketCaptureWidth(cleanPanels, minBucketSize));
            List<Segment3D> slits = cleanPanels.Slits(out List<Panel> slitPanels, null, 0.2, slitGapFloor, slitMaxGap, slitMaxOverlap);

            index = Params.IndexOfOutputParam("Slits");
            if (index != -1)
            {
                dataAccess.SetDataList(index, slits?.Where(x => x != null).Select(x => new GooSAMGeometry(x)));
            }

            index = Params.IndexOfOutputParam("SlitPanels");
            if (index != -1)
            {
                dataAccess.SetDataList(index, slitPanels?.Where(x => x != null).Select(x => new GooPanel(x)));
            }

            index = Params.IndexOfOutputParam("Diagnostics");
            if (index != -1)
            {
                dataAccess.SetDataList(index, diagnostics);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, cleanPanels != null && cleanPanels.Count != 0);
            }

            index = Params.IndexOfOutputParam("SourceMap");
            if (index != -1)
            {
                dataAccess.SetDataList(index, report?.FormatSourceMap());
            }

            index = Params.IndexOfOutputParam("LevelFrames");
            if (index != -1)
            {
                dataAccess.SetDataList(index, report == null ? null : SolverReportFormat.FormatLevelFrames(report.LevelFrames));
            }

            index = Params.IndexOfOutputParam("LevelGroups");
            if (index != -1)
            {
                dataAccess.SetDataList(index, report?.FormatLevelGroups());
            }

            index = Params.IndexOfOutputParam("CleanReport");
            if (index != -1)
            {
                dataAccess.SetDataList(index, report?.FormatCleanReport());
            }
        }

        /// <summary>
        /// The bucket capture width to use as the slit-gap floor: the largest <c>SolverParameter.BucketSize</c>
        /// stamped on the panels, so a parallel pair within ANY panel's capture slab (which the clean bucket
        /// snaps/merges) is not reported as a slit. Only pairs whose gap exceeds the bucket - the ones the
        /// bucket did not capture - are surfaced. Falls back to <paramref name="minBucketSize"/>.
        /// </summary>
        private static double BucketCaptureWidth(IEnumerable<Panel> panels, double minBucketSize)
        {
            double result = minBucketSize;
            foreach (Panel panel in panels ?? Enumerable.Empty<Panel>())
            {
                if (panel != null && panel.TryGetValue(SolverParameter.BucketSize, out double bucketSize) && !double.IsNaN(bucketSize) && bucketSize > result)
                {
                    result = bucketSize;
                }
            }

            return result;
        }
    }
}
