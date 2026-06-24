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
