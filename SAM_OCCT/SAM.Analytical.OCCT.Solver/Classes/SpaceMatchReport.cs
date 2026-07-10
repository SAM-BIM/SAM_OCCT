// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OCCT.Solver
{
    /// <summary>
    /// The full result of <see cref="SpaceMatcher.Match"/> (docs/CONTROLLED_WORKFLOW_PLAN.md §6): per-space
    /// and per-cell match records, the level-group datums used, double-height verification, panel
    /// contribution and missing-separator findings, and the stable coded <c>SAM_OCCT_SPACEMATCH:</c> report
    /// grammar (<see cref="ToLines"/>).
    /// </summary>
    public class SpaceMatchReport
    {
        public IReadOnlyList<SpaceMatchRecord> SpaceMatches { get; }

        public IReadOnlyList<CellMatchRecord> CellMatches { get; }

        /// <summary>The level-group datums used to compute expected vertical spans, ascending.</summary>
        public IReadOnlyList<double> LevelGroupDatums { get; }

        /// <summary>Per requested double-height Guid: true when that space matched a single cell spanning
        /// its full expected height with no unexpected intermediate split.</summary>
        public IReadOnlyDictionary<Guid, bool> DoubleHeightOk { get; }

        /// <summary>Guids of generated cluster panels bounding zero spaces.</summary>
        public IReadOnlyList<Guid> OrphanClusterPanelGuids { get; }

        /// <summary>Guids of supplied input panels with no coplanar-overlapping contribution to any generated cluster face.</summary>
        public IReadOnlyList<Guid> UnusedInputPanelGuids { get; }

        /// <summary>Missing-separator findings for every merged pair (candidate-but-unused / partial / genuinely absent).</summary>
        public IReadOnlyList<SeparatorFinding> SeparatorFindings { get; }

        private readonly Dictionary<Guid, string> labels;

        public SpaceMatchReport(
            List<SpaceMatchRecord> spaceMatches,
            List<CellMatchRecord> cellMatches,
            IReadOnlyList<double> levelGroupDatums,
            List<Guid> orphanClusterPanelGuids,
            List<Guid> unusedInputPanelGuids,
            List<SeparatorFinding> separatorFindings,
            IReadOnlyDictionary<Guid, string> labels,
            IEnumerable<Guid> doubleHeightGuids = null)
        {
            SpaceMatches = spaceMatches ?? new List<SpaceMatchRecord>();
            CellMatches = cellMatches ?? new List<CellMatchRecord>();
            LevelGroupDatums = levelGroupDatums ?? new List<double>();
            OrphanClusterPanelGuids = orphanClusterPanelGuids ?? new List<Guid>();
            UnusedInputPanelGuids = unusedInputPanelGuids ?? new List<Guid>();
            SeparatorFindings = separatorFindings ?? new List<SeparatorFinding>();
            this.labels = labels == null ? new Dictionary<Guid, string>() : labels.ToDictionary(x => x.Key, x => x.Value);

            Dictionary<Guid, bool> doubleHeightOk = new Dictionary<Guid, bool>();
            foreach (Guid guid in (doubleHeightGuids ?? Enumerable.Empty<Guid>()).Distinct())
            {
                SpaceMatchRecord record = SpaceMatches.FirstOrDefault(x => x.Guid == guid);
                doubleHeightOk[guid] = record != null && record.Outcome == SpaceMatchOutcome.Matched;
            }

            DoubleHeightOk = doubleHeightOk;
        }

        /// <summary>
        /// True only when: no Missing/Merged/Split/IncorrectlyBounded expected space; no Extra cell; every
        /// requested double-height check passes; no orphan cluster panel. Unused or merged-away source
        /// panels are warnings only (plan §6) and do not affect validity.
        /// </summary>
        public bool Valid
        {
            get
            {
                if (SpaceMatches.Any(x => x.Outcome == SpaceMatchOutcome.Missing
                    || x.Outcome == SpaceMatchOutcome.Merged
                    || x.Outcome == SpaceMatchOutcome.Split
                    || x.Outcome == SpaceMatchOutcome.IncorrectlyBounded))
                {
                    return false;
                }

                if (CellMatches.Any(x => x.Outcome == SpaceMatchOutcome.Extra))
                {
                    return false;
                }

                if (DoubleHeightOk.Values.Any(ok => !ok))
                {
                    return false;
                }

                if (OrphanClusterPanelGuids.Count > 0)
                {
                    return false;
                }

                return true;
            }
        }

        /// <summary>The stable, greppable <c>SAM_OCCT_SPACEMATCH:</c> report lines (plan §6 grammar): one
        /// SUMMARY, one LEVELS, then MATCHED/MERGED/MISSING/SPLIT/INCORRECT (per expected space, one MERGED
        /// line per cell group) sorted by label, EXTRA (per cell) sorted by cell index, DOUBLE_HEIGHT sorted
        /// by label, MISSING_WALL/PANEL_ORPHAN/PANEL_UNUSED/BOUNDARY sorted.</summary>
        public List<string> ToLines()
        {
            List<string> lines = new List<string>();

            int matched = SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Matched);
            int merged = SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Merged);
            int missing = SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Missing);
            int split = SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Split);
            int incorrect = SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.IncorrectlyBounded);
            int extra = CellMatches.Count(x => x.Outcome == SpaceMatchOutcome.Extra);

            lines.Add(string.Format(
                "SAM_OCCT_SPACEMATCH: SUMMARY expected={0} cells={1} matched={2} merged={3} missing={4} split={5} incorrect={6} extra={7}",
                SpaceMatches.Count, CellMatches.Count, matched, merged, missing, split, incorrect, extra));

            lines.Add(string.Format(
                "SAM_OCCT_SPACEMATCH: LEVELS groups={0} elevations={1}",
                LevelGroupDatums.Count, string.Join("|", LevelGroupDatums.Select(x => x.ToString("0.000")))));

            foreach (SpaceMatchRecord record in SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Matched).OrderBy(x => x.Label))
            {
                lines.Add(string.Format(
                    "SAM_OCCT_SPACEMATCH: MATCHED guid={0} name={1} cell={2} loc={3} z=[{4}]",
                    record.Guid, record.Label, record.CellIndices.FirstOrDefault(), FormatPoint(record.Location), FormatSpan(record.ActualSpan)));
            }

            List<int> mergedCellsReported = new List<int>();
            foreach (SpaceMatchRecord record in SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Merged).OrderBy(x => x.Label))
            {
                int cellIndex = record.CellIndices.FirstOrDefault();
                if (mergedCellsReported.Contains(cellIndex))
                {
                    continue;
                }

                mergedCellsReported.Add(cellIndex);
                List<SpaceMatchRecord> group = SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Merged && x.CellIndices.FirstOrDefault() == cellIndex).OrderBy(x => x.Label).ToList();
                lines.Add(string.Format(
                    "SAM_OCCT_SPACEMATCH: MERGED guids={0} names={1} cell={2} detail=\"{3} expected locations in one cell\"",
                    string.Join("+", group.Select(x => x.Guid)), string.Join("+", group.Select(x => x.Label)), cellIndex, group.Count));
            }

            foreach (SpaceMatchRecord record in SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Missing).OrderBy(x => x.Label))
            {
                lines.Add(string.Format(
                    "SAM_OCCT_SPACEMATCH: MISSING guid={0} name={1} loc={2} nearestCell={3} distance={4:0.000}",
                    record.Guid, record.Label, FormatPoint(record.Location), record.NearestCellIndex, record.NearestDistance));
            }

            foreach (SpaceMatchRecord record in SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Split).OrderBy(x => x.Label))
            {
                lines.Add(string.Format(
                    "SAM_OCCT_SPACEMATCH: SPLIT guid={0} name={1} cells={2} level={3:0.000} detail=\"{4}\"",
                    record.Guid, record.Label, string.Join("+", record.CellIndices), record.SplitElevation, record.Detail));
            }

            foreach (SpaceMatchRecord record in SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.IncorrectlyBounded).OrderBy(x => x.Label))
            {
                lines.Add(string.Format(
                    "SAM_OCCT_SPACEMATCH: INCORRECT guid={0} name={1} cell={2} z=[{3}] expectedZ=[{4}] detail=\"{5}\"",
                    record.Guid, record.Label, record.CellIndices.FirstOrDefault(), FormatSpan(record.ActualSpan), FormatSpan(record.ExpectedSpan), record.Detail));
            }

            foreach (CellMatchRecord record in CellMatches.Where(x => x.Outcome == SpaceMatchOutcome.Extra).OrderBy(x => x.CellIndex))
            {
                lines.Add(string.Format(
                    "SAM_OCCT_SPACEMATCH: EXTRA cell={0} centre={1} volume={2:0.000} detail=\"contains no expected location\"",
                    record.CellIndex, FormatPoint(record.Center), record.Volume));
            }

            foreach (KeyValuePair<Guid, bool> pair in DoubleHeightOk.OrderBy(x => LabelOf(x.Key)))
            {
                SpaceMatchRecord record = SpaceMatches.FirstOrDefault(x => x.Guid == pair.Key);
                lines.Add(string.Format(
                    "SAM_OCCT_SPACEMATCH: DOUBLE_HEIGHT guid={0} name={1} ok={2} z=[{3}]",
                    pair.Key, LabelOf(pair.Key), pair.Value.ToString().ToLowerInvariant(), FormatSpan(record?.ExpectedSpan)));
            }

            foreach (SeparatorFinding finding in SeparatorFindings.OrderBy(x => x.NameA).ThenBy(x => x.NameB).ThenBy(x => x.PanelGuid))
            {
                lines.Add(string.Format(
                    "SAM_OCCT_SPACEMATCH: MISSING_WALL guids={0}+{1} panel={2} detail=\"{3}\"",
                    finding.GuidA, finding.GuidB, finding.PanelGuid.HasValue ? finding.PanelGuid.Value.ToString() : "(absent)", finding.Detail));
            }

            foreach (Guid guid in OrphanClusterPanelGuids.OrderBy(x => x))
            {
                lines.Add(string.Format("SAM_OCCT_SPACEMATCH: PANEL_ORPHAN panel={0} detail=\"cluster panel bounds no space\"", guid));
            }

            foreach (Guid guid in UnusedInputPanelGuids.OrderBy(x => x))
            {
                lines.Add(string.Format("SAM_OCCT_SPACEMATCH: PANEL_UNUSED panel={0} detail=\"input panel contributed to no cluster face\"", guid));
            }

            foreach (SpaceMatchRecord record in SpaceMatches.Where(x => x.Boundary).OrderBy(x => x.Label))
            {
                lines.Add(string.Format(
                    "SAM_OCCT_SPACEMATCH: BOUNDARY guid={0} cells={1} detail=\"location on shared boundary; assigned to nearest centre\"",
                    record.Guid, string.Join("+", record.BoundaryCellIndices)));
            }

            return lines;
        }

        private string LabelOf(Guid guid)
        {
            return labels.TryGetValue(guid, out string label) ? label : guid.ToString();
        }

        private static string FormatPoint(Point3D point3D)
        {
            return point3D == null ? "(null)" : string.Format("({0:0.###},{1:0.###},{2:0.###})", point3D.X, point3D.Y, point3D.Z);
        }

        private static string FormatSpan(double[] span)
        {
            return span == null ? "?,?" : string.Format("{0:0.000},{1:0.000}", span[0], span[1]);
        }
    }
}
