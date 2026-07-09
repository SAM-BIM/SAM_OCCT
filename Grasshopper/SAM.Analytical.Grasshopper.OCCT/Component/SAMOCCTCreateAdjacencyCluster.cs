// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Analytical;
using SAM.Analytical.OCCT;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Core.Grasshopper;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SAM.Analytical.Grasshopper.OCCT
{
    public class SAMOCCTCreateAdjacencyCluster : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("c104f272-9b10-454f-9825-16e2d41adfa6");

        public override string LatestComponentVersion => "0.2.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTCreateAdjacencyCluster()
          : base("SAMOCCT.CreateAdjacencyCluster", "SAMOCCT.CreateAdjacencyCluster", "Create a SAM AdjacencyCluster from analytical Panels using OCCT cell building", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                GooPanelParam panels = new GooPanelParam() { Name = "_panels", NickName = "_panels", Description = "Analytical Panels that define closed cell boundaries. Use this when your model starts from panels rather than closed Shells.", Access = GH_ParamAccess.list };
                panels.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(panels, ParamVisibility.Binding));

                GooSpaceParam spaces = new GooSpaceParam() { Name = "spaces_", NickName = "spaces_", Description = "Optional existing Spaces to match into the OCCT-created cells.", Access = GH_ParamAccess.list, Optional = true };
                spaces.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(spaces, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number tolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "tolerance_", NickName = "tolerance_", Description = "OCCT build tolerance", Access = GH_ParamAccess.item };
                tolerance.SetPersistentData(Tolerance.Distance);
                result.Add(new GH_SAMParam(tolerance, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number fuzzyTolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "fuzzyTolerance_", NickName = "fuzzyTolerance_", Description = "OCCT fuzzy tolerance", Access = GH_ParamAccess.item };
                fuzzyTolerance.SetPersistentData(Tolerance.MacroDistance);
                result.Add(new GH_SAMParam(fuzzyTolerance, ParamVisibility.Voluntary));

                // P3 (docs/CELLCOMPLEX_FIRST_HANDOVER.md): optional direct handoff from SAMOCCT.Solve3D's
                // CellComplex output. Append-only and Voluntary/Optional - unwired, this component behaves
                // exactly as before (native rebuild). Wired, it is honoured ONLY when every incoming panel's
                // SolveId stamp and the panel roster (count + Guid set) still match the complex - otherwise
                // this falls back to the same rebuild path with a drift diagnostic naming why.
                GooResolvedCellComplexParam cellComplex = new GooResolvedCellComplexParam() { Name = "cellComplex_", NickName = "cellComplex_", Description = "Optional: the CellComplex from SAMOCCT.Solve3D's CellComplex output, for a direct handoff (no native rebuild) when _panels still matches the roster that solve produced.", Access = GH_ParamAccess.item, Optional = true };
                result.Add(new GH_SAMParam(cellComplex, ParamVisibility.Voluntary));

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
                result.Add(new GH_SAMParam(new GooAdjacencyClusterParam() { Name = "AdjacencyCluster", NickName = "AdjacencyCluster", Description = "SAM AdjacencyCluster", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Diagnostics", NickName = "Diagnostics", Description = "OCCT diagnostics", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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
            if (index == -1 || !dataAccess.GetDataList(index, panels) || panels == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid panels");
                return;
            }

            List<Space> spaces = new List<Space>();
            index = Params.IndexOfInputParam("spaces_");
            if (index != -1)
            {
                dataAccess.GetDataList(index, spaces);
            }

            double tolerance = Tolerance.Distance;
            index = Params.IndexOfInputParam("tolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref tolerance);
            }

            double fuzzyTolerance = Tolerance.MacroDistance;
            index = Params.IndexOfInputParam("fuzzyTolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref fuzzyTolerance);
            }

            // P3 (docs/CELLCOMPLEX_FIRST_HANDOVER.md): optional direct handoff. Unwired (old saved
            // definitions, or a fresh solve never routed through SAMOCCT.Solve3D's CellComplex output), this
            // is null and behaviour is IDENTICAL to before P3 - the rebuild path below runs unconditionally.
            ResolvedCellComplex resolvedCellComplex = null;
            index = Params.IndexOfInputParam("cellComplex_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref resolvedCellComplex);
            }

            List<string> diagnostics = new List<string>();
            Stopwatch stopwatch_Total = Stopwatch.StartNew();
            Stopwatch stopwatch = Stopwatch.StartNew();

            AdjacencyCluster adjacencyCluster = null;

            // The roster gate: consume the supplied complex directly ONLY if every incoming panel's SolveId
            // stamp matches AND the panel roster (count + Guid set) still equals what that solve produced -
            // never silently, and only when a complex was actually wired in (an unwired cellComplex_ is the
            // normal, quiet default - not a drift to report).
            bool directConsumeAttempted = resolvedCellComplex != null;
            bool directConsumed = false;
            bool rosterOk = CellComplexHandoff.TryDirectConsume(panels, resolvedCellComplex, out string driftReason);
            if (rosterOk)
            {
                adjacencyCluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(panels, resolvedCellComplex, out List<string> complexDiagnostics, tolerance: tolerance, fuzzyTolerance: fuzzyTolerance);
                diagnostics.AddRange(complexDiagnostics);
                directConsumed = adjacencyCluster != null;

                if (!directConsumed)
                {
                    diagnostics.Add("SAM_OCCT_ANALYTICAL_COMPLEX_REBUILD: direct handoff was eligible but produced no cluster (see SAM_OCCT_ANALYTICAL_COMPLEX_* diagnostics above); falling back to the native rebuild.");
                }
            }
            else if (directConsumeAttempted)
            {
                diagnostics.Add(string.Format("SAM_OCCT_ANALYTICAL_COMPLEX_REBUILD: supplied cellComplex_ was not consumed directly ({0}); falling back to the native rebuild.", driftReason));
            }

            if (!directConsumed)
            {
                diagnostics.Add(string.Format("SAM_OCCT_ANALYTICAL_PANEL_METADATA: Supplied {0} panel(s) and {1} existing space(s). Panel OCCT path will match supplied spaces after OCCT cell creation and otherwise use auto-generated names.", panels?.Count ?? 0, spaces?.Count ?? 0));
                Log log = new Log();
                // Bugfix (P1): match the solver's own validated build recipe instead of OcctBuildOptions'
                // defaults (AvoidInternalShapes=true, SewBeforeBuild=false, SewingTolerance=0.0). Production
                // previously diverged from every test/solver call site, which built with these solver-matched
                // options - see docs/CELLCOMPLEX_FIRST_HANDOVER.md §A "Diagnosed seam".
                adjacencyCluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(spaces, panels, out OcctCellComplexResult result, log, new OcctBuildOptions { Tolerance = tolerance, FuzzyTolerance = fuzzyTolerance, AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 });
                diagnostics.Add(string.Format("SAM_OCCT_TIMING_OCCT_AND_ADJACENCY: {0:0.000}s.", stopwatch.Elapsed.TotalSeconds));

                if (result?.Diagnostics != null)
                {
                    diagnostics.AddRange(result.Diagnostics.Select(x => x.ToString()));
                }

                if (adjacencyCluster == null)
                {
                    diagnostics.Add("SAM_OCCT_ANALYTICAL_PANEL_REBUILD_FAILED: OCCT could not create a valid adjacency cluster from the supplied panels.");
                }
                else
                {
                    diagnostics.Add(string.Format("SAM_OCCT_ANALYTICAL_PANEL_SUCCESS: Created adjacency cluster with {0} space(s) and {1} panel(s).", adjacencyCluster.GetSpaces()?.Count ?? 0, adjacencyCluster.GetPanels()?.Count ?? 0));
                }
            }

            diagnostics.Add(string.Format("SAM_OCCT_TIMING_TOTAL: {0:0.000}s.", stopwatch_Total.Elapsed.TotalSeconds));

            index = Params.IndexOfOutputParam("AdjacencyCluster");
            if (index != -1)
            {
                dataAccess.SetData(index, adjacencyCluster == null ? null : new GooAdjacencyCluster(adjacencyCluster));
            }

            index = Params.IndexOfOutputParam("Diagnostics");
            if (index != -1)
            {
                dataAccess.SetDataList(index, diagnostics);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, adjacencyCluster != null);
            }

            foreach (string diagnostic in diagnostics)
            {
                AddRuntimeMessage(adjacencyCluster == null ? GH_RuntimeMessageLevel.Warning : GH_RuntimeMessageLevel.Remark, diagnostic);
            }
        }
    }
}
