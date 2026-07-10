// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Analytical.Solver;
using SAM.Core;
using SAM.Core.Grasshopper;
using SAM.Geometry.Grasshopper;
using SAM.Geometry.OCCT;
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

        public override string LatestComponentVersion => "0.6.0";

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

                global::Grasshopper.Kernel.Parameters.Param_Number normalizeCapOffset = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "normalizeCapOffset_", NickName = "normalizeCapOffset_", Description = "Max perpendicular offset (m) within which a level's floor/roof tiles are normalized onto one plane (the dominant cap's). Collapses the small plane differences left when several imported roof/floor tiles over one space are merged at slightly different tilts/elevations, so the kernel can close the cell. Floors and roofs separate automatically. 0 = disable. Default 0.3.", Access = GH_ParamAccess.item };
                normalizeCapOffset.SetPersistentData(0.3);
                result.Add(new GH_SAMParam(normalizeCapOffset, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number bucketBetweenLevels = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "bucketBetweenLevels_", NickName = "bucketBetweenLevels_", Description = "Level-group merge band (m, P2). GH default 0.21; 0 = off. Merges near-coplanar slab-skin datums in the managed pipeline while the raw LevelFrame band remains 0.15 m; a raw-first-adopted solve never clusters caps. SAM_Solver uses the same name (and GH default 0.21) for a final cross-level WALL re-snap; SAM_OCCT instead merges LEVEL DATUMS and performs no cross-level wall re-snap. Tune larger values per model; values >= 0.25 can consume genuine split levels.", Access = GH_ParamAccess.item };
                bucketBetweenLevels.SetPersistentData(SolverComponentDefaults.BucketBetweenLevels);
                result.Add(new GH_SAMParam(bucketBetweenLevels, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number slitMinGap = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "slitMinGap_", NickName = "slitMinGap_", Description = "Minimum perpendicular gap (m) of a remaining double-wall/slit to report in the slits diagnostics.", Access = GH_ParamAccess.item };
                slitMinGap.SetPersistentData(0.02);
                result.Add(new GH_SAMParam(slitMinGap, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number slitMaxGap = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "slitMaxGap_", NickName = "slitMaxGap_", Description = "Maximum perpendicular gap (m) of a remaining double-wall/slit to report in the slits diagnostics.", Access = GH_ParamAccess.item };
                slitMaxGap.SetPersistentData(0.5);
                result.Add(new GH_SAMParam(slitMaxGap, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number slitMaxOverlap = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "slitMaxOverlap_", NickName = "slitMaxOverlap_", Description = "Maximum parallel overlap length (m) reported as a slit. Longer side-by-side runs are ignored. 0 = no limit.", Access = GH_ParamAccess.item };
                slitMaxOverlap.SetPersistentData(2.0);
                result.Add(new GH_SAMParam(slitMaxOverlap, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean classifyCells = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "classifyCells_", NickName = "classifyCells_", Description = "Classify resolved cells (Interior/Exterior/Sliver) for the CellClassification output - one extra native envelope decode. Default false (no extra cost unless requested).", Access = GH_ParamAccess.item };
                classifyCells.SetPersistentData(false);
                result.Add(new GH_SAMParam(classifyCells, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number minCellVolume = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "minCellVolume_", NickName = "minCellVolume_", Description = "Minimum cell volume (m3) below which a cell classifies Sliver. Only used when classifyCells_ is true.", Access = GH_ParamAccess.item };
                minCellVolume.SetPersistentData(0.05);
                result.Add(new GH_SAMParam(minCellVolume, ParamVisibility.Voluntary));

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

                // Phase 8: staged/diagnostic outputs, append-only and Voluntary (right-click "Zoom in" or the
                // parameter list to add them - existing saved definitions keep working unchanged).
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "CleanFaces", NickName = "CleanFaces", Description = "Stage A (clean bucket) output faces: external shape only, within-bucket parallels snapped onto one backer, coplanar overlaps merged. Empty when the raw-first attempt was adopted (Stage A never ran) - see Successful/ClosureReport's \"Adopted path\".", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new GooPanelParam() { Name = "GapFillPanels", NickName = "GapFillPanels", Description = "The PanelType.Air panels this solve fabricated to close residual naked-boundary loops (a subset of Panels, filtered by Provenance=GapFill).", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "Cells", NickName = "Cells", Description = "Boundary shells of the resolved cells (rooms/zones) the native kernel formed.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Point() { Name = "CellCentres", NickName = "CellCentres", Description = "Centre point of each resolved cell, index-aligned with Cells/CellVolumes/CellClassification.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "CellVolumes", NickName = "CellVolumes", Description = "Volume (m3) of each resolved cell, index-aligned with Cells.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "CellClassification", NickName = "CellClassification", Description = "Interior/Exterior/Sliver/Unknown role of each resolved cell, index-aligned with Cells. Empty unless classifyCells_ is true.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "NakedWires", NickName = "NakedWires", Description = "Residual naked (free) boundary loops as polylines (closed where the loop closes on itself).", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "SourceMap", NickName = "SourceMap", Description = "One line per input source: which resolved output face(s) it contributed to, and how (provenance).", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "LevelFrames", NickName = "LevelFrames", Description = "One line per clustered RAW level datum (elevation, tilt, cap count) the managed pipeline conditioned onto. Empty when the raw-first attempt was adopted or the model formed no frames.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "LevelGroups", NickName = "LevelGroups", Description = "One line per level GROUP (P2): the merged storey datum caps normalize onto (elevation, the raw frames it merged, cap count, spread, tilt). Equals LevelFrames when bucketBetweenLevels = 0. Empty when the raw-first attempt was adopted or the model formed no frames.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "ClosureReport", NickName = "ClosureReport", Description = "Human-readable summary of the solve: adopted path, raw/final closure signatures, AutoTune rounds, diagnostics counts, level frames.", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));

                // P3 (docs/CELLCOMPLEX_FIRST_HANDOVER.md): the CellComplex the solve adopted, exposed as a
                // value so SAMOCCT.CreateAdjacencyCluster can consume it directly instead of rebuilding.
                // Append-only and Voluntary - existing saved definitions keep working unchanged.
                result.Add(new GH_SAMParam(new GooResolvedCellComplexParam() { Name = "CellComplex", NickName = "CellComplex", Description = "The cell complex this solve adopted (cells, unique faces, adjacencies, naked wires, SolveId). Wire into SAMOCCT.CreateAdjacencyCluster's cellComplex_ input for a direct handoff (no native rebuild) - it is honoured only when the incoming panel roster still matches (see SolveId output stamp).", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "ComplexFaces", NickName = "ComplexFaces", Description = "Geometry of every unique cell face in the CellComplex (index-aligned with ComplexFaceOwners).", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "ComplexFaceOwners", NickName = "ComplexFaceOwners", Description = "Per unique cell face (index-aligned with ComplexFaces): its per-decode face key and owner cell index/indices - one owner for an envelope face, two for a shared internal separator.", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "ComplexAdjacencies", NickName = "ComplexAdjacencies", Description = "One line per shared-face adjacency: the two cell indices it separates and its face key (index-aligned with ComplexAdjacencyFaces).", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new GooSAMGeometryParam() { Name = "ComplexAdjacencyFaces", NickName = "ComplexAdjacencyFaces", Description = "The shared-face geometry for each adjacency (index-aligned with ComplexAdjacencies).", Access = GH_ParamAccess.list }, ParamVisibility.Voluntary));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "ComplexSummary", NickName = "ComplexSummary", Description = "One-line CellComplex summary: cell/face/adjacency/naked-wire counts, excluded-face count, panel-roster count, SolveId.", Access = GH_ParamAccess.item }, ParamVisibility.Voluntary));

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

            // The voluntary input is absent on old saved components AND fresh placements, so this fallback is
            // the effective GH default. Version-gated: documents saved before 0.6.0 keep the core default 0.
            double bucketBetweenLevels = SolverComponentDefaults.BucketBetweenLevelsFallback(ComponentVersion, "0.6.0");
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

            bool classifyCells = false;
            index = Params.IndexOfInputParam("classifyCells_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref classifyCells);
            }

            double minCellVolume = 0.05;
            index = Params.IndexOfInputParam("minCellVolume_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref minCellVolume);
            }

            // weights/maxExtends null => read SolverParameter.Weight / SolverParameter.MaxExtend off each
            // panel (the same parameters SAMAnalytical.Visualize shows), so they can be tuned per panel.
            List<Panel> resolvedPanels = panels.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> diagnostics, out _, out Solve3DReport report, weights: null, maxExtends: null, minBucketSize: minBucketSize, thicknessFactor: thicknessFactor, alignColinearOffset: alignColinearOffset, normalizeCapOffset: normalizeCapOffset, classifyCells: classifyCells, minCellVolume: minCellVolume, bucketBetweenLevels: bucketBetweenLevels);

            // P3 (docs/CELLCOMPLEX_FIRST_HANDOVER.md): stamp every output panel with this solve's SolveId
            // (PanelProvenanceParameter), and attach the SAME roster to the CellComplex output, so
            // SAMOCCT.CreateAdjacencyCluster can later prove an incoming panel set genuinely came from THIS
            // solve (the P3 roster gate) before consuming the complex directly instead of rebuilding.
            ResolvedCellComplex resolvedCellComplex = report?.ResolvedCellComplex;
            if (resolvedCellComplex != null && resolvedPanels != null)
            {
                CellComplexHandoff.StampSolveId(resolvedPanels, resolvedCellComplex.SolveId);
                resolvedCellComplex = resolvedCellComplex.WithPanelGuids(resolvedPanels.Where(x => x != null).Select(x => x.Guid));
            }

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

            // Phase 8 staged/diagnostic outputs - all Voluntary, so a definition that never wires them up pays
            // no extra cost beyond the (already-cheap unless classifyCells_ is true) report assembly.
            index = Params.IndexOfOutputParam("CleanFaces");
            if (index != -1)
            {
                dataAccess.SetDataList(index, report?.CleanFace3Ds?.Where(x => x != null).Select(x => new GooSAMGeometry(x)));
            }

            index = Params.IndexOfOutputParam("GapFillPanels");
            if (index != -1)
            {
                IEnumerable<Panel> gapFillPanels = resolvedPanels?.Where(x => x != null && x.PanelType == PanelType.Air && x.TryGetValue(PanelProvenanceParameter.Provenance, out string provenance) && provenance == "GapFill");
                dataAccess.SetDataList(index, gapFillPanels?.Select(x => new GooPanel(x)));
            }

            index = Params.IndexOfOutputParam("Cells");
            if (index != -1)
            {
                dataAccess.SetDataList(index, report?.Cells?.Where(x => x?.Shell != null).Select(x => new GooSAMGeometry(x.Shell)));
            }

            index = Params.IndexOfOutputParam("CellCentres");
            if (index != -1)
            {
                dataAccess.SetDataList(index, report?.Cells?.Where(x => x?.Center != null).Select(x => new global::Rhino.Geometry.Point3d(x.Center.X, x.Center.Y, x.Center.Z)));
            }

            index = Params.IndexOfOutputParam("CellVolumes");
            if (index != -1)
            {
                dataAccess.SetDataList(index, report?.Cells?.Where(x => x != null).Select(x => x.Volume));
            }

            index = Params.IndexOfOutputParam("CellClassification");
            if (index != -1)
            {
                dataAccess.SetDataList(index, report?.CellRoles?.Select(x => x.ToString()));
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

            index = Params.IndexOfOutputParam("ClosureReport");
            if (index != -1)
            {
                dataAccess.SetData(index, report?.ClosureReportText);
            }

            // P3 CellComplex outputs - all Voluntary, sourced from the roster-stamped complex above so the
            // SolveId this component just stamped on Panels matches what CellComplex/ComplexSummary report.
            index = Params.IndexOfOutputParam("CellComplex");
            if (index != -1)
            {
                dataAccess.SetData(index, resolvedCellComplex == null ? null : new GooResolvedCellComplex(resolvedCellComplex));
            }

            index = Params.IndexOfOutputParam("ComplexFaces");
            if (index != -1)
            {
                dataAccess.SetDataList(index, SolverReportFormat.FormatResolvedCellComplexFaceGeometry(resolvedCellComplex)?.Select(x => new GooSAMGeometry(x)));
            }

            index = Params.IndexOfOutputParam("ComplexFaceOwners");
            if (index != -1)
            {
                dataAccess.SetDataList(index, SolverReportFormat.FormatResolvedCellComplexFaceOwners(resolvedCellComplex));
            }

            index = Params.IndexOfOutputParam("ComplexAdjacencies");
            if (index != -1)
            {
                dataAccess.SetDataList(index, SolverReportFormat.FormatResolvedCellComplexAdjacencies(resolvedCellComplex));
            }

            index = Params.IndexOfOutputParam("ComplexAdjacencyFaces");
            if (index != -1)
            {
                dataAccess.SetDataList(index, SolverReportFormat.FormatResolvedCellComplexAdjacencyFaceGeometry(resolvedCellComplex)?.Where(x => x != null).Select(x => new GooSAMGeometry(x)));
            }

            index = Params.IndexOfOutputParam("ComplexSummary");
            if (index != -1)
            {
                dataAccess.SetData(index, SolverReportFormat.FormatResolvedCellComplexSummary(resolvedCellComplex));
            }
        }
    }
}
