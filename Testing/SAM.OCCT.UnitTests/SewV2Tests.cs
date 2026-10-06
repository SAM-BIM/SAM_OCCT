// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for the Phase 5d capped adaptive sew (<see cref="HealStage.SewV2"/>) - the pure/managed
    /// sub-logic: the tolerance cap math, the before↔after wire Closed/Persisting/New classification, and the
    /// fusion veto. The whole <see cref="HealStage.SewV2"/> orchestration (which calls the native sew/validate)
    /// is exercised by the native-gated integration fixtures. See
    /// docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §F/§J.
    /// </summary>
    public class SewV2Tests
    {
        private const double PreBuildFloor = 0.01;

        private static OcctNakedWire Wire(double x0, double y0, double x1, double y1)
        {
            // A closed rectangular loop at z = 0 (edge owners unused by ClassifyLoops).
            return new OcctNakedWire(
                new List<Point3D> { new Point3D(x0, y0, 0), new Point3D(x1, y0, 0), new Point3D(x1, y1, 0), new Point3D(x0, y1, 0) },
                true,
                new List<int>());
        }

        /// <summary>A horizontal 2x2 m quad at z = <paramref name="z"/> spanning [0,2]x[0,2] (normal +Z).</summary>
        private static Face3D HorizontalPlate(double z)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, z), new Point3D(2, 0, z), new Point3D(2, 2, z), new Point3D(0, 2, z));
        }

        // ---- Tolerance cap math (design §F) ----

        [Fact]
        public void SewToleranceCap_NoNearParallelPair_UsesTheExpandTolerance()
        {
            // Arrange & Act - no minSep => the base tolerance (floored, clamped) is used.
            double cap = HealStage.SewToleranceCap(0.1, 0.5, null, hardClamp: 0.3, preBuildFloor: PreBuildFloor);

            // Assert
            Assert.Equal(0.1, cap, 6);
        }

        [Fact]
        public void SewToleranceCap_CloseDoubleWall_CapsAtHalfTheGap()
        {
            // Arrange & Act - an 0.08 m near-parallel pair caps the sew at 0.08 x 0.5 = 0.04 m.
            double cap = HealStage.SewToleranceCap(0.1, 0.5, 0.08, preBuildFloor: PreBuildFloor);

            // Assert - below the 0.08 gap, so a global sew cannot bridge (fuse) the double wall.
            Assert.Equal(0.04, cap, 6);
        }

        [Fact]
        public void SewToleranceCap_WideSeparation_DoesNotReduceBelowExpandTolerance()
        {
            // Arrange & Act - a 0.30 m gap caps at 0.15 m, but the expand tolerance (0.1) is tighter and wins.
            double cap = HealStage.SewToleranceCap(0.1, 0.5, 0.30, preBuildFloor: PreBuildFloor);

            // Assert
            Assert.Equal(0.1, cap, 6);
        }

        [Fact]
        public void SewToleranceCap_LargeExpandTolerance_IsHardClamped()
        {
            // Arrange & Act - the hard clamp bounds an over-large expand tolerance independent of the gap.
            double cap = HealStage.SewToleranceCap(0.5, 0.5, null, hardClamp: 0.3, preBuildFloor: PreBuildFloor);

            // Assert
            Assert.Equal(0.3, cap, 6);
        }

        [Fact]
        public void SewToleranceCap_GapTooSmall_FallsToOrBelowThePreBuildFloor_SoTheSewIsSkipped()
        {
            // Arrange & Act - an 0.02 m gap caps at 0.01 m = the pre-build floor: the sew has nothing safe to
            // gain and would only risk fusing the close pair, so SewV2 treats cap <= floor as "skip".
            double cap = HealStage.SewToleranceCap(0.1, 0.5, 0.02, preBuildFloor: PreBuildFloor);

            // Assert
            Assert.True(cap <= PreBuildFloor, $"expected the cap ({cap}) to fall to/below the floor ({PreBuildFloor})");
        }

        // ---- Wire bookkeeping (Closed / Persisting / New) ----

        [Fact]
        public void ClassifyLoops_MixedOutcomes_CountsClosedPersistingAndNew()
        {
            // Arrange - before {A, B}; after {A' (=A), C}. A persists, B closed (no after near it), C is new.
            List<OcctNakedWire> before = new List<OcctNakedWire> { Wire(0, 0, 1, 1), Wire(10, 10, 11, 11) };
            List<OcctNakedWire> after = new List<OcctNakedWire> { Wire(0, 0, 1, 1), Wire(20, 20, 21, 21) };

            // Act
            HealStage.LoopBookkeeping loops = HealStage.ClassifyLoops(before, after, sewTolerance: 0.04);

            // Assert
            Assert.Equal(1, loops.Persisting);
            Assert.Equal(1, loops.Closed);
            Assert.Equal(1, loops.New);
            Assert.Single(loops.NewLoops);
        }

        [Fact]
        public void ClassifyLoops_AllBeforeWiresRemoved_AreAllClosed()
        {
            // Arrange - the sew closed every naked loop (no after wires).
            List<OcctNakedWire> before = new List<OcctNakedWire> { Wire(0, 0, 1, 1), Wire(5, 5, 6, 6) };
            List<OcctNakedWire> after = new List<OcctNakedWire>();

            // Act
            HealStage.LoopBookkeeping loops = HealStage.ClassifyLoops(before, after, sewTolerance: 0.04);

            // Assert
            Assert.Equal(2, loops.Closed);
            Assert.Equal(0, loops.Persisting);
            Assert.Equal(0, loops.New);
        }

        [Fact]
        public void ClassifyLoops_OnlyNewAfterWires_AreAllNew()
        {
            // Arrange - the sew introduced loops that had no before counterpart.
            List<OcctNakedWire> before = new List<OcctNakedWire>();
            List<OcctNakedWire> after = new List<OcctNakedWire> { Wire(0, 0, 1, 1) };

            // Act
            HealStage.LoopBookkeeping loops = HealStage.ClassifyLoops(before, after, sewTolerance: 0.04);

            // Assert
            Assert.Equal(1, loops.New);
            Assert.Equal(0, loops.Closed);
            Assert.Equal(0, loops.Persisting);
        }

        // ---- Fusion veto (design §F) ----

        [Fact]
        public void DetectFusion_ParallelPairCollapsedToOneFace_IsVetoed()
        {
            // Arrange - two parallel skins 0.08 m apart; the "sewn" result kept only the z = 0 skin (the two
            // fused into one), so the z = 0.08 skin is no longer represented.
            List<Face3D> preSew = new List<Face3D> { HorizontalPlate(0.0), HorizontalPlate(0.08) };
            List<Face3D> sewn = new List<Face3D> { HorizontalPlate(0.0) };

            // Act
            bool fused = HealStage.DetectFusion(preSew, sewn, sewTolerance: 0.05, out MinPairSeparation.Pair pair);

            // Assert - the fusion veto fires and names the collapsed pair.
            Assert.True(fused);
            Assert.Equal(0.08, pair.Separation, 6);
        }

        [Fact]
        public void DetectFusion_ParallelPairBothSurvive_IsNotVetoed()
        {
            // Arrange - both skins survive the sew (a legitimate stitch that did not fuse the double wall).
            List<Face3D> preSew = new List<Face3D> { HorizontalPlate(0.0), HorizontalPlate(0.08) };
            List<Face3D> sewn = new List<Face3D> { HorizontalPlate(0.0), HorizontalPlate(0.08) };

            // Act
            bool fused = HealStage.DetectFusion(preSew, sewn, sewTolerance: 0.05, out _);

            // Assert
            Assert.False(fused);
        }

        [Fact]
        public void DetectFusion_NoNearParallelPairWithinWindow_IsNotVetoed()
        {
            // Arrange - the only parallel pair is 0.30 m apart, outside the 2 x 0.05 = 0.10 m veto window.
            List<Face3D> preSew = new List<Face3D> { HorizontalPlate(0.0), HorizontalPlate(0.30) };
            List<Face3D> sewn = new List<Face3D> { HorizontalPlate(0.0) };

            // Act - no pair is close enough to be a fusion candidate, so nothing is vetoed.
            bool fused = HealStage.DetectFusion(preSew, sewn, sewTolerance: 0.05, out _);

            // Assert
            Assert.False(fused);
        }
    }
}
