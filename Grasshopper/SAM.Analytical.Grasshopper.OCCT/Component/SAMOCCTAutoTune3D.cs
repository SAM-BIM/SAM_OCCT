// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core.Grasshopper;
using SAM.Geometry.Grasshopper;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.Grasshopper.OCCT
{
    /// <summary>
    /// Diagnosis-driven closure (Phase 5e) with parameter discovery: runs the raw-first 3D panel solver,
    /// and - only when naked edges remain or closure required fabricated gap-fill patches - a bounded
    /// escalation of wall MaxExtend reach. Optionally discovers optimal bucketBetweenLevels, fillMargin,
    /// and directionalCapGrow by sweeping plausible ranges and reporting the best combination.
    /// </summary>
    public class SAMOCCTAutoTune3D : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("9de8b4c0-14f6-4828-b966-aa57cf58143b");

        public override string LatestComponentVersion => "0.2.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTAutoTune3D()
          : base("SAMOCCT.AutoTune3D", "SAMOCCT.AutoTune3D",
                "Diagnosis-driven closure with parameter discovery. Runs the raw-first solver, escalates MaxExtend on culprit walls, and optionally discovers optimal bucketBetweenLevels/fillMargin/directionalCapGrow by sweeping plausible ranges. Reports the best configuration found.",
                "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                GooPanelParam panels = new GooPanelParam() { Name = "_panels", NickName = "_panels", Description = "SAM Analytical Panels to solve.", Access = GH_ParamAccess.list };
                panels.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(panels, ParamVisibility.Binding));

                // --- Conditioning parameters ---
                global::Grasshopper.Kernel.Parameters.Param_Number bucketBetweenLevels = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "bucketBetweenLevels_", NickName = "bucketBetweenLevels_", Description = "Level-group merge band (m). 0 = auto (default). Larger = fewer level groups. Optimal value depends on the model.", Access = GH_ParamAccess.item };
                bucketBetweenLevels.SetPersistentData(0.0);
                result.Add(new GH_SAMParam(bucketBetweenLevels, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number fillMargin = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "fillMargin_", NickName = "fillMargin_", Description = "Cap growth reach (m). How far floors/roofs grow to meet walls. Default 0.5.", Access = GH_ParamAccess.item };
                fillMargin.SetPersistentData(0.5);
                result.Add(new GH_SAMParam(fillMargin, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean directionalCapGrow = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "directionalCapGrow_", NickName = "directionalCapGrow_", Description = "Directional (true) vs uniform (false) cap growth. True = per-edge evidence-based. False = uniform offset. Default true.", Access = GH_ParamAccess.item };
                directionalCapGrow.SetPersistentData(true);
                result.Add(new GH_SAMParam(directionalCapGrow, ParamVisibility.Voluntary));

                // --- Parameter discovery ---
                global::Grasshopper.Kernel.Parameters.Param_Boolean discover = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "discoverParameters_", NickName = "discoverParameters_", Description = "When true, sweeps bucketBetweenLevels x fillMargin x directionalCapGrow combinations and reports the best configuration on the BestConfig output. The solve still runs with the final best parameters.", Access = GH_ParamAccess.item };
                discover.SetPersistentData(false);
                result.Add(new GH_SAMParam(discover, ParamVisibility.Voluntary));

                // --- Standard solver knobs ---
                global::Grasshopper.Kernel.Parameters.Param_Number minBucketSize = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "minBucketSize_", NickName = "minBucketSize_", Description = "Capture half-width (m). Default 0.4.", Access = GH_ParamAccess.item };
                minBucketSize.SetPersistentData(0.4);
                result.Add(new GH_SAMParam(minBucketSize, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number thicknessFactor = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "thicknessFactor_", NickName = "thicknessFactor_", Description = "Construction thickness fraction. Default 0.6.", Access = GH_ParamAccess.item };
                thicknessFactor.SetPersistentData(0.6);
                result.Add(new GH_SAMParam(thicknessFactor, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number alignColinearOffset = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "alignColinearOffset_", NickName = "alignColinearOffset_", Description = "Colinear wall alignment offset (m). Default 0.3.", Access = GH_ParamAccess.item };
                alignColinearOffset.SetPersistentData(0.3);
                result.Add(new GH_SAMParam(alignColinearOffset, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number normalizeCapOffset = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "normalizeCapOffset_", NickName = "normalizeCapOffset_", Description = "Cap plane normalization offset (m). Default 0.3.", Access = GH_ParamAccess.item };
                normalizeCapOffset.SetPersistentData(0.3);
                result.Add(new GH_SAMParam(normalizeCapOffset, ParamVisibility.Voluntary));

                // --- AutoTune escalation knobs ---
                global::Grasshopper.Kernel.Parameters.Param_Integer maxRounds = new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "maxRounds_", NickName = "maxRounds_", Description = "Max escalation rounds. Default 3.", Access = GH_ParamAccess.item };
                maxRounds.SetPersistentData(3);
                result.Add(new GH_SAMParam(maxRounds, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number maxExtendLadder = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "maxExtendLadder_", NickName = "maxExtendLadder_", Description = "MaxExtend targets (m), ascending. Default [0.5, 0.75, 1.0, 1.5].", Access = GH_ParamAccess.list };
                maxExtendLadder.SetPersistentData(0.5, 0.75, 1.0, 1.5);
                result.Add(new GH_SAMParam(maxExtendLadder, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean escalateBucket = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "escalateBucket_", NickName = "escalateBucket_", Description = "Also grow BucketSize. Default OFF.", Access = GH_ParamAccess.item };
                escalateBucket.SetPersistentData(false);
                result.Add(new GH_SAMParam(escalateBucket, ParamVisibility.Voluntary));

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
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "Panels", NickName = "Panels", Description = "Resolved panels.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Point() { Name = "NakedPoints", NickName = "NakedPoints", Description = "Naked edge locations.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Diagnostics", NickName = "Diagnostics", Description = "Diagnostics", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "Successful", NickName = "Successful", Description = "Run successful?", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "BestConfig", NickName = "BestConfig", Description = "Best parameter configuration found (when discoverParameters_=true). Format: 'band=X fill=Y dir=Z -> cells=N naked=M'.", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "OptimalBand", NickName = "OptimalBand", Description = "Discovered optimal bucketBetweenLevels (wire to SAMOCCT.Extend3D bucketBetweenLevels_).", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "OptimalFill", NickName = "OptimalFill", Description = "Discovered optimal fillMargin (wire to SAMOCCT.Extend3D fillMargin_).", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "OptimalDirCap", NickName = "OptimalDirCap", Description = "Discovered optimal directionalCapGrow (wire to SAMOCCT.Extend3D directionalCapGrow_).", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "NakedWires", NickName = "NakedWires", Description = "Naked boundary loops.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "SourceMap", NickName = "SourceMap", Description = "Source-to-output mapping.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "ClosureReport", NickName = "ClosureReport", Description = "Closure summary.", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "Rounds", NickName = "Rounds", Description = "Rounds attempted.", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "RoundsAccepted", NickName = "RoundsAccepted", Description = "Rounds accepted.", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "DiscoveryReport", NickName = "DiscoveryReport", Description = "Full parameter sweep report (when discoverParameters_=true).", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));

                return result.ToArray();
            }
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            int index_Successful = Params.IndexOfOutputParam("Successful");
            if (index_Successful != -1) dataAccess.SetData(index_Successful, false);

            int index;
            bool run = false;
            index = Params.IndexOfInputParam("_run");
            if (index == -1 || !dataAccess.GetData(index, ref run) || !run) return;

            List<Panel> panels = new List<Panel>();
            index = Params.IndexOfInputParam("_panels");
            if (index == -1 || !dataAccess.GetDataList(index, panels) || panels.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid panels"); return; }

            // --- Conditioning parameters ---
            double bucketBetweenLevels = 0.0;
            index = Params.IndexOfInputParam("bucketBetweenLevels_");
            if (index != -1) dataAccess.GetData(index, ref bucketBetweenLevels);

            double fillMargin = 0.5;
            index = Params.IndexOfInputParam("fillMargin_");
            if (index != -1) dataAccess.GetData(index, ref fillMargin);

            bool directionalCapGrow = true;
            index = Params.IndexOfInputParam("directionalCapGrow_");
            if (index != -1) dataAccess.GetData(index, ref directionalCapGrow);

            // --- Discover mode ---
            bool discover = false;
            index = Params.IndexOfInputParam("discoverParameters_");
            if (index != -1) dataAccess.GetData(index, ref discover);

            // --- Standard knobs ---
            double minBucketSize = 0.4;
            index = Params.IndexOfInputParam("minBucketSize_");
            if (index != -1) dataAccess.GetData(index, ref minBucketSize);

            double thicknessFactor = 0.6;
            index = Params.IndexOfInputParam("thicknessFactor_");
            if (index != -1) dataAccess.GetData(index, ref thicknessFactor);

            double alignColinearOffset = 0.3;
            index = Params.IndexOfInputParam("alignColinearOffset_");
            if (index != -1) dataAccess.GetData(index, ref alignColinearOffset);

            double normalizeCapOffset = 0.3;
            index = Params.IndexOfInputParam("normalizeCapOffset_");
            if (index != -1) dataAccess.GetData(index, ref normalizeCapOffset);

            // --- AutoTune escalation ---
            AutoTune3DOptions tune = new AutoTune3DOptions();

            index = Params.IndexOfInputParam("maxRounds_");
            if (index != -1) { int val = tune.MaxRounds; if (dataAccess.GetData(index, ref val)) tune.MaxRounds = val; }

            index = Params.IndexOfInputParam("maxExtendLadder_");
            if (index != -1) { List<double> ladder = new List<double>(); if (dataAccess.GetDataList(index, ladder) && ladder.Count != 0) tune.MaxExtendLadder = ladder; }

            index = Params.IndexOfInputParam("escalateBucket_");
            if (index != -1) { bool val = tune.EscalateBucket; if (dataAccess.GetData(index, ref val)) tune.EscalateBucket = val; }

            // --- Run parameter discovery if requested ---
            List<string> discoveryReport = new List<string>();
            if (discover)
            {
                var faces = panels.Select(p => p.GetFace3D()).Where(f => f != null).ToList();
                var paramSolver = new ParameterDiscoverySolver(faces);
                paramSolver.Execute(
                    new SAM.Core.OCCT.OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 },
                    sweepBands: new[] { 0.15, 0.21, 0.3, 0.4, 0.5 },
                    sweepMargins: new[] { 0.3, 0.5, 0.7, 1.0 });

                discoveryReport = new List<string>(paramSolver.Diagnostics);
                if (paramSolver.BestTrial != null)
                {
                    // Override user-supplied params with discovered best.
                    bucketBetweenLevels = paramSolver.BestTrial.BucketBetweenLevels;
                    fillMargin = paramSolver.BestTrial.FillMargin;
                    directionalCapGrow = paramSolver.BestTrial.DirectionalCapGrow;
                    discoveryReport.Add(string.Format("ADOPTED: band={0:0.###} fill={1:0.###} dir={2}",
                        bucketBetweenLevels, fillMargin, directionalCapGrow));
                }
            }

            // --- Run AutoTune ---
            List<Panel> resolvedPanels = panels.AutoTune3D(
                out List<Point3D> nakedPoint3Ds, out List<string> diagnostics, out _, out Solve3DReport report,
                weights: null, maxExtends: null,
                minBucketSize: minBucketSize, thicknessFactor: thicknessFactor,
                alignColinearOffset: alignColinearOffset, normalizeCapOffset: normalizeCapOffset,
                options: null, tune: tune,
                bucketBetweenLevels: bucketBetweenLevels, fillMargin: fillMargin, directionalCapGrow: directionalCapGrow);

            // --- Output ---
            index = Params.IndexOfOutputParam("Panels");
            if (index != -1) dataAccess.SetDataList(index, resolvedPanels?.Select(x => new GooPanel(x)));

            index = Params.IndexOfOutputParam("NakedPoints");
            if (index != -1) dataAccess.SetDataList(index, nakedPoint3Ds?.Where(x => x != null).Select(x => new global::Rhino.Geometry.Point3d(x.X, x.Y, x.Z)));

            index = Params.IndexOfOutputParam("Diagnostics");
            if (index != -1) dataAccess.SetDataList(index, diagnostics);

            if (index_Successful != -1) dataAccess.SetData(index_Successful, resolvedPanels != null && resolvedPanels.Count != 0);

            index = Params.IndexOfOutputParam("BestConfig");
            if (index != -1 && discover)
                dataAccess.SetData(index, discoveryReport.FirstOrDefault(x => x.StartsWith("Best:") || x.StartsWith("ADOPTED:")) ?? "");

            // Numeric outputs for direct wiring to SAMOCCT.Extend3D.
            index = Params.IndexOfOutputParam("OptimalBand");
            if (index != -1) dataAccess.SetData(index, bucketBetweenLevels);

            index = Params.IndexOfOutputParam("OptimalFill");
            if (index != -1) dataAccess.SetData(index, fillMargin);

            index = Params.IndexOfOutputParam("OptimalDirCap");
            if (index != -1) dataAccess.SetData(index, directionalCapGrow);

            index = Params.IndexOfOutputParam("NakedWires");
            if (index != -1)
                dataAccess.SetDataList(index, report?.NakedWires?.Where(x => x != null && x.Point3Ds.Count >= 2).Select(x => new Polyline3D(x.Point3Ds, x.IsClosed)).Select(x => new GooSAMGeometry(x)));

            index = Params.IndexOfOutputParam("SourceMap");
            if (index != -1) dataAccess.SetDataList(index, report?.FormatSourceMap());

            index = Params.IndexOfOutputParam("ClosureReport");
            if (index != -1) dataAccess.SetData(index, report?.ClosureReportText);

            index = Params.IndexOfOutputParam("Rounds");
            if (index != -1) dataAccess.SetData(index, report?.Rounds ?? 0);

            index = Params.IndexOfOutputParam("RoundsAccepted");
            if (index != -1) dataAccess.SetData(index, report?.RoundsAccepted ?? 0);

            index = Params.IndexOfOutputParam("DiscoveryReport");
            if (index != -1 && discover) dataAccess.SetDataList(index, discoveryReport);
        }
    }
}
