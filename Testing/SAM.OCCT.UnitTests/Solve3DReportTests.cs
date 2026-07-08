// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.OCCT.Solver;
using SAM.Geometry.OCCT.Solver;
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
    }
}
