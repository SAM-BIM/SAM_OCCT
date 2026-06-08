// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using SAM.Core;
using SAM.Core.Grasshopper;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using Point3D = SAM.Geometry.Spatial.Point3D;

namespace SAM.Geometry.Grasshopper.OCCT
{
    public class SAMOCCTTriangulateSurface : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("8d2f6c1a-4b7e-49a2-9c3f-1e6b5a0d7f42");

        public override string LatestComponentVersion => "0.1.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTTriangulateSurface()
          : base("SAMOCCT.TriangulateSurface", "SAMOCCT.TriangulateSurface", "Triangulate a possibly non-planar surface into planar SAM Face3Ds ready for panelling. Each Face3D is a triangle, so every output is guaranteed planar; edge-length and area controls keep panels from getting too tiny.", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                global::Grasshopper.Kernel.Parameters.Param_GenericObject surfaces = new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_surfaces", NickName = "_surfaces", Description = "Surfaces to triangulate. Accepts Rhino surfaces, Breps/polysurfaces or meshes that may be non-planar (e.g. curved or warped facade surfaces).", Access = GH_ParamAccess.list };
                surfaces.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(surfaces, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number maxEdgeLength = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "maxEdgeLength_", NickName = "maxEdgeLength_", Description = "Target maximum mesh edge length in model units. Drives panel size: larger values give fewer, bigger planar panels. 0 = unlimited (let curvature drive the mesh).", Access = GH_ParamAccess.item };
                maxEdgeLength.SetPersistentData(1.0);
                result.Add(new GH_SAMParam(maxEdgeLength, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number minEdgeLength = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "minEdgeLength_", NickName = "minEdgeLength_", Description = "Minimum mesh edge length in model units. Stops the mesher from creating tiny slivers.", Access = GH_ParamAccess.item };
                minEdgeLength.SetPersistentData(0.1);
                result.Add(new GH_SAMParam(minEdgeLength, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number minArea = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "minArea_", NickName = "minArea_", Description = "Discard any triangle whose area (model units squared) is below this value, so no too-tiny panels are produced.", Access = GH_ParamAccess.item };
                minArea.SetPersistentData(0.01);
                result.Add(new GH_SAMParam(minArea, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number tolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "tolerance_", NickName = "tolerance_", Description = "Tolerance used when validating triangles.", Access = GH_ParamAccess.item };
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
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "Face3Ds", NickName = "Face3Ds", Description = "Planar triangular SAM Face3Ds. Feed these into SAMOCCT.CreateShells / SAMOCCT.PanelsFromShells or any SAM panelling workflow.", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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

            List<GH_ObjectWrapper> surfaceWrappers = new List<GH_ObjectWrapper>();
            index = Params.IndexOfInputParam("_surfaces");
            if (index == -1 || !dataAccess.GetDataList(index, surfaceWrappers) || surfaceWrappers == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid surfaces");
                return;
            }

            double maxEdgeLength = 1.0;
            index = Params.IndexOfInputParam("maxEdgeLength_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref maxEdgeLength);
            }

            double minEdgeLength = 0.1;
            index = Params.IndexOfInputParam("minEdgeLength_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref minEdgeLength);
            }

            double minArea = 0.01;
            index = Params.IndexOfInputParam("minArea_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref minArea);
            }

            double tolerance = Tolerance.Distance;
            index = Params.IndexOfInputParam("tolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref tolerance);
            }

            MeshingParameters meshingParameters = new MeshingParameters
            {
                SimplePlanes = true,
                JaggedSeams = false,
                MinimumEdgeLength = minEdgeLength > 0 ? minEdgeLength : 0.0001,
                MaximumEdgeLength = maxEdgeLength > 0 ? maxEdgeLength : 0.0
            };

            List<Face3D> face3Ds = new List<Face3D>();
            int inputCount = 0;
            int skippedSmall = 0;
            int skippedInvalid = 0;

            foreach (GH_ObjectWrapper objectWrapper in surfaceWrappers)
            {
                List<Mesh> meshes = ToMeshes(objectWrapper?.Value, meshingParameters);
                if (meshes == null || meshes.Count == 0)
                {
                    continue;
                }

                inputCount++;

                foreach (Mesh mesh in meshes)
                {
                    if (mesh == null)
                    {
                        continue;
                    }

                    mesh.Faces.ConvertQuadsToTriangles();

                    for (int i = 0; i < mesh.Faces.Count; i++)
                    {
                        MeshFace meshFace = mesh.Faces[i];

                        Point3f a = mesh.Vertices[meshFace.A];
                        Point3f b = mesh.Vertices[meshFace.B];
                        Point3f c = mesh.Vertices[meshFace.C];

                        Face3D face3D = ToFace3D(a, b, c, tolerance);
                        if (face3D == null)
                        {
                            skippedInvalid++;
                            continue;
                        }

                        if (minArea > 0 && face3D.GetArea() < minArea)
                        {
                            skippedSmall++;
                            continue;
                        }

                        face3Ds.Add(face3D);
                    }
                }
            }

            List<string> diagnostics = new List<string>
            {
                string.Format("SAM_OCCT_TRIANGULATE_SUCCESS: Created {0} planar Face3D(s) from {1} surface(s); skipped {2} below minArea and {3} degenerate triangle(s).", face3Ds.Count, inputCount, skippedSmall, skippedInvalid)
            };

            index = Params.IndexOfOutputParam("Face3Ds");
            if (index != -1)
            {
                dataAccess.SetDataList(index, face3Ds);
            }

            index = Params.IndexOfOutputParam("Diagnostics");
            if (index != -1)
            {
                dataAccess.SetDataList(index, diagnostics);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, face3Ds.Count != 0);
            }
        }

        private static List<Mesh> ToMeshes(object value, MeshingParameters meshingParameters)
        {
            if (value == null)
            {
                return null;
            }

            Brep brep = null;
            Mesh mesh = null;

            switch (value)
            {
                case GH_Mesh ghMesh:
                    mesh = ghMesh.Value;
                    break;
                case GH_Brep ghBrep:
                    brep = ghBrep.Value;
                    break;
                case GH_Surface ghSurface:
                    brep = ghSurface.Value;
                    break;
                case Mesh rhinoMesh:
                    mesh = rhinoMesh;
                    break;
                case Brep rhinoBrep:
                    brep = rhinoBrep;
                    break;
                case Surface rhinoSurface:
                    brep = rhinoSurface.ToBrep();
                    break;
                default:
                    Brep brep_Temp = null;
                    if (GH_Convert.ToBrep(value, ref brep_Temp, GH_Conversion.Both))
                    {
                        brep = brep_Temp;
                    }
                    break;
            }

            if (mesh != null)
            {
                return new List<Mesh> { mesh };
            }

            if (brep == null)
            {
                return null;
            }

            Mesh[] meshes = Mesh.CreateFromBrep(brep, meshingParameters);
            if (meshes == null)
            {
                return null;
            }

            List<Mesh> result = new List<Mesh>();
            foreach (Mesh meshFromBrep in meshes)
            {
                if (meshFromBrep != null)
                {
                    result.Add(meshFromBrep);
                }
            }

            return result;
        }

        private static Face3D ToFace3D(Point3f a, Point3f b, Point3f c, double tolerance)
        {
            Point3D point3D_1 = new Point3D(a.X, a.Y, a.Z);
            Point3D point3D_2 = new Point3D(b.X, b.Y, b.Z);
            Point3D point3D_3 = new Point3D(c.X, c.Y, c.Z);

            if (point3D_1.Distance(point3D_2) <= tolerance || point3D_2.Distance(point3D_3) <= tolerance || point3D_3.Distance(point3D_1) <= tolerance)
            {
                return null;
            }

            try
            {
                Triangle3D triangle3D = new Triangle3D(point3D_1, point3D_2, point3D_3);
                return new Face3D(triangle3D);
            }
            catch
            {
                return null;
            }
        }
    }
}
