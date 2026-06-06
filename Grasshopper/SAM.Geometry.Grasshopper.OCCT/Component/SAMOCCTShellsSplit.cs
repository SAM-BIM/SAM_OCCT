// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using SAM.Core;
using SAM.Core.Grasshopper;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Geometry.Grasshopper.OCCT
{
    public class SAMOCCTShellsSplit : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("fb08a424-65d4-4b4e-8f8a-7af2d13de524");

        public override string LatestComponentVersion => "0.1.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTShellsSplit()
          : base("SAMOCCT.ShellsSplit", "SAMOCCT.ShellsSplit", "Split overlapping or touching shell volumes into cleaner adjacent pieces", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                global::Grasshopper.Kernel.Parameters.Param_GenericObject shells = new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_shells", NickName = "_shells", Description = "Closed volumes to split against each other. Accepts SAM Shells or closed Rhino Breps/polysurfaces that convert to SAM Shells.", Access = GH_ParamAccess.list };
                shells.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(shells, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number silverSpacing = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "silverSpacing_", NickName = "silverSpacing_", Description = "Snap tolerance", Access = GH_ParamAccess.item };
                silverSpacing.SetPersistentData(Tolerance.MacroDistance);
                result.Add(new GH_SAMParam(silverSpacing, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number tolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "tolerance_", NickName = "tolerance_", Description = "Tolerance", Access = GH_ParamAccess.item };
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
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "Shells", NickName = "Shells", Description = "Shells split at mutual intersections/shared faces.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Diagnostics", NickName = "Diagnostics", Description = "Diagnostics", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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

            List<GH_ObjectWrapper> shellWrappers = new List<GH_ObjectWrapper>();
            index = Params.IndexOfInputParam("_shells");
            if (index == -1 || !dataAccess.GetDataList(index, shellWrappers))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid shells");
                return;
            }

            double silverSpacing = Tolerance.MacroDistance;
            index = Params.IndexOfInputParam("silverSpacing_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref silverSpacing);
            }

            double tolerance = Tolerance.Distance;
            index = Params.IndexOfInputParam("tolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref tolerance);
            }

            List<Shell> shells = new List<Shell>();
            foreach (GH_ObjectWrapper objectWrapper in shellWrappers)
            {
                if (Query.TryGetSAMGeometries(objectWrapper, out List<Shell> shells_Temp) && shells_Temp != null)
                {
                    shells.AddRange(shells_Temp);
                }
            }

            List<Shell> resultShells = shells.Split(silverSpacing, Tolerance.Angle, tolerance);
            List<string> diagnostics = new List<string>
            {
                string.Format("SAM_OCCT_SPLIT_SUCCESS: Split {0} input shell(s) into {1} shell(s).", shells.Count, resultShells?.Count ?? 0)
            };

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
                dataAccess.SetData(index_Successful, resultShells != null && resultShells.Count != 0);
            }
        }
    }
}
