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
    /// Native build of two rooms divided by a party wall with a kink: an angled piece stops just short of the end of a
    /// straight piece, the shape of the Softhouse A/B joint (2.15 degrees, 1.9 mm short). <c>Extend3D</c> grows the
    /// angled piece to the joint and the build must then give two closed rooms.
    /// </summary>
    public class ExtendWallsShallowJunctionIntegrationTests
    {
        private const double Width = 12;
        private const double Depth = 10;
        private const double Height = 3;
        private const double JointX = 6;
        private const double JointY = 5;
        private const double ShortOfJoint = 0.0019;

        private readonly ITestOutputHelper output;

        public ExtendWallsShallowJunctionIntegrationTests(ITestOutputHelper output)
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

        /// <summary>The box and its party wall. <paramref name="lineMiss"/> moves the angled piece sideways, so its line
        /// passes that far from the end of the straight piece.</summary>
        private static List<Panel> KinkedPartyWall(double degrees, double lineMiss)
        {
            double theta = degrees * (System.Math.PI / 180);
            double ux = System.Math.Sin(theta), uy = System.Math.Cos(theta);
            double southX = JointX - (JointY * ux / uy) + lineMiss;
            double tipX = JointX - (ShortOfJoint * ux) + lineMiss, tipY = JointY - (ShortOfJoint * uy);

            return new List<Panel>
            {
                Cap(PanelType.Floor, 0), Cap(PanelType.Roof, Height),
                Wall(0, 0, Width, 0), Wall(Width, 0, Width, Depth), Wall(Width, Depth, 0, Depth), Wall(0, Depth, 0, 0),
                Wall(southX, 0, tipX, tipY), Wall(JointX, JointY, JointX, Depth),
            };
        }

        private List<double> Volumes(List<Panel> panels, string label)
        {
            List<Panel> extended = panels.Extend3D(out _, inputAlreadyClean: true);
            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.AdjacencyCluster(new List<Space>(), extended, out OcctCellComplexResult _, null, Options());
            Assert.NotNull(adjacencyCluster);

            List<double> volumes = adjacencyCluster.GetSpaces().ConvertAll(x => adjacencyCluster.Shell(x)?.Volume() ?? double.NaN).OrderBy(x => x).ToList();
            output.WriteLine(label + " volumes: " + string.Join(", ", volumes.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture))));
            return volumes;
        }

        private static double WestRoomVolume(double degrees)
        {
            double theta = degrees * (System.Math.PI / 180);
            return ((JointX * Depth) - (0.5 * JointY * JointY * System.Math.Tan(theta))) * Height;
        }

        private static void AssertTwoRooms(List<double> volumes, double degrees)
        {
            double west = WestRoomVolume(degrees);
            double east = (Width * Depth * Height) - west;

            Assert.Equal(2, volumes.Count);
            Assert.Equal(System.Math.Min(west, east), volumes[0], 1);
            Assert.Equal(System.Math.Max(west, east), volumes[1], 1);
        }

        [SkippableTheory]
        [InlineData(2.15)] // the Softhouse joint: stops on the joint
        [InlineData(6.0)]  // above the limit: keeps the overshoot
        [InlineData(10.0)]
        public void AdjacencyCluster_KinkedPartyWall_GivesTwoClosedRooms(double degrees)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            List<Panel> panels = KinkedPartyWall(degrees, 0);

            // Act
            List<double> volumes = Volumes(panels, degrees.ToString(CultureInfo.InvariantCulture) + " degrees");

            // Assert
            AssertTwoRooms(volumes, degrees);
        }

        [SkippableTheory]
        [InlineData(0.00001)] // the discriminating case: with the overshoot the rooms come out open (volume NaN)
        [InlineData(0.0001)]
        [InlineData(-0.0001)]
        public void AdjacencyCluster_KinkedPartyWallLineMissesTheJoint_GivesTwoClosedRooms(double lineMiss)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange: at 2.15 degrees a line miss moves the junction 26.6 times as far along the wall (0.1 mm: 2.66 mm).
            const double degrees = 2.15;
            List<Panel> panels = KinkedPartyWall(degrees, lineMiss);

            // Act
            List<double> volumes = Volumes(panels, "line miss " + lineMiss.ToString(CultureInfo.InvariantCulture));

            // Assert
            AssertTwoRooms(volumes, degrees);
        }
    }
}
