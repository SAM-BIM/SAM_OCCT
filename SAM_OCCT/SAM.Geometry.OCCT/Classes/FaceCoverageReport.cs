// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    /// <summary>
    /// Input-face coverage of a built cell complex: which input faces the cells actually use, which were
    /// legitimately trimmed away, and which cut through a cell without bounding it (an under-split).
    /// </summary>
    public class FaceCoverageReport
    {
        private readonly List<FaceCoverageEntry> entries;

        public FaceCoverageReport(IEnumerable<FaceCoverageEntry> entries, int cellCount)
        {
            this.entries = entries == null ? new List<FaceCoverageEntry>() : entries.Where(x => x != null).ToList();
            CellCount = cellCount;
        }

        /// <summary>One entry per evaluated input face (faces below minArea are not evaluated).</summary>
        public IReadOnlyList<FaceCoverageEntry> Entries
        {
            get { return entries; }
        }

        public int CellCount { get; }

        public double InputArea
        {
            get { return entries.Sum(x => x.Area); }
        }

        public double CoveredArea
        {
            get { return entries.Sum(x => x.CoveredArea); }
        }

        /// <summary>Total uncovered area lying inside cells: non-zero means some input face cuts through a cell.</summary>
        public double InteriorArea
        {
            get { return entries.Sum(x => x.InteriorArea); }
        }

        /// <summary>Total area of faces the complex does not use at all and that lie outside every cell.</summary>
        public double UnusedArea
        {
            get { return entries.Where(x => x.Verdict == FaceCoverageVerdict.Unused).Sum(x => x.Area); }
        }

        public double UnresolvedArea
        {
            get { return entries.Sum(x => x.UnresolvedArea); }
        }

        /// <summary>True when every evaluated face is <see cref="FaceCoverageVerdict.Covered"/> or <see cref="FaceCoverageVerdict.Trimmed"/>.</summary>
        public bool IsClean
        {
            get { return entries.All(x => x.Verdict == FaceCoverageVerdict.Covered || x.Verdict == FaceCoverageVerdict.Trimmed); }
        }

        public int Count(FaceCoverageVerdict verdict)
        {
            return entries.Count(x => x.Verdict == verdict);
        }

        public FaceCoverageEntry Entry(int faceIndex)
        {
            return entries.Find(x => x.FaceIndex == faceIndex);
        }

        public string Summary()
        {
            return string.Format(
                "{0} cell(s); {1} face(s): {2} covered, {3} trimmed, {4} unused, {5} partial, {6} missing, {7} unresolved; covered {8:0.###} of {9:0.###}, interior {10:0.###}, unused {11:0.###}, unresolved {12:0.###}",
                CellCount, entries.Count,
                Count(FaceCoverageVerdict.Covered), Count(FaceCoverageVerdict.Trimmed), Count(FaceCoverageVerdict.Unused),
                Count(FaceCoverageVerdict.Partial), Count(FaceCoverageVerdict.Missing), Count(FaceCoverageVerdict.Unresolved),
                CoveredArea, InputArea, InteriorArea, UnusedArea, UnresolvedArea);
        }
    }
}
