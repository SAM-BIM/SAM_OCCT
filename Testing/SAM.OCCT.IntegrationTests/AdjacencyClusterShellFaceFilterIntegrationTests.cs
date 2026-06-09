// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Regression tests for issue #11. Create.AdjacencyCluster(shells, ...) must:
    /// (1) keep every shell face for the OCCT volume build - dropping a sub-minArea
    /// face from an already-closed shell opens it and OCCT cannot build the cell; and
    /// (2) still honour minArea as a POST-build panel filter, so tiny sliver faces
    /// are kept for closure but not turned into SAM panels. Native-gated.
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

        [SkippableFact]
        public void AdjacencyCluster_MinAreaAboveSliver_KeepsClosureButExcludesSliverPanel()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - caps 0.0009 m^2, walls 0.03 m^2. minArea sits between them.
            Shell shell = TestGeometry.CreateSlenderColumnBox(0, 0, 0, 1.0, out double capArea);
            const double minArea = 0.005;
            Assert.True(capArea < minArea, "cap must be below minArea");

            // Act - minArea is a post-build panel filter: it must NOT reopen the shell.
            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.AdjacencyCluster(
                new List<Shell> { shell }, null, out OcctCellComplexResult result, null, new OcctBuildOptions(), minArea: minArea);

            // Assert - the space still builds (closure preserved) and no panel is below minArea.
            Assert.NotNull(adjacencyCluster);
            Assert.Single(adjacencyCluster.GetSpaces());

            List<Panel> panels = adjacencyCluster.GetPanels();
            Assert.NotNull(panels);
            Assert.NotEmpty(panels);
            Assert.DoesNotContain(panels, p => { double a = p.GetFace3D()?.GetArea() ?? double.NaN; return !double.IsNaN(a) && a < minArea; });
        }
    }
}
