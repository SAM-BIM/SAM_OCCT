// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Geometry.Planar;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// Measures how the input faces of a cell-complex build are represented by the built cells. Pure managed;
        /// no native call.
        /// <para>
        /// For each input face (area at least <paramref name="minArea"/>), the covered region is the part of the
        /// face that coincides with coplanar cell faces. Coplanar means normals are parallel in either orientation
        /// within <paramref name="angleTolerance"/> and the plane offset is at most <paramref name="tolerance"/>.
        /// Coverage is measured by area, so a face split into many cell faces still counts as fully covered.
        /// </para>
        /// <para>
        /// Each uncovered piece is classified at an internal point offset by a small distance to both sides of the
        /// face:
        /// <list type="bullet">
        /// <item>both sides outside every cell: legitimate trimming (eaves, flaps past a junction);</item>
        /// <item>both sides inside the same cell: the face cuts through the cell without bounding it, the under-split
        /// signature;</item>
        /// <item>anything else: an unresolved inconsistency.</item>
        /// </list>
        /// </para>
        /// <para>
        /// A dangling partition stub that legitimately stops inside a room is also reported as interior. Callers
        /// compare candidate complexes rather than treating interior area as an error on its own.
        /// </para>
        /// </summary>
        /// <param name="inputFaces">The faces that were handed to the build, in input order.</param>
        /// <param name="cells">The closed cell shells the build produced.</param>
        /// <param name="minArea">Faces below this area are not evaluated.</param>
        /// <param name="tolerance">Distance tolerance (plane offset, polygon booleans).</param>
        /// <param name="fuzzyTolerance">Silver spacing used for the point-in-cell tests.</param>
        /// <param name="angleTolerance">Angle tolerance (radians) for parallel normals.</param>
        public static FaceCoverageReport FaceCoverage(IEnumerable<Face3D> inputFaces, IEnumerable<Shell> cells, double minArea = Tolerance.MacroDistance, double tolerance = Tolerance.Distance, double fuzzyTolerance = Tolerance.MacroDistance, double angleTolerance = Tolerance.Angle)
        {
            List<Shell> shells = cells?.Where(x => x != null).ToList() ?? new List<Shell>();
            List<CoverageCellFace> pool = new List<CoverageCellFace>();
            List<BoundingBox3D> shellBoxes = new List<BoundingBox3D>();
            foreach (Shell shell in shells)
            {
                shellBoxes.Add(shell.GetBoundingBox(fuzzyTolerance));
                foreach (Face3D face3D in shell.Face3Ds ?? new List<Face3D>())
                {
                    Plane plane = face3D?.GetPlane();
                    BoundingBox3D boundingBox3D = face3D?.GetBoundingBox(tolerance);
                    if (plane != null && boundingBox3D != null)
                    {
                        pool.Add(new CoverageCellFace(face3D, plane, boundingBox3D));
                    }
                }
            }

            double cosAngle = System.Math.Cos(angleTolerance);
            // OCCT glues faces within the fuzzy tolerance, so geometry closer than it is the same geometry.
            double band = System.Math.Max(tolerance, fuzzyTolerance);
            double offset = System.Math.Max(10 * tolerance, 2 * fuzzyTolerance);
            List<FaceCoverageEntry> entries = new List<FaceCoverageEntry>();

            int index = -1;
            foreach (Face3D face3D in inputFaces ?? Enumerable.Empty<Face3D>())
            {
                index++;
                if (face3D == null)
                {
                    continue;
                }

                double area = face3D.GetArea();
                if (double.IsNaN(area) || area < minArea || area <= 0)
                {
                    continue;
                }

                Plane plane = face3D.GetPlane();
                Face2D face2D = plane?.Convert(face3D);
                if (face2D == null)
                {
                    continue;
                }

                // A piece of remainder smaller than a tolerance-wide strip along the boundary is numerical noise.
                double sliverArea = System.Math.Max(4 * band * System.Math.Sqrt(area), band * band);

                BoundingBox3D faceBox = face3D.GetBoundingBox(tolerance);
                List<Face2D> candidates = new List<Face2D>();
                foreach (CoverageCellFace cellFace in pool)
                {
                    if (!faceBox.InRange(cellFace.BoundingBox3D, band))
                    {
                        continue;
                    }

                    double dot = System.Math.Abs(plane.Normal.DotProduct(cellFace.Plane.Normal));
                    if (dot < cosAngle || plane.Distance(cellFace.Plane.Origin) > band)
                    {
                        continue;
                    }

                    Face2D candidate = plane.Convert(cellFace.Face3D);
                    if (candidate != null)
                    {
                        candidates.Add(candidate);
                    }
                }

                List<Face2D> remainder;
                if (candidates.Count == 0)
                {
                    remainder = new List<Face2D> { face2D };
                }
                else
                {
                    remainder = face2D.Difference(candidates, tolerance) ?? new List<Face2D>();
                }

                double remainderArea = remainder.Sum(x => x?.GetArea() ?? 0);
                double coveredArea = System.Math.Max(0, area - remainderArea);
                double interiorArea = 0, exteriorArea = 0, unresolvedArea = 0;

                foreach (Face2D piece in remainder)
                {
                    double pieceArea = piece?.GetArea() ?? 0;
                    if (pieceArea < sliverArea)
                    {
                        continue;
                    }

                    Point3D point3D = plane.Convert(piece)?.GetInternalPoint3D(tolerance);
                    if (point3D == null)
                    {
                        unresolvedArea += pieceArea;
                        continue;
                    }

                    Vector3D normal = plane.Normal;
                    Point3D above = new Point3D(point3D.X + normal.X * offset, point3D.Y + normal.Y * offset, point3D.Z + normal.Z * offset);
                    Point3D below = new Point3D(point3D.X - normal.X * offset, point3D.Y - normal.Y * offset, point3D.Z - normal.Z * offset);
                    int cellAbove = ContainingCell(shells, shellBoxes, above, fuzzyTolerance, tolerance);
                    int cellBelow = ContainingCell(shells, shellBoxes, below, fuzzyTolerance, tolerance);

                    if (cellAbove < 0 && cellBelow < 0)
                    {
                        exteriorArea += pieceArea;
                    }
                    else if (cellAbove == cellBelow)
                    {
                        interiorArea += pieceArea;
                    }
                    else
                    {
                        unresolvedArea += pieceArea;
                    }
                }

                bool hasCover = coveredArea >= sliverArea;
                FaceCoverageVerdict verdict;
                if (interiorArea == 0 && exteriorArea == 0 && unresolvedArea == 0)
                {
                    verdict = FaceCoverageVerdict.Covered;
                }
                else if (interiorArea > 0)
                {
                    verdict = hasCover ? FaceCoverageVerdict.Partial : FaceCoverageVerdict.Missing;
                }
                else if (unresolvedArea > 0)
                {
                    verdict = FaceCoverageVerdict.Unresolved;
                }
                else
                {
                    verdict = hasCover ? FaceCoverageVerdict.Trimmed : FaceCoverageVerdict.Unused;
                }

                entries.Add(new FaceCoverageEntry(index, area, coveredArea, interiorArea, exteriorArea, unresolvedArea, verdict));
            }

            return new FaceCoverageReport(entries, shells.Count);
        }

        /// <summary>
        /// Coverage of a decoded <see cref="OcctCellComplexResult"/> using the build's own tolerances.
        /// </summary>
        public static FaceCoverageReport FaceCoverage(IEnumerable<Face3D> inputFaces, OcctCellComplexResult cellComplexResult, Core.OCCT.OcctBuildOptions options, double minArea = Tolerance.MacroDistance)
        {
            options = options ?? new Core.OCCT.OcctBuildOptions();
            IEnumerable<Shell> shells = cellComplexResult?.Cells?.Select(x => x?.Shell) ?? Enumerable.Empty<Shell>();
            return FaceCoverage(inputFaces, shells, minArea, options.Tolerance, options.FuzzyTolerance);
        }

        private static int ContainingCell(List<Shell> shells, List<BoundingBox3D> shellBoxes, Point3D point3D, double fuzzyTolerance, double tolerance)
        {
            for (int i = 0; i < shells.Count; i++)
            {
                if (shellBoxes[i] != null && !shellBoxes[i].Inside(point3D, true, tolerance))
                {
                    continue;
                }

                if (shells[i].Inside(point3D, fuzzyTolerance, tolerance))
                {
                    return i;
                }
            }

            return -1;
        }

        private sealed class CoverageCellFace
        {
            public CoverageCellFace(Face3D face3D, Plane plane, BoundingBox3D boundingBox3D)
            {
                Face3D = face3D;
                Plane = plane;
                BoundingBox3D = boundingBox3D;
            }

            public Face3D Face3D { get; }

            public Plane Plane { get; }

            public BoundingBox3D BoundingBox3D { get; }
        }
    }
}
