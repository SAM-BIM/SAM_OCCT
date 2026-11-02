// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// <see cref="Panel3DSnapSolver.ExtendWalls"/> overshoot at shallow joints. The overshoot gives the native build a
    /// clean crossing; past a joint where the walls meet at a few degrees the tip lies a few millimetres clear of the
    /// target wall instead, and the Softhouse reversed-face run (houses A and B, 2.15 degrees) joined two cells there.
    /// Pure managed.
    /// </summary>
    [Collection(ExtendCensusCollection.Name)]
    public class ExtendWallsShallowJunctionTests
    {
        private const double VerticalAngle = 20 * (System.Math.PI / 180);
        private const double Overshoot = 0.05;
        private const double Tolerance = 1e-6;
        private const double ShortOfTarget = 0.1;

        /// <summary>The wall ends exactly on the junction, within a tolerance far below the overshoot.</summary>
        private const double OnJunction = 1e-5;

        [Theory]
        [InlineData(0.5)]
        [InlineData(1.0)]
        [InlineData(2.15)] // the Softhouse A/B party-wall joint
        [InlineData(4.0)]
        [InlineData(4.9)]
        public void ExtendWalls_EndMeetingAWallBelowTheShallowLimit_StopsOnTheJunction(double degrees)
        {
            (SnappedPanel approaching, SnappedPanel target) = ShallowTJunction(degrees);

            Panel3DSnapSolver.ExtendWalls(new List<SnappedPanel> { approaching, target }, VerticalAngle, Overshoot, Tolerance);

            Assert.True(DistanceFromJunction(approaching) <= OnJunction,
                $"At {degrees} degrees the end should stop on the junction, not {DistanceFromJunction(approaching):0.#######} m from it");
        }

        [Theory]
        [InlineData(5.1)]
        [InlineData(6.0)]
        [InlineData(10.0)]
        [InlineData(15.0)]
        [InlineData(30.0)]
        [InlineData(60.0)]
        [InlineData(89.0)]
        public void ExtendWalls_EndMeetingAWallAboveTheShallowLimit_KeepsTheOvershoot(double degrees)
        {
            (SnappedPanel approaching, SnappedPanel target) = ShallowTJunction(degrees);

            Panel3DSnapSolver.ExtendWalls(new List<SnappedPanel> { approaching, target }, VerticalAngle, Overshoot, Tolerance);

            Assert.Equal(Overshoot, DistanceFromJunction(approaching), 5);
        }

        [Fact]
        public void ExtendWalls_SmallestAngleTheSolverJoins_StopsOnTheJunction()
        {
            // The 2D solver treats walls within 0.3 degrees as parallel and does not join them (SnapSolver.ArcToleranceAngleRad);
            // 0.35 degrees is the shallowest joint it still makes, and is below the limit.
            (SnappedPanel approaching, SnappedPanel target) = ShallowTJunction(0.35);

            Panel3DSnapSolver.ExtendWalls(new List<SnappedPanel> { approaching, target }, VerticalAngle, Overshoot, Tolerance);

            Assert.True(DistanceFromJunction(approaching) <= OnJunction);
        }

        [Fact]
        public void ExtendWalls_ShallowLimit_IsFiveDegrees()
        {
            Assert.Equal(5.0, SHALLOW_LIMIT_DEGREES(), 9);
        }

        [Fact]
        public void ExtendWalls_EndAlsoMeetingASteepWall_KeepsTheOvershoot()
        {
            // The end meets the shallow wall (x = 0) and a crossing wall (y = 0) at the same point: the crossing wall
            // still needs the overshoot, so the end keeps it.
            (SnappedPanel approaching, SnappedPanel target) = ShallowTJunction(2.15);
            SnappedPanel crossing = Wall(2, new Point3D(-5, 0, 0), new Point3D(5, 0, 0));

            Panel3DSnapSolver.ExtendWalls(new List<SnappedPanel> { approaching, target, crossing }, VerticalAngle, Overshoot, Tolerance);

            Assert.Equal(Overshoot, DistanceFromJunction(approaching), 5);
        }

        [Fact]
        public void ExtendWalls_ShallowJunction_RecordsNoOvershootAndTheReason()
        {
            (SnappedPanel approaching, SnappedPanel target) = ShallowTJunction(2.15);
            List<ExtendRecord> records = new List<ExtendRecord>();

            Panel3DSnapSolver.ExtendWalls(new List<SnappedPanel> { approaching, target }, VerticalAngle, Overshoot, Tolerance, records);

            ExtendRecord record = Assert.Single(records.Where(x => x.PanelIndex == 0 && x.Outcome == ExtendOutcome.Applied));
            Assert.Equal(0.0, record.Overshoot);
            Assert.Contains("shallow", record.TargetDescription);
        }

        [Fact]
        public void ExtendWalls_SteepJunction_RecordsTheOvershoot()
        {
            (SnappedPanel approaching, SnappedPanel target) = ShallowTJunction(30);
            List<ExtendRecord> records = new List<ExtendRecord>();

            Panel3DSnapSolver.ExtendWalls(new List<SnappedPanel> { approaching, target }, VerticalAngle, Overshoot, Tolerance, records);

            ExtendRecord record = Assert.Single(records.Where(x => x.PanelIndex == 0 && x.Outcome == ExtendOutcome.Applied));
            Assert.Equal(Overshoot, record.Overshoot);
            Assert.DoesNotContain("shallow", record.TargetDescription);
        }

        [Theory]
        [InlineData(2.15)]
        [InlineData(30.0)]
        public void ExtendWalls_FaceVertexOrderReversed_GivesTheSameEnd(double degrees)
        {
            (SnappedPanel approaching, SnappedPanel target) = ShallowTJunction(degrees);
            (SnappedPanel reversedApproaching, SnappedPanel reversedTarget) = ShallowTJunction(degrees, reverse: true);

            Panel3DSnapSolver.ExtendWalls(new List<SnappedPanel> { approaching, target }, VerticalAngle, Overshoot, Tolerance);
            Panel3DSnapSolver.ExtendWalls(new List<SnappedPanel> { reversedApproaching, reversedTarget }, VerticalAngle, Overshoot, Tolerance);

            Assert.Equal(DistanceFromJunction(approaching), DistanceFromJunction(reversedApproaching), 6);
        }

        [Fact]
        public void ExtendWalls_GrownEndAgainstAPerpendicularWall_KeepsTheOvershootAndTheTrimmedEndIsExact()
        {
            // The common corner: the wall's end is grown by the overshoot, its untouched end stays where it is.
            (SnappedPanel approaching, SnappedPanel target) = ShallowTJunction(90);

            Panel3DSnapSolver.ExtendWalls(new List<SnappedPanel> { approaching, target }, VerticalAngle, Overshoot, Tolerance);

            Assert.Equal(Overshoot, DistanceFromJunction(approaching), 5);
            Assert.Equal(-8.0, approaching.GetBoundingBox().Min.X, 5);
        }

        private static double SHALLOW_LIMIT_DEGREES() => Panel3DSnapSolver.SHALLOW_JUNCTION_MAX_ANGLE * 180 / System.Math.PI;

        /// <summary>A long wall along Y at x = 0 (y -10..7, so the junction is not at its middle) and a wall approaching it from the south-west at
        /// <paramref name="degrees"/> to it, 8 m long, ending <see cref="ShortOfTarget"/> short of the junction at
        /// the origin (its line passes through the origin).</summary>
        private static (SnappedPanel Approaching, SnappedPanel Target) ShallowTJunction(double degrees, bool reverse = false)
        {
            double theta = degrees * (System.Math.PI / 180);
            double ux = System.Math.Sin(theta), uy = System.Math.Cos(theta);
            // The approaching wall's line is -8 u .. -ShortOfTarget u from the origin. For 90 degrees it runs along X.
            Point3D start = new Point3D(-8 * ux, -8 * uy, 0);
            Point3D end = new Point3D(-ShortOfTarget * ux, -ShortOfTarget * uy, 0);
            SnappedPanel approaching = Wall(0, start, end, reverse);
            SnappedPanel target = Wall(1, new Point3D(0, -10, 0), new Point3D(0, 7, 0), reverse);
            return (approaching, target);
        }

        private static SnappedPanel Wall(int index, Point3D start, Point3D end, bool reverse = false)
        {
            Point3D[] points =
            {
                new Point3D(start.X, start.Y, 0), new Point3D(end.X, end.Y, 0),
                new Point3D(end.X, end.Y, 3), new Point3D(start.X, start.Y, 3),
            };
            if (reverse)
            {
                points = points.Reverse().ToArray();
            }

            return new SnappedPanel(index, TestGeometry.CreatePlanarFace(points), 1, 0.3, maxExtension: 1.0);
        }

        /// <summary>Plan distance from the junction (the origin) to the end of the wall nearest to it.</summary>
        private static double DistanceFromJunction(SnappedPanel wall)
        {
            Segment3D foot = wall.GetBaseSegment(Tolerance);
            return new[] { foot.GetStart(), foot.GetEnd() }
                .Min(p => System.Math.Sqrt((p.X * p.X) + (p.Y * p.Y)));
        }
    }
}
