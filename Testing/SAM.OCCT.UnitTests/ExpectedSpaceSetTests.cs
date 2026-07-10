// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// <see cref="ExpectedSpaceSet"/> (docs/CONTROLLED_WORKFLOW_PLAN.md §6): GUID identity, duplicate/multiline
    /// name handling, and the self-contained level-group inference from input-panel caps. Pure managed
    /// (<see cref="Geometry.OCCT.Solver.LevelFrame"/>/<c>Shell</c> geometry only) - no OCCT DLL required.
    /// </summary>
    public class ExpectedSpaceSetTests
    {
        private static readonly Construction FloorConstruction = new Construction("Test Floor");

        private static Panel CreateCapPanel(double elevation, double halfSize = 5)
        {
            List<Point3D> points = new List<Point3D>
            {
                new Point3D(-halfSize, -halfSize, elevation),
                new Point3D(halfSize, -halfSize, elevation),
                new Point3D(halfSize, halfSize, elevation),
                new Point3D(-halfSize, halfSize, elevation)
            };
            Face3D face3D = Face3D.Create(new List<IClosedPlanar3D> { new Polygon3D(points) });
            return global::SAM.Analytical.Create.Panel(FloorConstruction, PanelType.Floor, face3D);
        }

        private static Space CreateSpace(string name, Point3D location)
        {
            return new Space(Guid.NewGuid(), name, location);
        }

        [Fact]
        public void Create_DuplicateNames_WarnsAndDisambiguatesLabels()
        {
            // Arrange
            Space a = CreateSpace("Office", new Point3D(0, 0, 1));
            Space b = CreateSpace("Office", new Point3D(5, 0, 1));

            // Act
            ExpectedSpaceSet expectedSpaceSet = ExpectedSpaceSet.Create(new[] { a, b }, new List<Panel>());

            // Assert
            Assert.Single(expectedSpaceSet.Warnings);
            Assert.Contains("Office", expectedSpaceSet.Labels.Values);
            Assert.Contains("Office#2", expectedSpaceSet.Labels.Values);
        }

        [Fact]
        public void Create_MultilineName_Throws()
        {
            // Arrange
            Space space = CreateSpace("Office\nSuite", new Point3D(0, 0, 0));

            // Act / Assert
            Assert.Throws<ArgumentException>(() => ExpectedSpaceSet.Create(new[] { space }, new List<Panel>()));
        }

        [Fact]
        public void Create_EmptyName_Throws()
        {
            // Arrange
            Space space = CreateSpace(string.Empty, new Point3D(0, 0, 0));

            // Act / Assert
            Assert.Throws<ArgumentException>(() => ExpectedSpaceSet.Create(new[] { space }, new List<Panel>()));
        }

        [Fact]
        public void Create_DuplicateGuid_Throws()
        {
            // Arrange
            Guid guid = Guid.NewGuid();
            Space a = new Space(guid, "A", new Point3D(0, 0, 0));
            Space b = new Space(guid, "B", new Point3D(5, 0, 0));

            // Act / Assert
            Assert.Throws<ArgumentException>(() => ExpectedSpaceSet.Create(new[] { a, b }, new List<Panel>()));
        }

        [Fact]
        public void Create_FixtureLikeCapElevations_MergesIntoThreeLevelGroups()
        {
            // Arrange - mirrors the 9-space fixture's raw cap elevations (plan §1): 12.24/12.436/15.29/15.473/18.34.
            List<Panel> panels = new List<Panel>
            {
                CreateCapPanel(12.24), CreateCapPanel(12.436),
                CreateCapPanel(15.29), CreateCapPanel(15.473),
                CreateCapPanel(18.34)
            };
            Space space = CreateSpace("North0", new Point3D(0, 0, 13.9));
            SpaceMatchOptions options = new SpaceMatchOptions { LevelGroupBand = 0.21 };

            // Act
            ExpectedSpaceSet expectedSpaceSet = ExpectedSpaceSet.Create(new[] { space }, panels, options);

            // Assert
            Assert.Equal(3, expectedSpaceSet.LevelGroupDatums.Count);
            Assert.Equal(12.24, expectedSpaceSet.LevelGroupDatums[0], 2);
            Assert.Equal(15.29, expectedSpaceSet.LevelGroupDatums[1], 2);
            Assert.Equal(18.34, expectedSpaceSet.LevelGroupDatums[2], 2);
        }

        [Fact]
        public void Create_OrdinarySpace_SpanIsAdjacentDatumPair()
        {
            // Arrange
            List<Panel> panels = new List<Panel> { CreateCapPanel(12.24), CreateCapPanel(15.29), CreateCapPanel(18.34) };
            Space space = CreateSpace("North0", new Point3D(0, 0, 13.9));

            // Act
            ExpectedSpaceSet expectedSpaceSet = ExpectedSpaceSet.Create(new[] { space }, panels);

            // Assert
            double[] span = expectedSpaceSet.LevelSpans[space.Guid];
            Assert.Equal(12.24, span[0], 2);
            Assert.Equal(15.29, span[1], 2);
        }

        [Fact]
        public void Create_DoubleHeightSpace_SpanSkipsIntermediateDatum()
        {
            // Arrange - West3-like: location near the middle datum, flagged double-height.
            List<Panel> panels = new List<Panel> { CreateCapPanel(12.24), CreateCapPanel(15.29), CreateCapPanel(18.34) };
            Space space = CreateSpace("West3", new Point3D(0, 0, 15.25));

            // Act
            ExpectedSpaceSet expectedSpaceSet = ExpectedSpaceSet.Create(new[] { space }, panels, null, new[] { space.Guid });

            // Assert
            double[] span = expectedSpaceSet.LevelSpans[space.Guid];
            Assert.Equal(12.24, span[0], 2);
            Assert.Equal(18.34, span[1], 2);
        }

        [Fact]
        public void ToSeedSpaces_ReturnsExpectedSpacesAsCopy()
        {
            // Arrange
            Space space = CreateSpace("North0", new Point3D(0, 0, 13.9));
            ExpectedSpaceSet expectedSpaceSet = ExpectedSpaceSet.Create(new[] { space }, new List<Panel>());

            // Act
            List<Space> seeds = expectedSpaceSet.ToSeedSpaces();

            // Assert
            Assert.Single(seeds);
            Assert.Equal(space.Guid, seeds[0].Guid);
        }
    }
}
