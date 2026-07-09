// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="Solve3DReport"/> - the Grasshopper-facing solve snapshot DTO
    /// (Phase 8a). Pure managed: no native OCCT DLL required.
    /// </summary>
    public class Solve3DReportTests
    {
        [Fact]
        public void Constructor_NullCollections_DefaultToEmptyNotNull()
        {
            // Act
            Solve3DReport report = new Solve3DReport(
                rawAdopted: true, signature: null, rawAttemptSignature: null, diagnostics: null,
                sourceMap: null, sources: null, cells: null, cellRoles: null, nakedWires: null,
                cleanFace3Ds: null, levelFrames: null, nativeResolved: false, resolvedCellCount: 0);

            // Assert
            Assert.NotNull(report.Diagnostics);
            Assert.NotNull(report.SourceMap);
            Assert.Empty(report.Sources);
            Assert.Empty(report.Cells);
            Assert.Empty(report.CellRoles);
            Assert.Empty(report.NakedWires);
            Assert.Empty(report.CleanFace3Ds);
            Assert.Empty(report.LevelFrames);
        }

        [Fact]
        public void Constructor_Always_PopulatesClosureReportText()
        {
            // Act
            Solve3DReport report = new Solve3DReport(
                rawAdopted: true, signature: null, rawAttemptSignature: null, diagnostics: null,
                sourceMap: null, sources: null, cells: null, cellRoles: null, nakedWires: null,
                cleanFace3Ds: null, levelFrames: null, nativeResolved: true, resolvedCellCount: 5);

            // Assert
            Assert.Contains("Adopted path: Raw", report.ClosureReportText);
        }

        [Fact]
        public void FormatSourceMap_DelegatesToSharedFormatter()
        {
            // Arrange
            SourceMap sourceMap = new SourceMap();
            sourceMap.RecordFabricated(new FaceKey(1), Provenance.GapFill);
            Solve3DReport report = new Solve3DReport(
                rawAdopted: false, signature: null, rawAttemptSignature: null, diagnostics: null,
                sourceMap: sourceMap, sources: null, cells: null, cellRoles: null, nakedWires: null,
                cleanFace3Ds: null, levelFrames: null, nativeResolved: false, resolvedCellCount: 0);

            // Act
            List<string> lines = report.FormatSourceMap();

            // Assert
            Assert.Single(lines);
            Assert.StartsWith("fabricated ->", lines[0]);
        }

        [Fact]
        public void FormatCells_DelegatesToSharedFormatter()
        {
            // Arrange
            List<SolverCell> cells = new List<SolverCell> { new SolverCell(0, 3.0, null, null) };
            Solve3DReport report = new Solve3DReport(
                rawAdopted: false, signature: null, rawAttemptSignature: null, diagnostics: null,
                sourceMap: null, sources: null, cells: cells, cellRoles: null, nakedWires: null,
                cleanFace3Ds: null, levelFrames: null, nativeResolved: false, resolvedCellCount: 1);

            // Act
            List<string> lines = report.FormatCells();

            // Assert
            Assert.Single(lines);
            Assert.Contains("not classified", lines[0]);
        }

        [Fact]
        public void Constructor_NullExtendRecords_DefaultsToEmptyNotNull()
        {
            // Act - the trailing optional extendRecords param defaults to an empty (never null) list.
            Solve3DReport report = new Solve3DReport(
                rawAdopted: true, signature: null, rawAttemptSignature: null, diagnostics: null,
                sourceMap: null, sources: null, cells: null, cellRoles: null, nakedWires: null,
                cleanFace3Ds: null, levelFrames: null, nativeResolved: false, resolvedCellCount: 0);

            // Assert
            Assert.NotNull(report.ExtendRecords);
            Assert.Empty(report.ExtendRecords);
            Assert.Empty(report.FormatExtendRecords());
        }

        [Fact]
        public void FormatExtendRecords_RecordsAndSourcesSupplied_RoundTripsThroughReport()
        {
            // Arrange - the E3 round-trip: records + sources carried on the report format to the coded lines,
            // with the panel Guid resolved from the report's own Sources.
            Panel wall = MakeWallPanel();
            List<Panel> sources = new List<Panel> { wall };
            List<ExtendRecord> records = new List<ExtendRecord>
            {
                new ExtendRecord(0, 0, ExtendOperationKind.Top, 2.5, 3.05, "elevation",
                    new Point3D(2, 0, 2.5), new Point3D(2, 0, 3.05), 1, 1, "cap-scalar", "cap z=3", 0.05, false)
            };
            Solve3DReport report = new Solve3DReport(
                rawAdopted: false, signature: null, rawAttemptSignature: null, diagnostics: null,
                sourceMap: null, sources: sources, cells: null, cellRoles: null, nakedWires: null,
                cleanFace3Ds: null, levelFrames: null, nativeResolved: false, resolvedCellCount: 0,
                extendRecords: records);

            // Act
            List<string> lines = report.FormatExtendRecords();
            List<Segment3D> preview = report.ExtendPreviewSegment3Ds();

            // Assert
            Assert.Single(lines);
            Assert.StartsWith("SAM_OCCT_EXTEND3D_PANEL:", lines[0]);
            Assert.Contains(wall.Guid.ToString(), lines[0]);
            Assert.Contains("top", lines[0]);
            Assert.Single(preview); // one moved-edge segment
        }

        /// <summary>Axis-aligned wall rectangle in the z = 0 plane, for a source panel with a Guid.</summary>
        private static Panel MakeWallPanel()
        {
            List<Point3D> points = new List<Point3D>
            {
                new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 1, 0), new Point3D(0, 1, 0)
            };
            Face3D face3D = Face3D.Create(new List<IClosedPlanar3D> { new Polygon3D(points) });
            return global::SAM.Analytical.Create.Panel(new Construction("Test Wall"), PanelType.Wall, face3D);
        }
    }
}
