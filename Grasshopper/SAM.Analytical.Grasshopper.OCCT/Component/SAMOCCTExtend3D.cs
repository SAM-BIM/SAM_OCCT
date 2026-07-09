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

        public override string LatestComponentVersion => "0.6.0";

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

                global::Grasshopper.Kernel.Parameters.Param_Number alignColinearOffset = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "alignColinearOffset_", NickName = "alignColinearOffset_", Description = "Max perpendicular offset (m) at which consecutive segments of one vertical wall run are aligned onto a single plane (closes small step jogs in an imported wall). Keep below the gap between genuinely separate parallel walls so those stay put. 0 = disable. Default 0.3.", Access = GH_ParamAccess.item };
                alignColinearOffset.SetPersistentData(0.3);
                result.Add(new GH_SAMParam(alignColinearOffset, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number normalizeCapOffset = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "normalizeCapOffset_", NickName = "normalizeCapOffset_", Description = "Max perpendicular offset (m) within which a level's floor/roof tiles are normalized onto one plane (the dominant cap's). Collapses the small plane differences left when several imported roof/floor tiles over one space are merged at slightly different tilts/elevations, so the kernel can close the cell. Floors and roofs separate automatically. 0 = disable. Default 0.3.", Access = GH_ParamAccess.item };
                normalizeCapOffset.SetPersistentData(0.3);
                result.Add(new GH_SAMParam(normalizeCapOffset, ParamVisibility.Voluntary));

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
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "Panels", NickName = "Panels", Description = "Filled/extended panels (pre-resolve), for REVIEW only. Floors/roofs grown out to the walls and walls extended to their caps (overshooting); not yet trimmed. Carry BucketSize/Weight so SAMAnalytical.Visualize draws the capture slab (bucket). Do NOT pipe these into SAMOCCT.Solve3D - it re-extends internally; feed Solve3D the original (or Clean3D) panels instead.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "Slits", NickName = "Slits", Description = "Remaining double-wall/slit diagnostics as short connector Segment3Ds between near-parallel wall axes. Same detection as AutoTuneSolver.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "SlitPanels", NickName = "SlitPanels", Description = "Panels whose section axes touch a remaining slit. Use these to spot double-wall/problem panels before Solve3D.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "OpenPanels", NickName = "OpenPanels", Description = "Walls whose feet do NOT close into a loop in plan after the extend - they still have an end no other wall meets. Raise their MaxExtend (SolverParameter.MaxExtend) or bucket size and re-run so floors/roofs can fill a closed polysurface.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Point() { Name = "OpenEnds", NickName = "OpenEnds", Description = "Locations of the open (naked-in-plan) wall-foot ends - the corners where the loop does not close.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Diagnostics", NickName = "Diagnostics", Description = "Diagnostics", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "Successful", NickName = "Successful", Description = "Run successfully?", Access = GH_ParamAccess.item }, ParamVisibility.Binding));

                // Phase 8: pre-resolve reporting, append-only and Voluntary - existing saved definitions keep working.
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "SourceMap", NickName = "SourceMap", Description = "One line per input source: which filled/extended output face(s) it contributed to.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "LevelFrames", NickName = "LevelFrames", Description = "One line per clustered level datum (elevation, tilt, cap count) cap normalization conditioned onto. Empty when the model formed no frames.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));

                // E3 (docs/EXTEND3D_ROBUST_HANDOVER.md): per-panel extend observability, append-only and Voluntary.
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "ExtendReport", NickName = "ExtendReport", Description = "One SAM_OCCT_EXTEND3D_PANEL line per applied extend/fill op: which panel (source Guid + solver index), which edge (top / bottom / plan-start / plan-end / cap-grow), measured from -> to, toward what target (cap index + scalar or sloped-plane branch, or the 2D plan-loop), the overshoot and the lateral-cap flag. Also carries any SAM_OCCT_EXTEND3D_HOLE_DROPPED a footprint trim recorded.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "ExtendPreview", NickName = "ExtendPreview", Description = "Moved-edge preview: a Segment3D from -> to for each wall edge the extend moved (top/base raised/lowered at the wall centre, plan ends grown). Cap grows are in-plane offsets with no single edge and contribute none. Drop alongside the panels to see what moved and how far.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));

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
            List<Panel> extendedPanels = panels.Extend3D(out List<string> diagnostics, out Solve3DReport report, weights: null, maxExtends: null, minBucketSize: minBucketSize, thicknessFactor: thicknessFactor, fillMargin: fillMargin, alignColinearOffset: alignColinearOffset, normalizeCapOffset: normalizeCapOffset);

            index = Params.IndexOfOutputParam("Panels");
            if (index != -1)
            {
                dataAccess.SetDataList(index, extendedPanels?.Select(x => new GooPanel(x)));
            }

            // Remaining double-wall/slit diagnostics on the filled/extended panels (same detector as
            // AutoTuneSolver). Only surface slits the bucket did NOT close: parallel panels whose
            // perpendicular gap is OUTSIDE the capture slab (gap > bucket width). Pairs within the bucket
            // are snapped/merged by the clean step, so reporting them is noise - floor the slit gap at the
            // bucket capture width.
            double slitGapFloor = System.Math.Max(slitMinGap, BucketCaptureWidth(extendedPanels, minBucketSize));
            List<Segment3D> slits = extendedPanels.Slits(out List<Panel> slitPanels, null, 0.2, slitGapFloor, slitMaxGap, slitMaxOverlap);

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

            // Plan-closure diagnostic: the walls whose feet still leave an open end (no other wall meets them),
            // and the open-corner locations. These are the panels to upgrade (MaxExtend / bucket) so the loops
            // close and floors/roofs can fill a closed polysurface.
            List<Panel> openPanels = panels.OpenPanels3D(out List<Point3D> openEndPoint3Ds, out List<string> openDiagnostics, weights: null, maxExtends: null, minBucketSize: minBucketSize, thicknessFactor: thicknessFactor, alignColinearOffset: alignColinearOffset, normalizeCapOffset: normalizeCapOffset);
            if (openDiagnostics != null)
            {
                diagnostics.AddRange(openDiagnostics);
            }

            index = Params.IndexOfOutputParam("OpenPanels");
            if (index != -1)
            {
                dataAccess.SetDataList(index, openPanels?.Where(x => x != null).Select(x => new GooPanel(x)));
            }

            index = Params.IndexOfOutputParam("OpenEnds");
            if (index != -1)
            {
                dataAccess.SetDataList(index, openEndPoint3Ds?.Where(x => x != null).Select(x => new global::Rhino.Geometry.Point3d(x.X, x.Y, x.Z)));
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

            // E3 observability: per-panel extend summary + moved-edge preview from the pre-resolve report.
            index = Params.IndexOfOutputParam("ExtendReport");
            if (index != -1)
            {
                dataAccess.SetDataList(index, report?.FormatExtendRecords());
            }

            index = Params.IndexOfOutputParam("ExtendPreview");
            if (index != -1)
            {
                dataAccess.SetDataList(index, report?.ExtendPreviewSegment3Ds()?.Where(x => x != null).Select(x => new GooSAMGeometry(x)));
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
