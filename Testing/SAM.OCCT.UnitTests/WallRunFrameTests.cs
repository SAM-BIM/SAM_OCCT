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
    /// other). Every case here runs the same geometry through every combination of plane axes (normal direction and
    /// run/height swap), loop start and loop winding and asks for one answer. Pure managed.
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

        /// <summary>One panel for every way the same wall can be presented: the four plane-axis assignments with the
        /// run or the height first, the normal either way, and the loop started at each vertex in either winding.</summary>
        private static List<SnappedPanel> Presentations(Point3D[] loop, int sourceIndex = 0)
        {
            Point3D first = loop[0];
            Vector3D run = new Vector3D(loop[1].X - first.X, loop[1].Y - first.Y, 0).Unit;
            Vector3D up = new Vector3D(0, 0, 1);
            List<(Vector3D X, Vector3D Y)> axes = new List<(Vector3D, Vector3D)>
            {
                (run, up), (run.GetNegated(), up), (up, run), (up.GetNegated(), run),
                (run, up.GetNegated()), (run.GetNegated(), up.GetNegated()), (up, run.GetNegated()), (up.GetNegated(), run.GetNegated()),
            };

            List<SnappedPanel> result = new List<SnappedPanel>();
            int variant = 0;
            foreach ((Vector3D x, Vector3D y) in axes)
            {
                Plane plane = new Plane(first, x, y);
                for (int reversed = 0; reversed < 2; reversed++)
                {
                    List<Point3D> points = loop.ToList();
                    if (reversed == 1)
                    {
                        points.Reverse();
                    }

                    int start = variant++ % points.Count;
                    points = points.Skip(start).Concat(points.Take(start)).ToList();
                    Face3D face = new Face3D(plane, new Polygon2D(points.Select(p => plane.Convert(p))));
                    result.Add(new SnappedPanel(sourceIndex, face, 1, 0.1, 0.5));
                }
            }

            return result;
        }

        /// <summary>The answer for every presentation of both walls; all must agree.</summary>
        private static HashSet<bool> AbutsAnswers(Point3D[] backer, Point3D[] candidate, double maxOffset = MaxOffset)
        {
            HashSet<bool> answers = new HashSet<bool>();
            List<SnappedPanel> candidates = Presentations(candidate, 1);
            foreach (SnappedPanel a in Presentations(backer))
            {
                foreach (SnappedPanel b in candidates)
                {
                    answers.Add(a.AbutsColinearWithin(b, maxOffset, Tolerance));
                }
            }

            return answers;
        }

        [Fact]
        public void Presentations_OneWall_AreGenuinelyDifferentFrames()
        {
            // Arrange / Act: guards the fixtures - if all presentations shared the run on AxisX nothing below would test anything.
            List<SnappedPanel> panels = Presentations(Rect(0, 0, 5, 0, 0, 3));
            int runOnAxisX = panels.Count(p => System.Math.Abs(p.Plane.AxisX.DotProduct(new Vector3D(1, 0, 0))) > 0.99);
            int heightOnAxisX = panels.Count(p => System.Math.Abs(p.Plane.AxisX.DotProduct(new Vector3D(0, 0, 1))) > 0.99);

            // Assert
            Assert.Equal(16, panels.Count);
            Assert.Equal(8, runOnAxisX);
            Assert.Equal(8, heightOnAxisX);
            Assert.Contains(panels, p => p.Plane.Normal.Y > 0.99);
            Assert.Contains(panels, p => p.Plane.Normal.Y < -0.99);
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
        public void AbutsColinearWithin_NullOrDegenerateInput_IsFalse()
        {
            SnappedPanel wall = Presentations(Rect(0, 0, 5, 0, 0, 3))[0];

            Assert.False(wall.AbutsColinearWithin(null, MaxOffset, Tolerance));
        }
    }
}
