// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    public class OcctCellComplexResult : IDisposable
    {
        private class CellFaceOwner
        {
            public int CellIndex { get; set; }

            public int FaceIndex { get; set; }
        }

        private readonly List<OcctCell> cells = new List<OcctCell>();
        private readonly List<OcctCellFaceAdjacency> faceAdjacencies = new List<OcctCellFaceAdjacency>();
        private readonly List<OcctDiagnostic> diagnostics = new List<OcctDiagnostic>();

        public bool NativeAvailable { get; internal set; }

        public string NativeVersion { get; internal set; }

        /// <summary>
        /// The retained native topology handle when the operation ran with
        /// <c>OcctBuildOptions.RetainTopology</c>; null otherwise. The result
        /// owns the handle - dispose the result (or the handle directly; both
        /// are idempotent) when done. Legacy callers that never opt in get null
        /// and need not dispose anything.
        /// </summary>
        public OcctTopology Topology { get; internal set; }

        /// <summary>
        /// Observational native history (<c>BRepTools_History</c>) for the op that
        /// produced this result, or null when the native build predates ABI v4, the
        /// op does not capture history (only <c>build_cell_complex</c> and
        /// <c>merge_coplanar</c> do), or history was unavailable. A pure managed
        /// snapshot - nothing to dispose. Consumed by the solver's
        /// <c>SourceMap</c> composition (Phase 3); callers degrade to the geometric
        /// heuristic when it is null.
        /// </summary>
        public OcctHistory History { get; internal set; }

        /// <summary>
        /// Set by operations that legitimately produce no cells (e.g. the
        /// distance/proximity query, issue #28) to report success without a
        /// decoded cell complex. Cell-producing operations leave this false and
        /// are judged successful by their decoded cell count instead.
        /// </summary>
        internal bool OperationSucceeded { get; set; }

        public bool Success
        {
            get
            {
                return (cells.Count != 0 || OperationSucceeded) && !diagnostics.Any(x => x.Severity == OcctDiagnosticSeverity.Error);
            }
        }

        public IReadOnlyList<OcctCell> Cells
        {
            get { return cells; }
        }

        public IReadOnlyList<Shell> Shells
        {
            get { return cells.ConvertAll(x => x.Shell); }
        }

        public IReadOnlyList<OcctDiagnostic> Diagnostics
        {
            get { return diagnostics; }
        }

        public IReadOnlyList<OcctCellFaceAdjacency> FaceAdjacencies
        {
            get { return faceAdjacencies; }
        }

        public void AddCell(OcctCell cell)
        {
            if (cell?.Shell != null)
            {
                cells.Add(cell);
            }
        }

        public void AddDiagnostic(OcctDiagnosticSeverity severity, string code, string message, int? sourceIndex = null)
        {
            diagnostics.Add(new OcctDiagnostic(severity, code, message, sourceIndex));
        }

        public void Dispose()
        {
            Topology?.Dispose();
        }

        public void BuildFaceAdjacencies()
        {
            faceAdjacencies.Clear();

            Dictionary<int, List<CellFaceOwner>> dictionary = new Dictionary<int, List<CellFaceOwner>>();
            for (int i = 0; i < cells.Count; i++)
            {
                IReadOnlyList<OcctCellFace> faces = cells[i].Faces;
                if (faces == null)
                {
                    continue;
                }

                for (int j = 0; j < faces.Count; j++)
                {
                    int topologyKey = faces[j]?.TopologyKey ?? 0;
                    if (topologyKey == 0)
                    {
                        continue;
                    }

                    if (!dictionary.TryGetValue(topologyKey, out List<CellFaceOwner> owners))
                    {
                        owners = new List<CellFaceOwner>();
                        dictionary[topologyKey] = owners;
                    }

                    owners.Add(new CellFaceOwner { CellIndex = i, FaceIndex = j });
                }
            }

            foreach (KeyValuePair<int, List<CellFaceOwner>> keyValuePair in dictionary)
            {
                List<CellFaceOwner> owners = keyValuePair.Value;
                if (owners == null || owners.Count < 2)
                {
                    continue;
                }

                for (int i = 0; i < owners.Count - 1; i++)
                {
                    for (int j = i + 1; j < owners.Count; j++)
                    {
                        if (owners[i].CellIndex == owners[j].CellIndex)
                        {
                            continue;
                        }

                        faceAdjacencies.Add(new OcctCellFaceAdjacency(keyValuePair.Key, owners[i].CellIndex, owners[i].FaceIndex, owners[j].CellIndex, owners[j].FaceIndex));
                    }
                }
            }
        }
    }
}
