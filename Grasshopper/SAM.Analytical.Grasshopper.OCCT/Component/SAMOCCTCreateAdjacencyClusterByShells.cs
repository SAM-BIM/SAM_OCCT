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
    public class SAMOCCTCreateAdjacencyClusterByShells : GH_SAMVariableOutputParameterComponent
    {
        public override Guid ComponentGuid => new Guid("d6950fec-cea4-4b48-9099-8943a7765e81");

        public override string LatestComponentVersion => "0.6.0";

        protected override System.Drawing.Bitmap Icon => SAMOCCTIcon.SAM_OCCT24;

        public SAMOCCTCreateAdjacencyClusterByShells()
          : base("SAMOCCT.CreateAdjacencyClusterByShells", "SAMOCCT.CreateAdjacencyClusterByShells", "Create a SAM AdjacencyCluster from closed shell space volumes using OCCT cell building", "SAM", "OCCT")
        {
        }

        protected override GH_SAMParam[] Inputs
        {
            get
            {
                List<GH_SAMParam> result = new List<GH_SAMParam>();

                global::Grasshopper.Kernel.Parameters.Param_GenericObject shells = new global::Grasshopper.Kernel.Parameters.Param_GenericObject() { Name = "_shells", NickName = "_shells", Description = "Closed space volumes. Accepts SAM Shells, closed Rhino Breps/polysurfaces, Rhino Meshes, or SAM Mesh3Ds. Each item should represent one intended space/cell: meshes have their faces assembled into a single Shell.", Access = GH_ParamAccess.list };
                shells.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(shells, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number elevationGround = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "elevationGround_", NickName = "elevationGround_", Description = "Ground elevation", Access = GH_ParamAccess.item };
                elevationGround.SetPersistentData(0.0);
                result.Add(new GH_SAMParam(elevationGround, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number maxDistance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "maxDistance_", NickName = "maxDistance_", Description = "Compatibility input for legacy SAM rebuild panel matching. The shell-native direct OCCT topology path normally does not use this value.", Access = GH_ParamAccess.item };
                maxDistance.SetPersistentData(0.01);
                result.Add(new GH_SAMParam(maxDistance, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number maxAngle = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "maxAngle_", NickName = "maxAngle_", Description = "Advanced SAM rebuild panel matching angle", Access = GH_ParamAccess.item };
                maxAngle.SetPersistentData(0.0872664626);
                result.Add(new GH_SAMParam(maxAngle, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number fuzzyTolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "fuzzyTolerance_", NickName = "fuzzyTolerance_", Description = "OCCT fuzzy tolerance. Also used as SAM silver spacing for seed space and shell checks.", Access = GH_ParamAccess.item };
                fuzzyTolerance.SetPersistentData(Tolerance.MacroDistance);
                result.Add(new GH_SAMParam(fuzzyTolerance, ParamVisibility.Voluntary));

                // minArea_ is a POST-build panel filter only (issue #11): every shell face
                // is kept for the OCCT volume build so the cell never opens, then faces below
                // minArea_ are simply not turned into SAM panels. It cannot reopen a shell.
                global::Grasshopper.Kernel.Parameters.Param_Number minArea = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "minArea_", NickName = "minArea_", Description = "Minimum SAM panel area in m². Applied AFTER the OCCT volume is built: every shell face is kept so the cell stays closed, but faces below this area are not turned into SAM panels (e.g. tiny triangulation slivers). Does not reopen shells. Set to 0 to keep every panel.", Access = GH_ParamAccess.item };
                minArea.SetPersistentData(0.01);
                result.Add(new GH_SAMParam(minArea, ParamVisibility.Voluntary));

                global::Grasshopper.Kernel.Parameters.Param_Number tolerance = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "tolerance_", NickName = "tolerance_", Description = "OCCT and SAM model tolerance", Access = GH_ParamAccess.item };
                tolerance.SetPersistentData(Tolerance.Distance);
                result.Add(new GH_SAMParam(tolerance, ParamVisibility.Voluntary));

                // Sewing heals a triangulated / near-touching face soup into shared topology BEFORE
                // MakerVolume, instead of relying on MakerVolume's fuzzy-tolerance guesswork. It is
                // always applied to mesh-derived shells (a mesh is exactly such a soup); this toggle
                // additionally forces it on for SAM Shell / closed Brep input. Bugfix (P1): default
                // flipped to true so the default build recipe matches the solver's own validated
                // options (AvoidInternalShapes=false, SewBeforeBuild=true, SewingTolerance=0.01) - see
                // docs/CELLCOMPLEX_FIRST_HANDOVER.md §A "Diagnosed seam". Still overridable per-run.
                global::Grasshopper.Kernel.Parameters.Param_Boolean sew = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "sew_", NickName = "sew_", Description = "Sew-and-heal faces before the OCCT volume build. Mesh input is always sewn (it is a triangle soup). Default true, matching the solver's own validated build recipe; set false to disable for SAM Shell / closed Brep input that is already watertight.", Access = GH_ParamAccess.item };
                sew.SetPersistentData(true);
                result.Add(new GH_SAMParam(sew, ParamVisibility.Voluntary));

                // meshInput_ tessellates Brep/surface input with Rhino's mesher before building,
                // instead of converting the Brep straight to a SAM Shell. A curved (NURBS) Brep face
                // often converts to self-intersecting, non-watertight planar SAM faces that OCCT
                // cannot close (MakerVolume status 40); a clean planar mesh of the same Brep closes.
                global::Grasshopper.Kernel.Parameters.Param_Boolean meshInput = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "meshInput_", NickName = "meshInput_", Description = "Mesh Brep/surface input (Rhino BRepMesh) into a planar triangle soup before the OCCT build, rather than converting the Brep directly to a SAM Shell. Turn this on when Breps with curved/NURBS faces fail to build a watertight volume. Rhino Meshes and SAM Shells/Mesh3Ds are unaffected. Default false.", Access = GH_ParamAccess.item };
                meshInput.SetPersistentData(false);
                result.Add(new GH_SAMParam(meshInput, ParamVisibility.Binding));

                global::Grasshopper.Kernel.Parameters.Param_Number meshDeflection = new global::Grasshopper.Kernel.Parameters.Param_Number() { Name = "meshDeflection_", NickName = "meshDeflection_", Description = "Max chord deviation (model units) for meshInput_: how far a planar mesh triangle may deviate from the true curved surface. Smaller hugs curvature with more triangles; larger is coarser. Flat faces are unaffected (kept coarse). Default 0.1.", Access = GH_ParamAccess.item };
                meshDeflection.SetPersistentData(0.1);
                result.Add(new GH_SAMParam(meshDeflection, ParamVisibility.Voluntary));

                // weldMesh_ merges coincident-but-duplicate mesh vertices (e.g. an unwelded mesh with
                // 1594 vertices but only ~499 unique positions) into shared topology before the build,
                // at tolerance_, dropping only genuinely degenerate slivers. It cannot fix T-junctions
                // or self-intersections - those need repair at the source mesh.
                global::Grasshopper.Kernel.Parameters.Param_Boolean weldMesh = new global::Grasshopper.Kernel.Parameters.Param_Boolean() { Name = "weldMesh_", NickName = "weldMesh_", Description = "Weld coincident duplicate vertices of mesh input (Rhino Mesh / SAM Mesh3D / meshed Brep) into shared topology at tolerance_ before the build. Cleans unwelded meshes; does not fix T-junctions or self-intersecting faces. Default true.", Access = GH_ParamAccess.item };
                weldMesh.SetPersistentData(true);
                result.Add(new GH_SAMParam(weldMesh, ParamVisibility.Voluntary));

                GooSpaceParam spaces = new GooSpaceParam() { Name = "spaces_", NickName = "spaces_", Description = "Optional existing Spaces to match into shell cells. If supplied, matching spaces preserve metadata and names.", Access = GH_ParamAccess.list, Optional = true };
                spaces.DataMapping = GH_DataMapping.Flatten;
                result.Add(new GH_SAMParam(spaces, ParamVisibility.Voluntary));

                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "names_", NickName = "names_", Description = "Optional names for shell-derived seed spaces, used when spaces_ is not supplied or not matched.", Access = GH_ParamAccess.list, Optional = true }, ParamVisibility.Voluntary));

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
                result.Add(new GH_SAMParam(new GooAdjacencyClusterParam() { Name = "AdjacencyCluster", NickName = "AdjacencyCluster", Description = "SAM Analytical AdjacencyCluster", Access = GH_ParamAccess.item }, ParamVisibility.Binding));
                result.Add(new GH_SAMParam(new global::Grasshopper.Kernel.Parameters.Param_String() { Name = "Diagnostics", NickName = "Diagnostics", Description = "Creation diagnostics", Access = GH_ParamAccess.list }, ParamVisibility.Binding));
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

            int index_Diagnostics = Params.IndexOfOutputParam("Diagnostics");

            int index;

            bool run = false;
            index = Params.IndexOfInputParam("_run");
            if (index == -1 || !dataAccess.GetData(index, ref run) || !run)
            {
                return;
            }

            List<GH_ObjectWrapper> objectWrappers = new List<GH_ObjectWrapper>();
            index = Params.IndexOfInputParam("_shells");
            if (index == -1 || !dataAccess.GetDataList(index, objectWrappers) || objectWrappers == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid shells");
                return;
            }

            // Tolerances are read up-front because they are needed when assembling SAM Shells
            // from mesh faces below (see the Mesh3D/Rhino Mesh fallback in the parsing loop).
            double fuzzyTolerance = Tolerance.MacroDistance;
            index = Params.IndexOfInputParam("fuzzyTolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref fuzzyTolerance);
            }

            double tolerance = Tolerance.Distance;
            index = Params.IndexOfInputParam("tolerance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref tolerance);
            }

            bool sew = true;
            index = Params.IndexOfInputParam("sew_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref sew);
            }

            bool meshInput = false;
            index = Params.IndexOfInputParam("meshInput_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref meshInput);
            }

            double meshDeflection = 0.1;
            index = Params.IndexOfInputParam("meshDeflection_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref meshDeflection);
            }

            bool weldMesh = true;
            index = Params.IndexOfInputParam("weldMesh_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref weldMesh);
            }

            // OcctBuildOptions reused for the watertightness pre-check (tolerance = welding distance)
            // and the cell build below, so both agree on tolerance. Bugfix (P1): AvoidInternalShapes
            // and SewingTolerance now match the solver's own validated build recipe instead of
            // OcctBuildOptions' defaults (true / 0.0) - see docs/CELLCOMPLEX_FIRST_HANDOVER.md §A
            // "Diagnosed seam". SewBeforeBuild is still resolved below from sew_/mesh-input detection.
            OcctBuildOptions buildOptions = new OcctBuildOptions { Tolerance = tolerance, FuzzyTolerance = fuzzyTolerance, AvoidInternalShapes = false, SewingTolerance = 0.01 };

            List<string> diagnostics = new List<string>();

            // Each list item is treated as one intended space/cell. SAM Shells and closed
            // Rhino Breps/polysurfaces convert directly to a Shell. A Rhino Mesh or SAM Mesh3D
            // is not a closed Brep, so it never resolves to a Shell on its own; instead we pull
            // its triangle Face3Ds and assemble them into a single Shell (one mesh = one volume).
            List<Shell> shells = new List<Shell>();
            int meshShellCount = 0;
            int openMeshShellCount = 0;
            int meshedBrepCount = 0;
            foreach (GH_ObjectWrapper objectWrapper in objectWrappers)
            {
                GH_ObjectWrapper effectiveWrapper = objectWrapper;

                // meshInput_: tessellate a Brep/surface up-front with Rhino's mesher (then weld /
                // fill / unify into one watertight mesh) and process the mesh instead of the Brep.
                // This bypasses the Brep -> SAM Shell conversion, which for curved/NURBS faces can
                // produce self-intersecting, non-watertight faces OCCT rejects.
                if (meshInput && TryMeshBrep(objectWrapper?.Value, meshDeflection, out GH_ObjectWrapper meshWrapper, out bool meshIsClosed))
                {
                    effectiveWrapper = meshWrapper;
                    meshedBrepCount++;
                    if (!meshIsClosed)
                    {
                        diagnostics.Add(string.Format("SAM_OCCT_ANALYTICAL_SHELL_MESH_BREP_OPEN: Brep/surface input [{0}] did not mesh into a closed solid even after weld/fill repair. Sewing will still try to close it; if the cell is dropped, the source Brep is likely open or has gaps wider than meshDeflection_.", meshedBrepCount - 1));
                    }
                }
                else if (global::SAM.Geometry.Grasshopper.Query.TryGetSAMGeometries(objectWrapper, out List<Shell> shells_Temp) && shells_Temp != null && shells_Temp.Count != 0)
                {
                    shells.AddRange(shells_Temp);
                    continue;
                }

                // Mesh fallback: Face3D extraction handles both a Rhino GH_Mesh (via Convert.ToSAM)
                // and a SAM Mesh3D, returning each mesh triangle as a Face3D.
                if (global::SAM.Geometry.Grasshopper.Query.TryGetSAMGeometries(effectiveWrapper, out List<Face3D> face3Ds) && face3Ds != null && face3Ds.Count != 0)
                {
                    // Weld pass: a mesh exported unwelded repeats each shared corner once per face
                    // (e.g. 1594 vertices for ~499 unique positions). Rebuild the triangles through a
                    // shared vertex list at tolerance_ so coincident corners become one vertex and
                    // shared edges line up exactly; degenerate slivers are dropped. This does NOT fix
                    // T-junctions or self-intersecting faces - those must be repaired on the source mesh.
                    if (weldMesh)
                    {
                        List<Triangle3D> triangle3Ds = new List<Triangle3D>();
                        foreach (Face3D face3D in face3Ds)
                        {
                            List<Triangle3D> triangle3Ds_Temp = global::SAM.Geometry.Spatial.Query.Triangulate(face3D, tolerance);
                            if (triangle3Ds_Temp != null)
                            {
                                triangle3Ds.AddRange(triangle3Ds_Temp);
                            }
                        }

                        Mesh3D welded = global::SAM.Geometry.Spatial.Create.Mesh3D(triangle3Ds, tolerance);
                        List<Triangle3D> weldedTriangle3Ds = welded?.GetTriangles();
                        if (weldedTriangle3Ds != null && weldedTriangle3Ds.Count != 0)
                        {
                            diagnostics.Add(string.Format("SAM_OCCT_ANALYTICAL_SHELL_MESH_WELD: Welded mesh input [{0}] to {1} shared vertex/vertices; {2} triangle(s) in, {3} after weld.", meshShellCount, welded.PointsCount, face3Ds.Count, weldedTriangle3Ds.Count));
                            face3Ds = weldedTriangle3Ds.ConvertAll(x => new Face3D(x));
                        }
                    }

                    // Prefer merging coplanar mesh triangles into clean planar faces; if that fails
                    // (e.g. a non-watertight mesh), fall back to the raw triangle faces and let the
                    // OCCT MakerVolume + UnifySameDomain pass merge/heal them.
                    Shell shell = global::SAM.Geometry.Spatial.Create.Shell(face3Ds, fuzzyTolerance, tolerance) ?? new Shell(face3Ds);
                    if (shell == null)
                    {
                        continue;
                    }

                    shells.Add(shell);
                    meshShellCount++;

                    // Watertightness pre-check: a mesh that is not a closed volume is the
                    // commonest cause of an opaque OCCT build failure. Report it up-front,
                    // per mesh, so the user knows which input to fix and that sewing will
                    // try to close it.
                    if (global::SAM.Geometry.OCCT.Query.Watertightness(shell.Face3Ds, buildOptions, out int edgeCount, out int nakedEdgeCount, out int nonManifoldEdgeCount) && nakedEdgeCount != 0)
                    {
                        openMeshShellCount++;
                        diagnostics.Add(string.Format("SAM_OCCT_ANALYTICAL_SHELL_MESH_OPEN: Mesh input shell [{0}] is not a closed volume: {1} naked (open) edge(s) of {2} (and {3} non-manifold). Sewing will attempt to close it; if the cell is still dropped, repair the mesh so it is watertight.", meshShellCount - 1, nakedEdgeCount, edgeCount, nonManifoldEdgeCount));
                    }
                }
            }

            if (meshedBrepCount != 0)
            {
                diagnostics.Add(string.Format("SAM_OCCT_ANALYTICAL_SHELL_MESH_BREP: meshInput_ tessellated {0} Brep/surface input(s) with Rhino's mesher (deflection {1:0.######}) before the OCCT build.", meshedBrepCount, meshDeflection));
            }
            if (meshShellCount != 0)
            {
                diagnostics.Add(string.Format("SAM_OCCT_ANALYTICAL_SHELL_MESH_INPUT: Assembled {0} shell(s) from mesh input (Rhino Mesh / SAM Mesh3D / meshed Brep); {1} reported open by the watertightness pre-check.", meshShellCount, openMeshShellCount));
            }

            // Sewing heals a triangulated / near-touching face soup before MakerVolume. A mesh is
            // exactly such a soup, so always sew mesh-derived shells; the sew_ toggle additionally
            // forces sewing for SAM Shell / closed Brep input.
            buildOptions.SewBeforeBuild = sew || meshShellCount != 0;
            if (buildOptions.SewBeforeBuild)
            {
                diagnostics.Add(string.Format("SAM_OCCT_ANALYTICAL_SHELL_SEW: Sew-and-heal before MakerVolume is ON (sew_={0}, mesh input={1}).", sew, meshShellCount != 0));
            }

            if (shells.Count == 0)
            {
                diagnostics.Add("SAM_OCCT_ANALYTICAL_SHELL_INPUT_EMPTY: No shells could be built from the supplied input (expected SAM Shells, closed Rhino Breps, Rhino Meshes, or SAM Mesh3Ds).");
                if (index_Diagnostics != -1)
                {
                    dataAccess.SetDataList(index_Diagnostics, diagnostics);
                }
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, diagnostics[0]);
                return;
            }

            double elevationGround = 0;
            index = Params.IndexOfInputParam("elevationGround_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref elevationGround);
            }

            double maxDistance = 0.01;
            index = Params.IndexOfInputParam("maxDistance_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref maxDistance);
            }

            double maxAngle = 0.0872664626;
            index = Params.IndexOfInputParam("maxAngle_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref maxAngle);
            }

            double minArea = 0.01;
            index = Params.IndexOfInputParam("minArea_");
            if (index != -1)
            {
                dataAccess.GetData(index, ref minArea);
            }

            List<Space> inputSpaces = new List<Space>();
            index = Params.IndexOfInputParam("spaces_");
            if (index != -1)
            {
                dataAccess.GetDataList(index, inputSpaces);
            }

            List<string> names = new List<string>();
            index = Params.IndexOfInputParam("names_");
            if (index != -1)
            {
                dataAccess.GetDataList(index, names);
            }

            Stopwatch stopwatch_Total = Stopwatch.StartNew();
            Stopwatch stopwatch = Stopwatch.StartNew();
            Log log = new Log();
            diagnostics.Add(string.Format("SAM_OCCT_ANALYTICAL_SHELL_METADATA: Supplied {0} existing space(s) and {1} name(s). Shell-native OCCT path will match supplied spaces after OCCT cell creation and otherwise use names or auto-generated names.", inputSpaces?.Count ?? 0, names?.Count ?? 0));
            AdjacencyCluster adjacencyCluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                shells,
                inputSpaces,
                out OcctCellComplexResult cellComplexResult,
                log,
                buildOptions,
                names,
                minArea: minArea,
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

            index = Params.IndexOfOutputParam("AdjacencyCluster");
            if (index != -1)
            {
                dataAccess.SetData(index, adjacencyCluster == null ? null : new GooAdjacencyCluster(adjacencyCluster));
            }

            if (index_Diagnostics != -1)
            {
                dataAccess.SetDataList(index_Diagnostics, diagnostics);
            }

            if (index_Successful != -1)
            {
                dataAccess.SetData(index_Successful, adjacencyCluster != null);
            }

            foreach (string diagnostic in diagnostics)
            {
                AddRuntimeMessage(adjacencyCluster == null ? GH_RuntimeMessageLevel.Warning : GH_RuntimeMessageLevel.Remark, diagnostic);
            }
        }

        /// <summary>
        /// Meshes a Brep/surface input with Rhino's BRepMesh into a single welded triangle
        /// mesh and wraps it as a GH_Mesh, so the existing mesh-to-Shell path handles it.
        /// Returns false when the value is not a Brep/surface (e.g. a SAM Shell or Mesh),
        /// leaving the caller to process it normally. Flat faces stay coarse (SimplePlanes);
        /// curvature is tessellated to within <paramref name="meshDeflection"/>.
        /// </summary>
        private static bool TryMeshBrep(object value, double meshDeflection, out GH_ObjectWrapper meshWrapper, out bool meshIsClosed)
        {
            meshWrapper = null;
            meshIsClosed = false;

            Rhino.Geometry.Brep brep = ToBrep(value);
            if (brep == null)
            {
                return false;
            }

            Rhino.Geometry.MeshingParameters meshingParameters = new Rhino.Geometry.MeshingParameters
            {
                SimplePlanes = true,            // keep flat faces coarse instead of over-triangulating them
                JaggedSeams = false,            // match tessellation across shared edges so the mesh stays watertight
                ClosedObjectPostProcess = true, // help a closed Brep mesh into a closed mesh
                Tolerance = meshDeflection > 0 ? meshDeflection : 0.1
            };

            Rhino.Geometry.Mesh[] meshes = Rhino.Geometry.Mesh.CreateFromBrep(brep, meshingParameters);
            if (meshes == null || meshes.Length == 0)
            {
                return false;
            }

            Rhino.Geometry.Mesh combined = new Rhino.Geometry.Mesh();
            foreach (Rhino.Geometry.Mesh mesh in meshes)
            {
                if (mesh != null)
                {
                    combined.Append(mesh);
                }
            }

            if (combined.Faces.Count == 0)
            {
                return false;
            }

            // Repair pass: Rhino meshes a Brep face-by-face, so the per-face meshes meet at
            // coincident-but-separate vertices and can leave hairline gaps. Weld the seams,
            // fill any small holes, and unify winding so the result is a single watertight
            // mesh the OCCT volume build can close, rather than a soup OCCT must heal.
            combined.Vertices.CombineIdentical(true, true); // weld coincident vertices across faces
            combined.Faces.ConvertQuadsToTriangles();
            combined.FillHoles();                           // close small tessellation gaps along seams
            combined.UnifyNormals();                        // consistent face winding
            combined.RebuildNormals();
            combined.Compact();

            meshIsClosed = combined.IsClosed;

            meshWrapper = new GH_ObjectWrapper(new GH_Mesh(combined));
            return true;
        }

        /// <summary>
        /// Extracts a Rhino Brep from a Grasshopper/Rhino value (GH_Brep, GH_Surface, raw
        /// Brep/Surface, or anything GH can convert to a Brep). Returns null otherwise, so
        /// SAM Shells / Meshes are left for the normal path.
        /// </summary>
        private static Rhino.Geometry.Brep ToBrep(object value)
        {
            switch (value)
            {
                case null:
                    return null;
                case GH_Brep ghBrep:
                    return ghBrep.Value;
                case GH_Surface ghSurface:
                    return ghSurface.Value;
                case Rhino.Geometry.Brep brep:
                    return brep;
                case Rhino.Geometry.Surface surface:
                    return surface.ToBrep();
            }

            Rhino.Geometry.Brep converted = null;
            if (GH_Convert.ToBrep(value, ref converted, GH_Conversion.Both))
            {
                return converted;
            }

            return null;
        }
    }
}
