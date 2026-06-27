// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare a static Query class.
using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// End-to-end tests that drive the real native OCCT BRepExtrema distance
    /// primitive (issue #28). Each test auto-skips (via SkippableFact) when the
    /// native library is absent, so the suite is safe on agents without OCCT
    /// while still exercising the distance path wherever SAM.Occt.Native is
    /// built.
    /// </summary>
    public class ShellsDistanceIntegrationTests
    {
        [SkippableFact]
        public void ShellsDistance_GapBetweenBoxes_ReportsGapDistance()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a unit box at the origin and a unit box starting at
            // x = 1.5, leaving a 0.5 gap along x.
            List<Shell> shells = new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) };
            List<Shell> otherShells = new List<Shell> { TestGeometry.CreateBox(1.5, 0, 0, 1, 1, 1) };

            // Act
            double? distance = GeometryQuery.ShellsDistance(shells, otherShells, out OcctCellComplexResult result, out Point3D pointA, out Point3D pointB, new OcctBuildOptions());

            // Assert
            Assert.True(result.NativeAvailable);
            Assert.True(result.Success);
            Assert.NotNull(distance);
            Assert.Equal(0.5, distance.Value, 6);
            Assert.NotNull(pointA);
            Assert.NotNull(pointB);
            // Closest points sit on the facing walls: x = 1 on the first box and
            // x = 1.5 on the second.
            Assert.Equal(1.0, pointA.X, 6);
            Assert.Equal(1.5, pointB.X, 6);
        }

        [SkippableFact]
        public void ShellsDistance_TouchingBoxes_ReportsZero()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - two unit boxes sharing the plane x = 1.
            List<Shell> shells = new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) };
            List<Shell> otherShells = new List<Shell> { TestGeometry.CreateUnitBox(1, 0, 0) };

            // Act
            double? distance = GeometryQuery.ShellsDistance(shells, otherShells, out OcctCellComplexResult result, out Point3D pointA, out Point3D pointB, new OcctBuildOptions());

            // Assert - touching shapes have zero separation.
            Assert.True(result.Success);
            Assert.NotNull(distance);
            Assert.Equal(0.0, distance.Value, 6);
        }
    }
}
