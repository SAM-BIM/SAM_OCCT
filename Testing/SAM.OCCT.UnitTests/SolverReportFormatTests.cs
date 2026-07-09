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
        public void FormatExtendRecords_TopExtend_EmitsPanelLineWithGuidKindAndTarget()
        {
            // Arrange - a sloped-plane (E2) top extend of source panel 0 toward cap panel 5.
            Panel wall = MakeWallPanel();
            List<Panel> sources = new List<Panel> { wall };
            List<ExtendRecord> records = new List<ExtendRecord>
            {
                new ExtendRecord(0, 0, ExtendOperationKind.Top, 2.7, 3.25, "distance-to-plane",
                    new Point3D(2, 0.5, 2.7), new Point3D(2, 0.5, 3.25),
                    5, -1, "cap-plane", "cap plane n=(0,0.45,0.89) z=3.2", 0.05, false)
            };

            // Act
            List<string> lines = SolverReportFormat.FormatExtendRecords(records, sources);

            // Assert
            Assert.Single(lines);
            Assert.StartsWith("SAM_OCCT_EXTEND3D_PANEL:", lines[0]);
            Assert.Contains(wall.Guid.ToString(), lines[0]);      // panel Guid resolved from sources
            Assert.Contains("(#0)", lines[0]);                    // solver index
            Assert.Contains("top", lines[0]);
            Assert.Contains("2.7", lines[0]);                     // from
            Assert.Contains("3.25", lines[0]);                    // to
            Assert.Contains("cap #5 cap-plane", lines[0]);        // sloped-plane branch, cap index
            Assert.Contains("overshoot", lines[0]);
        }

        [Fact]
        public void FormatExtendRecords_SourceOutOfRange_ReportsNotAvailable()
        {
            // Arrange - a record with no resolvable source (index -1).
            List<ExtendRecord> records = new List<ExtendRecord>
            {
                new ExtendRecord(3, -1, ExtendOperationKind.PlanEnd, 4.0, 4.55, "plan",
                    new Point3D(4, 0, 0), new Point3D(4.55, 0, 0), -1, -1, "walls", "2D plan-loop junction", 0.05, true)
            };

            // Act
            List<string> lines = SolverReportFormat.FormatExtendRecords(records, null);

            // Assert
            Assert.Single(lines);
            Assert.Contains("panel n/a (#3)", lines[0]);
            Assert.Contains("plan-end", lines[0]);
            Assert.Contains("lateral-capped True", lines[0]); // the lateral cap flag surfaces
        }

        [Fact]
        public void FormatExtendRecords_Null_ReturnsEmptyList()
        {
            Assert.Empty(SolverReportFormat.FormatExtendRecords(null, null));
        }

        [Fact]
        public void ExtendPreviewSegment3Ds_MixedRecords_OneSegmentPerMovedEdgeNoneForCapGrow()
        {
            // Arrange - a moved top edge (distinct from/to) and a cap grow (null from/to).
            List<ExtendRecord> records = new List<ExtendRecord>
            {
                new ExtendRecord(0, 0, ExtendOperationKind.Top, 2.5, 3.05, "elevation",
                    new Point3D(2, 0, 2.5), new Point3D(2, 0, 3.05), 1, 1, "cap-scalar", "cap z=3", 0.05, false),
                new ExtendRecord(1, 1, ExtendOperationKind.CapGrow, 16.0, 25.0, "area",
                    null, null, -1, -1, "fixed-margin", "", 0.05, false)
            };

            // Act
            List<Segment3D> segments = SolverReportFormat.ExtendPreviewSegment3Ds(records);

            // Assert
            Assert.Single(segments); // only the moved top edge; the cap grow has no single edge
            Assert.Equal(2.5, segments[0].GetStart().Z, 3);
            Assert.Equal(3.05, segments[0].GetEnd().Z, 3);
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
