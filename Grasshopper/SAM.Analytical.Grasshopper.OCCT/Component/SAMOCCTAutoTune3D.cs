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
    /// Auto-tunes solver parameters by sweeping combinations and scoring closure quality.
    /// <para>
    /// Without discovery (discover_=false): runs the solver with your supplied parameters —
    /// useful when you already know good values. Wire the OptimalBand/Fill/DirCap outputs
    /// to SAMOCCT.Extend3D for subsequent runs.
    /// </para>
    /// <para>
    /// With discovery (discover_=true): sweeps all combinations of sweepBands × sweepMargins
    /// × directionalCapGrow and returns the best. The sweep runs the Extend3D→CreateAdjacencyCluster
    /// pipeline internally so the discovered values work directly in your GH chain.
    /// </para>
    /// </summary>
    public class SAMOCCTAutoTune3D : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("9de8b4c0-14f6-4828-b966-aa57cf58143b");
        public override string LatestComponentVersion => "0.3.0";
        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTAutoTune3D()
          : base("SAMOCCT.AutoTune3D", "SAMOCCT.AutoTune3D",
                "Auto-discovers optimal solve parameters by sweeping settings and scoring results. Wire OptimalBand/OptimalFill/OptimalDirCap to SAMOCCT.Extend3D for your production chain.",
                "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                // === REQUIRED ===
                GooPanelParam panels = new GooPanelParam() { Name = "_panels", NickName = "_panels", Description = "Input panels. Feed the ORIGINAL model panels.", Access = GH_ParamAccess.list };
                panels.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(panels, ParamVisibility.Binding));

                // === SOLVE PARAMETERS (used when discover_=false) ===
                global::Grasshopper.Kernel.Parameters.Param_Number bucketBetweenLevels = new global::Grasshopper.Kernel.Parameters.Param_Number()
                { Name = "_band", NickName = "_band", Description = "Level-group merge band (m). 0 = solver default. Controls how cap elevations merge into building storeys. Typical range: 0.15-0.5.", Access = GH_ParamAccess.item };
                bucketBetweenLevels.SetPersistentData(0.0);
                result.Add(new GH_SAMParam(bucketBetweenLevels, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number fillMargin = new global::Grasshopper.Kernel.Parameters.Param_Number()
                { Name = "_fill", NickName = "_fill", Description = "Cap growth distance (m). How far floors/roofs grow outward to meet walls. Typical range: 0.3-1.0.", Access = GH_ParamAccess.item };
                fillMargin.SetPersistentData(0.5);
                result.Add(new GH_SAMParam(fillMargin, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Boolean directionalCapGrow = new global::Grasshopper.Kernel.Parameters.Param_Boolean()
                { Name = "_dirGrow", NickName = "_dirGrow", Description = "Cap growth mode. True = per-edge (safer, gaps may remain). False = uniform (closes all gaps, may overshoot).", Access = GH_ParamAccess.item };
                directionalCapGrow.SetPersistentData(true);
                result.Add(new GH_SAMParam(directionalCapGrow, ParamVisibility.Voluntary));

                // === DISCOVERY ===
                global::Grasshopper.Kernel.Parameters.Param_Boolean discover = new global::Grasshopper.Kernel.Parameters.Param_Boolean()
                { Name = "_discover", NickName = "_discover", Description = "Run parameter sweep? True = sweep all combinations and use the best. False = use _band/_fill/_dirGrow directly.", Access = GH_ParamAccess.item };
                discover.SetPersistentData(true);
                result.Add(new GH_SAMParam(discover, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number sweepBands = new global::Grasshopper.Kernel.Parameters.Param_Number()
                { Name = "sweepBands_", NickName = "sweepBands_", Description = "Band values to try (list). Default: 0.15, 0.21, 0.3, 0.4, 0.5. Enter your own list to test specific values.", Access = GH_ParamAccess.list };
                sweepBands.SetPersistentData(0.15, 0.21, 0.3, 0.4, 0.5);
                result.Add(new GH_SAMParam(sweepBands, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number sweepMargins = new global::Grasshopper.Kernel.Parameters.Param_Number()
                { Name = "sweepMargins_", NickName = "sweepMargins_", Description = "Fill margin values to try (list). Default: 0.3, 0.5, 0.7, 1.0. Enter your own list to test specific values.", Access = GH_ParamAccess.list };
                sweepMargins.SetPersistentData(0.3, 0.5, 0.7, 1.0);
                result.Add(new GH_SAMParam(sweepMargins, ParamVisibility.Voluntary));

                // === ADVANCED (Stage A, only relevant when using the Solve3D result directly) ===
                global::Grasshopper.Kernel.Parameters.Param_Number minBucketSize = new global::Grasshopper.Kernel.Parameters.Param_Number()
                { Name = "minBucket_", NickName = "minBucket_", Description = "ADVANCED: Capture half-width for Stage A clean (m). Default 0.4.", Access = GH_ParamAccess.item };
                minBucketSize.SetPersistentData(0.4);
                result.Add(new GH_SAMParam(minBucketSize, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Integer maxRounds = new global::Grasshopper.Kernel.Parameters.Param_Integer()
                { Name = "maxRounds_", NickName = "maxRounds_", Description = "ADVANCED: Max AutoTune escalation rounds. Default 3.", Access = GH_ParamAccess.item };
                maxRounds.SetPersistentData(3);
                result.Add(new GH_SAMParam(maxRounds, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean run = new global::Grasshopper.Kernel.Parameters.Param_Boolean()
                { Name = "_run", NickName = "_run", Description = "Trigger solve.", Access = GH_ParamAccess.item };
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
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "Panels", NickName = "Panels", Description = "Solved panels (use directly, or wire OptimalBand/Fill/DirCap to Extend3D).", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Point() { Name = "NakedPoints", NickName = "NakedPoints", Description = "Naked (open) edge locations after solve.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Report", NickName = "Report", Description = "Full diagnostics + sweep results (when discover_=true).", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "OK", NickName = "OK", Description = "True if solve produced panels.", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                // Discovery outputs — wire these to SAMOCCT.Extend3D
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "Band", NickName = "Band", Description = "Optimal level merge band → wire to SAMOCCT.Extend3D bucketBetweenLevels_.", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "Fill", NickName = "Fill", Description = "Optimal cap growth reach → wire to SAMOCCT.Extend3D fillMargin_.", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "DirGrow", NickName = "DirGrow", Description = "Optimal cap growth mode → wire to SAMOCCT.Extend3D directionalCapGrow_.", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Summary", NickName = "Summary", Description = "One-line summary: 'band=X fill=Y dir=Z -> cells=N'.", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));

                return result.ToArray();
            }
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            int index_ok = Params.IndexOfOutputParam("OK");
            if (index_ok != -1) dataAccess.SetData(index_ok, false);

            int index;
            bool run = false;
            index = Params.IndexOfInputParam("_run");
            if (index == -1 || !dataAccess.GetData(index, ref run) || !run) return;

            List<Panel> panels = new List<Panel>();
            index = Params.IndexOfInputParam("_panels");
            if (index == -1 || !dataAccess.GetDataList(index, panels) || panels.Count == 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No panels supplied"); return; }

            // --- Solve parameters ---
            double band = 0.0;
            index = Params.IndexOfInputParam("_band");
            if (index != -1) dataAccess.GetData(index, ref band);

            double fill = 0.5;
            index = Params.IndexOfInputParam("_fill");
            if (index != -1) dataAccess.GetData(index, ref fill);

            bool dirGrow = true;
            index = Params.IndexOfInputParam("_dirGrow");
            if (index != -1) dataAccess.GetData(index, ref dirGrow);

            // --- Discovery ---
            bool discover = true;
            index = Params.IndexOfInputParam("_discover");
            if (index != -1) dataAccess.GetData(index, ref discover);

            List<double> userBands = null;
            index = Params.IndexOfInputParam("sweepBands_");
            if (index != -1) { var list = new List<double>(); if (dataAccess.GetDataList(index, list) && list.Count > 0) userBands = list; }

            List<double> userMargins = null;
            index = Params.IndexOfInputParam("sweepMargins_");
            if (index != -1) { var list = new List<double>(); if (dataAccess.GetDataList(index, list) && list.Count > 0) userMargins = list; }

            // --- Advanced ---
            double minBucket = 0.4;
            index = Params.IndexOfInputParam("minBucket_");
            if (index != -1) dataAccess.GetData(index, ref minBucket);

            AutoTune3DOptions tune = new AutoTune3DOptions();
            index = Params.IndexOfInputParam("maxRounds_");
            if (index != -1) { int val = tune.MaxRounds; if (dataAccess.GetData(index, ref val)) tune.MaxRounds = val; }

            // --- Discover or use supplied params ---
            List<string> report = new List<string>();
            if (discover)
            {
                var faces = panels.Select(p => p.GetFace3D()).Where(f => f != null).ToList();
                var sweep = new ParameterDiscoverySolver(faces)
                {
                    Mode = ParameterDiscoverySolver.WorkflowMode.Extend3D
                };

                // Use user-supplied sweep ranges, or defaults.
                double[] bands = userBands?.ToArray();
                double[] margins = userMargins?.ToArray();
                sweep.Execute(
                    new SAM.Core.OCCT.OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 },
                    sweepBands: bands, sweepMargins: margins);

                report = new List<string>(sweep.Diagnostics);

                if (sweep.BestTrial != null)
                {
                    band = sweep.BestTrial.BucketBetweenLevels;
                    fill = sweep.BestTrial.FillMargin;
                    dirGrow = sweep.BestTrial.DirectionalCapGrow;
                    report.Add(string.Format("USING: band={0:0.###} fill={1:0.###} dir={2} cells={3}",
                        band, fill, dirGrow, sweep.BestTrial.CellCount));
                }
            }

            // --- Solve ---
            List<Panel> resolved = panels.AutoTune3D(
                out List<Point3D> nakedPoints, out List<string> diags, out _, out Solve3DReport solveReport,
                minBucketSize: minBucket, tune: tune,
                bucketBetweenLevels: band, fillMargin: fill, directionalCapGrow: dirGrow);

            report.AddRange(diags);

            // --- Output ---
            index = Params.IndexOfOutputParam("Panels");
            if (index != -1) dataAccess.SetDataList(index, resolved?.Select(x => new GooPanel(x)));

            index = Params.IndexOfOutputParam("NakedPoints");
            if (index != -1) dataAccess.SetDataList(index, nakedPoints?.Where(x => x != null).Select(x => new global::Rhino.Geometry.Point3d(x.X, x.Y, x.Z)));

            index = Params.IndexOfOutputParam("Report");
            if (index != -1) dataAccess.SetDataList(index, report);

            if (index_ok != -1) dataAccess.SetData(index_ok, resolved != null && resolved.Count != 0);

            index = Params.IndexOfOutputParam("Band");
            if (index != -1) dataAccess.SetData(index, band);

            index = Params.IndexOfOutputParam("Fill");
            if (index != -1) dataAccess.SetData(index, fill);

            index = Params.IndexOfOutputParam("DirGrow");
            if (index != -1) dataAccess.SetData(index, dirGrow);

            index = Params.IndexOfOutputParam("Summary");
            if (index != -1) dataAccess.SetData(index, string.Format("band={0:0.###} fill={1:0.###} dir={2}", band, fill, dirGrow));
        }
    }
}
