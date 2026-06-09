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
    public class SAMOCCTMergeCoplanarFace3Ds : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("a4e1c7d2-3f8b-4c6a-9d21-7b5e2f0a9c34");

        public override string LatestComponentVersion => "0.1.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTMergeCoplanarFace3Ds()
          : base("SAMOCCT.MergeCoplanarFace3Ds", "SAMOCCT.MergeCoplanarFace3Ds", "Merge adjacent coplanar Face3Ds into fewer, larger Face3Ds using the OCCT engine (ShapeUpgrade_UnifySameDomain). Disjoint faces and faces on different planes are kept separate.", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                global::Grasshopper.Kernel.Parameters.Param_GenericObject face3Ds = new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_face3Ds", NickName = "_face3Ds", Description = "Faces to merge. Accepts SAM Face3Ds and geometry that converts to Face3Ds. Adjacent faces sharing an edge and lying on the same plane are unified into one face.", Access = GH_ParamAccess.list };
                face3Ds.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(face3Ds, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number angleTolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "angleTolerance_", NickName = "angleTolerance_", Description = "Maximum angle (radians) between face normals still treated as coplanar. Larger values merge slightly mismatched faces.", Access = GH_ParamAccess.item };
                angleTolerance.SetPersistentData(Tolerance.Angle);
                result.Add(new GH_SAMParam(angleTolerance, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number tolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "tolerance_", NickName = "tolerance_", Description = "Sewing/linear tolerance used to make coincident edges shared before merging.", Access = GH_ParamAccess.item };
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
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "Face3Ds", NickName = "Face3Ds", Description = "Coplanar-merged SAM Face3Ds.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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

            double angleTolerance = Tolerance.Angle;
            index = Params.IndexOfInputParam("angleTolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref angleTolerance);
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

            if (face3Ds.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not convert any input into SAM Face3Ds");
                return;
            }

            List<Face3D> merged = Geometry.OCCT.Query.MergeCoplanarFace3Ds(face3Ds, out OcctCellComplexResult result, angleTolerance, new OcctBuildOptions { Tolerance = tolerance });

            List<string> diagnostics = result?.Diagnostics?.Select(x => x.ToString()).ToList() ?? new List<string>();
            diagnostics.Add(string.Format("SAM_OCCT_MERGE_COPLANAR_FACE3DS: Merged {0} input face(s) into {1} face(s).", face3Ds.Count, merged?.Count ?? 0));

            index = Params.IndexOfOutputParam("Face3Ds");
            if (index != -1)
            {
                dataAccess.SetDataList(index, merged);
            }

            index = Params.IndexOfOutputParam("Diagnostics");
            if (index != -1)
            {
                dataAccess.SetDataList(index, diagnostics);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, merged != null && merged.Count != 0);
            }
        }
    }
}
