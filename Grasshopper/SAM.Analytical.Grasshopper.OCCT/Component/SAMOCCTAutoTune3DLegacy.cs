// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core.Grasshopper;
using SAM.Geometry.Grasshopper;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.Grasshopper.OCCT
{
    public class SAMOCCTAutoTune3DLegacy : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("9de8b4c0-14f6-4828-b966-aa57cf58143b");
        public override string LatestComponentVersion => "0.5.0";
        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTAutoTune3DLegacy()
          : base("SAMOCCT.AutoTune3D (Legacy)", "SAMOCCT.AutoTune3D (Legacy)",
                "Legacy escalation-solver AutoTune. Runs the solver with your supplied parameters (no parameter sweep). Use SAMOCCT.AutoTune3D (new GUID) for discovery-capable version.",
                "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                GooPanelParam panels = new GooPanelParam() { Name = "_panels", NickName = "_panels", Description = "Input panels.", Access = GH_ParamAccess.list };
                panels.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(panels, ParamVisibility.Binding));

                var band = new global::Grasshopper.Kernel.Parameters.Param_Number()
                { Name = "_band", NickName = "_band", Description = "Level-group merge band (m).", Access = GH_ParamAccess.item };
                band.SetPersistentData(0.0);
                result.Add(new GH_SAMParam(band, ParamVisibility.Binding));

                var fill = new global::Grasshopper.Kernel.Parameters.Param_Number()
                { Name = "_fill", NickName = "_fill", Description = "Cap growth distance (m).", Access = GH_ParamAccess.item };
                fill.SetPersistentData(0.5);
                result.Add(new GH_SAMParam(fill, ParamVisibility.Binding));

                var bucket = new global::Grasshopper.Kernel.Parameters.Param_Number()
                { Name = "_bucket", NickName = "_bucket", Description = "Wall merge half-width.", Access = GH_ParamAccess.item };
                bucket.SetPersistentData(0.4);
                result.Add(new GH_SAMParam(bucket, ParamVisibility.Binding));

                var align = new global::Grasshopper.Kernel.Parameters.Param_Number()
                { Name = "_align", NickName = "_align", Description = "Colinear wall merge distance.", Access = GH_ParamAccess.item };
                align.SetPersistentData(0.3);
                result.Add(new GH_SAMParam(align, ParamVisibility.Binding));

                var dirGrow = new global::Grasshopper.Kernel.Parameters.Param_Boolean()
                { Name = "_dirGrow", NickName = "_dirGrow", Description = "Cap growth mode.", Access = GH_ParamAccess.item };
                dirGrow.SetPersistentData(true);
                result.Add(new GH_SAMParam(dirGrow, ParamVisibility.Voluntary));

                var gap = new global::Grasshopper.Kernel.Parameters.Param_Number()
                { Name = "_gap", NickName = "_gap", Description = "Double-wall merge gap (m).", Access = GH_ParamAccess.item };
                gap.SetPersistentData(0.0);
                result.Add(new GH_SAMParam(gap, ParamVisibility.Binding));

                var run = new global::Grasshopper.Kernel.Parameters.Param_Boolean()
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
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "Panels", NickName = "Panels", Description = "Solved panels.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Point() { Name = "NakedPoints", NickName = "NakedPoints", Description = "Naked edge locations.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Report", NickName = "Report", Description = "Diagnostics.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Diagnostics", NickName = "Diagnostics", Description = "Diagnostics.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "Successful", NickName = "Successful", Description = "Run successful?", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
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
            if (index == -1 || !dataAccess.GetDataList(index, panels) || panels.Count == 0) return;

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

            double gap = 0.0;
            index = Params.IndexOfInputParam("_gap");
            if (index != -1) dataAccess.GetData(index, ref gap);

            var tune = new SAM.Geometry.OCCT.Solver.AutoTune3DOptions();

            List<Panel> resolved = panels.AutoTune3D(
                out List<Point3D> nakedPoints, out List<string> diags, out _, out Solve3DReport solveReport,
                minBucketSize: bucket, tune: tune,
                alignColinearOffset: align,
                bucketBetweenLevels: band, fillMargin: fill, directionalCapGrow: dirGrow,
                doubleWallGap: gap);

            index = Params.IndexOfOutputParam("Panels");
            if (index != -1) dataAccess.SetDataList(index, resolved?.Select(x => new GooPanel(x)));

            index = Params.IndexOfOutputParam("NakedPoints");
            if (index != -1) dataAccess.SetDataList(index, nakedPoints?.Where(x => x != null).Select(x => new global::Rhino.Geometry.Point3d(x.X, x.Y, x.Z)));

            index = Params.IndexOfOutputParam("Report");
            if (index != -1) dataAccess.SetDataList(index, diags);

            index = Params.IndexOfOutputParam("Diagnostics");
            if (index != -1) dataAccess.SetDataList(index, diags);

            if (index_ok != -1) dataAccess.SetData(index_ok, resolved != null && resolved.Count != 0);
        }
    }
}