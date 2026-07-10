// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OCCT.Solver
{
    /// <summary>
    /// GUID-based matcher of an <see cref="ExpectedSpaceSet"/> against generated <see cref="CellGeometry"/>
    /// cells (docs/CONTROLLED_WORKFLOW_PLAN.md §6). The containment predicate mirrors
    /// <c>SAM.Analytical.OCCT.Create.AdjacencyCluster</c>'s own <c>FindSeedSpace</c>:
    /// <c>Shell.Inside(location, silverSpacing, tolerance) || Shell.On(location, tolerance)</c> - but the
    /// matcher builds its own containment matrix and classification independently of whatever
    /// (greedy, first-fit) space matching the builder itself performed, because a supplied <c>spaces_</c>
    /// list only steers the builder's rebuild path and seeds names - it is not a validation reference.
    /// </summary>
    public static class SpaceMatcher
    {
        private const double DEFAULT_CapNormalZ_ConeTolerance = 20.0 * (System.Math.PI / 180.0);

        /// <summary>
        /// Matches every expected space in <paramref name="expectedSpaceSet"/> against <paramref name="cells"/>
        /// and returns the full <see cref="SpaceMatchReport"/>. When <paramref name="adjacencyCluster"/>
        /// and/or <paramref name="sourcePanels"/> are supplied, also runs
        /// <see cref="PanelContributionFinder"/> (orphan cluster panels / unused input panels) and
        /// <see cref="SeparatorPanelFinder"/> (missing-wall evidence for every merged pair) and folds their
        /// findings into the same report.
        /// </summary>
        /// <param name="expectedSpaceSet">The expected spaces and their inferred level spans.</param>
        /// <param name="cells">The generated cell geometry to match against.</param>
        /// <param name="doubleHeightGuids">Guids of spaces known to be double-height - MUST be the same set passed to <see cref="ExpectedSpaceSet.Create"/> (its expected spans were computed against it).</param>
        /// <param name="adjacencyCluster">Optional: the built cluster, for orphan-cluster-panel detection.</param>
        /// <param name="sourcePanels">Optional: the original input panels, for unused-panel and missing-separator detection.</param>
        public static SpaceMatchReport Match(
            ExpectedSpaceSet expectedSpaceSet,
            List<CellGeometry> cells,
            IEnumerable<Guid> doubleHeightGuids = null,
            AdjacencyCluster adjacencyCluster = null,
            IEnumerable<Panel> sourcePanels = null)
        {
            if (expectedSpaceSet == null)
            {
                throw new ArgumentNullException(nameof(expectedSpaceSet));
            }

            SpaceMatchOptions options = expectedSpaceSet.Options;
            cells = cells ?? new List<CellGeometry>();
            List<Space> spaces = expectedSpaceSet.Spaces.ToList();
            HashSet<Guid> doubleHeightSet = new HashSet<Guid>(doubleHeightGuids ?? Enumerable.Empty<Guid>());

            List<string> boundaryLines = new List<string>();

            // 1. Containment matrix (FindSeedSpace's own predicate).
            Dictionary<Guid, List<int>> containment = new Dictionary<Guid, List<int>>();
            foreach (Space space in spaces)
            {
                List<int> hits = new List<int>();
                if (space.Location != null)
                {
                    foreach (CellGeometry cell in cells)
                    {
                        if (cell?.Shell == null)
                        {
                            continue;
                        }

                        if (cell.Shell.Inside(space.Location, options.SilverSpacing, options.Tolerance) || cell.Shell.On(space.Location, options.Tolerance))
                        {
                            hits.Add(cell.Index);
                        }
                    }
                }

                containment[space.Guid] = hits;
            }

            // 2. Resolve boundary ambiguity: assign each contained space to exactly one cell (nearest centre, stable index tie-break).
            Dictionary<Guid, int> assignedCell = new Dictionary<Guid, int>();
            Dictionary<Guid, bool> boundary = new Dictionary<Guid, bool>();
            foreach (Space space in spaces)
            {
                List<int> hits = containment[space.Guid];
                if (hits.Count == 0)
                {
                    continue;
                }

                if (hits.Count == 1)
                {
                    assignedCell[space.Guid] = hits[0];
                    continue;
                }

                int best = hits
                    .OrderBy(i => DistanceToCentre(space.Location, cells[i]))
                    .ThenBy(i => i)
                    .First();
                assignedCell[space.Guid] = best;
                boundary[space.Guid] = true;
            }

            // 3. Group by assigned cell; classify Matched/Merged/Split/IncorrectlyBounded.
            Dictionary<int, List<Space>> byCell = new Dictionary<int, List<Space>>();
            foreach (KeyValuePair<Guid, int> pair in assignedCell)
            {
                if (!byCell.TryGetValue(pair.Value, out List<Space> members))
                {
                    members = new List<Space>();
                    byCell[pair.Value] = members;
                }

                members.Add(spaces.First(x => x.Guid == pair.Key));
            }

            HashSet<int> splitPartnerCells = new HashSet<int>();
            List<SpaceMatchRecord> spaceRecords = new List<SpaceMatchRecord>();

            foreach (Space space in spaces)
            {
                if (!assignedCell.TryGetValue(space.Guid, out int cellIndex))
                {
                    // Missing.
                    int nearest = NearestCellIndex(cells, space.Location);
                    double distance = nearest >= 0 && space.Location != null && cells[nearest].Center != null
                        ? space.Location.Distance(cells[nearest].Center)
                        : double.NaN;

                    spaceRecords.Add(new SpaceMatchRecord(
                        space.Guid, space.Name, expectedSpaceSet.Labels[space.Guid], SpaceMatchOutcome.Missing, space.Location,
                        new List<int>(), new List<Guid>(), nearest, distance,
                        expectedSpaceSet.LevelSpans.TryGetValue(space.Guid, out double[] expectedSpanMissing) ? expectedSpanMissing : null,
                        null, double.NaN, false, new List<int>(), "no cell contains the expected location"));
                    continue;
                }

                List<Space> siblings = byCell[cellIndex];
                bool isBoundary = boundary.TryGetValue(space.Guid, out bool b) && b;
                List<int> boundaryHits = isBoundary ? containment[space.Guid] : new List<int>();

                if (siblings.Count > 1)
                {
                    List<Guid> partners = siblings.Where(x => x.Guid != space.Guid).Select(x => x.Guid).OrderBy(x => x).ToList();
                    double[] actualSpanMerged = CellSpan(cells[cellIndex]);
                    spaceRecords.Add(new SpaceMatchRecord(
                        space.Guid, space.Name, expectedSpaceSet.Labels[space.Guid], SpaceMatchOutcome.Merged, space.Location,
                        new List<int> { cellIndex }, partners, -1, double.NaN,
                        expectedSpaceSet.LevelSpans.TryGetValue(space.Guid, out double[] expectedSpanMerged) ? expectedSpanMerged : null,
                        actualSpanMerged, double.NaN, isBoundary, boundaryHits,
                        string.Format("{0} expected locations in one cell", siblings.Count)));
                    continue;
                }

                // Sole occupant: check span consistency.
                double[] actualSpan = CellSpan(cells[cellIndex]);
                double[] expectedSpan = expectedSpaceSet.LevelSpans.TryGetValue(space.Guid, out double[] es) ? es : null;
                bool consistent = SpanConsistent(actualSpan, expectedSpan, options.LevelBand);

                if (consistent)
                {
                    // Double-height requested? Verify no internal near-horizontal face at an intermediate datum.
                    if (doubleHeightSet.Contains(space.Guid) && !CheckDoubleHeight(cells[cellIndex], expectedSpan, expectedSpaceSet.LevelGroupDatums, options, out double violationDatum))
                    {
                        spaceRecords.Add(new SpaceMatchRecord(
                            space.Guid, space.Name, expectedSpaceSet.Labels[space.Guid], SpaceMatchOutcome.Split, space.Location,
                            new List<int> { cellIndex }, new List<Guid>(), -1, double.NaN,
                            expectedSpan, actualSpan, violationDatum, isBoundary, boundaryHits,
                            string.Format("Unexpected split: {0} split at level {1:0.00}", expectedSpaceSet.Labels[space.Guid], violationDatum)));
                        continue;
                    }

                    spaceRecords.Add(new SpaceMatchRecord(
                        space.Guid, space.Name, expectedSpaceSet.Labels[space.Guid], SpaceMatchOutcome.Matched, space.Location,
                        new List<int> { cellIndex }, new List<Guid>(), -1, double.NaN,
                        expectedSpan, actualSpan, double.NaN, isBoundary, boundaryHits, "span consistent"));
                    continue;
                }

                // Span mismatch: look for split partners among unclaimed cells.
                List<int> partnerCells = FindSplitPartners(cells, cellIndex, byCell.Keys, expectedSpan, options);
                if (partnerCells.Count > 0)
                {
                    foreach (int partner in partnerCells)
                    {
                        splitPartnerCells.Add(partner);
                    }

                    List<int> allCellIndices = new List<int> { cellIndex };
                    allCellIndices.AddRange(partnerCells);
                    allCellIndices.Sort();

                    double splitElevation = NearestDatum(expectedSpaceSet.LevelGroupDatums, InteriorBoundary(actualSpan, expectedSpan, options.LevelBand));

                    spaceRecords.Add(new SpaceMatchRecord(
                        space.Guid, space.Name, expectedSpaceSet.Labels[space.Guid], SpaceMatchOutcome.Split, space.Location,
                        allCellIndices, new List<Guid>(), -1, double.NaN,
                        expectedSpan, actualSpan, splitElevation, isBoundary, boundaryHits,
                        string.Format("expected span does not fit one cell; {0} partner cell(s) cover the rest", partnerCells.Count)));
                    continue;
                }

                spaceRecords.Add(new SpaceMatchRecord(
                    space.Guid, space.Name, expectedSpaceSet.Labels[space.Guid], SpaceMatchOutcome.IncorrectlyBounded, space.Location,
                    new List<int> { cellIndex }, new List<Guid>(), -1, double.NaN,
                    expectedSpan, actualSpan, double.NaN, isBoundary, boundaryHits,
                    "cell span does not match the expected span and no partner cell explains the mismatch"));
            }

            // 4. Cell-level records: Matched/Merged for occupied cells, Extra for the rest (excluding split partners).
            List<CellMatchRecord> cellRecords = new List<CellMatchRecord>();
            foreach (CellGeometry cell in cells)
            {
                if (byCell.TryGetValue(cell.Index, out List<Space> occupants))
                {
                    SpaceMatchOutcome outcome = occupants.Count > 1 ? SpaceMatchOutcome.Merged : SpaceMatchOutcome.Matched;
                    cellRecords.Add(new CellMatchRecord(cell.Index, outcome, occupants.Select(x => x.Guid).OrderBy(x => x).ToList(), cell.Center, cell.Volume, CellSpan(cell)));
                }
                else if (!splitPartnerCells.Contains(cell.Index))
                {
                    cellRecords.Add(new CellMatchRecord(cell.Index, SpaceMatchOutcome.Extra, new List<Guid>(), cell.Center, cell.Volume, CellSpan(cell)));
                }
            }

            // 5. Panel contribution + missing-separator analysis (optional).
            List<Guid> orphanClusterPanelGuids = new List<Guid>();
            List<Guid> unusedInputPanelGuids = new List<Guid>();
            List<SeparatorFinding> separatorFindings = new List<SeparatorFinding>();

            if (adjacencyCluster != null)
            {
                orphanClusterPanelGuids = PanelContributionFinder.OrphanClusterPanels(adjacencyCluster).Select(x => x.Guid).ToList();
            }

            if (sourcePanels != null)
            {
                List<Panel> clusterPanels = adjacencyCluster?.GetPanels() ?? new List<Panel>();
                unusedInputPanelGuids = PanelContributionFinder.UnusedInputPanels(sourcePanels, clusterPanels).Select(x => x.Guid).ToList();

                List<Panel> sourcePanelList = sourcePanels.ToList();
                foreach (SpaceMatchRecord record in spaceRecords.Where(x => x.Outcome == SpaceMatchOutcome.Merged))
                {
                    foreach (Guid partnerGuid in record.PartnerGuids)
                    {
                        if (record.Guid.CompareTo(partnerGuid) >= 0)
                        {
                            continue; // one scan per unordered pair
                        }

                        Space a = expectedSpaceSet.TryGetSpace(record.Guid);
                        Space b = expectedSpaceSet.TryGetSpace(partnerGuid);
                        double[] spanA = expectedSpaceSet.LevelSpans.TryGetValue(record.Guid, out double[] sa) ? sa : null;
                        double[] spanB = expectedSpaceSet.LevelSpans.TryGetValue(partnerGuid, out double[] sb) ? sb : null;
                        separatorFindings.AddRange(SeparatorPanelFinder.Find(a, spanA, b, spanB, sourcePanelList, options));
                    }
                }
            }

            return new SpaceMatchReport(spaceRecords, cellRecords, expectedSpaceSet.LevelGroupDatums, orphanClusterPanelGuids, unusedInputPanelGuids, separatorFindings, expectedSpaceSet.Labels, doubleHeightSet);
        }

        private static double DistanceToCentre(Point3D location, CellGeometry cell)
        {
            if (location == null || cell?.Center == null)
            {
                return double.MaxValue;
            }

            return location.Distance(cell.Center);
        }

        private static int NearestCellIndex(List<CellGeometry> cells, Point3D location)
        {
            if (location == null)
            {
                return -1;
            }

            int best = -1;
            double bestDistance = double.MaxValue;
            foreach (CellGeometry cell in cells)
            {
                if (cell?.Center == null)
                {
                    continue;
                }

                double distance = location.Distance(cell.Center);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = cell.Index;
                }
            }

            return best;
        }

        private static double[] CellSpan(CellGeometry cell)
        {
            BoundingBox3D box = cell?.Shell?.GetBoundingBox();
            if (box?.Min == null || box.Max == null)
            {
                return null;
            }

            return new double[] { box.Min.Z, box.Max.Z };
        }

        private static bool SpanConsistent(double[] actual, double[] expected, double band)
        {
            if (actual == null || expected == null)
            {
                return false;
            }

            return System.Math.Abs(actual[0] - expected[0]) <= band && System.Math.Abs(actual[1] - expected[1]) <= band;
        }

        /// <summary>The actual-span endpoint that lies strictly inside the expected span (the interior
        /// boundary a short cell stops at) - the value <see cref="NearestDatum"/> snaps to a Split's
        /// reported elevation. <see cref="double.NaN"/> when neither endpoint is interior (e.g. both cells
        /// share the same interior boundary; the caller falls back to the mid-span in that case).</summary>
        private static double InteriorBoundary(double[] actual, double[] expected, double band)
        {
            if (actual == null || expected == null)
            {
                return double.NaN;
            }

            if (actual[0] > expected[0] + band)
            {
                return actual[0];
            }

            if (actual[1] < expected[1] - band)
            {
                return actual[1];
            }

            return (expected[0] + expected[1]) / 2.0;
        }

        private static double NearestDatum(IReadOnlyList<double> datums, double value)
        {
            if (datums == null || datums.Count == 0 || double.IsNaN(value))
            {
                return double.NaN;
            }

            return datums.OrderBy(d => System.Math.Abs(d - value)).First();
        }

        /// <summary>Unclaimed cells whose plan-footprint overlaps <paramref name="cellIndex"/>'s footprint by
        /// at least <see cref="SpaceMatchOptions.MinSplitPlanOverlap"/> of the smaller footprint AND whose
        /// vertical span sits inside the expected span (± band) - the evidence for a
        /// <see cref="SpaceMatchOutcome.Split"/> classification.</summary>
        private static List<int> FindSplitPartners(List<CellGeometry> cells, int cellIndex, IEnumerable<int> occupiedCells, double[] expectedSpan, SpaceMatchOptions options)
        {
            List<int> result = new List<int>();
            if (expectedSpan == null)
            {
                return result;
            }

            HashSet<int> occupied = new HashSet<int>(occupiedCells);
            CellGeometry cell = cells.First(x => x.Index == cellIndex);
            BoundingBox3D box = cell.Shell?.GetBoundingBox();
            if (box == null)
            {
                return result;
            }

            foreach (CellGeometry other in cells)
            {
                if (other.Index == cellIndex || occupied.Contains(other.Index))
                {
                    continue;
                }

                BoundingBox3D otherBox = other.Shell?.GetBoundingBox();
                if (otherBox == null)
                {
                    continue;
                }

                if (otherBox.Min.Z < expectedSpan[0] - options.LevelBand || otherBox.Max.Z > expectedSpan[1] + options.LevelBand)
                {
                    continue;
                }

                if (PlanOverlapFraction(box, otherBox) >= options.MinSplitPlanOverlap)
                {
                    result.Add(other.Index);
                }
            }

            return result;
        }

        private static double PlanOverlapFraction(BoundingBox3D a, BoundingBox3D b)
        {
            double overlapX = System.Math.Max(0, System.Math.Min(a.Max.X, b.Max.X) - System.Math.Max(a.Min.X, b.Min.X));
            double overlapY = System.Math.Max(0, System.Math.Min(a.Max.Y, b.Max.Y) - System.Math.Max(a.Min.Y, b.Min.Y));
            double overlapArea = overlapX * overlapY;

            double areaA = (a.Max.X - a.Min.X) * (a.Max.Y - a.Min.Y);
            double areaB = (b.Max.X - b.Min.X) * (b.Max.Y - b.Min.Y);
            double smaller = System.Math.Min(areaA, areaB);

            return smaller <= 0 ? 0 : overlapArea / smaller;
        }

        /// <summary>Double-height verification (plan §6 point 5): the cell spans the full expected
        /// (bottom-to-roof) span AND carries no near-horizontal boundary face within
        /// <see cref="SpaceMatchOptions.LevelBand"/> of an intermediate level-group datum inside its own
        /// plan footprint. Returns false with the violating datum in <paramref name="violationDatum"/>
        /// otherwise.</summary>
        private static bool CheckDoubleHeight(CellGeometry cell, double[] expectedSpan, IReadOnlyList<double> datums, SpaceMatchOptions options, out double violationDatum)
        {
            violationDatum = double.NaN;
            BoundingBox3D box = cell.Shell?.GetBoundingBox();
            if (box == null || expectedSpan == null)
            {
                return true;
            }

            List<double> intermediate = (datums ?? new List<double>())
                .Where(d => d > expectedSpan[0] + options.LevelBand && d < expectedSpan[1] - options.LevelBand)
                .ToList();
            if (intermediate.Count == 0)
            {
                return true;
            }

            double capNormalZ = System.Math.Cos(DEFAULT_CapNormalZ_ConeTolerance);
            foreach (Face3D face in cell.Shell?.Face3Ds ?? new List<Face3D>())
            {
                Plane plane = face?.GetPlane();
                if (plane == null)
                {
                    continue;
                }

                Vector3D normal = plane.Normal.Unit;
                if (System.Math.Abs(normal.Z) < capNormalZ)
                {
                    continue; // not a cap face
                }

                double elevation = normal.Z < 0 ? -plane.Origin.Z : plane.Origin.Z;
                foreach (double datum in intermediate)
                {
                    if (System.Math.Abs(elevation - datum) > options.LevelBand)
                    {
                        continue;
                    }

                    Point3D centroid = face.GetBoundingBox()?.GetCentroid();
                    if (centroid == null || centroid.X < box.Min.X || centroid.X > box.Max.X || centroid.Y < box.Min.Y || centroid.Y > box.Max.Y)
                    {
                        continue;
                    }

                    violationDatum = datum;
                    return false;
                }
            }

            return true;
        }
    }
}
