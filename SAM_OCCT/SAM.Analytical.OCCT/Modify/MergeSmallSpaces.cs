// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OCCT
{
    public static partial class Modify
    {
        /// <summary>
        /// Merges unwanted tiny spaces/cells of an <see cref="AdjacencyCluster"/>
        /// into the best adjacent larger space. A space is treated as "small" when
        /// its floor area is below <paramref name="minArea"/> or its volume is below
        /// <paramref name="minVolume"/>. Each small space is merged across a shared
        /// internal boundary into the most suitable neighbour, the now-internal
        /// shared panels are dropped, and the surviving space picks up the small
        /// space's remaining panels, volume, and adjacency.
        /// </summary>
        /// <param name="adjacencyCluster">Adjacency cluster to clean. Not modified; a new cleaned cluster is returned.</param>
        /// <param name="mergedSpaces">Original small spaces that were successfully merged away.</param>
        /// <param name="unmergedSmallSpaces">Original small spaces that could not be merged (e.g. protected, isolated, or only stacked neighbours).</param>
        /// <param name="report">Human-readable, coded diagnostics describing what was merged and what could not be.</param>
        /// <param name="minArea">Minimum acceptable floor area in m². Spaces below this are merge candidates. Default 0.3.</param>
        /// <param name="minVolume">Optional minimum acceptable volume in m³. Spaces below this are merge candidates. Pass null to ignore volume. Default 0.5.</param>
        /// <param name="tolerance">Distance tolerance for geometry and level comparisons. Default <see cref="Tolerance.Distance"/>.</param>
        /// <param name="mergeMode">Strategy for choosing the merge target. Default <see cref="MergeSmallSpacesMode.LongestSharedBoundary"/>.</param>
        /// <param name="allowMergeExternal">When false, small spaces are not merged into volumeless/void (external) neighbour cells. Default false.</param>
        /// <param name="protectedSpaces">Optional spaces that must never be merged away or used as a merge target.</param>
        /// <returns>A new cleaned <see cref="AdjacencyCluster"/>, or null when no valid cluster was supplied.</returns>
        public static AdjacencyCluster MergeSmallSpaces(
            this AdjacencyCluster adjacencyCluster,
            out List<Space> mergedSpaces,
            out List<Space> unmergedSmallSpaces,
            out List<string> report,
            double minArea = 0.3,
            double? minVolume = 0.5,
            double tolerance = Tolerance.Distance,
            MergeSmallSpacesMode mergeMode = MergeSmallSpacesMode.LongestSharedBoundary,
            bool allowMergeExternal = false,
            IEnumerable<Space> protectedSpaces = null)
        {
            mergedSpaces = new List<Space>();
            unmergedSmallSpaces = new List<Space>();
            report = new List<string>();

            if (adjacencyCluster == null)
            {
                report.Add("SAM_OCCT_MERGE_INPUT_NULL: No AdjacencyCluster was supplied.");
                return null;
            }

            List<Space> spaces = adjacencyCluster.GetSpaces();
            if (spaces == null || spaces.Count == 0)
            {
                report.Add("SAM_OCCT_MERGE_INPUT_EMPTY: AdjacencyCluster contains no spaces.");
                return new AdjacencyCluster(adjacencyCluster);
            }

            List<Panel> panels = adjacencyCluster.GetPanels() ?? new List<Panel>();

            report.Add(string.Format(
                "SAM_OCCT_MERGE_PARAMETERS: minArea={0:0.###} m², minVolume={1}, tolerance={2:0.####}, mergeMode={3}, allowMergeExternal={4}, protected={5}, spaces={6}, panels={7}.",
                minArea,
                minVolume.HasValue ? string.Format("{0:0.###} m³", minVolume.Value) : "ignored",
                tolerance,
                mergeMode,
                allowMergeExternal,
                protectedSpaces?.Count() ?? 0,
                spaces.Count,
                panels.Count));

            HashSet<Guid> protectedGuids = new HashSet<Guid>(
                (protectedSpaces ?? Enumerable.Empty<Space>()).Where(x => x != null).Select(x => x.Guid));

            // Per-space geometric metrics.
            Dictionary<Guid, Space> spaceByGuid = new Dictionary<Guid, Space>();
            Dictionary<Guid, double> floorAreaByGuid = new Dictionary<Guid, double>();
            Dictionary<Guid, double> volumeByGuid = new Dictionary<Guid, double>();
            Dictionary<Guid, double> minZByGuid = new Dictionary<Guid, double>();
            Dictionary<Guid, double> maxZByGuid = new Dictionary<Guid, double>();

            foreach (Space space in spaces)
            {
                if (space == null)
                {
                    continue;
                }

                spaceByGuid[space.Guid] = space;
                floorAreaByGuid[space.Guid] = FloorArea(adjacencyCluster, space, tolerance, out double minZ, out double maxZ);
                minZByGuid[space.Guid] = minZ;
                maxZByGuid[space.Guid] = maxZ;
                volumeByGuid[space.Guid] = Volume(space);
            }

            // Shared internal boundary area between each pair of spaces.
            Dictionary<Guid, Dictionary<Guid, double>> sharedArea = new Dictionary<Guid, Dictionary<Guid, double>>();
            foreach (Panel panel in panels)
            {
                if (panel == null)
                {
                    continue;
                }

                List<Space> panelSpaces = adjacencyCluster.GetSpaces(panel);
                if (panelSpaces == null)
                {
                    continue;
                }

                List<Space> distinct = panelSpaces.Where(x => x != null)
                    .GroupBy(x => x.Guid).Select(x => x.First()).ToList();
                if (distinct.Count != 2)
                {
                    continue;
                }

                double area = PanelArea(panel);
                if (double.IsNaN(area) || area <= 0)
                {
                    continue;
                }

                AddSharedArea(sharedArea, distinct[0].Guid, distinct[1].Guid, area);
                AddSharedArea(sharedArea, distinct[1].Guid, distinct[0].Guid, area);
            }

            // Identify small spaces.
            List<Space> smallSpaces = new List<Space>();
            foreach (Space space in spaces.Where(x => x != null))
            {
                double area = floorAreaByGuid[space.Guid];
                double volume = volumeByGuid[space.Guid];

                bool smallByArea = !double.IsNaN(area) && area > 0 && area < minArea;
                bool smallByVolume = minVolume.HasValue && !double.IsNaN(volume) && volume > 0 && volume < minVolume.Value;
                if (smallByArea || smallByVolume)
                {
                    smallSpaces.Add(space);
                }
            }

            report.Add(string.Format("SAM_OCCT_MERGE_CANDIDATES: Found {0} small space(s) out of {1}.", smallSpaces.Count, spaces.Count));

            if (smallSpaces.Count == 0)
            {
                report.Add("SAM_OCCT_MERGE_NO_OP: No spaces fell below the supplied thresholds. Returning an unchanged adjacency cluster.");
                return new AdjacencyCluster(adjacencyCluster);
            }

            // Union-find over space Guids; merging a small space into a target unions their groups.
            Dictionary<Guid, Guid> parent = new Dictionary<Guid, Guid>();
            foreach (Guid guid in spaceByGuid.Keys)
            {
                parent[guid] = guid;
            }

            // Smallest spaces first so they merge into larger neighbours.
            foreach (Space small in smallSpaces.OrderBy(x => floorAreaByGuid[x.Guid]).ThenBy(x => volumeByGuid[x.Guid]))
            {
                double smallArea = floorAreaByGuid[small.Guid];
                double smallVolume = volumeByGuid[small.Guid];
                string smallLabel = SpaceLabel(small, smallArea, smallVolume);

                if (protectedGuids.Contains(small.Guid))
                {
                    unmergedSmallSpaces.Add(small);
                    report.Add(string.Format("SAM_OCCT_MERGE_UNMERGED: {0} is protected and was left unchanged.", smallLabel));
                    continue;
                }

                if (!sharedArea.TryGetValue(small.Guid, out Dictionary<Guid, double> neighbours) || neighbours.Count == 0)
                {
                    unmergedSmallSpaces.Add(small);
                    report.Add(string.Format("SAM_OCCT_MERGE_UNMERGED: {0} has no shared internal boundary with any neighbour.", smallLabel));
                    continue;
                }

                Guid? target = SelectTarget(
                    small,
                    neighbours,
                    mergeMode,
                    allowMergeExternal,
                    protectedGuids,
                    spaceByGuid,
                    floorAreaByGuid,
                    volumeByGuid,
                    minZByGuid,
                    maxZByGuid,
                    tolerance,
                    out string rejectionSummary);

                if (target == null)
                {
                    unmergedSmallSpaces.Add(small);
                    report.Add(string.Format("SAM_OCCT_MERGE_UNMERGED: {0} had no safe merge target ({1}).", smallLabel, rejectionSummary));
                    continue;
                }

                Space targetSpace = spaceByGuid[target.Value];
                Union(parent, small.Guid, target.Value);
                mergedSpaces.Add(small);
                report.Add(string.Format(
                    "SAM_OCCT_MERGE_MERGED: {0} merged into '{1}' across {2:0.###} m² of shared boundary.",
                    smallLabel,
                    targetSpace.Name,
                    neighbours[target.Value]));
            }

            // Build the cleaned cluster from the resulting merge groups.
            AdjacencyCluster result = Rebuild(adjacencyCluster, spaces, panels, parent, floorAreaByGuid, volumeByGuid, tolerance, out int droppedPanels);

            report.Add(string.Format("SAM_OCCT_MERGE_DROPPED_PANELS: Removed {0} now-internal shared panel(s).", droppedPanels));
            report.Add(string.Format(
                "SAM_OCCT_MERGE_RESULT: {0} space(s) and {1} panel(s) after merging (was {2} space(s) and {3} panel(s)). Merged {4}, unmerged {5}.",
                result.GetSpaces()?.Count ?? 0,
                result.GetPanels()?.Count ?? 0,
                spaces.Count,
                panels.Count,
                mergedSpaces.Count,
                unmergedSmallSpaces.Count));

            return result;
        }

        private static AdjacencyCluster Rebuild(
            AdjacencyCluster adjacencyCluster,
            List<Space> spaces,
            List<Panel> panels,
            Dictionary<Guid, Guid> parent,
            Dictionary<Guid, double> floorAreaByGuid,
            Dictionary<Guid, double> volumeByGuid,
            double tolerance,
            out int droppedPanels)
        {
            droppedPanels = 0;

            // Choose a representative original space per group (largest floor area, then volume).
            Dictionary<Guid, Space> representativeByRoot = new Dictionary<Guid, Space>();
            Dictionary<Guid, double> groupVolumeByRoot = new Dictionary<Guid, double>();
            foreach (Space space in spaces.Where(x => x != null))
            {
                Guid root = Find(parent, space.Guid);

                double volume = volumeByGuid[space.Guid];
                groupVolumeByRoot.TryGetValue(root, out double existingVolume);
                groupVolumeByRoot[root] = existingVolume + (double.IsNaN(volume) || volume < 0 ? 0 : volume);

                if (!representativeByRoot.TryGetValue(root, out Space current))
                {
                    representativeByRoot[root] = space;
                    continue;
                }

                if (Prefer(space, current, floorAreaByGuid, volumeByGuid))
                {
                    representativeByRoot[root] = space;
                }
            }

            AdjacencyCluster result = new AdjacencyCluster();
            Dictionary<Guid, Space> newSpaceByRoot = new Dictionary<Guid, Space>();
            foreach (KeyValuePair<Guid, Space> keyValuePair in representativeByRoot)
            {
                Space original = keyValuePair.Value;
                Space newSpace = new Space(original, original.Name, original.Location);

                double volume = groupVolumeByRoot[keyValuePair.Key];
                if (volume > 0)
                {
                    newSpace.SetValue(SpaceParameter.Volume, volume);
                }

                result.AddObject(newSpace);
                newSpaceByRoot[keyValuePair.Key] = newSpace;
            }

            foreach (Panel panel in panels.Where(x => x != null))
            {
                List<Space> panelSpaces = adjacencyCluster.GetSpaces(panel);
                List<Guid> roots = (panelSpaces ?? new List<Space>())
                    .Where(x => x != null && parent.ContainsKey(x.Guid))
                    .Select(x => Find(parent, x.Guid))
                    .Distinct()
                    .ToList();

                // A panel that previously separated two spaces now inside the same
                // merged group is no longer a boundary and is dropped.
                int validSpaceCount = panelSpaces?.Count(x => x != null && parent.ContainsKey(x.Guid)) ?? 0;
                if (validSpaceCount >= 2 && roots.Count == 1)
                {
                    droppedPanels++;
                    continue;
                }

                result.AddObject(panel);
                foreach (Guid root in roots)
                {
                    if (newSpaceByRoot.TryGetValue(root, out Space newSpace))
                    {
                        result.AddRelation(newSpace, panel);
                    }
                }
            }

            result = result.UpdateNormals(false, true, false, Tolerance.MacroDistance, tolerance);
            result.Normalize(false);
            result.UpdatePanelTypes(0);
            result.SetDefaultConstructionByPanelType();
            return result;
        }

        private static Guid? SelectTarget(
            Space small,
            Dictionary<Guid, double> neighbours,
            MergeSmallSpacesMode mergeMode,
            bool allowMergeExternal,
            HashSet<Guid> protectedGuids,
            Dictionary<Guid, Space> spaceByGuid,
            Dictionary<Guid, double> floorAreaByGuid,
            Dictionary<Guid, double> volumeByGuid,
            Dictionary<Guid, double> minZByGuid,
            Dictionary<Guid, double> maxZByGuid,
            double tolerance,
            out string rejectionSummary)
        {
            int rejectedProtected = 0;
            int rejectedExternal = 0;
            int rejectedLevel = 0;

            List<KeyValuePair<Guid, double>> candidates = new List<KeyValuePair<Guid, double>>();
            foreach (KeyValuePair<Guid, double> neighbour in neighbours)
            {
                if (!spaceByGuid.ContainsKey(neighbour.Key))
                {
                    continue;
                }

                if (protectedGuids.Contains(neighbour.Key))
                {
                    rejectedProtected++;
                    continue;
                }

                if (!allowMergeExternal && IsExternal(neighbour.Key, floorAreaByGuid, volumeByGuid))
                {
                    rejectedExternal++;
                    continue;
                }

                if (!OverlapsVertically(small.Guid, neighbour.Key, minZByGuid, maxZByGuid, tolerance))
                {
                    rejectedLevel++;
                    continue;
                }

                candidates.Add(neighbour);
            }

            rejectionSummary = string.Format("protected:{0}, external:{1}, different-level:{2}", rejectedProtected, rejectedExternal, rejectedLevel);

            if (candidates.Count == 0)
            {
                return null;
            }

            switch (mergeMode)
            {
                case MergeSmallSpacesMode.LargestNeighbour:
                    return candidates
                        .OrderByDescending(x => NonNegative(floorAreaByGuid[x.Key]))
                        .ThenByDescending(x => NonNegative(volumeByGuid[x.Key]))
                        .ThenByDescending(x => x.Value)
                        .First().Key;

                case MergeSmallSpacesMode.SameTypeFirst:
                    string smallType = SpaceType(small);
                    List<KeyValuePair<Guid, double>> sameType = candidates
                        .Where(x => string.Equals(SpaceType(spaceByGuid[x.Key]), smallType, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    List<KeyValuePair<Guid, double>> pool = sameType.Count > 0 ? sameType : candidates;
                    return pool.OrderByDescending(x => x.Value).First().Key;

                case MergeSmallSpacesMode.LongestSharedBoundary:
                default:
                    return candidates.OrderByDescending(x => x.Value).First().Key;
            }
        }

        private static bool Prefer(Space candidate, Space current, Dictionary<Guid, double> floorAreaByGuid, Dictionary<Guid, double> volumeByGuid)
        {
            double candidateArea = NonNegative(floorAreaByGuid[candidate.Guid]);
            double currentArea = NonNegative(floorAreaByGuid[current.Guid]);
            if (candidateArea != currentArea)
            {
                return candidateArea > currentArea;
            }

            return NonNegative(volumeByGuid[candidate.Guid]) > NonNegative(volumeByGuid[current.Guid]);
        }

        private static bool IsExternal(Guid guid, Dictionary<Guid, double> floorAreaByGuid, Dictionary<Guid, double> volumeByGuid)
        {
            double area = floorAreaByGuid[guid];
            double volume = volumeByGuid[guid];
            bool hasArea = !double.IsNaN(area) && area > 0;
            bool hasVolume = !double.IsNaN(volume) && volume > 0;
            return !hasArea && !hasVolume;
        }

        private static bool OverlapsVertically(Guid a, Guid b, Dictionary<Guid, double> minZByGuid, Dictionary<Guid, double> maxZByGuid, double tolerance)
        {
            double minA = minZByGuid[a];
            double maxA = maxZByGuid[a];
            double minB = minZByGuid[b];
            double maxB = maxZByGuid[b];
            if (double.IsNaN(minA) || double.IsNaN(maxA) || double.IsNaN(minB) || double.IsNaN(maxB))
            {
                return true;
            }

            double overlap = Math.Min(maxA, maxB) - Math.Max(minA, minB);
            return overlap > tolerance;
        }

        private static double FloorArea(AdjacencyCluster adjacencyCluster, Space space, double tolerance, out double minZ, out double maxZ)
        {
            minZ = double.NaN;
            maxZ = double.NaN;

            List<Panel> spacePanels = adjacencyCluster.GetPanels(space);
            if (spacePanels == null || spacePanels.Count == 0)
            {
                return double.NaN;
            }

            List<Tuple<double, double>> horizontal = new List<Tuple<double, double>>();
            foreach (Panel panel in spacePanels)
            {
                Face3D face3D = panel?.GetFace3D();
                if (face3D == null)
                {
                    continue;
                }

                BoundingBox3D boundingBox3D = face3D.GetBoundingBox();
                if (boundingBox3D != null)
                {
                    double low = boundingBox3D.Min.Z;
                    double high = boundingBox3D.Max.Z;
                    minZ = double.IsNaN(minZ) ? low : Math.Min(minZ, low);
                    maxZ = double.IsNaN(maxZ) ? high : Math.Max(maxZ, high);
                }

                Vector3D normal = face3D.GetPlane()?.Normal;
                if (normal == null || Math.Abs(normal.Z) < 0.9)
                {
                    continue;
                }

                double area = face3D.GetArea();
                if (double.IsNaN(area) || area <= 0)
                {
                    continue;
                }

                horizontal.Add(new Tuple<double, double>(boundingBox3D == null ? 0 : boundingBox3D.Min.Z, area));
            }

            if (horizontal.Count == 0)
            {
                return double.NaN;
            }

            double floorZ = horizontal.Min(x => x.Item1);
            return horizontal.Where(x => x.Item1 <= floorZ + tolerance).Sum(x => x.Item2);
        }

        private static double Volume(Space space)
        {
            if (space != null && space.TryGetValue(SpaceParameter.Volume, out double volume) && !double.IsNaN(volume))
            {
                return Math.Abs(volume);
            }

            return double.NaN;
        }

        private static double PanelArea(Panel panel)
        {
            Face3D face3D = panel?.GetFace3D();
            return face3D == null ? double.NaN : face3D.GetArea();
        }

        private static string SpaceType(Space space)
        {
            string name = space?.InternalCondition?.Name;
            return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
        }

        private static string SpaceLabel(Space space, double area, double volume)
        {
            string areaText = double.IsNaN(area) ? "n/a" : string.Format("{0:0.###} m²", area);
            string volumeText = double.IsNaN(volume) ? "n/a" : string.Format("{0:0.###} m³", volume);
            return string.Format("'{0}' (area {1}, volume {2})", space?.Name, areaText, volumeText);
        }

        private static double NonNegative(double value)
        {
            return double.IsNaN(value) || value < 0 ? 0 : value;
        }

        private static void AddSharedArea(Dictionary<Guid, Dictionary<Guid, double>> sharedArea, Guid from, Guid to, double area)
        {
            if (!sharedArea.TryGetValue(from, out Dictionary<Guid, double> inner))
            {
                inner = new Dictionary<Guid, double>();
                sharedArea[from] = inner;
            }

            inner.TryGetValue(to, out double existing);
            inner[to] = existing + area;
        }

        private static Guid Find(Dictionary<Guid, Guid> parent, Guid guid)
        {
            Guid root = guid;
            while (parent[root] != root)
            {
                root = parent[root];
            }

            // Path compression.
            while (parent[guid] != root)
            {
                Guid next = parent[guid];
                parent[guid] = root;
                guid = next;
            }

            return root;
        }

        private static void Union(Dictionary<Guid, Guid> parent, Guid a, Guid b)
        {
            Guid rootA = Find(parent, a);
            Guid rootB = Find(parent, b);
            if (rootA != rootB)
            {
                parent[rootA] = rootB;
            }
        }
    }
}
