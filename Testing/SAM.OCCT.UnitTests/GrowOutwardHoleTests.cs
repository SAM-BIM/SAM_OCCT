// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Regression tests for cap growth over a panel with an internal opening. <see cref="SnappedPanel.GrowOutward"/>
    /// must offset ONLY the external boundary: the opening keeps its original position and shape, so the net area is
    /// the grown outline minus the unchanged opening. Previously the shared SAM Face2D offset (which offsets each
    /// internal edge from the external outline) turned every opening into a copy of the grown outline, silently
    /// removing it (and no SAM_OCCT_EXTEND3D_HOLE_DROPPED was emitted). Pure-managed (no native).
    /// </summary>
    public class GrowOutwardHoleTests
    {
        private const double Tolerance = 1e-6;
        private const double AreaTolerance = 1e-4;

        /// <summary>A 10 x 10 m slab (z=0, x/y 0..10) with a 2 x 2 m opening at x/y 4..6.</summary>
        private static SnappedPanel SlabWithOpening()
        {
            Face3D face = Face3D.Create(new List<IClosedPlanar3D>
            {
                new Polygon3D(new List<Point3D> { new Point3D(0, 0, 0), new Point3D(10, 0, 0), new Point3D(10, 10, 0), new Point3D(0, 10, 0) }),
                new Polygon3D(new List<Point3D> { new Point3D(4, 4, 0), new Point3D(6, 4, 0), new Point3D(6, 6, 0), new Point3D(4, 6, 0) })
            });
            return new SnappedPanel(0, face, 1, 0.3, 0.5);
        }

        private static SnappedPanel WallAlongX(double x)
        {
            return new SnappedPanel(1, TestGeometry.CreatePlanarFace(
                new Point3D(x, 0, 0), new Point3D(x, 10, 0), new Point3D(x, 10, 3), new Point3D(x, 0, 3)), 1, 0.3, 0.5);
        }

        private static void AssertOpeningUnchanged(SnappedPanel slab)
        {
            List<IClosedPlanar3D> holes = slab.Face3D.GetInternalEdge3Ds();
            Assert.NotNull(holes);
            Assert.Single(holes);
            BoundingBox3D box = holes[0].GetBoundingBox();
            Assert.Equal(4.0, box.Min.X, 6);
            Assert.Equal(4.0, box.Min.Y, 6);
            Assert.Equal(6.0, box.Max.X, 6);
            Assert.Equal(6.0, box.Max.Y, 6);
            Assert.Equal(4.0, holes[0].GetArea(), 6); // same 2 x 2 shape
        }

        [Fact]
        public void GrowOutward_SlabWithOpening_GrowsPerimeterAndKeepsOpeningInPlace()
        {
            // Arrange
            SnappedPanel slab = SlabWithOpening();

            // Act
            bool grown = slab.GrowOutward(0.5, Tolerance);

            // Assert - 11 x 11 perimeter, opening at its original position and shape, 121 - 4 = 117 m2 net
            Assert.True(grown);
            BoundingBox3D outer = slab.Face3D.GetExternalEdge3D().GetBoundingBox();
            Assert.Equal(-0.5, outer.Min.X, 6);
            Assert.Equal(-0.5, outer.Min.Y, 6);
            Assert.Equal(10.5, outer.Max.X, 6);
            Assert.Equal(10.5, outer.Max.Y, 6);
            Assert.Equal(121.0, slab.Face3D.GetExternalEdge3D().GetArea(), 4);
            AssertOpeningUnchanged(slab);
            Assert.Equal(117.0, slab.GetArea(), 4);
        }

        [Fact]
        public void GrowOutward_SlabWithOpening_PreservedOpeningEmitsNoHoleDropDiagnostic()
        {
            // Arrange
            SnappedPanel slab = SlabWithOpening();

            // Act
            slab.GrowOutward(0.5, Tolerance);

            // Assert - nothing was lost, so nothing is reported (instance diagnostics; the static counter is shared across tests)
            Assert.DoesNotContain(slab.ExtendDiagnostics, d => d.Contains("SAM_OCCT_EXTEND3D_HOLE_DROPPED"));
        }

        [Fact]
        public void GrowOutward_SolidSlab_StillGrows()
        {
            // Arrange - guard that the opening-free path is unchanged
            SnappedPanel slab = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(10, 0, 0), new Point3D(10, 10, 0), new Point3D(0, 10, 0)), 1, 0.3, 0.5);

            // Act
            bool grown = slab.GrowOutward(0.5, Tolerance);

            // Assert
            Assert.True(grown);
            Assert.Equal(121.0, slab.GetArea(), 4);
            Assert.Null(slab.Face3D.GetInternalEdge3Ds());
        }

        [Fact]
        public void GrowOutwardTo_SlabWithOpeningShortOfWall_GrowsToWallAndKeepsOpeningInPlace()
        {
            // Arrange - a wall plane 0.3 m outside the x=0 edge; measured grow = 0.3 gap + 0.05 overshoot = 0.35
            SnappedPanel slab = SlabWithOpening();
            List<SnappedPanel> walls = new List<SnappedPanel> { WallAlongX(-0.3) };

            // Act
            bool grown = slab.GrowOutwardTo(walls, maxReach: 0.5, overshoot: 0.05, tolerance: Tolerance);

            // Assert - uniform 0.35 m growth -> 10.7 x 10.7 outline, opening untouched
            Assert.True(grown);
            BoundingBox3D outer = slab.Face3D.GetExternalEdge3D().GetBoundingBox();
            Assert.Equal(-0.35, outer.Min.X, 6);
            Assert.Equal(10.35, outer.Max.X, 6);
            AssertOpeningUnchanged(slab);
            Assert.Equal((10.7 * 10.7) - 4.0, slab.GetArea(), 4);
        }

        [Fact]
        public void GrowOutwardTo_SlabWithOpeningNoWallInReach_ReturnsFalseAndKeepsOpening()
        {
            // Arrange - the only wall is far away: the measured grow is a no-op and must not touch the opening
            SnappedPanel slab = SlabWithOpening();
            List<SnappedPanel> walls = new List<SnappedPanel> { WallAlongX(-20) };

            // Act
            bool grown = slab.GrowOutwardTo(walls, maxReach: 0.5, overshoot: 0.05, tolerance: Tolerance);

            // Assert
            Assert.False(grown);
            AssertOpeningUnchanged(slab);
            Assert.Equal(96.0, slab.GetArea(), 4);
        }
    }
}
