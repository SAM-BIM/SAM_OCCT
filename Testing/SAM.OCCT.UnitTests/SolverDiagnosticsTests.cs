// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="SolverDiagnostic"/> and <see cref="SolverDiagnostics"/> - the
    /// machine-readable event contract every solver stage emits into
    /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §N, Phase 1). Pure-managed.
    /// </summary>
    public class SolverDiagnosticsTests
    {
        [Fact]
        public void Add_ByFields_AppearsInAll()
        {
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            diagnostics.Add(SolverStage.Resolve, DiagnosticCode.AdoptedLevel, OcctDiagnosticSeverity.Info, "adopted");

            Assert.Single(diagnostics.All);
            Assert.Equal(SolverStage.Resolve, diagnostics.All[0].Stage);
            Assert.Equal(DiagnosticCode.AdoptedLevel, diagnostics.All[0].Code);
            Assert.Equal("adopted", diagnostics.All[0].Message);
        }

        [Fact]
        public void Add_NullDiagnostic_IsIgnored()
        {
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            diagnostics.Add((SolverDiagnostic)null);

            Assert.Empty(diagnostics.All);
        }

        [Fact]
        public void OfCode_MixedDiagnostics_FiltersToMatchingCode()
        {
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            diagnostics.Add(SolverStage.Resolve, DiagnosticCode.SliverCell, OcctDiagnosticSeverity.Warning, "sliver");
            diagnostics.Add(SolverStage.Resolve, DiagnosticCode.DroppedFace, OcctDiagnosticSeverity.Warning, "dropped");

            Assert.Single(diagnostics.OfCode(DiagnosticCode.SliverCell));
        }

        [Fact]
        public void OfStage_MixedDiagnostics_FiltersToMatchingStage()
        {
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            diagnostics.Add(SolverStage.Resolve, DiagnosticCode.AdoptedLevel, OcctDiagnosticSeverity.Info, "resolve");
            diagnostics.Add(SolverStage.Heal, DiagnosticCode.EscalatedPanel, OcctDiagnosticSeverity.Info, "heal");

            Assert.Single(diagnostics.OfStage(SolverStage.Heal));
        }

        [Fact]
        public void HasErrors_OnlyWarningsAndInfo_ReturnsFalse()
        {
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            diagnostics.Add(SolverStage.Resolve, DiagnosticCode.DroppedFace, OcctDiagnosticSeverity.Warning, "dropped");

            Assert.False(diagnostics.HasErrors);
        }

        [Fact]
        public void HasErrors_ContainsErrorSeverity_ReturnsTrue()
        {
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            diagnostics.Add(SolverStage.Resolve, DiagnosticCode.ToleranceDrift, OcctDiagnosticSeverity.Error, "drift");

            Assert.True(diagnostics.HasErrors);
        }

        [Fact]
        public void Clear_AfterAdding_EmptiesAll()
        {
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            diagnostics.Add(SolverStage.Snap, DiagnosticCode.Overlap, OcctDiagnosticSeverity.Info, "overlap");

            diagnostics.Clear();

            Assert.Empty(diagnostics.All);
        }

        [Fact]
        public void ToString_FormatsStageCodeSeverityAndMessage()
        {
            SolverDiagnostic diagnostic = new SolverDiagnostic(SolverStage.Resolve, DiagnosticCode.AdoptedLevel, OcctDiagnosticSeverity.Info, "adopted raw");

            string text = diagnostic.ToString();

            Assert.Contains("Resolve", text);
            Assert.Contains("AdoptedLevel", text);
            Assert.Contains("Info", text);
            Assert.Contains("adopted raw", text);
        }

        [Fact]
        public void Constructor_NullPoint3Ds_ReturnsEmptyList()
        {
            SolverDiagnostic diagnostic = new SolverDiagnostic(SolverStage.Heal, DiagnosticCode.NakedLoop, OcctDiagnosticSeverity.Warning, "loop");

            Assert.Empty(diagnostic.Point3Ds);
        }
    }
}
