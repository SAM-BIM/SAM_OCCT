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

                global::Grasshopper.Kernel.Parameters.Param_Number minBucketSolve = new global::Grasshopper.Kernel.Parameters.Param_Number()
                { Name = "_bucket", NickName = "_bucket", Description = "Wall merge capture half-width → Extend3D minBucketSize_. Default 0.4.", Access = GH_ParamAccess.item };
                minBucketSolve.SetPersistentData(0.4);
                result.Add(new GH_SAMParam(minBucketSolve, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number alignColinear = new global::Grasshopper.Kernel.Parameters.Param_Number()
                { Name = "_align", NickName = "_align", Description = "Colinear wall abut merge distance → Extend3D alignColinearOffset_. Default 0.3.", Access = GH_ParamAccess.item };
                alignColinear.SetPersistentData(0.3);
                result.Add(new GH_SAMParam(alignColinear, ParamVisibility.Binding));

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

                global::Grasshopper.Kernel.Parameters.Param_Number sweepBuckets = new global::Grasshopper.Kernel.Parameters.Param_Number()
                { Name = "sweepBuckets_", NickName = "sweepBuckets_", Description = "Min bucket values to try (list). Controls wall merge distance. Default: 0.4, 0.5, 0.6, 0.7. Larger = more wall merging.", Access = GH_ParamAccess.list };
                sweepBuckets.SetPersistentData(0.4, 0.5, 0.6, 0.7);
                result.Add(new GH_SAMParam(sweepBuckets, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number sweepAligns = new global::Grasshopper.Kernel.Parameters.Param_Number()
                { Name = "sweepAligns_", NickName = "sweepAligns_", Description = "Colinear align values to try (list). Controls colinear wall abut merge distance. Default: 0.1, 0.2, 0.3, 0.4, 0.5. Smaller = stricter.", Access = GH_ParamAccess.list };
                sweepAligns.SetPersistentData(0.1, 0.2, 0.3, 0.4, 0.5);
                result.Add(new GH_SAMParam(sweepAligns, ParamVisibility.Voluntary));

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
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "Panels", NickName = "Panels", Description = "Solved panels (use directly, or wire Band/Fill/Bucket/Align/DirGrow to Extend3D).", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Point() { Name = "NakedPoints", NickName = "NakedPoints", Description = "Naked (open) edge locations after solve.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Report", NickName = "Report", Description = "Full diagnostics + sweep results (when _discover=true).", Access = GH_ParamAccess.list }, ParamVisibility.Binding));

                // Discovery outputs — wire these to SAMOCCT.Extend3D (Binding so visible immediately)
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "Band", NickName = "Band", Description = "Optimal level merge band → wire to SAMOCCT.Extend3D bucketBetweenLevels_.", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "Fill", NickName = "Fill", Description = "Optimal cap growth reach → wire to SAMOCCT.Extend3D fillMargin_.", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "Bucket", NickName = "Bucket", Description = "Optimal wall merge distance → wire to SAMOCCT.Extend3D minBucketSize_.", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "Align", NickName = "Align", Description = "Optimal colinear align distance → wire to SAMOCCT.Extend3D alignColinearOffset_.", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "DirGrow", NickName = "DirGrow", Description = "Optimal cap growth mode → wire to SAMOCCT.Extend3D directionalCapGrow_.", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Summary", NickName = "Summary", Description = "One-line summary: 'band=X fill=Y bucket=Z align=A dir=W'.", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Diagnostics", NickName = "Diagnostics", Description = "Diagnostics", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "Successful", NickName = "Successful", Description = "Run successfully?", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                return result.ToArray();
            }
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            int index_ok = Params.IndexOfOutputParam("Successful");
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

            double bucket = 0.4;
            index = Params.IndexOfInputParam("_bucket");
            if (index != -1) dataAccess.GetData(index, ref bucket);

            double align = 0.3;
            index = Params.IndexOfInputParam("_align");
            if (index != -1) dataAccess.GetData(index, ref align);

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

            List<double> userBuckets = null;
            index = Params.IndexOfInputParam("sweepBuckets_");
            if (index != -1) { var list = new List<double>(); if (dataAccess.GetDataList(index, list) && list.Count > 0) userBuckets = list; }

            List<double> userAligns = null;
            index = Params.IndexOfInputParam("sweepAligns_");
            if (index != -1) { var list = new List<double>(); if (dataAccess.GetDataList(index, list) && list.Count > 0) userAligns = list; }

            // --- Advanced ---
            double minBucket = 0.4;
            index = Params.IndexOfInputParam("minBucket_");
            if (index != -1) dataAccess.GetData(index, ref minBucket);

            if (bucket == 0.4 && minBucket != 0.4) bucket = minBucket; // fallback: minBucket_ when _bucket not wired

            AutoTune3DOptions tune = new AutoTune3DOptions();
            index = Params.IndexOfInputParam("maxRounds_");
            if (index != -1) { int val = tune.MaxRounds; if (dataAccess.GetData(index, ref val)) tune.MaxRounds = val; }

            // --- Discover or use supplied params ---
            List<string> report = new List<string>();
            if (discover)
            {
                // Sweep using the actual Extend3D → CreateAdjacencyCluster chain
                // (not the internal solver shortcut) so the discovered params match
                // what the user gets in their GH workflow.
                double[] bands = userBands?.ToArray();
                if (bands == null || bands.Length == 0) bands = new[] { 0.15, 0.21, 0.3, 0.4, 0.5 };
                double[] margins = userMargins?.ToArray();
                if (margins == null || margins.Length == 0) margins = new[] { 0.3, 0.5, 0.7, 1.0 };
                double[] buckets = userBuckets?.ToArray();
                if (buckets == null || buckets.Length == 0) buckets = new[] { 0.4, 0.5, 0.6, 0.7 };
                double[] aligns = userAligns?.ToArray();
                if (aligns == null || aligns.Length == 0) aligns = new[] { 0.3 };  // single default - not dimension-exploded
                bool[] dirs = { true, false };

                report.Add(string.Format("Sweep: {0} bands x {1} margins x {2} buckets x {3} aligns x 2 dirs = {4} combinations",
                    bands.Length, margins.Length, buckets.Length, aligns.Length, bands.Length * margins.Length * buckets.Length * aligns.Length * 2));

                int bestCells = -1;
                double bestBand = 0, bestFill = 0.5, bestBucket = 0.4, bestAlign = 0.3;
                bool bestDir = true;
                var buildOpts = new SAM.Core.OCCT.OcctBuildOptions
                { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };

                foreach (double b in bands)
                {
                    foreach (double m in margins)
                    {
                        foreach (double bk in buckets)
                        {
                            foreach (double al in aligns)
                            {
                                foreach (bool d in dirs)
                                {
                                    List<Panel> ext = panels.Extend3D(out _, out _,
                                        minBucketSize: bk, alignColinearOffset: al,
                                        bucketBetweenLevels: b, fillMargin: m, directionalCapGrow: d);
                                    var nonAir = (ext ?? new List<Panel>())
                                        .Where(x => x?.GetFace3D() != null).ToList();

                                    var cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                                        null, nonAir, out SAM.Geometry.OCCT.OcctCellComplexResult cr, new SAM.Core.Log(), buildOpts);
                                    int cells = cr?.Cells?.Count ?? 0;
                                    int spaces = cluster?.GetSpaces()?.Count ?? 0;
                                    cr?.Dispose();

                                    report.Add(string.Format("  band={0:0.###} fill={1:0.###} bucket={2:0.###} align={3:0.###} dir={4} → cells={5} spaces={6}",
                                        b, m, bk, al, d, cells, spaces));

                                    if (cells > bestCells || (cells == bestCells && spaces > (bestCells > 0 ? spaces : 0)))
                                    {
                                        bestCells = cells;
                                        bestBand = b; bestFill = m; bestBucket = bk; bestAlign = al; bestDir = d;
                                    }
                                }
                            }
                        }
                    }
                }

                band = bestBand; fill = bestFill; bucket = bestBucket; align = bestAlign; dirGrow = bestDir;
                report.Add(string.Format("BEST: band={0:0.###} fill={1:0.###} bucket={2:0.###} align={3:0.###} dir={4} → {5} cells",
                    band, fill, bucket, align, dirGrow, bestCells));
            }

            // --- Solve ---
            List<Panel> resolved = panels.AutoTune3D(
                out List<Point3D> nakedPoints, out List<string> diags, out _, out Solve3DReport solveReport,
                minBucketSize: bucket, tune: tune,
                alignColinearOffset: align,
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

            index = Params.IndexOfOutputParam("Bucket");
            if (index != -1) dataAccess.SetData(index, bucket);

            index = Params.IndexOfOutputParam("Align");
            if (index != -1) dataAccess.SetData(index, align);

            index = Params.IndexOfOutputParam("DirGrow");
            if (index != -1) dataAccess.SetData(index, dirGrow);

            index = Params.IndexOfOutputParam("Summary");
            if (index != -1) dataAccess.SetData(index, string.Format("band={0:0.###} fill={1:0.###} bucket={2:0.###} align={3:0.###} dir={4}", band, fill, bucket, align, dirGrow));

            index = Params.IndexOfOutputParam("Diagnostics");
            if (index != -1) dataAccess.SetDataList(index, report);

            if (index_ok != -1) dataAccess.SetData(index_ok, resolved != null && resolved.Count != 0);
        }
    }
}
