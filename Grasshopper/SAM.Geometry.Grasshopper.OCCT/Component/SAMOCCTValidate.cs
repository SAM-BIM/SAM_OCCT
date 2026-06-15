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
    public class SAMOCCTValidate : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("2c7e9d14-8a35-4f6b-9c0e-71d5b8e3a4f2");

        public override string LatestComponentVersion => "0.1.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTValidate()
          : base("SAMOCCT.Validate", "SAMOCCT.Validate", "Validate boundary geometry with OCCT (BRepCheck_Analyzer + ShapeAnalysis_FreeBounds + optional BOPAlgo_ArgumentAnalyzer) and locate why it does not close: naked (open) edges, self-intersections and small/invalid faces, each with an XYZ location. Use it to find the gap before SAMOCCT.CreateShells fails to build a watertight volume (issue #37).", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_geometry", NickName = "_geometry", Description = "Geometry to validate. Accepts SAM Face3Ds / Shells and geometry that converts to Face3Ds (it is sewn into a shell before the kernel analysers run).", Access = GH_ParamAccess.list }, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Boolean checkSelfIntersections = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "checkSelfIntersections_", NickName = "checkSelfIntersections_", Description = "When true (default) the costly BOPAlgo_ArgumentAnalyzer self-intersection / small-edge test runs in addition to the validity and watertightness checks.", Access = GH_ParamAccess.item };
                checkSelfIntersections.SetPersistentData(true);
                result.Add(new GH_SAMParam(checkSelfIntersections, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number tolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "tolerance_", NickName = "tolerance_", Description = "OCCT tolerance used by the analysers (also the small-face area threshold).", Access = GH_ParamAccess.item };
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
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "IsValid", NickName = "IsValid", Description = "True when the geometry is topologically valid (no invalid faces / self-intersections).", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "IsWatertight", NickName = "IsWatertight", Description = "True when there are no naked (free) boundary edges - i.e. no gaps.", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "IssueLocations", NickName = "IssueLocations", Description = "A representative point for each located issue (naked edge midpoint, face centroid, etc.).", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Issues", NickName = "Issues", Description = "A description of each located issue (category, size and location), aligned with IssueLocations.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Diagnostics", NickName = "Diagnostics", Description = "OCCT diagnostics", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "Successful", NickName = "Successful", Description = "Run successfully (a report was produced)?", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
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
            index = Params.IndexOfInputParam("_geometry");
            if (index == -1 || !dataAccess.GetDataList(index, objectWrappers) || objectWrappers == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid data");
                return;
            }

            bool checkSelfIntersections = true;
            index = Params.IndexOfInputParam("checkSelfIntersections_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref checkSelfIntersections);
            }

            double tolerance = Tolerance.Distance;
            index = Params.IndexOfInputParam("tolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref tolerance);
            }

            // Accept shells or loose faces - both reduce to a face soup the
            // validator sews and analyses.
            List<Face3D> face3Ds = new List<Face3D>();
            foreach (GH_ObjectWrapper objectWrapper in objectWrappers)
            {
                if (Query.TryGetSAMGeometries(objectWrapper, out List<Shell> shells_Temp) && shells_Temp != null)
                {
                    foreach (Shell shell in shells_Temp)
                    {
                        List<Face3D> shellFace3Ds = shell?.Face3Ds;
                        if (shellFace3Ds != null)
                        {
                            face3Ds.AddRange(shellFace3Ds);
                        }
                    }
                }
                else if (Query.TryGetSAMGeometries(objectWrapper, out List<Face3D> face3Ds_Temp) && face3Ds_Temp != null)
                {
                    face3Ds.AddRange(face3Ds_Temp);
                }
            }

            bool success = Geometry.OCCT.Query.Validate(face3Ds, out OcctValidationReport report, out OcctCellComplexResult result, new OcctBuildOptions { Tolerance = tolerance }, checkSelfIntersections);
            List<string> diagnostics = result?.Diagnostics?.Select(x => x.ToString()).ToList();

            index = Params.IndexOfOutputParam("IsValid");
            if (index != -1)
            {
                dataAccess.SetData(index, report != null && report.IsValid);
            }

            index = Params.IndexOfOutputParam("IsWatertight");
            if (index != -1)
            {
                dataAccess.SetData(index, report != null && report.IsWatertight);
            }

            index = Params.IndexOfOutputParam("IssueLocations");
            if (index != -1)
            {
                dataAccess.SetDataList(index, report?.Issues?.Select(x => x.Location).ToList());
            }

            index = Params.IndexOfOutputParam("Issues");
            if (index != -1)
            {
                dataAccess.SetDataList(index, report?.Issues?.Select(x => x.ToString()).ToList());
            }

            index = Params.IndexOfOutputParam("Diagnostics");
            if (index != -1)
            {
                dataAccess.SetDataList(index, diagnostics);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, success);
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
