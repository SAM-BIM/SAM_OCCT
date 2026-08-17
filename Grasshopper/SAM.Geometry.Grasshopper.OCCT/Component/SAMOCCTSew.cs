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
    public class SAMOCCTSew : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("8d2f6b41-5c0a-4e93-9a17-2b6e7d4c1f80");

        public override string LatestComponentVersion => "0.1.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTSew()
          : base("SAMOCCT.Sew", "SAMOCCT.Sew", "Sew a face soup into the tightest closed SAM Shell using OCCT (BRepBuilderAPI_Sewing + ShapeFix). Unlike SAMOCCT.CreateShells this does not need the faces to already bound a volume - it closes triangulated / near-touching faces that a direct build leaves open (issue #37).", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_face3Ds", NickName = "_face3Ds", Description = "Boundary faces/surfaces to sew into a closed volume. Accepts SAM Face3Ds and geometry that converts to Face3Ds.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number sewingTolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "sewingTolerance_", NickName = "sewingTolerance_", Description = "OCCT sewing tolerance [m]: coincident / near-touching face edges within this distance are joined into shared topology. Increase it to bridge larger gaps between faces.", Access = GH_ParamAccess.item };
                sewingTolerance.SetPersistentData(Tolerance.MacroDistance);
                result.Add(new GH_SAMParam(sewingTolerance, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Boolean makeSolid = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "makeSolid_", NickName = "makeSolid_", Description = "When true (default) each healed closed shell becomes a solid volume. Set false only to return a healed (possibly still open) shell.", Access = GH_ParamAccess.item };
                makeSolid.SetPersistentData(true);
                result.Add(new GH_SAMParam(makeSolid, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number tolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "tolerance_", NickName = "tolerance_", Description = "OCCT build tolerance [m] (face topology key quantization).", Access = GH_ParamAccess.item };
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
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "Shells", NickName = "Shells", Description = "Closed SAM Shell volumes produced by sewing and healing the faces.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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
            index = Params.IndexOfInputParam("_face3Ds");
            if (index == -1 || !dataAccess.GetDataList(index, objectWrappers) || objectWrappers == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            double sewingTolerance = Tolerance.MacroDistance;
            index = Params.IndexOfInputParam("sewingTolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref sewingTolerance);
            }

            bool makeSolid = true;
            index = Params.IndexOfInputParam("makeSolid_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref makeSolid);
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

            List<Shell> shells = Geometry.OCCT.Query.Sew(face3Ds, out OcctCellComplexResult result, new OcctBuildOptions { Tolerance = tolerance, SewingTolerance = sewingTolerance }, makeSolid);
            List<string> diagnostics = result?.Diagnostics?.Select(x => x.ToString()).ToList();

            index = Params.IndexOfOutputParam("Shells");
            if (index != -1)
            {
                dataAccess.SetDataList(index, shells);
            }

            index = Params.IndexOfOutputParam("Diagnostics");
            if (index != -1)
            {
                dataAccess.SetDataList(index, diagnostics);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, result != null && result.Success);
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
