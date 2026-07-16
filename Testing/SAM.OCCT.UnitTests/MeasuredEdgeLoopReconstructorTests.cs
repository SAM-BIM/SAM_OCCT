// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Planar;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    public sealed class MeasuredEdgeLoopReconstructorTests
    {
        private const double Tolerance = 1e-6;

        [Fact]
        public void TryReconstruct_CanonicalBowTie_RejectsExplicitCrossing()
        {
            // Arrange
            List<Point2D> boundary = Points((0, 0), (4, 4), (0, 4), (4, 0));

            // Act
            bool intersects = MeasuredEdgeLoopReconstructor.HasNonAdjacentIntersections(boundary, Tolerance);
            bool rebuilt = MeasuredEdgeLoopReconstructor.TryReconstruct(boundary, ZeroMovements(boundary), Tolerance, out _);

            // Assert
            Assert.True(intersects);
            Assert.False(rebuilt);
        }

        [Fact]
        public void TryReconstruct_NearParallelAdjacentShiftedEdges_UsesBoundedBevel()
        {
            // Arrange
            List<Point2D> boundary = Points((0, 0), (10, 0), (20, 0.1), (20, 10), (0, 10));
            double[] movements = { 0.1, 0.2, 0, 0, 0 };

            // Act
            bool rebuilt = MeasuredEdgeLoopReconstructor.TryReconstruct(boundary, movements, Tolerance, out var result);

            // Assert
            Assert.True(rebuilt);
            Assert.True(result.BevelledJoinCount >= 1);
            AssertBounded(boundary, movements, result);
            Assert.False(MeasuredEdgeLoopReconstructor.HasNonAdjacentIntersections(result.Vertices, Tolerance));
        }

        [Fact]
        public void TryReconstruct_ObservedRemoteSpikeScale_BoundsFormerHundredMetreJoin()
        {
            // Arrange - unequal movements on consecutive lines whose direction changes by about 0.006 degrees.
            List<Point2D> boundary = Points((0, 0), (10, 0), (20, 0.001), (20, 10), (0, 10));
            double[] movements = { 0.1, 0.2, 0, 0, 0 };
            double legacyJoinDistance = UnboundedJoinDistance(boundary[0], boundary[1], movements[0], boundary[2], movements[1]);

            // Act
            bool rebuilt = MeasuredEdgeLoopReconstructor.TryReconstruct(boundary, movements, Tolerance, out var result);

            // Assert
            Assert.True(legacyJoinDistance > 100);
            Assert.True(rebuilt);
            Assert.True(result.BevelledJoinCount >= 1);
            AssertBounded(boundary, movements, result);
            Assert.True(result.MaximumDisplacementRatio <= MeasuredEdgeLoopReconstructor.DefaultMitreLimit + Tolerance);
        }

        [Fact]
        public void TryReconstruct_RectangularExpansion_PreservesExactSquareMitres()
        {
            // Arrange
            List<Point2D> boundary = Points((0, 0), (4, 0), (4, 3), (0, 3));
            double[] movements = { 0.25, 0.25, 0.25, 0.25 };

            // Act
            bool rebuilt = MeasuredEdgeLoopReconstructor.TryReconstruct(boundary, movements, Tolerance, out var result);

            // Assert
            Assert.True(rebuilt);
            Assert.Equal(0, result.BevelledJoinCount);
            Assert.Equal(4, result.Vertices.Count);
            Assert.Equal(System.Math.Sqrt(2), result.MaximumDisplacementRatio, 6);
            Assert.Contains(result.Vertices, point => Near(point, -0.25, -0.25));
            Assert.Contains(result.Vertices, point => Near(point, 4.25, 3.25));
            AssertBounded(boundary, movements, result);
        }

        [Fact]
        public void TryReconstruct_ConcaveLShape_PreservesWindingAndConcavity()
        {
            // Arrange
            List<Point2D> boundary = Points((0, 0), (4, 0), (4, 1), (1, 1), (1, 4), (0, 4));
            double[] movements = Enumerable.Repeat(0.1, boundary.Count).ToArray();
            double areaBefore = SignedArea(boundary);

            // Act
            bool rebuilt = MeasuredEdgeLoopReconstructor.TryReconstruct(boundary, movements, Tolerance, out var result);

            // Assert
            Assert.True(rebuilt);
            Assert.True(SignedArea(result.Vertices) > areaBefore);
            Assert.Equal(System.Math.Sign(areaBefore), System.Math.Sign(SignedArea(result.Vertices)));
            Assert.Contains(result.Vertices, point => Near(point, 1.1, 1.1));
            Assert.False(MeasuredEdgeLoopReconstructor.HasNonAdjacentIntersections(result.Vertices, Tolerance));
            AssertBounded(boundary, movements, result);
        }

        [Fact]
        public void GrowEdgesToWalls_CapWithInternalOpening_PreservesOpeningAfterSharedReconstruction()
        {
            // Arrange
            Face3D face = Face3D.Create(new List<IClosedPlanar3D>
            {
                Polygon3D((0, 0, 0), (4, 0, 0), (4, 4, 0), (0, 4, 0)),
                Polygon3D((1, 1, 0), (2, 1, 0), (2, 2, 0), (1, 2, 0))
            });
            SnappedPanel cap = new SnappedPanel(0, face, 1, 0.3, 0.5);
            SnappedPanel wall = new SnappedPanel(1, TestGeometry.CreatePlanarFace(
                new Point3D(4.3, 0, 0), new Point3D(4.3, 4, 0), new Point3D(4.3, 4, 3), new Point3D(4.3, 0, 3)), 1, 0.3, 0.5);

            // Act
            bool grew = cap.GrowEdgesToWalls(new[] { wall }, 0.5, 0.05, Tolerance);

            // Assert
            Assert.True(grew);
            Assert.Equal(1, cap.Face3D.GetInternalEdge3Ds()?.Count ?? 0);
            Assert.True(cap.Face3D.IsValid());
        }

        [Fact]
        public void TryReconstruct_ReversedWinding_ProducesEquivalentBoundedLoop()
        {
            // Arrange
            List<Point2D> forward = Points((0, 0), (4, 0), (4, 3), (0, 3));
            List<Point2D> reversed = forward.AsEnumerable().Reverse().ToList();
            double[] movements = { 0.2, 0.2, 0.2, 0.2 };

            // Act
            bool forwardOk = MeasuredEdgeLoopReconstructor.TryReconstruct(forward, movements, Tolerance, out var forwardResult);
            bool reversedOk = MeasuredEdgeLoopReconstructor.TryReconstruct(reversed, movements, Tolerance, out var reversedResult);

            // Assert
            Assert.True(forwardOk);
            Assert.True(reversedOk);
            Assert.Equal(System.Math.Abs(SignedArea(forwardResult.Vertices)), System.Math.Abs(SignedArea(reversedResult.Vertices)), 6);
            Assert.True(SignedArea(forwardResult.Vertices) * SignedArea(reversedResult.Vertices) < 0);
            AssertBounded(forward, movements, forwardResult);
            AssertBounded(reversed, movements, reversedResult);
        }

        [Fact]
        public void TryReconstruct_RotatedGeometry_IsRotationInvariant()
        {
            // Arrange
            List<Point2D> boundary = Points((0, 0), (10, 0), (20, 0.1), (20, 10), (0, 10));
            double[] movements = { 0.1, 0.2, 0, 0, 0 };
            List<Point2D> rotated = Rotate(boundary, 37 * System.Math.PI / 180);

            // Act
            bool originalOk = MeasuredEdgeLoopReconstructor.TryReconstruct(boundary, movements, Tolerance, out var original);
            bool rotatedOk = MeasuredEdgeLoopReconstructor.TryReconstruct(rotated, movements, Tolerance, out var transformed);

            // Assert
            Assert.True(originalOk);
            Assert.True(rotatedOk);
            Assert.Equal(original.BevelledJoinCount, transformed.BevelledJoinCount);
            Assert.Equal(original.MaximumDisplacementRatio, transformed.MaximumDisplacementRatio, 6);
            Assert.Equal(System.Math.Abs(SignedArea(original.Vertices)), System.Math.Abs(SignedArea(transformed.Vertices)), 6);
            AssertBounded(rotated, movements, transformed);
        }

        [Fact]
        public void TryReconstruct_ZeroGrowthNeighbour_KeepsStationaryEdgeLine()
        {
            // Arrange
            List<Point2D> boundary = Points((0, 0), (4, 0), (4, 3), (0, 3));
            double[] movements = { 0.2, 0, 0, 0 };

            // Act
            bool rebuilt = MeasuredEdgeLoopReconstructor.TryReconstruct(boundary, movements, Tolerance, out var result);

            // Assert
            Assert.True(rebuilt);
            Assert.Contains(result.Vertices, point => Near(point, 4, -0.2));
            Assert.Contains(result.Vertices, point => Near(point, 4, 3));
            AssertBounded(boundary, movements, result);
        }

        [Fact]
        public void TryReconstruct_RepeatedExecution_IsDeterministic()
        {
            // Arrange
            List<Point2D> boundary = Points((0, 0), (10, 0), (20, 0.001), (20, 10), (0, 10));
            double[] movements = { 0.1, 0.2, 0, 0, 0 };

            // Act
            bool firstOk = MeasuredEdgeLoopReconstructor.TryReconstruct(boundary, movements, Tolerance, out var first);
            bool secondOk = MeasuredEdgeLoopReconstructor.TryReconstruct(boundary, movements, Tolerance, out var second);

            // Assert
            Assert.True(firstOk);
            Assert.True(secondOk);
            Assert.Equal(first.BevelledJoinCount, second.BevelledJoinCount);
            Assert.Equal(first.SourceVertexIndices, second.SourceVertexIndices);
            Assert.Equal(first.Vertices.Count, second.Vertices.Count);
            for (int i = 0; i < first.Vertices.Count; i++)
            {
                Assert.Equal(first.Vertices[i].X, second.Vertices[i].X);
                Assert.Equal(first.Vertices[i].Y, second.Vertices[i].Y);
            }
        }

        private static void AssertBounded(
            IReadOnlyList<Point2D> original,
            IReadOnlyList<double> movements,
            MeasuredEdgeLoopReconstructor.Reconstruction result)
        {
            for (int i = 0; i < result.Vertices.Count; i++)
            {
                int source = result.SourceVertexIndices[i];
                int previous = (source - 1 + original.Count) % original.Count;
                double acceptedBound = MeasuredEdgeLoopReconstructor.DefaultMitreLimit
                    * System.Math.Max(movements[previous], movements[source]);
                Assert.True(Distance(original[source], result.Vertices[i]) <= acceptedBound + Tolerance,
                    $"Vertex {i} exceeded bound {acceptedBound} for source {source}.");
            }
        }

        private static double UnboundedJoinDistance(Point2D start, Point2D vertex, double firstMovement, Point2D end, double secondMovement)
        {
            double firstDx = vertex.X - start.X;
            double firstDy = vertex.Y - start.Y;
            double firstLength = System.Math.Sqrt(firstDx * firstDx + firstDy * firstDy);
            Point2D firstOffsetStart = new Point2D(start.X + firstDy / firstLength * firstMovement, start.Y - firstDx / firstLength * firstMovement);
            Point2D firstOffsetEnd = new Point2D(vertex.X + firstDy / firstLength * firstMovement, vertex.Y - firstDx / firstLength * firstMovement);

            double secondDx = end.X - vertex.X;
            double secondDy = end.Y - vertex.Y;
            double secondLength = System.Math.Sqrt(secondDx * secondDx + secondDy * secondDy);
            Point2D secondOffsetStart = new Point2D(vertex.X + secondDy / secondLength * secondMovement, vertex.Y - secondDx / secondLength * secondMovement);
            Point2D secondOffsetEnd = new Point2D(end.X + secondDy / secondLength * secondMovement, end.Y - secondDx / secondLength * secondMovement);
            Point2D intersection = SAM.Geometry.Planar.Query.Intersection(
                firstOffsetStart, firstOffsetEnd, secondOffsetStart, secondOffsetEnd, false, Tolerance);
            return Distance(vertex, intersection);
        }

        private static List<Point2D> Rotate(IEnumerable<Point2D> points, double angle)
        {
            double cosine = System.Math.Cos(angle);
            double sine = System.Math.Sin(angle);
            return points.Select(point => new Point2D(point.X * cosine - point.Y * sine, point.X * sine + point.Y * cosine)).ToList();
        }

        private static List<Point2D> Points(params (double x, double y)[] coordinates)
        {
            return coordinates.Select(coordinate => new Point2D(coordinate.x, coordinate.y)).ToList();
        }

        private static double[] ZeroMovements(IReadOnlyCollection<Point2D> points)
        {
            return new double[points.Count];
        }

        private static Polygon3D Polygon3D(params (double x, double y, double z)[] coordinates)
        {
            return new Polygon3D(coordinates.Select(coordinate => new Point3D(coordinate.x, coordinate.y, coordinate.z)).ToList());
        }

        private static bool Near(Point2D point, double x, double y)
        {
            return System.Math.Abs(point.X - x) <= Tolerance && System.Math.Abs(point.Y - y) <= Tolerance;
        }

        private static double SignedArea(IReadOnlyList<Point2D> vertices)
        {
            double twiceArea = 0;
            for (int i = 0; i < vertices.Count; i++)
            {
                Point2D next = vertices[(i + 1) % vertices.Count];
                twiceArea += vertices[i].X * next.Y - next.X * vertices[i].Y;
            }
            return twiceArea / 2;
        }

        private static double Distance(Point2D first, Point2D second)
        {
            double dx = second.X - first.X;
            double dy = second.Y - first.Y;
            return System.Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
