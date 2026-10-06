// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

// SAM.Analytical.OCCT and SAM.Analytical.OCCT.Solver both declare a static Create class.
using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;

namespace SAM.Analytical.OCCT.Solver
{
    public static partial class Create
    {
        /// <summary>
        /// Phase 7c analytical spaces handoff (docs/P6_ARCHITECTURE_REVIEW.md §P sub-step 7c): solves
        /// <paramref name="panels"/> (<see cref="Modify.Solve3D"/>), classifies the resolved cells
        /// (<see cref="CellClassifier"/>), and builds one <c>Space</c> per <see cref="CellRole.Interior"/>
        /// cell - location = cell centre - by REUSING the existing
        /// <see cref="global::SAM.Analytical.OCCT.Create.AdjacencyCluster(IEnumerable{Space}, IEnumerable{Panel}, out OcctCellComplexResult, Log, OcctBuildOptions, double, double, double, double)"/>
        /// path (the Tower prior art) for adjacency/panel construction, never reimplementing it.
        /// </summary>
        /// <remarks>
        /// <b>Closure gate:</b> when the solve leaves any naked (free) boundary edge, spaces are NOT
        /// created - a degraded result (e.g. a multi-level managed solve short of full closure) returns
        /// null and a <see cref="DiagnosticCode.SpacesRefused"/> diagnostic rather than building spaces on
        /// an incomplete cell complex. This is required for cases such as the pinned
        /// `two-level-tilted.sam` managed baseline (29 cells / 29 naked): forcing the managed pipeline on
        /// that fixture must refuse spaces, not silently produce 29 of them.
        /// <para>
        /// <b>Cell exclusion:</b> a cell classifying <see cref="CellRole.Sliver"/>, <see cref="CellRole.Exterior"/>,
        /// or <see cref="CellRole.Unknown"/> does not become a <c>Space</c> - it is removed from the full
        /// adjacency cluster the reused entry point builds (which creates one Space per decoded cell,
        /// matched back to its cell by centre location), and a <see cref="DiagnosticCode.CellExcludedFromSpaces"/>
        /// diagnostic records it (in addition to the classifier's own <see cref="DiagnosticCode.SliverCell"/>).
        /// </para>
        /// <para>
        /// <b>Air policy:</b> input air panels are excluded from the solve (<see cref="Modify.Solve3D"/>'s
        /// existing behaviour) and never seen by the cell build; this method re-adds them, and any
        /// solver-fabricated gap-fill air panel (<c>PanelProvenanceParameter.Provenance == "GapFill"</c>),
        /// into the returned cluster unchanged, so no air panel is silently dropped from the analytical
        /// output. Air panels never contribute a Space.
        /// </para>
        /// </remarks>
        /// <param name="panels">The panels to solve and turn into spaces. Not modified; a new
        /// <c>AdjacencyCluster</c> is returned.</param>
        /// <param name="diagnostics">Machine-readable events from the solve, classification, and space
        /// construction - the closure-gate refusal and every excluded cell are recorded here, never silent.</param>
        /// <param name="forceManagedPipeline">As <see cref="Modify.Solve3D"/>: skip the raw-first attempt
        /// and always run the managed clean/extend/resolve pipeline. Default false.</param>
        /// <param name="minCellVolume">Minimum cell volume (m3) below which a cell classifies
        /// <see cref="CellRole.Sliver"/> - the same semantics as <see cref="Panel3DSnapSolver.MinCellVolume"/>.</param>
        /// <param name="minArea">Minimum panel area, forwarded to the reused <c>AdjacencyCluster</c> build.</param>
        /// <param name="maxAngle">Coplanar-merge angle tolerance, forwarded to the reused <c>AdjacencyCluster</c> build.</param>
        /// <param name="options">OCCT build options (distance/fuzzy/glue tolerances).</param>
        /// <returns>An <c>AdjacencyCluster</c> with one Space per interior cell, its panels (including
        /// re-joined air panels), and their adjacency relations; or null when no usable panels were
        /// supplied, the solve/build failed, or the closure gate refused (see <paramref name="diagnostics"/>).</returns>
        /// <summary>
        /// The Phase 7c closure-gate decision, pure and native-free so it is unit-testable without a
        /// kernel: spaces are refused whenever any naked (free) boundary edge remains in the resolved
        /// geometry. Strictly "&gt; 0" - a watertight solve (0 naked edges) always proceeds.
        /// </summary>
        public static bool ShouldRefuseSpaces(int nakedEdgeCount)
        {
            return nakedEdgeCount > 0;
        }

        public static AdjacencyCluster Spaces(
            IEnumerable<Panel> panels,
            out SolverDiagnostics diagnostics,
            bool forceManagedPipeline = false,
            double minCellVolume = 0.05,
            double minArea = Tolerance.MacroDistance,
            double maxAngle = 0.0872664626,
            OcctBuildOptions options = null)
        {
            diagnostics = new SolverDiagnostics();

            List<Panel> panelList = panels?.Where(x => x != null).ToList();
            if (panelList == null || panelList.Count == 0)
            {
                diagnostics.Add(SolverStage.Heal, DiagnosticCode.SpacesRefused, OcctDiagnosticSeverity.Error, "No panels were supplied; cannot create spaces.");
                return null;
            }

            // Air policy: input air panels bypass solving unchanged (Modify.Solve3D already excludes them
            // from the solve and does not re-add them to its own output) - rejoin them here.
            List<Panel> inputAirPanels = panelList.Where(x => x.PanelType == PanelType.Air).ToList();

            List<Panel> solved = panelList.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> solveDiagnostics, out _, out Solve3DReport report, forceManagedPipeline: forceManagedPipeline, options: options);
            foreach (string message in solveDiagnostics ?? new List<string>())
            {
                diagnostics.Add(SolverStage.Heal, DiagnosticCode.AdoptedLevel, OcctDiagnosticSeverity.Info, message);
            }

            if (solved == null || solved.Count == 0)
            {
                diagnostics.Add(SolverStage.Heal, DiagnosticCode.SpacesRefused, OcctDiagnosticSeverity.Error, "Solve3D produced no panels; cannot create spaces.");
                return null;
            }

            // Closure gate (Phase 7c): a degraded solve (naked edges remain) never gets spaces, even
            // though Solve3D itself always returns a best-effort panel set.
            int nakedCount = nakedPoint3Ds?.Count ?? 0;
            if (ShouldRefuseSpaces(nakedCount))
            {
                diagnostics.Add(SolverStage.Heal, DiagnosticCode.SpacesRefused, OcctDiagnosticSeverity.Warning,
                    string.Format("Refused to create spaces: {0} naked edge(s) remain in the resolved geometry.", nakedCount),
                    point3Ds: nakedPoint3Ds);
                return null;
            }

            // P2 (docs/CELLCOMPLEX_FIRST_HANDOVER.md): consume the complex the solver ALREADY validated
            // (report.ResolvedCellComplex) instead of rebuilding it with a second CellComplexByPanels decode
            // (the diagnosed seam). The only native build this method now pays for is the classifier's
            // envelope point-in-solid decode below.
            ResolvedCellComplex resolvedCellComplex = report?.ResolvedCellComplex;
            if (resolvedCellComplex == null || resolvedCellComplex.Cells == null || resolvedCellComplex.Cells.Count == 0)
            {
                diagnostics.Add(SolverStage.Heal, DiagnosticCode.SpacesRefused, OcctDiagnosticSeverity.Error, "The solve produced no adopted cell complex; cannot create spaces (native unavailable?).");
                return null;
            }

            List<Panel> nonAirSolved = solved.Where(x => x != null && x.PanelType != PanelType.Air).ToList();
            List<Panel> solvedAirPanels = solved.Where(x => x != null && x.PanelType == PanelType.Air).ToList();

            OcctBuildOptions occtOptions = options ?? new OcctBuildOptions
            {
                AvoidInternalShapes = false,
                SewBeforeBuild = true,
                SewingTolerance = 0.01
            };

            // Classify the adopted cells (from the DTO - no rebuild). The classifier does its own single
            // envelope decode of the resolved faces; it reads only cell volume/centre, both carried by the DTO.
            List<Face3D> nonAirFace3Ds = nonAirSolved.Select(x => x.GetFace3D()).Where(x => x != null && x.IsValid()).ToList();
            List<SolverCell> cells = resolvedCellComplex.Cells.Select(x => new SolverCell(x.Index, x.Volume, x.Centre, null)).ToList();
            IReadOnlyList<CellRole> roles = CellClassifier.ClassifyCells(cells, nonAirFace3Ds, minCellVolume, occtOptions, diagnostics);

            // Exclude every non-Interior cell BY CELL INDEX (relation filtering), never a centre-distance match.
            List<int> excludeCellIndices = new List<int>();
            for (int i = 0; i < roles.Count; i++)
            {
                if (roles[i] == CellRole.Interior)
                {
                    continue;
                }

                excludeCellIndices.Add(i);
                diagnostics.Add(SolverStage.Heal, DiagnosticCode.CellExcludedFromSpaces, OcctDiagnosticSeverity.Info,
                    string.Format("Cell {0} classified {1}; excluded from spaces.", i, roles[i]));
            }

            AdjacencyCluster cluster = AnalyticalOcctCreate.AdjacencyCluster(nonAirSolved, resolvedCellComplex, out List<string> clusterDiagnostics, minArea: minArea, maxAngle: maxAngle, tolerance: occtOptions.Tolerance, fuzzyTolerance: occtOptions.FuzzyTolerance, excludeCellIndices: excludeCellIndices);
            foreach (string message in clusterDiagnostics ?? new List<string>())
            {
                diagnostics.Add(SolverStage.Heal, DiagnosticCode.AdoptedLevel, OcctDiagnosticSeverity.Info, message);
            }

            if (cluster == null)
            {
                diagnostics.Add(SolverStage.Heal, DiagnosticCode.SpacesRefused, OcctDiagnosticSeverity.Error, "Building the adjacency cluster from the adopted complex produced no usable spaces; cannot create spaces.");
                return null;
            }

            // Air policy: rejoin the caller's original air panels and every solver-fabricated GapFill air
            // panel - both bypass solving/space creation but belong in the analytical output.
            //
            // Known limitation: these panels are only added as objects, never related to a Space. That
            // pairing has never been built here - the cells the solve resolves are matched to spaces by
            // centre location, and air panels take no part in the cell build at all, so there is nothing
            // to relate them to at this point. Consequently Query.FloorArea cannot see them: it collects a
            // space's boundaries through Query.NormalDictionary, which walks the cluster's space-to-panel
            // relations, so an unrelated air panel contributes nothing to SpaceParameter.Area even though
            // Air is an accepted floor type. Moving the UpdateFloorAreas recalculation after this loop
            // would therefore be a no-op, not a fix.
            //
            // Closing the gap needs new geometry rather than a floor-area change: each air panel would
            // have to be matched to the cell whose boundary its face lies on (for example by testing its
            // face centroid against the resolved cell shells) before a relation could be added. That is
            // solver work and is deliberately out of scope here.
            foreach (Panel airPanel in inputAirPanels.Concat(solvedAirPanels))
            {
                cluster.AddObject(airPanel);
            }

            return cluster;
        }
    }
}
