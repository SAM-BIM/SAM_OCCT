// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Regression tests for issue #11: Create.AdjacencyCluster(shells, ...) must not
    /// drop sub-minArea faces from an already-closed shell, because removing a
    /// load-bearing face opens the volume and OCCT can no longer build the cell.
    /// Native-gated (auto-skips without SAM.Occt.Native).
    /// </summary>
    public class AdjacencyClusterShellFaceFilterIntegrationTests
    {
        private readonly ITestOutputHelper output;

        public AdjacencyClusterShellFaceFilterIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [SkippableFact]
        public void AdjacencyCluster_ClosedShellWithSubMinAreaFace_KeepsFaceAndBuildsSpace()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a watertight slender column whose caps (0.0009 m^2) sit below
            // the default minArea while leaving a 0.03 m opening if removed. With the
            // old area pre-filter the caps were dropped and the volume opened.
            Shell shell = TestGeometry.CreateSlenderColumnBox(0, 0, 0, 1.0, out double capArea);
            List<Shell> shells = new List<Shell> { shell };

            OcctBuildOptions options = new OcctBuildOptions();
            Assert.True(capArea < SAM.Core.Tolerance.MacroDistance, "cap must be below the default minArea to exercise the filter");

            // Act
            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.AdjacencyCluster(shells, null, out OcctCellComplexResult result, null, options);

            // Assert - before the fix the sliver was filtered out, the shell opened,
            // and OCCT produced no cell (null cluster / zero spaces).
            if (result?.Diagnostics != null)
            {
                foreach (OcctDiagnostic diagnostic in result.Diagnostics)
                {
                    output.WriteLine(diagnostic.ToString());
                }
            }

            Assert.NotNull(adjacencyCluster);
            List<Space> spaces = adjacencyCluster.GetSpaces();
            Assert.NotNull(spaces);
            Assert.Single(spaces);
        }
    }
}
