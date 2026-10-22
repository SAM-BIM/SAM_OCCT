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
    /// Pure managed; the checks run on decoded shells only.
    /// </summary>
    internal static class OcctUnderSplitGuard
    {
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
        /// Cells of a complex must not share interior. Every cell's own internal point, plus any extra
        /// <paramref name="probes"/> (e.g. the internal points of an alternative build, used only as sample
        /// locations), must lie inside at most one cell. A point inside two cells proves an overlap, such as a
        /// duplicated envelope cell around the rooms.
        /// </summary>
        internal static List<string> CheckOverlap(IReadOnlyList<OcctCell> cells, IEnumerable<Point3D> probes, OcctBuildOptions options, string label)
        {
            options = options ?? new OcctBuildOptions();
            List<string> failures = new List<string>();
            if (cells == null || cells.Count < 2)
            {
                return failures;
            }

            List<BoundingBox3D> boxes = cells.Select(x => x?.Shell?.GetBoundingBox(options.FuzzyTolerance)).ToList();
            List<Point3D> points = cells.Select(x => InternalPoint(x, options)).Where(x => x != null).ToList();
            if (probes != null)
            {
                points.AddRange(probes.Where(x => x != null));
            }

            HashSet<string> pairs = new HashSet<string>();
            foreach (Point3D point3D in points)
            {
                List<int> containing = new List<int>();
                for (int i = 0; i < cells.Count; i++)
                {
                    Shell shell = cells[i]?.Shell;
                    if (shell == null || (boxes[i] != null && !boxes[i].Inside(point3D, true, options.Tolerance)))
                    {
                        continue;
                    }

                    if (shell.Inside(point3D, options.FuzzyTolerance, options.Tolerance))
                    {
                        containing.Add(i);
                    }
                }

                for (int i = 0; i < containing.Count - 1; i++)
                {
                    for (int j = i + 1; j < containing.Count; j++)
                    {
                        pairs.Add(string.Format("{0} {1} and {2}", label, containing[i], containing[j]));
                    }
                }
            }

            foreach (string pair in pairs.OrderBy(x => x, System.StringComparer.Ordinal))
            {
                failures.Add(string.Format("{0} overlap", pair));
            }

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
        /// Closed, positive-volume, non-sliver and mutually non-overlapping cells. Closure is tested at
        /// <paramref name="closureTolerance"/>, by default <see cref="OcctBuildOptions.Tolerance"/>.
        /// </summary>
        internal static List<string> ValidateCells(IReadOnlyList<OcctCell> cells, OcctBuildOptions options, double? closureTolerance = null)
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
                Point3D point3D = internalPoints[i];
                if (point3D == null)
                {
                    failures.Add(string.Format("cell {0} has no internal point", i));
                    continue;
                }

                for (int j = 0; j < cells.Count; j++)
                {
                    if (i != j && cells[j]?.Shell != null && cells[j].Shell.Inside(point3D, options.FuzzyTolerance, options.Tolerance))
                    {
                        failures.Add(string.Format("cells {0} and {1} overlap", i, j));
                    }
                }
            }

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
