// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static SAM.OCCT.UnitTests.WallFixtures;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// The pieces of the rule (b) bend veto (<see cref="BendVeto"/>): when a move is refused, the exact contact of a run-end segment with a
    /// face (holes, notches and disconnected stretches respected, a corner touch empty), the run-end segments of a notched outline and the
    /// equal-weight midplane target.
    /// </summary>
    public class BendVetoContactTests
    {
        private const double BX = 0.145;
        private static readonly ToleranceBudget Tolerances = new ToleranceBudget();

        private static SnappedPanel Panel(int index, Piece piece)
        {
            return new SnappedPanel(index, TestGeometry.CreatePlanarFace(piece.Loop()), piece.Weight, 0.4, 0.4);
        }

        private static SnappedPanel Angled()
        {
            return Panel(0, Piece.Wall("angled", 0, 0, BX - 1e-4, 3.848, 0, 3, 1.0));
        }

        private static bool Refuses(SnappedPanel candidate, SnappedPanel backer, SnappedPanel third, out string reason)
        {
            return BendVeto.Refuses(candidate, backer, backer.Plane, new[] { backer, candidate, third }, Tolerances.Angle, Tolerances.ArcAngle, Tolerances.Distance, null, out reason);
        }

        [Fact]
        public void Refuses_RotationWithinTheArcTolerance_IsNotRefused()
        {
            // Arrange: a run turned 0.2 degrees (below the 0.3 degree arc tolerance) whose far end meets a continuation
            double dx = 2 * System.Math.Tan(0.2 * System.Math.PI / 180);
            SnappedPanel backer = Panel(0, Piece.Wall("b", 0, 0, 0, 4, 0, 3, 1));
            SnappedPanel candidate = Panel(1, Piece.Wall("c", 0, 4, dx, 6, 0, 3, 0.5));
            SnappedPanel third = Panel(2, Piece.Wall("t", dx, 6, dx, 8, 0, 3, 0.3));

            // Act
            bool refused = Refuses(candidate, backer, third, out string reason);

            // Assert
            Assert.False(refused, reason);
        }

        [Fact]
        public void Refuses_PerpendicularReturnAtTheRunEnd_IsNotCounted()
        {
            // Arrange: the run end meets only a perpendicular return, which the rotation slides along
            SnappedPanel backer = Angled();
            SnappedPanel candidate = Panel(1, Piece.Wall("run", BX, 3.85, BX, 5.7, 0, 3, 0.5));
            SnappedPanel ret = Panel(2, Piece.Wall("return", BX - 1, 5.7, BX + 1, 5.7, 0, 3, 0.4));

            // Act
            bool refused = Refuses(candidate, backer, ret, out string reason);

            // Assert
            Assert.False(refused, reason);
        }

        [Fact]
        public void Refuses_ContinuationAtTheRunEnd_NamesTheBrokenConnection()
        {
            // Arrange
            SnappedPanel backer = Angled();
            SnappedPanel candidate = Panel(1, Piece.Wall("run", BX, 3.85, BX, 5.7, 0, 3, 0.5));
            SnappedPanel next = Panel(2, Piece.Wall("next", BX, 5.7, BX, 8, 0, 3, 0.3));

            // Act
            bool refused = Refuses(candidate, backer, next, out string reason);

            // Assert
            Assert.True(refused);
            Assert.Contains("meets panel 2 over 3 m", reason);
        }

        [Fact]
        public void Refuses_LeaningWallCrossingTheRunEndAtOneHeight_IsNotRefused()
        {
            // Arrange: the next wall leans 1 degree (within the 5 degree angle tolerance) about the horizontal line through the run end's
            // mid-height, so it meets the run end at z = 1.5 only. Within 1 um of its plane lies 2 um / sin(1 deg) = 0.11 mm of the end
            // edge: a crossing, not a stretch the edge lies on.
            SnappedPanel backer = Angled();
            SnappedPanel candidate = Panel(1, Piece.Wall("run", BX, 3.85, BX, 5.7, 0, 3, 0.5));
            SnappedPanel leaning = Panel(2, Piece.Leaning("leaning", BX, 5.7, 8, 0, 3, 1, 0.3));

            // Act
            bool refused = Refuses(candidate, backer, leaning, out string reason);

            // Assert
            Assert.False(refused, reason);
        }

        [Fact]
        public void Contact_SegmentCrossingALeaningFaceAtOnePoint_IsEmpty()
        {
            // Arrange: the end edge x = BX, y = 5.7, z 0..3 and a face leaning 1 degree that it crosses at z = 1.5
            Face3D leaning = TestGeometry.CreatePlanarFace(Piece.Leaning("leaning", BX, 5.7, 8, 0, 3, 1, 0.3).Loop());

            // Act
            List<(double From, double To)> contact = BendVeto.Contact(new Segment3D(new Point3D(BX, 5.7, 0), new Point3D(BX, 5.7, 3)), leaning, Tol);

            // Assert
            Assert.Empty(contact);
        }

        [Fact]
        public void Contact_SegmentWithinTheDistanceToleranceFromEndToEnd_IsTheWholeSegment()
        {
            // Arrange: a 3 m edge running from 0.5 um behind the face's plane to 0.5 um in front of it: it lies on the plane throughout
            Face3D face = TestGeometry.CreatePlanarFace(Piece.Wall("face", 0, 0, 0, 2, 0, 3, 1).Loop());

            // Act
            List<(double From, double To)> contact = BendVeto.Contact(new Segment3D(new Point3D(-5e-7, 0, 0), new Point3D(5e-7, 0, 3)), face, Tol);

            // Assert
            Assert.Single(contact);
            Assert.Equal(3, contact[0].To - contact[0].From, 6);
        }

        [Fact]
        public void Refuses_ContinuationTurnedInPlanAboutTheRunEnd_IsStillRefused()
        {
            // Arrange: the next wall turned 1 degree in plan about the run end's vertical edge (a real plan bend): the edge lies on it
            double a = System.Math.PI / 180;
            SnappedPanel backer = Angled();
            SnappedPanel candidate = Panel(1, Piece.Wall("run", BX, 3.85, BX, 5.7, 0, 3, 0.5));
            SnappedPanel turned = Panel(2, Piece.Wall("turned", BX, 5.7, BX + (2.3 * System.Math.Sin(a)), 5.7 + (2.3 * System.Math.Cos(a)), 0, 3, 0.3));

            // Act
            bool refused = Refuses(candidate, backer, turned, out string reason);

            // Assert
            Assert.True(refused);
            Assert.Contains("meets panel 2 over 3 m", reason);
        }

        private static Face3D WallWithOpening()
        {
            // the plane x = 0, 4 m long and 3 m high, with a 1 x 1 m opening at y 1..2, z 1..2
            Polygon3D outer = new Polygon3D(new List<Point3D> { new Point3D(0, 0, 0), new Point3D(0, 4, 0), new Point3D(0, 4, 3), new Point3D(0, 0, 3) });
            Polygon3D hole = new Polygon3D(new List<Point3D> { new Point3D(0, 1, 1), new Point3D(0, 2, 1), new Point3D(0, 2, 2), new Point3D(0, 1, 2) });
            return Face3D.Create(new List<IClosedPlanar3D> { outer, hole });
        }

        [Fact]
        public void Contact_SegmentAcrossAnOpening_GivesTheStretchesEitherSide()
        {
            // Act
            List<(double From, double To)> contact = BendVeto.Contact(new Segment3D(new Point3D(0, 1.5, 0), new Point3D(0, 1.5, 3)), WallWithOpening(), Tol);

            // Assert
            Assert.Equal(2, contact.Count);
            Assert.Equal(0, contact[0].From, 6);
            Assert.Equal(1, contact[0].To, 6);
            Assert.Equal(2, contact[1].From, 6);
            Assert.Equal(3, contact[1].To, 6);
        }

        [Fact]
        public void Contact_SegmentAlongTheSideOfAnOpening_IsTheWholeSegment()
        {
            // Act: the opening's edge is on the face, so the segment lies on the face throughout
            List<(double From, double To)> contact = BendVeto.Contact(new Segment3D(new Point3D(0, 1, 0), new Point3D(0, 1, 3)), WallWithOpening(), Tol);

            // Assert
            Assert.Single(contact);
            Assert.Equal(3, contact[0].To - contact[0].From, 6);
        }

        [Fact]
        public void Contact_SegmentThroughASideNotch_GivesTheDisconnectedStretches()
        {
            // Arrange: a C-shaped face (x = 0) whose notch from the y = 0 side spans z 1..2 and reaches y = 1
            Face3D face = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(0, 4, 0), new Point3D(0, 4, 3), new Point3D(0, 0, 3),
                new Point3D(0, 0, 2), new Point3D(0, 1, 2), new Point3D(0, 1, 1), new Point3D(0, 0, 1));

            // Act: the end edge at y = 0.5, from the top down
            List<(double From, double To)> contact = BendVeto.Contact(new Segment3D(new Point3D(0, 0.5, 3), new Point3D(0, 0.5, 0)), face, Tol);

            // Assert
            Assert.Equal(2, contact.Count);
            Assert.Equal(1, contact[0].To - contact[0].From, 6);
            Assert.Equal(1, contact[1].To - contact[1].From, 6);
        }

        [Fact]
        public void Contact_ShortStretchBetweenSampleHeights_IsMeasuredExactly()
        {
            // Arrange: an 80 mm strip at 1.21..1.29 m; a 21-point sampling of the 3 m edge (every 0.15 m) finds no point on it
            Face3D strip = TestGeometry.CreatePlanarFace(Piece.Wall("strip", 0, 0, 0, 2, 1.21, 1.29, 1).Loop());

            // Act
            List<(double From, double To)> contact = BendVeto.Contact(new Segment3D(new Point3D(0, 0, 0), new Point3D(0, 0, 3)), strip, Tol);

            // Assert
            Assert.Single(contact);
            Assert.Equal(1.21, contact[0].From, 6);
            Assert.Equal(1.29, contact[0].To, 6);
        }

        [Theory]
        [InlineData(3.0)] // the face starts where the segment ends: a corner touch
        [InlineData(2.9999995)] // overlaps by less than the distance tolerance
        public void Contact_TouchAtACorner_IsEmpty(double faceBottom)
        {
            // Arrange
            Face3D above = TestGeometry.CreatePlanarFace(Piece.Wall("above", 0, 0, 0, 2, faceBottom, 6, 1).Loop());

            // Act
            List<(double From, double To)> contact = BendVeto.Contact(new Segment3D(new Point3D(0, 0, 0), new Point3D(0, 0, 3)), above, Tol);

            // Assert
            Assert.Empty(contact);
        }

        [Fact]
        public void Contact_SegmentOffThePlane_IsEmpty()
        {
            // Arrange
            Face3D face = TestGeometry.CreatePlanarFace(Piece.Wall("face", 0, 0, 0, 2, 0, 3, 1).Loop());

            // Act: 1 mm in front of the face
            List<(double From, double To)> contact = BendVeto.Contact(new Segment3D(new Point3D(0.001, 0, 0), new Point3D(0.001, 0, 3)), face, Tol);

            // Assert
            Assert.Empty(contact);
        }

        [Fact]
        public void RunEndSegments_NotchedEnd_GivesEachPieceOfTheEnd()
        {
            // Arrange: a wall on x = 0 whose y = 2 end has a 0.2 m deep notch from z 1..2, splitting that end into two 1 m pieces
            SnappedPanel wall = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(0, 2, 0), new Point3D(0, 2, 1), new Point3D(0, 1.8, 1), new Point3D(0, 1.8, 2), new Point3D(0, 2, 2), new Point3D(0, 2, 3), new Point3D(0, 0, 3)), 1, 0.4, 0.4);

            // Act
            List<Segment3D> ends = wall.RunEndSegments(null, Tol);

            // Assert: the y = 0 end (3 m) and the two 1 m pieces of the y = 2 end; the notch's own side is not at the extreme
            Assert.Equal(3, ends.Count);
            Assert.Equal(5, ends.Sum(s => s.GetLength()), 6);
            Assert.DoesNotContain(ends, s => System.Math.Abs(s[0].Y - 1.8) <= Tol);
        }

        [Fact]
        public void MidplaneWith_ReturnsTheMoveTargetOrTheOwnPlaneWithinTolerance()
        {
            // Arrange
            SnappedPanel a = Panel(0, Piece.Wall("a", 0, 0, 0, 4, 0, 3, 1));
            SnappedPanel b = Panel(1, Piece.Wall("b", 0.1, 4, 0.1, 6, 0, 3, 1));
            SnappedPanel c = Panel(2, Piece.Wall("c", 1e-7, 4, 1e-7, 6, 0, 3, 1));

            // Act
            Plane mid = a.MidplaneWith(b, Tol);
            Plane same = a.MidplaneWith(c, Tol);

            // Assert
            Assert.Equal(0.05, mid.Distance(new Point3D(0, 0, 0)), 9);
            Assert.Equal(0, mid.Distance(new Point3D(0.05, 3, 1)), 9);
            Assert.Same(a.Plane, same);
        }
    }
}
