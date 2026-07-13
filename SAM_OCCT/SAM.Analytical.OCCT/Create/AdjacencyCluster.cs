// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SAM.Analytical.OCCT
{
    public static partial class Create
    {
        public static AdjacencyCluster AdjacencyCluster(IEnumerable<Space> spaces, IEnumerable<Panel> panels, out OcctCellComplexResult cellComplexResult, Log log = null, OcctBuildOptions options = null, double thinnessRatio = 0.01, double minArea = Tolerance.MacroDistance, double maxDistance = 0.1, double maxAngle = 0.0872664626)
        {
            cellComplexResult = null;

            List<Panel> panels_Temp = panels?.Where(x => x != null).ToList();
            if (panels_Temp == null || panels_Temp.Count == 0)
            {
                cellComplexResult = new OcctCellComplexResult();
                cellComplexResult.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_ANALYTICAL_INPUT_EMPTY", "No panels were supplied for adjacency cluster creation.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);
            List<Space> spaces_Temp = spaces?.Where(x => x != null).ToList();

            Stopwatch stopwatch = Stopwatch.StartNew();
            List<Shell> shells = Geometry.OCCT.Create.CellComplexByPanels(panels_Temp, out cellComplexResult, options);
            cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TIMING_NATIVE_CELL_BUILD", string.Format("OCCT cell build and decode took {0:0.000}s.", stopwatch.Elapsed.TotalSeconds));
            if (shells == null || shells.Count == 0)
            {
                if (log != null && cellComplexResult?.Diagnostics != null)
                {
                    foreach (OcctDiagnostic diagnostic in cellComplexResult.Diagnostics)
                    {
                        Core.Modify.Add(log, diagnostic.ToString());
                    }
                }

                return null;
            }

            cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_ANALYTICAL_INPUT", string.Format("Received {0} panel(s) and {1} seed space(s) for OCCT adjacency creation.", panels_Temp.Count, spaces_Temp?.Count ?? 0));

            if (log != null)
            {
                Core.Modify.Add(log, "OCCT created {0} closed shell(s). Building SAM adjacency cluster.", shells.Count);
            }

            cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_ANALYTICAL_BUILD", string.Format("Building SAM adjacency cluster from {0} OCCT shell(s).", shells.Count));
            if (spaces_Temp != null && spaces_Temp.Count != 0 && spaces_Temp.Count != shells.Count)
            {
                cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_ANALYTICAL_CELL_SPACE_DELTA", string.Format("Seed space count ({0}) differs from OCCT cell count ({1}). This usually means OCCT merged, rejected, or could not close at least one intended cell.", spaces_Temp.Count, shells.Count));
            }

            stopwatch.Restart();
            AdjacencyCluster adjacencyCluster = DirectAdjacencyCluster(cellComplexResult, spaces_Temp, options, minArea, maxAngle);
            if (adjacencyCluster != null)
            {
                cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TIMING_DIRECT_REBUILD", string.Format("Direct SAM topology rebuild took {0:0.000}s.", stopwatch.Elapsed.TotalSeconds));
                int directSpaceCount = adjacencyCluster.GetSpaces()?.Count ?? 0;
                int directPanelCount = adjacencyCluster.GetPanels()?.Count ?? 0;
                cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_ANALYTICAL_DIRECT_SUCCESS", string.Format("Created SAM adjacency cluster directly from OCCT topology with {0} space(s), {1} panel(s), and {2} shared face relation(s).", directSpaceCount, directPanelCount, cellComplexResult.FaceAdjacencies?.Count ?? 0));
                return adjacencyCluster;
            }

            cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TIMING_DIRECT_REBUILD", string.Format("Direct SAM topology rebuild attempt took {0:0.000}s.", stopwatch.Elapsed.TotalSeconds));
            cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_ANALYTICAL_DIRECT_FALLBACK", "Direct OCCT topology rebuild failed or had no usable cell-face topology. Falling back to SAM geometric rebuild.");

            stopwatch.Restart();
            adjacencyCluster = global::SAM.Analytical.Create.AdjacencyCluster(
                shells,
                spaces_Temp,
                panels_Temp,
                addMissingSpaces: true,
                addMissingPanels: true,
                thinnessRatio: thinnessRatio,
                minArea: minArea,
                maxDistance: maxDistance,
                maxAngle: maxAngle,
                silverSpacing: options.FuzzyTolerance,
                tolerance_Distance: options.Tolerance);
            cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TIMING_GEOMETRIC_REBUILD", string.Format("Fallback SAM geometric rebuild took {0:0.000}s.", stopwatch.Elapsed.TotalSeconds));

            if (log != null)
            {
                if (adjacencyCluster == null)
                {
                    Core.Modify.Add(log, "SAM adjacency cluster rebuild failed.");
                }
                else
                {
                    Core.Modify.Add(log, "SAM adjacency cluster created with {0} space(s) and {1} panel(s).", adjacencyCluster.GetSpaces()?.Count ?? 0, adjacencyCluster.GetPanels()?.Count ?? 0);
                }
            }

            if (adjacencyCluster == null)
            {
                cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_ANALYTICAL_REBUILD_FAILED", "OCCT produced closed shells, but SAM could not rebuild an adjacency cluster from them.");
            }
            else
            {
                int adjacencySpaceCount = adjacencyCluster.GetSpaces()?.Count ?? 0;
                int adjacencyPanelCount = adjacencyCluster.GetPanels()?.Count ?? 0;
                cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_ANALYTICAL_SUCCESS", string.Format("Created SAM adjacency cluster with {0} space(s) and {1} panel(s).", adjacencySpaceCount, adjacencyPanelCount));

                if (adjacencySpaceCount != shells.Count)
                {
                    cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_ANALYTICAL_REBUILD_SPACE_DELTA", string.Format("SAM adjacency space count ({0}) differs from OCCT shell count ({1}). This usually means SAM merged, rejected, or could not assign one or more rebuilt cells.", adjacencySpaceCount, shells.Count));
                }
            }

            return adjacencyCluster;
        }

        public static AdjacencyCluster AdjacencyCluster(IEnumerable<Shell> shells, IEnumerable<Space> spaces, out OcctCellComplexResult cellComplexResult, Log log = null, OcctBuildOptions options = null, IEnumerable<string> names = null, double minArea = Tolerance.MacroDistance, double maxAngle = 0.0872664626)
        {
            cellComplexResult = null;

            List<Shell> shells_Temp = shells?.Where(x => x != null).ToList();
            if (shells_Temp == null || shells_Temp.Count == 0)
            {
                cellComplexResult = new OcctCellComplexResult();
                cellComplexResult.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_ANALYTICAL_SHELL_INPUT_EMPTY", "No shells were supplied for adjacency cluster creation.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);
            List<Space> spaces_Temp = spaces?.Where(x => x != null).ToList();
            List<string> names_Temp = names?.ToList();

            Stopwatch stopwatch = Stopwatch.StartNew();
            List<Face3D> face3Ds = new List<Face3D>();
            int smallFacesKept = 0;
            foreach (Shell shell in shells_Temp)
            {
                List<Face3D> face3Ds_Temp = shell.Face3Ds;
                if (face3Ds_Temp == null)
                {
                    continue;
                }

                foreach (Face3D face3D in face3Ds_Temp)
                {
                    if (face3D == null)
                    {
                        continue;
                    }

                    // These shells are already closed volumes, so every face is load-bearing:
                    // dropping a small-but-valid face punches a hole and OCCT can no longer
                    // build the cell (issue #11). Only skip genuinely degenerate faces that
                    // OCCT could not turn into a face anyway. Sliver removal that preserves
                    // closure is ShellsRepair's job (OCCT defeaturing extends the neighbours),
                    // never a naive area filter here.
                    double area = face3D.GetArea();
                    if (double.IsNaN(area) || area <= options.Tolerance)
                    {
                        continue;
                    }

                    if (area < minArea)
                    {
                        smallFacesKept++;
                    }

                    face3Ds.Add(face3D);
                }
            }
            cellComplexResult = new OcctCellComplexResult();
            cellComplexResult.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_ANALYTICAL_SHELL_FACES", string.Format("Extracted {0} face(s) directly from {1} shell(s).", face3Ds.Count, shells_Temp.Count));
            if (smallFacesKept > 0)
            {
                cellComplexResult.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_ANALYTICAL_SHELL_SMALL_FACES_KEPT", string.Format("Kept {0} face(s) below minArea ({1:0.######} m^2) for the OCCT volume build so the cell stays closed; they are excluded from SAM panels by minArea afterwards. (Dropping them before the build would open the shell.)", smallFacesKept, minArea));
            }
            cellComplexResult.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TIMING_SHELL_FACE_EXTRACTION", string.Format("Shell face extraction took {0:0.000}s.", stopwatch.Elapsed.TotalSeconds));

            if (face3Ds.Count == 0)
            {
                cellComplexResult.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_ANALYTICAL_SHELL_NO_FACES", "No usable shell Face3D geometry could be extracted.");
                return null;
            }

            stopwatch.Restart();
            List<Shell> occtShells = Geometry.OCCT.Create.Shells(face3Ds, out cellComplexResult, options);
            cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TIMING_NATIVE_CELL_BUILD", string.Format("OCCT cell build and decode took {0:0.000}s.", stopwatch.Elapsed.TotalSeconds));
            if (occtShells == null || occtShells.Count == 0)
            {
                if (log != null && cellComplexResult?.Diagnostics != null)
                {
                    foreach (OcctDiagnostic diagnostic in cellComplexResult.Diagnostics)
                    {
                        Core.Modify.Add(log, diagnostic.ToString());
                    }
                }

                return null;
            }

            cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_ANALYTICAL_INPUT", string.Format("Received {0} shell face(s), {1} seed space(s), and {2} name(s) for OCCT adjacency creation.", face3Ds.Count, spaces_Temp?.Count ?? 0, names_Temp?.Count ?? 0));
            cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_ANALYTICAL_BUILD", string.Format("Building SAM adjacency cluster from {0} OCCT shell(s).", occtShells.Count));
            if (spaces_Temp != null && spaces_Temp.Count != 0 && spaces_Temp.Count != occtShells.Count)
            {
                cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_ANALYTICAL_CELL_SPACE_DELTA", string.Format("Seed space count ({0}) differs from OCCT cell count ({1}). This usually means OCCT merged, rejected, or could not close at least one intended cell.", spaces_Temp.Count, occtShells.Count));
            }
            else if ((spaces_Temp == null || spaces_Temp.Count == 0) && shells_Temp.Count != occtShells.Count)
            {
                cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_ANALYTICAL_CELL_SPACE_DELTA", string.Format("Input shell count ({0}) differs from OCCT cell count ({1}). This usually means OCCT merged, rejected, or could not close at least one intended cell.", shells_Temp.Count, occtShells.Count));
            }

            // When the combined MakerVolume rebuild produced a different number of
            // cells than the input shells, log exactly which input shells were not
            // reproduced so the dropped levels can be identified. See issue #11.
            if (shells_Temp.Count != occtShells.Count)
            {
                LogShellRebuildAttribution(shells_Temp, occtShells, options, cellComplexResult);
            }

            stopwatch.Restart();
            AdjacencyCluster adjacencyCluster = DirectAdjacencyCluster(cellComplexResult, spaces_Temp, options, minArea, maxAngle, names_Temp);
            if (adjacencyCluster == null)
            {
                cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_ANALYTICAL_DIRECT_FAILED", "OCCT produced closed shells, but the direct SAM topology rebuild failed.");
                return null;
            }

            cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TIMING_DIRECT_REBUILD", string.Format("Direct SAM topology rebuild took {0:0.000}s.", stopwatch.Elapsed.TotalSeconds));
            int directSpaceCount = adjacencyCluster.GetSpaces()?.Count ?? 0;
            int directPanelCount = adjacencyCluster.GetPanels()?.Count ?? 0;
            cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_ANALYTICAL_DIRECT_SUCCESS", string.Format("Created SAM adjacency cluster directly from OCCT topology with {0} space(s), {1} panel(s), and {2} shared face relation(s).", directSpaceCount, directPanelCount, cellComplexResult.FaceAdjacencies?.Count ?? 0));
            return adjacencyCluster;
        }

        /// <summary>
        /// Builds a SAM <see cref="AdjacencyCluster"/> directly from a <see cref="ResolvedCellComplex"/> the
        /// solver already validated (Phase P2), with NO native rebuild - one space per cell, one panel per
        /// unique cell face (deduped per-decode <see cref="ResolvedCellFace.TopologyKey"/>), relations from
        /// each face's owner cells. Panel identity (construction/type/Guid) is inherited from a supplied
        /// <paramref name="panels"/> ONLY on an unambiguous geometric match (exactly one coplanar panel whose
        /// face contains the cell face's interior point); a zero or ambiguous match falls back to a default
        /// construction/type and is reported (<c>SAM_OCCT_ANALYTICAL_PANEL_IDENTITY</c>), never silently
        /// mis-attributed. Returns null when the complex has no cells or no relations form.
        /// </summary>
        public static AdjacencyCluster AdjacencyCluster(IEnumerable<Panel> panels, ResolvedCellComplex resolvedCellComplex, out List<string> diagnostics, double minArea = Tolerance.MacroDistance, double maxAngle = 0.0872664626, double tolerance = Tolerance.Distance, double fuzzyTolerance = Tolerance.MacroDistance, IEnumerable<int> excludeCellIndices = null)
        {
            diagnostics = new List<string>();

            if (resolvedCellComplex?.Cells == null || resolvedCellComplex.Cells.Count == 0)
            {
                diagnostics.Add("SAM_OCCT_ANALYTICAL_COMPLEX_EMPTY: The supplied ResolvedCellComplex has no cells.");
                return null;
            }

            List<Panel> panels_Temp = panels?.Where(x => x != null && x.GetFace3D() != null && x.GetFace3D().IsValid()).ToList() ?? new List<Panel>();

            // Cells to omit (e.g. non-Interior cells the caller classified out), addressed BY CELL INDEX -
            // never a centre-distance match. An excluded cell gets no space; a face it shares with a kept
            // cell still becomes that cell's (now envelope) panel; a face owned only by excluded cells relates
            // to nothing.
            HashSet<int> excluded = excludeCellIndices == null ? new HashSet<int>() : new HashSet<int>(excludeCellIndices);

            // One space per KEPT cell, located at the cell's native centre (or the bbox centre of its owned
            // faces as a fallback), addressed by cell index.
            Dictionary<int, Space> spaceByCellIndex = new Dictionary<int, Space>();
            int count = 1;
            for (int cellIndex = 0; cellIndex < resolvedCellComplex.Cells.Count; cellIndex++)
            {
                if (excluded.Contains(cellIndex))
                {
                    continue;
                }

                ResolvedCell cell = resolvedCellComplex.Cells[cellIndex];
                Point3D location = cell?.Centre ?? OwnedFacesCentre(resolvedCellComplex, cellIndex);

                if (location != null)
                {
                    List<Face3D> ownedFace3Ds = OwnedFace3Ds(resolvedCellComplex, cellIndex);
                    if (ownedFace3Ds.Count > 0)
                    {
                        Shell validationShell = new Shell(ownedFace3Ds);
                        if (!validationShell.Inside(location, fuzzyTolerance, tolerance) && !validationShell.On(location, tolerance))
                        {
                            location = validationShell.InternalPoint3D(fuzzyTolerance, tolerance) ?? OwnedFacesCentre(resolvedCellComplex, cellIndex);
                        }
                    }
                }

                if (location == null)
                {
                    diagnostics.Add(string.Format("SAM_OCCT_ANALYTICAL_COMPLEX_CELL_NO_LOCATION: Cell {0} had no centre and no owned-face geometry to derive one; cannot place a space.", cellIndex));
                    return null;
                }

                Space space = new Space(string.Format("Cell {0}", count), location);
                count++;
                if (cell != null && !double.IsNaN(cell.Volume))
                {
                    space.SetValue(SpaceParameter.Volume, System.Math.Abs(cell.Volume));
                }

                spaceByCellIndex[cellIndex] = space;
            }

            // One panel per unique cell face, keyed by its per-decode TopologyKey; identity inherited on an
            // unambiguous geometric match, defaulted (and counted) otherwise.
            Dictionary<int, Panel> panelsByKey = new Dictionary<int, Panel>();
            // A source panel's plane can contain the interior point of more than one distinct cell face (e.g.
            // a wall spanning past an internal separator) - MatchPanelByGeometry matches each independently, so
            // two DIFFERENT cell faces can both resolve to the SAME matched.Guid. Re-stamping both with that
            // Guid would hand back two Panel objects sharing one identity; AdjacencyCluster (Guid-keyed)
            // silently keeps only the last one added, dropping a panel and its relations with no diagnostic.
            // Only the Guid is unsafe to duplicate: the second-and-later faces still inherit the source's
            // construction and type (they ARE pieces of that same physical element), but get a fresh Guid.
            HashSet<System.Guid> claimedGuids = new HashSet<System.Guid>();
            int inheritedCount = 0, defaultedCount = 0, ambiguousCount = 0, freshGuidCount = 0, smallFaceCount = 0;
            foreach (ResolvedCellFace cellFace in resolvedCellComplex.Faces)
            {
                if (cellFace?.Face3D == null || panelsByKey.ContainsKey(cellFace.TopologyKey))
                {
                    continue;
                }

                double area = cellFace.Face3D.GetArea();
                if (!double.IsNaN(area) && area < minArea)
                {
                    smallFaceCount++;
                    continue;
                }

                PanelType panelType = Query.PanelType(cellFace.Face3D.GetPlane()?.Normal, maxAngle);
                if (panelType == PanelType.Undefined)
                {
                    panelType = PanelType.Air;
                }

                Panel matched = MatchPanelByGeometry(cellFace.Face3D, panels_Temp, tolerance, out bool ambiguous);
                Panel panel;
                if (matched != null)
                {
                    // Inherit the matched source panel's construction and type; take THIS face's geometry.
                    // Built via the same non-trimming factory as the default branch (so identity attribution
                    // never drops a face the default would keep). Re-stamp with the source Guid only if it is
                    // still free; if an earlier cell face already claimed it (see comment above), keep the
                    // fresh Guid the factory assigned so this piece is not silently dropped by AddObject.
                    Construction construction = matched.Construction ?? Query.DefaultConstruction(panelType);
                    PanelType matchedType = matched.PanelType != PanelType.Undefined ? matched.PanelType : panelType;
                    Panel basePanel = global::SAM.Analytical.Create.Panel(construction, matchedType, cellFace.Face3D);
                    if (basePanel == null)
                    {
                        panel = null;
                    }
                    else if (claimedGuids.Add(matched.Guid))
                    {
                        panel = global::SAM.Analytical.Create.Panel(matched.Guid, basePanel);
                        if (panel != null)
                        {
                            inheritedCount++;
                        }
                    }
                    else
                    {
                        panel = basePanel; // construction/type inherited, fresh Guid
                        freshGuidCount++;
                    }
                }
                else
                {
                    if (ambiguous)
                    {
                        ambiguousCount++;
                    }

                    panel = global::SAM.Analytical.Create.Panel(Query.DefaultConstruction(panelType), panelType, cellFace.Face3D);
                    if (panel != null)
                    {
                        defaultedCount++;
                    }
                }

                if (panel != null)
                {
                    panelsByKey[cellFace.TopologyKey] = panel;
                }
            }

            AdjacencyCluster result = new AdjacencyCluster();
            foreach (Space space in spaceByCellIndex.Values)
            {
                result.AddObject(space);
            }

            foreach (Panel panel in panelsByKey.Values)
            {
                result.AddObject(panel);
            }

            int relationCount = 0;
            foreach (ResolvedCellFace cellFace in resolvedCellComplex.Faces)
            {
                if (cellFace == null || !panelsByKey.TryGetValue(cellFace.TopologyKey, out Panel panel))
                {
                    continue;
                }

                foreach (int ownerCellIndex in cellFace.OwnerCellIndices ?? new List<int>())
                {
                    if (!spaceByCellIndex.TryGetValue(ownerCellIndex, out Space space))
                    {
                        continue; // excluded or out-of-range owner cell
                    }

                    if (result.AddRelation(space, panel))
                    {
                        relationCount++;
                    }
                }
            }

            if (relationCount == 0)
            {
                diagnostics.Add("SAM_OCCT_ANALYTICAL_COMPLEX_NO_RELATIONS: The supplied ResolvedCellComplex produced no space-panel relations.");
                return null;
            }

            diagnostics.Add(string.Format(
                "SAM_OCCT_ANALYTICAL_COMPLEX_CONSUMED: Built adjacency cluster directly from the supplied ResolvedCellComplex (SolveId {0}) with {1} space(s), {2} panel(s), {3} relation(s); {4} face(s) below minArea skipped; {5} face(s) had TopologyKey==0 (excluded from the complex); {6} cell(s) excluded by index.",
                resolvedCellComplex.SolveId, spaceByCellIndex.Count, panelsByKey.Count, relationCount, smallFaceCount, resolvedCellComplex.TopologyKeyZeroFaceCount, excluded.Count));
            diagnostics.Add(string.Format(
                "SAM_OCCT_ANALYTICAL_PANEL_IDENTITY: {0} panel(s) fully inherited identity (construction/type/Guid) from a supplied panel; {1} inherited construction/type but got a fresh Guid because the matched source panel's Guid was already claimed by an earlier cell face (never a silent collision that drops a panel); {2} defaulted ({3} of them because the geometric match was ambiguous, never mis-attributed).",
                inheritedCount, freshGuidCount, defaultedCount, ambiguousCount));

            // Keep normals consistent with the relations; do NOT reset panel types or constructions, so the
            // inherited identity survives (unmatched faces keep the default type/construction assigned above).
            result = result.UpdateNormals(false, true, false, fuzzyTolerance, tolerance);
            result.Normalize(false);

            return result;
        }

        /// <summary>The bounding-box centre of the faces a cell owns in the complex, a fallback space location
        /// when the native cell centre is absent.</summary>
        private static Point3D OwnedFacesCentre(ResolvedCellComplex resolvedCellComplex, int cellIndex)
        {
            List<BoundingBox3D> boundingBox3Ds = new List<BoundingBox3D>();
            foreach (ResolvedCellFace cellFace in resolvedCellComplex.Faces)
            {
                if (cellFace?.Face3D == null || cellFace.OwnerCellIndices == null || !cellFace.OwnerCellIndices.Contains(cellIndex))
                {
                    continue;
                }

                BoundingBox3D boundingBox3D = cellFace.Face3D.GetBoundingBox();
                if (boundingBox3D != null && boundingBox3D.IsValid())
                {
                    boundingBox3Ds.Add(boundingBox3D);
                }
            }

            return boundingBox3Ds.Count == 0 ? null : new BoundingBox3D(boundingBox3Ds).GetCentroid();
        }

        private static List<Face3D> OwnedFace3Ds(ResolvedCellComplex resolvedCellComplex, int cellIndex)
        {
            List<Face3D> face3Ds = new List<Face3D>();
            foreach (ResolvedCellFace cellFace in resolvedCellComplex.Faces)
            {
                if (cellFace?.Face3D == null || cellFace.OwnerCellIndices == null || !cellFace.OwnerCellIndices.Contains(cellIndex))
                {
                    continue;
                }

                face3Ds.Add(cellFace.Face3D);
            }

            return face3Ds;
        }

        /// <summary>Conservatively matches a cell face to a supplied panel: returns the sole panel whose face
        /// is coplanar with the cell face AND contains its interior point. Null when there is no such panel or
        /// when more than one qualifies (<paramref name="ambiguous"/> = true) - so identity is never
        /// mis-attributed by guessing between candidates.</summary>
        private static Panel MatchPanelByGeometry(Face3D face3D, List<Panel> panels, double tolerance, out bool ambiguous)
        {
            ambiguous = false;
            if (panels == null || panels.Count == 0)
            {
                return null;
            }

            Plane facePlane = face3D.GetPlane();
            Point3D internalPoint = face3D.GetInternalPoint3D(tolerance);
            if (facePlane == null || internalPoint == null)
            {
                return null;
            }

            Panel matched = null;
            int candidateCount = 0;
            foreach (Panel panel in panels)
            {
                Face3D panelFace3D = panel.GetFace3D();
                Plane panelPlane = panelFace3D?.GetPlane();
                if (panelPlane == null || !panelPlane.Coplanar(facePlane, tolerance))
                {
                    continue;
                }

                if (panelFace3D.On(internalPoint, tolerance))
                {
                    candidateCount++;
                    matched = panel;
                }
            }

            if (candidateCount == 1)
            {
                return matched;
            }

            ambiguous = candidateCount > 1;
            return null;
        }

        /// <summary>
        /// Logs why a combined MakerVolume rebuild produced a different number of
        /// cells than the input shells, by mapping each input shell's interior to
        /// the rebuilt cell that contains it:
        /// <list type="bullet">
        /// <item>shells whose interior is contained by no cell were dropped
        /// (<c>SAM_OCCT_ANALYTICAL_SHELL_NOT_REBUILT</c>); and</item>
        /// <item>two or more shells sharing one cell were merged into a single
        /// space (<c>SAM_OCCT_ANALYTICAL_SHELLS_MERGED</c>).</item>
        /// </list>
        /// Runs only when there is a cell/shell count delta, so the point-in-solid
        /// tests are not paid for the common case where everything closed.
        /// </summary>
        private static void LogShellRebuildAttribution(List<Shell> inputShells, List<Shell> occtShells, OcctBuildOptions options, OcctCellComplexResult cellComplexResult)
        {
            if (inputShells == null || occtShells == null || cellComplexResult == null)
            {
                return;
            }

            // Map each input shell to the index of the rebuilt cell containing its
            // interior point (-1 when no cell contains it).
            Dictionary<int, List<int>> inputsByCell = new Dictionary<int, List<int>>();
            int uncovered = 0;

            for (int i = 0; i < inputShells.Count; i++)
            {
                Shell inputShell = inputShells[i];
                if (inputShell == null)
                {
                    continue;
                }

                Point3D internalPoint = inputShell.InternalPoint3D(options.FuzzyTolerance, options.Tolerance);

                int cellIndex = -1;
                if (internalPoint != null)
                {
                    for (int j = 0; j < occtShells.Count; j++)
                    {
                        Shell occtShell = occtShells[j];
                        if (occtShell != null && (occtShell.Inside(internalPoint, options.FuzzyTolerance, options.Tolerance) || occtShell.On(internalPoint, options.Tolerance)))
                        {
                            cellIndex = j;
                            break;
                        }
                    }
                }

                if (cellIndex < 0)
                {
                    uncovered++;

                    BoundingBox3D boundingBox3D = inputShell.GetBoundingBox();
                    Point3D centroid = boundingBox3D?.GetCentroid();
                    cellComplexResult.AddDiagnostic(
                        OcctDiagnosticSeverity.Warning,
                        "SAM_OCCT_ANALYTICAL_SHELL_NOT_REBUILT",
                        string.Format(
                            "Input shell [{0}] was not reproduced as an OCCT cell (no rebuilt cell contains its interior{1}). Z range {2:0.###}..{3:0.###} m, centroid ({4:0.###}, {5:0.###}, {6:0.###}), {7} face(s). The combined MakerVolume rebuild could not close this volume - often a mismatched shared face with an adjacent shell.",
                            i,
                            internalPoint == null ? "; no interior point could be sampled" : string.Empty,
                            boundingBox3D == null ? double.NaN : boundingBox3D.Min.Z,
                            boundingBox3D == null ? double.NaN : boundingBox3D.Max.Z,
                            centroid == null ? double.NaN : centroid.X,
                            centroid == null ? double.NaN : centroid.Y,
                            centroid == null ? double.NaN : centroid.Z,
                            inputShell.Face3Ds?.Count ?? 0),
                        i);
                    continue;
                }

                if (!inputsByCell.TryGetValue(cellIndex, out List<int> members))
                {
                    members = new List<int>();
                    inputsByCell[cellIndex] = members;
                }

                members.Add(i);
            }

            // Cells that contain the interior of more than one input shell: those
            // input shells were merged and only one space results for them.
            int mergedShells = 0;
            foreach (KeyValuePair<int, List<int>> keyValuePair in inputsByCell)
            {
                List<int> members = keyValuePair.Value;
                if (members.Count < 2)
                {
                    continue;
                }

                mergedShells += members.Count;

                string memberText = string.Join(", ", members.Select(index =>
                {
                    Point3D memberCentroid = inputShells[index]?.GetBoundingBox()?.GetCentroid();
                    return string.Format("{0} (Z~{1:0.###})", index, memberCentroid == null ? double.NaN : memberCentroid.Z);
                }));

                BoundingBox3D cellBoundingBox3D = occtShells[keyValuePair.Key]?.GetBoundingBox();
                cellComplexResult.AddDiagnostic(
                    OcctDiagnosticSeverity.Warning,
                    "SAM_OCCT_ANALYTICAL_SHELLS_MERGED",
                    string.Format(
                        "Input shells [{0}] were merged into a single rebuilt cell #{1} (Z range {2:0.###}..{3:0.###} m); only one space is produced for them. This often follows defeaturing dissolving the shared interface between adjacent shells.",
                        memberText,
                        keyValuePair.Key,
                        cellBoundingBox3D == null ? double.NaN : cellBoundingBox3D.Min.Z,
                        cellBoundingBox3D == null ? double.NaN : cellBoundingBox3D.Max.Z));
            }

            if (uncovered > 0 || mergedShells > 0)
            {
                cellComplexResult.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_ANALYTICAL_SHELL_NOT_REBUILT_SUMMARY", string.Format("{0} input shell(s) dropped and {1} input shell(s) merged into shared cells, out of {2} input shell(s).", uncovered, mergedShells, inputShells.Count));
            }
        }

        private static AdjacencyCluster DirectAdjacencyCluster(OcctCellComplexResult cellComplexResult, List<Space> seedSpaces, OcctBuildOptions options, double minArea, double toleranceAngle, List<string> names = null)
        {
            if (cellComplexResult?.Cells == null || cellComplexResult.Cells.Count == 0)
            {
                return null;
            }

            AdjacencyCluster result = new AdjacencyCluster();
            Stopwatch stopwatch = Stopwatch.StartNew();
            List<Space> spaces = CreateSpaces(cellComplexResult, seedSpaces, options, names);
            if (spaces == null || spaces.Count != cellComplexResult.Cells.Count)
            {
                return null;
            }

            cellComplexResult.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TIMING_DIRECT_CREATE_SPACES", string.Format("Direct rebuild created {0} space object(s) in {1:0.000}s.", spaces.Count, stopwatch.Elapsed.TotalSeconds));

            stopwatch.Restart();
            for (int i = 0; i < spaces.Count; i++)
            {
                result.AddObject(spaces[i]);
            }
            cellComplexResult.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TIMING_DIRECT_ADD_SPACES", string.Format("Direct rebuild added {0} space object(s) to the adjacency cluster in {1:0.000}s.", spaces.Count, stopwatch.Elapsed.TotalSeconds));

            stopwatch.Restart();
            Dictionary<int, Panel> panels = CreatePanels(cellComplexResult, minArea, toleranceAngle);
            if (panels == null || panels.Count == 0)
            {
                return null;
            }

            foreach (Panel panel in panels.Values)
            {
                result.AddObject(panel);
            }
            cellComplexResult.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TIMING_DIRECT_PANELS", string.Format("Direct rebuild created and added {0} panel(s) in {1:0.000}s.", panels.Count, stopwatch.Elapsed.TotalSeconds));

            stopwatch.Restart();
            int relationCount = 0;
            int topologyKeyZeroFaceCount = 0;
            Dictionary<int, int> topologyKeyOwnerCounts = new Dictionary<int, int>();
            HashSet<System.Guid> relatedPanelGuids = new HashSet<System.Guid>();
            for (int cellIndex = 0; cellIndex < cellComplexResult.Cells.Count; cellIndex++)
            {
                IReadOnlyList<OcctCellFace> faces = cellComplexResult.Cells[cellIndex].Faces;
                if (faces == null)
                {
                    continue;
                }

                foreach (OcctCellFace face in faces)
                {
                    if (face == null)
                    {
                        continue;
                    }

                    if (face.TopologyKey == 0)
                    {
                        // Parity (P1): a decoded face with no per-decode identity is silently skipped
                        // by the relation loop below - count it so it is never a silent gap.
                        topologyKeyZeroFaceCount++;
                        continue;
                    }

                    topologyKeyOwnerCounts.TryGetValue(face.TopologyKey, out int ownerCount);
                    topologyKeyOwnerCounts[face.TopologyKey] = ownerCount + 1;

                    if (!panels.TryGetValue(face.TopologyKey, out Panel panel))
                    {
                        continue;
                    }

                    if (result.AddRelation(spaces[cellIndex], panel))
                    {
                        relationCount++;
                        relatedPanelGuids.Add(panel.Guid);
                    }
                }
            }

            // Parity diagnostic (P1, SAM_OCCT_ANALYTICAL_PARITY): counting only, does not change the
            // relationCount==0 refusal below. Expected relation count assumes each shared face is owned
            // by exactly two cells (the geometrically normal case for a planar cell complex): every
            // OcctCellComplexResult.FaceAdjacencies entry contributes two relations (one per owning
            // cell), and every envelope face (TopologyKey owned by exactly one cell) contributes one.
            // TopologyKey keys are per-decode only (OcctCellComplexResult.BuildFaceAdjacencies); this
            // never compares keys across two different decodes.
            int envelopeFaceCount = topologyKeyOwnerCounts.Values.Count(x => x == 1);
            int faceAdjacencyCount = cellComplexResult.FaceAdjacencies?.Count ?? 0;
            int expectedRelationCount = (2 * faceAdjacencyCount) + envelopeFaceCount;
            int zeroRelationPanelCount = panels.Count - relatedPanelGuids.Count;
            bool parityClean = relationCount == expectedRelationCount && topologyKeyZeroFaceCount == 0 && zeroRelationPanelCount == 0;
            cellComplexResult.AddDiagnostic(
                parityClean ? OcctDiagnosticSeverity.Info : OcctDiagnosticSeverity.Warning,
                "SAM_OCCT_ANALYTICAL_PARITY",
                string.Format(
                    "Parity check: {0} relation(s) added vs {1} expected (2 x {2} shared face adjacency(ies) + {3} envelope face(s)); {4} face(s) had TopologyKey==0 (silently skipped); {5} panel(s) received zero relation(s).",
                    relationCount, expectedRelationCount, faceAdjacencyCount, envelopeFaceCount, topologyKeyZeroFaceCount, zeroRelationPanelCount));

            if (relationCount == 0)
            {
                return null;
            }
            cellComplexResult.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TIMING_DIRECT_RELATIONS", string.Format("Direct rebuild added {0} space-panel relation(s) in {1:0.000}s.", relationCount, stopwatch.Elapsed.TotalSeconds));

            stopwatch.Restart();
            result = result.UpdateNormals(false, true, false, options.FuzzyTolerance, options.Tolerance);
            result.Normalize(false);
            result.UpdatePanelTypes(0);
            result.SetDefaultConstructionByPanelType();
            cellComplexResult.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TIMING_DIRECT_FINALIZE", string.Format("Direct rebuild finalized normals, panel types, and constructions in {0:0.000}s.", stopwatch.Elapsed.TotalSeconds));

            return result;
        }

        private static List<Space> CreateSpaces(OcctCellComplexResult cellComplexResult, List<Space> seedSpaces, OcctBuildOptions options, List<string> names = null)
        {
            List<Space> result = new List<Space>();
            HashSet<System.Guid> usedSeedSpaceGuids = new HashSet<System.Guid>();
            HashSet<string> usedNames = new HashSet<string>();
            int count = 1;
            int centerLocationCount = 0;
            int boundingBoxLocationCount = 0;
            int fallbackLocationCount = 0;
            Stopwatch fallbackStopwatch = new Stopwatch();

            for (int i = 0; i < cellComplexResult.Cells.Count; i++)
            {
                OcctCell cell = cellComplexResult.Cells[i];
                Shell shell = cell?.Shell;
                if (shell == null)
                {
                    return null;
                }

                Point3D location = cell.Center == null ? null : new Point3D(cell.Center);
                if (location != null && (shell.Inside(location, options.FuzzyTolerance, options.Tolerance) || shell.On(location, options.Tolerance)))
                {
                    centerLocationCount++;
                }
                else
                {
                    location = null;
                }

                if (location == null)
                {
                    location = BoundingBoxCenter(cell);
                    if (location != null && (shell.Inside(location, options.FuzzyTolerance, options.Tolerance) || shell.On(location, options.Tolerance)))
                    {
                        boundingBoxLocationCount++;
                    }
                    else
                    {
                        location = null;
                    }
                }

                if (location == null)
                {
                    fallbackStopwatch.Start();
                    location = shell.InternalPoint3D(options.FuzzyTolerance, options.Tolerance);
                    fallbackStopwatch.Stop();
                    if (location != null)
                    {
                        fallbackLocationCount++;
                    }
                }

                if (location == null)
                {
                    return null;
                }

                Space space = FindSeedSpace(shell, seedSpaces, usedSeedSpaceGuids, options);
                if (space != null)
                {
                    System.Guid seedSpaceGuid = space.Guid;
                    space = new Space(space, space.Name, location);
                    usedSeedSpaceGuids.Add(seedSpaceGuid);
                }
                else
                {
                    string name = null;
                    if (names != null && i < names.Count && !string.IsNullOrWhiteSpace(names[i]))
                    {
                        name = names[i];
                    }

                    if (string.IsNullOrWhiteSpace(name) || usedNames.Contains(name))
                    {
                        do
                        {
                            name = string.Format("Cell {0}", count);
                            count++;
                        }
                        while (usedNames.Contains(name));
                    }

                    space = new Space(name, location);
                }

                usedNames.Add(space.Name);

                if (!double.IsNaN(cell.Volume))
                {
                    space.SetValue(SpaceParameter.Volume, System.Math.Abs(cell.Volume));
                }

                result.Add(space);
            }

            cellComplexResult.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_ANALYTICAL_SPACE_LOCATIONS", string.Format("Used {0} OCCT cell center location(s), {1} decoded shell bounding-box center location(s), and {2} SAM fallback internal point location(s). Fallback location search took {3:0.000}s.", centerLocationCount, boundingBoxLocationCount, fallbackLocationCount, fallbackStopwatch.Elapsed.TotalSeconds));

            return result;
        }

        private static Point3D BoundingBoxCenter(OcctCell cell)
        {
            List<BoundingBox3D> boundingBox3Ds = new List<BoundingBox3D>();
            IReadOnlyList<OcctCellFace> faces = cell?.Faces;
            if (faces == null || faces.Count == 0)
            {
                return cell?.Shell?.GetBoundingBox()?.GetCentroid();
            }

            foreach (OcctCellFace face in faces)
            {
                BoundingBox3D boundingBox3D = face?.Face3D?.GetBoundingBox();
                if (boundingBox3D != null && boundingBox3D.IsValid())
                {
                    boundingBox3Ds.Add(boundingBox3D);
                }
            }

            if (boundingBox3Ds.Count == 0)
            {
                return cell?.Shell?.GetBoundingBox()?.GetCentroid();
            }

            return new BoundingBox3D(boundingBox3Ds).GetCentroid();
        }

        private static Space FindSeedSpace(Shell shell, List<Space> seedSpaces, HashSet<System.Guid> usedSeedSpaceGuids, OcctBuildOptions options)
        {
            if (shell == null || seedSpaces == null || seedSpaces.Count == 0)
            {
                return null;
            }

            foreach (Space seedSpace in seedSpaces)
            {
                if (seedSpace == null || seedSpace.Location == null || usedSeedSpaceGuids.Contains(seedSpace.Guid))
                {
                    continue;
                }

                if (shell.Inside(seedSpace.Location, options.FuzzyTolerance, options.Tolerance) || shell.On(seedSpace.Location, options.Tolerance))
                {
                    return seedSpace;
                }
            }

            return null;
        }

        private static Dictionary<int, Panel> CreatePanels(OcctCellComplexResult cellComplexResult, double minArea, double toleranceAngle)
        {
            Dictionary<int, Panel> result = new Dictionary<int, Panel>();
            foreach (OcctCell cell in cellComplexResult.Cells)
            {
                IReadOnlyList<OcctCellFace> faces = cell?.Faces;
                if (faces == null)
                {
                    continue;
                }

                foreach (OcctCellFace cellFace in faces)
                {
                    if (cellFace == null || cellFace.TopologyKey == 0 || cellFace.Face3D == null || result.ContainsKey(cellFace.TopologyKey))
                    {
                        continue;
                    }

                    double area = cellFace.Face3D.GetArea();
                    if (!double.IsNaN(area) && area < minArea)
                    {
                        continue;
                    }

                    PanelType panelType = Query.PanelType(cellFace.Face3D.GetPlane()?.Normal, toleranceAngle);
                    if (panelType == PanelType.Undefined)
                    {
                        panelType = PanelType.Air;
                    }

                    Construction construction = Query.DefaultConstruction(panelType);
                    Panel panel = global::SAM.Analytical.Create.Panel(construction, panelType, cellFace.Face3D);
                    if (panel != null)
                    {
                        result[cellFace.TopologyKey] = panel;
                    }
                }
            }

            return result;
        }
    }
}
