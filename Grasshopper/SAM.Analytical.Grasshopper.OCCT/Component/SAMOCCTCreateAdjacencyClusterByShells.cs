// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using SAM.Analytical;
using SAM.Core;
using SAM.Core.Grasshopper;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.Grasshopper.OCCT
{
    public class SAMOCCTCreateAdjacencyClusterByShells : GH_SAMComponent
    {
        public override Guid ComponentGuid => new Guid("d6950fec-cea4-4b48-9099-8943a7765e81");

        public override string LatestComponentVersion => "0.1.0";

        public SAMOCCTCreateAdjacencyClusterByShells()
          : base("SAMOCCT.CreateAdjacencyClusterByShells", "SAMOCCT.CreateAdjacencyClusterByShells", "Create SAM AdjacencyCluster from SAM Shells", "SAM", "OCCT")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager inputParamManager)
        {
            int index = inputParamManager.AddGenericParameter("_shells", "_shells", "SAM Geometry Shells representing spaces", GH_ParamAccess.list);
            inputParamManager[index].DataMapping = GH_DataMapping.Flatten;

            inputParamManager.AddNumberParameter("elevationGround_", "elevationGround_", "Ground elevation", GH_ParamAccess.item, 0);
            inputParamManager.AddNumberParameter("maxDistance_", "maxDistance_", "Max panel matching distance", GH_ParamAccess.item, 0.01);
            inputParamManager.AddNumberParameter("maxAngle_", "maxAngle_", "Max panel matching angle", GH_ParamAccess.item, 0.0872664626);
            inputParamManager.AddNumberParameter("silverSpacing_", "silverSpacing_", "Silver spacing for space computation", GH_ParamAccess.item, Tolerance.MacroDistance);
            inputParamManager.AddNumberParameter("minArea_", "minArea_", "Minimal face area", GH_ParamAccess.item, 0.01);
            inputParamManager.AddNumberParameter("tolerance_", "tolerance_", "Tolerance", GH_ParamAccess.item, Tolerance.Distance);
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
            if (!dataAccess.GetData(7, ref run) || !run)
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

            AdjacencyCluster adjacencyCluster = global::SAM.Analytical.Create.AdjacencyCluster(
                shells,
                elevationGround,
                0.001,
                minArea,
                maxDistance,
                maxAngle,
                silverSpacing,
                tolerance,
                Tolerance.Angle);

            if (adjacencyCluster == null)
            {
                diagnostics.Add("SAM_OCCT_ANALYTICAL_SHELL_REBUILD_FAILED: SAM could not create an adjacency cluster from the supplied shells.");
            }
            else
            {
                diagnostics.Add(string.Format("SAM_OCCT_ANALYTICAL_SHELL_SUCCESS: Created adjacency cluster with {0} space(s) and {1} panel(s).", adjacencyCluster.GetSpaces()?.Count ?? 0, adjacencyCluster.GetPanels()?.Count ?? 0));
            }

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
