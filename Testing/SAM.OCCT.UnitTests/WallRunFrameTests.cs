// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Planar;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// <see cref="SnappedPanel.AbutsColinearWithin"/> reads a wall's run and height. They must not depend on how the
    /// face was written down: a face's plane takes its axes from the vertex order, so the plane's AxisX is the run on
    /// some walls and the height on others (Softhouse: 114 of 236 walls on the reversed-face run, 103 of 237 on the
    /// other). Every case here runs the same geometry through the full presentation matrix - the eight plane-axis
    /// assignments (normal direction, run/height swap, axis signs) x three plane origins x both loop windings x every
    /// loop start, 192 presentations per wall - and asks for one answer. Pure managed.
    /// </summary>
    public class WallRunFrameTests
    {
        private const double MaxOffset = 0.3;
        private const double Tolerance = 1e-6;

        // ---- fixtures: vertical walls given by their plan end points and elevations --------------------------------

        private static Point3D[] Rect(double x0, double y0, double x1, double y1, double z0, double z1)
        {
            return new[] { new Point3D(x0, y0, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x0, y0, z1) };
        }

        /// <summary>The same wall leaning by <paramref name="degrees"/> from the vertical: every point is shifted along the
        /// wall's horizontal normal by <paramref name="jog"/> plus its elevation times the slope, so two leaning walls built
        /// this way share one plane when their jog is the same.</summary>
        private static Point3D[] LeaningRect(double x0, double y0, double x1, double y1, double z0, double z1, double jog, double degrees)
        {
            double length = System.Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
            double nx = (y1 - y0) / length;
            double ny = -(x1 - x0) / length;
            double slope = System.Math.Tan(degrees * System.Math.PI / 180.0);
            Point3D At(double x, double y, double z)
            {
                double shift = jog + z * slope;
                return new Point3D(x + nx * shift, y + ny * shift, z);
            }

            return new[] { At(x0, y0, z0), At(x1, y1, z0), At(x1, y1, z1), At(x0, y0, z1) };
        }

        private static Point3D[] RotatedAboutZ(Point3D[] loop, double degrees)
        {
            double c = System.Math.Cos(degrees * System.Math.PI / 180.0);
            double s = System.Math.Sin(degrees * System.Math.PI / 180.0);
            return loop.Select(p => new Point3D(p.X * c - p.Y * s, p.X * s + p.Y * c, p.Z)).ToArray();
        }

        private static Vector3D Between(Point3D to, Point3D from)
        {
            return new Vector3D(to.X - from.X, to.Y - from.Y, to.Z - from.Z);
        }

        /// <summary>One panel for every way the same wall can be presented: the eight plane-axis assignments (run or
        /// height first, each axis either way - the normal follows), three plane origins (the first vertex, the centroid and
        /// a point on the plane outside the face), both windings and the loop started at every vertex. Every combination is
        /// enumerated; none is tied to another.</summary>
        private static List<SnappedPanel> Presentations(Point3D[] loop, int sourceIndex = 0)
        {
            Point3D first = loop[0];
            Vector3D edge = Between(loop[1], first);
            Vector3D run = new Vector3D(edge.X, edge.Y, 0).Unit;
            Vector3D normal = edge.CrossProduct(Between(loop[3], first)).Unit;
            Vector3D up = normal.CrossProduct(run).Unit;
            if (up.Z < 0)
            {
                up = up.GetNegated();
            }

            List<(Vector3D X, Vector3D Y)> axes = new List<(Vector3D, Vector3D)>
            {
                (run, up), (run.GetNegated(), up), (up, run), (up.GetNegated(), run),
                (run, up.GetNegated()), (run.GetNegated(), up.GetNegated()), (up, run.GetNegated()), (up.GetNegated(), run.GetNegated()),
            };

            Point3D centroid = new Point3D(loop.Average(p => p.X), loop.Average(p => p.Y), loop.Average(p => p.Z));
            Point3D outside = new Point3D(first.X - run.X * 3.7 + up.X * 2.3, first.Y - run.Y * 3.7 + up.Y * 2.3, first.Z - run.Z * 3.7 + up.Z * 2.3);
            Point3D[] origins = { first, centroid, outside };

            List<SnappedPanel> result = new List<SnappedPanel>();
            foreach ((Vector3D x, Vector3D y) in axes)
            {
                foreach (Point3D origin in origins)
                {
                    Plane plane = new Plane(origin, x, y);
                    for (int reversed = 0; reversed < 2; reversed++)
                    {
                        List<Point3D> ordered = loop.ToList();
                        if (reversed == 1)
                        {
                            ordered.Reverse();
                        }

                        for (int start = 0; start < ordered.Count; start++)
                        {
                            List<Point3D> points = ordered.Skip(start).Concat(ordered.Take(start)).ToList();
                            Face3D face = new Face3D(plane, new Polygon2D(points.Select(p => plane.Convert(p))));
                            result.Add(new SnappedPanel(sourceIndex, face, 1, 0.1, 0.5));
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>The answer for every presentation of both walls; all must agree.</summary>
        private static HashSet<bool> AbutsAnswers(Point3D[] backer, Point3D[] candidate, double maxOffset = MaxOffset, double tolerance = Tolerance)
        {
            HashSet<bool> answers = new HashSet<bool>();
            List<SnappedPanel> candidates = Presentations(candidate, 1);
            foreach (SnappedPanel a in Presentations(backer))
            {
                foreach (SnappedPanel b in candidates)
                {
                    answers.Add(a.AbutsColinearWithin(b, maxOffset, tolerance));
                }
            }

            return answers;
        }

        [Fact]
        public void Presentations_OneWall_AreTheFullMatrixOfGenuinelyDifferentFrames()
        {
            // Arrange / Act: guards the fixtures - if all presentations shared the run on AxisX nothing below would test anything.
            List<SnappedPanel> panels = Presentations(Rect(0, 0, 5, 0, 0, 3));
            int runOnAxisX = panels.Count(p => System.Math.Abs(p.Plane.AxisX.DotProduct(new Vector3D(1, 0, 0))) > 0.99);
            int heightOnAxisX = panels.Count(p => System.Math.Abs(p.Plane.AxisX.DotProduct(new Vector3D(0, 0, 1))) > 0.99);
            int distinctOrigins = panels.Select(p => $"{p.Plane.Origin.X:F6},{p.Plane.Origin.Y:F6},{p.Plane.Origin.Z:F6}").Distinct().Count();
            int distinctFrames = panels.Select(p => $"{p.Plane.AxisX.X:F6},{p.Plane.AxisX.Y:F6},{p.Plane.AxisX.Z:F6}|{p.Plane.AxisY.X:F6},{p.Plane.AxisY.Y:F6},{p.Plane.AxisY.Z:F6}").Distinct().Count();
            int distinctLoops = panels.Select(p => string.Join(";", SnappedPanel.BoundaryPoints(p.Face3D).Select(q => $"{q.X:F3},{q.Y:F3},{q.Z:F3}"))).Distinct().Count();

            // Assert: 8 axis assignments x 3 origins x 2 windings x 4 loop starts, each combination present.
            Assert.Equal(192, panels.Count);
            Assert.Equal(96, runOnAxisX);
            Assert.Equal(96, heightOnAxisX);
            Assert.Contains(panels, p => p.Plane.Normal.Y > 0.99);
            Assert.Contains(panels, p => p.Plane.Normal.Y < -0.99);
            Assert.Equal(3, distinctOrigins);
            Assert.Equal(8, distinctFrames);
            Assert.Equal(8, distinctLoops); // 2 windings x 4 starts, whatever the frame
        }

        // ---- one answer whatever the presentation ------------------------------------------------------------------

        [Fact]
        public void AbutsColinearWithin_EndToEndOnTheSamePlane_TrueInEveryPresentation()
        {
            Assert.Equal(new[] { true }, AbutsAnswers(Rect(0, 0, 5, 0, 0, 3), Rect(5, 0, 10, 0, 0, 3)));
        }

        [Fact]
        public void AbutsColinearWithin_TowersStyleJog023_TrueInEveryPresentation()
        {
            // The towers step: the next bay is 0.23 m off the plane. Inside the 0.3 m offset, it aligns.
            Assert.Equal(new[] { true }, AbutsAnswers(Rect(0, 0, 5, 0, 0, 3), Rect(5, 0.23, 10, 0.23, 0, 3)));
        }

        [Theory]
        [InlineData(0.29, true)]
        [InlineData(0.31, false)]
        public void AbutsColinearWithin_PerpendicularOffsetAroundTheLimit_SameVerdictInEveryPresentation(double jog, bool expected)
        {
            Assert.Equal(new[] { expected }, AbutsAnswers(Rect(0, 0, 5, 0, 0, 3), Rect(5, jog, 10, jog, 0, 3)));
        }

        [Fact]
        public void AbutsColinearWithin_StackedStoreysWithAHeightGap_FalseInEveryPresentation()
        {
            // Same run, heights 0.2 m apart: not the same band of wall. With the height read as the run
            // (AxisX = Z) the run overlap passes the band test and the 0.2 m height gap passes the run test.
            Assert.Equal(new[] { false }, AbutsAnswers(Rect(0, 0, 5, 0, 0, 3), Rect(0, 0, 5, 0, 3.2, 6)));
        }

        [Fact]
        public void AbutsColinearWithin_RunGapWiderThanTheOffset_FalseInEveryPresentation()
        {
            Assert.Equal(new[] { false }, AbutsAnswers(Rect(0, 0, 5, 0, 0, 3), Rect(5.5, 0, 10, 0, 0, 3)));
        }

        [Fact]
        public void AbutsColinearWithin_OverlappingRunAndHeight_TrueInEveryPresentation()
        {
            Assert.Equal(new[] { true }, AbutsAnswers(Rect(0, 0, 5, 0, 0, 3), Rect(3, 0.1, 8, 0.1, 1, 4)));
        }

        [Fact]
        public void AbutsColinearWithin_OppositeDirectionOfTravel_SameAsForward()
        {
            // The candidate is written against the backer's direction (its run points -X).
            Assert.Equal(new[] { true }, AbutsAnswers(Rect(0, 0, 5, 0, 0, 3), Rect(10, 0.1, 5, 0.1, 0, 3)));
        }

        // ---- walls at any bearing ----------------------------------------------------------------------------------

        [Theory]
        [InlineData(30)]
        [InlineData(45)]
        [InlineData(90)]
        [InlineData(137)]
        [InlineData(-61)]
        public void AbutsColinearWithin_WallsAtAnyBearing_SameVerdictsInEveryPresentation(double degrees)
        {
            Point3D[] backer = RotatedAboutZ(Rect(0, 0, 5, 0, 0, 3), degrees);

            Assert.Equal(new[] { true }, AbutsAnswers(backer, RotatedAboutZ(Rect(5, 0.23, 10, 0.23, 0, 3), degrees)));
            Assert.Equal(new[] { true }, AbutsAnswers(backer, RotatedAboutZ(Rect(3, 0.1, 8, 0.1, 1, 4), degrees)));
            Assert.Equal(new[] { false }, AbutsAnswers(backer, RotatedAboutZ(Rect(5, 0.31, 10, 0.31, 0, 3), degrees)));
            Assert.Equal(new[] { false }, AbutsAnswers(backer, RotatedAboutZ(Rect(0, 0, 5, 0, 3.2, 6), degrees)));
            Assert.Equal(new[] { false }, AbutsAnswers(backer, RotatedAboutZ(Rect(5.5, 0, 10, 0, 0, 3), degrees)));
        }

        // ---- near-vertical walls -----------------------------------------------------------------------------------

        [Theory]
        [InlineData(0.25)]
        [InlineData(1.0)]
        [InlineData(3.0)]
        public void AbutsColinearWithin_LeaningWalls_SameVerdictsAsVerticalInEveryPresentation(double lean)
        {
            // The walls lean by the same angle, so they share one plane (offset by the jog). The height is world Z, the run
            // the horizontal axis of the normal: the verdicts are those of the vertical wall, not of the plane's own axes.
            Point3D[] backer = LeaningRect(0, 0, 5, 0, 0, 3, 0, lean);

            Assert.Equal(new[] { true }, AbutsAnswers(backer, LeaningRect(5, 0, 10, 0, 0, 3, 0, lean)));
            Assert.Equal(new[] { true }, AbutsAnswers(backer, LeaningRect(5, 0, 10, 0, 0, 3, 0.23, lean)));
            Assert.Equal(new[] { false }, AbutsAnswers(backer, LeaningRect(5, 0, 10, 0, 0, 3, 0.31, lean)));
            Assert.Equal(new[] { false }, AbutsAnswers(backer, LeaningRect(0, 0, 5, 0, 3.2, 6, 0, lean)));
            Assert.Equal(new[] { false }, AbutsAnswers(backer, LeaningRect(5.5, 0, 10, 0, 0, 3, 0, lean)));
        }

        // ---- exact tolerance boundaries ----------------------------------------------------------------------------

        [Theory]
        [InlineData(0.25, true)]
        [InlineData(0.2500001, false)]
        public void AbutsColinearWithin_OffsetAtTheLimit_InclusiveAndTheSameInEveryPresentation(double jog, bool expected)
        {
            // 0.25 is exact in binary and the wall is axis aligned, so the boundary value itself is deterministic.
            Assert.Equal(new[] { expected }, AbutsAnswers(Rect(0, 0, 5, 0, 0, 3), Rect(5, jog, 10, jog, 0, 3), 0.25, 0.125));
        }

        [Theory]
        [InlineData(5.25, true)]
        [InlineData(5.2500001, false)]
        public void AbutsColinearWithin_RunGapAtTheLimit_InclusiveAndTheSameInEveryPresentation(double startX, bool expected)
        {
            Assert.Equal(new[] { expected }, AbutsAnswers(Rect(0, 0, 5, 0, 0, 3), Rect(startX, 0, 10, 0, 0, 3), 0.25, 0.125));
        }

        [Theory]
        [InlineData(2.875, false)]
        [InlineData(2.8749999, true)]
        public void AbutsColinearWithin_HeightOverlapAtTheTolerance_ExclusiveAndTheSameInEveryPresentation(double candidateBottom, bool expected)
        {
            // Overlap of exactly the tolerance does not count as the same band (overlap <= tolerance is false).
            Assert.Equal(new[] { expected }, AbutsAnswers(Rect(0, 0, 5, 0, 0, 3), Rect(0, 0, 5, 0, candidateBottom, 6), 0.25, 0.125));
        }

        [Theory]
        [InlineData(45, 0.25 - 1e-9, true)]
        [InlineData(45, 0.25 + 1e-9, false)]
        [InlineData(137, 0.25 - 1e-9, true)]
        [InlineData(137, 0.25 + 1e-9, false)]
        public void AbutsColinearWithin_OffsetJustEitherSideOfTheLimitAtABearing_SameVerdictInEveryPresentation(double degrees, double jog, bool expected)
        {
            // Off the axes the boundary value itself is subject to rounding; a nanometre either side is not.
            Assert.Equal(new[] { expected }, AbutsAnswers(RotatedAboutZ(Rect(0, 0, 5, 0, 0, 3), degrees), RotatedAboutZ(Rect(5, jog, 10, jog, 0, 3), degrees), 0.25, 0.125));
        }

        // ---- near-horizontal planes ---------------------------------------------------------------------------------

        [Fact]
        public void AbutsColinearWithin_NearHorizontalPlaneAboveTheCapThreshold_OneAnswerInEveryPresentation()
        {
            // A plane whose normal has a horizontal part of 1e-6 (above the 1e-9 switch to the plane-frame reading) has a run
            // axis defined by the normal alone. Production only calls this for vertical walls; this pins that the reading is
            // still presentation independent just past the switch.
            double rise = 1e-6;
            Point3D[] a = { new Point3D(0, 0, 0), new Point3D(5, 0, 0), new Point3D(5, 5, 5 * rise), new Point3D(0, 5, 5 * rise) };
            Point3D[] b = { new Point3D(5.1, 0, 0), new Point3D(10, 0, 0), new Point3D(10, 5, 5 * rise), new Point3D(5.1, 5, 5 * rise) };

            Assert.Single(AbutsAnswers(a, b));
        }

        // ---- recorded Softhouse walls (frames.txt, 4 decimals): backer #294, candidates #287 and #329 ------------------

        private static readonly Point3D[] Wall294 = Rect(10.5724, -6.6175, 10.7262, -2.5286, 0.025, 3.475);
        private static readonly Point3D[] Wall287 = Rect(10.7263, -2.887, 10.7263, -2.4363, 3.45, 6.475);
        private static readonly Point3D[] Wall329 = Rect(10.7263, -2.5267, 10.7263, -2.4363, 0.025, 3.475);

        [Fact]
        public void AbutsColinearWithin_Softhouse294Against287_TrueInEveryPresentation()
        {
            // Upstairs piece overlapping 0.358 m along the run, 0.025 m of height overlap. The 3 decimal answer on the
            // face-order run where #294 has the run on AxisX; the other order gave the same answer by accident.
            Assert.Equal(new[] { true }, AbutsAnswers(Wall294, Wall287));
        }

        [Fact]
        public void AbutsColinearWithin_Softhouse294Against329_TrueInEveryPresentation()
        {
            // 0.09 m ground piece end to end with #294. Axes with the height on AxisX answered false for it.
            Assert.Equal(new[] { true }, AbutsAnswers(Wall294, Wall329));
        }

        // ---- the legacy frame is the defect (mutation: the old reading would fail the cases above) -----------------

        [Fact]
        public void LegacyPlaneFrameReading_StackedStoreysWithAHeightGap_GivesBothAnswers()
        {
            // The oracle reproduces the original implementation (plane AxisX as the run, AxisY as the height). If this
            // test ever stops finding both answers, the fixtures no longer detect the defect.
            HashSet<bool> answers = new HashSet<bool>();
            List<SnappedPanel> candidates = Presentations(Rect(0, 0, 5, 0, 3.2, 6), 1);
            foreach (SnappedPanel a in Presentations(Rect(0, 0, 5, 0, 0, 3)))
            {
                foreach (SnappedPanel b in candidates)
                {
                    answers.Add(LegacyAbuts(a, b, MaxOffset, Tolerance));
                }
            }

            Assert.Equal(2, answers.Count);
        }

        [Fact]
        public void LegacyPlaneFrameReading_Softhouse294Against329_GivesBothAnswers()
        {
            HashSet<bool> answers = new HashSet<bool>();
            List<SnappedPanel> candidates = Presentations(Wall329, 1);
            foreach (SnappedPanel a in Presentations(Wall294))
            {
                foreach (SnappedPanel b in candidates)
                {
                    answers.Add(LegacyAbuts(a, b, MaxOffset, Tolerance));
                }
            }

            Assert.Equal(2, answers.Count);
        }

        /// <summary>The original <c>AbutsColinearWithin</c>, kept here as a test oracle.</summary>
        private static bool LegacyAbuts(SnappedPanel a, SnappedPanel b, double maxOffset, double tolerance)
        {
            if (System.Math.Abs(a.Plane.Distance(b.Plane.Origin)) > maxOffset)
            {
                return false;
            }

            (double minU, double maxU, double minV, double maxV) boundsA = LegacyBounds(a.Plane, a.Face3D);
            (double minU, double maxU, double minV, double maxV) boundsB = LegacyBounds(a.Plane, b.Face3D);
            double overlapV = System.Math.Min(boundsA.maxV, boundsB.maxV) - System.Math.Max(boundsA.minV, boundsB.minV);
            if (overlapV <= tolerance)
            {
                return false;
            }

            return System.Math.Max(boundsA.minU, boundsB.minU) - System.Math.Min(boundsA.maxU, boundsB.maxU) <= maxOffset;
        }

        private static (double, double, double, double) LegacyBounds(Plane plane, Face3D face)
        {
            List<Point2D> points = ((ISegmentable3D)face.GetExternalEdge3D()).GetPoints().Select(p => plane.Convert(p)).ToList();
            return (points.Min(p => p.X), points.Max(p => p.X), points.Min(p => p.Y), points.Max(p => p.Y));
        }

        // ---- degenerate axis ---------------------------------------------------------------------------------------

        [Fact]
        public void AbutsColinearWithin_HorizontalCaps_KeepsThePlaneFrameReading()
        {
            // A cap has no horizontal run axis. It falls back to the plane's own axes, as before: neighbouring tiles
            // 0.1 m apart in height, abutting along X, aligned.
            SnappedPanel a = new SnappedPanel(0, TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(5, 0, 0), new Point3D(5, 5, 0), new Point3D(0, 5, 0)), 1, 0.1, 0.5);
            SnappedPanel b = new SnappedPanel(1, TestGeometry.CreatePlanarFace(new Point3D(5.1, 0, 0.1), new Point3D(10, 0, 0.1), new Point3D(10, 5, 0.1), new Point3D(5.1, 5, 0.1)), 1, 0.1, 0.5);
            SnappedPanel far = new SnappedPanel(2, TestGeometry.CreatePlanarFace(new Point3D(6, 0, 0.1), new Point3D(10, 0, 0.1), new Point3D(10, 5, 0.1), new Point3D(6, 5, 0.1)), 1, 0.1, 0.5);

            Assert.True(a.AbutsColinearWithin(b, MaxOffset, Tolerance));
            Assert.Equal(LegacyAbuts(a, b, MaxOffset, Tolerance), a.AbutsColinearWithin(b, MaxOffset, Tolerance));
            Assert.False(a.AbutsColinearWithin(far, MaxOffset, Tolerance));
        }

        [Fact]
        public void DegenerateWallLoops_NeverFormAPanel_SoNeverReachTheWallReading()
        {
            // A loop collapsed onto a horizontal line (zero height), a vertical line (zero run) or a point creates no face, and
            // a panel cannot be built without one: degenerate walls are rejected before AbutsColinearWithin is reached.
            Point3D[][] loops =
            {
                new[] { new Point3D(0, 0, 1.5), new Point3D(5, 0, 1.5), new Point3D(10, 0, 1.5), new Point3D(2, 0, 1.5) },
                new[] { new Point3D(5, 0, 0), new Point3D(5, 0, 1.5), new Point3D(5, 0, 3), new Point3D(5, 0, 2) },
                new[] { new Point3D(5, 0, 1.5), new Point3D(5, 0, 1.5), new Point3D(5, 0, 1.5), new Point3D(5, 0, 1.5) },
            };

            foreach (Point3D[] loop in loops)
            {
                Face3D face = TestGeometry.CreatePlanarFace(loop);

                Assert.Null(face);
                Assert.Throws<System.ArgumentNullException>(() => new SnappedPanel(0, face, 1, 0.1, 0.5));
            }
        }
        [Fact]
        public void AbutsColinearWithin_NullInput_IsFalse()
        {
            SnappedPanel wall = Presentations(Rect(0, 0, 5, 0, 0, 3))[0];

            Assert.False(wall.AbutsColinearWithin(null, MaxOffset, Tolerance));
        }
    }
}
