// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;
using GeometryOcctCreate = SAM.Geometry.OCCT.Create;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Pure-managed tests for the zoned twisted-tower geometry generator (issue #12).
    /// These exercise only SAM.Geometry / SAM.Analytical.OCCT guard logic and face
    /// construction - no native OCCT call - so they run deterministically on any agent.
    /// The watertight cell-complex behaviour is covered by the native-gated
    /// TowerIntegrationTests.
    /// </summary>
    public class TowerGenerationTests
    {
        private const double VerticalityTolerance = 1e-9;

        [Fact]
        public void Tower_Untwisted_BuildsExpectedFaceSet()
        {
            // Arrange
            const int floors = 3;

            // Act
            List<Face3D> face3Ds = GeometryOcctCreate.Tower(12.0, 0.0, floors);

            // Assert - per floor: 4 facade quads + 4 core walls + 4 partitions; plus floors+1 plates.
            Assert.NotNull(face3Ds);
            Assert.DoesNotContain(face3Ds, x => x == null);
            Assert.Equal(12 * floors + floors + 1, face3Ds.Count);
        }

        [Fact]
        public void Tower_Twisted_InternalWallsStayVertical()
        {
            // Arrange - a clearly twisted tower: 30 degrees over 3 floors.
            const int floors = 3;

            // Act
            List<Face3D> face3Ds = GeometryOcctCreate.Tower(12.0, Math.PI / 6.0, floors);

            // Assert - facade quads are triangulated (2 each), internals stay single quads:
            // per floor 8 facade triangles + 4 core walls + 4 partitions; plus floors+1 plates.
            Assert.NotNull(face3Ds);
            Assert.Equal(16 * floors + floors + 1, face3Ds.Count);

            // Internal separations (core walls + diagonal partitions) must be VERTICAL even
            // when the facade twists; only the facade triangles may tilt; plates horizontal.
            int verticalCount = face3Ds.Count(x => IsVertical(x));
            int horizontalCount = face3Ds.Count(x => IsHorizontal(x));
            int tiltedCount = face3Ds.Count - verticalCount - horizontalCount;

            Assert.Equal(8 * floors, verticalCount);   // 4 core walls + 4 partitions per floor
            Assert.Equal(floors + 1, horizontalCount); // floor plates
            Assert.Equal(8 * floors, tiltedCount);     // facade triangles only
        }

        [Fact]
        public void Tower_NoTwist_AllFacesVerticalOrHorizontal()
        {
            // Act
            List<Face3D> face3Ds = GeometryOcctCreate.Tower(10.0, 0.0, 2);

            // Assert - an untwisted prism has no reason to contain any tilted face.
            Assert.NotNull(face3Ds);
            Assert.DoesNotContain(face3Ds, x => !IsVertical(x) && !IsHorizontal(x));
        }

        [Fact]
        public void Tower_NoTwist_BoundingBoxMatchesFootprintAndHeight()
        {
            // Arrange
            const double height = 10.0;
            const double width = 20.0;

            // Act
            List<Face3D> face3Ds = GeometryOcctCreate.Tower(height, 0.0, 2, width);

            // Assert
            Assert.NotNull(face3Ds);
            BoundingBox3D boundingBox3D = new BoundingBox3D(BoundingBoxes(face3Ds));
            Assert.Equal(width, boundingBox3D.Max.X - boundingBox3D.Min.X, 3);
            Assert.Equal(width, boundingBox3D.Max.Y - boundingBox3D.Min.Y, 3);
            Assert.Equal(height, boundingBox3D.Max.Z - boundingBox3D.Min.Z, 3);
        }

        [Fact]
        public void Tower_Twisted_WidensPlanFootprint()
        {
            // Arrange - a 90 degree total twist rotates upper storeys so the axis-aligned
            // footprint of the whole tower is wider than a single square plan.
            const double width = 20.0;

            // Act
            List<Face3D> face3Ds = GeometryOcctCreate.Tower(12.0, Math.PI / 2.0, 4, width);

            // Assert
            Assert.NotNull(face3Ds);
            BoundingBox3D boundingBox3D = new BoundingBox3D(BoundingBoxes(face3Ds));
            Assert.True(boundingBox3D.Max.X - boundingBox3D.Min.X > width + 1e-3, "A twisted tower's footprint must exceed the un-rotated plan width.");
        }

        [Theory]
        [InlineData(0.0, 0.0, 3, 20.0, 5.0)]   // non-positive height
        [InlineData(-5.0, 0.0, 3, 20.0, 5.0)]  // negative height
        [InlineData(10.0, 0.0, 0, 20.0, 5.0)]  // no floors
        [InlineData(10.0, 0.0, 3, 20.0, 10.0)] // core inset removes the whole plan
        [InlineData(10.0, 0.0, 3, 0.0, 5.0)]   // zero width
        public void Tower_InvalidArguments_ReturnsNull(double height, double twistAngle, int floors, double width, double coreInset)
        {
            // Act
            List<Face3D> face3Ds = GeometryOcctCreate.Tower(height, twistAngle, floors, width, coreInset);

            // Assert
            Assert.Null(face3Ds);
        }

        [Fact]
        public void Tower_AnalyticalInvalidArguments_ReturnsNullWithDiagnostic()
        {
            // Act - invalid arguments are rejected before any native call.
            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.Tower(0.0, 0.0, 0, out OcctCellComplexResult result);

            // Assert
            Assert.Null(adjacencyCluster);
            Assert.NotNull(result);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_TOWER_INPUT_INVALID" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        private static bool IsVertical(Face3D face3D)
        {
            Vector3D normal = face3D?.GetPlane()?.Normal?.Unit;
            return normal != null && Math.Abs(normal.Z) < VerticalityTolerance;
        }

        private static bool IsHorizontal(Face3D face3D)
        {
            Vector3D normal = face3D?.GetPlane()?.Normal?.Unit;
            return normal != null && Math.Abs(normal.Z) > 1 - VerticalityTolerance;
        }

        private static List<BoundingBox3D> BoundingBoxes(IEnumerable<Face3D> face3Ds)
        {
            List<BoundingBox3D> result = new List<BoundingBox3D>();
            foreach (Face3D face3D in face3Ds)
            {
                BoundingBox3D boundingBox3D = face3D?.GetBoundingBox();
                if (boundingBox3D != null)
                {
                    result.Add(boundingBox3D);
                }
            }

            return result;
        }
    }
}
