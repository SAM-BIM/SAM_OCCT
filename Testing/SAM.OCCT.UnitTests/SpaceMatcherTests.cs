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
    /// <see cref="SpaceMatcher"/> (docs/CONTROLLED_WORKFLOW_PLAN.md §6): the GUID-based containment,
    /// classification (Matched/Merged/Missing/Split/IncorrectlyBounded/Extra), boundary determinism and
    /// double-height verification, against synthetic box <see cref="Shell"/>s. Pure managed - no OCCT DLL required.
    /// </summary>
    public class SpaceMatcherTests
    {
        private static readonly Construction FloorConstruction = new Construction("Test Floor");

        private static List<Face3D> CreateBoxFaces(double xMin, double yMin, double zMin, double xMax, double yMax, double zMax)
        {
            return new List<Face3D>
            {
                Rect(new Point3D(xMin, yMin, zMin), new Point3D(xMax, yMin, zMin), new Point3D(xMax, yMax, zMin), new Point3D(xMin, yMax, zMin)),
                Rect(new Point3D(xMin, yMin, zMax), new Point3D(xMax, yMin, zMax), new Point3D(xMax, yMax, zMax), new Point3D(xMin, yMax, zMax)),
                Rect(new Point3D(xMin, yMin, zMin), new Point3D(xMax, yMin, zMin), new Point3D(xMax, yMin, zMax), new Point3D(xMin, yMin, zMax)),
                Rect(new Point3D(xMin, yMax, zMin), new Point3D(xMax, yMax, zMin), new Point3D(xMax, yMax, zMax), new Point3D(xMin, yMax, zMax)),
                Rect(new Point3D(xMin, yMin, zMin), new Point3D(xMin, yMax, zMin), new Point3D(xMin, yMax, zMax), new Point3D(xMin, yMin, zMax)),
                Rect(new Point3D(xMax, yMin, zMin), new Point3D(xMax, yMax, zMin), new Point3D(xMax, yMax, zMax), new Point3D(xMax, yMin, zMax))
            };
        }

        private static Face3D Rect(params Point3D[] points)
        {
            return Face3D.Create(new List<IClosedPlanar3D> { new Polygon3D(new List<Point3D>(points)) });
        }

        private static Shell CreateBoxShell(double xMin, double yMin, double zMin, double xMax, double yMax, double zMax)
        {
            return new Shell(CreateBoxFaces(xMin, yMin, zMin, xMax, yMax, zMax));
        }

        private static CellGeometry ToCellGeometry(int index, Shell shell)
        {
            BoundingBox3D box = shell.GetBoundingBox();
            return new CellGeometry(index, shell, box.GetCentroid(), shell.Volume());
        }

        private static Panel CreateCapPanel(double elevation, double halfSize = 50)
        {
            List<Point3D> points = new List<Point3D>
            {
                new Point3D(-halfSize, -halfSize, elevation), new Point3D(halfSize, -halfSize, elevation),
                new Point3D(halfSize, halfSize, elevation), new Point3D(-halfSize, halfSize, elevation)
            };
            return global::SAM.Analytical.Create.Panel(FloorConstruction, PanelType.Floor, Face3D.Create(new List<IClosedPlanar3D> { new Polygon3D(points) }));
        }

        private static ExpectedSpaceSet CreateExpectedSet(IEnumerable<Space> spaces, IEnumerable<double> datums, IEnumerable<Guid> doubleHeightGuids = null)
        {
            List<Panel> panels = datums.Select(x => CreateCapPanel(x)).ToList();
            return ExpectedSpaceSet.Create(spaces, panels, new SpaceMatchOptions { LevelBand = 0.21, LevelGroupBand = 0.21 }, doubleHeightGuids);
        }

        [Fact]
        public void Match_OneToOne_ReturnsMatched()
        {
            // Arrange
            Space space = new Space(Guid.NewGuid(), "North0", new Point3D(5, 5, 5));
            ExpectedSpaceSet expectedSpaceSet = CreateExpectedSet(new[] { space }, new[] { 0.0, 10.0 });
            List<CellGeometry> cells = new List<CellGeometry> { ToCellGeometry(0, CreateBoxShell(0, 0, 0, 10, 10, 10)) };

            // Act
            SpaceMatchReport report = SpaceMatcher.Match(expectedSpaceSet, cells);

            // Assert
            SpaceMatchRecord record = Assert.Single(report.SpaceMatches);
            Assert.Equal(SpaceMatchOutcome.Matched, record.Outcome);
            Assert.True(report.Valid);
        }

        [Fact]
        public void Match_TwoExpectedInOneCell_ReturnsMergedWithPartners()
        {
            // Arrange
            Space a = new Space(Guid.NewGuid(), "West1", new Point3D(3, 3, 5));
            Space b = new Space(Guid.NewGuid(), "West2", new Point3D(7, 7, 5));
            ExpectedSpaceSet expectedSpaceSet = CreateExpectedSet(new[] { a, b }, new[] { 0.0, 10.0 });
            List<CellGeometry> cells = new List<CellGeometry> { ToCellGeometry(0, CreateBoxShell(0, 0, 0, 10, 10, 10)) };

            // Act
            SpaceMatchReport report = SpaceMatcher.Match(expectedSpaceSet, cells);

            // Assert
            Assert.All(report.SpaceMatches, x => Assert.Equal(SpaceMatchOutcome.Merged, x.Outcome));
            SpaceMatchRecord recordA = report.SpaceMatches.First(x => x.Guid == a.Guid);
            Assert.Equal(new[] { b.Guid }, recordA.PartnerGuids);
            Assert.False(report.Valid);
            Assert.Contains(report.ToLines(), x => x.Contains("MERGED") && x.Contains("West1") && x.Contains("West2"));
        }

        [Fact]
        public void Match_ExpectedOutsideAllCells_ReturnsMissingWithNearestDistance()
        {
            // Arrange
            Space space = new Space(Guid.NewGuid(), "East1", new Point3D(100, 100, 5));
            ExpectedSpaceSet expectedSpaceSet = CreateExpectedSet(new[] { space }, new[] { 0.0, 10.0 });
            List<CellGeometry> cells = new List<CellGeometry> { ToCellGeometry(0, CreateBoxShell(0, 0, 0, 10, 10, 10)) };

            // Act
            SpaceMatchReport report = SpaceMatcher.Match(expectedSpaceSet, cells);

            // Assert
            SpaceMatchRecord record = Assert.Single(report.SpaceMatches);
            Assert.Equal(SpaceMatchOutcome.Missing, record.Outcome);
            Assert.Equal(0, record.NearestCellIndex);
            Assert.True(record.NearestDistance > 0);
            Assert.False(report.Valid);
        }

        [Fact]
        public void Match_NullLocation_ReturnsMissing()
        {
            // Arrange
            Space space = new Space(Guid.NewGuid(), "Unplaced", null);
            ExpectedSpaceSet expectedSpaceSet = CreateExpectedSet(new[] { space }, new[] { 0.0, 10.0 });
            List<CellGeometry> cells = new List<CellGeometry> { ToCellGeometry(0, CreateBoxShell(0, 0, 0, 10, 10, 10)) };

            // Act
            SpaceMatchReport report = SpaceMatcher.Match(expectedSpaceSet, cells);

            // Assert
            SpaceMatchRecord record = Assert.Single(report.SpaceMatches);
            Assert.Equal(SpaceMatchOutcome.Missing, record.Outcome);
            Assert.Equal(-1, record.NearestCellIndex);
        }

        [Fact]
        public void Match_CellWithNoExpected_ReturnsExtra()
        {
            // Arrange
            Space space = new Space(Guid.NewGuid(), "North0", new Point3D(5, 5, 5));
            ExpectedSpaceSet expectedSpaceSet = CreateExpectedSet(new[] { space }, new[] { 0.0, 10.0 });
            List<CellGeometry> cells = new List<CellGeometry>
            {
                ToCellGeometry(0, CreateBoxShell(0, 0, 0, 10, 10, 10)),
                ToCellGeometry(1, CreateBoxShell(100, 100, 0, 110, 110, 10))
            };

            // Act
            SpaceMatchReport report = SpaceMatcher.Match(expectedSpaceSet, cells);

            // Assert
            CellMatchRecord extra = Assert.Single(report.CellMatches, x => x.Outcome == SpaceMatchOutcome.Extra);
            Assert.Equal(1, extra.CellIndex);
            Assert.False(report.Valid);
            Assert.Contains(report.ToLines(), x => x.StartsWith("SAM_OCCT_SPACEMATCH: EXTRA cell=1"));
        }

        [Fact]
        public void Match_UndersizedCellWithUnclaimedPartner_ReturnsSplitWithNearestDatum()
        {
            // Arrange - expected span [0,10]; the assigned cell only reaches z=6, an unclaimed same-footprint
            // cell covers [6,10] - the split evidence. SplitElevation snaps the z=6 interior boundary to the
            // nearest known datum (10).
            Space space = new Space(Guid.NewGuid(), "Room", new Point3D(5, 5, 5));
            ExpectedSpaceSet expectedSpaceSet = CreateExpectedSet(new[] { space }, new[] { 0.0, 10.0, 20.0 });
            List<CellGeometry> cells = new List<CellGeometry>
            {
                ToCellGeometry(0, CreateBoxShell(0, 0, 0, 10, 10, 6)),
                ToCellGeometry(1, CreateBoxShell(0, 0, 6, 10, 10, 10))
            };

            // Act
            SpaceMatchReport report = SpaceMatcher.Match(expectedSpaceSet, cells);

            // Assert
            SpaceMatchRecord record = Assert.Single(report.SpaceMatches);
            Assert.Equal(SpaceMatchOutcome.Split, record.Outcome);
            Assert.Equal(new[] { 0, 1 }, record.CellIndices);
            Assert.Equal(10.0, record.SplitElevation, 2);
            Assert.Empty(report.CellMatches.Where(x => x.Outcome == SpaceMatchOutcome.Extra));
            Assert.False(report.Valid);
        }

        [Fact]
        public void Match_SpanOvershootNoPartner_ReturnsIncorrectlyBounded()
        {
            // Arrange - expected span [0,10]; the sole cell overshoots to z=15 with no partner cell to explain it.
            Space space = new Space(Guid.NewGuid(), "Room", new Point3D(5, 5, 5));
            ExpectedSpaceSet expectedSpaceSet = CreateExpectedSet(new[] { space }, new[] { 0.0, 10.0 });
            List<CellGeometry> cells = new List<CellGeometry> { ToCellGeometry(0, CreateBoxShell(0, 0, 0, 10, 10, 15)) };

            // Act
            SpaceMatchReport report = SpaceMatcher.Match(expectedSpaceSet, cells);

            // Assert
            SpaceMatchRecord record = Assert.Single(report.SpaceMatches);
            Assert.Equal(SpaceMatchOutcome.IncorrectlyBounded, record.Outcome);
            Assert.False(report.Valid);
        }

        [Fact]
        public void Match_BoundaryLocation_DeterministicNearestCentreAndBoundaryLine()
        {
            // Arrange - the expected location sits exactly on the shared face between two equidistant cells;
            // the tie breaks to the lower cell index, deterministically.
            Space space = new Space(Guid.NewGuid(), "OnWall", new Point3D(10, 5, 5));
            ExpectedSpaceSet expectedSpaceSet = CreateExpectedSet(new[] { space }, new[] { 0.0, 10.0 });
            List<CellGeometry> cells = new List<CellGeometry>
            {
                ToCellGeometry(0, CreateBoxShell(0, 0, 0, 10, 10, 10)),
                ToCellGeometry(1, CreateBoxShell(10, 0, 0, 20, 10, 10))
            };

            // Act
            SpaceMatchReport report = SpaceMatcher.Match(expectedSpaceSet, cells);

            // Assert
            SpaceMatchRecord record = Assert.Single(report.SpaceMatches);
            Assert.True(record.Boundary);
            Assert.Equal(new[] { 0, 1 }, record.BoundaryCellIndices);
            Assert.Equal(0, record.CellIndices.Single());
            Assert.Equal(SpaceMatchOutcome.Matched, record.Outcome);
            Assert.Contains(report.ToLines(), x => x.StartsWith("SAM_OCCT_SPACEMATCH: BOUNDARY"));
        }

        [Fact]
        public void Match_DoubleHeightOk_MatchedWithTrueFlag()
        {
            // Arrange - a clean double-height cell spanning [0,20] with no intermediate face near datum 10.
            Space space = new Space(Guid.NewGuid(), "West3", new Point3D(5, 5, 10));
            ExpectedSpaceSet expectedSpaceSet = CreateExpectedSet(new[] { space }, new[] { 0.0, 10.0, 20.0 }, new[] { space.Guid });
            List<CellGeometry> cells = new List<CellGeometry> { ToCellGeometry(0, CreateBoxShell(0, 0, 0, 10, 10, 20)) };

            // Act
            SpaceMatchReport report = SpaceMatcher.Match(expectedSpaceSet, cells, new[] { space.Guid });

            // Assert
            SpaceMatchRecord record = Assert.Single(report.SpaceMatches);
            Assert.Equal(SpaceMatchOutcome.Matched, record.Outcome);
            Assert.True(report.DoubleHeightOk[space.Guid]);
            Assert.True(report.Valid);
        }

        [Fact]
        public void Match_DoubleHeightViolatedByIntermediateFace_ReturnsSplit()
        {
            // Arrange - same [0,20] box, but with an extra near-horizontal face at the intermediate datum (10)
            // tucked in a corner far from the space's own location, so containment for the location is unaffected.
            Space space = new Space(Guid.NewGuid(), "West3", new Point3D(8, 8, 10));
            ExpectedSpaceSet expectedSpaceSet = CreateExpectedSet(new[] { space }, new[] { 0.0, 10.0, 20.0 }, new[] { space.Guid });

            List<Face3D> faces = CreateBoxFaces(0, 0, 0, 10, 10, 20);
            faces.Add(Rect(new Point3D(0, 0, 10), new Point3D(3, 0, 10), new Point3D(3, 3, 10), new Point3D(0, 3, 10)));
            Shell shell = new Shell(faces);
            List<CellGeometry> cells = new List<CellGeometry> { ToCellGeometry(0, shell) };

            // Act
            SpaceMatchReport report = SpaceMatcher.Match(expectedSpaceSet, cells, new[] { space.Guid });

            // Assert
            SpaceMatchRecord record = Assert.Single(report.SpaceMatches);
            Assert.Equal(SpaceMatchOutcome.Split, record.Outcome);
            Assert.Equal(10.0, record.SplitElevation, 2);
            Assert.Contains("split at level", record.Detail);
            Assert.False(report.DoubleHeightOk[space.Guid]);
            Assert.False(report.Valid);
        }

        [Fact]
        public void Match_DoubleHeightViolatedByDownwardFacingIntermediateFace_ReturnsSplit()
        {
            // Arrange - like the violated case, but the intermediate face at datum 10 is wound the OTHER way
            // so its normal points DOWN (-Z). A cap's elevation must be read from its position, not a
            // normal-signed offset, or a downward-facing intermediate slab (an intermediate floor's
            // underside - common real geometry) slips past the double-height check as a false ok=true.
            Space space = new Space(Guid.NewGuid(), "West3", new Point3D(8, 8, 10));
            ExpectedSpaceSet expectedSpaceSet = CreateExpectedSet(new[] { space }, new[] { 0.0, 10.0, 20.0 }, new[] { space.Guid });

            List<Face3D> faces = CreateBoxFaces(0, 0, 0, 10, 10, 20);
            // Reversed winding vs the upward case -> normal points -Z.
            faces.Add(Rect(new Point3D(0, 0, 10), new Point3D(0, 3, 10), new Point3D(3, 3, 10), new Point3D(3, 0, 10)));
            Shell shell = new Shell(faces);
            List<CellGeometry> cells = new List<CellGeometry> { ToCellGeometry(0, shell) };

            // Act
            SpaceMatchReport report = SpaceMatcher.Match(expectedSpaceSet, cells, new[] { space.Guid });

            // Assert
            SpaceMatchRecord record = Assert.Single(report.SpaceMatches);
            Assert.Equal(SpaceMatchOutcome.Split, record.Outcome);
            Assert.Equal(10.0, record.SplitElevation, 2);
            Assert.False(report.DoubleHeightOk[space.Guid]);
            Assert.False(report.Valid);
        }

        [Fact]
        public void ToLines_SummaryLine_ReflectsCounts()
        {
            // Arrange
            Space matched = new Space(Guid.NewGuid(), "North0", new Point3D(5, 5, 5));
            Space missing = new Space(Guid.NewGuid(), "East1", new Point3D(100, 100, 5));
            ExpectedSpaceSet expectedSpaceSet = CreateExpectedSet(new[] { matched, missing }, new[] { 0.0, 10.0 });
            List<CellGeometry> cells = new List<CellGeometry> { ToCellGeometry(0, CreateBoxShell(0, 0, 0, 10, 10, 10)) };

            // Act
            SpaceMatchReport report = SpaceMatcher.Match(expectedSpaceSet, cells);
            List<string> lines = report.ToLines();

            // Assert
            Assert.Equal("SAM_OCCT_SPACEMATCH: SUMMARY expected=2 cells=1 matched=1 merged=0 missing=1 split=0 incorrect=0 extra=0", lines[0]);
            Assert.StartsWith("SAM_OCCT_SPACEMATCH: LEVELS groups=2", lines[1]);
        }
    }
}
