// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Native
{
    /// <summary>
    /// Decides whether a direct MakerVolume build may replace a sew-then-MakerVolume build that is suspected of
    /// under-splitting (input faces cutting through a cell instead of bounding it). The direct candidate is adopted
    /// only when every criterion holds:
    /// <list type="number">
    /// <item>every direct cell is closed, has positive volume and is not a sliver;</item>
    /// <item>no two direct cells overlap;</item>
    /// <item>the direct complex refines the sew complex: each sew cell is exactly partitioned by direct cells;</item>
    /// <item>input-face coverage strictly improves and no face loses coverage.</item>
    /// </list>
    /// A sew build that is demonstrably invalid (a non-finite or non-positive signed cell volume, or overlapping
    /// cells) is not a valid reference for criteria 3 and 4. A replacement for it is judged on its own instead
    /// (<see cref="EvaluateReplacement"/>): criteria 1 and 2 plus complete input-face coverage.
    /// The checks run on decoded shells and are managed, except the exact overlap test of a cell pair that the
    /// managed separation tests cannot decide (<see cref="CheckDisjoint"/>): that uses the native boolean Common.
    /// </summary>
    internal static class OcctUnderSplitGuard
    {
        /// <summary>
        /// The volume common to two closed cells, or null when it cannot be computed. Production uses the native
        /// boolean Common (<see cref="OcctCellComplexBuilder.CommonVolume"/>); tests pass a stand-in.
        /// </summary>
        internal delegate double? CommonVolume(Shell first, Shell second);

        /// <summary>An exact overlap test that is never available: pairs the managed tests cannot decide fail.</summary>
        internal static readonly CommonVolume NoExactTest = (first, second) => null;

        internal sealed class Evaluation
        {
            public Evaluation(bool adopt, List<string> failures, FaceCoverageReport sewCoverage, FaceCoverageReport directCoverage)
            {
                Adopt = adopt;
                Failures = failures ?? new List<string>();
                SewCoverage = sewCoverage;
                DirectCoverage = directCoverage;
            }

            public bool Adopt { get; }

            public List<string> Failures { get; }

            public FaceCoverageReport SewCoverage { get; }

            public FaceCoverageReport DirectCoverage { get; }
        }

        /// <summary>True when the coverage of a sew build warrants evaluating a direct alternative.</summary>
        internal static bool IsSuspect(FaceCoverageReport coverage)
        {
            return coverage != null && (coverage.InteriorArea > 0 || coverage.UnusedArea > 0);
        }

        internal static Evaluation Evaluate(IReadOnlyList<Face3D> inputFaces, IReadOnlyList<OcctCell> sewCells, IReadOnlyList<OcctCell> directCells, OcctBuildOptions options, double minArea, FaceCoverageReport sewCoverage = null)
        {
            options = options ?? new OcctBuildOptions();
            List<string> failures = new List<string>();

            if (sewCoverage == null)
            {
                sewCoverage = Query.FaceCoverage(inputFaces, sewCells?.Select(x => x?.Shell), minArea, options.Tolerance, options.FuzzyTolerance);
            }

            if (directCells == null || directCells.Count == 0)
            {
                failures.Add("direct build produced no cells");
                return new Evaluation(false, failures, sewCoverage, null);
            }

            failures.AddRange(ValidateCells(directCells, options));
            failures.AddRange(CheckRefinement(sewCells, directCells, options));

            FaceCoverageReport directCoverage = Query.FaceCoverage(inputFaces, directCells.Select(x => x?.Shell), minArea, options.Tolerance, options.FuzzyTolerance);
            failures.AddRange(CheckCoverageImprovement(sewCoverage, directCoverage, System.Math.Max(options.Tolerance, options.FuzzyTolerance)));

            return new Evaluation(failures.Count == 0, failures, sewCoverage, directCoverage);
        }

        /// <summary>
        /// Judges a direct candidate that would replace a demonstrably invalid sew build. There is no valid sew
        /// complex to refine or to improve on, so the refinement and coverage-gain criteria do not apply. The
        /// candidate must stand on its own: valid cells (<see cref="ValidateCells"/>) and complete coverage of the
        /// input faces (<see cref="CheckCoverageComplete"/>).
        /// <para>
        /// Closure is judged at the build's own glue band, max(Tolerance, FuzzyTolerance), the band the coverage check
        /// already uses: MakerVolume glues geometry closer than the fuzzy tolerance, and decoded real-model cells show
        /// seams of 1e-6 to 1e-4 m. The under-split route (<see cref="Evaluate"/>) keeps its stricter Tolerance.
        /// </para>
        /// </summary>
        internal static Evaluation EvaluateReplacement(IReadOnlyList<Face3D> inputFaces, IReadOnlyList<OcctCell> directCells, OcctBuildOptions options, double minArea)
        {
            options = options ?? new OcctBuildOptions();
            List<string> failures = new List<string>();

            if (directCells == null || directCells.Count == 0)
            {
                failures.Add("direct build produced no cells");
                return new Evaluation(false, failures, null, null);
            }

            failures.AddRange(ValidateCells(directCells, options, System.Math.Max(options.Tolerance, options.FuzzyTolerance)));

            FaceCoverageReport directCoverage = Query.FaceCoverage(inputFaces, directCells.Select(x => x?.Shell), minArea, options.Tolerance, options.FuzzyTolerance);
            failures.AddRange(CheckCoverageComplete(directCoverage));

            return new Evaluation(failures.Count == 0, failures, null, directCoverage);
        }

        /// <summary>
        /// Signed cell volumes must be finite and positive. Runs before any absolute value is taken: an inverted
        /// cell (negative volume) would otherwise pass as a valid one.
        /// </summary>
        internal static List<string> CheckSignedVolumes(IReadOnlyList<OcctCell> cells, string label)
        {
            List<string> failures = new List<string>();
            if (cells == null || cells.Count == 0)
            {
                failures.Add("no cells");
                return failures;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                double volume = cells[i]?.Volume ?? double.NaN;
                if (cells[i]?.Shell == null)
                {
                    failures.Add(string.Format("{0} {1} has no shell", label, i));
                }
                else if (double.IsNaN(volume) || double.IsInfinity(volume))
                {
                    failures.Add(string.Format("{0} {1} has a non-finite volume", label, i));
                }
                else if (volume <= 0)
                {
                    failures.Add(string.Format("{0} {1} has a non-positive signed volume ({2:0.###} m3)", label, i, volume));
                }
            }

            return failures;
        }

        /// <summary>Sum of the signed cell volumes.</summary>
        internal static double TotalVolume(IReadOnlyList<OcctCell> cells)
        {
            return cells?.Sum(x => x?.Volume ?? 0) ?? 0;
        }

        /// <summary>
        /// Null when the two complexes enclose the same total volume (within 0.1 % plus tolerance times the input
        /// area); otherwise a description of the disagreement. A disagreement says that one of the two builds is
        /// wrong, not which one.
        /// </summary>
        internal static string CheckVolumeAgreement(IReadOnlyList<OcctCell> sewCells, IReadOnlyList<OcctCell> directCells, double inputArea, OcctBuildOptions options)
        {
            options = options ?? new OcctBuildOptions();
            double sewVolume = TotalVolume(sewCells);
            double directVolume = TotalVolume(directCells);
            double allowed = 1e-3 * System.Math.Max(System.Math.Abs(sewVolume), System.Math.Abs(directVolume)) + options.Tolerance * System.Math.Max(inputArea, 0);
            double difference = System.Math.Abs(sewVolume - directVolume);
            if (double.IsNaN(difference) || difference > allowed)
            {
                return string.Format("sew total volume {0:0.###} m3 differs from direct total volume {1:0.###} m3 by {2:0.###} m3 (allowed {3:0.###} m3)", sewVolume, directVolume, difference, allowed);
            }

            return null;
        }

        /// <summary>
        /// Cells of a complex must not share interior: <see cref="CheckDisjoint"/> with the native exact test.
        /// </summary>
        internal static List<string> CheckOverlap(IReadOnlyList<OcctCell> cells, IEnumerable<Point3D> probes, OcctBuildOptions options, string label)
        {
            return CheckDisjoint(cells, probes, options, label, null);
        }

        /// <summary>
        /// Cells of a complex must not share interior. Every pair must be shown disjoint; a pair that cannot be is a
        /// failure, so the check fails closed. Point samples can only prove an overlap, never disjointness.
        /// <list type="number">
        /// <item>Proof of overlap: a sample point inside two cells. The samples are each cell's internal point, the
        /// <paramref name="probes"/> (e.g. an alternative build's internal points, used only as locations) and the
        /// centre of each pair's bounding-box intersection.</item>
        /// <item>Proof of disjointness, managed and exact up to the glue band max(Tolerance, FuzzyTolerance):
        /// bounding boxes that overlap by no more than the band along some axis, or a face plane of either cell with
        /// all vertices of one cell on one side and all of the other on the other side. A cell lies in the convex
        /// hull of its vertices, so such a plane separates the cells (face-touching cells share it).</item>
        /// <item>Otherwise (e.g. a cell in the notch of an L-shaped one) the exact common volume, without pieces no
        /// thicker than the band (seams between touching cells): disjoint when it is 0, an overlap when positive, and
        /// a failure when it cannot be computed. A null <paramref name="commonVolume"/> is the native boolean Common.</item>
        /// </list>
        /// </summary>
        internal static List<string> CheckDisjoint(IReadOnlyList<OcctCell> cells, IEnumerable<Point3D> probes, OcctBuildOptions options, string label, CommonVolume commonVolume)
        {
            options = options ?? new OcctBuildOptions();
            List<string> failures = new List<string>();
            if (cells == null || cells.Count < 2)
            {
                return failures;
            }

            if (commonVolume == null)
            {
                OcctBuildOptions nativeOptions = options;
                commonVolume = (first, second) => OcctCellComplexBuilder.CommonVolume(first, second, nativeOptions);
            }

            double band = System.Math.Max(options.Tolerance, options.FuzzyTolerance);
            List<BoundingBox3D> boxes = cells.Select(x => x?.Shell?.GetBoundingBox()).ToList();
            List<Point3D> points = cells.Select(x => InternalPoint(x, options)).Where(x => x != null).ToList();
            if (probes != null)
            {
                points.AddRange(probes.Where(x => x != null));
            }

            HashSet<long> overlapping = new HashSet<long>();
            foreach (Point3D point3D in points)
            {
                List<int> containing = ContainingCells(cells, boxes, point3D, options);
                for (int i = 0; i < containing.Count - 1; i++)
                {
                    for (int j = i + 1; j < containing.Count; j++)
                    {
                        overlapping.Add(PairKey(containing[i], containing[j], cells.Count));
                    }
                }
            }

            List<Point3D>[] vertices = new List<Point3D>[cells.Count];
            SortedDictionary<long, string> messages = new SortedDictionary<long, string>();
            foreach (long key in overlapping)
            {
                messages[key] = string.Format("{0} {1} and {2} overlap", label, key / cells.Count, key % cells.Count);
            }

            for (int i = 0; i < cells.Count - 1; i++)
            {
                for (int j = i + 1; j < cells.Count; j++)
                {
                    long key = PairKey(i, j, cells.Count);
                    Shell first = cells[i]?.Shell;
                    Shell second = cells[j]?.Shell;
                    if (overlapping.Contains(key) || first == null || second == null || boxes[i] == null || boxes[j] == null || Separated(boxes[i], boxes[j], band))
                    {
                        continue;
                    }

                    Point3D centre = IntersectionCentre(boxes[i], boxes[j]);
                    if (first.Inside(centre, options.FuzzyTolerance, options.Tolerance) && second.Inside(centre, options.FuzzyTolerance, options.Tolerance))
                    {
                        messages[key] = string.Format("{0} {1} and {2} overlap", label, i, j);
                        continue;
                    }

                    vertices[i] = vertices[i] ?? Vertices(first);
                    vertices[j] = vertices[j] ?? Vertices(second);
                    if (SeparatedByFacePlane(first, vertices[i], vertices[j], band) || SeparatedByFacePlane(second, vertices[j], vertices[i], band))
                    {
                        continue;
                    }

                    double? common;
                    try
                    {
                        common = commonVolume(first, second);
                    }
                    catch (System.Exception)
                    {
                        common = null;
                    }

                    if (common == null || double.IsNaN(common.Value) || double.IsInfinity(common.Value))
                    {
                        messages[key] = string.Format("{0} {1} and {2}: disjointness could not be established (no separating plane; exact overlap test unavailable or inconclusive)", label, i, j);
                    }
                    else if (System.Math.Abs(common.Value) > 0)
                    {
                        messages[key] = string.Format("{0} {1} and {2} overlap (common volume {3:0.###} m3)", label, i, j, System.Math.Abs(common.Value));
                    }
                }
            }

            failures.AddRange(messages.Values);
            return failures;
        }

        /// <summary>
        /// Absolute coverage of a candidate judged on its own: every evaluated input face is covered or legitimately
        /// trimmed. No face may cut through a cell, be left unused outside every cell or be unresolved.
        /// </summary>
        internal static List<string> CheckCoverageComplete(FaceCoverageReport coverage)
        {
            List<string> failures = new List<string>();
            if (coverage == null)
            {
                failures.Add("coverage unavailable");
                return failures;
            }

            if (!coverage.IsClean)
            {
                failures.Add(string.Format("input-face coverage is incomplete ({0})", coverage.Summary()));
            }

            return failures;
        }

        /// <summary>The internal points of <paramref name="cells"/> (null entries skipped).</summary>
        internal static List<Point3D> InternalPoints(IReadOnlyList<OcctCell> cells, OcctBuildOptions options)
        {
            options = options ?? new OcctBuildOptions();
            return cells?.Select(x => InternalPoint(x, options)).Where(x => x != null).ToList() ?? new List<Point3D>();
        }

        /// <summary>
        /// Closed, positive-volume, non-sliver and mutually disjoint cells (<see cref="CheckDisjoint"/>). Closure is
        /// tested at <paramref name="closureTolerance"/>, by default <see cref="OcctBuildOptions.Tolerance"/>. The exact
        /// overlap test is <paramref name="commonVolume"/>, by default the native boolean Common.
        /// </summary>
        internal static List<string> ValidateCells(IReadOnlyList<OcctCell> cells, OcctBuildOptions options, double? closureTolerance = null, CommonVolume commonVolume = null)
        {
            List<string> failures = new List<string>();
            if (cells == null)
            {
                failures.Add("no cells");
                return failures;
            }

            List<Point3D> internalPoints = new List<Point3D>();
            for (int i = 0; i < cells.Count; i++)
            {
                Shell shell = cells[i]?.Shell;
                if (shell == null)
                {
                    failures.Add(string.Format("cell {0} has no shell", i));
                    internalPoints.Add(null);
                    continue;
                }

                if (!shell.IsClosed(closureTolerance ?? options.Tolerance))
                {
                    failures.Add(string.Format("cell {0} is not closed", i));
                }

                // The signed volume is checked first: an inverted cell has a negative volume whose absolute value
                // would otherwise look valid.
                double volume = cells[i].Volume;
                double area = SurfaceArea(shell);
                BoundingBox3D boundingBox3D = shell.GetBoundingBox();
                double minExtent = boundingBox3D == null ? 0 : System.Math.Min(boundingBox3D.Width, System.Math.Min(boundingBox3D.Depth, boundingBox3D.Height));
                if (double.IsNaN(volume) || double.IsInfinity(volume))
                {
                    failures.Add(string.Format("cell {0} has a non-finite volume", i));
                }
                else if (volume < 0)
                {
                    failures.Add(string.Format("cell {0} has a negative signed volume ({1:0.###} m3)", i, volume));
                }
                else if (volume == 0 || area <= 0)
                {
                    failures.Add(string.Format("cell {0} has no volume", i));
                }
                else if (2 * volume / area < options.FuzzyTolerance || minExtent < options.FuzzyTolerance)
                {
                    // Mean thickness (volume per half surface area) or bounding-box extent below the fuzzy
                    // tolerance: a sliver cell.
                    failures.Add(string.Format("cell {0} is a sliver (mean thickness {1:0.#####}, min extent {2:0.#####})", i, 2 * volume / area, minExtent));
                }

                internalPoints.Add(InternalPoint(cells[i], options));
            }

            for (int i = 0; i < cells.Count; i++)
            {
                if (internalPoints[i] == null)
                {
                    failures.Add(string.Format("cell {0} has no internal point", i));
                }
            }

            failures.AddRange(CheckDisjoint(cells, null, options, "cells", commonVolume));
            return failures;
        }

        /// <summary>
        /// Every coarse (sew) cell must be exactly partitioned by the fine (direct) cells whose internal points it
        /// contains; fine cells outside every coarse cell are allowed (space the coarse build left empty).
        /// </summary>
        internal static List<string> CheckRefinement(IReadOnlyList<OcctCell> coarseCells, IReadOnlyList<OcctCell> fineCells, OcctBuildOptions options)
        {
            List<string> failures = new List<string>();
            if (coarseCells == null || fineCells == null)
            {
                return failures;
            }

            double[] memberVolumes = new double[coarseCells.Count];
            for (int j = 0; j < fineCells.Count; j++)
            {
                Point3D point3D = InternalPoint(fineCells[j], options);
                if (point3D == null)
                {
                    continue;
                }

                int parents = 0;
                for (int i = 0; i < coarseCells.Count; i++)
                {
                    Shell shell = coarseCells[i]?.Shell;
                    if (shell != null && (shell.Inside(point3D, options.FuzzyTolerance, options.Tolerance) || shell.On(point3D, options.Tolerance)))
                    {
                        memberVolumes[i] += System.Math.Abs(fineCells[j].Volume);
                        parents++;
                    }
                }

                if (parents > 1)
                {
                    failures.Add(string.Format("direct cell {0} lies in {1} sew cells", j, parents));
                }
            }

            for (int i = 0; i < coarseCells.Count; i++)
            {
                double volume = System.Math.Abs(coarseCells[i]?.Volume ?? 0);
                double area = SurfaceArea(coarseCells[i]?.Shell);
                double allowed = 1e-3 * volume + options.Tolerance * area;
                if (System.Math.Abs(memberVolumes[i] - volume) > allowed)
                {
                    failures.Add(string.Format("sew cell {0} ({1:0.###}) is not partitioned by direct cells ({2:0.###})", i, volume, memberVolumes[i]));
                }
            }

            return failures;
        }

        /// <summary>Total covered area must strictly increase, and no single face may lose coverage (beyond a
        /// <paramref name="tolerance"/>-wide boundary band).</summary>
        internal static List<string> CheckCoverageImprovement(FaceCoverageReport before, FaceCoverageReport after, double tolerance)
        {
            List<string> failures = new List<string>();
            if (before == null || after == null)
            {
                failures.Add("coverage unavailable");
                return failures;
            }

            int regressed = 0;
            foreach (FaceCoverageEntry entry in before.Entries)
            {
                FaceCoverageEntry other = after.Entry(entry.FaceIndex);
                double sliver = System.Math.Max(4 * tolerance * System.Math.Sqrt(entry.Area), tolerance * tolerance);
                if (other == null || other.CoveredArea < entry.CoveredArea - sliver)
                {
                    regressed++;
                }
            }

            if (regressed > 0)
            {
                failures.Add(string.Format("{0} input face(s) lose coverage", regressed));
            }

            double gain = after.CoveredArea - before.CoveredArea;
            if (gain <= tolerance * System.Math.Sqrt(System.Math.Max(before.InputArea, tolerance)))
            {
                failures.Add(string.Format("coverage does not improve ({0:0.###} -> {1:0.###})", before.CoveredArea, after.CoveredArea));
            }

            if (after.UnresolvedArea > before.UnresolvedArea + tolerance)
            {
                failures.Add(string.Format("unresolved area grows ({0:0.###} -> {1:0.###})", before.UnresolvedArea, after.UnresolvedArea));
            }

            return failures;
        }

        private static double SurfaceArea(Shell shell)
        {
            double area = 0;
            foreach (Face3D face3D in shell?.Face3Ds ?? new List<Face3D>())
            {
                double faceArea = face3D?.GetArea() ?? double.NaN;
                if (!double.IsNaN(faceArea))
                {
                    area += faceArea;
                }
            }

            return area;
        }

        private static long PairKey(int i, int j, int count)
        {
            return (long)i * count + j;
        }

        private static List<int> ContainingCells(IReadOnlyList<OcctCell> cells, List<BoundingBox3D> boxes, Point3D point3D, OcctBuildOptions options)
        {
            List<int> containing = new List<int>();
            for (int i = 0; i < cells.Count; i++)
            {
                Shell shell = cells[i]?.Shell;
                if (shell == null || (boxes[i] != null && !boxes[i].Inside(point3D, true, System.Math.Max(options.Tolerance, options.FuzzyTolerance))))
                {
                    continue;
                }

                if (shell.Inside(point3D, options.FuzzyTolerance, options.Tolerance))
                {
                    containing.Add(i);
                }
            }

            return containing;
        }

        // Interiors are disjoint when the boxes overlap by no more than the band along some axis.
        private static bool Separated(BoundingBox3D first, BoundingBox3D second, double band)
        {
            return System.Math.Min(first.Max.X, second.Max.X) - System.Math.Max(first.Min.X, second.Min.X) <= band
                || System.Math.Min(first.Max.Y, second.Max.Y) - System.Math.Max(first.Min.Y, second.Min.Y) <= band
                || System.Math.Min(first.Max.Z, second.Max.Z) - System.Math.Max(first.Min.Z, second.Min.Z) <= band;
        }

        private static Point3D IntersectionCentre(BoundingBox3D first, BoundingBox3D second)
        {
            return new Point3D(
                (System.Math.Max(first.Min.X, second.Min.X) + System.Math.Min(first.Max.X, second.Max.X)) / 2,
                (System.Math.Max(first.Min.Y, second.Min.Y) + System.Math.Min(first.Max.Y, second.Max.Y)) / 2,
                (System.Math.Max(first.Min.Z, second.Min.Z) + System.Math.Min(first.Max.Z, second.Max.Z)) / 2);
        }

        // True when a face plane of shell puts all its own vertices on one side (within the band) and all vertices of
        // the other shell on the other side. Both cells lie in the convex hulls of their vertices, so the plane
        // separates them up to a band-thick slab.
        private static bool SeparatedByFacePlane(Shell shell, List<Point3D> vertices, List<Point3D> otherVertices, double band)
        {
            if (vertices.Count == 0 || otherVertices.Count == 0)
            {
                return false;
            }

            foreach (Face3D face3D in shell.Face3Ds ?? new List<Face3D>())
            {
                Plane plane = face3D?.GetPlane();
                if (plane == null)
                {
                    continue;
                }

                SignedRange(plane, vertices, out double min, out double max);
                SignedRange(plane, otherVertices, out double otherMin, out double otherMax);
                if ((max <= band && otherMin >= -band) || (min >= -band && otherMax <= band))
                {
                    return true;
                }
            }

            return false;
        }

        private static void SignedRange(Plane plane, List<Point3D> point3Ds, out double min, out double max)
        {
            Vector3D normal = plane.Normal;
            Point3D origin = plane.Origin;
            double length = System.Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
            min = double.MaxValue;
            max = double.MinValue;
            foreach (Point3D point3D in point3Ds)
            {
                double distance = (normal.X * (point3D.X - origin.X) + normal.Y * (point3D.Y - origin.Y) + normal.Z * (point3D.Z - origin.Z)) / length;
                min = System.Math.Min(min, distance);
                max = System.Math.Max(max, distance);
            }
        }

        // Every face's outer-loop vertices; inner loops lie inside their face, so the convex hull is the same. Empty
        // (no separating plane can be trusted) when any face has a boundary that is not a polygon.
        private static List<Point3D> Vertices(Shell shell)
        {
            List<Point3D> vertices = new List<Point3D>();
            foreach (Face3D face3D in shell?.Face3Ds ?? new List<Face3D>())
            {
                List<Point3D> point3Ds = (face3D?.GetExternalEdge3D() as ISegmentable3D)?.GetPoints();
                if (point3Ds == null || point3Ds.Count == 0)
                {
                    return new List<Point3D>();
                }

                vertices.AddRange(point3Ds);
            }

            return vertices;
        }

        private static Point3D InternalPoint(OcctCell cell, OcctBuildOptions options)
        {
            Shell shell = cell?.Shell;
            if (shell == null)
            {
                return null;
            }

            if (cell.Center != null && shell.Inside(cell.Center, options.FuzzyTolerance, options.Tolerance))
            {
                return cell.Center;
            }

            Point3D point3D = shell.InternalPoint3D(options.FuzzyTolerance, options.Tolerance);
            if (point3D != null)
            {
                return point3D;
            }

            // Fallback, verified by the same Inside test: the vertex centroid. Thin tetrahedral cells (wedges under a
            // pitched roof in a direct build) defeat both the native centre and Shell.InternalPoint3D, but their
            // vertex centroid lies well inside them.
            List<Point3D> vertices = new List<Point3D>();
            foreach (Face3D face3D in shell.Face3Ds ?? new List<Face3D>())
            {
                if (face3D?.GetExternalEdge3D() is ISegmentable3D segmentable3D)
                {
                    vertices.AddRange(segmentable3D.GetPoints() ?? new List<Point3D>());
                }
            }

            if (vertices.Count == 0)
            {
                return null;
            }

            Point3D centroid = new Point3D(vertices.Average(x => x.X), vertices.Average(x => x.Y), vertices.Average(x => x.Z));
            return shell.Inside(centroid, options.FuzzyTolerance, options.Tolerance) ? centroid : null;
        }
    }
}
