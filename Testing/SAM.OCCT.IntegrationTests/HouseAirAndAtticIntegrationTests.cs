// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Owner decisions (2026-10-13) on the two-storey house:
    /// <list type="bullet">
    /// <item>the stair void between Hall and Landing is an open boundary, an Air panel, not solid floor;</item>
    /// <item>Hall and Landing stay separate spaces;</item>
    /// <item>the attic is an analytical space marked unconditioned;</item>
    /// <item>result: 10 rooms + 1 unconditioned attic = 11 spaces.</item>
    /// </list>
    /// The fixture's slab is the filled one the GH script produced, so these tests also cover an Air panel
    /// overlapping a solid one: the opening takes precedence.
    /// </summary>
    public class HouseAirAndAtticIntegrationTests
    {
        // Stair void in the UF slab mid-plane (z = 2.9 m): x 5.0..5.95, y 3.0..6.5.
        private const double StairX0 = 5.0, StairX1 = 5.95, StairY0 = 3.0, StairY1 = 6.5, SlabZ = 2.9;

        private static readonly string FixturePath = Path.Combine(System.AppContext.BaseDirectory, "Fixtures", "House-Regression", "House-TwoStorey-23panels.sam");

        private readonly ITestOutputHelper output;

        public HouseAirAndAtticIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static OcctBuildOptions GrasshopperRecipe()
        {
            return new OcctBuildOptions { Tolerance = Tolerance.Distance, FuzzyTolerance = Tolerance.MacroDistance, AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01, ValidateSewCoverage = true };
        }

        private static Panel StairAirPanel()
        {
            Face3D face3D = new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(StairX0, StairY0, SlabZ), new Point3D(StairX1, StairY0, SlabZ), new Point3D(StairX1, StairY1, SlabZ), new Point3D(StairX0, StairY1, SlabZ),
            }));

            return Analytical.Create.Panel(null, PanelType.Air, face3D);
        }

        private static (List<Panel> Panels, List<Space> Seeds) LoadHouseWithStairAir()
        {
            List<IJSAMObject> objects = SAM.Core.Convert.ToSAM(FixturePath) ?? new List<IJSAMObject>();
            List<Panel> panels = objects.OfType<Panel>().ToList();
            panels.Add(StairAirPanel());

            InternalCondition unconditioned = Analytical.Query.DefaultInternalConditionLibrary().GetInternalConditions("S00_Unconditioned").FirstOrDefault();
            List<Space> seeds = objects.OfType<Space>().Select(x => x.Name == "Attic" ? new Space(x) { InternalCondition = unconditioned } : x).ToList();
            return (panels, seeds);
        }

        private static AdjacencyCluster Build(out OcctCellComplexResult result)
        {
            (List<Panel> panels, List<Space> seeds) = LoadHouseWithStairAir();
            return AnalyticalOcctCreate.AdjacencyCluster(seeds, panels, out result, null, GrasshopperRecipe());
        }

        private static Space Named(AdjacencyCluster adjacencyCluster, string name)
        {
            return adjacencyCluster.GetSpaces().Single(x => x.Name == name);
        }

        [SkippableFact]
        public void AdjacencyCluster_HouseWithStairAir_KeepsElevenNamedSpaces()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Act
            AdjacencyCluster adjacencyCluster = Build(out OcctCellComplexResult result);

            // Assert
            Assert.NotNull(adjacencyCluster);
            List<Space> spaces = adjacencyCluster.GetSpaces();
            Assert.Equal(11, spaces.Count);
            Assert.Contains(spaces, x => x.Name == "GF Hall");
            Assert.Contains(spaces, x => x.Name == "UF Landing");
            Assert.Contains(spaces, x => x.Name == "Attic");
            Assert.DoesNotContain(result.Diagnostics, x => x.Severity == OcctDiagnosticSeverity.Error);
        }

        [SkippableFact]
        public void AdjacencyCluster_HouseWithStairAir_AirPanelSeparatesExactlyHallAndLanding()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Act
            AdjacencyCluster adjacencyCluster = Build(out OcctCellComplexResult result);

            // Assert
            List<Panel> airPanels = adjacencyCluster.GetPanels().FindAll(x => x.PanelType == PanelType.Air);
            Assert.NotEmpty(airPanels);
            Assert.Equal((StairX1 - StairX0) * (StairY1 - StairY0), airPanels.Sum(x => x.GetArea()), 3);
            foreach (Panel airPanel in airPanels)
            {
                List<string> names = adjacencyCluster.GetSpaces(airPanel).Select(x => x.Name).OrderBy(x => x).ToList();
                Assert.Equal(new List<string> { "GF Hall", "UF Landing" }, names);
                Assert.Null(airPanel.Construction);
            }

            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_ANALYTICAL_AIR_PANELS");
        }

        [SkippableFact]
        public void AdjacencyCluster_HouseWithStairAir_RestOfSlabStaysPhysicalFloor()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Act
            AdjacencyCluster adjacencyCluster = Build(out _);
            Space hall = Named(adjacencyCluster, "GF Hall");
            Space landing = Named(adjacencyCluster, "UF Landing");

            // Assert: Hall and Landing also share the solid part of the slab, which keeps a physical type and
            // construction. Hall footprint between mid-planes is 2.0 x 7.75 m; the void takes 3.325 m2.
            List<Panel> shared = adjacencyCluster.GetPanels(hall).Intersect(adjacencyCluster.GetPanels(landing)).ToList();
            List<Panel> solid = shared.FindAll(x => x.PanelType != PanelType.Air);
            Assert.NotEmpty(solid);
            Assert.All(solid, x => Assert.NotNull(x.Construction));
            Assert.Equal(2.0 * 7.75 - 3.325, solid.Sum(x => x.GetArea()), 3);

            // Real walls, floors and ceilings are never Air.
            Assert.Equal(shared.Count(x => x.PanelType == PanelType.Air), adjacencyCluster.GetPanels().Count(x => x.PanelType == PanelType.Air));
        }

        [SkippableFact]
        public void AdjacencyCluster_HouseWithStairAir_AtticIsUnconditionedAndAdjacent()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Act
            AdjacencyCluster adjacencyCluster = Build(out _);
            Space attic = Named(adjacencyCluster, "Attic");

            // Assert
            Assert.Equal("S00_Unconditioned", attic.InternalCondition?.Name);
            List<Panel> atticPanels = adjacencyCluster.GetPanels(attic);
            Assert.Contains(atticPanels, x => adjacencyCluster.GetSpaces(x).Count == 2); // ceiling shared with UF rooms
            Assert.Contains(atticPanels, x => adjacencyCluster.GetSpaces(x).Count == 1); // roof
        }

        [SkippableFact]
        public void MapInternalConditions_AtticNamedUnconditioned_GetsUnconditionedCondition()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange: no condition on the seed - only its name, mapped by SAM's default text map.
            (List<Panel> panels, List<Space> seeds) = LoadHouseWithStairAir();
            seeds = seeds.Select(x => x.Name == "Attic" ? new Space("Attic Unconditioned", x.Location) : x).ToList();
            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.AdjacencyCluster(seeds, panels, out _, null, GrasshopperRecipe());

            // Act
            adjacencyCluster.MapInternalConditions(Analytical.Query.DefaultInternalConditionLibrary(), Analytical.Query.DefaultInternalConditionTextMap());

            // Assert
            Assert.Equal("S00_Unconditioned", Named(adjacencyCluster, "Attic Unconditioned").InternalCondition?.Name);
        }

        [SkippableFact]
        public void AdjacencyCluster_HouseWithStairAir_VolumesAndFloorAreasAreRecorded()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Act
            AdjacencyCluster adjacencyCluster = Build(out OcctCellComplexResult result);

            // Assert: the void lies in the slab mid-plane, so closure is unchanged (564.1 m3 in total). Every
            // space carries a positive floor area, and the Landing still has one over the void (SAM counts an
            // Air floor). Values are written to the test output for the record rather than assumed.
            double total = 0;
            foreach (Space space in adjacencyCluster.GetSpaces().OrderBy(x => x.Name))
            {
                Assert.True(space.TryGetValue(SpaceParameter.Volume, out double volume) && volume > 0);
                Assert.True(space.TryGetValue(SpaceParameter.Area, out double area) && area > 0, space.Name);
                total += volume;
                output.WriteLine("{0}: volume {1:0.###} m3, floor area {2:0.###} m2", space.Name, volume, area);
            }

            output.WriteLine("Total {0:0.###} m3", total);
            Assert.InRange(total, 564.1 - 0.5, 564.1 + 0.5);
        }

        [SkippableFact]
        public void AdjacencyCluster_AirPanelCoincidentWithWall_KeepsTheWallPhysical()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange: an Air panel exactly duplicating a real wall (as a solver gap fill can) is not an opening
            // and must never turn the wall into Air.
            (List<Panel> panels, List<Space> seeds) = LoadHouseWithStairAir();
            panels.RemoveAll(x => x.PanelType == PanelType.Air);
            Panel wall = panels.Where(x => x.PanelType == PanelType.Wall).OrderByDescending(x => x.GetArea()).First();
            panels.Add(Analytical.Create.Panel(null, PanelType.Air, wall.GetFace3D()));

            // Act
            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.AdjacencyCluster(seeds, panels, out _, null, GrasshopperRecipe());

            // Assert
            Assert.Equal(11, adjacencyCluster.GetSpaces().Count);
            Assert.DoesNotContain(adjacencyCluster.GetPanels(), x => x.PanelType == PanelType.Air);
        }

        [SkippableFact]
        public void AdjacencyCluster_HouseWithStairAir_IsDeterministic()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Act
            AdjacencyCluster first = Build(out _);
            AdjacencyCluster second = Build(out _);

            // Assert
            string Signature(AdjacencyCluster adjacencyCluster) => string.Join(";", adjacencyCluster.GetPanels().Select(x => string.Format("{0}:{1:0.######}", x.PanelType, x.GetArea())).OrderBy(x => x));
            Assert.Equal(Signature(first), Signature(second));
        }
    }
}
