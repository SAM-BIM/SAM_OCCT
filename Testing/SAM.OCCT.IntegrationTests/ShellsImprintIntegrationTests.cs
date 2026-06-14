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
    /// End-to-end tests that drive the real native OCCT General Fuse imprint
    /// (issue #27). Each test auto-skips (via SkippableFact) when the native
    /// library is absent, so the suite is safe on agents without OCCT while
    /// still exercising the imprint + decode path wherever SAM.Occt.Native is
    /// built.
    /// </summary>
    public class ShellsImprintIntegrationTests
    {
        [SkippableFact]
        public void ShellsImprint_PartiallyTouchingBoxes_SplitsSharedFaceIntoMatchingAdjacency()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a unit box and a smaller box whose face at x = 1 covers
            // only half (y = [0, 0.5]) of the unit box's x = 1 face. Without
            // imprinting the two boundary faces have different extents, so they
            // never key-match and decode reports no adjacency between the cells.
            List<Shell> shells = new List<Shell>
            {
                TestGeometry.CreateUnitBox(0, 0, 0),
                TestGeometry.CreateBox(1, 0, 0, 1, 0.5, 1)
            };

            // Act
            List<Shell> result_Shells = GeometryQuery.ShellsImprint(shells, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert - imprinting (BOPAlgo_Builder / General Fuse) splits the unit
            // box's x = 1 face into a y = [0, 0.5] sub-face matching the smaller
            // box plus the remainder, so the two volumes stay distinct (one cell
            // each, total 1.5 m³) but now share a key-matched face adjacency.
            Assert.True(result.NativeAvailable);
            Assert.True(result.Success);
            Assert.NotNull(result_Shells);
            Assert.Equal(2, result.Cells.Count);
            Assert.All(result.Cells, cell => Assert.True(cell.Volume > 0));
            Assert.Equal(1.5, result.Cells.Sum(x => x.Volume), 6);
            Assert.NotEmpty(result.FaceAdjacencies);
        }

        [SkippableFact]
        public void ShellsImprint_SingleShell_PassesThroughAsOneCell()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a single solid has no neighbour to imprint against.
            List<Shell> shells = new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) };

            // Act
            List<Shell> result_Shells = GeometryQuery.ShellsImprint(shells, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert - pass-through: one cell, unit volume, no adjacencies.
            Assert.True(result.Success);
            Assert.NotNull(result_Shells);
            Assert.Single(result.Cells);
            Assert.Equal(1.0, result.Cells.Sum(x => x.Volume), 6);
            Assert.Empty(result.FaceAdjacencies);
        }
    }
}
