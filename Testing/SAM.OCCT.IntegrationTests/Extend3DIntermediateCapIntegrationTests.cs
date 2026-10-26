// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
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
    /// Room identity through the chained handoff <c>Clean3D(0.21) -&gt; Extend3D(inputAlreadyClean) -&gt; AdjacencyCluster</c>
    /// on two-storey models where an upper-storey wall has no wall below it. Extend3D must not carry such a wall
    /// through the first floor to the ground floor: the ground-floor room keeps its volume and is not split.
    /// </summary>
    public class Extend3DIntermediateCapIntegrationTests
    {
        private readonly ITestOutputHelper output;

        public Extend3DIntermediateCapIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static OcctBuildOptions Options()
        {
            return new OcctBuildOptions { Tolerance = Tolerance.Distance, FuzzyTolerance = Tolerance.MacroDistance, AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01, ValidateSewCoverage = true };
        }

        private static Panel Wall(params (double X, double Y, double Z)[] points)
        {
            return Analytical.Create.Panel(new Construction("Wall"), PanelType.Wall, new Face3D(new Polygon3D(points.Select(x => new Point3D(x.X, x.Y, x.Z)).ToList())));
        }

        private static Panel Cap(PanelType panelType, double x0, double y0, double x1, double y1, double z)
        {
            return Analytical.Create.Panel(new Construction(panelType.ToString()), panelType, new Face3D(new Polygon3D(new List<Point3D> { new Point3D(x0, y0, z), new Point3D(x1, y0, z), new Point3D(x1, y1, z), new Point3D(x0, y1, z) })));
        }

        private static Panel Rectangle(double x0, double y0, double x1, double y1, double z0, double z1)
        {
            return Wall((x0, y0, z0), (x1, y1, z0), (x1, y1, z1), (x0, y0, z1));
        }

        private List<double> Volumes(List<Panel> panels)
        {
            List<Panel> cleaned = panels.Clean3D(out _, bucketBetweenLevels: 0.21);
            List<Panel> extended = cleaned.Extend3D(out _, bucketBetweenLevels: 0.21, inputAlreadyClean: true, directionalCapGrow: true);
            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.AdjacencyCluster(new List<Space>(), extended, out OcctCellComplexResult result, null, Options());
            Assert.NotNull(adjacencyCluster);

            List<double> volumes = adjacencyCluster.GetSpaces().ConvertAll(x => adjacencyCluster.Shell(x)?.Volume() ?? double.NaN).OrderBy(x => x).ToList();
            output.WriteLine("Volumes: " + string.Join(", ", volumes.Select(x => x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))));
            return volumes;
        }

        [SkippableFact]
        public void AdjacencyCluster_UpperWallSetBackBaseJustBelowFirstFloor_GroundFloorRoomNotSplit()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange: 10 x 6 m ground floor; the upper storey is set back to y 1..6 and its south wall's base sits 25 mm
            // below the first floor (as a mid-surface wall snapped to a slab normalised 25 mm higher).
            List<Panel> panels = new List<Panel>
            {
                Cap(PanelType.Floor, 0, 0, 10, 6, 0), Cap(PanelType.Floor, 0, 0, 10, 6, 3), Cap(PanelType.Roof, 0, 1, 10, 6, 6),
                Rectangle(0, 0, 10, 0, 0, 3), Rectangle(0, 6, 10, 6, 0, 3), Rectangle(0, 0, 0, 6, 0, 3), Rectangle(10, 0, 10, 6, 0, 3),
                Rectangle(0, 1, 10, 1, 2.975, 6), Rectangle(0, 6, 10, 6, 3, 6), Rectangle(0, 1, 0, 6, 3, 6), Rectangle(10, 1, 10, 6, 3, 6),
            };

            // Act
            List<double> volumes = Volumes(panels);

            // Assert: the upper room (10 x 5 x 3) and one ground-floor room (10 x 6 x 3); before the fix the set-back
            // wall reached the ground and split the ground floor into 30 + 150 m3.
            Assert.Equal(2, volumes.Count);
            Assert.Equal(150.0, volumes[0], 0);
            Assert.Equal(180.0, volumes[1], 0);
        }

        [SkippableFact]
        public void AdjacencyCluster_PartyWallOnlyUpstairsWithGroundFloorStub_GroundFloorRoomNotSplit()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange: a 10 x 6 m two-storey box. Upstairs a party wall at x = 5 divides two rooms; on the ground floor
            // only a 2 m stub of it exists (y 0..2), so the ground floor is one open room. Clean3D merges the stub and
            // the upstairs wall into one L-shaped face.
            List<Panel> panels = new List<Panel>
            {
                Cap(PanelType.Floor, 0, 0, 10, 6, 0), Cap(PanelType.Floor, 0, 0, 10, 6, 3), Cap(PanelType.Roof, 0, 0, 10, 6, 6),
                Rectangle(0, 0, 10, 0, 0, 6), Rectangle(0, 6, 10, 6, 0, 6), Rectangle(0, 0, 0, 6, 0, 6), Rectangle(10, 0, 10, 6, 0, 6),
                Rectangle(5, 0, 5, 2, 0, 3), Rectangle(5, 0, 5, 6, 3, 6),
            };

            // Act
            List<double> volumes = Volumes(panels);

            // Assert: two upstairs rooms of 90 m3 and one ground-floor room of 180 m3; before the fix the stub's notch
            // was filled to the ground and the ground floor split into two 90 m3 rooms.
            Assert.Equal(3, volumes.Count);
            Assert.Equal(90.0, volumes[0], 0);
            Assert.Equal(90.0, volumes[1], 0);
            Assert.Equal(180.0, volumes[2], 0);
        }
    }
}
