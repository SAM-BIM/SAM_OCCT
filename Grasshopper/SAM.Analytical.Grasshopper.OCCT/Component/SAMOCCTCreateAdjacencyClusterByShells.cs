// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using SAM.Analytical;
using SAM.Core;
using SAM.Core.Grasshopper;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace SAM.Analytical.Grasshopper.OCCT
{
    public class SAMOCCTCreateAdjacencyClusterByShells : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("d6950fec-cea4-4b48-9099-8943a7765e81");

        public override string LatestComponentVersion => "0.3.4";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTCreateAdjacencyClusterByShells()
          : base("SAMOCCT.CreateAdjacencyClusterByShells", "SAMOCCT.CreateAdjacencyClusterByShells", "Create a SAM AdjacencyCluster from closed shell space volumes using OCCT cell building", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                global::Grasshopper.Kernel.Parameters.Param_GenericObject shells = new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_shells", NickName = "_shells", Description = "Closed space volumes. Accepts SAM Shells, closed Rhino Breps/polysurfaces, Rhino Meshes, or SAM Mesh3Ds. Each item should represent one intended space/cell: meshes have their faces assembled into a single Shell.", Access = GH_ParamAccess.list };
                shells.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(shells, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number elevationGround = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "elevationGround_", NickName = "elevationGround_", Description = "Ground elevation", Access = GH_ParamAccess.item };
                elevationGround.SetPersistentData(0.0);
                result.Add(new GH_SAMParam(elevationGround, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number maxDistance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "maxDistance_", NickName = "maxDistance_", Description = "Compatibility input for legacy SAM rebuild panel matching. The shell-native direct OCCT topology path normally does not use this value.", Access = GH_ParamAccess.item };
                maxDistance.SetPersistentData(0.01);
                result.Add(new GH_SAMParam(maxDistance, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number maxAngle = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "maxAngle_", NickName = "maxAngle_", Description = "Advanced SAM rebuild panel matching angle", Access = GH_ParamAccess.item };
                maxAngle.SetPersistentData(0.0872664626);
                result.Add(new GH_SAMParam(maxAngle, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number fuzzyTolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "fuzzyTolerance_", NickName = "fuzzyTolerance_", Description = "OCCT fuzzy tolerance. Also used as SAM silver spacing for seed space and shell checks.", Access = GH_ParamAccess.item };
                fuzzyTolerance.SetPersistentData(Tolerance.MacroDistance);
                result.Add(new GH_SAMParam(fuzzyTolerance, ParamVisibility.Voluntary));

                // minArea_ is a POST-build panel filter only (issue #11): every shell face
                // is kept for the OCCT volume build so the cell never opens, then faces below
                // minArea_ are simply not turned into SAM panels. It cannot reopen a shell.
                global::Grasshopper.Kernel.Parameters.Param_Number minArea = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "minArea_", NickName = "minArea_", Description = "Minimum SAM panel area in m². Applied AFTER the OCCT volume is built: every shell face is kept so the cell stays closed, but faces below this area are not turned into SAM panels (e.g. tiny triangulation slivers). Does not reopen shells. Set to 0 to keep every panel.", Access = GH_ParamAccess.item };
                minArea.SetPersistentData(0.01);
                result.Add(new GH_SAMParam(minArea, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number tolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "tolerance_", NickName = "tolerance_", Description = "OCCT and SAM model tolerance", Access = GH_ParamAccess.item };
                tolerance.SetPersistentData(Tolerance.Distance);
                result.Add(new GH_SAMParam(tolerance, ParamVisibility.Voluntary));

                GooSpaceParam spaces = new GooSpaceParam() { Name = "spaces_", NickName = "spaces_", Description = "Optional existing Spaces to match into shell cells. If supplied, matching spaces preserve metadata and names.", Access = GH_ParamAccess.list, Optional = true };
                spaces.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(spaces, ParamVisibility.Voluntary));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "names_", NickName = "names_", Description = "Optional names for shell-derived seed spaces, used when spaces_ is not supplied or not matched.", Access = GH_ParamAccess.list, Optional = true }, ParamVisibility.Voluntary));

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
                result.Add(new GH_SAMParam(new GooAdjacencyClusterParam() { Name = "AdjacencyCluster", NickName = "AdjacencyCluster", Description = "SAM Analytical AdjacencyCluster", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Diagnostics", NickName = "Diagnostics", Description = "Creation diagnostics", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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

            int index_Diagnostics = Params.IndexOfOutputParam("Diagnostics");

            int index;

            bool run = false;
            index = Params.IndexOfInputParam("_run");
            if (index == -1 || !dataAccess.GetData(index, ref run) || !run)
            {
                return;
            }

            List<GH_ObjectWrapper> objectWrappers = new List<GH_ObjectWrapper>();
            index = Params.IndexOfInputParam("_shells");
            if (index == -1 || !dataAccess.GetDataList(index, objectWrappers) || objectWrappers == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid shells");
                return;
            }

            // Tolerances are read up-front because they are needed when assembling SAM Shells
            // from mesh faces below (see the Mesh3D/Rhino Mesh fallback in the parsing loop).
            double fuzzyTolerance = Tolerance.MacroDistance;
            index = Params.IndexOfInputParam("fuzzyTolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref fuzzyTolerance);
            }

            double tolerance = Tolerance.Distance;
            index = Params.IndexOfInputParam("tolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref tolerance);
            }

            // Each list item is treated as one intended space/cell. SAM Shells and closed
            // Rhino Breps/polysurfaces convert directly to a Shell. A Rhino Mesh or SAM Mesh3D
            // is not a closed Brep, so it never resolves to a Shell on its own; instead we pull
            // its triangle Face3Ds and assemble them into a single Shell (one mesh = one volume).
            List<Shell> shells = new List<Shell>();
            int meshShellCount = 0;
            foreach (GH_ObjectWrapper objectWrapper in objectWrappers)
            {
                if (global::SAM.Geometry.Grasshopper.Query.TryGetSAMGeometries(objectWrapper, out List<Shell> shells_Temp) && shells_Temp != null && shells_Temp.Count != 0)
                {
                    shells.AddRange(shells_Temp);
                    continue;
                }

                // Mesh fallback: Face3D extraction handles both a Rhino GH_Mesh (via Convert.ToSAM)
                // and a SAM Mesh3D, returning each mesh triangle as a Face3D.
                if (global::SAM.Geometry.Grasshopper.Query.TryGetSAMGeometries(objectWrapper, out List<Face3D> face3Ds) && face3Ds != null && face3Ds.Count != 0)
                {
                    // Prefer merging coplanar mesh triangles into clean planar faces; if that fails
                    // (e.g. a non-watertight mesh), fall back to the raw triangle faces and let the
                    // OCCT MakerVolume + UnifySameDomain pass merge/heal them.
                    Shell shell = global::SAM.Geometry.Spatial.Create.Shell(face3Ds, fuzzyTolerance, tolerance) ?? new Shell(face3Ds);
                    if (shell != null)
                    {
                        shells.Add(shell);
                        meshShellCount++;
                    }
                }
            }

            List<string> diagnostics = new List<string>();
            if (meshShellCount != 0)
            {
                diagnostics.Add(string.Format("SAM_OCCT_ANALYTICAL_SHELL_MESH_INPUT: Assembled {0} shell(s) from mesh input (Rhino Mesh / SAM Mesh3D).", meshShellCount));
            }
            if (shells.Count == 0)
            {
                diagnostics.Add("SAM_OCCT_ANALYTICAL_SHELL_INPUT_EMPTY: No shells could be built from the supplied input (expected SAM Shells, closed Rhino Breps, Rhino Meshes, or SAM Mesh3Ds).");
                if (index_Diagnostics != -1)
                {
                    dataAccess.SetDataList(index_Diagnostics, diagnostics);
                }
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, diagnostics[0]);
                return;
            }

            double elevationGround = 0;
            index = Params.IndexOfInputParam("elevationGround_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref elevationGround);
            }

            double maxDistance = 0.01;
            index = Params.IndexOfInputParam("maxDistance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref maxDistance);
            }

            double maxAngle = 0.0872664626;
            index = Params.IndexOfInputParam("maxAngle_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref maxAngle);
            }

            double minArea = 0.01;
            index = Params.IndexOfInputParam("minArea_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref minArea);
            }

            List<Space> inputSpaces = new List<Space>();
            index = Params.IndexOfInputParam("spaces_");
            if (index != -1)
            {
                dataAccess.GetDataList(index, inputSpaces);
            }

            List<string> names = new List<string>();
            index = Params.IndexOfInputParam("names_");
            if (index != -1)
            {
                dataAccess.GetDataList(index, names);
            }

            Stopwatch stopwatch_Total = Stopwatch.StartNew();
            Stopwatch stopwatch = Stopwatch.StartNew();
            Log log = new Log();
            diagnostics.Add(string.Format("SAM_OCCT_ANALYTICAL_SHELL_METADATA: Supplied {0} existing space(s) and {1} name(s). Shell-native OCCT path will match supplied spaces after OCCT cell creation and otherwise use names or auto-generated names.", inputSpaces?.Count ?? 0, names?.Count ?? 0));
            AdjacencyCluster adjacencyCluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                shells,
                inputSpaces,
                out OcctCellComplexResult cellComplexResult,
                log,
                new OcctBuildOptions { Tolerance = tolerance, FuzzyTolerance = fuzzyTolerance },
                names,
                minArea: minArea,
                maxAngle: maxAngle);
            diagnostics.Add(string.Format("SAM_OCCT_TIMING_OCCT_AND_ADJACENCY: {0:0.000}s.", stopwatch.Elapsed.TotalSeconds));

            if (cellComplexResult?.Diagnostics != null)
            {
                diagnostics.AddRange(cellComplexResult.Diagnostics.Select(x => x.ToString()));
            }

            stopwatch.Restart();
            if (adjacencyCluster == null)
            {
                diagnostics.Add("SAM_OCCT_ANALYTICAL_SHELL_REBUILD_FAILED: OCCT could not create a valid adjacency cluster from panels extracted from the supplied shells.");
            }
            else
            {
                diagnostics.Add(string.Format("SAM_OCCT_ANALYTICAL_SHELL_SUCCESS: Created adjacency cluster with {0} space(s) and {1} panel(s).", adjacencyCluster.GetSpaces()?.Count ?? 0, adjacencyCluster.GetPanels()?.Count ?? 0));

                adjacencyCluster.Cut(elevationGround, null, tolerance);
                adjacencyCluster.UpdatePanelTypes(elevationGround);
                adjacencyCluster.SetDefaultConstructionByPanelType();
            }
            diagnostics.Add(string.Format("SAM_OCCT_TIMING_POST_PROCESS: {0:0.000}s.", stopwatch.Elapsed.TotalSeconds));
            diagnostics.Add(string.Format("SAM_OCCT_TIMING_TOTAL: {0:0.000}s.", stopwatch_Total.Elapsed.TotalSeconds));

            index = Params.IndexOfOutputParam("AdjacencyCluster");
            if (index != -1)
            {
                dataAccess.SetData(index, adjacencyCluster == null ? null : new GooAdjacencyCluster(adjacencyCluster));
            }

            if (index_Diagnostics != -1)
            {
                dataAccess.SetDataList(index_Diagnostics, diagnostics);
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
