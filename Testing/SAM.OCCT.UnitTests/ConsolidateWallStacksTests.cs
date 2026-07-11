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
    /// Unit tests for <see cref="Panel3DSnapSolver.ConsolidateWallStacks"/> and
    /// <see cref="SnappedPanel.InPlaneOverlapRatioVsSmaller"/> - the explicit double-wall
    /// consolidation pass (opt-in via <c>doubleWallGap</c>). Pure-managed: no native OCCT required.
    /// </summary>
    public class ConsolidateWallStacksTests
    {
        private const double Angle = 5 * System.Math.PI / 180;
        private const double Distance = 0.001;
        private const double VerticalAngle = 20 * System.Math.PI / 180;

        // ──────────────────────────────────────────────────────────────
        // InPlaneOverlapRatioVsSmaller
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void InPlaneOverlapRatioVsSmaller_SmallWallInsideLargeFootprint_NearOne()
        {
            // Arrange: a 2 m wall segment facing a 10 m wall - the sub-segment case (towers cell22|26).
            SnappedPanel large = Wall(0, x0: 0, x1: 10);
            SnappedPanel small = Wall(0.345, x0: 3, x1: 5);

            // Act
            double vsSmaller = large.InPlaneOverlapRatioVsSmaller(small);
            double vsLarger = large.InPlaneOverlapRatio(small);

            // Assert: the smaller footprint is fully covered, while the larger-denominator ratio is low -
            // exactly the split that made the bucket-snap ratio gate reject the pair.
            Assert.True(vsSmaller > 0.99, $"vsSmaller={vsSmaller}");
            Assert.True(vsLarger < 0.5, $"vsLarger={vsLarger}");
        }

        [Fact]
        public void InPlaneOverlapRatioVsSmaller_CornerTouchOnly_Low()
        {
            // Arrange: two walls sharing only a sliver of footprint (the mis-pair class).
            SnappedPanel a = Wall(0, x0: 0, x1: 5);
            SnappedPanel b = Wall(0.2, x0: 4.9, x1: 10);

            // Act
            double vsSmaller = a.InPlaneOverlapRatioVsSmaller(b);

            // Assert
            Assert.True(vsSmaller < Panel3DSnapSolver.STACK_CONSOLIDATION_MIN_OVERLAP_RATIO, $"vsSmaller={vsSmaller}");
        }

        // ──────────────────────────────────────────────────────────────
        // ConsolidateWallStacks
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void ConsolidateWallStacks_FourWallStack_AllOnDominantPlane()
        {
            // Arrange: the towers wall-stack shape - four parallel walls chained at 0.198/0.111/0.222 m
            // (each neighbouring gap within 0.4, ends 0.531 apart). The second wall is slightly longer so
            // dominance is deterministic.
            SnappedPanel w1 = Wall(0.000, x0: 0, x1: 6);
            SnappedPanel w2 = Wall(0.198, x0: 0, x1: 6.2); // dominant (largest area)
            SnappedPanel w3 = Wall(0.309, x0: 0, x1: 6);
            SnappedPanel w4 = Wall(0.531, x0: 0, x1: 6);
            List<SnappedPanel> panels = new List<SnappedPanel> { w1, w2, w3, w4 };

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(panels, 0.4, Angle, Distance, VerticalAngle);

            // Assert: one plane for the whole chain (every member within 0.4 of the dominant), no residue.
            Assert.Equal(3, moved);
            foreach (SnappedPanel panel in panels)
            {
                Assert.True(System.Math.Abs(w2.Plane.Distance(Centroid(panel))) < 0.01,
                    $"wall at {Centroid(panel).Y} not on dominant plane");
            }
        }

        [Fact]
        public void ConsolidateWallStacks_GapZero_NoChange()
        {
            // Arrange
            SnappedPanel a = Wall(0);
            SnappedPanel b = Wall(0.2);

            // Act: default off - the pass must be inert.
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(new List<SnappedPanel> { a, b }, 0.0, Angle, Distance, VerticalAngle);

            // Assert
            Assert.Equal(0, moved);
            Assert.Equal(0.2, b.PerpendicularSeparation(a), 3);
        }

        [Fact]
        public void ConsolidateWallStacks_PairBeyondGap_NotMerged()
        {
            // Arrange: separation wider than the declared gap - a real corridor/void, left alone.
            SnappedPanel a = Wall(0);
            SnappedPanel b = Wall(0.5);

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(new List<SnappedPanel> { a, b }, 0.4, Angle, Distance, VerticalAngle);

            // Assert
            Assert.Equal(0, moved);
        }

        [Fact]
        public void ConsolidateWallStacks_LowOverlapRatio_NotMerged()
        {
            // Arrange: within the gap but sharing almost no footprint - distinct walls of neighbouring bays.
            SnappedPanel a = Wall(0, x0: 0, x1: 5);
            SnappedPanel b = Wall(0.2, x0: 4.9, x1: 10);

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(new List<SnappedPanel> { a, b }, 0.4, Angle, Distance, VerticalAngle);

            // Assert
            Assert.Equal(0, moved);
        }

        [Fact]
        public void ConsolidateWallStacks_ChainMemberBeyondCap_LeftPut()
        {
            // Arrange: a transitive chain 0 - 0.35 - 0.7 with the END wall dominant. The middle wall is
            // within the 0.4 travel cap; the far wall would need to travel 0.7 and must stay put.
            SnappedPanel dominant = Wall(0.0, x0: 0, x1: 8); // largest area
            SnappedPanel middle = Wall(0.35, x0: 0, x1: 6);
            SnappedPanel far = Wall(0.70, x0: 0, x1: 6);
            List<SnappedPanel> panels = new List<SnappedPanel> { dominant, middle, far };

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(panels, 0.4, Angle, Distance, VerticalAngle);

            // Assert: displacement is capped per member - no runaway chain drift.
            Assert.Equal(1, moved);
            Assert.True(System.Math.Abs(dominant.Plane.Distance(Centroid(middle))) < 0.01, "middle should be on the dominant plane");
            Assert.Equal(0.70, System.Math.Abs(dominant.Plane.Distance(Centroid(far))), 2);
        }

        [Fact]
        public void ConsolidateWallStacks_AntiParallelPairWiderThanVoidGuard_Merges()
        {
            // Arrange: the towers cell22|26 shape - an anti-parallel overlapping pair 0.345 m apart, which
            // the weighted snap's void guard (0.3 m) hard-blocks at ANY bucket size. The explicit gap is
            // the user's declaration that this slot is a modeling artifact.
            SnappedPanel towerFace = Wall(0, x0: -10, x1: 17, flip: true); // large
            SnappedPanel blockWall = Wall(0.345, x0: 0, x1: 7);

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(new List<SnappedPanel> { towerFace, blockWall }, 0.4, Angle, Distance, VerticalAngle);

            // Assert: the smaller wall lands on the larger (dominant) face's plane.
            Assert.Equal(1, moved);
            Assert.True(System.Math.Abs(towerFace.Plane.Distance(Centroid(blockWall))) < 0.01);
        }

        [Fact]
        public void ConsolidateWallStacks_Caps_NotTouched()
        {
            // Arrange: two stacked floor slabs 0.2 m apart - cap normalization's territory, never this pass.
            SnappedPanel f1 = Floor(0);
            SnappedPanel f2 = Floor(0.2);

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(new List<SnappedPanel> { f1, f2 }, 0.4, Angle, Distance, VerticalAngle);

            // Assert
            Assert.Equal(0, moved);
        }

        [Fact]
        public void ConsolidateWallStacks_Records_OneStackConsolidatedPerMove()
        {
            // Arrange
            SnappedPanel a = Wall(0, x0: 0, x1: 6.2);
            SnappedPanel b = Wall(0.2, x0: 0, x1: 6);
            List<CleanRecord> records = new List<CleanRecord>();

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(new List<SnappedPanel> { a, b }, 0.4, Angle, Distance, VerticalAngle, records: records);

            // Assert: recorded at the mutation site, kind + distance faithful.
            Assert.Equal(1, moved);
            CleanRecord record = Assert.Single(records);
            Assert.Equal(CleanRecordKind.StackConsolidated, record.Kind);
            Assert.Equal(0.2, record.DistanceMoved, 2);
            Assert.Equal("stack-consolidated", record.KindText());
        }

        [Fact]
        public void ConsolidateWallStacks_NullRecorder_GeometryIdenticalToRecordedRun()
        {
            // Arrange: two identical stacks, one consolidated with a recorder and one without.
            List<SnappedPanel> recorded = new List<SnappedPanel> { Wall(0, x0: 0, x1: 6.2), Wall(0.2), Wall(0.35) };
            List<SnappedPanel> silent = new List<SnappedPanel> { Wall(0, x0: 0, x1: 6.2), Wall(0.2), Wall(0.35) };

            // Act
            Panel3DSnapSolver.ConsolidateWallStacks(recorded, 0.4, Angle, Distance, VerticalAngle, records: new List<CleanRecord>());
            Panel3DSnapSolver.ConsolidateWallStacks(silent, 0.4, Angle, Distance, VerticalAngle, records: null);

            // Assert: the recorder is a pure side effect.
            for (int i = 0; i < recorded.Count; i++)
            {
                Assert.Equal(Centroid(recorded[i]).Y, Centroid(silent[i]).Y, 6);
            }
        }

        [Fact]
        public void ConsolidateWallStacks_MovedWall_DragsAbuttingPerpendicularEnd()
        {
            // Arrange: B (y=0.345) consolidates onto dominant A (y=0). Perpendicular wall C's foot END sits
            // on B's plane - a T junction that the move would otherwise leave hanging by 0.345 m (and the
            // Z-ignorant plan-loop extend cannot be relied on to re-close; see whole-level-towers).
            SnappedPanel a = Wall(0.000, x0: 0, x1: 8);
            SnappedPanel b = Wall(0.345, x0: 0, x1: 7, flip: true);
            SnappedPanel c = WallNS(2.0, y0: 0.345, y1: 5.0);

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(new List<SnappedPanel> { a, b, c }, 0.4, Angle, Distance, VerticalAngle);

            // Assert: B on A's plane, and C's end followed it there.
            Assert.Equal(1, moved);
            Assert.Equal(0.0, c.GetBoundingBox().Min.Y, 3);
        }

        [Fact]
        public void ConsolidateWallStacks_PerpendicularEndOnAnotherStorey_NotDragged()
        {
            // Arrange: same T junction shape, but C belongs to the storey ABOVE the moved wall.
            SnappedPanel a = Wall(0.000, x0: 0, x1: 8);
            SnappedPanel b = Wall(0.345, x0: 0, x1: 7, flip: true);
            SnappedPanel c = WallNS(2.0, y0: 0.345, y1: 5.0, z0: 4.0, z1: 7.0);

            // Act
            Panel3DSnapSolver.ConsolidateWallStacks(new List<SnappedPanel> { a, b, c }, 0.4, Angle, Distance, VerticalAngle);

            // Assert: C keeps its end - another storey's junction is not this move's business.
            Assert.Equal(0.345, c.GetBoundingBox().Min.Y, 3);
        }

        // ──────────────────────────────────────────────────────────────
        // SnapStage.Clean integration (managed)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void Clean_DefaultGap_AntiParallelStackBeyondVoidGuard_KeepsResidualPlanes()
        {
            // Arrange: three anti-parallel-alternating walls 0.35 m apart - beyond the 0.3 m void guard, so
            // the default clean must NOT merge them (they read as real voids).
            List<SnappedPanel> panels = Stack035();

            // Act
            SnapStage.Result result = SnapStage.Clean(panels, new ToleranceBudget(), 0.0, 0.0);

            // Assert: three distinct wall planes survive.
            Assert.Equal(3, result.CleanFace3Ds.Count);
        }

        [Fact]
        public void Clean_DoubleWallGap_AntiParallelStackBeyondVoidGuard_OnePlane()
        {
            // Arrange: the same stack, with the user's explicit doubleWallGap = 0.4.
            List<SnappedPanel> panels = Stack035();

            // Act
            SnapStage.Result result = SnapStage.Clean(panels, new ToleranceBudget(), 0.0, 0.0, doubleWallGap: 0.4);

            // Assert: the stack consolidates and the coplanar union leaves ONE wall face.
            Assert.Single(result.CleanFace3Ds);
        }

        /// <summary>Three walls at y = 0 / 0.35 / 0.7 with alternating winding (anti-parallel normals) and
        /// near-identical footprints - each neighbouring pair is void-guard-blocked in the weighted snap
        /// (0.35 &gt; 0.3) yet within an explicit doubleWallGap of 0.4. The MIDDLE wall is dominant
        /// (largest), so both ends travel 0.35 - within the per-member cap - and the stack can reach one
        /// plane.</summary>
        private static List<SnappedPanel> Stack035()
        {
            return new List<SnappedPanel>
            {
                Wall(0.00, x0: 0, x1: 6),
                Wall(0.35, x0: 0, x1: 6.2, flip: true),
                Wall(0.70, x0: 0, x1: 6),
            };
        }

        // ──────────────────────────────────────────────────────────────
        // helpers
        // ──────────────────────────────────────────────────────────────

        /// <summary>Vertical wall in the XZ plane at the given Y offset (normal ±Y via <paramref name="flip"/>).</summary>
        private static SnappedPanel Wall(double yOffset, double x0 = 0, double x1 = 6, double z0 = 0, double z1 = 3, bool flip = false)
        {
            Face3D face3D = flip
                ? TestGeometry.CreatePlanarFace(
                    new Point3D(x0, yOffset, z0),
                    new Point3D(x0, yOffset, z1),
                    new Point3D(x1, yOffset, z1),
                    new Point3D(x1, yOffset, z0))
                : TestGeometry.CreatePlanarFace(
                    new Point3D(x0, yOffset, z0),
                    new Point3D(x1, yOffset, z0),
                    new Point3D(x1, yOffset, z1),
                    new Point3D(x0, yOffset, z1));
            return new SnappedPanel(0, face3D, 1.0, 0.4, 0.5);
        }

        /// <summary>Vertical wall in the YZ plane at the given X offset (normal ±X) - perpendicular to <see cref="Wall"/>.</summary>
        private static SnappedPanel WallNS(double xOffset, double y0, double y1, double z0 = 0, double z1 = 3)
        {
            Face3D face3D = TestGeometry.CreatePlanarFace(
                new Point3D(xOffset, y0, z0),
                new Point3D(xOffset, y1, z0),
                new Point3D(xOffset, y1, z1),
                new Point3D(xOffset, y0, z1));
            return new SnappedPanel(0, face3D, 1.0, 0.4, 0.5);
        }

        /// <summary>Horizontal floor slab in the XY plane at the given Z offset.</summary>
        private static SnappedPanel Floor(double zOffset)
        {
            Face3D face3D = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, zOffset),
                new Point3D(6, 0, zOffset),
                new Point3D(6, 6, zOffset),
                new Point3D(0, 6, zOffset));
            return new SnappedPanel(0, face3D, 1.0, 0.4, 0.5);
        }

        private static Point3D Centroid(SnappedPanel panel)
        {
            return panel.GetBoundingBox().GetCentroid();
        }
    }
}
