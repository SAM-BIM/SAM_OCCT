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
    /// The full 3D panel solver: Step 1 (clean bucket) then Step 2 (fill floors/roofs to walls, extend walls
    /// between floors and to roofs, native MakerVolume resolve). Returns the resolved panels plus the locations
    /// of any residual naked (free) boundary edges.
    /// </summary>
    public class SAMOCCTSolve3D : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("3d6b9e02-4a17-4c8d-b5e3-1f9a2c7d4e8b");

        public override string LatestComponentVersion => "0.3.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTSolve3D()
          : base("SAMOCCT.Solve3D", "SAMOCCT.Solve3D", "The full 3D panel solver: clean bucket (Step 1) then fill floors/roofs to walls + extend walls between floors and to roofs + native resolve (Step 2). Returns resolved panels and naked-edge locations.", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                GooPanelParam panels = new GooPanelParam() { Name = "_panels", NickName = "_panels", Description = "SAM Analytical Panels to solve. Air panels are ignored. Feed the ORIGINAL panels (or SAMOCCT.Clean3D output) - Solve3D runs clean + extend + resolve itself. Do NOT feed SAMOCCT.Extend3D output: that is already-extended (overshooting) geometry and re-extending it over-merges panels and can drop walls.", Access = GH_ParamAccess.list };
                panels.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(panels, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number minBucketSize = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "minBucketSize_", NickName = "minBucketSize_", Description = "Capture half-width (m). Parallel panels within this band snap onto one backer in Step 1. Larger = more merging.", Access = GH_ParamAccess.item };
                minBucketSize.SetPersistentData(0.4);
                result.Add(new GH_SAMParam(minBucketSize, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number thicknessFactor = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "thicknessFactor_", NickName = "thicknessFactor_", Description = "Fraction of construction thickness used as the capture half-width (floored at minBucketSize).", Access = GH_ParamAccess.item };
                thicknessFactor.SetPersistentData(0.6);
                result.Add(new GH_SAMParam(thicknessFactor, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number alignColinearOffset = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "alignColinearOffset_", NickName = "alignColinearOffset_", Description = "Max perpendicular offset (m) at which consecutive segments of one vertical wall run are aligned onto a single plane (closes small step jogs in an imported wall). Keep below the gap between genuinely separate parallel walls so those stay put. 0 = disable. Default 0.3.", Access = GH_ParamAccess.item };
                alignColinearOffset.SetPersistentData(0.3);
                result.Add(new GH_SAMParam(alignColinearOffset, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number slitMinGap = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "slitMinGap_", NickName = "slitMinGap_", Description = "Minimum perpendicular gap (m) of a remaining double-wall/slit to report in the slits diagnostics.", Access = GH_ParamAccess.item };
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
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "Panels", NickName = "Panels", Description = "Resolved SAM Analytical Panels (Step 2 output). Carry BucketSize/Weight so SAMAnalytical.Visualize draws the capture slab (bucket) in the middle of each panel.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Point() { Name = "NakedPoints", NickName = "NakedPoints", Description = "Locations of residual naked (free) boundary edges.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "Slits", NickName = "Slits", Description = "Remaining double-wall/slit diagnostics as short connector Segment3Ds between near-parallel wall axes in the resolved panels. Same detection as AutoTuneSolver.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "SlitPanels", NickName = "SlitPanels", Description = "Resolved panels whose section axes touch a remaining slit. Use these to spot double-wall/problem panels.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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

            // weights/maxExtends null => read SolverParameter.Weight / SolverParameter.MaxExtend off each
            // panel (the same parameters SAMAnalytical.Visualize shows), so they can be tuned per panel.
            List<Panel> resolvedPanels = panels.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> diagnostics, weights: null, maxExtends: null, minBucketSize: minBucketSize, thicknessFactor: thicknessFactor, alignColinearOffset: alignColinearOffset);

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

            // Remaining double-wall/slit diagnostics on the resolved panels (same detector as AutoTuneSolver).
            List<Segment3D> slits = resolvedPanels.Slits(out List<Panel> slitPanels, null, 0.2, slitMinGap, slitMaxGap, slitMaxOverlap);

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
                dataAccess.SetData(index_Successful, resolvedPanels != null && resolvedPanels.Count != 0);
            }
        }
    }
}
