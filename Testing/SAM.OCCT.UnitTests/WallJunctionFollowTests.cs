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
    /// Root Cause B1 junction-follow contract: after wall-stack consolidation moves a wall off its old plane,
    /// an abutting perpendicular wall whose plan end terminated on that old plane is dragged onto the new
    /// plane so the T/L corner stays closed. The B1 change relaxed the ALONG-line match to
    /// <see cref="Panel3DSnapSolver.STACK_FOLLOW_END_OVERHANG"/> (0.1 m — the import corner-undershoot) while
    /// keeping the PERPENDICULAR match at <see cref="Panel3DSnapSolver.STACK_FOLLOW_END_TOLERANCE"/> (0.02 m).
    /// <para>These drive the production <see cref="Panel3DSnapSolver.DragAbuttingWallEnds"/> directly with
    /// synthetic geometry (no towers fixture). The moved wall's old plane is y=0.474 and its new plane is y=0;
    /// the observable is whether the abutting wall's end is carried from y≈0.474 onto y≈0.</para>
    /// </summary>
    public class WallJunctionFollowTests
    {
        private const double VerticalAngle = 20 * (System.Math.PI / 180);
        private const double Distance = 1e-6;
        private const double OldY = 0.474; // the moved wall's old plane
        private const double FootEndX = 10.0; // the moved wall's foot spans x∈[0,10]

        // A wall in the plane y=<paramref name="y"/> (normal ±Y), spanning x∈[x0,x1], z∈[z0,z1].
        private static SnappedPanel WallY(double y, double x0, double x1, double z0, double z1)
        {
            Face3D face = TestGeometry.CreatePlanarFace(
                new Point3D(x0, y, z0), new Point3D(x1, y, z0),
                new Point3D(x1, y, z1), new Point3D(x0, y, z1));
            return new SnappedPanel(0, face, 1.0, 0.3, Panel3DSnapSolver.DEFAULT_MaxExtension);
        }

        // A wall perpendicular to WallY: plane x=<paramref name="x"/> (normal ±X), spanning y∈[y0,y1], z∈[z0,z1].
        private static SnappedPanel WallX(double x, double y0, double y1, double z0, double z1)
        {
            Face3D face = TestGeometry.CreatePlanarFace(
                new Point3D(x, y0, z0), new Point3D(x, y1, z0),
                new Point3D(x, y1, z1), new Point3D(x, y0, z1));
            return new SnappedPanel(0, face, 1.0, 0.3, Panel3DSnapSolver.DEFAULT_MaxExtension);
        }

        /// <summary>Runs the junction-follow drag for a moved wall (old plane y=0.474 → new plane y=0) against a
        /// single abutting <paramref name="abutting"/> wall, and returns whether that wall's near-y end was
        /// carried onto the new plane (y≈0).</summary>
        private static bool AbuttingEndDragged(SnappedPanel abutting)
        {
            // Moved wall: capture its old plane/foot/box at y=0.474, then snap it onto the dominant plane y=0.
            SnappedPanel moved = WallY(OldY, 0, FootEndX, 0, 3);
            Plane oldPlane = moved.Plane;
            Segment3D oldFoot = moved.GetBaseSegment(Distance);
            BoundingBox3D oldBox = moved.GetBoundingBox();
            SnappedPanel dominant = WallY(0, 0, FootEndX, 0, 3);
            Assert.True(moved.SnapToBacker(dominant.Plane));

            double beforeMinY = abutting.GetBoundingBox().Min.Y;
            Panel3DSnapSolver.DragAbuttingWallEnds(
                new List<SnappedPanel> { moved, abutting }, moved, oldPlane, oldFoot, oldBox, Distance, null);
            double afterMinY = abutting.GetBoundingBox().Min.Y;

            // Dragged ⇔ the near-y end moved from ≈0.474 down onto the new plane ≈0.
            return beforeMinY > OldY - 0.05 && afterMinY < 0.05;
        }

        // 1. ~46 mm longitudinal overhang (the observed whole-level-towers undershoot): dragged.
        [Fact]
        public void DragAbuttingWallEnds_Overhang46mm_Dragged()
        {
            SnappedPanel abutting = WallX(FootEndX + 0.046, OldY, 5, 0, 3);
            Assert.True(AbuttingEndDragged(abutting));
        }

        // 2. Just below the 100 mm along-line allowance: dragged.
        [Fact]
        public void DragAbuttingWallEnds_OverhangJustBelow100mm_Dragged()
        {
            SnappedPanel abutting = WallX(FootEndX + 0.099, OldY, 5, 0, 3);
            Assert.True(AbuttingEndDragged(abutting));
        }

        // 3. Just above the 100 mm along-line allowance: NOT dragged.
        [Fact]
        public void DragAbuttingWallEnds_OverhangJustAbove100mm_NotDragged()
        {
            SnappedPanel abutting = WallX(FootEndX + 0.101, OldY, 5, 0, 3);
            Assert.False(AbuttingEndDragged(abutting));
        }

        // 4. More than 20 mm PERPENDICULAR off the old foot line: NOT dragged (even with a small overhang).
        [Fact]
        public void DragAbuttingWallEnds_Perpendicular21mm_NotDragged()
        {
            SnappedPanel abutting = WallX(FootEndX + 0.046, OldY + 0.021, 5, 0, 3);
            Assert.False(AbuttingEndDragged(abutting));
        }

        // 5. No overlapping storey Z range: NOT dragged (the end belongs to a different storey's junction).
        [Fact]
        public void DragAbuttingWallEnds_NoStoreyZOverlap_NotDragged()
        {
            SnappedPanel abutting = WallX(FootEndX + 0.046, OldY, 5, 5, 8); // z∈[5,8], moved wall z∈[0,3]
            Assert.False(AbuttingEndDragged(abutting));
        }

        // 6. A wall PARALLEL to the moved wall (a stack sibling, not a junction partner): NOT dragged.
        [Fact]
        public void DragAbuttingWallEnds_ParallelWall_NotDragged()
        {
            // Parallel: same normal (±Y) as the moved wall, continuing its line at y=0.474 past x=10.
            SnappedPanel parallel = WallY(OldY, FootEndX, FootEndX + 2, 0, 3);
            double beforeMinY = parallel.GetBoundingBox().Min.Y;

            SnappedPanel moved = WallY(OldY, 0, FootEndX, 0, 3);
            Plane oldPlane = moved.Plane;
            Segment3D oldFoot = moved.GetBaseSegment(Distance);
            BoundingBox3D oldBox = moved.GetBoundingBox();
            SnappedPanel dominant = WallY(0, 0, FootEndX, 0, 3);
            moved.SnapToBacker(dominant.Plane);

            Panel3DSnapSolver.DragAbuttingWallEnds(
                new List<SnappedPanel> { moved, parallel }, moved, oldPlane, oldFoot, oldBox, Distance, null);

            // A parallel wall is skipped by the junction-partner guard, so its plane is unchanged.
            Assert.Equal(beforeMinY, parallel.GetBoundingBox().Min.Y, 6);
        }
    }
}
