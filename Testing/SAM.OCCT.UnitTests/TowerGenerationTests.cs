// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using Xunit;
using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;
using GeometryOcctCreate = SAM.Geometry.OCCT.Create;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Pure-managed tests for the zoned twisted-tower geometry generator (issue #12).
    /// These exercise only SAM.Geometry / SAM.Analytical.OCCT guard logic and shell
    /// construction - no native OCCT call - so they run deterministically on any agent.
    /// The watertight cell-complex behaviour is covered by the native-gated
    /// TowerIntegrationTests.
    /// </summary>
    public class TowerGenerationTests
    {
        [Fact]
        public void Tower_DefaultProfile_BuildsFiveClosedShellsPerFloor()
        {
            // Arrange
            const int floors = 3;

            // Act
            List<Shell> shells = GeometryOcctCreate.Tower(12.0, Math.PI / 6.0, floors);

            // Assert - four perimeter zones plus a core per floor, every shell closed. Each of
            // the six bounding quads is split into two planar triangles (12 faces per shell).
            Assert.NotNull(shells);
            Assert.Equal(5 * floors, shells.Count);
            foreach (Shell shell in shells)
            {
                Assert.NotNull(shell);
                Assert.NotNull(shell.Face3Ds);
                Assert.Equal(12, shell.Face3Ds.Count);
                Assert.True(shell.IsClosed(), "Each generated tower shell must be a closed volume.");
            }
        }

        [Fact]
        public void Tower_NoTwist_BoundingBoxMatchesFootprintAndHeight()
        {
            // Arrange
            const double height = 10.0;
            const double width = 20.0;

            // Act
            List<Shell> shells = GeometryOcctCreate.Tower(height, 0.0, 2, width);

            // Assert
            Assert.NotNull(shells);
            BoundingBox3D boundingBox3D = new BoundingBox3D(BoundingBoxes(shells));
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
            List<Shell> shells = GeometryOcctCreate.Tower(12.0, Math.PI / 2.0, 4, width);

            // Assert
            Assert.NotNull(shells);
            BoundingBox3D boundingBox3D = new BoundingBox3D(BoundingBoxes(shells));
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
            List<Shell> shells = GeometryOcctCreate.Tower(height, twistAngle, floors, width, coreInset);

            // Assert
            Assert.Null(shells);
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

        private static List<BoundingBox3D> BoundingBoxes(IEnumerable<Shell> shells)
        {
            List<BoundingBox3D> result = new List<BoundingBox3D>();
            foreach (Shell shell in shells)
            {
                BoundingBox3D boundingBox3D = shell?.GetBoundingBox();
                if (boundingBox3D != null)
                {
                    result.Add(boundingBox3D);
                }
            }

            return result;
        }
    }
}
