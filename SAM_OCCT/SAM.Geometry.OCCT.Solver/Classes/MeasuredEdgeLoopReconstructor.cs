// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Planar;
using System;
using System.Collections.Generic;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Reconstructs a straight-edged planar loop after each edge has moved independently along its outward
    /// normal. Ordinary corners retain their exact mitre. A remote mitre is replaced by a bevel joining the
    /// endpoints of the two measured offset edges, so no measured edge movement is reduced or redirected.
    /// </summary>
    internal static class MeasuredEdgeLoopReconstructor
    {
        /// <summary>
        /// Successful PR #61 fixture joins peak at 1.413272 times the larger adjacent movement (the expected
        /// square-corner value is sqrt(2)). Two provides measured headroom while excluding the observed
        /// near-parallel amplification ratios of 36-362.
        /// </summary>
        internal const double DefaultMitreLimit = 2.0;

        internal sealed class Reconstruction
        {
            internal Reconstruction(List<Point2D> vertices, List<int> sourceVertexIndices, int bevelledJoinCount, double maximumDisplacementRatio)
            {
                Vertices = vertices;
                SourceVertexIndices = sourceVertexIndices;
                BevelledJoinCount = bevelledJoinCount;
                MaximumDisplacementRatio = maximumDisplacementRatio;
            }

            internal List<Point2D> Vertices { get; }
            internal List<int> SourceVertexIndices { get; }
            internal int BevelledJoinCount { get; }
            internal double MaximumDisplacementRatio { get; }
        }

        internal static bool TryReconstruct(
            IReadOnlyList<Point2D> boundary,
            IReadOnlyList<double> movements,
            double tolerance,
            out Reconstruction reconstruction,
            double mitreLimit = DefaultMitreLimit)
        {
            reconstruction = null;
            int count = boundary?.Count ?? 0;
            if (count < 3 || movements == null || movements.Count != count || tolerance <= 0 || mitreLimit < 1)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                if (!IsFinite(boundary[i]) || !IsFinite(movements[i]) || movements[i] < 0)
                {
                    return false;
                }
            }

            // Validate the supplied loop explicitly. In particular, do not rely on the split-segment count
            // returned by Query.SelfIntersectionSegment2Ds, which misses a canonical four-edge bow-tie.
            if (HasNonAdjacentIntersections(boundary, tolerance))
            {
                return false;
            }

            double signedArea = SignedArea(boundary);
            if (!IsFinite(signedArea) || System.Math.Abs(signedArea) <= tolerance * tolerance)
            {
                return false;
            }

            bool counterClockwise = signedArea > 0;
            Point2D[] offsetStarts = new Point2D[count];
            Point2D[] offsetEnds = new Point2D[count];
            for (int i = 0; i < count; i++)
            {
                Point2D start = boundary[i];
                Point2D end = boundary[(i + 1) % count];
                double dx = end.X - start.X;
                double dy = end.Y - start.Y;
                double length = System.Math.Sqrt(dx * dx + dy * dy);
                if (!IsFinite(length) || length <= tolerance)
                {
                    return false;
                }

                // A CCW external loop has its exterior on the right of each directed edge; CW is opposite.
                double sense = counterClockwise ? 1.0 : -1.0;
                double outwardX = sense * dy / length;
                double outwardY = sense * -dx / length;
                double movement = movements[i];
                offsetStarts[i] = new Point2D(start.X + outwardX * movement, start.Y + outwardY * movement);
                offsetEnds[i] = new Point2D(end.X + outwardX * movement, end.Y + outwardY * movement);
            }

            List<Point2D> vertices = new List<Point2D>(count);
            List<int> sourceIndices = new List<int>(count);
            int bevelledJoinCount = 0;
            double maximumRatio = 0;

            for (int vertexIndex = 0; vertexIndex < count; vertexIndex++)
            {
                int previousEdge = (vertexIndex - 1 + count) % count;
                double adjacentMovement = System.Math.Max(movements[previousEdge], movements[vertexIndex]);
                double bound = mitreLimit * adjacentMovement;

                if (TryBoundedMitre(
                    offsetStarts[previousEdge], offsetEnds[previousEdge],
                    offsetStarts[vertexIndex], offsetEnds[vertexIndex],
                    boundary[vertexIndex], bound, tolerance, out Point2D mitre))
                {
                    AddDistinct(vertices, sourceIndices, mitre, vertexIndex, tolerance);
                    maximumRatio = System.Math.Max(maximumRatio, DisplacementRatio(boundary[vertexIndex], mitre, adjacentMovement, tolerance));
                    continue;
                }

                // Deterministic bevel: retain each adjacent offset edge's endpoint. Both points are bounded by
                // their measured movement, including the important case where one neighbouring edge has zero
                // growth and must remain exactly on its original line.
                Point2D previousEnd = offsetEnds[previousEdge];
                Point2D currentStart = offsetStarts[vertexIndex];
                if (Distance(previousEnd, currentStart) > tolerance)
                {
                    bevelledJoinCount++;
                }
                AddDistinct(vertices, sourceIndices, previousEnd, vertexIndex, tolerance);
                AddDistinct(vertices, sourceIndices, currentStart, vertexIndex, tolerance);
                maximumRatio = System.Math.Max(maximumRatio, DisplacementRatio(boundary[vertexIndex], previousEnd, adjacentMovement, tolerance));
                maximumRatio = System.Math.Max(maximumRatio, DisplacementRatio(boundary[vertexIndex], currentStart, adjacentMovement, tolerance));
            }

            RemoveClosingDuplicate(vertices, sourceIndices, tolerance);
            if (vertices.Count < 3 || !AllVerticesBounded(boundary, movements, vertices, sourceIndices, mitreLimit, tolerance))
            {
                return false;
            }

            // Recheck the actual reconstructed segment set, including bevel segments.
            if (HasNonAdjacentIntersections(vertices, tolerance))
            {
                return false;
            }

            double reconstructedArea = SignedArea(vertices);
            if (!IsFinite(reconstructedArea)
                || System.Math.Abs(reconstructedArea) <= tolerance * tolerance
                || System.Math.Sign(reconstructedArea) != System.Math.Sign(signedArea))
            {
                return false;
            }

            reconstruction = new Reconstruction(vertices, sourceIndices, bevelledJoinCount, maximumRatio);
            return true;
        }

        internal static bool HasNonAdjacentIntersections(IReadOnlyList<Point2D> vertices, double tolerance)
        {
            int count = vertices?.Count ?? 0;
            if (count < 3)
            {
                return true;
            }

            for (int first = 0; first < count; first++)
            {
                int firstEnd = (first + 1) % count;
                for (int second = first + 1; second < count; second++)
                {
                    int secondEnd = (second + 1) % count;
                    if (firstEnd == second || secondEnd == first)
                    {
                        continue;
                    }

                    if (SegmentsIntersect(vertices[first], vertices[firstEnd], vertices[second], vertices[secondEnd], tolerance))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryBoundedMitre(
            Point2D firstStart,
            Point2D firstEnd,
            Point2D secondStart,
            Point2D secondEnd,
            Point2D originalVertex,
            double bound,
            double tolerance,
            out Point2D mitre)
        {
            mitre = null;
            double rx = firstEnd.X - firstStart.X;
            double ry = firstEnd.Y - firstStart.Y;
            double sx = secondEnd.X - secondStart.X;
            double sy = secondEnd.Y - secondStart.Y;
            double firstLength = System.Math.Sqrt(rx * rx + ry * ry);
            double secondLength = System.Math.Sqrt(sx * sx + sy * sy);
            double determinant = Cross(rx, ry, sx, sy);

            // Detect scale-relative parallelism before asking the general intersection helper to create a
            // potentially remote point. The projected parameter below is checked against the measured bound
            // before a Point2D is accepted.
            double parallelThreshold = tolerance * System.Math.Max(firstLength, secondLength);
            if (firstLength <= tolerance || secondLength <= tolerance || System.Math.Abs(determinant) <= parallelThreshold)
            {
                return false;
            }

            double qpx = secondStart.X - firstStart.X;
            double qpy = secondStart.Y - firstStart.Y;
            double parameter = Cross(qpx, qpy, sx, sy) / determinant;
            double x = firstStart.X + parameter * rx;
            double y = firstStart.Y + parameter * ry;
            if (!IsFinite(x) || !IsFinite(y))
            {
                return false;
            }

            double dx = x - originalVertex.X;
            double dy = y - originalVertex.Y;
            double displacement = System.Math.Sqrt(dx * dx + dy * dy);
            if (displacement > bound + tolerance)
            {
                return false;
            }

            mitre = new Point2D(x, y);
            return true;
        }

        private static bool AllVerticesBounded(
            IReadOnlyList<Point2D> original,
            IReadOnlyList<double> movements,
            IReadOnlyList<Point2D> reconstructed,
            IReadOnlyList<int> sourceIndices,
            double mitreLimit,
            double tolerance)
        {
            for (int i = 0; i < reconstructed.Count; i++)
            {
                int source = sourceIndices[i];
                int previous = (source - 1 + original.Count) % original.Count;
                double bound = mitreLimit * System.Math.Max(movements[previous], movements[source]);
                if (Distance(original[source], reconstructed[i]) > bound + tolerance)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SegmentsIntersect(Point2D a, Point2D b, Point2D c, Point2D d, double tolerance)
        {
            double abx = b.X - a.X;
            double aby = b.Y - a.Y;
            double cdx = d.X - c.X;
            double cdy = d.Y - c.Y;
            double scale = System.Math.Max(1.0, System.Math.Max(System.Math.Sqrt(abx * abx + aby * aby), System.Math.Sqrt(cdx * cdx + cdy * cdy)));
            double crossTolerance = tolerance * scale;

            double o1 = Cross(abx, aby, c.X - a.X, c.Y - a.Y);
            double o2 = Cross(abx, aby, d.X - a.X, d.Y - a.Y);
            double o3 = Cross(cdx, cdy, a.X - c.X, a.Y - c.Y);
            double o4 = Cross(cdx, cdy, b.X - c.X, b.Y - c.Y);

            if (((o1 > crossTolerance && o2 < -crossTolerance) || (o1 < -crossTolerance && o2 > crossTolerance))
                && ((o3 > crossTolerance && o4 < -crossTolerance) || (o3 < -crossTolerance && o4 > crossTolerance)))
            {
                return true;
            }

            return (System.Math.Abs(o1) <= crossTolerance && InBoundingBox(c, a, b, tolerance))
                || (System.Math.Abs(o2) <= crossTolerance && InBoundingBox(d, a, b, tolerance))
                || (System.Math.Abs(o3) <= crossTolerance && InBoundingBox(a, c, d, tolerance))
                || (System.Math.Abs(o4) <= crossTolerance && InBoundingBox(b, c, d, tolerance));
        }

        private static bool InBoundingBox(Point2D point, Point2D start, Point2D end, double tolerance)
        {
            return point.X >= System.Math.Min(start.X, end.X) - tolerance
                && point.X <= System.Math.Max(start.X, end.X) + tolerance
                && point.Y >= System.Math.Min(start.Y, end.Y) - tolerance
                && point.Y <= System.Math.Max(start.Y, end.Y) + tolerance;
        }

        private static void AddDistinct(List<Point2D> vertices, List<int> sourceIndices, Point2D point, int sourceIndex, double tolerance)
        {
            if (vertices.Count > 0 && Distance(vertices[vertices.Count - 1], point) <= tolerance)
            {
                return;
            }

            vertices.Add(point);
            sourceIndices.Add(sourceIndex);
        }

        private static void RemoveClosingDuplicate(List<Point2D> vertices, List<int> sourceIndices, double tolerance)
        {
            if (vertices.Count > 1 && Distance(vertices[0], vertices[vertices.Count - 1]) <= tolerance)
            {
                vertices.RemoveAt(vertices.Count - 1);
                sourceIndices.RemoveAt(sourceIndices.Count - 1);
            }
        }

        private static double DisplacementRatio(Point2D original, Point2D reconstructed, double adjacentMovement, double tolerance)
        {
            double displacement = Distance(original, reconstructed);
            if (adjacentMovement <= tolerance)
            {
                return displacement <= tolerance ? 0 : double.PositiveInfinity;
            }

            return displacement / adjacentMovement;
        }

        private static double SignedArea(IReadOnlyList<Point2D> vertices)
        {
            double twiceArea = 0;
            for (int i = 0; i < vertices.Count; i++)
            {
                Point2D current = vertices[i];
                Point2D next = vertices[(i + 1) % vertices.Count];
                twiceArea += current.X * next.Y - next.X * current.Y;
            }

            return twiceArea / 2.0;
        }

        private static double Distance(Point2D first, Point2D second)
        {
            double dx = second.X - first.X;
            double dy = second.Y - first.Y;
            return System.Math.Sqrt(dx * dx + dy * dy);
        }

        private static double Cross(double ax, double ay, double bx, double by)
        {
            return ax * by - ay * bx;
        }

        private static bool IsFinite(Point2D point)
        {
            return point != null && IsFinite(point.X) && IsFinite(point.Y);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
