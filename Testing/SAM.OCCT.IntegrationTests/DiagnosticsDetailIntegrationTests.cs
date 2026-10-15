// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;
using OcctQuery = SAM.Core.OCCT.Query;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Unified Diagnostics Phase 3 on the real two-storey house (native-gated): the structured records built from
    /// real solves keep the producer's severity and the exact legacy text, Warnings and Errors survive every detail
    /// level, and nothing about the geometry or the cluster depends on the level (the level is only a display filter
    /// applied after the solve; here the solves are repeated around the filtering to show it has no side effects).
    /// </summary>
    public class DiagnosticsDetailIntegrationTests
    {
        private static readonly string FixturePath = Path.Combine(System.AppContext.BaseDirectory, "Fixtures", "House-Regression", "House-TwoStorey-23panels.sam");

        private static readonly DiagnosticDetail[] Levels = { DiagnosticDetail.Summary, DiagnosticDetail.Standard, DiagnosticDetail.Detailed };

        private static (List<Panel> Panels, List<Space> Spaces) LoadFixture()
        {
            List<IJSAMObject> objects = SAM.Core.Convert.ToSAM(FixturePath) ?? new List<IJSAMObject>();
            return (objects.OfType<Panel>().ToList(), objects.OfType<Space>().ToList());
        }

        private static string PanelFingerprint(IEnumerable<Panel> panels)
        {
            return string.Join("|", panels.Where(x => x != null).Select(x => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}:{1:0.000}:{2}", x.PanelType, x.GetArea(), x.GetFace3D()?.GetPlane()?.Normal?.ToString())).OrderBy(x => x, System.StringComparer.Ordinal));
        }

        private static string ClusterFingerprint(AdjacencyCluster adjacencyCluster)
        {
            if (adjacencyCluster == null)
            {
                return "null";
            }

            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "spaces={0};panels={1};{2}", adjacencyCluster.GetSpaces()?.Count ?? 0, adjacencyCluster.GetPanels()?.Count ?? 0, PanelFingerprint(adjacencyCluster.GetPanels() ?? new List<Panel>()));
        }

        [SkippableFact]
        public void CreateAdjacencyCluster_House_RecordsKeepSeverityAndTextAndLevelsKeepWarningsAndErrors()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            (List<Panel> panels, List<Space> spaces) = LoadFixture();
            OcctBuildOptions options = new OcctBuildOptions { Tolerance = Tolerance.Distance, FuzzyTolerance = Tolerance.MacroDistance, AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01, MergeCoplanarBeforeBuild = false, ValidateSewCoverage = true };

            // Act
            AdjacencyCluster before = AnalyticalOcctCreate.AdjacencyCluster(spaces, panels, out OcctCellComplexResult result, new Log(), options);
            List<DiagnosticRecord> records = result.Diagnostics.Select(x => x.ToDiagnosticRecord()).ToList();
            Dictionary<DiagnosticDetail, List<DiagnosticRecord>> filtered = Levels.ToDictionary(x => x, x => records.FilterDiagnosticRecords(x));
            AdjacencyCluster after = AnalyticalOcctCreate.AdjacencyCluster(spaces, panels, out _, new Log(), options);

            // Assert: severity and legacy text are the original's.
            Assert.Equal(result.Diagnostics.Select(x => x.ToString()), records.Select(x => x.Text));
            Assert.Equal(result.Diagnostics.Select(x => x.Severity.ToString()), records.Select(x => x.Diagnostic.Severity.ToString()));

            // Assert: Detailed equals the legacy list; Warnings/Errors are the same at every level; Info only grows.
            Assert.Equal(records.Count, filtered[DiagnosticDetail.Detailed].Count);
            int warningsAndErrors = records.Count(x => x.Diagnostic.Severity != DiagnosticSeverity.Info);
            foreach (DiagnosticDetail level in Levels)
            {
                Assert.Equal(warningsAndErrors, filtered[level].Count(x => x.Diagnostic.Severity != DiagnosticSeverity.Info));
            }

            Assert.True(filtered[DiagnosticDetail.Summary].Count <= filtered[DiagnosticDetail.Standard].Count);
            Assert.True(filtered[DiagnosticDetail.Standard].Count <= filtered[DiagnosticDetail.Detailed].Count);

            // Assert: the result does not depend on the level (filtering has no side effects on a repeat solve).
            Assert.Equal(ClusterFingerprint(before), ClusterFingerprint(after));
        }

        [SkippableFact]
        public void Solve3D_House_RecordsEqualTheLegacyListAndKeepTheSolversSeverity()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            (List<Panel> panels, _) = LoadFixture();

            // Act
            List<Panel> solved = panels.Solve3D(out _, out List<string> diagnostics, out _, out Solve3DReport report);
            List<DiagnosticRecord> records = report.DiagnosticRecords.ToList();
            Dictionary<DiagnosticDetail, List<DiagnosticRecord>> filtered = Levels.ToDictionary(x => x, x => records.FilterDiagnosticRecords(x));
            List<Panel> solvedAgain = panels.Solve3D(out _, out List<string> diagnosticsAgain, out _, out _);

            // Assert: one record per legacy line, same text, same order.
            Assert.Equal(diagnostics, records.Select(x => x.Text).ToList());

            // Assert: the structured solver lines carry the solver's own severity.
            List<SAM.Geometry.OCCT.Solver.SolverDiagnostic> solverDiagnostics = report.Diagnostics.All.ToList();
            List<DiagnosticRecord> solverRecords = records.Where(x => x.Text.StartsWith("SAM_OCCT_SOLVE3D_DIAGNOSTIC:", System.StringComparison.Ordinal)).ToList();
            Assert.Equal(solverDiagnostics.Count, solverRecords.Count);
            for (int i = 0; i < solverDiagnostics.Count; i++)
            {
                Assert.Equal(solverDiagnostics[i].Severity.ToDiagnosticSeverity(), solverRecords[i].Diagnostic.Severity);
            }

            // Assert: Warnings/Errors are kept at every level; Detailed is the whole legacy list.
            int warningsAndErrors = records.Count(x => x.Diagnostic.Severity != DiagnosticSeverity.Info);
            foreach (DiagnosticDetail level in Levels)
            {
                Assert.Equal(warningsAndErrors, filtered[level].Count(x => x.Diagnostic.Severity != DiagnosticSeverity.Info));
            }

            Assert.Equal(diagnostics, filtered[DiagnosticDetail.Detailed].Select(x => x.Text).ToList());

            // Assert: identical solver output on a repeat (the level never reaches the solver).
            Assert.Equal(PanelFingerprint(solved), PanelFingerprint(solvedAgain));
            Assert.Equal(diagnostics.Count, diagnosticsAgain.Count);
        }

        [SkippableFact]
        public void Solve3D_NoUsablePanels_RecordsAnErrorWithTheLegacyLine()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange / Act
            List<Panel> solved = new List<Panel>().Solve3D(out _, out List<string> diagnostics, out _, out Solve3DReport report);

            // Assert
            Assert.Null(solved);
            Assert.Contains(report.DiagnosticRecords, x => x.Diagnostic.Severity == DiagnosticSeverity.Error && x.Text.StartsWith("SAM_OCCT_SOLVE3D_INPUT_EMPTY:", System.StringComparison.Ordinal));
            Assert.Equal(diagnostics, report.DiagnosticRecords.Select(x => x.Text).ToList());
        }
    }
}
