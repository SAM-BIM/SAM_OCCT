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
    public class SAMOCCTCreateShells : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("89f9c336-e4dc-4908-bb87-b7b10ee84245");

        public override string LatestComponentVersion => "0.3.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTCreateShells()
          : base("SAMOCCT.CreateShells", "SAMOCCT.CreateShells", "Create closed SAM Shell volumes from Face3D/surface boundary geometry using OCCT", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_face3Ds", NickName = "_face3Ds", Description = "Boundary faces/surfaces used to form closed volumes. Accepts SAM Face3Ds and geometry that converts to Face3Ds; use this before shell booleans when you only have surfaces.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number tolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "tolerance_", NickName = "tolerance_", Description = "OCCT build tolerance", Access = GH_ParamAccess.item };
                tolerance.SetPersistentData(Tolerance.Distance);
                result.Add(new GH_SAMParam(tolerance, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number fuzzyTolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "fuzzyTolerance_", NickName = "fuzzyTolerance_", Description = "OCCT fuzzy tolerance", Access = GH_ParamAccess.item };
                fuzzyTolerance.SetPersistentData(Tolerance.MacroDistance);
                result.Add(new GH_SAMParam(fuzzyTolerance, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Boolean sewBeforeBuild = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "sewBeforeBuild_", NickName = "sewBeforeBuild_", Description = "When true, the faces are first sewn+healed (BRepBuilderAPI_Sewing + ShapeFix) and only then closed by MakerVolume (issue #37), so triangulated / near-touching faces close instead of relying on the fuzzy tolerance alone. A hard MakerVolume failure also auto-retries via sew regardless of this flag, using sewingTolerance_.", Access = GH_ParamAccess.item };
                sewBeforeBuild.SetPersistentData(false);
                result.Add(new GH_SAMParam(sewBeforeBuild, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number sewingTolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "sewingTolerance_", NickName = "sewingTolerance_", Description = "OCCT sewing tolerance used by sewBeforeBuild_ and by the automatic sew-then-rebuild retry: coincident / near-touching face edges within this distance are joined. Leave 0 to fall back to tolerance_.", Access = GH_ParamAccess.item };
                sewingTolerance.SetPersistentData(0.0);
                result.Add(new GH_SAMParam(sewingTolerance, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Integer glueMode = new global::Grasshopper.Kernel.Parameters.Param_Integer() { Name = "glueMode_", NickName = "glueMode_", Description = "BOP glue mode for cell complexes with many coincident shared walls (issue #37): 0 = off (default), 1 = shift, 2 = full. Glue is a throughput win but corrupts merely-near-coincident faces, so it is applied ONLY when a watertightness check finds no gaps (otherwise it degrades to the glue-off path with a diagnostic).", Access = GH_ParamAccess.item };
                glueMode.SetPersistentData(0);
                result.Add(new GH_SAMParam(glueMode, ParamVisibility.Voluntary));

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
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "Shells", NickName = "Shells", Description = "Closed SAM Shell volumes found by OCCT.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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

            bool sewBeforeBuild = false;
            index = Params.IndexOfInputParam("sewBeforeBuild_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref sewBeforeBuild);
            }

            double sewingTolerance = 0.0;
            index = Params.IndexOfInputParam("sewingTolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref sewingTolerance);
            }

            int glueMode = 0;
            index = Params.IndexOfInputParam("glueMode_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref glueMode);
            }

            OcctGlueMode occtGlueMode = glueMode == 2 ? OcctGlueMode.Full : glueMode == 1 ? OcctGlueMode.Shift : OcctGlueMode.Off;

            List<Face3D> face3Ds = new List<Face3D>();
            foreach (GH_ObjectWrapper objectWrapper in objectWrappers)
            {
                if (Query.TryGetSAMGeometries(objectWrapper, out List<Face3D> face3Ds_Temp) && face3Ds_Temp != null)
                {
                    face3Ds.AddRange(face3Ds_Temp);
                }
            }

            List<Shell> shells = Geometry.OCCT.Create.Shells(face3Ds, out OcctCellComplexResult result, new OcctBuildOptions { Tolerance = tolerance, FuzzyTolerance = fuzzyTolerance, SewBeforeBuild = sewBeforeBuild, SewingTolerance = sewingTolerance, GlueMode = occtGlueMode });
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
