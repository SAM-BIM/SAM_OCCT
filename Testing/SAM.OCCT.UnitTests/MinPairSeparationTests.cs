// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="MinPairSeparation"/> - the managed near-parallel pair gap measurement
    /// that caps the Phase 5d adaptive-sew tolerance (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md
    /// §A.3/§F/§J.2). Pure-managed: no native OCCT DLL required.
    /// </summary>
    public class MinPairSeparationTests
    {
        /// <summary>A unit quad in the plane z = <paramref name="z"/> (normal +Z), spanning [0,1]x[0,1].</summary>
        private static Face3D HorizontalQuad(double z)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, z), new Point3D(1, 0, z), new Point3D(1, 1, z), new Point3D(0, 1, z));
        }

        /// <summary>A vertical quad in the plane x = <paramref name="x"/> (normal +X), spanning y,z in [0,1].</summary>
        private static Face3D VerticalQuad(double x)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(x, 0, 0), new Point3D(x, 1, 0), new Point3D(x, 1, 1), new Point3D(x, 0, 1));
        }

        [Fact]
        public void Compute_ParallelPairAt008_ReturnsSeparation()
        {
            // Arrange - two overlapping parallel faces 0.08 m apart.
            List<Face3D> face3Ds = new List<Face3D> { HorizontalQuad(0.0), HorizontalQuad(0.08) };

            // Act
            double? separation = MinPairSeparation.Compute(face3Ds);

            // Assert
            Assert.NotNull(separation);
            Assert.Equal(0.08, separation.Value, 6);
        }

        [Fact]
        public void Compute_ParallelPairAt030_ReturnsSeparation()
        {
            // Arrange - still within the [Tolerance.Distance, 0.5] window.
            List<Face3D> face3Ds = new List<Face3D> { HorizontalQuad(0.0), HorizontalQuad(0.30) };

            // Act
            double? separation = MinPairSeparation.Compute(face3Ds);

            // Assert
            Assert.NotNull(separation);
            Assert.Equal(0.30, separation.Value, 6);
        }

        [Fact]
        public void Compute_MultipleParallelPairs_ReturnsTheSmallest()
        {
            // Arrange - gaps of 0.30 and 0.08; the min is 0.08.
            List<Face3D> face3Ds = new List<Face3D> { HorizontalQuad(0.0), HorizontalQuad(0.30), HorizontalQuad(0.38) };

            // Act - 0.30->0.38 is 0.08 apart, 0.0->0.30 is 0.30, 0.0->0.38 is 0.38 (> max, ignored).
            double? separation = MinPairSeparation.Compute(face3Ds);

            // Assert
            Assert.NotNull(separation);
            Assert.Equal(0.08, separation.Value, 6);
        }

        [Fact]
        public void Compute_NonParallelPair_IsIgnored()
        {
            // Arrange - a horizontal and a vertical face (perpendicular normals).
            List<Face3D> face3Ds = new List<Face3D> { HorizontalQuad(0.0), VerticalQuad(0.08) };

            // Act
            double? separation = MinPairSeparation.Compute(face3Ds);

            // Assert - no near-parallel pair exists.
            Assert.Null(separation);
        }

        [Fact]
        public void Compute_NoValidPair_ReturnsNull()
        {
            // Arrange - a single face has no pair at all.
            List<Face3D> face3Ds = new List<Face3D> { HorizontalQuad(0.0) };

            // Act & Assert
            Assert.Null(MinPairSeparation.Compute(face3Ds));
        }

        [Fact]
        public void Compute_SeparationBeyondMax_ReturnsNull()
        {
            // Arrange - parallel but 0.80 m apart, past the 0.5 m window.
            List<Face3D> face3Ds = new List<Face3D> { HorizontalQuad(0.0), HorizontalQuad(0.80) };

            // Act & Assert
            Assert.Null(MinPairSeparation.Compute(face3Ds));
        }

        [Fact]
        public void Compute_CoincidentPair_IsIgnoredBelowMinSeparation()
        {
            // Arrange - two coincident faces (separation 0): not a slot gap, below the min bound.
            List<Face3D> face3Ds = new List<Face3D> { HorizontalQuad(0.0), HorizontalQuad(0.0) };

            // Act & Assert
            Assert.Null(MinPairSeparation.Compute(face3Ds));
        }

        [Fact]
        public void Compute_ThinPairWithNonOverlappingRawBoxes_IsNotHiddenByPrefilter()
        {
            // Arrange - two parallel vertical walls 0.08 m apart. Their raw X-extents ([0,0] and
            // [0.08,0.08]) do not overlap, so a naive un-grown bbox prefilter would hide this genuinely
            // close pair; the max-separation-grown prefilter must still surface it (§J.2).
            List<Face3D> face3Ds = new List<Face3D> { VerticalQuad(0.0), VerticalQuad(0.08) };

            // Act
            double? separation = MinPairSeparation.Compute(face3Ds);

            // Assert
            Assert.NotNull(separation);
            Assert.Equal(0.08, separation.Value, 6);
        }

        [Fact]
        public void Compute_LaterallyDisjointParallelPair_IsIgnored()
        {
            // Arrange - two parallel faces 0.08 m apart in Z but occupying disjoint XY regions, so they
            // are not two skins of one slot. Prefilter (grown by 0.5) still rejects a 10 m lateral gap.
            Face3D a = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(1, 1, 0), new Point3D(0, 1, 0));
            Face3D b = TestGeometry.CreatePlanarFace(
                new Point3D(10, 0, 0.08), new Point3D(11, 0, 0.08), new Point3D(11, 1, 0.08), new Point3D(10, 1, 0.08));

            // Act & Assert
            Assert.Null(MinPairSeparation.Compute(new List<Face3D> { a, b }));
        }
    }
}
