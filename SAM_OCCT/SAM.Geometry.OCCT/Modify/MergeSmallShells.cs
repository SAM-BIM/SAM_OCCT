// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    public static partial class Modify
    {
        /// <summary>
        /// Merges unwanted tiny closed shells/cells into the best adjacent larger
        /// shell. The supplied shells are decoded into an OCCT cell complex so that
        /// merging uses real per-cell volumes and real shared-face adjacency rather
        /// than bounding-box estimates. A cell is treated as "small" when its floor
        /// footprint is below <paramref name="minArea"/> or its volume is below
        /// <paramref name="minVolume"/>. Each small cell is grouped with the most
        /// suitable face-adjacent neighbour and the groups are fused with OCCT
        /// <see cref="Query.ShellsUnion(IEnumerable{Shell}, out OcctCellComplexResult, OcctBuildOptions)"/>.
        /// This is the shell/Brep counterpart of the analytical <c>MergeSmallSpaces</c>
        /// clean-up.
        /// </summary>
        /// <param name="shells">Closed shells (e.g. converted from Breps) to clean.</param>
        /// <param name="mergedSmallShells">Decoded small cells that were merged into a neighbour.</param>
        /// <param name="unmergedSmallShells">Decoded small cells that could not be merged (isolated or protected).</param>
        /// <param name="report">Coded diagnostics describing what was merged and what could not be.</param>
        /// <param name="minArea">Minimum acceptable floor footprint in m². Cells below this are merge candidates. Default 0.3.</param>
        /// <param name="minVolume">Optional minimum acceptable volume in m³. Cells below this are merge candidates. Pass null to ignore volume. Default 0.5.</param>
        /// <param name="tolerance">Distance tolerance for footprint comparisons and OCCT build/union. Default <see cref="Tolerance.Distance"/>.</param>
        /// <param name="fuzzyTolerance">OCCT fuzzy tolerance for the cell build and union. Default <see cref="Tolerance.MacroDistance"/>.</param>
        /// <param name="mergeMode">Strategy for choosing the merge target. Default <see cref="MergeShellsMode.LongestSharedBoundary"/>.</param>
        /// <param name="protectedShells">Optional shells that must never be merged away or used as a merge target (matched to decoded cells by containment).</param>
        /// <returns>The cleaned set of shells, or null when no valid shells were supplied. When native OCCT is unavailable the input is returned unchanged.</returns>
        public static List<Shell> MergeSmallShells(
            IEnumerable<Shell> shells,
            out List<Shell> mergedSmallShells,
            out List<Shell> unmergedSmallShells,
            out List<string> report,
            double minArea = 0.3,
            double? minVolume = 0.5,
            double tolerance = Tolerance.Distance,
            double fuzzyTolerance = Tolerance.MacroDistance,
            MergeShellsMode mergeMode = MergeShellsMode.LongestSharedBoundary,
            IEnumerable<Shell> protectedShells = null)
        {
            mergedSmallShells = new List<Shell>();
            unmergedSmallShells = new List<Shell>();
            report = new List<string>();

            List<Shell> input = shells?.Where(x => x != null).ToList();
            if (input == null || input.Count == 0)
            {
                report.Add("SAM_OCCT_MERGE_SHELLS_INPUT_EMPTY: No shells were supplied.");
                return null;
            }

            List<Shell> protectedList = protectedShells?.Where(x => x != null).ToList() ?? new List<Shell>();

            report.Add(string.Format(
                "SAM_OCCT_MERGE_SHELLS_PARAMETERS: minArea={0:0.###} m², minVolume={1}, tolerance={2:0.####}, fuzzyTolerance={3:0.####}, mergeMode={4}, protected={5}, shells={6}.",
                minArea,
                minVolume.HasValue ? string.Format("{0:0.###} m³", minVolume.Value) : "ignored",
                tolerance,
                fuzzyTolerance,
                mergeMode,
                protectedList.Count,
                input.Count));

            // Decode the shells into an OCCT cell complex so merging uses real
            // volumes and real shared-face adjacency.
            List<Face3D> face3Ds = input
                .SelectMany(x => x.Face3Ds ?? new List<Face3D>())
                .Where(x => x != null)
                .ToList();
            if (face3Ds.Count == 0)
            {
                report.Add("SAM_OCCT_MERGE_SHELLS_NO_FACES: No Face3D geometry could be extracted from the supplied shells.");
                return input.ToList();
            }

            OcctBuildOptions options = new OcctBuildOptions { Tolerance = tolerance, FuzzyTolerance = fuzzyTolerance };
            Create.Shells(face3Ds, out OcctCellComplexResult result, options);
            if (result?.Diagnostics != null)
            {
                report.AddRange(result.Diagnostics.Select(x => x.ToString()));
            }

            IReadOnlyList<OcctCell> cells = result?.Cells;
            if (cells == null || cells.Count == 0)
            {
                report.Add("SAM_OCCT_MERGE_SHELLS_NO_CELLS: OCCT decoded no closed cells (native OCCT may be unavailable). Returning the input unchanged.");
                return input.ToList();
            }

            int count = cells.Count;
            report.Add(string.Format("SAM_OCCT_MERGE_SHELLS_TOPOLOGY: Decoded {0} cell(s) and {1} shared-face adjacency relation(s) from {2} input shell(s).", count, result.FaceAdjacencies?.Count ?? 0, input.Count));

            // Per-cell metrics.
            double[] volumes = new double[count];
            double[] footprints = new double[count];
            for (int i = 0; i < count; i++)
            {
                volumes[i] = double.IsNaN(cells[i].Volume) ? double.NaN : Math.Abs(cells[i].Volume);
                footprints[i] = Footprint(cells[i].Faces, tolerance);
            }

            // Real shared-face boundary area between adjacent cells.
            Dictionary<int, Dictionary<int, double>> sharedArea = new Dictionary<int, Dictionary<int, double>>();
            foreach (OcctCellFaceAdjacency adjacency in result.FaceAdjacencies ?? new List<OcctCellFaceAdjacency>())
            {
                int a = adjacency.CellIndex1;
                int b = adjacency.CellIndex2;
                if (a < 0 || a >= count || b < 0 || b >= count || a == b)
                {
                    continue;
                }

                double area = SharedFaceArea(cells[a], adjacency.FaceIndex1, cells[b], adjacency.FaceIndex2);
                if (double.IsNaN(area) || area <= 0)
                {
                    continue;
                }

                AddSharedArea(sharedArea, a, b, area);
                AddSharedArea(sharedArea, b, a, area);
            }

            HashSet<int> protectedIndices = ProtectedIndices(cells, protectedList, fuzzyTolerance, tolerance);

            // Identify small cells.
            List<int> smallIndices = new List<int>();
            for (int i = 0; i < count; i++)
            {
                bool smallByArea = !double.IsNaN(footprints[i]) && footprints[i] > 0 && footprints[i] < minArea;
                bool smallByVolume = minVolume.HasValue && !double.IsNaN(volumes[i]) && volumes[i] > 0 && volumes[i] < minVolume.Value;
                if (smallByArea || smallByVolume)
                {
                    smallIndices.Add(i);
                }
            }

            report.Add(string.Format("SAM_OCCT_MERGE_SHELLS_CANDIDATES: Found {0} small cell(s) out of {1}.", smallIndices.Count, count));

            if (smallIndices.Count == 0)
            {
                report.Add("SAM_OCCT_MERGE_SHELLS_NO_OP: No cells fell below the supplied thresholds. Returning the decoded cells unchanged.");
                return cells.Select(x => x.Shell).Where(x => x != null).ToList();
            }

            // Union-find over cell indices; merging a small cell into a target unions their groups.
            int[] parent = Enumerable.Range(0, count).ToArray();

            foreach (int i in smallIndices.OrderBy(x => NonNegative(footprints[x])).ThenBy(x => NonNegative(volumes[x])))
            {
                string label = CellLabel(i, footprints[i], volumes[i]);

                if (protectedIndices.Contains(i))
                {
                    unmergedSmallShells.Add(cells[i].Shell);
                    report.Add(string.Format("SAM_OCCT_MERGE_SHELLS_UNMERGED: {0} is protected and was left unchanged.", label));
                    continue;
                }

                if (!sharedArea.TryGetValue(i, out Dictionary<int, double> neighbours) || neighbours.Count == 0)
                {
                    unmergedSmallShells.Add(cells[i].Shell);
                    report.Add(string.Format("SAM_OCCT_MERGE_SHELLS_UNMERGED: {0} has no face-adjacent neighbour to merge into.", label));
                    continue;
                }

                int target = -1;
                double targetMetric = double.NegativeInfinity;
                double targetShared = 0;
                foreach (KeyValuePair<int, double> neighbour in neighbours)
                {
                    if (protectedIndices.Contains(neighbour.Key))
                    {
                        continue;
                    }

                    double metric = mergeMode == MergeShellsMode.LargestNeighbour
                        ? Math.Max(NonNegative(footprints[neighbour.Key]), NonNegative(volumes[neighbour.Key]))
                        : neighbour.Value;

                    if (metric > targetMetric)
                    {
                        targetMetric = metric;
                        target = neighbour.Key;
                        targetShared = neighbour.Value;
                    }
                }

                if (target == -1)
                {
                    unmergedSmallShells.Add(cells[i].Shell);
                    report.Add(string.Format("SAM_OCCT_MERGE_SHELLS_UNMERGED: {0} had only protected neighbours.", label));
                    continue;
                }

                Union(parent, i, target);
                mergedSmallShells.Add(cells[i].Shell);
                report.Add(string.Format("SAM_OCCT_MERGE_SHELLS_MERGED: {0} merged into cell {1} across {2:0.###} m² of shared boundary.", label, target, targetShared));
            }

            // Fuse each multi-member group with OCCT; pass singletons through unchanged.
            List<Shell> resultShells = new List<Shell>();
            int unionGroups = 0;
            int unionFailures = 0;
            foreach (IGrouping<int, int> group in Enumerable.Range(0, count).GroupBy(x => Find(parent, x)))
            {
                List<int> members = group.ToList();
                if (members.Count == 1)
                {
                    if (cells[members[0]].Shell != null)
                    {
                        resultShells.Add(cells[members[0]].Shell);
                    }

                    continue;
                }

                List<Shell> groupShells = members.Select(x => cells[x].Shell).Where(x => x != null).ToList();
                List<Shell> unioned = Query.ShellsUnion(groupShells, out OcctCellComplexResult unionResult, options);
                if (unioned == null || unioned.Count == 0)
                {
                    // Union failed; keep the originals so no geometry is lost.
                    unionFailures++;
                    resultShells.AddRange(groupShells);
                    if (unionResult?.Diagnostics != null)
                    {
                        report.AddRange(unionResult.Diagnostics.Select(x => x.ToString()));
                    }

                    continue;
                }

                unionGroups++;
                resultShells.AddRange(unioned);
            }

            report.Add(string.Format(
                "SAM_OCCT_MERGE_SHELLS_RESULT: {0} shell(s) after merging (decoded {1} cell(s) from {2} input shell(s)). Fused {3} group(s), {4} union failure(s). Merged {5}, unmerged {6}.",
                resultShells.Count,
                count,
                input.Count,
                unionGroups,
                unionFailures,
                mergedSmallShells.Count,
                unmergedSmallShells.Count));

            return resultShells;
        }

        private static HashSet<int> ProtectedIndices(IReadOnlyList<OcctCell> cells, List<Shell> protectedShells, double fuzzyTolerance, double tolerance)
        {
            HashSet<int> result = new HashSet<int>();
            if (protectedShells == null || protectedShells.Count == 0)
            {
                return result;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                Point3D center = cells[i].Center;
                if (center == null)
                {
                    continue;
                }

                foreach (Shell protectedShell in protectedShells)
                {
                    if (protectedShell.Inside(center, fuzzyTolerance, tolerance) || protectedShell.On(center, tolerance))
                    {
                        result.Add(i);
                        break;
                    }
                }
            }

            return result;
        }

        private static double SharedFaceArea(OcctCell cellA, int faceIndexA, OcctCell cellB, int faceIndexB)
        {
            Face3D face3D = FaceAt(cellA, faceIndexA) ?? FaceAt(cellB, faceIndexB);
            return face3D == null ? double.NaN : face3D.GetArea();
        }

        private static Face3D FaceAt(OcctCell cell, int faceIndex)
        {
            IReadOnlyList<OcctCellFace> faces = cell?.Faces;
            if (faces == null || faceIndex < 0 || faceIndex >= faces.Count)
            {
                return null;
            }

            return faces[faceIndex]?.Face3D;
        }

        private static double Footprint(IReadOnlyList<OcctCellFace> faces, double tolerance)
        {
            if (faces == null || faces.Count == 0)
            {
                return double.NaN;
            }

            List<Tuple<double, double>> horizontal = new List<Tuple<double, double>>();
            foreach (OcctCellFace cellFace in faces)
            {
                Face3D face3D = cellFace?.Face3D;
                if (face3D == null)
                {
                    continue;
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

                BoundingBox3D boundingBox3D = face3D.GetBoundingBox();
                horizontal.Add(new Tuple<double, double>(boundingBox3D == null ? 0 : boundingBox3D.Min.Z, area));
            }

            if (horizontal.Count == 0)
            {
                return double.NaN;
            }

            double floorZ = horizontal.Min(x => x.Item1);
            return horizontal.Where(x => x.Item1 <= floorZ + tolerance).Sum(x => x.Item2);
        }

        private static string CellLabel(int index, double footprint, double volume)
        {
            string footprintText = double.IsNaN(footprint) ? "n/a" : string.Format("{0:0.###} m²", footprint);
            string volumeText = double.IsNaN(volume) ? "n/a" : string.Format("{0:0.###} m³", volume);
            return string.Format("Cell {0} (footprint {1}, volume {2})", index, footprintText, volumeText);
        }

        private static double NonNegative(double value)
        {
            return double.IsNaN(value) || value < 0 ? 0 : value;
        }

        private static void AddSharedArea(Dictionary<int, Dictionary<int, double>> sharedArea, int from, int to, double area)
        {
            if (!sharedArea.TryGetValue(from, out Dictionary<int, double> inner))
            {
                inner = new Dictionary<int, double>();
                sharedArea[from] = inner;
            }

            inner.TryGetValue(to, out double existing);
            inner[to] = existing + area;
        }

        private static int Find(int[] parent, int index)
        {
            while (parent[index] != index)
            {
                parent[index] = parent[parent[index]];
                index = parent[index];
            }

            return index;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int rootA = Find(parent, a);
            int rootB = Find(parent, b);
            if (rootA != rootB)
            {
                parent[rootA] = rootB;
            }
        }
    }
}
