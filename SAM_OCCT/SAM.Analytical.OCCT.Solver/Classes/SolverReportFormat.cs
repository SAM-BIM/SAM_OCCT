// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OCCT.Solver
{
    /// <summary>
    /// Grasshopper-facing text formatters (Phase 8a) over the solver's already-produced diagnostics,
    /// source mapping, level frames and cell metadata - pure string assembly, no geometry/algorithm
    /// change. Shared by the Solve3D/Clean3D/Extend3D/AutoTune3D analytical wrappers so every
    /// Grasshopper component that surfaces staged solver output reports it the same way.
    /// </summary>
    public static class SolverReportFormat
    {
        /// <summary>
        /// One line per <see cref="SolverDiagnostic"/>, prefixed <c>{prefix}_DIAGNOSTIC: [Stage/Code/Severity] Message</c> -
        /// the exact format the pre-Phase-8 Solve3D/AutoTune3D methods already emit inline, factored out here for reuse.
        /// </summary>
        public static List<string> FormatDiagnostics(SolverDiagnostics diagnostics, string prefix)
        {
            List<string> result = new List<string>();
            foreach (SolverDiagnostic solverDiagnostic in diagnostics?.All ?? new List<SolverDiagnostic>())
            {
                result.Add(string.Format("{0}_DIAGNOSTIC: {1}", prefix, solverDiagnostic));
            }

            return result;
        }

        /// <summary>
        /// One line per recorded source: which output face(s) (and provenance) it contributes to, naming the
        /// source panel's Guid when <paramref name="sources"/> is supplied and the index is in range. The
        /// fabricated sentinel (<see cref="SourceMap.FabricatedSource"/>) is reported as "fabricated" rather
        /// than a source index.
        /// </summary>
        public static List<string> FormatSourceMap(SourceMap sourceMap, IReadOnlyList<Panel> sources)
        {
            List<string> result = new List<string>();
            if (sourceMap == null)
            {
                return result;
            }

            foreach (int source in sourceMap.Sources.OrderBy(x => x))
            {
                IReadOnlyList<FaceKey> faceKeys = sourceMap.FacesOf(source);
                if (faceKeys.Count == 0)
                {
                    continue;
                }

                string sourceLabel = source == SourceMap.FabricatedSource
                    ? "fabricated"
                    : string.Format("source {0}{1}", source, sources != null && source >= 0 && source < sources.Count ? string.Format(" (panel {0})", sources[source]?.Guid) : string.Empty);

                List<string> faceLabels = new List<string>();
                foreach (FaceKey faceKey in faceKeys.OrderBy(x => x.Value))
                {
                    IReadOnlyList<Provenance> provenances = sourceMap.ProvenancesOf(faceKey);
                    faceLabels.Add(provenances.Count == 0 ? faceKey.ToString() : string.Format("{0} [{1}]", faceKey, string.Join(",", provenances)));
                }

                result.Add(string.Format("{0} -> {1}", sourceLabel, string.Join(", ", faceLabels)));
            }

            return result;
        }

        /// <summary>One line per level frame: elevation, tilt (degrees) and member cap count.</summary>
        public static List<string> FormatLevelFrames(IReadOnlyList<LevelFrame> levelFrames)
        {
            List<string> result = new List<string>();
            if (levelFrames == null)
            {
                return result;
            }

            for (int i = 0; i < levelFrames.Count; i++)
            {
                LevelFrame frame = levelFrames[i];
                if (frame == null)
                {
                    continue;
                }

                result.Add(string.Format(
                    "Frame {0}: elevation {1:0.###} m, tilt {2:0.#} deg, {3} cap(s)",
                    i, frame.Elevation, frame.TiltAngle * (180.0 / System.Math.PI), frame.CapIndices?.Count ?? 0));
            }

            return result;
        }

        /// <summary>
        /// One line per cell: index, volume, centre, and its classified role when <paramref name="cellRoles"/>
        /// is supplied and index-aligned to <paramref name="cells"/> (else "not classified").
        /// </summary>
        public static List<string> FormatCells(IReadOnlyList<SolverCell> cells, IReadOnlyList<CellRole> cellRoles)
        {
            List<string> result = new List<string>();
            if (cells == null)
            {
                return result;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                SolverCell cell = cells[i];
                if (cell == null)
                {
                    continue;
                }

                string role = cellRoles != null && i < cellRoles.Count ? cellRoles[i].ToString() : "not classified";
                string centre = cell.Center == null ? "n/a" : string.Format("({0:0.###}, {1:0.###}, {2:0.###})", cell.Center.X, cell.Center.Y, cell.Center.Z);

                result.Add(string.Format("Cell {0}: volume {1:0.###} m3, centre {2}, role {3}", cell.Index, cell.Volume, centre, role));
            }

            return result;
        }

        // ── ResolvedCellComplex formatters (docs/CELLCOMPLEX_FIRST_HANDOVER.md, Phase P3) ───────────────
        // Grasshopper-facing projections of the P2 DTO: face geometry/owners, adjacency pairs/geometry, and a
        // one-line summary. Index-aligned pairs (geometry list + string list) so a caller can zip them on the
        // canvas without re-deriving the alignment.

        /// <summary>The geometry of every unique cell face in the complex, in <see cref="ResolvedCellComplex.Faces"/> order.</summary>
        public static List<Face3D> FormatResolvedCellComplexFaceGeometry(ResolvedCellComplex resolvedCellComplex)
        {
            List<Face3D> result = new List<Face3D>();
            foreach (ResolvedCellFace face in resolvedCellComplex?.Faces ?? new List<ResolvedCellFace>())
            {
                if (face?.Face3D != null)
                {
                    result.Add(face.Face3D);
                }
            }

            return result;
        }

        /// <summary>One line per unique cell face (index-aligned with <see cref="FormatResolvedCellComplexFaceGeometry"/>):
        /// its per-decode TopologyKey and owner cell index/indices - one owner for an envelope face, two for a
        /// shared internal separator.</summary>
        public static List<string> FormatResolvedCellComplexFaceOwners(ResolvedCellComplex resolvedCellComplex)
        {
            List<string> result = new List<string>();
            foreach (ResolvedCellFace face in resolvedCellComplex?.Faces ?? new List<ResolvedCellFace>())
            {
                if (face?.Face3D == null)
                {
                    continue;
                }

                string owners = string.Join(", ", (face.OwnerCellIndices ?? new List<int>()).Select(x => string.Format("cell {0}", x)));
                result.Add(string.Format("face key {0}: {1}", face.TopologyKey, string.IsNullOrEmpty(owners) ? "no owner" : owners));
            }

            return result;
        }

        /// <summary>One line per shared-face adjacency: the two cell indices it separates and its per-decode
        /// TopologyKey (index-aligned with <see cref="FormatResolvedCellComplexAdjacencyFaceGeometry"/>).</summary>
        public static List<string> FormatResolvedCellComplexAdjacencies(ResolvedCellComplex resolvedCellComplex)
        {
            List<string> result = new List<string>();
            foreach (ResolvedFaceAdjacency adjacency in resolvedCellComplex?.Adjacencies ?? new List<ResolvedFaceAdjacency>())
            {
                if (adjacency == null)
                {
                    continue;
                }

                result.Add(string.Format("cell {0} <-> cell {1} (face key {2})", adjacency.CellIndex1, adjacency.CellIndex2, adjacency.TopologyKey));
            }

            return result;
        }

        /// <summary>The shared-face geometry for each adjacency (index-aligned with
        /// <see cref="FormatResolvedCellComplexAdjacencies"/>), looked up by TopologyKey in
        /// <see cref="ResolvedCellComplex.Faces"/>; an adjacency whose face key is not found (should not
        /// happen - both are decoded from the same result) contributes null rather than silently shifting the
        /// alignment.</summary>
        public static List<Face3D> FormatResolvedCellComplexAdjacencyFaceGeometry(ResolvedCellComplex resolvedCellComplex)
        {
            List<Face3D> result = new List<Face3D>();
            if (resolvedCellComplex == null)
            {
                return result;
            }

            Dictionary<int, Face3D> faceByKey = (resolvedCellComplex.Faces ?? new List<ResolvedCellFace>())
                .Where(x => x != null)
                .GroupBy(x => x.TopologyKey)
                .ToDictionary(x => x.Key, x => x.First().Face3D);

            foreach (ResolvedFaceAdjacency adjacency in resolvedCellComplex.Adjacencies ?? new List<ResolvedFaceAdjacency>())
            {
                if (adjacency == null)
                {
                    continue;
                }

                result.Add(faceByKey.TryGetValue(adjacency.TopologyKey, out Face3D face3D) ? face3D : null);
            }

            return result;
        }

        /// <summary>One human-readable summary line: cell/face/adjacency/naked-wire counts, the
        /// TopologyKey==0 exclusion count, and the SolveId - the P3 "parity/closure summary text" output.</summary>
        public static string FormatResolvedCellComplexSummary(ResolvedCellComplex resolvedCellComplex)
        {
            if (resolvedCellComplex == null)
            {
                return "no CellComplex (raw-first path failed, or this stage never resolves)";
            }

            return string.Format(
                "CellComplex (SolveId {0}): {1} cell(s), {2} unique face(s), {3} adjacency pair(s), {4} naked wire(s), {5} face(s) excluded (TopologyKey==0), {6} panel(s) in roster.",
                resolvedCellComplex.SolveId,
                resolvedCellComplex.Cells?.Count ?? 0,
                resolvedCellComplex.Faces?.Count ?? 0,
                resolvedCellComplex.Adjacencies?.Count ?? 0,
                resolvedCellComplex.NakedWires?.Count ?? 0,
                resolvedCellComplex.TopologyKeyZeroFaceCount,
                resolvedCellComplex.PanelGuids?.Count ?? 0);
        }
    }
}
