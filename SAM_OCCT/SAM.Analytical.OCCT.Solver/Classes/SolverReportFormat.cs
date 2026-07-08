// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
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
    }
}
