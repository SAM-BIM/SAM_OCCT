// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="SnappedPanel"/> and <see cref="Panel3DSnapSolver"/>.
    /// All tests are pure-managed: no native OCCT DLL required.
    /// </summary>
    public class Panel3DSnapSolverTests
    {
        // ──────────────────────────────────────────────────────────────
        // SnappedPanel: coplanar predicate
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void IsCoplanarWith_SamePlane_ReturnsTrue()
        {
            SnappedPanel a = MakeWallPanel(0, weight: 2);
            SnappedPanel b = MakeWallPanel(0, weight: 1);

            bool result = a.IsCoplanarWith(b, angleTolerance: 0.1, distanceTolerance: 0.01);

            Assert.True(result);
        }

        [Fact]
        public void IsCoplanarWith_ParallelButFarOffset_ReturnsFalse()
        {
            SnappedPanel a = MakeWallPanel(0, weight: 2);
            SnappedPanel b = MakeWallPanel(1.0, weight: 1); // 1 m offset — well beyond tolerance

            bool result = a.IsCoplanarWith(b, angleTolerance: 0.1, distanceTolerance: 0.01);

            Assert.False(result);
        }

        [Fact]
        public void IsCoplanarWith_PerpendicularPlanes_ReturnsFalse()
        {
            SnappedPanel wall = MakeWallPanel(0, weight: 2);
            SnappedPanel floor = MakeFloorPanel(0, weight: 1);

            bool result = wall.IsCoplanarWith(floor, angleTolerance: 0.1, distanceTolerance: 0.01);

            Assert.False(result);
        }

        // ──────────────────────────────────────────────────────────────
        // SnappedPanel: BucketSize slab containment
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void BucketContains_CandidateInsideSlab_ReturnsTrue()
        {
            // Backer at y = 0, bucket = 0.3; candidate at y = 0.2 (inside slab)
            SnappedPanel backer = MakeWallPanel(0, weight: 2, bucketSize: 0.3);
            SnappedPanel candidate = MakeWallPanel(0.2, weight: 1, bucketSize: 0.1);

            bool inside = backer.BucketContains(candidate, out _);

            Assert.True(inside);
        }

        [Fact]
        public void BucketContains_CandidateOutsideSlab_ReturnsFalse()
        {
            // Backer at y = 0, bucket = 0.1; candidate at y = 0.5 (outside slab)
            SnappedPanel backer = MakeWallPanel(0, weight: 2, bucketSize: 0.1);
            SnappedPanel candidate = MakeWallPanel(0.5, weight: 1, bucketSize: 0.1);

            bool inside = backer.BucketContains(candidate, out _);

            Assert.False(inside);
        }

        [Fact]
        public void BucketContains_CandidateFullyInsideSlab_SetsFully()
        {
            SnappedPanel backer = MakeWallPanel(0, weight: 2, bucketSize: 0.3);
            SnappedPanel candidate = MakeWallPanel(0.05, weight: 1, bucketSize: 0.1);

            backer.BucketContains(candidate, out bool fully);

            Assert.True(fully);
        }

        // ──────────────────────────────────────────────────────────────
        // SnappedPanel: backer-plane projection
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void SnapToBacker_LowerWeightPanel_ProjectsOntoBacker()
        {
            // Backer at y = 0; candidate at y = 0.15 — within bucket = 0.3
            SnappedPanel backer = MakeWallPanel(0, weight: 2, bucketSize: 0.3);
            SnappedPanel candidate = MakeWallPanel(0.15, weight: 1, bucketSize: 0.1);

            bool snapped = candidate.SnapToBacker(backer.Plane);

            Assert.True(snapped);
            Assert.True(candidate.Snapped);

            // All boundary points should now be on (or very close to) y = 0
            List<Point3D> pts = BoundaryPoints(candidate.Face3D);
            foreach (Point3D pt in pts)
            {
                Assert.True(System.Math.Abs(backer.Plane.Distance(pt)) < 1e-6,
                    $"Point {pt} is not on the backer plane after snap");
            }
        }

        // ──────────────────────────────────────────────────────────────
        // Panel3DSnapSolver.AdjustListLength
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void AdjustListLength_NullInput_PadsWithDefault()
        {
            List<double> result = Panel3DSnapSolver.AdjustListLength(null, 3, 0.5);

            Assert.Equal(3, result.Count);
            Assert.All(result, x => Assert.Equal(0.5, x));
        }

        [Fact]
        public void AdjustListLength_ShortInput_PadsWithDefault()
        {
            List<double> result = Panel3DSnapSolver.AdjustListLength(new List<double> { 1.0 }, 3, 0.5);

            Assert.Equal(3, result.Count);
            Assert.Equal(1.0, result[0]);
            Assert.Equal(0.5, result[1]);
            Assert.Equal(0.5, result[2]);
        }

        [Fact]
        public void AdjustListLength_ExactLength_ReturnsSameValues()
        {
            List<double> input = new List<double> { 1.0, 2.0, 3.0 };
            List<double> result = Panel3DSnapSolver.AdjustListLength(input, 3, 0.0);

            Assert.Equal(input, result);
        }

        [Fact]
        public void AdjustListLength_LongerInput_TruncatesToTarget()
        {
            List<double> input = new List<double> { 1.0, 2.0, 3.0, 4.0, 5.0 };
            List<double> result = Panel3DSnapSolver.AdjustListLength(input, 3, 0.0);

            Assert.Equal(3, result.Count);
        }

        // ──────────────────────────────────────────────────────────────
        // Panel3DSnapSolver.Snap (managed stage)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void Snap_LowerWeightInsideBucket_ProjectedOntoBacker()
        {
            // Two parallel walls 0.15 m apart; backer at y=0 (weight=2), candidate at y=0.15 (weight=1)
            SnappedPanel backer = MakeWallPanel(0, weight: 2, bucketSize: 0.3);
            SnappedPanel candidate = MakeWallPanel(0.15, weight: 1, bucketSize: 0.1);
            List<SnappedPanel> panels = new List<SnappedPanel> { backer, candidate };

            Panel3DSnapSolver.Snap(panels, toleranceAngle: 0.1, toleranceArcAngle: 0.01);

            Assert.True(candidate.Snapped, "Candidate should have snapped to backer");
            List<Point3D> pts = BoundaryPoints(candidate.Face3D);
            foreach (Point3D pt in pts)
            {
                Assert.True(System.Math.Abs(backer.Plane.Distance(pt)) < 1e-6,
                    $"Snapped panel boundary point {pt} not on backer plane");
            }
        }

        [Fact]
        public void Snap_HigherWeightOutsideBucket_NotSnapped()
        {
            // Two parallel walls 0.6 m apart — outside the backer's bucket of 0.3
            SnappedPanel backer = MakeWallPanel(0, weight: 2, bucketSize: 0.3);
            SnappedPanel candidate = MakeWallPanel(0.6, weight: 1, bucketSize: 0.1);
            List<SnappedPanel> panels = new List<SnappedPanel> { backer, candidate };

            Panel3DSnapSolver.Snap(panels, toleranceAngle: 0.1, toleranceArcAngle: 0.01);

            Assert.False(candidate.Snapped, "Candidate should NOT have snapped: it is outside the bucket");
        }

        [Fact]
        public void Snap_EqualWeightPanels_NeitherSnapped()
        {
            // Same weight — neither dominates
            SnappedPanel a = MakeWallPanel(0, weight: 1, bucketSize: 0.3);
            SnappedPanel b = MakeWallPanel(0.15, weight: 1, bucketSize: 0.3);
            List<SnappedPanel> panels = new List<SnappedPanel> { a, b };

            Panel3DSnapSolver.Snap(panels, toleranceAngle: 0.1, toleranceArcAngle: 0.01);

            Assert.False(a.Snapped);
            Assert.False(b.Snapped);
        }

        [Fact]
        public void Snap_PerpendicularPanels_NotSnapped()
        {
            // Backer is a Y-normal wall; candidate is a Z-normal floor — they are not parallel
            SnappedPanel backer = MakeWallPanel(0, weight: 2, bucketSize: 0.5);
            SnappedPanel floor = MakeFloorPanel(0.1, weight: 1, bucketSize: 0.1);
            List<SnappedPanel> panels = new List<SnappedPanel> { backer, floor };

            Panel3DSnapSolver.Snap(panels, toleranceAngle: 0.1, toleranceArcAngle: 0.01);

            Assert.False(floor.Snapped, "Floor should not snap to a wall backer");
        }

        // ──────────────────────────────────────────────────────────────
        // Panel3DSnapSolver.Execute (managed-only path — no native DLL)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void Execute_NullInput_ReturnsEmpty()
        {
            Panel3DSnapSolver solver = new Panel3DSnapSolver(null);
            solver.Execute();

            Assert.Empty(solver.SnappedPanels);
            Assert.Empty(solver.ResolvedFace3Ds);
        }

        [Fact]
        public void Execute_EmptyInput_ReturnsEmpty()
        {
            Panel3DSnapSolver solver = new Panel3DSnapSolver(new List<Face3D>());
            solver.Execute();

            Assert.Empty(solver.SnappedPanels);
        }

        [Fact]
        public void Execute_TwoParallelWalls_SnappedPanelsRegistered()
        {
            Face3D wall0 = MakeWallFace(yOffset: 0);
            Face3D wall1 = MakeWallFace(yOffset: 0.15);

            List<double> weights = new List<double> { 2.0, 1.0 };
            List<double> buckets = new List<double> { 0.3, 0.1 };

            Panel3DSnapSolver solver = new Panel3DSnapSolver(
                new List<Face3D> { wall0, wall1 },
                buckets,
                weights);

            solver.Execute();

            Assert.Equal(2, solver.SnappedPanels.Count);
            // The lower-weight panel (index 1) should have been snapped
            Assert.True(solver.SnappedPanels[1].Snapped);
        }

        [Fact]
        public void Execute_SinglePanel_NoSnapAndResolvedContainsFace()
        {
            Face3D wall = MakeWallFace(yOffset: 0);
            Panel3DSnapSolver solver = new Panel3DSnapSolver(new List<Face3D> { wall });
            solver.Execute();

            Assert.Single(solver.SnappedPanels);
            Assert.Single(solver.ResolvedFace3Ds);
        }

        // ──────────────────────────────────────────────────────────────
        // SnappedPanel: vertical predicate + top extension
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void IsVertical_Wall_ReturnsTrue()
        {
            SnappedPanel wall = MakeWallPanel(0);
            Assert.True(wall.IsVertical(20 * (System.Math.PI / 180)));
        }

        [Fact]
        public void IsVertical_Floor_ReturnsFalse()
        {
            SnappedPanel floor = MakeFloorPanel(0);
            Assert.False(floor.IsVertical(20 * (System.Math.PI / 180)));
        }

        [Fact]
        public void ExtendTopTo_TallerTarget_RaisesTopAndReturnsTrue()
        {
            SnappedPanel wall = MakeWallPanel(0); // top at z = 3
            bool extended = wall.ExtendTopTo(5.0, 1e-6);

            Assert.True(extended);
            Assert.Equal(5.0, wall.GetBoundingBox().Max.Z, 3);
            Assert.Equal(0.0, wall.GetBoundingBox().Min.Z, 3); // base preserved
        }

        [Fact]
        public void ExtendTopTo_TargetBelowCurrentTop_NoChangeAndReturnsFalse()
        {
            SnappedPanel wall = MakeWallPanel(0); // top at z = 3
            bool extended = wall.ExtendTopTo(2.0, 1e-6);

            Assert.False(extended);
            Assert.Equal(3.0, wall.GetBoundingBox().Max.Z, 3);
        }

        [Fact]
        public void Extend_WallUnderFloorCap_ExtendsUpToCap()
        {
            // Wall z 0..3; floor cap at z = 4 directly above it.
            SnappedPanel wall = MakeWallPanel(0);
            SnappedPanel cap = MakeFloorPanel(4.0);

            List<SnappedPanel> panels = new List<SnappedPanel> { wall, cap };
            Panel3DSnapSolver.Extend(panels, 20 * (System.Math.PI / 180), overshoot: 0.05, toleranceDistance: 1e-6);

            // Wall top should now reach just past the cap; cap is unchanged.
            Assert.True(wall.GetBoundingBox().Max.Z > 3.5, "Wall should have been extended toward the cap");
            Assert.Equal(4.0, cap.GetBoundingBox().Max.Z, 3);
        }

        [Fact]
        public void Extend_WallWithNoCapAbove_LeftUntouched()
        {
            // Two parallel walls, no horizontal cap above either.
            SnappedPanel wallA = MakeWallPanel(0);
            SnappedPanel wallB = MakeWallPanel(2.0);

            List<SnappedPanel> panels = new List<SnappedPanel> { wallA, wallB };
            Panel3DSnapSolver.Extend(panels, 20 * (System.Math.PI / 180), overshoot: 0.05, toleranceDistance: 1e-6);

            Assert.Equal(3.0, wallA.GetBoundingBox().Max.Z, 3);
            Assert.Equal(3.0, wallB.GetBoundingBox().Max.Z, 3);
        }

        // ──────────────────────────────────────────────────────────────
        // helpers
        // ──────────────────────────────────────────────────────────────

        /// <summary>Unit quad in the XZ plane at the given Y offset (Y-normal wall).</summary>
        private static Face3D MakeWallFace(double yOffset)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(0, yOffset, 0),
                new Point3D(1, yOffset, 0),
                new Point3D(1, yOffset, 3),
                new Point3D(0, yOffset, 3));
        }

        /// <summary>Unit quad in the XY plane at the given Z offset (Z-normal floor).</summary>
        private static Face3D MakeFloorFace(double zOffset)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, zOffset),
                new Point3D(1, 0, zOffset),
                new Point3D(1, 1, zOffset),
                new Point3D(0, 1, zOffset));
        }

        private static SnappedPanel MakeWallPanel(double yOffset, double weight = 1, double bucketSize = 0.3)
        {
            return new SnappedPanel(0, MakeWallFace(yOffset), weight, bucketSize, 0.5);
        }

        private static SnappedPanel MakeFloorPanel(double zOffset, double weight = 1, double bucketSize = 0.3)
        {
            return new SnappedPanel(0, MakeFloorFace(zOffset), weight, bucketSize, 0.5);
        }

        private static List<Point3D> BoundaryPoints(Face3D face3D)
        {
            return (face3D?.GetExternalEdge3D() as ISegmentable3D)?.GetPoints() ?? new List<Point3D>();
        }
    }
}
