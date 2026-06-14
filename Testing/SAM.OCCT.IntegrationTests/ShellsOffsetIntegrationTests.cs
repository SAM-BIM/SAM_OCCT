// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare a static Query class.
using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// End-to-end tests for the real native OCCT offset / thick-solid ops
    /// (issue #29). Each test auto-skips (via SkippableFact) when the native
    /// library is absent. Offsetting is failure-prone, so assertions stay
    /// behaviour-level (volume direction / positivity) rather than exact.
    /// </summary>
    public class ShellsOffsetIntegrationTests
    {
        [SkippableFact]
        public void ShellsOffset_OutwardOnUnitBox_GrowsVolume()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a unit box (volume 1).
            List<Shell> shells = new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) };

            // Act - grow the skin outward by 0.25.
            List<Shell> result_Shells = GeometryQuery.ShellsOffset(shells, 0.25, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert - an outward offset enlarges the solid beyond the unit volume.
            Assert.True(result.NativeAvailable);
            Assert.True(result.Success);
            Assert.NotNull(result_Shells);
            Assert.NotEmpty(result.Cells);
            Assert.True(result.Cells.Sum(x => x.Volume) > 1.0);
        }

        [SkippableFact]
        public void ShellsThicken_UnitBox_ProducesPositiveWallVolume()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            List<Shell> shells = new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) };

            // Act - hollow into a 0.1 wall.
            List<Shell> result_Shells = GeometryQuery.ShellsThicken(shells, 0.1, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert - a valid solid with positive volume is produced.
            Assert.True(result.Success);
            Assert.NotNull(result_Shells);
            Assert.NotEmpty(result.Cells);
            Assert.True(result.Cells.Sum(x => x.Volume) > 0);
        }
    }
}
