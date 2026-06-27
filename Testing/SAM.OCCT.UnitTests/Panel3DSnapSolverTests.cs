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
        public void Snap_EqualWeightPanelsWithinBucket_LaterAbsorbedByEarlier()
        {
            // Equal weight, within-bucket, near-parallel: the later panel is absorbed onto the earlier
            // backer (deterministic by the stable descending-weight sort) so coincident/offset
            // "double-wall" pairs of the same weight collapse onto one plane instead of staying apart.
            SnappedPanel a = MakeWallPanel(0, weight: 1, bucketSize: 0.3);
            SnappedPanel b = MakeWallPanel(0.15, weight: 1, bucketSize: 0.3);
            List<SnappedPanel> panels = new List<SnappedPanel> { a, b };

            Panel3DSnapSolver.Snap(panels, toleranceAngle: 0.1, toleranceArcAngle: 0.01);

            Assert.False(a.Snapped, "The first equal-weight panel is the backer and stays put");
            Assert.True(b.Snapped, "The second equal-weight panel within the bucket snaps onto the backer");
        }

        [Fact]
        public void Snap_ParallelWallsWithinBucketButNoInPlaneOverlap_NotSnapped()
        {
            // Two parallel walls 0.2 m apart (well within the 0.4 m bucket) and near-parallel, but lying
            // over DIFFERENT parts of the plane: backer runs x[0..1], candidate runs x[2..3]. They are
            // distinct walls of adjacent bays, not a double-wall - so the lower-weight candidate must
            // keep its original position rather than being dragged onto the backer. Regression for the
            // Clean3D "moving walls that need no move" defect.
            SnappedPanel backer = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(1, 0, 3), new Point3D(0, 0, 3)), 2, 0.4, 0.5);
            SnappedPanel candidate = new SnappedPanel(1, TestGeometry.CreatePlanarFace(
                new Point3D(2, 0.2, 0), new Point3D(3, 0.2, 0), new Point3D(3, 0.2, 3), new Point3D(2, 0.2, 3)), 1, 0.4, 0.5);
            List<SnappedPanel> panels = new List<SnappedPanel> { backer, candidate };

            Panel3DSnapSolver.Snap(panels, toleranceAngle: 0.1, toleranceArcAngle: 0.01, toleranceDistance: 1e-6);

            Assert.False(candidate.Snapped, "Candidate over a different part of the plane (no in-plane overlap) must stay put");
            foreach (Point3D pt in BoundaryPoints(candidate.Face3D))
            {
                Assert.Equal(0.2, pt.Y, 6); // unchanged: still on its own y=0.2 plane, not dragged onto the backer at y=0
            }
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
        public void Snap_TwoParallelWallsWithinBucket_LowerWeightSnapped()
        {
            // Two parallel walls 0.15 m apart, within the 0.3 m bucket; the lower-weight one snaps onto the backer.
            SnappedPanel backer = MakeWallPanel(0, weight: 2, bucketSize: 0.3);
            SnappedPanel candidate = MakeWallPanel(0.15, weight: 1, bucketSize: 0.3);

            Panel3DSnapSolver.Snap(
                new List<SnappedPanel> { backer, candidate },
                5 * (System.Math.PI / 180),
                0.3 * (System.Math.PI / 180));

            Assert.True(candidate.Snapped, "Lower-weight candidate within the bucket should snap onto the backer");
            Assert.False(backer.Snapped);
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
        public void Extend_WallUnderSlopedRoof_EaveBelowWallTop_ExtendsUpToRoof()
        {
            // Large-space tilted-roof regression (issue: one wall does not extend to the roof). A wall
            // (x 0..4, y=0, z 0..3) sits under a roof that slopes from an eave at z=2.5 - BELOW the wall top
            // (3) - up to a ridge at z=5. The roof's bounding-box Min.Z (2.5) is below the wall top, but the
            // roof surface directly above the wall is higher, so the wall must still extend up to the roof.
            // Gating on the cap's bounding-box Min.Z wrongly rejected the roof and left this wall short.
            SnappedPanel wall = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 0, 3), new Point3D(0, 0, 3)), 1, 0.3, 0.5);
            SnappedPanel roof = new SnappedPanel(1, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 2.5), new Point3D(4, 0, 5), new Point3D(4, 1, 5), new Point3D(0, 1, 2.5)), 1, 0.3, 0.5);

            List<SnappedPanel> panels = new List<SnappedPanel> { wall, roof };
            Panel3DSnapSolver.Extend(panels, 20 * (System.Math.PI / 180), overshoot: 0.05, toleranceDistance: 1e-6, roofOvershoot: 0.5, includeRoofs: true);

            // The wall must rise above its original flat top (z=3) toward the roof ridge (z=5); before the fix
            // it stayed at 3 because the roof's eave (2.5) sat below the wall top.
            Assert.True(wall.GetBoundingBox().Max.Z > 4.0,
                $"Wall should extend up to the sloped roof (got top z={wall.GetBoundingBox().Max.Z})");
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
        // SnappedPanel: lateral (in-plan) extension
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void ExtendHorizontal_DifferentReachPerEnd_WidensByBothReaches()
        {
            // Wall along X (x 0..2, z 0..3). Grow 0.5 m off one end and 1.0 m off the other.
            SnappedPanel wall = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 0, 3), new Point3D(0, 0, 3)), 1, 0.3, 0.5);

            bool extended = wall.ExtendHorizontal(startReach: 0.5, endReach: 1.0, tolerance: 1e-6);

            Assert.True(extended);
            BoundingBox3D box = wall.GetBoundingBox();
            // Orientation-independent: the plan length grows by 0.5 + 1.0 regardless of which end is "start".
            Assert.Equal(3.5, box.Max.X - box.Min.X, 3);
            Assert.Equal(0.0, box.Min.Z, 3); // height preserved
            Assert.Equal(3.0, box.Max.Z, 3);
        }

        [Fact]
        public void ExtendHorizontal_NoReach_ReturnsFalse()
        {
            SnappedPanel wall = MakeWallPanel(0);
            Assert.False(wall.ExtendHorizontal(0, 0, 1e-6));
        }

        // ──────────────────────────────────────────────────────────────
        // Panel3DSnapSolver.ExtendWalls (close the plan loop)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void ExtendWalls_WallShortOfPerpendicularWall_ExtendsEndToMeetIt()
        {
            // Wall A runs along X at y=0 (x 0..2). Wall B runs along Y at x=2.5 (y 0..2). A's +X end stops
            // 0.5 m short of B (within A's 0.98 m length-capped reach); its -X end has no wall to meet.
            Face3D a = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 0, 3), new Point3D(0, 0, 3));
            Face3D b = TestGeometry.CreatePlanarFace(
                new Point3D(2.5, 0, 0), new Point3D(2.5, 2, 0), new Point3D(2.5, 2, 3), new Point3D(2.5, 0, 3));
            SnappedPanel wallA = new SnappedPanel(0, a, 1, 0.3, maxExtension: 1.0);
            SnappedPanel wallB = new SnappedPanel(1, b, 1, 0.3, maxExtension: 1.0);

            Panel3DSnapSolver.ExtendWalls(
                new List<SnappedPanel> { wallA, wallB }, 20 * (System.Math.PI / 180), overshoot: 0.05, toleranceDistance: 1e-6);

            // A reaches B's plane (x=2.5, plus the overshoot); the other end and B are untouched.
            Assert.True(wallA.GetBoundingBox().Max.X >= 2.5, "Wall A should extend to meet wall B at x=2.5");
            Assert.Equal(0.0, wallA.GetBoundingBox().Min.X, 3); // the end with no wall stays put
            Assert.Equal(2.5, wallB.GetBoundingBox().Max.X, 3); // B untouched
        }

        [Fact]
        public void Execute_TiltedLevel_UpAxisClosesLoopLikeUpright()
        {
            // L of two perpendicular walls: A along X at y=0 (x 0..2), B along Y at x=2.5 (y 0..2). A's +X
            // end stops 0.5 m short of B. Upright, the extend closes that corner. Tilt the WHOLE thing 30
            // deg about X: now A's normal has |Nz|=0.5 (> sin20), so the world-Z classifier wrongly reads
            // it as a cap and the loop never closes - UNLESS the solver is told the level Up axis and runs
            // the extend in the level frame. Regression for tilted-level Extend3D.
            double theta = 30 * (System.Math.PI / 180);
            List<Face3D> upright = new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 0, 3), new Point3D(0, 0, 3)),
                TestGeometry.CreatePlanarFace(new Point3D(2.5, 0, 0), new Point3D(2.5, 2, 0), new Point3D(2.5, 2, 3), new Point3D(2.5, 0, 3)),
            };
            List<Face3D> tilted = upright.Select(f => TiltAboutX(f, theta)).ToList();
            Vector3D up = new Vector3D(0, -System.Math.Sin(theta), System.Math.Cos(theta)); // world Z tilted 30 deg about X

            // Total extended-face area is frame-invariant (rigid rotation preserves area), so it captures
            // whether wall A was actually extended to meet B.
            double uprightArea = RunExtendTotalArea(upright, null);
            double tiltedWithUp = RunExtendTotalArea(tilted, up);
            double tiltedNoUp = RunExtendTotalArea(tilted, null);

            // With the level Up, the tilted level extends exactly like the upright one (same area)...
            Assert.Equal(uprightArea, tiltedWithUp, 3);
            // ...and the Up axis is doing real work: without it the world-Z classifier mis-reads the tilted
            // wall as a cap (so Fill grows it instead of the wall extending to its neighbour), giving a
            // materially different result.
            Assert.True(System.Math.Abs(tiltedNoUp - tiltedWithUp) > 0.5,
                $"Without Up the tilted level should resolve differently (area {tiltedNoUp:0.00} vs {tiltedWithUp:0.00})");
        }

        private static double RunExtendTotalArea(List<Face3D> face3Ds, Vector3D up)
        {
            Panel3DSnapSolver solver = new Panel3DSnapSolver(
                face3Ds,
                Enumerable.Repeat(0.3, face3Ds.Count).ToList(),
                Enumerable.Repeat(1.0, face3Ds.Count).ToList(),
                Enumerable.Repeat(1.0, face3Ds.Count).ToList())
            {
                StopAfterExtend = true,
                Up = up
            };
            solver.Execute(null);
            return solver.ResolvedFace3Ds.Where(x => x != null).Sum(x => x.GetArea());
        }

        /// <summary>Rotates a face's vertices about the world X axis by <paramref name="theta"/> radians.</summary>
        private static Face3D TiltAboutX(Face3D face3D, double theta)
        {
            double c = System.Math.Cos(theta), s = System.Math.Sin(theta);
            List<Point3D> pts = ((ISegmentable3D)face3D.GetExternalEdge3D()).GetPoints();
            return TestGeometry.CreatePlanarFace(pts.Select(p => new Point3D(p.X, p.Y * c - p.Z * s, p.Y * s + p.Z * c)).ToArray());
        }

        [Fact]
        public void ExtendWalls_ShortStubCappedByLength_DoesNotOverReach()
        {
            // A 0.5 m stub with a generous MaxExtend (1.0). The length cap (0.49 x 0.5 = 0.245 m) keeps it
            // from reaching wall B 0.3 m away, so the stub is left alone instead of shooting across the gap.
            Face3D a = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(0.5, 0, 0), new Point3D(0.5, 0, 3), new Point3D(0, 0, 3));
            Face3D b = TestGeometry.CreatePlanarFace(
                new Point3D(0.8, 0, 0), new Point3D(0.8, 2, 0), new Point3D(0.8, 2, 3), new Point3D(0.8, 0, 3));
            SnappedPanel wallA = new SnappedPanel(0, a, 1, 0.3, maxExtension: 1.0);
            SnappedPanel wallB = new SnappedPanel(1, b, 1, 0.3, maxExtension: 1.0);

            Panel3DSnapSolver.ExtendWalls(
                new List<SnappedPanel> { wallA, wallB }, 20 * (System.Math.PI / 180), overshoot: 0.05, toleranceDistance: 1e-6);

            Assert.Equal(0.5, wallA.GetBoundingBox().Max.X, 3); // capped: 0.245 m reach < 0.3 m gap
        }

        [Fact]
        public void ExtendWalls_TwoShortWallsAtCorner_BothExtendToMeet()
        {
            // Wall A along X at y=0 (x 0..2); wall B along Y at x=2.5 (y 0.5..2). NEITHER reaches the corner
            // (2.5, 0): A's +X end stops at x=2, B's -Y end stops at y=0.5, and each end's ray misses the
            // other's extent. The cost-based ExtensionSolver extends BOTH ends to the shared corner - the
            // case the old per-end ray scan left open.
            Face3D a = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 0, 3), new Point3D(0, 0, 3));
            Face3D b = TestGeometry.CreatePlanarFace(
                new Point3D(2.5, 0.5, 0), new Point3D(2.5, 2, 0), new Point3D(2.5, 2, 3), new Point3D(2.5, 0.5, 3));
            SnappedPanel wallA = new SnappedPanel(0, a, 1, 0.3, maxExtension: 1.0);
            SnappedPanel wallB = new SnappedPanel(1, b, 1, 0.3, maxExtension: 1.0);

            Panel3DSnapSolver.ExtendWalls(
                new List<SnappedPanel> { wallA, wallB }, 20 * (System.Math.PI / 180), overshoot: 0.05, toleranceDistance: 1e-6);

            Assert.True(wallA.GetBoundingBox().Max.X >= 2.5, "Wall A should extend to the corner x=2.5");
            Assert.True(wallB.GetBoundingBox().Min.Y <= 0.0, "Wall B should extend to the corner y=0");
        }

        [Fact]
        public void ExtendWalls_NoWallWithinReach_LeavesWallUntouched()
        {
            // The perpendicular wall is 8 m away - well beyond the 1 m MaxExtend reach.
            Face3D a = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 0, 3), new Point3D(0, 0, 3));
            Face3D b = TestGeometry.CreatePlanarFace(
                new Point3D(10, 0, 0), new Point3D(10, 2, 0), new Point3D(10, 2, 3), new Point3D(10, 0, 3));
            SnappedPanel wallA = new SnappedPanel(0, a, 1, 0.3, maxExtension: 1.0);
            SnappedPanel wallB = new SnappedPanel(1, b, 1, 0.3, maxExtension: 1.0);

            Panel3DSnapSolver.ExtendWalls(
                new List<SnappedPanel> { wallA, wallB }, 20 * (System.Math.PI / 180), overshoot: 0.05, toleranceDistance: 1e-6);

            Assert.Equal(2.0, wallA.GetBoundingBox().Max.X, 3);
        }

        [Fact]
        public void ExtendWalls_GapBeyondMaxExtend_LeavesWallUntouched()
        {
            // Wall B is 1 m past A's end, but A's MaxExtend is only 0.5 m - too short to reach, so A stays put.
            // (This is the per-panel control: raise MaxExtend to let a given wall close a wider corner.)
            Face3D a = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 0, 3), new Point3D(0, 0, 3));
            Face3D b = TestGeometry.CreatePlanarFace(
                new Point3D(3, 0, 0), new Point3D(3, 2, 0), new Point3D(3, 2, 3), new Point3D(3, 0, 3));
            SnappedPanel wallA = new SnappedPanel(0, a, 1, 0.3, maxExtension: 0.5);
            SnappedPanel wallB = new SnappedPanel(1, b, 1, 0.3, maxExtension: 0.5);

            Panel3DSnapSolver.ExtendWalls(
                new List<SnappedPanel> { wallA, wallB }, 20 * (System.Math.PI / 180), overshoot: 0.05, toleranceDistance: 1e-6);

            Assert.Equal(2.0, wallA.GetBoundingBox().Max.X, 3);
        }

        [Fact]
        public void ExtendWalls_ParallelWalls_LeavesBothUntouched()
        {
            // Two collinear walls along X with a gap (x 0..2 and x 5..7): parallel, so neither closes onto
            // the other - a corner needs a crossing wall, not a colinear one.
            Face3D a = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 0, 3), new Point3D(0, 0, 3));
            Face3D b = TestGeometry.CreatePlanarFace(
                new Point3D(5, 0, 0), new Point3D(7, 0, 0), new Point3D(7, 0, 3), new Point3D(5, 0, 3));
            SnappedPanel wallA = new SnappedPanel(0, a, 1, 0.3, maxExtension: 10.0);
            SnappedPanel wallB = new SnappedPanel(1, b, 1, 0.3, maxExtension: 10.0);

            Panel3DSnapSolver.ExtendWalls(
                new List<SnappedPanel> { wallA, wallB }, 20 * (System.Math.PI / 180), overshoot: 0.05, toleranceDistance: 1e-6);

            Assert.Equal(2.0, wallA.GetBoundingBox().Max.X, 3);
            Assert.Equal(5.0, wallB.GetBoundingBox().Min.X, 3);
        }

        // ──────────────────────────────────────────────────────────────
        // Panel3DSnapSolver.OpenWallEnds (plan-closure diagnostic)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void OpenWallEnds_ClosedSquare_ReturnsNoOpenEnds()
        {
            // Four vertical walls whose feet form a closed 2x2 plan loop: every end meets a neighbour.
            List<SnappedPanel> walls = ClosedSquareWalls();

            List<Point3D> openEnds = Panel3DSnapSolver.OpenWallEnds(
                walls, 20 * (System.Math.PI / 180), connectionTolerance: 0.1, toleranceDistance: 1e-6, out List<Face3D> openFaces);

            Assert.Empty(openEnds);
            Assert.Empty(openFaces);
        }

        [Fact]
        public void OpenWallEnds_OpenUShape_ReturnsTheTwoFreeEnds()
        {
            // Drop the top wall (0,2)-(2,2): the two ends that met it - (2,2) and (0,2) - are now open.
            List<SnappedPanel> walls = ClosedSquareWalls();
            walls.RemoveAt(2); // the top wall

            List<Point3D> openEnds = Panel3DSnapSolver.OpenWallEnds(
                walls, 20 * (System.Math.PI / 180), connectionTolerance: 0.1, toleranceDistance: 1e-6, out List<Face3D> openFaces);

            Assert.Equal(2, openEnds.Count);
            Assert.Equal(2, openFaces.Count); // the two walls that owned the now-open ends
            Assert.Contains(openEnds, p => System.Math.Abs(p.X - 2) < 1e-3 && System.Math.Abs(p.Y - 2) < 1e-3);
            Assert.Contains(openEnds, p => System.Math.Abs(p.X - 0) < 1e-3 && System.Math.Abs(p.Y - 2) < 1e-3);
        }

        /// <summary>Four vertical walls (z 0..3) whose feet trace a closed 2x2 plan square.</summary>
        private static List<SnappedPanel> ClosedSquareWalls()
        {
            return new List<SnappedPanel>
            {
                new SnappedPanel(0, TestGeometry.CreatePlanarFace( // (0,0)->(2,0)
                    new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 0, 3), new Point3D(0, 0, 3)), 1, 0.3, 0.4),
                new SnappedPanel(1, TestGeometry.CreatePlanarFace( // (2,0)->(2,2)
                    new Point3D(2, 0, 0), new Point3D(2, 2, 0), new Point3D(2, 2, 3), new Point3D(2, 0, 3)), 1, 0.3, 0.4),
                new SnappedPanel(2, TestGeometry.CreatePlanarFace( // (0,2)->(2,2)
                    new Point3D(0, 2, 0), new Point3D(2, 2, 0), new Point3D(2, 2, 3), new Point3D(0, 2, 3)), 1, 0.3, 0.4),
                new SnappedPanel(3, TestGeometry.CreatePlanarFace( // (0,0)->(0,2)
                    new Point3D(0, 0, 0), new Point3D(0, 2, 0), new Point3D(0, 2, 3), new Point3D(0, 0, 3)), 1, 0.3, 0.4),
            };
        }

        // ──────────────────────────────────────────────────────────────
        // Step 1: strip internal edges + clean bucket (snap + coplanar merge)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void StripInternalEdges_PanelWithHole_RemovesHole()
        {
            // 4x4 external wall (XZ, y=0) with a 1x1 hole -> area 15 before, 16 after stripping the hole.
            SnappedPanel panel = new SnappedPanel(0, MakeWallFaceWithHole(), 1, 0.3, 0.5);
            Assert.Equal(15.0, panel.GetArea(), 1);

            bool stripped = panel.StripInternalEdges();

            Assert.True(stripped);
            Assert.Null(panel.Face3D.GetInternalEdge3Ds());
            Assert.Equal(16.0, panel.GetArea(), 1);
        }

        [Fact]
        public void StripInternalEdges_SolidPanel_NoChange()
        {
            SnappedPanel solid = MakeWallPanel(0);
            Assert.False(solid.StripInternalEdges());
        }

        [Fact]
        public void CleanBucket_SmallPanelContainedInLargerCoplanar_MergesToOne()
        {
            // A 1x1 panel fully inside a 4x3 wall, both on the y=0 plane -> one merged 4x3 face.
            Face3D large = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 0, 3), new Point3D(0, 0, 3));
            Face3D small = TestGeometry.CreatePlanarFace(
                new Point3D(1, 0, 1), new Point3D(2, 0, 1), new Point3D(2, 0, 2), new Point3D(1, 0, 2));
            List<SnappedPanel> panels = new List<SnappedPanel>
            {
                new SnappedPanel(0, large, 2, 0.3, 0.5),
                new SnappedPanel(1, small, 1, 0.3, 0.5),
            };

            List<Face3D> clean = Panel3DSnapSolver.CleanBucket(panels, 5 * (System.Math.PI / 180), 0.3 * (System.Math.PI / 180), 1e-6);

            Assert.Single(clean);
            Assert.Equal(12.0, clean[0].GetArea(), 1); // 4x3; the contained 1x1 is absorbed
        }

        [Fact]
        public void CleanBucket_TwoParallelWallsWithinBucket_MergeToOne()
        {
            // Two identical walls 0.15 m apart (within bucket): the lower-weight snaps onto the backer, then merge.
            List<SnappedPanel> panels = new List<SnappedPanel>
            {
                MakeWallPanel(0, weight: 2, bucketSize: 0.3),
                MakeWallPanel(0.15, weight: 1, bucketSize: 0.3),
            };

            List<Face3D> clean = Panel3DSnapSolver.CleanBucket(panels, 5 * (System.Math.PI / 180), 0.3 * (System.Math.PI / 180), 1e-6);

            Assert.Single(clean);
        }

        [Fact]
        public void CleanBucket_PerpendicularPanels_KeptSeparate()
        {
            // A wall and a floor are not coplanar -> both survive as distinct clean panels.
            List<SnappedPanel> panels = new List<SnappedPanel>
            {
                MakeWallPanel(0),
                MakeFloorPanel(0),
            };

            List<Face3D> clean = Panel3DSnapSolver.CleanBucket(panels, 5 * (System.Math.PI / 180), 0.3 * (System.Math.PI / 180), 1e-6);

            Assert.Equal(2, clean.Count);
        }

        [Fact]
        public void GrowOutward_Floor_IncreasesArea()
        {
            SnappedPanel floor = MakeFloorPanel(0); // unit 1x1 floor, area 1
            double areaBefore = floor.GetArea();

            bool grown = floor.GrowOutward(0.5, 1e-6);

            Assert.True(grown);
            Assert.True(floor.GetArea() > areaBefore, "Grown floor should have larger area");
        }

        [Fact]
        public void GapFill_OpenBoxMissingTop_FillsTheOpening()
        {
            // Unit cube with no ceiling: floor + 4 walls. The 4 top edges form one naked loop.
            List<Face3D> faces = new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(0,0,0), new Point3D(1,0,0), new Point3D(1,1,0), new Point3D(0,1,0)), // floor
                TestGeometry.CreatePlanarFace(new Point3D(0,0,0), new Point3D(1,0,0), new Point3D(1,0,1), new Point3D(0,0,1)), // y=0
                TestGeometry.CreatePlanarFace(new Point3D(0,1,0), new Point3D(1,1,0), new Point3D(1,1,1), new Point3D(0,1,1)), // y=1
                TestGeometry.CreatePlanarFace(new Point3D(0,0,0), new Point3D(0,1,0), new Point3D(0,1,1), new Point3D(0,0,1)), // x=0
                TestGeometry.CreatePlanarFace(new Point3D(1,0,0), new Point3D(1,1,0), new Point3D(1,1,1), new Point3D(1,0,1)), // x=1
            };

            List<Face3D> fill = GapFill.NakedLoopFace3Ds(faces, null, 1e-3);

            Assert.Single(fill);
            Assert.Equal(1.0, fill[0].GetArea(), 2);            // the 1x1 opening
            Assert.Equal(1.0, fill[0].GetBoundingBox().Max.Z, 3); // at the top, z = 1
        }

        [Fact]
        public void Execute_StopAfterExtend_ExtendsWallToCapWithoutNativeResolve()
        {
            // Wall z 0..3 (XZ, y=0) with a floor cap at z=4 directly above it. StopAfterExtend runs the managed
            // clean + fill + extend, but stops before the native resolve, so the wall is extended up to the cap
            // (overshooting) and NativeResolved stays false - the split (trim) is Solve3D's job.
            Face3D wall = MakeWallFace(0);
            Face3D cap = MakeFloorFace(4.0);

            Panel3DSnapSolver solver = new Panel3DSnapSolver(new List<Face3D> { wall, cap }) { StopAfterExtend = true };
            solver.Execute();

            Assert.False(solver.NativeResolved, "StopAfterExtend must not run the native resolve (split)");
            Assert.NotEmpty(solver.ResolvedFace3Ds);

            // The wall has been extended up toward the cap (its top reached at least the cap elevation).
            double topZ = solver.ResolvedFace3Ds.Max(x => x.GetBoundingBox().Max.Z);
            Assert.True(topZ >= 4.0, "Wall should be extended up to the cap (z >= 4)");
        }

        [Fact]
        public void Fill_GrowsCapsButNotWalls()
        {
            SnappedPanel wall = MakeWallPanel(0);
            SnappedPanel floor = MakeFloorPanel(0);
            double wallAreaBefore = wall.GetArea();
            double floorAreaBefore = floor.GetArea();

            Panel3DSnapSolver.Fill(new List<SnappedPanel> { wall, floor }, 20 * (System.Math.PI / 180), margin: 0.5, toleranceDistance: 1e-6);

            Assert.Equal(wallAreaBefore, wall.GetArea(), 3);          // wall untouched
            Assert.True(floor.GetArea() > floorAreaBefore);          // floor grown
        }

        [Fact]
        public void GrowOutwardTo_FloorShortOfWall_GrowsToMeetWall()
        {
            // A wall in the y=0 plane (x 0..1, z 0..3) and a floor at z=0 that stops 0.1 m short of it
            // (y 0.1..1). The measured grow should extend the floor to meet/overshoot the wall plane at y=0,
            // not by a blanket margin.
            SnappedPanel wall = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(1, 0, 3), new Point3D(0, 0, 3)), 1, 0.3, 0.5);
            SnappedPanel floor = new SnappedPanel(1, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0.1, 0), new Point3D(1, 0.1, 0), new Point3D(1, 1, 0), new Point3D(0, 1, 0)), 1, 0.3, 0.5);

            bool grown = floor.GrowOutwardTo(new List<SnappedPanel> { wall }, maxReach: 0.5, overshoot: 0.05, tolerance: 1e-6);

            Assert.True(grown);
            Assert.True(floor.GetBoundingBox().Min.Y <= 1e-6, "Floor should grow to meet/overshoot the wall plane at y=0");
        }

        [Fact]
        public void GrowOutwardTo_NoWallInReach_ReturnsFalse()
        {
            // The only wall is 10 m away in plan, far beyond the floor's reach, so the measured grow is a no-op
            // and the caller is expected to fall back to the fixed-margin grow.
            SnappedPanel floor = MakeFloorPanel(0);
            SnappedPanel farWall = new SnappedPanel(1, TestGeometry.CreatePlanarFace(
                new Point3D(0, 10, 0), new Point3D(1, 10, 0), new Point3D(1, 10, 3), new Point3D(0, 10, 3)), 1, 0.3, 0.5);

            bool grown = floor.GrowOutwardTo(new List<SnappedPanel> { farWall }, maxReach: 0.5, overshoot: 0.05, tolerance: 1e-6);

            Assert.False(grown);
        }

        [Fact]
        public void GapFill_NonPlanarLoop_ReturnsValidTriangulatedPatch()
        {
            // A floor (z=0) and a wall (y=0) sharing one edge: their free edges form a single, non-planar
            // L-shaped naked loop. A single planar polygon over it is invalid (the old behaviour returned
            // nothing); the fan-triangulation fallback must still close it with valid faces.
            List<Face3D> faces = new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(0,0,0), new Point3D(1,0,0), new Point3D(1,1,0), new Point3D(0,1,0)), // floor z=0
                TestGeometry.CreatePlanarFace(new Point3D(0,0,0), new Point3D(1,0,0), new Point3D(1,0,1), new Point3D(0,0,1)), // wall y=0
            };

            List<Face3D> fill = GapFill.NakedLoopFace3Ds(faces, null, 1e-3);

            Assert.NotEmpty(fill);
            Assert.All(fill, f => Assert.True(f != null && f.IsValid(), "Every patch face should be valid"));
            // The patch spans both the floor (z=0) and the top of the wall (z=1) - it is genuinely non-planar.
            Assert.True(fill.Max(f => f.GetBoundingBox().Max.Z) >= 1.0 - 1e-3, "Patch should reach the wall top (z=1)");
        }

        [Fact]
        public void GapFill_NearCoincidentSharedEdge_MergedAsInteriorNotNaked()
        {
            // Two coplanar floor tiles whose adjacent edges are 0.6 mm apart - under the 1 mm tolerance, but on
            // opposite sides of a grid-rounding boundary (Round(1000)=1000 vs Round(1000.6)=1001). The vertex
            // clustering must merge that seam into one interior edge, so the only naked loop is the outer 2x1
            // rectangle (one patch, area ~2). A grid-rounded key would split the seam into two naked edges and
            // fragment the loop into two patches.
            List<Face3D> faces = new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(0,0,0), new Point3D(1,0,0), new Point3D(1,1,0), new Point3D(0,1,0)),
                TestGeometry.CreatePlanarFace(new Point3D(1.0006,0,0), new Point3D(2,0,0), new Point3D(2,1,0), new Point3D(1.0006,1,0)),
            };

            List<Face3D> fill = GapFill.NakedLoopFace3Ds(faces, null, 1e-3);

            Assert.Single(fill);
            Assert.Equal(2.0, fill[0].GetArea(), 1); // merged outer rectangle, ~2 m^2 (seam treated as interior)
        }

        // ──────────────────────────────────────────────────────────────
        // Panel3DSnapSolver.NormalizeCaps (level-plane normalization)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void NormalizeCaps_AdjacentTilesWithinOffset_SnapOntoDominantPlane()
        {
            // Two edge-touching (non-overlapping) floor tiles of one level: a large 3x3 backer at z=0 and a
            // small 1x1 tile beside it at z=0.1 (within the 0.3 offset). They do not overlap in plan, so the
            // bucket snap leaves them put; NormalizeCaps groups them by perpendicular nearness and projects
            // the smaller onto the dominant (larger) tile's z=0 plane.
            SnappedPanel backer = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(3, 0, 0), new Point3D(3, 3, 0), new Point3D(0, 3, 0)), 1, 0.3, 0.5);
            SnappedPanel tile = new SnappedPanel(1, TestGeometry.CreatePlanarFace(
                new Point3D(3, 0, 0.1), new Point3D(4, 0, 0.1), new Point3D(4, 1, 0.1), new Point3D(3, 1, 0.1)), 1, 0.3, 0.5);

            Panel3DSnapSolver.NormalizeCaps(new List<SnappedPanel> { backer, tile }, 5 * (System.Math.PI / 180), normalizeCapOffset: 0.3, toleranceDistance: 1e-6);

            Assert.True(tile.Snapped, "The adjacent tile should be normalized onto the dominant cap's plane");
            foreach (Point3D pt in BoundaryPoints(tile.Face3D))
            {
                Assert.True(System.Math.Abs(backer.Plane.Distance(pt)) < 1e-6, $"Tile point {pt} not on the dominant z=0 plane");
            }
            Assert.False(backer.Snapped, "The dominant (largest) cap is the backer and stays put");
        }

        [Fact]
        public void NormalizeCaps_FloorAndRoofFarApart_StayOnSeparatePlanes()
        {
            // A floor at z=0 and the roof above at z=3: parallel but far more than the offset apart, so they
            // are different levels and must NOT be merged onto one plane.
            SnappedPanel floor = MakeFloorPanel(0);
            SnappedPanel roof = MakeFloorPanel(3.0);

            Panel3DSnapSolver.NormalizeCaps(new List<SnappedPanel> { floor, roof }, 5 * (System.Math.PI / 180), normalizeCapOffset: 0.3, toleranceDistance: 1e-6);

            Assert.False(floor.Snapped);
            Assert.False(roof.Snapped, "A roof a storey above the floor is a separate level and must stay put");
        }

        [Fact]
        public void NormalizeCaps_NonParallelSlopes_NotMerged()
        {
            // A flat cap (Z-normal) and a 30-deg-tilted cap sharing a similar elevation: their normals differ
            // by more than the angle tolerance, so the two pitches are kept distinct (a real ridge, not noise).
            double theta = 30 * (System.Math.PI / 180);
            SnappedPanel flat = MakeFloorPanel(0);
            SnappedPanel sloped = new SnappedPanel(1, TiltAboutX(MakeFloorFace(0), theta), 1, 0.3, 0.5);

            Panel3DSnapSolver.NormalizeCaps(new List<SnappedPanel> { flat, sloped }, 5 * (System.Math.PI / 180), normalizeCapOffset: 0.3, toleranceDistance: 1e-6);

            Assert.False(flat.Snapped);
            Assert.False(sloped.Snapped, "A differently-pitched cap is not parallel, so it keeps its own plane");
        }

        [Fact]
        public void NormalizeCaps_Walls_LeftUntouched()
        {
            // Vertical walls are not caps; NormalizeCaps must never move them (the bucket snap handles walls).
            SnappedPanel wallA = MakeWallPanel(0);
            SnappedPanel wallB = MakeWallPanel(0.1);

            Panel3DSnapSolver.NormalizeCaps(new List<SnappedPanel> { wallA, wallB }, 5 * (System.Math.PI / 180), normalizeCapOffset: 0.3, toleranceDistance: 1e-6);

            Assert.False(wallA.Snapped);
            Assert.False(wallB.Snapped);
        }

        [Fact]
        public void NormalizeCaps_OffsetDisabled_DoesNothing()
        {
            // normalizeCapOffset <= tolerance disables the pass: even near-coincident tiles stay put.
            SnappedPanel backer = MakeFloorPanel(0);
            SnappedPanel tile = MakeFloorPanel(0.1);

            Panel3DSnapSolver.NormalizeCaps(new List<SnappedPanel> { backer, tile }, 5 * (System.Math.PI / 180), normalizeCapOffset: 0, toleranceDistance: 1e-6);

            Assert.False(tile.Snapped);
        }

        // ──────────────────────────────────────────────────────────────
        // Panel3DSnapSolver.SnapOpposedPartitions (back-to-back partitions)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void SnapOpposedPartitions_CongruentOpposingSkinsWithinBucket_CollapseOntoOnePlane()
        {
            // Two room-facing skins of one shared partition are the same wall seen from each room, so they are
            // congruent (equal area): a skin at y=0 (normal -Y) and its twin at y=0.15 (normal +Y), both
            // 1 x 3, within the 0.3 m bucket and overlapping in plan. They must collapse onto the second
            // skin's plane (y=0.15) so the fragile room closes exactly while the other room's gap is left for
            // the fill to recover.
            SnappedPanel first = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(1, 0, 3), new Point3D(0, 0, 3)), 1, 0.3, 0.5);
            SnappedPanel second = new SnappedPanel(1, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0.15, 0), new Point3D(0, 0.15, 3), new Point3D(1, 0.15, 3), new Point3D(1, 0.15, 0)), 1, 0.3, 0.5);

            // Sanity: the two skins genuinely oppose.
            Assert.True(first.Plane.Normal.Unit.DotProduct(second.Plane.Normal.Unit) < -0.99, "Skins should be anti-parallel");

            Panel3DSnapSolver.SnapOpposedPartitions(new List<SnappedPanel> { first, second }, 5 * (System.Math.PI / 180), 1e-6);

            // Both skins now lie on the second skin's plane (y = 0.15).
            foreach (Point3D pt in BoundaryPoints(first.Face3D))
            {
                Assert.True(System.Math.Abs(pt.Y - 0.15) < 1e-6, $"Skin point {pt} not collapsed onto the partner skin plane y=0.15");
            }
            Assert.True(first.Snapped, "The first skin should have been projected onto its congruent partner's plane");
        }

        [Fact]
        public void SnapOpposedPartitions_UnequalAreaOpposingSkins_LeftAlone()
        {
            // Anti-parallel, within-bucket, in-plane-overlapping - but the two faces are very different sizes
            // (area 6 vs 3), so they are NOT one partition's two (congruent) skins: e.g. a long shared wall
            // caught against a short partition skin. Collapsing such a mis-pair drags the long wall off its
            // room and merges two rooms into one cell, so the area-ratio gate must leave them put.
            SnappedPanel large = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 0, 3), new Point3D(0, 0, 3)), 1, 0.3, 0.5);
            SnappedPanel small = new SnappedPanel(1, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0.15, 0), new Point3D(0, 0.15, 3), new Point3D(1, 0.15, 3), new Point3D(1, 0.15, 0)), 1, 0.3, 0.5);

            Assert.True(large.Plane.Normal.Unit.DotProduct(small.Plane.Normal.Unit) < -0.99, "Skins should be anti-parallel");

            Panel3DSnapSolver.SnapOpposedPartitions(new List<SnappedPanel> { large, small }, 5 * (System.Math.PI / 180), 1e-6);

            Assert.False(large.Snapped, "Unequal-area opposing faces are a mis-pair and must not be collapsed");
            Assert.False(small.Snapped, "Unequal-area opposing faces are a mis-pair and must not be collapsed");
        }

        [Fact]
        public void SnapOpposedPartitions_SameFacingDuplicate_LeftToWeightedSnap()
        {
            // Two SAME-facing parallel skins (a wall imported twice) are a genuine double-wall, not a
            // back-to-back partition; SnapOpposedPartitions must not touch them (the weighted bucket snap does).
            SnappedPanel a = MakeWallPanel(0);
            SnappedPanel b = MakeWallPanel(0.15);
            Assert.True(a.Plane.Normal.Unit.DotProduct(b.Plane.Normal.Unit) > 0.99, "Skins should be parallel (same facing)");

            Panel3DSnapSolver.SnapOpposedPartitions(new List<SnappedPanel> { a, b }, 5 * (System.Math.PI / 180), 1e-6);

            Assert.False(a.Snapped);
            Assert.False(b.Snapped);
        }

        [Fact]
        public void SnapOpposedPartitions_OpposingSkinsBeyondBucket_LeftAlone()
        {
            // Opposing skins a whole room apart (1 m, well beyond the 0.3 m bucket) are two distinct external
            // walls, not a shared partition - they must not be collapsed.
            SnappedPanel a = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 0, 3), new Point3D(0, 0, 3)), 1, 0.3, 0.5);
            SnappedPanel b = new SnappedPanel(1, TestGeometry.CreatePlanarFace(
                new Point3D(0, 1, 0), new Point3D(0, 1, 3), new Point3D(2, 1, 3), new Point3D(2, 1, 0)), 1, 0.3, 0.5);

            Panel3DSnapSolver.SnapOpposedPartitions(new List<SnappedPanel> { a, b }, 5 * (System.Math.PI / 180), 1e-6);

            Assert.False(a.Snapped);
            Assert.False(b.Snapped);
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

        /// <summary>4x4 wall in the XZ plane (y=0) with a 1x1 internal hole (area 15).</summary>
        private static Face3D MakeWallFaceWithHole()
        {
            List<Point3D> external = new List<Point3D>
            {
                new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 0, 4), new Point3D(0, 0, 4)
            };
            List<Point3D> hole = new List<Point3D>
            {
                new Point3D(1, 0, 1), new Point3D(2, 0, 1), new Point3D(2, 0, 2), new Point3D(1, 0, 2)
            };
            return Face3D.Create(new List<IClosedPlanar3D> { new Polygon3D(external), new Polygon3D(hole) });
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
