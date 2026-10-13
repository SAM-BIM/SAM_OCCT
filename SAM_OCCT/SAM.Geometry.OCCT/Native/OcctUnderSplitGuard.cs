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

        /// <summary>Closed, positive-volume, non-sliver and mutually non-overlapping cells.</summary>
        internal static List<string> ValidateCells(IReadOnlyList<OcctCell> cells, OcctBuildOptions options)
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

                if (!shell.IsClosed(options.Tolerance))
                {
                    failures.Add(string.Format("cell {0} is not closed", i));
                }

                double volume = System.Math.Abs(cells[i].Volume);
                double area = SurfaceArea(shell);
                BoundingBox3D boundingBox3D = shell.GetBoundingBox();
                double minExtent = boundingBox3D == null ? 0 : System.Math.Min(boundingBox3D.Width, System.Math.Min(boundingBox3D.Depth, boundingBox3D.Height));
                if (double.IsNaN(volume) || volume <= 0 || area <= 0)
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

            return shell.InternalPoint3D(options.FuzzyTolerance, options.Tolerance);
        }
    }
}
