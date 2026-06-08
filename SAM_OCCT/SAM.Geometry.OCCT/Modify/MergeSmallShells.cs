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
        /// shell. A shell is treated as "small" when its floor footprint is below
        /// <paramref name="minArea"/> or its axis-aligned bounding-box volume is
        /// below <paramref name="minVolume"/>. Each small shell is grouped with the
        /// most suitable touching neighbour and the groups are fused with OCCT
        /// <see cref="Query.ShellsUnion(IEnumerable{Shell}, out OcctCellComplexResult, OcctBuildOptions)"/>.
        /// This is the shell/Brep counterpart of the analytical
        /// <c>MergeSmallSpaces</c> clean-up.
        /// </summary>
        /// <param name="shells">Closed shells (e.g. converted from Breps) to clean.</param>
        /// <param name="mergedSmallShells">Original small shells that were merged into a neighbour.</param>
        /// <param name="unmergedSmallShells">Original small shells that could not be merged (isolated or protected).</param>
        /// <param name="report">Coded diagnostics describing what was merged and what could not be.</param>
        /// <param name="minArea">Minimum acceptable floor footprint in m². Shells below this are merge candidates. Default 0.3.</param>
        /// <param name="minVolume">Optional minimum acceptable bounding-box volume in m³. Shells below this are merge candidates. Pass null to ignore volume. Default 0.5.</param>
        /// <param name="tolerance">Distance tolerance for adjacency and footprint comparisons. Default <see cref="Tolerance.Distance"/>.</param>
        /// <param name="fuzzyTolerance">OCCT fuzzy tolerance for the union step. Default <see cref="Tolerance.MacroDistance"/>.</param>
        /// <param name="mergeMode">Strategy for choosing the merge target. Default <see cref="MergeShellsMode.LongestSharedBoundary"/>.</param>
        /// <param name="protectedShells">Optional shells that must never be merged away or used as a merge target (matched by centroid/size).</param>
        /// <returns>The cleaned set of shells, or null when no valid shells were supplied.</returns>
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

            // Per-shell metrics.
            int count = input.Count;
            BoundingBox3D[] boundingBoxes = new BoundingBox3D[count];
            double[] footprints = new double[count];
            double[] boxVolumes = new double[count];
            for (int i = 0; i < count; i++)
            {
                boundingBoxes[i] = input[i].GetBoundingBox();
                footprints[i] = Footprint(input[i], tolerance);
                boxVolumes[i] = BoxVolume(boundingBoxes[i]);
            }

            HashSet<int> protectedIndices = ProtectedIndices(input, boundingBoxes, footprints, protectedShells, tolerance);

            report.Add(string.Format(
                "SAM_OCCT_MERGE_SHELLS_PARAMETERS: minArea={0:0.###} m², minVolume={1}, tolerance={2:0.####}, fuzzyTolerance={3:0.####}, mergeMode={4}, protected={5}, shells={6}.",
                minArea,
                minVolume.HasValue ? string.Format("{0:0.###} m³", minVolume.Value) : "ignored",
                tolerance,
                fuzzyTolerance,
                mergeMode,
                protectedIndices.Count,
                count));

            // Identify small shells.
            List<int> smallIndices = new List<int>();
            for (int i = 0; i < count; i++)
            {
                bool smallByArea = !double.IsNaN(footprints[i]) && footprints[i] > 0 && footprints[i] < minArea;
                bool smallByVolume = minVolume.HasValue && !double.IsNaN(boxVolumes[i]) && boxVolumes[i] > 0 && boxVolumes[i] < minVolume.Value;
                if (smallByArea || smallByVolume)
                {
                    smallIndices.Add(i);
                }
            }

            report.Add(string.Format("SAM_OCCT_MERGE_SHELLS_CANDIDATES: Found {0} small shell(s) out of {1}.", smallIndices.Count, count));

            if (smallIndices.Count == 0)
            {
                report.Add("SAM_OCCT_MERGE_SHELLS_NO_OP: No shells fell below the supplied thresholds. Returning the input unchanged.");
                return input.ToList();
            }

            // Union-find over shell indices; merging a small shell into a target unions their groups.
            int[] parent = Enumerable.Range(0, count).ToArray();
            HashSet<int> mergedIndices = new HashSet<int>();

            foreach (int i in smallIndices.OrderBy(x => NonNegative(footprints[x])).ThenBy(x => NonNegative(boxVolumes[x])))
            {
                string label = ShellLabel(i, footprints[i], boxVolumes[i]);

                if (protectedIndices.Contains(i))
                {
                    unmergedSmallShells.Add(input[i]);
                    report.Add(string.Format("SAM_OCCT_MERGE_SHELLS_UNMERGED: {0} is protected and was left unchanged.", label));
                    continue;
                }

                int target = -1;
                double targetMetric = double.NegativeInfinity;
                double targetContact = 0;
                for (int j = 0; j < count; j++)
                {
                    if (j == i || protectedIndices.Contains(j))
                    {
                        continue;
                    }

                    if (!Adjacent(boundingBoxes[i], boundingBoxes[j], tolerance))
                    {
                        continue;
                    }

                    double contact = ContactArea(boundingBoxes[i], boundingBoxes[j]);
                    double metric = mergeMode == MergeShellsMode.LargestNeighbour
                        ? Math.Max(NonNegative(footprints[j]), NonNegative(boxVolumes[j]))
                        : contact;

                    if (metric > targetMetric)
                    {
                        targetMetric = metric;
                        target = j;
                        targetContact = contact;
                    }
                }

                if (target == -1)
                {
                    unmergedSmallShells.Add(input[i]);
                    report.Add(string.Format("SAM_OCCT_MERGE_SHELLS_UNMERGED: {0} has no touching neighbour to merge into.", label));
                    continue;
                }

                Union(parent, i, target);
                mergedIndices.Add(i);
                mergedSmallShells.Add(input[i]);
                report.Add(string.Format("SAM_OCCT_MERGE_SHELLS_MERGED: {0} merged into shell {1} (estimated shared contact {2:0.###} m²).", label, target, targetContact));
            }

            // Fuse each multi-member group with OCCT; pass singletons through unchanged.
            List<Shell> result = new List<Shell>();
            int unionGroups = 0;
            int unionFailures = 0;
            foreach (IGrouping<int, int> group in Enumerable.Range(0, count).GroupBy(x => Find(parent, x)))
            {
                List<int> members = group.ToList();
                if (members.Count == 1)
                {
                    result.Add(input[members[0]]);
                    continue;
                }

                List<Shell> groupShells = members.Select(x => input[x]).ToList();
                List<Shell> unioned = Query.ShellsUnion(groupShells, out OcctCellComplexResult unionResult, new OcctBuildOptions { Tolerance = tolerance, FuzzyTolerance = fuzzyTolerance });
                if (unioned == null || unioned.Count == 0)
                {
                    // Union failed (e.g. native OCCT unavailable). Keep the originals so no geometry is lost.
                    unionFailures++;
                    result.AddRange(groupShells);
                    if (unionResult?.Diagnostics != null)
                    {
                        report.AddRange(unionResult.Diagnostics.Select(x => x.ToString()));
                    }

                    continue;
                }

                unionGroups++;
                result.AddRange(unioned);
            }

            report.Add(string.Format(
                "SAM_OCCT_MERGE_SHELLS_RESULT: {0} shell(s) after merging (was {1}). Fused {2} group(s), {3} union failure(s). Merged {4}, unmerged {5}.",
                result.Count,
                count,
                unionGroups,
                unionFailures,
                mergedSmallShells.Count,
                unmergedSmallShells.Count));

            return result;
        }

        private static HashSet<int> ProtectedIndices(List<Shell> shells, BoundingBox3D[] boundingBoxes, double[] footprints, IEnumerable<Shell> protectedShells, double tolerance)
        {
            HashSet<int> result = new HashSet<int>();
            List<Shell> protectedList = protectedShells?.Where(x => x != null).ToList();
            if (protectedList == null || protectedList.Count == 0)
            {
                return result;
            }

            foreach (Shell protectedShell in protectedList)
            {
                BoundingBox3D protectedBox = protectedShell.GetBoundingBox();
                Point3D protectedCentroid = protectedBox?.GetCentroid();
                if (protectedCentroid == null)
                {
                    continue;
                }

                for (int i = 0; i < shells.Count; i++)
                {
                    if (result.Contains(i) || boundingBoxes[i] == null)
                    {
                        continue;
                    }

                    Point3D centroid = boundingBoxes[i].GetCentroid();
                    if (centroid != null && centroid.Distance(protectedCentroid) <= tolerance)
                    {
                        result.Add(i);
                        break;
                    }
                }
            }

            return result;
        }

        private static bool Adjacent(BoundingBox3D a, BoundingBox3D b, double tolerance)
        {
            if (a == null || b == null)
            {
                return false;
            }

            return AxisOverlap(a.Min.X, a.Max.X, b.Min.X, b.Max.X) >= -tolerance
                && AxisOverlap(a.Min.Y, a.Max.Y, b.Min.Y, b.Max.Y) >= -tolerance
                && AxisOverlap(a.Min.Z, a.Max.Z, b.Min.Z, b.Max.Z) >= -tolerance;
        }

        private static double ContactArea(BoundingBox3D a, BoundingBox3D b)
        {
            if (a == null || b == null)
            {
                return 0;
            }

            double[] overlaps =
            {
                Math.Max(0, AxisOverlap(a.Min.X, a.Max.X, b.Min.X, b.Max.X)),
                Math.Max(0, AxisOverlap(a.Min.Y, a.Max.Y, b.Min.Y, b.Max.Y)),
                Math.Max(0, AxisOverlap(a.Min.Z, a.Max.Z, b.Min.Z, b.Max.Z))
            };

            Array.Sort(overlaps);
            // Product of the two largest axis overlaps approximates the touching face area.
            return overlaps[2] * overlaps[1];
        }

        private static double AxisOverlap(double minA, double maxA, double minB, double maxB)
        {
            return Math.Min(maxA, maxB) - Math.Max(minA, minB);
        }

        private static double BoxVolume(BoundingBox3D boundingBox)
        {
            if (boundingBox == null)
            {
                return double.NaN;
            }

            double dx = boundingBox.Max.X - boundingBox.Min.X;
            double dy = boundingBox.Max.Y - boundingBox.Min.Y;
            double dz = boundingBox.Max.Z - boundingBox.Min.Z;
            return dx * dy * dz;
        }

        private static double Footprint(Shell shell, double tolerance)
        {
            List<Face3D> face3Ds = shell?.Face3Ds;
            if (face3Ds == null || face3Ds.Count == 0)
            {
                return double.NaN;
            }

            List<Tuple<double, double>> horizontal = new List<Tuple<double, double>>();
            foreach (Face3D face3D in face3Ds)
            {
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

        private static string ShellLabel(int index, double footprint, double boxVolume)
        {
            string footprintText = double.IsNaN(footprint) ? "n/a" : string.Format("{0:0.###} m²", footprint);
            string volumeText = double.IsNaN(boxVolume) ? "n/a" : string.Format("{0:0.###} m³", boxVolume);
            return string.Format("Shell {0} (footprint {1}, bbox volume {2})", index, footprintText, volumeText);
        }

        private static double NonNegative(double value)
        {
            return double.IsNaN(value) || value < 0 ? 0 : value;
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
