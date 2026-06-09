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
    /// Native-gated tests for the zoned twisted-tower component (issue #12). They drive the
    /// real OCCT cell-complex pipeline (BOPAlgo_MakerVolume over the generated tower faces),
    /// so each auto-skips when SAM.Occt.Native is unavailable. They assert the watertight,
    /// five-zone, multi-level AdjacencyCluster the issue calls for: one space per zone per
    /// floor, shared internal boundaries between zones, orientation/floor zone naming, and -
    /// even when the facade twists - strictly vertical internal separations typed
    /// WallInternal (or Air).
    /// </summary>
    public class TowerIntegrationTests
    {
        private const double VerticalityTolerance = 1e-6;

        private readonly ITestOutputHelper output;

        public TowerIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [SkippableTheory]
        [InlineData(0.0)]                   // straight (untwisted) tower
        [InlineData(System.Math.PI / 12.0)] // gently twisted tower
        public void Tower_MultiFloor_BuildsWatertightZonedAdjacencyCluster(double twistAngle)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            const int floors = 2;
            const int expectedSpaces = 5 * floors; // four perimeter zones + core, per floor

            // Act
            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.Tower(8.0, twistAngle, floors, out OcctCellComplexResult result);

            // Assert
            WriteDiagnostics(result);
            Assert.NotNull(adjacencyCluster);

            List<Space> spaces = adjacencyCluster.GetSpaces();
            Assert.NotNull(spaces);
            Assert.Equal(expectedSpaces, spaces.Count);

            // Watertight stacking/partitioning means the zones share internal boundaries.
            Assert.NotNull(result.FaceAdjacencies);
            Assert.NotEmpty(result.FaceAdjacencies);

            List<Panel> panels = adjacencyCluster.GetPanels();
            Assert.NotNull(panels);
            Assert.NotEmpty(panels);

            // Zone naming: a core per floor, and every floor index present.
            List<string> names = spaces.Select(x => x?.Name).Where(x => !string.IsNullOrEmpty(x)).ToList();
            Assert.Equal(expectedSpaces, names.Count);
            Assert.Equal(floors, names.Count(x => x.Contains("Zone_CORE")));
            for (int floor = 0; floor < floors; floor++)
            {
                string prefix = string.Format("Floor_{0}_", floor);
                Assert.Contains(names, x => x.StartsWith(prefix));
            }

            // Internal separations (panels shared by two spaces) must never tilt with the
            // facade: vertical separation walls typed WallInternal (or Air), horizontal
            // ones are the internal floor plates.
            int internalWallCount = 0;
            foreach (Panel panel in panels)
            {
                List<Space> relatedSpaces = adjacencyCluster.GetRelatedObjects<Space>(panel);
                if (relatedSpaces == null || relatedSpaces.Count < 2)
                {
                    continue;
                }

                Vector3D normal = panel.GetFace3D()?.GetPlane()?.Normal?.Unit;
                Assert.NotNull(normal);

                double z = System.Math.Abs(normal.Z);
                bool vertical = z < VerticalityTolerance;
                bool horizontal = z > 1 - VerticalityTolerance;
                Assert.True(vertical || horizontal, string.Format("Internal panel {0} must be vertical or horizontal, but |normal.Z| = {1}.", panel.Name, z));

                if (vertical)
                {
                    internalWallCount++;
                    Assert.True(panel.PanelType == PanelType.WallInternal || panel.PanelType == PanelType.Air, string.Format("Internal separation wall {0} must be WallInternal or Air, but is {1}.", panel.Name, panel.PanelType));
                }
            }

            Assert.True(internalWallCount > 0, "Expected vertical internal separation walls between the tower zones.");
        }

        private void WriteDiagnostics(OcctCellComplexResult result)
        {
            if (result?.Diagnostics == null)
            {
                output.WriteLine("(no OCCT diagnostics)");
                return;
            }

            foreach (OcctDiagnostic diagnostic in result.Diagnostics)
            {
                output.WriteLine(diagnostic.ToString());
            }
        }
    }
}
