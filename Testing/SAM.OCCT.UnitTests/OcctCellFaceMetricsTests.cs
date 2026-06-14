// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Validates the per-face metrics exposed on <see cref="OcctCellFace"/>
    /// (issue #31): area, outward normal, tilt and azimuth. These are derived
    /// from the planar Face3D, so they are exact and need no native library -
    /// the tests run on any platform / CI agent.
    /// </summary>
    public class OcctCellFaceMetricsTests
    {
        private const double Tolerance = 1e-6;

        [Fact]
        public void OcctCellFace_HorizontalUnitQuad_ReportsUnitAreaVerticalNormalZeroOrFlatTilt()
        {
            // Arrange - a 1 x 1 face in the z = 0 plane.
            OcctCellFace cellFace = new OcctCellFace(TestGeometry.CreateUnitQuadFace(), 1);

            // Assert area
            Assert.Equal(1.0, cellFace.Area, 6);

            // Normal is vertical (winding may make it +Z or -Z).
            Vector3D normal = cellFace.Normal;
            Assert.NotNull(normal);
            Assert.Equal(1.0, Math.Abs(normal.Z), 6);

            // A horizontal face has tilt 0 (up) or 180 (down).
            double tilt = cellFace.Tilt;
            Assert.True(Math.Min(tilt, 180.0 - tilt) < Tolerance, string.Format("Unexpected tilt {0} for a horizontal face.", tilt));
        }

        [Fact]
        public void OcctCellFace_VerticalFace_ReportsNinetyDegreeTilt()
        {
            // Arrange - a 1 x 1 face in the x-z plane (y = 0), so its normal is
            // horizontal (+/-Y) and the face is vertical.
            Face3D vertical = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0),
                new Point3D(1, 0, 0),
                new Point3D(1, 0, 1),
                new Point3D(0, 0, 1));
            OcctCellFace cellFace = new OcctCellFace(vertical, 1);

            // Assert - a vertical wall has tilt 90 regardless of normal sign.
            Assert.Equal(90.0, cellFace.Tilt, 4);
        }

        [Fact]
        public void OcctCellFace_NullFace_ReportsNaNAndNullNormal()
        {
            // Arrange
            OcctCellFace cellFace = new OcctCellFace(null, 0);

            // Assert
            Assert.True(double.IsNaN(cellFace.Area));
            Assert.Null(cellFace.Normal);
            Assert.True(double.IsNaN(cellFace.Tilt));
            Assert.True(double.IsNaN(cellFace.Azimuth));
        }
    }
}
