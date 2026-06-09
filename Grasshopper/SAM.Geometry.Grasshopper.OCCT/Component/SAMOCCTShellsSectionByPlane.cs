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
    public class SAMOCCTShellsSectionByPlane : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("67f00e15-259c-4080-8b34-efb4cd59cb8c");

        public override string LatestComponentVersion => "0.3.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTShellsSectionByPlane()
          : base("SAMOCCT.ShellsSectionByPlane", "SAMOCCT.ShellsSectionByPlane", "Create section Face3Ds and split SAM Shells by plane", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                global::Grasshopper.Kernel.Parameters.Param_GenericObject shells = new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_shells", NickName = "_shells", Description = "Closed volumes to section and split. Accepts SAM Shells or closed Rhino Breps/polysurfaces that convert to SAM Shells.", Access = GH_ParamAccess.list };
                shells.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(shells, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_GenericObject planes = new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "planes_", NickName = "planes_", Description = "One or more SAM/Rhino planes to section by. Supply many level planes to cut a shell into many levels in one go. Uses the shell centroid XY plane if none are supplied.", Access = GH_ParamAccess.list, Optional = true };
                planes.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(planes, ParamVisibility.Binding));

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
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "Face3Ds", NickName = "Face3Ds", Description = "Section Face3Ds created where each shell crosses the plane.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "Shells", NickName = "Shells", Description = "Input shells split by the section plane. Use these directly for atrium/level division workflows.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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

            List<Plane> planes = new List<Plane>();
            index = Params.IndexOfInputParam("planes_");
            if (index != -1)
            {
                List<GH_ObjectWrapper> planeWrappers = new List<GH_ObjectWrapper>();
                if (dataAccess.GetDataList(index, planeWrappers))
                {
                    foreach (GH_ObjectWrapper planeWrapper in planeWrappers)
                    {
                        if (planeWrapper != null && Query.TryGetSAMGeometries(planeWrapper, out List<Plane> planes_Temp) && planes_Temp != null)
                        {
                            planes.AddRange(planes_Temp.Where(x => x != null));
                        }
                    }
                }
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

            List<Face3D> face3Ds = new List<Face3D>();
            List<Shell> shells_Split = new List<Shell>();
            List<string> occtDiagnostics = new List<string>();
            foreach (Shell shell in shells)
            {
                BoundingBox3D boundingBox3D = shell?.GetBoundingBox();
                if (boundingBox3D == null)
                {
                    continue;
                }

                List<Plane> planes_Temp = planes.Count != 0 ? planes : new List<Plane> { new Plane(boundingBox3D.GetCentroid(), Vector3D.WorldZ) };

                // Section and split with the OCCT engine in one BOPAlgo_MakerVolume pass: the
                // shell's boundary faces plus a bounded patch per plane are partitioned into the
                // level cells, and the on-plane cell faces - already trimmed to the shell by
                // OCCT - are the section Face3Ds. No managed SAM sectioning is involved.
                List<Shell> levels = Geometry.OCCT.Query.ShellSectionByPlanes(shell, planes_Temp, out List<Face3D> sectionFace3Ds, out OcctCellComplexResult result, new OcctBuildOptions { Tolerance = tolerance });
                if (result?.Diagnostics != null)
                {
                    occtDiagnostics.AddRange(result.Diagnostics.Select(x => x.ToString()));
                }

                if (levels != null && levels.Count != 0)
                {
                    if (sectionFace3Ds != null)
                    {
                        face3Ds.AddRange(sectionFace3Ds);
                    }

                    shells_Split.AddRange(levels);
                    continue;
                }

                // OCCT could not build cells (e.g. native unavailable); fall back to the managed
                // SAM Shell.Section for the section faces and keep the original shell.
                occtDiagnostics.Add("SAM_OCCT_SECTION_MANAGED_FALLBACK: OCCT section unavailable. Using managed SAM Shell.Section and keeping the original shell.");
                foreach (Plane plane_Temp in planes_Temp)
                {
                    List<Face3D> face3Ds_Temp = shell.Section(plane_Temp, true, Tolerance.Angle, tolerance, Tolerance.MacroDistance);
                    if (face3Ds_Temp != null)
                    {
                        face3Ds.AddRange(face3Ds_Temp.Where(x => x != null));
                    }
                }

                shells_Split.Add(new Shell(shell));
            }

            List<string> diagnostics = new List<string>
            {
                string.Format("SAM_OCCT_SECTION_SUCCESS: Created {0} section Face3D(s) and {1} level shell(s).", face3Ds.Count, shells_Split.Count)
            };
            diagnostics.AddRange(occtDiagnostics);

            index = Params.IndexOfOutputParam("Face3Ds");
            if (index != -1)
            {
                dataAccess.SetDataList(index, face3Ds);
            }

            index = Params.IndexOfOutputParam("Shells");
            if (index != -1)
            {
                dataAccess.SetDataList(index, shells_Split);
            }

            index = Params.IndexOfOutputParam("Diagnostics");
            if (index != -1)
            {
                dataAccess.SetDataList(index, diagnostics);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, face3Ds.Count != 0 || shells_Split.Count != 0);
            }
        }
    }
}
