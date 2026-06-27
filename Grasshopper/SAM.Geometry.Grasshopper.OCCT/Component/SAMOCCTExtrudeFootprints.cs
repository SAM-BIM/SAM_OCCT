// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using SAM.Core;
using SAM.Core.Grasshopper;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.Grasshopper.OCCT
{
    public class SAMOCCTExtrudeFootprints : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("23ea0b4e-cea3-4757-9c05-8d21ce44e0b2");

        public override string LatestComponentVersion => "0.1.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTExtrudeFootprints()
          : base("SAMOCCT.ExtrudeFootprints", "SAMOCCT.ExtrudeFootprints", "Extrude planar footprint Face3Ds vertically into closed SAM Shell volumes using OCCT. Built for the draw-floor-outline-plus-storey-height workflow; the shells feed straight into SAMOCCT.CreateShells / MergeSmallShells / CreateAdjacencyCluster.", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                global::Grasshopper.Kernel.Parameters.Param_GenericObject face3Ds = new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_footprints", NickName = "_footprints", Description = "Planar footprint faces to extrude. Accepts SAM Face3Ds and geometry that converts to Face3Ds.", Access = GH_ParamAccess.list };
                face3Ds.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(face3Ds, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number height = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "_height", NickName = "_height", Description = "Vertical extrusion height (extrudes each footprint along +Z).", Access = GH_ParamAccess.item };
                result.Add(new GH_SAMParam(height, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number tolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "tolerance_", NickName = "tolerance_", Description = "OCCT build tolerance", Access = GH_ParamAccess.item };
                tolerance.SetPersistentData(Tolerance.Distance);
                result.Add(new GH_SAMParam(tolerance, ParamVisibility.Voluntary));

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
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "Shells", NickName = "Shells", Description = "Closed shell volumes, one per footprint.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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

            List<GH_ObjectWrapper> objectWrappers = new List<GH_ObjectWrapper>();
            index = Params.IndexOfInputParam("_footprints");
            if (index == -1 || !dataAccess.GetDataList(index, objectWrappers) || objectWrappers == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid footprints");
                return;
            }

            double height = double.NaN;
            index = Params.IndexOfInputParam("_height");
            if (index == -1 || !dataAccess.GetData(index, ref height) || double.IsNaN(height))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid height");
                return;
            }

            double tolerance = Tolerance.Distance;
            index = Params.IndexOfInputParam("tolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref tolerance);
            }

            List<Face3D> face3Ds = new List<Face3D>();
            foreach (GH_ObjectWrapper objectWrapper in objectWrappers)
            {
                if (Query.TryGetSAMGeometries(objectWrapper, out List<Face3D> face3Ds_Temp) && face3Ds_Temp != null)
                {
                    face3Ds.AddRange(face3Ds_Temp);
                }
            }

            List<Shell> resultShells = Geometry.OCCT.Query.ExtrudeFace3Ds(face3Ds, height, out OcctCellComplexResult result, new OcctBuildOptions { Tolerance = tolerance });
            List<string> diagnostics = result?.Diagnostics?.Select(x => x.ToString()).ToList();

            index = Params.IndexOfOutputParam("Shells");
            if (index != -1)
            {
                dataAccess.SetDataList(index, resultShells);
            }

            index = Params.IndexOfOutputParam("Diagnostics");
            if (index != -1)
            {
                dataAccess.SetDataList(index, diagnostics);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, result != null && result.Success && resultShells != null && resultShells.Count != 0);
            }

            if (diagnostics != null)
            {
                foreach (string diagnostic in diagnostics)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, diagnostic);
                }
            }
        }
    }
}
