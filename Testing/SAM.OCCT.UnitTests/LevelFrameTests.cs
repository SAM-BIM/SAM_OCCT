// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="LevelFrame"/> - the Phase 6 level-datum clustering foundation
    /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §E Phase 6). Pure-managed: no native OCCT DLL
    /// required. Covers the acceptance list: flat single level -> one frame; tilted single level -> one
    /// tilted frame; two stacked levels -> two frames; a ~0.25 m split-level landing preserved (not merged
    /// away); deterministic clustering order; and the ambiguous-membership diagnostic.
    /// </summary>
    public class LevelFrameTests
    {
        /// <summary>A horizontal (normal +Z) cap tile spanning [x0,x0+size] x [y0,y0+size] at elevation z.</summary>
        private static Face3D FloorTile(double x0, double y0, double size, double z)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(x0, y0, z),
                new Point3D(x0 + size, y0, z),
                new Point3D(x0 + size, y0 + size, z),
                new Point3D(x0, y0 + size, z));
        }

        /// <summary>
        /// A cap tile spanning x in [x0,x1], y in [0,1], tilted about the world X-axis by
        /// <paramref name="tiltRadians"/>: its plane normal is (0, -sin, cos), so |normal.Z| = cos(tilt) - a
        /// cap, not a wall, for any tilt below 90°. Two such tiles at different x share the SAME tilted plane.
        /// </summary>
        private static Face3D TiltedFloorTile(double x0, double x1, double tiltRadians)
        {
            double c = Math.Cos(tiltRadians);
            double s = Math.Sin(tiltRadians);
            return TestGeometry.CreatePlanarFace(
                new Point3D(x0, 0, 0),
                new Point3D(x1, 0, 0),
                new Point3D(x1, c, s),
                new Point3D(x0, c, s));
        }

        /// <summary>A vertical (normal ±X) wall in the plane x = <paramref name="x"/>, y in [y0,y1], z in [zBottom,zTop].</summary>
        private static Face3D Wall(double x, double y0, double y1, double zBottom, double zTop)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(x, y0, zBottom),
                new Point3D(x, y1, zBottom),
                new Point3D(x, y1, zTop),
                new Point3D(x, y0, zTop));
        }

        [Fact]
        public void Cluster_FlatSingleTile_ProducesOneFrameAtItsElevation()
        {
            // Arrange - a single flat floor tile at z = 0.
            List<Face3D> caps = new List<Face3D> { FloorTile(0, 0, 1, 0) };

            // Act
            List<LevelFrame> frames = LevelFrame.Cluster(caps);

            // Assert - one flat frame at elevation 0.
            Assert.Single(frames);
            Assert.Equal(0.0, frames[0].Elevation, 6);
            Assert.Equal(0.0, frames[0].TiltAngle, 6);
            Assert.True(frames[0].Normal.Z > 0.999); // up-axis is +Z
        }

        [Fact]
        public void Cluster_FlatSingleLevelTwoCoplanarTiles_MergesIntoOneFrame()
        {
            // Arrange - two adjacent floor tiles on one datum (z = 0): one flat level.
            List<Face3D> caps = new List<Face3D> { FloorTile(0, 0, 1, 0), FloorTile(1, 0, 1, 0) };

            // Act
            List<LevelFrame> frames = LevelFrame.Cluster(caps);

            // Assert - a single frame owning both tiles.
            Assert.Single(frames);
            Assert.Equal(new List<int> { 0, 1 }, frames[0].CapIndices);
        }

        [Fact]
        public void Cluster_TiltedSingleLevel_ProducesOneTiltedFrame()
        {
            // Arrange - two coplanar tiles on one datum tilted 25° (well past the 20° world-frame ceiling).
            double tilt = 25.0 * (Math.PI / 180.0);
            List<Face3D> caps = new List<Face3D> { TiltedFloorTile(0, 1, tilt), TiltedFloorTile(1, 2, tilt) };

            // Act
            List<LevelFrame> frames = LevelFrame.Cluster(caps);

            // Assert - one frame whose up-axis carries the 25° tilt.
            Assert.Single(frames);
            Assert.Equal(tilt, frames[0].TiltAngle, 4);
            Assert.Equal(2, frames[0].CapIndices.Count);
        }

        [Fact]
        public void Cluster_TwoStackedLevels_ProducesTwoFramesSortedByElevation()
        {
            // Arrange - a floor slab at z = 0 and a floor slab at z = 3 (a storey apart).
            List<Face3D> caps = new List<Face3D> { FloorTile(0, 0, 1, 0), FloorTile(0, 0, 1, 3) };

            // Act
            List<LevelFrame> frames = LevelFrame.Cluster(caps);

            // Assert - two frames, ascending by elevation.
            Assert.Equal(2, frames.Count);
            Assert.Equal(0.0, frames[0].Elevation, 6);
            Assert.Equal(3.0, frames[1].Elevation, 6);
        }

        [Fact]
        public void Cluster_SplitLevelLanding_IsNotMergedIntoMainFloor()
        {
            // Arrange - a main floor at z = 0 and a landing 0.25 m above it. The 0.25 m step exceeds the
            // 0.15 m default band (and is inside the legacy 0.3 m NormalizeCapOffset that would have eaten
            // it), so it must survive as its own frame - the guard the plan calls for (Risk 5).
            List<Face3D> caps = new List<Face3D> { FloorTile(0, 0, 2, 0), FloorTile(3, 0, 1, 0.25) };

            // Act
            List<LevelFrame> frames = LevelFrame.Cluster(caps);

            // Assert - two distinct frames; the landing is preserved at 0.25 m, not merged away.
            Assert.Equal(2, frames.Count);
            Assert.Equal(0.0, frames[0].Elevation, 6);
            Assert.Equal(0.25, frames[1].Elevation, 6);
        }

        [Fact]
        public void Cluster_LandingWithinBand_MergesIntoMainFloor()
        {
            // Arrange - a "landing" only 0.05 m above the floor: import/tile noise, inside the 0.15 m band.
            List<Face3D> caps = new List<Face3D> { FloorTile(0, 0, 2, 0), FloorTile(3, 0, 1, 0.05) };

            // Act
            List<LevelFrame> frames = LevelFrame.Cluster(caps);

            // Assert - one datum: the near-coincident tiles are the same level.
            Assert.Single(frames);
            Assert.Equal(2, frames[0].CapIndices.Count);
        }

        [Fact]
        public void Cluster_InputOrderShuffled_ProducesIdenticalFrames()
        {
            // Arrange - three levels' worth of tiles (z = 0 x2, z = 3, z = 6) in two different input orders.
            Face3D a = FloorTile(0, 0, 1, 0);
            Face3D b = FloorTile(1, 0, 1, 0);
            Face3D c = FloorTile(0, 0, 1, 3);
            Face3D d = FloorTile(0, 0, 1, 6);
            List<Face3D> order1 = new List<Face3D> { a, b, c, d };
            List<Face3D> order2 = new List<Face3D> { d, c, b, a };

            // Act
            List<LevelFrame> frames1 = LevelFrame.Cluster(order1);
            List<LevelFrame> frames2 = LevelFrame.Cluster(order2);

            // Assert - identical frame count and elevations, in the same (elevation-sorted) order.
            Assert.Equal(3, frames1.Count);
            Assert.Equal(frames1.Count, frames2.Count);
            for (int i = 0; i < frames1.Count; i++)
            {
                Assert.Equal(frames1[i].Elevation, frames2[i].Elevation, 6);
                Assert.Equal(frames1[i].CapIndices.Count, frames2[i].CapIndices.Count);
            }

            Assert.Equal(new List<double> { 0.0, 3.0, 6.0 }, frames1.Select(x => Math.Round(x.Elevation, 6)));
        }

        [Fact]
        public void Cluster_EmptyInput_ReturnsEmpty()
        {
            // Act & Assert
            Assert.Empty(LevelFrame.Cluster(new List<Face3D>()));
            Assert.Empty(LevelFrame.Cluster(null));
        }

        [Fact]
        public void Cluster_ReportsFrameCountDiagnostic()
        {
            // Arrange
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            List<Face3D> caps = new List<Face3D> { FloorTile(0, 0, 1, 0), FloorTile(0, 0, 1, 3) };

            // Act
            LevelFrame.Cluster(caps, diagnostics: diagnostics);

            // Assert - the frame count is reported (never silent).
            Assert.Contains(diagnostics.All, x => x.Message.Contains("level frame"));
        }

        [Fact]
        public void AssignCapToFrame_CapMatchingOneFrame_ReturnsThatFrame()
        {
            // Arrange - two well-separated frames; a cap clearly on the upper datum.
            List<LevelFrame> frames = LevelFrame.Cluster(new List<Face3D> { FloorTile(0, 0, 1, 0), FloorTile(0, 0, 1, 3) });
            Face3D cap = FloorTile(5, 5, 1, 3.0);

            // Act
            int index = LevelFrame.AssignCapToFrame(cap, frames);

            // Assert - the z = 3 frame (index 1 in the elevation-sorted list).
            Assert.Equal(1, index);
        }

        [Fact]
        public void AssignCapToFrame_CapWithinBandOfTwoFrames_PicksNearestAndWarns()
        {
            // Arrange - two frames 0.20 m apart (separate: 0.20 > 0.15 band). A probe cap at z = 0.08 is
            // within 0.15 of BOTH (0.08 and 0.12) - an ambiguous membership.
            List<LevelFrame> frames = LevelFrame.Cluster(new List<Face3D> { FloorTile(0, 0, 1, 0), FloorTile(0, 0, 1, 0.20) });
            Assert.Equal(2, frames.Count); // guard: the two datums did stay distinct
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            Face3D probe = FloorTile(5, 5, 1, 0.08);

            // Act
            int index = LevelFrame.AssignCapToFrame(probe, frames, diagnostics: diagnostics);

            // Assert - nearest datum (z = 0, index 0) wins, and the ambiguity is recorded.
            Assert.Equal(0, index);
            Assert.Contains(diagnostics.All, x => x.Code == DiagnosticCode.AmbiguousLevelFrame);
        }

        [Fact]
        public void AssignCapToFrame_NoMatchingFrame_ReturnsMinusOne()
        {
            // Arrange - a single frame at z = 0; a cap far above every datum.
            List<LevelFrame> frames = LevelFrame.Cluster(new List<Face3D> { FloorTile(0, 0, 1, 0) });
            Face3D cap = FloorTile(0, 0, 1, 10);

            // Act & Assert
            Assert.Equal(-1, LevelFrame.AssignCapToFrame(cap, frames));
        }

        [Fact]
        public void AssignWallToFrames_StoreyHeightWall_SpansBothFloorAndCeilingFrames()
        {
            // Arrange - datums at z = 0 and z = 3; a wall running the full storey height.
            List<LevelFrame> frames = LevelFrame.Cluster(new List<Face3D> { FloorTile(0, 0, 1, 0), FloorTile(0, 0, 1, 3) });
            Face3D wall = Wall(0, 0, 1, 0, 3);

            // Act
            IReadOnlyList<int> indices = LevelFrame.AssignWallToFrames(wall, frames);

            // Assert - the wall belongs to both levels it connects.
            Assert.Equal(new List<int> { 0, 1 }, indices);
        }

        [Fact]
        public void AssignWallToFrames_ShortWall_SpansOnlyItsLevel()
        {
            // Arrange - datums at z = 0 and z = 3; a stub wall from z = 0 to z = 1.
            List<LevelFrame> frames = LevelFrame.Cluster(new List<Face3D> { FloorTile(0, 0, 1, 0), FloorTile(0, 0, 1, 3) });
            Face3D wall = Wall(0, 0, 1, 0, 1);

            // Act
            IReadOnlyList<int> indices = LevelFrame.AssignWallToFrames(wall, frames);

            // Assert - only the z = 0 datum falls within the wall's span.
            Assert.Equal(new List<int> { 0 }, indices);
        }

        [Fact]
        public void AssignWallToFrames_FloatingWall_FallsBackToNearestWithDiagnostic()
        {
            // Arrange - datums at z = 0 and z = 3; a wall floating high (z = 5 to 6) that reaches neither.
            List<LevelFrame> frames = LevelFrame.Cluster(new List<Face3D> { FloorTile(0, 0, 1, 0), FloorTile(0, 0, 1, 3) });
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            Face3D wall = Wall(0, 0, 1, 5, 6);

            // Act
            IReadOnlyList<int> indices = LevelFrame.AssignWallToFrames(wall, frames, diagnostics: diagnostics);

            // Assert - falls back to the nearest datum (z = 3, index 1) and records the ambiguity.
            Assert.Equal(new List<int> { 1 }, indices);
            Assert.Contains(diagnostics.All, x => x.Code == DiagnosticCode.AmbiguousLevelFrame);
        }

        [Fact]
        public void SignedElevation_PointAboveFlatDatum_ReturnsHeightAboveIt()
        {
            // Arrange - a flat frame at z = 2.
            LevelFrame frame = LevelFrame.Cluster(new List<Face3D> { FloorTile(0, 0, 1, 2) }).Single();

            // Act & Assert - a point 1.5 m above the datum measures +1.5; one below measures negative.
            Assert.Equal(1.5, frame.SignedElevation(new Point3D(0, 0, 3.5)), 6);
            Assert.Equal(-2.0, frame.SignedElevation(new Point3D(0, 0, 0.0)), 6);
        }

        [Fact]
        public void Constructor_DownwardNormal_CollapsesUpAxisToPositiveZ()
        {
            // Arrange & Act - a ceiling cap's normal points down; the frame's up-axis must point up.
            LevelFrame frame = new LevelFrame(new Vector3D(0, 0, -1), new Point3D(0, 0, 3));

            // Assert
            Assert.True(frame.Normal.Z > 0.999);
            Assert.Equal(3.0, frame.Elevation, 6);
        }

        [Fact]
        public void ToFromFrameCoordinates_TiltedFrameRoundTrip_ReturnsOriginalPoint()
        {
            // Arrange - a tilted frame and an arbitrary world point.
            double tilt = 25.0 * (Math.PI / 180.0);
            LevelFrame frame = LevelFrame.Cluster(new List<Face3D> { TiltedFloorTile(0, 1, tilt) }).Single();
            Point3D world = new Point3D(0.7, 0.3, 1.4);

            // Act - world -> frame -> world.
            Point3D roundTrip = frame.FromFrameCoordinates(frame.ToFrameCoordinates(world));

            // Assert - the transforms are consistent inverses.
            Assert.Equal(world.X, roundTrip.X, 6);
            Assert.Equal(world.Y, roundTrip.Y, 6);
            Assert.Equal(world.Z, roundTrip.Z, 6);
        }

        [Fact]
        public void ContainsCap_CapOnDatum_ReturnsTrueAndBeyondBandFalse()
        {
            // Arrange - a flat frame at z = 0.
            LevelFrame frame = LevelFrame.Cluster(new List<Face3D> { FloorTile(0, 0, 1, 0) }).Single();

            // Act & Assert
            Assert.True(frame.ContainsCap(FloorTile(2, 2, 1, 0.05)));  // within the 0.15 m band
            Assert.False(frame.ContainsCap(FloorTile(2, 2, 1, 0.50))); // beyond the band
            Assert.False(frame.ContainsCap(Wall(0, 0, 1, 0, 1)));      // a wall is not a cap of this datum
        }
    }
}
