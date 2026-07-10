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
    /// <see cref="SeparatorPanelFinder"/> (docs/CONTROLLED_WORKFLOW_PLAN.md §6 "Missing-separator analysis"):
    /// candidate-present-but-unused, lateral-near-miss (partial), and genuinely-absent findings for a merged
    /// pair. Pure managed - no OCCT DLL required.
    /// </summary>
    public class SeparatorPanelFinderTests
    {
        private static readonly Construction WallConstruction = new Construction("Test Wall");

        private static readonly SpaceMatchOptions Options = new SpaceMatchOptions { LevelBand = 0.21, LateralMargin = 0.5 };

        private static readonly double[] SpanA = new double[] { 0.0, 10.0 };
        private static readonly double[] SpanB = new double[] { 0.0, 10.0 };

        private static Space SpaceA()
        {
            return new Space(Guid.NewGuid(), "A", new Point3D(0, 5, 5));
        }

        private static Space SpaceB()
        {
            return new Space(Guid.NewGuid(), "B", new Point3D(10, 5, 5));
        }

        private static Panel CreateWallPanel(double x, double yMin, double yMax, double zMin, double zMax)
        {
            List<Point3D> points = new List<Point3D>
            {
                new Point3D(x, yMin, zMin), new Point3D(x, yMax, zMin), new Point3D(x, yMax, zMax), new Point3D(x, yMin, zMax)
            };
            return global::SAM.Analytical.Create.Panel(WallConstruction, PanelType.Wall, Face3D.Create(new List<IClosedPlanar3D> { new Polygon3D(points) }));
        }

        [Fact]
        public void Find_FullHeightLaterallyCoveringPanel_ReturnsCandidate()
        {
            // Arrange - a full-height wall at x=5 spanning y=[0,10] fully covers the pair's midpoint (5,5,5).
            Space a = SpaceA();
            Space b = SpaceB();
            List<Panel> panels = new List<Panel> { CreateWallPanel(5, 0, 10, 0, 10) };

            // Act
            List<SeparatorFinding> findings = SeparatorPanelFinder.Find(a, SpanA, b, SpanB, panels, Options);

            // Assert
            SeparatorFinding finding = Assert.Single(findings);
            Assert.Equal(SeparatorFindingKind.Candidate, finding.Kind);
            Assert.Equal(panels[0].Guid, finding.PanelGuid);
        }

        [Fact]
        public void Find_NoPanels_ReturnsAbsent()
        {
            // Arrange
            Space a = SpaceA();
            Space b = SpaceB();

            // Act
            List<SeparatorFinding> findings = SeparatorPanelFinder.Find(a, SpanA, b, SpanB, new List<Panel>(), Options);

            // Assert
            SeparatorFinding finding = Assert.Single(findings);
            Assert.Equal(SeparatorFindingKind.Absent, finding.Kind);
            Assert.Null(finding.PanelGuid);
        }

        [Fact]
        public void Find_LateralNearMiss_ReturnsPartial()
        {
            // Arrange - full-height wall at x=5 but its lateral extent (y=[5.3,10]) just misses the pair's
            // midpoint (5,5,5) while staying within LateralMargin (0.5) of its own bounds.
            Space a = SpaceA();
            Space b = SpaceB();
            List<Panel> panels = new List<Panel> { CreateWallPanel(5, 5.3, 10, 0, 10) };

            // Act
            List<SeparatorFinding> findings = SeparatorPanelFinder.Find(a, SpanA, b, SpanB, panels, Options);

            // Assert
            SeparatorFinding finding = Assert.Single(findings);
            Assert.Equal(SeparatorFindingKind.Partial, finding.Kind);
            Assert.Equal(panels[0].Guid, finding.PanelGuid);
        }

        [Fact]
        public void Find_PartialHeightFragment_ReturnsAbsent()
        {
            // Arrange - a wall only covering a narrow z band (not the full expected [0,10] height) is not a
            // usable separator; the scan reports it as genuinely absent, not a candidate.
            Space a = SpaceA();
            Space b = SpaceB();
            List<Panel> panels = new List<Panel> { CreateWallPanel(5, 0, 10, 4.5, 5.5) };

            // Act
            List<SeparatorFinding> findings = SeparatorPanelFinder.Find(a, SpanA, b, SpanB, panels, Options);

            // Assert
            SeparatorFinding finding = Assert.Single(findings);
            Assert.Equal(SeparatorFindingKind.Absent, finding.Kind);
        }

        [Fact]
        public void Find_WallNotBetweenLocations_IsIgnored()
        {
            // Arrange - a wall on the far side of B (x=15) does not sit strictly between A (x=0) and B (x=10).
            Space a = SpaceA();
            Space b = SpaceB();
            List<Panel> panels = new List<Panel> { CreateWallPanel(15, 0, 10, 0, 10) };

            // Act
            List<SeparatorFinding> findings = SeparatorPanelFinder.Find(a, SpanA, b, SpanB, panels, Options);

            // Assert
            SeparatorFinding finding = Assert.Single(findings);
            Assert.Equal(SeparatorFindingKind.Absent, finding.Kind);
        }
    }
}
