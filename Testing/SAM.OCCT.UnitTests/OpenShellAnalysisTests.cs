// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Native;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Validates the pure-managed naked-edge watertightness check that explains a
    /// native cell-builder status 40 ("faces do not bound a volume"). None of these
    /// require the native OCCT library, so they run on any CI agent.
    /// </summary>
    public class OpenShellAnalysisTests
    {
        [Fact]
        public void Report_ClosedBox_LogsNoNakedEdges()
        {
            // Arrange
            List<Face3D> face3Ds = TestGeometry.CreateClosedBoxFaces();
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            OcctOpenShellAnalysis.Report(face3Ds, new OcctBuildOptions(), result);

            // Assert
            OcctDiagnostic diagnostic = Assert.Single(result.Diagnostics, x => x.Code == "SAM_OCCT_OPEN_SHELL_ANALYSIS");
            Assert.Equal(OcctDiagnosticSeverity.Info, diagnostic.Severity);
            Assert.Contains("no naked edges", diagnostic.Message);
        }

        [Fact]
        public void Report_OpenBox_LogsFourNakedEdges()
        {
            // Arrange - unit cube missing its top face leaves four open rim edges.
            List<Face3D> face3Ds = TestGeometry.CreateOpenBoxFaces();
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            OcctOpenShellAnalysis.Report(face3Ds, new OcctBuildOptions(), result);

            // Assert
            OcctDiagnostic diagnostic = Assert.Single(result.Diagnostics, x => x.Code == "SAM_OCCT_OPEN_SHELL_ANALYSIS");
            Assert.Equal(OcctDiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Contains("4 naked (open) edge(s)", diagnostic.Message);
            Assert.Contains("NOT closed", diagnostic.Message);
        }

        [Fact]
        public void Report_NullFaces_AddsNoDiagnostic()
        {
            // Arrange
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            OcctOpenShellAnalysis.Report(null, new OcctBuildOptions(), result);

            // Assert
            Assert.DoesNotContain(result.Diagnostics, x => x.Code == "SAM_OCCT_OPEN_SHELL_ANALYSIS");
        }

        [Fact]
        public void DescribeBuildStatus_40_ExplainsNoClosedVolume()
        {
            // Act
            string description = OcctOpenShellAnalysis.DescribeBuildStatus(40);

            // Assert
            Assert.Contains("no closed solid", description);
        }
    }
}
