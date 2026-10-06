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
    /// Diagnosis-driven closure (Phase 5e): runs the raw-first 3D panel solver, and - only when naked
    /// (free) boundary edges remain or closure required fabricated gap-fill patches - a bounded escalation
    /// of the implicated walls' MaxExtend reach, re-solving through the managed pipeline and accepting a
    /// round only when the closure signature does not regress. When the baseline is already watertight
    /// this behaves exactly like SAMOCCT.Solve3D.
    /// </summary>
    public class SAMOCCTAutoTune3DLegacy : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("9de8b4c0-14f6-4828-b966-aa57cf58143b");

        public override string LatestComponentVersion => "0.1.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTAutoTune3DLegacy()
          : base("SAMOCCT.AutoTune3D", "SAMOCCT.AutoTune3D", "Diagnosis-driven closure: runs the raw-first 3D panel solver, then - only when naked edges remain or closure needed a fabricated gap-fill patch - bounded rounds of measured-to-target wall extension on the implicated panels only, accepted only when the closure signature does not regress. Behaves exactly like SAMOCCT.Solve3D on an already-watertight model.", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                GooPanelParam panels = new GooPanelParam() { Name = "_panels", NickName = "_panels", Description = "SAM Analytical Panels to solve. Air panels are ignored. Feed the ORIGINAL panels - AutoTune3D runs clean + extend + resolve (and escalation rounds) itself.", Access = GH_ParamAccess.list };
                panels.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(panels, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number minBucketSize = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "minBucketSize_", NickName = "minBucketSize_", Description = "Capture half-width (m). Parallel panels within this band snap onto one backer in Step 1. Larger = more merging.", Access = GH_ParamAccess.item };
                minBucketSize.SetPersistentData(0.4);
                result.Add(new GH_SAMParam(minBucketSize, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number thicknessFactor = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "thicknessFactor_", NickName = "thicknessFactor_", Description = "Fraction of construction thickness used as the capture half-width (floored at minBucketSize).", Access = GH_ParamAccess.item };
                thicknessFactor.SetPersistentData(0.6);
                result.Add(new GH_SAMParam(thicknessFactor, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number alignColinearOffset = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "alignColinearOffset_", NickName = "alignColinearOffset_", Description = "Max perpendicular offset (m) at which consecutive segments of one vertical wall run are aligned onto a single plane. 0 = disable. Default 0.3.", Access = GH_ParamAccess.item };
                alignColinearOffset.SetPersistentData(0.3);
                result.Add(new GH_SAMParam(alignColinearOffset, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number normalizeCapOffset = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "normalizeCapOffset_", NickName = "normalizeCapOffset_", Description = "Max perpendicular offset (m) within which a level's floor/roof tiles are normalized onto one plane. 0 = disable. Default 0.3.", Access = GH_ParamAccess.item };
                normalizeCapOffset.SetPersistentData(0.3);
                result.Add(new GH_SAMParam(normalizeCapOffset, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Integer maxRounds = new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "maxRounds_", NickName = "maxRounds_", Description = "Hard cap on escalation rounds (each round is one full managed re-solve). Default 3.", Access = GH_ParamAccess.item };
                maxRounds.SetPersistentData(3);
                result.Add(new GH_SAMParam(maxRounds, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number maxExtendLadder = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "maxExtendLadder_", NickName = "maxExtendLadder_", Description = "Absolute MaxExtend permission targets (m), ascending. Each escalated culprit source is raised to the smallest rung strictly greater than its current reach. Default [0.5, 0.75, 1.0, 1.5].", Access = GH_ParamAccess.list };
                maxExtendLadder.SetPersistentData(0.5, 0.75, 1.0, 1.5);
                result.Add(new GH_SAMParam(maxExtendLadder, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean escalateBucket = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "escalateBucket_", NickName = "escalateBucket_", Description = "Also grow an escalated culprit's BucketSize. Default OFF - a bucket change alters Stage-A snapping globally for that panel and can merge a genuine double wall.", Access = GH_ParamAccess.item };
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
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "Panels", NickName = "Panels", Description = "Resolved SAM Analytical Panels after any accepted escalation rounds.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Point() { Name = "NakedPoints", NickName = "NakedPoints", Description = "Locations of residual naked (free) boundary edges after tuning.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Diagnostics", NickName = "Diagnostics", Description = "Diagnostics", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "Successful", NickName = "Successful", Description = "Run successfully?", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "NakedWires", NickName = "NakedWires", Description = "Residual naked (free) boundary loops as polylines (closed where the loop closes on itself).", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "SourceMap", NickName = "SourceMap", Description = "One line per input source: which resolved output face(s) it contributed to, and how (provenance).", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "ClosureReport", NickName = "ClosureReport", Description = "Human-readable summary: final closure signature, escalation rounds attempted/accepted, diagnostics counts.", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "Rounds", NickName = "Rounds", Description = "Escalation rounds attempted.", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "RoundsAccepted", NickName = "RoundsAccepted", Description = "Escalation rounds accepted (a subset of Rounds).", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));

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
            index = Params.IndexOfInputParam("minBucketSize_");
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

            AutoTune3DOptions tune = new AutoTune3DOptions();

            index = Params.IndexOfInputParam("maxRounds_");
            if (index != -1)
            {
                int maxRounds = tune.MaxRounds;
                if (dataAccess.GetData(index, ref maxRounds))
                {
                    tune.MaxRounds = maxRounds;
                }
            }

            index = Params.IndexOfInputParam("maxExtendLadder_");
            if (index != -1)
            {
                List<double> maxExtendLadder = new List<double>();
                if (dataAccess.GetDataList(index, maxExtendLadder) && maxExtendLadder.Count != 0)
                {
                    tune.MaxExtendLadder = maxExtendLadder;
                }
            }

            index = Params.IndexOfInputParam("escalateBucket_");
            if (index != -1)
            {
                bool escalateBucket = tune.EscalateBucket;
                if (dataAccess.GetData(index, ref escalateBucket))
                {
                    tune.EscalateBucket = escalateBucket;
                }
            }

            // weights/maxExtends null => read SolverParameter.Weight / SolverParameter.MaxExtend off each
            // panel, so they can be tuned per panel (the same convention SAMOCCT.Solve3D uses).
            List<Panel> resolvedPanels = panels.AutoTune3D(out List<Point3D> nakedPoint3Ds, out List<string> diagnostics, out _, out Solve3DReport report, weights: null, maxExtends: null, minBucketSize: minBucketSize, thicknessFactor: thicknessFactor, alignColinearOffset: alignColinearOffset, normalizeCapOffset: normalizeCapOffset, tune: tune);

            index = Params.IndexOfOutputParam("Panels");
            if (index != -1)
            {
                dataAccess.SetDataList(index, resolvedPanels?.Select(x => new GooPanel(x)));
            }

            index = Params.IndexOfOutputParam("NakedPoints");
            if (index != -1)
            {
                dataAccess.SetDataList(index, nakedPoint3Ds?.Where(x => x != null).Select(x => new global::Rhino.Geometry.Point3d(x.X, x.Y, x.Z)));
            }

            index = Params.IndexOfOutputParam("Diagnostics");
            if (index != -1)
            {
                dataAccess.SetDataList(index, diagnostics);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, resolvedPanels != null && resolvedPanels.Count != 0);
            }

            index = Params.IndexOfOutputParam("NakedWires");
            if (index != -1)
            {
                IEnumerable<Polyline3D> nakedWires = report?.NakedWires?.Where(x => x != null && x.Point3Ds.Count >= 2).Select(x => new Polyline3D(x.Point3Ds, x.IsClosed));
                dataAccess.SetDataList(index, nakedWires?.Select(x => new GooSAMGeometry(x)));
            }

            index = Params.IndexOfOutputParam("SourceMap");
            if (index != -1)
            {
                dataAccess.SetDataList(index, report?.FormatSourceMap());
            }

            index = Params.IndexOfOutputParam("ClosureReport");
            if (index != -1)
            {
                dataAccess.SetData(index, report?.ClosureReportText);
            }

            index = Params.IndexOfOutputParam("Rounds");
            if (index != -1)
            {
                dataAccess.SetData(index, report?.Rounds ?? 0);
            }

            index = Params.IndexOfOutputParam("RoundsAccepted");
            if (index != -1)
            {
                dataAccess.SetData(index, report?.RoundsAccepted ?? 0);
            }
        }
    }
}
