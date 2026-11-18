// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Native build, through managed Clean3D and Extend3D, of two rooms divided by a party wall with a genuine bend - the Softhouse A/B
    /// shape: a heavier angled piece (2.15 degrees) up to the joint, then the straight line in two lighter pieces. Clean3D's wall-abut rule
    /// (b) would turn the first straight piece onto the angled one and leave a 94 mm step to the second, so the rooms leak into each other;
    /// the bend veto keeps it on its line and the build must give the two rooms, closed, each with its own volume.
    /// </summary>
    public class BendVetoTwoRoomIntegrationTests
    {
        private const double Width = 12;
        private const double Depth = 10;
        private const double Height = 3;
        private const double JointX = 6;
        private const double JointY = 5;
        private const double SplitY = 7.5;
        private const double Degrees = 2.15;

        private readonly ITestOutputHelper output;

        public BendVetoTwoRoomIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static OcctBuildOptions Options()
        {
            return new OcctBuildOptions { Tolerance = Tolerance.Distance, FuzzyTolerance = Tolerance.MacroDistance, AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01, ValidateSewCoverage = true };
        }

        private static Panel Cap(PanelType panelType, double z)
        {
            return Analytical.Create.Panel(new Construction(panelType.ToString()), panelType, new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(0, 0, z), new Point3D(Width, 0, z), new Point3D(Width, Depth, z), new Point3D(0, Depth, z),
            })));
        }

        private static Panel Wall(double x0, double y0, double x1, double y1)
        {
            return Analytical.Create.Panel(new Construction("Wall"), PanelType.Wall, new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(x0, y0, 0), new Point3D(x1, y1, 0), new Point3D(x1, y1, Height), new Point3D(x0, y0, Height),
            })));
        }

        /// <summary>The box, the bent party wall and the Clean3D weights: the angled piece is the heaviest party piece, the first straight
        /// piece the lightest.</summary>
        private static (List<Panel> Panels, List<double> Weights) TwoRooms()
        {
            double southX = JointX - (JointY * System.Math.Tan(Degrees * (System.Math.PI / 180)));
            List<Panel> panels = new List<Panel>
            {
                Cap(PanelType.Floor, 0), Cap(PanelType.Roof, Height),
                Wall(0, 0, Width, 0), Wall(Width, 0, Width, Depth), Wall(Width, Depth, 0, Depth), Wall(0, Depth, 0, 0),
                Wall(southX, 0, JointX, JointY), Wall(JointX, JointY, JointX, SplitY), Wall(JointX, SplitY, JointX, Depth),
            };

            List<double> weights = new List<double> { 1, 1, 1, 1, 1, 1, 0.9, 0.5, 0.8 };
            return (panels, weights);
        }

        private static double WestRoomVolume()
        {
            return ((JointX * Depth) - (0.5 * JointY * JointY * System.Math.Tan(Degrees * (System.Math.PI / 180)))) * Height;
        }

        [SkippableFact]
        public void AdjacencyCluster_PartyWallWithABend_GivesTwoSeparateClosedRooms()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            (List<Panel> panels, List<double> weights) = TwoRooms();
            Point3D west = new Point3D(JointX / 2, Depth / 2, Height / 2), east = new Point3D((JointX + Width) / 2, Depth / 2, Height / 2);
            double westVolume = WestRoomVolume(), eastVolume = (Width * Depth * Height) - westVolume;

            // Act
            List<Panel> clean = panels.Clean3D(out List<string> _, weights: weights);
            List<Panel> extended = clean.Extend3D(out List<string> _, inputAlreadyClean: true);
            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.AdjacencyCluster(new List<Space>(), extended, out OcctCellComplexResult _, null, Options());

            // Assert
            Assert.NotNull(adjacencyCluster);
            List<Shell> shells = adjacencyCluster.GetSpaces().ConvertAll(x => adjacencyCluster.Shell(x));
            output.WriteLine("rooms: " + string.Join(", ", shells.Select(x => (x?.Volume() ?? double.NaN).ToString("0.###", CultureInfo.InvariantCulture) + (x != null && x.IsClosed(Tolerance.MacroDistance) ? " closed" : " open"))));

            Assert.Equal(2, shells.Count);
            Assert.All(shells, x => Assert.True(x != null && x.IsClosed(Tolerance.MacroDistance), "a room is not closed"));
            Shell westRoom = Assert.Single(shells, x => x.Inside(west));
            Shell eastRoom = Assert.Single(shells, x => x.Inside(east));
            Assert.NotSame(westRoom, eastRoom);
            Assert.False(westRoom.Inside(east));
            Assert.False(eastRoom.Inside(west));
            Assert.Equal(westVolume, westRoom.Volume(), 2);
            Assert.Equal(eastVolume, eastRoom.Volume(), 2);
        }
    }
}
