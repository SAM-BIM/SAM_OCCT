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
    /// Step 2 (managed) of the 3D panel solver, before the split: clean bucket (Step 1) then fill floors/roofs
    /// out to the surrounding walls and extend walls up to the cap above / down to the floor below. The walls
    /// overshoot their caps and the caps overshoot the walls on purpose - the native MakerVolume split that
    /// trims them back runs in SAMOCCT.Solve3D. Use this to review the pre-resolve fill/extend geometry.
    /// </summary>
    public class SAMOCCTExtend3D : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("a7e4c92f-1b53-4d8a-9f26-3c70e1b8d4a5");

        public override string LatestComponentVersion => "0.2.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTExtend3D()
          : base("SAMOCCT.Extend3D", "SAMOCCT.Extend3D", "Step 2 (managed) of the 3D panel solver, before the split: clean bucket then fill floors/roofs out to the walls and extend walls up to the cap above / down to the floor below. Walls overshoot their caps on purpose - the native split that trims them runs in SAMOCCT.Solve3D. Use to review the fill/extend geometry before resolving.", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                GooPanelParam panels = new GooPanelParam() { Name = "_panels", NickName = "_panels", Description = "SAM Analytical Panels to fill/extend. Air panels are ignored. Typically the SAMOCCT.Clean3D output.", Access = GH_ParamAccess.list };
                panels.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(panels, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number minBucketSize = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "minBucketSize_", NickName = "minBucketSize_", Description = "Capture half-width (m). Parallel panels within this band snap onto one backer in the clean bucket. Larger = more merging.", Access = GH_ParamAccess.item };
                minBucketSize.SetPersistentData(0.4);
                result.Add(new GH_SAMParam(minBucketSize, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number thicknessFactor = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "thicknessFactor_", NickName = "thicknessFactor_", Description = "Fraction of construction thickness used as the capture half-width (floored at minBucketSize).", Access = GH_ParamAccess.item };
                thicknessFactor.SetPersistentData(0.6);
                result.Add(new GH_SAMParam(thicknessFactor, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number fillMargin = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "fillMargin_", NickName = "fillMargin_", Description = "How far floors/roofs are grown outward past the walls (and walls past their caps), in metres. The overshoot the native split trims back in Solve3D.", Access = GH_ParamAccess.item };
                fillMargin.SetPersistentData(0.5);
                result.Add(new GH_SAMParam(fillMargin, ParamVisibility.Voluntary));

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
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "Panels", NickName = "Panels", Description = "Filled/extended panels (pre-resolve). Floors/roofs grown out to the walls and walls extended to their caps (overshooting); not yet trimmed. Carry BucketSize/Weight so SAMAnalytical.Visualize draws the capture slab (bucket).", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "Slits", NickName = "Slits", Description = "Remaining double-wall/slit diagnostics as short connector Segment3Ds between near-parallel wall axes. Same detection as AutoTuneSolver.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "SlitPanels", NickName = "SlitPanels", Description = "Panels whose section axes touch a remaining slit. Use these to spot double-wall/problem panels before Solve3D.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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

            double fillMargin = 0.5;
            index = Params.IndexOfInputParam("fillMargin_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref fillMargin);
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

            List<Panel> extendedPanels = panels.Extend3D(out List<string> diagnostics, null, minBucketSize, thicknessFactor, fillMargin);

            index = Params.IndexOfOutputParam("Panels");
            if (index != -1)
            {
                dataAccess.SetDataList(index, extendedPanels?.Select(x => new GooPanel(x)));
            }

            // Remaining double-wall/slit diagnostics on the filled/extended panels (same detector as AutoTuneSolver).
            List<Segment3D> slits = extendedPanels.Slits(out List<Panel> slitPanels, null, 0.2, slitMinGap, slitMaxGap, slitMaxOverlap);

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
                dataAccess.SetData(index_Successful, extendedPanels != null && extendedPanels.Count != 0);
            }
        }
    }
}
