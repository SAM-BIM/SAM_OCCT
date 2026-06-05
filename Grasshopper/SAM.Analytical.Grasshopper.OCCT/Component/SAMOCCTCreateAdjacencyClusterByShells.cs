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
    public class SAMOCCTCreateAdjacencyClusterByShells : GH_SAMComponent
    {
        public override Guid ComponentGuid => new Guid("d6950fec-cea4-4b48-9099-8943a7765e81");

        public override string LatestComponentVersion => "0.3.0";

        public SAMOCCTCreateAdjacencyClusterByShells()
          : base("SAMOCCT.CreateAdjacencyClusterByShells", "SAMOCCT.CreateAdjacencyClusterByShells", "Create a SAM AdjacencyCluster from closed shell space volumes using OCCT cell building", "SAM", "OCCT")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager inputParamManager)
        {
            int index = inputParamManager.AddGenericParameter("_shells", "_shells", "Closed space volumes. Accepts SAM Shells or closed Rhino Breps/polysurfaces that convert to SAM Shells. One shell should represent one intended space/cell.", GH_ParamAccess.list);
            inputParamManager[index].DataMapping = GH_DataMapping.Flatten;

            inputParamManager.AddNumberParameter("elevationGround_", "elevationGround_", "Ground elevation", GH_ParamAccess.item, 0);
            inputParamManager.AddNumberParameter("maxDistance_", "maxDistance_", "Max panel matching distance", GH_ParamAccess.item, 0.01);
            inputParamManager.AddNumberParameter("maxAngle_", "maxAngle_", "Max panel matching angle", GH_ParamAccess.item, 0.0872664626);
            inputParamManager.AddNumberParameter("silverSpacing_", "silverSpacing_", "OCCT fuzzy tolerance and silver spacing for space computation", GH_ParamAccess.item, Tolerance.MacroDistance);
            inputParamManager.AddNumberParameter("minArea_", "minArea_", "Minimal face area", GH_ParamAccess.item, 0.01);
            inputParamManager.AddNumberParameter("tolerance_", "tolerance_", "Tolerance", GH_ParamAccess.item, Tolerance.Distance);
            GooSpaceParam gooSpaceParam = new GooSpaceParam();
            gooSpaceParam.Optional = true;
            index = inputParamManager.AddParameter(gooSpaceParam, "spaces_", "spaces_", "Optional existing Spaces to match into shell cells. If supplied, matching spaces preserve metadata and names.", GH_ParamAccess.list);
            inputParamManager[index].DataMapping = GH_DataMapping.Flatten;

            index = inputParamManager.AddTextParameter("names_", "names_", "Optional names for shell-derived seed spaces, used when spaces_ is not supplied or not matched.", GH_ParamAccess.list);
            inputParamManager[index].Optional = true;
            inputParamManager.AddBooleanParameter("_run", "_run", "Run", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager outputParamManager)
        {
            outputParamManager.AddParameter(new GooAdjacencyClusterParam(), "AdjacencyCluster", "AdjacencyCluster", "SAM Analytical AdjacencyCluster", GH_ParamAccess.item);
            outputParamManager.AddTextParameter("Diagnostics", "Diagnostics", "Creation diagnostics", GH_ParamAccess.list);
            outputParamManager.AddBooleanParameter("Successful", "Successful", "Run successfully?", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess dataAccess)
        {
            dataAccess.SetData(2, false);

            bool run = false;
            if (!dataAccess.GetData(9, ref run) || !run)
            {
                return;
            }

            List<GH_ObjectWrapper> objectWrappers = new List<GH_ObjectWrapper>();
            if (!dataAccess.GetDataList(0, objectWrappers) || objectWrappers == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid shells");
                return;
            }

            List<Shell> shells = new List<Shell>();
            foreach (GH_ObjectWrapper objectWrapper in objectWrappers)
            {
                if (global::SAM.Geometry.Grasshopper.Query.TryGetSAMGeometries(objectWrapper, out List<Shell> shells_Temp) && shells_Temp != null)
                {
                    shells.AddRange(shells_Temp);
                }
            }

            List<string> diagnostics = new List<string>();
            if (shells.Count == 0)
            {
                diagnostics.Add("SAM_OCCT_ANALYTICAL_SHELL_INPUT_EMPTY: No SAM shells were supplied.");
                dataAccess.SetDataList(1, diagnostics);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, diagnostics[0]);
                return;
            }

            double elevationGround = 0;
            dataAccess.GetData(1, ref elevationGround);

            double maxDistance = 0.01;
            dataAccess.GetData(2, ref maxDistance);

            double maxAngle = 0.0872664626;
            dataAccess.GetData(3, ref maxAngle);

            double silverSpacing = Tolerance.MacroDistance;
            dataAccess.GetData(4, ref silverSpacing);

            double minArea = 0.01;
            dataAccess.GetData(5, ref minArea);

            double tolerance = Tolerance.Distance;
            dataAccess.GetData(6, ref tolerance);

            List<Space> inputSpaces = new List<Space>();
            dataAccess.GetDataList(7, inputSpaces);

            List<string> names = new List<string>();
            dataAccess.GetDataList(8, names);

            Stopwatch stopwatch_Total = Stopwatch.StartNew();
            Stopwatch stopwatch = Stopwatch.StartNew();

            List<Panel> panels = new List<Panel>();
            List<Space> spaces = new List<Space>();
            int count = 1;
            HashSet<Guid> usedSpaceGuids = new HashSet<Guid>();
            HashSet<string> usedNames = new HashSet<string>();

            for (int i = 0; i < shells.Count; i++)
            {
                Shell shell = shells[i];

                List<Panel> panels_Temp = global::SAM.Analytical.Create.Panels(shell, silverSpacing, tolerance);
                if (panels_Temp != null)
                {
                    panels_Temp.RemoveAll(x => x?.GetFace3D() == null || x.GetFace3D().GetArea() < minArea);
                    panels.AddRange(panels_Temp);
                }

                Point3D point3D = shell.InternalPoint3D(silverSpacing, tolerance);
                if (point3D != null)
                {
                    Space space = null;
                    if (inputSpaces != null && inputSpaces.Count != 0)
                    {
                        List<Shell> shells_Temp = new List<Shell>() { shell };
                        foreach (Space inputSpace in inputSpaces)
                        {
                            if (inputSpace == null || usedSpaceGuids.Contains(inputSpace.Guid) || inputSpace.Location == null)
                            {
                                continue;
                            }

                            List<Shell> spaceShells = global::SAM.Analytical.Query.SpaceShells(shells_Temp, inputSpace.Location, silverSpacing, tolerance);
                            if (spaceShells != null && spaceShells.Count != 0)
                            {
                                space = new Space(inputSpace, inputSpace.Name, point3D);
                                usedSpaceGuids.Add(inputSpace.Guid);
                                break;
                            }
                        }
                    }

                    if (space == null)
                    {
                        string name = null;
                        if (names != null && i < names.Count && !string.IsNullOrWhiteSpace(names[i]))
                        {
                            name = names[i];
                        }

                        if (string.IsNullOrWhiteSpace(name))
                        {
                            do
                            {
                                name = string.Format("Cell {0}", count);
                                count++;
                            }
                            while (usedNames.Contains(name));
                        }

                        space = new Space(name, point3D);
                    }

                    usedNames.Add(space.Name);
                    spaces.Add(space);
                }
            }

            diagnostics.Add(string.Format("SAM_OCCT_ANALYTICAL_SHELL_PANELS: Extracted {0} panel(s) and {1} seed space(s) from {2} shell(s).", panels.Count, spaces.Count, shells.Count));
            diagnostics.Add(string.Format("SAM_OCCT_TIMING_PANEL_EXTRACTION: {0:0.000}s.", stopwatch.Elapsed.TotalSeconds));

            Log log = new Log();
            stopwatch.Restart();
            AdjacencyCluster adjacencyCluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                spaces,
                panels,
                out OcctCellComplexResult cellComplexResult,
                log,
                new OcctBuildOptions { Tolerance = tolerance, FuzzyTolerance = silverSpacing },
                thinnessRatio: 0.001,
                minArea: minArea,
                maxDistance: maxDistance,
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

            dataAccess.SetData(0, adjacencyCluster == null ? null : new GooAdjacencyCluster(adjacencyCluster));
            dataAccess.SetDataList(1, diagnostics);
            dataAccess.SetData(2, adjacencyCluster != null);

            foreach (string diagnostic in diagnostics)
            {
                AddRuntimeMessage(adjacencyCluster == null ? GH_RuntimeMessageLevel.Warning : GH_RuntimeMessageLevel.Remark, diagnostic);
            }
        }
    }
}
