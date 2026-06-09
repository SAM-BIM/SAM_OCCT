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
    /// End-to-end tests that drive the real native OCCT boolean operations.
    /// Each test auto-skips (via SkippableFact) when the native library is absent,
    /// so the suite is safe to run on agents without OCCT while still providing
    /// real coverage wherever SAM.Occt.Native has been built.
    /// </summary>
    public class ShellsBooleanIntegrationTests
    {
        [SkippableFact]
        public void ShellsUnion_TwoAdjacentBoxes_MergesIntoSingleCell()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - two unit boxes sharing the plane x = 1.
            List<Shell> shells = new List<Shell>
            {
                TestGeometry.CreateUnitBox(0, 0, 0),
                TestGeometry.CreateUnitBox(1, 0, 0)
            };

            // Act
            List<Shell> result_Shells = GeometryQuery.ShellsUnion(shells, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert - a union (OCCT BRepAlgoAPI_Fuse) welds the two adjacent boxes
            // into a single solid and discards the shared internal partition at
            // x = 1, so the result is one cell of the combined volume. With a single
            // cell there is, by definition, no inter-cell face adjacency.
            Assert.True(result.NativeAvailable);
            Assert.True(result.Success);
            Assert.NotNull(result_Shells);
            Assert.Single(result.Cells);
            Assert.All(result.Cells, cell => Assert.True(cell.Volume > 0));
            Assert.Equal(2.0, result.Cells.Sum(x => x.Volume), 6);
            Assert.Empty(result.FaceAdjacencies);
        }

        [SkippableFact]
        public void ShellsIntersection_OverlappingBoxes_ProducesPositiveVolume()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - boxes overlapping in x = [1, 2].
            List<Shell> targets = new List<Shell> { TestGeometry.CreateBox(0, 0, 0, 2, 1, 1) };
            List<Shell> tools = new List<Shell> { TestGeometry.CreateBox(1, 0, 0, 2, 1, 1) };

            // Act
            List<Shell> result_Shells = GeometryQuery.ShellsIntersection(targets, tools, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result_Shells);
            Assert.NotEmpty(result.Cells);
            Assert.True(result.Cells.Sum(x => x.Volume) > 0);
        }

        [SkippableFact]
        public void ShellsDifference_CutterRemovesMaterial_ReducesVolume()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - cut an overlapping box out of a larger one.
            List<Shell> targets = new List<Shell> { TestGeometry.CreateBox(0, 0, 0, 2, 1, 1) };
            List<Shell> cutters = new List<Shell> { TestGeometry.CreateBox(1, 0, 0, 2, 1, 1) };

            // Act
            List<Shell> result_Shells = GeometryQuery.ShellsDifference(targets, cutters, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result_Shells);
            Assert.NotEmpty(result.Cells);
            double remaining = result.Cells.Sum(x => x.Volume);
            Assert.True(remaining > 0 && remaining < 2.0 + global::SAM.Core.Tolerance.Distance);
        }
    }
}
