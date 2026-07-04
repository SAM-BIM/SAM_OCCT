// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="SolverReportFormat"/> - the shared Grasshopper text formatters
    /// (Phase 8a). Pure string assembly: no native OCCT DLL required.
    /// </summary>
    public class SolverReportFormatTests
    {
        private static readonly Construction WallConstruction = new Construction("Test Wall");

        /// <summary>Axis-aligned rectangle in the z = 0 plane, y in [0, 1], x in [0, 4].</summary>
        private static Panel MakeWallPanel()
        {
            List<Point3D> points = new List<Point3D>
            {
                new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 1, 0), new Point3D(0, 1, 0)
            };
            Face3D face3D = Face3D.Create(new List<IClosedPlanar3D> { new Polygon3D(points) });
            return global::SAM.Analytical.Create.Panel(WallConstruction, PanelType.Wall, face3D);
        }

        [Fact]
        public void FormatDiagnostics_EntriesSupplied_PrefixesEachLine()
        {
            // Arrange
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            diagnostics.Add(SolverStage.Resolve, DiagnosticCode.NakedEdge, OcctDiagnosticSeverity.Warning, "gap found");

            // Act
            List<string> lines = SolverReportFormat.FormatDiagnostics(diagnostics, "SAM_OCCT_TEST");

            // Assert
            Assert.Single(lines);
            Assert.StartsWith("SAM_OCCT_TEST_DIAGNOSTIC:", lines[0]);
            Assert.Contains("gap found", lines[0]);
        }

        [Fact]
        public void FormatDiagnostics_NullDiagnostics_ReturnsEmptyList()
        {
            // Act
            List<string> lines = SolverReportFormat.FormatDiagnostics(null, "SAM_OCCT_TEST");

            // Assert
            Assert.Empty(lines);
        }

        [Fact]
        public void FormatSourceMap_OneToOneSource_NamesSourcePanel()
        {
            // Arrange
            SourceMap sourceMap = new SourceMap();
            sourceMap.Record(0, new FaceKey(0), Provenance.Resolved);
            List<Panel> sources = new List<Panel> { MakeWallPanel() };

            // Act
            List<string> lines = SolverReportFormat.FormatSourceMap(sourceMap, sources);

            // Assert
            Assert.Single(lines);
            Assert.Contains("source 0 (panel", lines[0]);
            Assert.Contains("f0", lines[0]);
        }

        [Fact]
        public void FormatSourceMap_FabricatedSource_LabelsFabricated()
        {
            // Arrange
            SourceMap sourceMap = new SourceMap();
            sourceMap.RecordFabricated(new FaceKey(3), Provenance.GapFill);

            // Act
            List<string> lines = SolverReportFormat.FormatSourceMap(sourceMap, null);

            // Assert
            Assert.Single(lines);
            Assert.StartsWith("fabricated ->", lines[0]);
            Assert.Contains("GapFill", lines[0]);
        }

        [Fact]
        public void FormatSourceMap_NullMap_ReturnsEmptyList()
        {
            // Act
            List<string> lines = SolverReportFormat.FormatSourceMap(null, null);

            // Assert
            Assert.Empty(lines);
        }

        [Fact]
        public void FormatLevelFrames_FramesSupplied_OneLinePerFrame()
        {
            // Arrange
            List<LevelFrame> levelFrames = new List<LevelFrame>
            {
                new LevelFrame(new Vector3D(0, 0, 1), new Point3D(0, 0, 2.5))
            };

            // Act
            List<string> lines = SolverReportFormat.FormatLevelFrames(levelFrames);

            // Assert
            Assert.Single(lines);
            Assert.Contains("elevation 2.5", lines[0]);
        }

        [Fact]
        public void FormatCells_RolesIndexAligned_IncludesRoleName()
        {
            // Arrange
            List<SolverCell> cells = new List<SolverCell> { new SolverCell(0, 12.5, new Point3D(1, 2, 3), null) };
            List<CellRole> roles = new List<CellRole> { CellRole.Interior };

            // Act
            List<string> lines = SolverReportFormat.FormatCells(cells, roles);

            // Assert
            Assert.Single(lines);
            Assert.Contains("role Interior", lines[0]);
            Assert.Contains("volume 12.5", lines[0]);
        }

        [Fact]
        public void FormatCells_NoRolesSupplied_ReportsNotClassified()
        {
            // Arrange
            List<SolverCell> cells = new List<SolverCell> { new SolverCell(0, 12.5, new Point3D(1, 2, 3), null) };

            // Act
            List<string> lines = SolverReportFormat.FormatCells(cells, null);

            // Assert
            Assert.Contains("not classified", lines[0]);
        }
    }
}
