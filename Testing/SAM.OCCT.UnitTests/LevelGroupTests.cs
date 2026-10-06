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
    /// Unit tests for <see cref="LevelGroup"/> / <see cref="LevelFrame.GroupFrames"/> - the P2 second-stage
    /// level grouping (docs/CONTROLLED_WORKFLOW_PLAN.md §4.1). Pure-managed: no native OCCT DLL required.
    /// Covers the acceptance list: the EXACT fixture elevations 12.24/12.436/15.29/15.473/18.34 grouping at
    /// band 0.21 -> 3 groups (datums 12.24/15.29/18.34), 0.15 -> 5, 0 -> 5 (identity); dominant-area-first
    /// datum choice; seed-anchored non-transitivity; shuffle determinism; tilted perpendicular membership;
    /// and the near-miss diagnostic.
    /// </summary>
    public class LevelGroupTests
    {
        /// <summary>A horizontal floor tile of side <paramref name="size"/> at elevation <paramref name="z"/>
        /// (area = size²) - the tile's area drives the dominant-area seed order.</summary>
        private static Face3D FloorTile(double x0, double y0, double size, double z)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(x0, y0, z),
                new Point3D(x0 + size, y0, z),
                new Point3D(x0 + size, y0 + size, z),
                new Point3D(x0, y0 + size, z));
        }

        /// <summary>A cap tile tilted about world X by <paramref name="tiltRadians"/>, its whole plane raised
        /// <paramref name="zShift"/> in world Z (so its perpendicular offset from the un-shifted tile is
        /// cos(tilt)·zShift).</summary>
        private static Face3D TiltedTile(double x0, double x1, double tiltRadians, double zShift)
        {
            double c = Math.Cos(tiltRadians);
            double s = Math.Sin(tiltRadians);
            return TestGeometry.CreatePlanarFace(
                new Point3D(x0, 0, zShift),
                new Point3D(x1, 0, zShift),
                new Point3D(x1, c, s + zShift),
                new Point3D(x0, c, s + zShift));
        }

        /// <summary>The five raw fixture frames (12.24 / 12.436 / 15.29 / 15.473 / 18.34), with the true-datum
        /// members (12.24, 15.29, 18.34) given the larger area so they are the dominant seeds.</summary>
        private static List<LevelFrame> FixtureFrames()
        {
            List<Face3D> caps = new List<Face3D>
            {
                FloorTile(0, 0, 10, 12.240),   // area 100 - dominant of the 12.x group
                FloorTile(0, 0, 3, 12.436),    // area 9
                FloorTile(0, 0, 9, 15.290),    // area 81 - dominant of the 15.x group
                FloorTile(0, 0, 2, 15.473),    // area 4
                FloorTile(0, 0, 5, 18.340),    // area 25
            };

            List<LevelFrame> frames = LevelFrame.Cluster(caps);
            Assert.Equal(5, frames.Count); // guard: the 0.196/0.183 gaps exceed the pinned 0.15 raw band
            return frames;
        }

        [Fact]
        public void GroupFrames_FixtureElevationsBand021_ProducesThreeGroupsAtTrueDatums()
        {
            // Arrange - the five raw fixture frames.
            List<LevelFrame> frames = FixtureFrames();

            // Act - merge over the controlled fixture's 0.21 m band.
            List<LevelGroup> groups = LevelFrame.GroupFrames(frames, 0.21);

            // Assert - three storey groups at the dominant (true) datums 12.24 / 15.29 / 18.34.
            Assert.Equal(3, groups.Count);
            Assert.Equal(new List<double> { 12.240, 15.290, 18.340 }, groups.Select(x => Math.Round(x.Elevation, 3)));
            Assert.Equal(2, groups[0].FrameIndices.Count); // 12.240 + 12.436
            Assert.Equal(2, groups[1].FrameIndices.Count); // 15.290 + 15.473
            Assert.Single(groups[2].FrameIndices);         // 18.340 alone
            Assert.Equal(0.196, groups[0].Spread, 3);      // 12.436 - 12.240
        }

        [Fact]
        public void GroupFrames_FixtureElevationsBand015_ProducesFiveGroupsWithNearMiss()
        {
            // Arrange
            List<LevelFrame> frames = FixtureFrames();
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act - at 0.15 m the 0.196/0.183 gaps do NOT merge; each frame is its own group.
            List<LevelGroup> groups = LevelFrame.GroupFrames(frames, 0.15, LevelFrame.DEFAULT_NormalConeTolerance, diagnostics);

            // Assert - five groups, and the two near-misses (0.196, 0.183 within (0.15, 0.30]) are reported.
            Assert.Equal(5, groups.Count);
            Assert.Contains(diagnostics.All, x => x.Code == DiagnosticCode.LevelBandNearMiss);
        }

        [Fact]
        public void GroupFrames_BandZero_IsIdentityWithNoNearMiss()
        {
            // Arrange
            List<LevelFrame> frames = FixtureFrames();
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act - band 0 = off: groups are exactly the frames (identity).
            List<LevelGroup> groups = LevelFrame.GroupFrames(frames, 0.0, LevelFrame.DEFAULT_NormalConeTolerance, diagnostics);

            // Assert - one group per frame, same elevations, and NO near-miss noise on the off path.
            Assert.Equal(frames.Count, groups.Count);
            Assert.Equal(frames.Select(x => Math.Round(x.Elevation, 3)), groups.Select(x => Math.Round(x.Elevation, 3)));
            Assert.All(groups, g => Assert.Single(g.FrameIndices));
            Assert.DoesNotContain(diagnostics.All, x => x.Code == DiagnosticCode.LevelBandNearMiss);
        }

        [Fact]
        public void GroupFrames_HigherFrameHasLargerArea_DominantAreaWinsDatum()
        {
            // Arrange - two frames 0.18 m apart; the HIGHER one (10.18) has the larger area, so dominant-area
            // must beat lower-elevation for the group datum.
            List<LevelFrame> frames = LevelFrame.Cluster(new List<Face3D>
            {
                FloorTile(0, 0, 2, 10.00),   // area 4
                FloorTile(0, 0, 10, 10.18),  // area 100 - dominant
            });
            Assert.Equal(2, frames.Count);

            // Act
            List<LevelGroup> groups = LevelFrame.GroupFrames(frames, 0.21);

            // Assert - one group whose datum is the DOMINANT (larger-area) frame's elevation, not the lower.
            Assert.Single(groups);
            Assert.Equal(10.18, groups[0].Elevation, 3);
        }

        [Fact]
        public void GroupFrames_ChainedBeyondSeed_IsNonTransitive()
        {
            // Arrange - datums at 0, 0.18, 0.36. Each adjacent pair is within 0.21, but 0.36 is > 0.21 from the
            // dominant seed at 0. Seed-anchored membership must NOT chain 0.36 in via 0.18.
            List<LevelFrame> frames = LevelFrame.Cluster(new List<Face3D>
            {
                FloorTile(0, 0, 10, 0.00),  // area 100 - dominant seed
                FloorTile(0, 0, 7, 0.18),   // area 49
                FloorTile(0, 0, 3, 0.36),   // area 9
            });
            Assert.Equal(3, frames.Count);

            // Act
            List<LevelGroup> groups = LevelFrame.GroupFrames(frames, 0.21);

            // Assert - two groups: {0, 0.18} anchored at 0, and {0.36} on its own (not chained in transitively).
            Assert.Equal(2, groups.Count);
            Assert.Equal(0.00, groups[0].Elevation, 3);
            Assert.Equal(2, groups[0].FrameIndices.Count);
            Assert.Equal(0.36, groups[1].Elevation, 3);
        }

        [Fact]
        public void GroupFrames_InputOrderShuffled_ProducesIdenticalGroups()
        {
            // Arrange - the fixture frames, and the same frames in reverse order (GroupFrames must be
            // order-independent, not rely on the caller's frame order).
            List<LevelFrame> frames = FixtureFrames();
            List<LevelFrame> shuffled = Enumerable.Reverse(frames).ToList();

            // Act
            List<LevelGroup> groups1 = LevelFrame.GroupFrames(frames, 0.21);
            List<LevelGroup> groups2 = LevelFrame.GroupFrames(shuffled, 0.21);

            // Assert - identical group count and datums.
            Assert.Equal(groups1.Count, groups2.Count);
            Assert.Equal(groups1.Select(x => Math.Round(x.Elevation, 3)), groups2.Select(x => Math.Round(x.Elevation, 3)));
        }

        [Fact]
        public void GroupFrames_TiltedFramesWithinPerpendicularBand_MergeByPerpendicularDistance()
        {
            // Arrange - two co-tilted (25°) caps whose planes are ~0.18 m apart PERPENDICULAR (a 0.20 m world-Z
            // shift × cos25°): far enough to be two raw frames, close enough to group at 0.21.
            double tilt = 25.0 * (Math.PI / 180.0);
            List<LevelFrame> frames = LevelFrame.Cluster(new List<Face3D>
            {
                TiltedTile(0, 3, tilt, 0.00),  // larger footprint => dominant seed
                TiltedTile(0, 1, tilt, 0.20),
            });
            Assert.Equal(2, frames.Count); // guard: perpendicular ~0.18 m > 0.15 raw band

            // Act
            List<LevelGroup> groups = LevelFrame.GroupFrames(frames, 0.21);

            // Assert - one tilted group; membership was decided by perpendicular datum distance, not world Z.
            Assert.Single(groups);
            Assert.Equal(tilt, groups[0].TiltAngle, 3);
            Assert.Equal(2, groups[0].FrameIndices.Count);
        }

        [Fact]
        public void GroupFrames_NullOrEmpty_ReturnsEmpty()
        {
            // Act & Assert
            Assert.Empty(LevelFrame.GroupFrames(null, 0.21));
            Assert.Empty(LevelFrame.GroupFrames(new List<LevelFrame>(), 0.21));
        }
    }
}
