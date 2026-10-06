// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="ClosureReport"/> - the Grasshopper-facing closure report formatter
    /// (Phase 8a, docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md Phase 8). Pure string assembly:
    /// no native OCCT DLL required.
    /// </summary>
    public class ClosureReportTests
    {
        [Fact]
        public void Format_RawAdopted_ReportsRawPath()
        {
            // Arrange
            ClosureSignature3D signature = new ClosureSignature3D(22, new List<double> { 100 }, 0, 90, 0);

            // Act
            string text = ClosureReport.Format(rawAdopted: true, signature: signature, rawAttemptSignature: signature, diagnostics: null);

            // Assert
            Assert.Contains("Adopted path: Raw", text);
        }

        [Fact]
        public void Format_ManagedAdopted_ReportsManagedPath()
        {
            // Arrange
            ClosureSignature3D signature = new ClosureSignature3D(22, new List<double> { 100 }, 0, 90, 0);

            // Act
            string text = ClosureReport.Format(rawAdopted: false, signature: signature, rawAttemptSignature: null, diagnostics: null);

            // Assert
            Assert.Contains("Adopted path: Managed", text);
            Assert.Contains("Raw attempt: not attempted", text);
        }

        [Fact]
        public void Format_NullSignature_ReportsNoResult()
        {
            // Act
            string text = ClosureReport.Format(rawAdopted: false, signature: null, rawAttemptSignature: null, diagnostics: null);

            // Assert
            Assert.Contains("Final: no result", text);
        }

        [Fact]
        public void Format_DiagnosticsSupplied_CountsBySeverity()
        {
            // Arrange
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            diagnostics.Add(SolverStage.Resolve, DiagnosticCode.SliverCell, OcctDiagnosticSeverity.Warning, "one warning");
            diagnostics.Add(SolverStage.Resolve, DiagnosticCode.AdoptedLevel, OcctDiagnosticSeverity.Info, "one info");
            diagnostics.Add(SolverStage.Resolve, DiagnosticCode.AdoptedLevel, OcctDiagnosticSeverity.Info, "another info");

            // Act
            string text = ClosureReport.Format(rawAdopted: true, signature: null, rawAttemptSignature: null, diagnostics: diagnostics);

            // Assert
            Assert.Contains("Diagnostics: 0 error(s), 1 warning(s), 2 info", text);
        }

        [Fact]
        public void Format_RoundsSupplied_ReportsAcceptedOfAttempted()
        {
            // Act
            string text = ClosureReport.Format(rawAdopted: false, signature: null, rawAttemptSignature: null, diagnostics: null, rounds: 3, roundsAccepted: 2);

            // Assert
            Assert.Contains("AutoTune rounds: 2 accepted of 3 attempted", text);
        }

        [Fact]
        public void Format_LevelFramesSupplied_ListsEachFrame()
        {
            // Arrange
            List<LevelFrame> levelFrames = new List<LevelFrame>
            {
                new LevelFrame(new Vector3D(0, 0, 1), new Point3D(0, 0, 0)),
                new LevelFrame(new Vector3D(0, 0, 1), new Point3D(0, 0, 3))
            };

            // Act
            string text = ClosureReport.Format(rawAdopted: false, signature: null, rawAttemptSignature: null, diagnostics: null, levelFrames: levelFrames);

            // Assert
            Assert.Contains("Level frames: 2", text);
            Assert.Contains("Frame 0:", text);
            Assert.Contains("Frame 1:", text);
        }

        [Fact]
        public void Format_RawAdoptedWithFrames_MarksFramesNotApplicable()
        {
            // Act
            string text = ClosureReport.Format(rawAdopted: true, signature: null, rawAttemptSignature: null, diagnostics: null, levelFrames: new List<LevelFrame>());

            // Assert
            Assert.Contains("Level frames: 0 (n/a on the raw path)", text);
        }

        [Fact]
        public void Format_Always_EndsWithTimingsNotTracked()
        {
            // Act
            string text = ClosureReport.Format(rawAdopted: false, signature: null, rawAttemptSignature: null, diagnostics: null);

            // Assert
            Assert.EndsWith("Timings: not tracked", text);
        }
    }
}
