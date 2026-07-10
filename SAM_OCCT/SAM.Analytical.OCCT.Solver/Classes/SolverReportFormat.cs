// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>
        /// One coded <c>SAM_OCCT_EXTEND3D_PANEL:</c> line per applied managed extend/fill operation (E3,
        /// docs/EXTEND3D_ROBUST_HANDOVER.md): the mutated panel (its source Guid + solver index), the operation
        /// kind, the measured from -&gt; to, the target (cap index + scalar/sloped-plane branch, or the 2D
        /// plan-loop), the overshoot and the lateral-cap flag. The source Guid is resolved here (the geometry
        /// solver carries only indices); an out-of-range/absent source is reported as "n/a".
        /// </summary>
        public static List<string> FormatExtendRecords(IReadOnlyList<ExtendRecord> extendRecords, IReadOnlyList<Panel> sources)
        {
            List<string> result = new List<string>();
            foreach (ExtendRecord extendRecord in extendRecords ?? new List<ExtendRecord>())
            {
                if (extendRecord == null)
                {
                    continue;
                }

                result.Add(string.Format(
                    "SAM_OCCT_EXTEND3D_PANEL: panel {0} {1}",
                    PanelLabel(extendRecord.PanelIndex, extendRecord.SourceIndex, sources),
                    extendRecord.Describe()));
            }

            return result;
        }

        /// <summary>
        /// The per-panel extend diagnostics recorded on the conditioned panels themselves - currently only
        /// <c>SAM_OCCT_EXTEND3D_HOLE_DROPPED</c>, emitted when a footprint trim clips or drops an internal
        /// opening (E1 / R6). These live on the <see cref="SnappedPanel"/> and were previously not surfaced
        /// beyond the panel; E3 flows them out to the diagnostics list so a dropped window is never silent.
        /// Already fully-formed coded lines, returned verbatim.
        /// </summary>
        public static List<string> FormatExtendPanelDiagnostics(IReadOnlyList<SnappedPanel> snappedPanels)
        {
            List<string> result = new List<string>();
            foreach (SnappedPanel snappedPanel in snappedPanels ?? new List<SnappedPanel>())
            {
                foreach (string diagnostic in snappedPanel?.ExtendDiagnostics ?? (IReadOnlyList<string>)new List<string>())
                {
                    if (!string.IsNullOrEmpty(diagnostic))
                    {
                        result.Add(diagnostic);
                    }
                }
            }

            return result;
        }

        /// <summary>The moved-edge preview segments for the applied extend operations (E3): one
        /// <see cref="Segment3D"/> per record that moved a single edge (top/bottom/plan-start/plan-end). Cap
        /// grows have no single edge and contribute none. For dropping straight onto a Grasshopper canvas
        /// alongside the extended panels.</summary>
        public static List<Segment3D> ExtendPreviewSegment3Ds(IReadOnlyList<ExtendRecord> extendRecords)
        {
            List<Segment3D> result = new List<Segment3D>();
            foreach (ExtendRecord extendRecord in extendRecords ?? new List<ExtendRecord>())
            {
                Segment3D segment3D = extendRecord?.PreviewSegment3D();
                if (segment3D != null)
                {
                    result.Add(segment3D);
                }
            }

            return result;
        }

        /// <summary>"{source Guid} (#{solver index})" for an extend record's panel; "n/a (#idx)" when the
        /// source index is out of range or the panel is absent.</summary>
        private static string PanelLabel(int panelIndex, int sourceIndex, IReadOnlyList<Panel> sources)
        {
            string guid = sourceIndex >= 0 && sources != null && sourceIndex < sources.Count && sources[sourceIndex] != null
                ? sources[sourceIndex].Guid.ToString()
                : "n/a";
            return string.Format("{0} (#{1})", guid, panelIndex);
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

        /// <summary>One <c>SAM_OCCT_CLEAN3D_LEVELGROUP</c> line per level group (P2,
        /// docs/CONTROLLED_WORKFLOW_PLAN.md §4.1): the group datum elevation, the raw frame indices it merged and
        /// their elevations, the total cap count, the perpendicular spread and the tilt.</summary>
        public static List<string> FormatLevelGroups(IReadOnlyList<LevelGroup> levelGroups, IReadOnlyList<LevelFrame> levelFrames)
        {
            List<string> result = new List<string>();
            if (levelGroups == null)
            {
                return result;
            }

            for (int i = 0; i < levelGroups.Count; i++)
            {
                LevelGroup group = levelGroups[i];
                if (group == null)
                {
                    continue;
                }

                string frames = string.Join(",", group.FrameIndices ?? new List<int>());
                string elevations = string.Join(", ", (group.MemberElevations ?? new List<double>()).Select(x => x.ToString("0.000", CultureInfo.InvariantCulture)));
                result.Add(string.Format(CultureInfo.InvariantCulture,
                    "SAM_OCCT_CLEAN3D_LEVELGROUP: group {0}: elevation {1:0.000} m, frames [{2}] ({3}), {4} cap(s), spread {5:0.000} m, tilt {6:0.0} deg",
                    i, group.Elevation, frames, elevations, group.CapCount, group.Spread, group.TiltAngle * (180.0 / System.Math.PI)));
            }

            return result;
        }

        /// <summary>
        /// The full CleanReport (P2 §4): a <c>SAM_OCCT_CLEAN3D_LEVELS</c> summary (raw frame count -> group count
        /// + the <c>bucketBetweenLevels</c> band), the per-group <c>SAM_OCCT_CLEAN3D_LEVELGROUP</c> lines, and one
        /// <c>SAM_OCCT_CLEAN3D_PANEL</c> line per <see cref="CleanRecord"/> - the mutated panel (source Guid +
        /// index), the clean action, the distance moved onto its backer, and the resolved BucketSize/Weight/
        /// MaxExtend with their <see cref="ParameterProvenance"/> tags (the parameter reporting the geometry
        /// solver cannot do - it carries no Guids or provenance). The value/provenance lists are index-aligned to
        /// <paramref name="sources"/>.
        /// </summary>
        public static List<string> FormatCleanReport(
            IReadOnlyList<CleanRecord> cleanRecords,
            IReadOnlyList<LevelGroup> levelGroups,
            IReadOnlyList<LevelFrame> levelFrames,
            IReadOnlyList<Panel> sources,
            IReadOnlyList<double> bucketValues,
            IReadOnlyList<ParameterProvenance> bucketProvenance,
            IReadOnlyList<double> weightValues,
            IReadOnlyList<ParameterProvenance> weightProvenance,
            IReadOnlyList<double> maxExtendValues,
            IReadOnlyList<ParameterProvenance> maxExtendProvenance,
            double bucketBetweenLevels)
        {
            List<string> result = new List<string>();

            int frameCount = levelFrames?.Count ?? 0;
            int groupCount = levelGroups?.Count ?? 0;
            result.Add(string.Format(CultureInfo.InvariantCulture,
                "SAM_OCCT_CLEAN3D_LEVELS: {0} raw level frame(s) -> {1} level group(s) (bucketBetweenLevels={2:0.###} m).",
                frameCount, groupCount, bucketBetweenLevels));

            result.AddRange(FormatLevelGroups(levelGroups, levelFrames));

            foreach (CleanRecord cleanRecord in cleanRecords ?? new List<CleanRecord>())
            {
                if (cleanRecord == null)
                {
                    continue;
                }

                System.Text.StringBuilder line = new System.Text.StringBuilder();
                line.Append(string.Format(CultureInfo.InvariantCulture, "SAM_OCCT_CLEAN3D_PANEL: panel {0} {1}",
                    CleanPanelLabel(cleanRecord.SourceIndex, sources), cleanRecord.KindText()));

                if (cleanRecord.BackerSourceIndex >= 0)
                {
                    line.Append(string.Format(CultureInfo.InvariantCulture, "; moved {0:0.000} m onto {1}", cleanRecord.DistanceMoved, CleanPanelLabel(cleanRecord.BackerSourceIndex, sources)));
                }
                else if (cleanRecord.Kind != CleanRecordKind.DroppedInvalid)
                {
                    line.Append(string.Format(CultureInfo.InvariantCulture, "; moved {0:0.000} m", cleanRecord.DistanceMoved));
                }

                line.Append(string.Format(CultureInfo.InvariantCulture, "; bucket {0}; weight {1}; maxExtend {2}",
                    ValueTag(cleanRecord.SourceIndex, bucketValues, bucketProvenance, "0.000"),
                    ValueTag(cleanRecord.SourceIndex, weightValues, weightProvenance, "0.00"),
                    ValueTag(cleanRecord.SourceIndex, maxExtendValues, maxExtendProvenance, "0.00")));

                if (cleanRecord.LevelGroupIndex >= 0)
                {
                    line.Append(string.Format(CultureInfo.InvariantCulture, "; group {0}", cleanRecord.LevelGroupIndex));
                }

                result.Add(line.ToString());
            }

            return result;
        }

        /// <summary>"{source Guid} (#{sourceIndex})" for a clean record's panel; "n/a (#idx)" when the source
        /// index is out of range or the panel is absent.</summary>
        private static string CleanPanelLabel(int sourceIndex, IReadOnlyList<Panel> sources)
        {
            string guid = sourceIndex >= 0 && sources != null && sourceIndex < sources.Count && sources[sourceIndex] != null
                ? sources[sourceIndex].Guid.ToString()
                : "n/a";
            return string.Format(CultureInfo.InvariantCulture, "{0} (#{1})", guid, sourceIndex);
        }

        /// <summary>"{value} ({provenance-tag})" for a resolved parameter at <paramref name="sourceIndex"/>,
        /// formatted with the caller's invariant numeric <paramref name="format"/>; "n/a" when the index is out
        /// of range.</summary>
        private static string ValueTag(int sourceIndex, IReadOnlyList<double> values, IReadOnlyList<ParameterProvenance> provenance, string format)
        {
            if (sourceIndex < 0 || values == null || sourceIndex >= values.Count)
            {
                return "n/a";
            }

            string tag = provenance != null && sourceIndex < provenance.Count ? provenance[sourceIndex].ToTag() : "n/a";
            return string.Format(CultureInfo.InvariantCulture, "{0} ({1})", values[sourceIndex].ToString(format, CultureInfo.InvariantCulture), tag);
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
