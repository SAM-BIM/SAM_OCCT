// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// The severity of the level-clip diagnostics (review of 2026-10-26, revision 3, finding 1; DIAGNOSTICS_STANDARD 2.1,
    /// 6 and 7): a required clip that could not be made leaves the extension whole, so the wall may stand where the input
    /// has none - an Error, set where the line is produced, kept through the adapter and Solve3D's records and never
    /// filtered at any detail level. A completed clip that drops a kept extension piece is a usable, degraded result - a
    /// Warning. A made clip is Info. Each failure is produced by the real clip routine, not a stub.
    /// </summary>
    [Collection(ExtendCensusCollection.Name)]
    public class ExtendLevelClipDiagnosticsTests
    {
        private const double Overshoot = 0.05;
        private const double Tolerance = 1e-6;

        private static readonly DiagnosticDetail[] Details = { DiagnosticDetail.Summary, DiagnosticDetail.Standard, DiagnosticDetail.Detailed };

        private static List<(double Z, IList<(double From, double To)> Columns, IList<(double U, double ZMin, double ZMax)> Crossings)> Level(params (double U, double ZMin, double ZMax)[] crossings)
        {
            return new List<(double, IList<(double, double)>, IList<(double, double, double)>)>
            {
                (3.0, new List<(double, double)> { (-0.3, 4.3) }, crossings.ToList()),
            };
        }

        /// <summary>A wall (x = 0) whose clip cannot keep its original: the original (a 50 mm square at y 10) lies apart from
        /// the far larger extension (y 0..4), so the piece holding the kept extension does not hold it.</summary>
        private static SnappedPanel FailedClip()
        {
            Face3D original = TestGeometry.CreatePlanarFace(new Point3D(0, 10, 5), new Point3D(0, 10.05, 5), new Point3D(0, 10.05, 5.05), new Point3D(0, 10, 5.05));
            SnappedPanel wall = new SnappedPanel(7, TestGeometry.CreatePlanarFace(new Point3D(0, 0, -0.05), new Point3D(0, 4, -0.05), new Point3D(0, 4, 6), new Point3D(0, 0, 6)), 1, 0.3, 0.5);
            wall.ClipBeyondLevels(original, Level(), false, Overshoot, Tolerance, out bool failed);
            Assert.True(failed);
            return wall;
        }

        /// <summary>A wall whose clip is made but drops a kept junction piece not connected to the wall (0.15 m2).</summary>
        private static SnappedPanel ClipWithDroppedPiece()
        {
            Face3D original = TestGeometry.CreatePlanarFace(new Point3D(0, 0, 6), new Point3D(0, 0, 2.5), new Point3D(0, 1, 2.5), new Point3D(0, 1, 3), new Point3D(0, 4, 3), new Point3D(0, 4, 6));
            SnappedPanel wall = new SnappedPanel(8, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 6), new Point3D(0, 0, 2.5), new Point3D(0, 1, 2.5), new Point3D(0, 1, -0.05), new Point3D(0, 1.15, -0.05), new Point3D(0, 1.15, 3), new Point3D(0, 4, 3), new Point3D(0, 4, 6)), 1, 0.3, 0.5);
            Assert.True(wall.ClipBeyondLevels(original, Level((1.15, 1.0, 2.0)), false, Overshoot, Tolerance, out bool failed));
            Assert.False(failed);
            return wall;
        }

        /// <summary>A wall (x = 0, y 0..6) merged across two storeys with a narrower ground floor, extended in its stack: one
        /// clip made, nothing failed.</summary>
        private static SnappedPanel MadeClip()
        {
            SnappedPanel wall = new SnappedPanel(9, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(0, 2, 0), new Point3D(0, 2, 3), new Point3D(0, 6, 3), new Point3D(0, 6, 6), new Point3D(0, 0, 6)), 1, 0.3, 0.5);
            List<SnappedPanel> panels = new List<double> { 0, 3, 6 }
                .Select((z, i) => new SnappedPanel(100 + i, TestGeometry.CreatePlanarFace(new Point3D(-1, -1, z), new Point3D(11, -1, z), new Point3D(11, 7, z), new Point3D(-1, 7, z)), 1, 0.3, 0.5))
                .ToList();
            panels.Add(wall);
            Panel3DSnapSolver.Extend(panels, 20 * (System.Math.PI / 180), Overshoot, Tolerance);
            return wall;
        }

        [Fact]
        public void ClipBeyondLevels_ClipCannotBeMade_ProducerRecordsAnError()
        {
            // Arrange / Act
            SnappedPanel wall = FailedClip();

            // Assert: one line, an Error, at the producer.
            Assert.Single(wall.ExtendDiagnostics);
            Assert.StartsWith("SAM_OCCT_EXTEND3D_LEVEL_CLIP_FAILED: panel (#7) bottom extension", wall.ExtendDiagnostics[0]);
            Assert.Contains("may stand where the input has no wall", wall.ExtendDiagnostics[0]);
            Assert.Equal(new[] { OcctDiagnosticSeverity.Error }, wall.ExtendDiagnosticSeverities.ToArray());
        }

        [Fact]
        public void ClipBeyondLevels_KeptPieceDropped_ProducerRecordsAWarningAfterTheInfoClip()
        {
            // Arrange / Act
            SnappedPanel wall = ClipWithDroppedPiece();

            // Assert
            Assert.Equal(2, wall.ExtendDiagnostics.Count);
            Assert.StartsWith("SAM_OCCT_EXTEND3D_LEVEL_CLIP:", wall.ExtendDiagnostics[0]);
            Assert.StartsWith("SAM_OCCT_EXTEND3D_LEVEL_CLIP_PIECE_DROPPED:", wall.ExtendDiagnostics[1]);
            Assert.Contains("the wall may leave an opening there", wall.ExtendDiagnostics[1]);
            Assert.Equal(new[] { OcctDiagnosticSeverity.Info, OcctDiagnosticSeverity.Warning }, wall.ExtendDiagnosticSeverities.ToArray());
        }

        [Fact]
        public void Extend_ClipMade_IsInfoAndNothingFailed()
        {
            // Arrange / Act
            SnappedPanel wall = MadeClip();

            // Assert: an ordinary clip is Info only; no Warning or Error.
            Assert.Contains(wall.ExtendDiagnostics, x => x.StartsWith("SAM_OCCT_EXTEND3D_LEVEL_CLIP:"));
            Assert.Equal(wall.ExtendDiagnostics.Count, wall.ExtendDiagnosticSeverities.Count);
            Assert.All(wall.ExtendDiagnosticSeverities, x => Assert.Equal(OcctDiagnosticSeverity.Info, x));
        }

        [Fact]
        public void FormatExtendPanelDiagnosticRecords_ProducerSeverities_KeptLineForLineWithTheLegacyText()
        {
            // Arrange
            List<SnappedPanel> panels = new List<SnappedPanel> { MadeClip(), FailedClip(), ClipWithDroppedPiece() };

            // Act
            List<DiagnosticRecord> records = SolverReportFormat.FormatExtendPanelDiagnosticRecords(panels);
            List<string> legacy = SolverReportFormat.FormatExtendPanelDiagnostics(panels);

            // Assert: same lines in the same order; severity from the producer, not the text.
            Assert.Equal(legacy, records.ConvertAll(x => x.Text));
            Assert.Equal(DiagnosticSeverity.Error, records.Single(x => x.Text.StartsWith("SAM_OCCT_EXTEND3D_LEVEL_CLIP_FAILED")).Diagnostic.Severity);
            Assert.Equal(DiagnosticSeverity.Warning, records.Single(x => x.Text.StartsWith("SAM_OCCT_EXTEND3D_LEVEL_CLIP_PIECE_DROPPED")).Diagnostic.Severity);
            Assert.All(records.Where(x => x.Text.StartsWith("SAM_OCCT_EXTEND3D_LEVEL_CLIP:")), x => Assert.Equal(DiagnosticSeverity.Info, x.Diagnostic.Severity));
        }

        [Fact]
        public void AppendExtendPanelDiagnostics_FailedClip_ErrorSurvivesEveryDetailLevelAndIsRaised()
        {
            // Arrange: Solve3D's own path for these lines, on the real producer's failure.
            List<string> diagnostics = new List<string> { "SAM_OCCT_SOLVE3D_RESULT: earlier line." };
            List<DiagnosticRecord> diagnosticRecords = new List<DiagnosticRecord> { SAM.Core.OCCT.Query.CreateDiagnosticRecord(diagnostics[0], DiagnosticSeverity.Info) };

            // Act
            SAM.Analytical.OCCT.Solver.Modify.AppendExtendPanelDiagnostics(diagnostics, diagnosticRecords, new List<SnappedPanel> { MadeClip(), FailedClip(), ClipWithDroppedPiece() });

            // Assert: the legacy list and the records stay in step (Solve3D's component falls back to all-Info otherwise).
            Assert.Equal(diagnostics, diagnosticRecords.ConvertAll(x => x.Text));
            string failed = diagnostics.Single(x => x.StartsWith("SAM_OCCT_EXTEND3D_LEVEL_CLIP_FAILED"));
            string dropped = diagnostics.Single(x => x.StartsWith("SAM_OCCT_EXTEND3D_LEVEL_CLIP_PIECE_DROPPED"));
            foreach (DiagnosticDetail detail in Details)
            {
                List<DiagnosticRecord> filtered = diagnosticRecords.FilterDiagnosticRecords(detail);
                Assert.Contains(filtered, x => x.Text == failed && x.Diagnostic.Severity == DiagnosticSeverity.Error);
                Assert.Contains(filtered, x => x.Text == dropped && x.Diagnostic.Severity == DiagnosticSeverity.Warning);
            }

            List<DiagnosticMessage> messages = diagnosticRecords.GetDiagnosticMessages();
            Assert.Contains(messages, x => x.Severity == DiagnosticSeverity.Error && x.Text == failed);
            Assert.Contains(messages, x => x.Severity == DiagnosticSeverity.Warning && x.Text == dropped);
            Assert.DoesNotContain(messages, x => x.Text.StartsWith("SAM_OCCT_EXTEND3D_LEVEL_CLIP:"));
        }

        [Fact]
        public void FormatExtendRecords_ExtendWhoseLevelClipFailed_OrdinaryLineIsFollowedByTheRisk()
        {
            // Arrange: the record the solver keeps for an extension whose clip could not be made.
            ExtendRecord record = new ExtendRecord(
                panelIndex: 3, sourceIndex: -1, kind: ExtendOperationKind.Bottom,
                fromValue: 0, toValue: -0.05, measureUnit: "elevation",
                from: new Point3D(0, 0, 0), to: new Point3D(0, 0, -0.05),
                targetPanelIndex: 0, targetSourceIndex: -1, targetKind: "cap-scalar", targetDescription: "cap z=0",
                overshoot: 0.05, maxExtendCapped: false);
            record.AddRisk(ExtendRiskFlag.LevelClipFailed);

            // Act
            List<string> lines = SolverReportFormat.FormatExtendRecords(new List<ExtendRecord> { record }, new List<Panel>());

            // Assert: the ordinary extend line never stands alone as a completed clip.
            Assert.Equal(2, lines.Count);
            Assert.StartsWith("SAM_OCCT_EXTEND3D_PANEL:", lines[0]);
            Assert.StartsWith("SAM_OCCT_EXTEND3D_RISKY:", lines[1]);
            Assert.Contains("LevelClipFailed", lines[1]);
        }
    }
}
