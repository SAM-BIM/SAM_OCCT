// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="GapFill.FromNakedWires"/> - the Phase 5b wire-based gap filling
    /// (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §G/§J.6). Pure-managed (Face3D/Polygon3D +
    /// hand-built <see cref="OcctNakedWire"/>s): no native OCCT DLL required.
    /// </summary>
    public class GapFillV2Tests
    {
        private static OcctNakedWire Wire(bool isClosed, params Point3D[] points)
        {
            // Edge owner indices are unused by FromNakedWires; pass an empty list.
            return new OcctNakedWire(new List<Point3D>(points), isClosed, new List<int>());
        }

        private static bool HasWarning(SolverDiagnostics diagnostics)
        {
            return diagnostics.OfCode(DiagnosticCode.NakedLoop).Any(d => d.Severity == OcctDiagnosticSeverity.Warning);
        }

        [Fact]
        public void FromNakedWires_ClosedPlanarSquare_CreatesOneValidPatch()
        {
            // Arrange - a 1x1 closed planar loop at z = 0.
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            OcctNakedWire wire = Wire(true,
                new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(1, 1, 0), new Point3D(0, 1, 0));

            // Act
            GapFill.GapFillResult result = GapFill.FromNakedWires(new List<OcctNakedWire> { wire }, diagnostics, Core.Tolerance.Distance);

            // Assert - exactly one planar patch, valid, with the loop recorded as planar-patched.
            Assert.Single(result.Patches);
            Assert.True(result.Patches[0].IsValid());
            Assert.Single(result.LoopOutcomes);
            Assert.True(result.LoopOutcomes[0].PlanarPatched);
            Assert.False(result.LoopOutcomes[0].FanPatched);
            // A patch-created diagnostic is emitted (Info, not a warning).
            Assert.Contains(diagnostics.OfCode(DiagnosticCode.NakedLoop), d => d.Severity == OcctDiagnosticSeverity.Info);
        }

        [Fact]
        public void FromNakedWires_OpenWire_EmitsResidualWarningAndNoPatch()
        {
            // Arrange - an OPEN polyline (not closed).
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            OcctNakedWire wire = Wire(false,
                new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(1, 1, 0));

            // Act
            GapFill.GapFillResult result = GapFill.FromNakedWires(new List<OcctNakedWire> { wire }, diagnostics, Core.Tolerance.Distance);

            // Assert - no patch, residual outcome, and a warning (never a silent drop).
            Assert.Empty(result.Patches);
            Assert.True(result.LoopOutcomes[0].Residual);
            Assert.True(HasWarning(diagnostics));
        }

        [Fact]
        public void FromNakedWires_NonPlanarClosedLoop_UsesFanFallbackWithWarning()
        {
            // Arrange - a closed 4-point loop that is NOT coplanar (last point lifted in Z).
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            OcctNakedWire wire = Wire(true,
                new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(1, 1, 0), new Point3D(0, 1, 1));

            // Act
            GapFill.GapFillResult result = GapFill.FromNakedWires(new List<OcctNakedWire> { wire }, diagnostics, Core.Tolerance.Distance);

            // Assert - fan-patched (never a single planar face), tagged with a warning.
            Assert.NotEmpty(result.Patches);
            Assert.True(result.LoopOutcomes[0].FanPatched);
            Assert.False(result.LoopOutcomes[0].PlanarPatched);
            Assert.True(HasWarning(diagnostics));
        }

        [Fact]
        public void FromNakedWires_SelfIntersectingPlanarLoop_IsNotAcceptedAsSinglePlanarPatch()
        {
            // Arrange - a planar bow-tie (self-intersecting) loop at z = 0.
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            OcctNakedWire wire = Wire(true,
                new Point3D(0, 0, 0), new Point3D(1, 1, 0), new Point3D(1, 0, 0), new Point3D(0, 1, 0));

            // Act
            GapFill.GapFillResult result = GapFill.FromNakedWires(new List<OcctNakedWire> { wire }, diagnostics, Core.Tolerance.Distance);

            // Assert - the self-intersecting boundary is NOT accepted as a clean single planar patch; it is
            // handled safely (fan fallback or residual) and warned, never silently accepted.
            Assert.False(result.LoopOutcomes[0].PlanarPatched);
            Assert.True(HasWarning(diagnostics));
        }

        [Fact]
        public void FromNakedWires_TinyClosedLoop_IsRejectedWithWarningAndNoPatch()
        {
            // Arrange - a 5 mm x 5 mm loop (area 2.5e-5 m2, below the 1e-4 air-panel floor).
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            OcctNakedWire wire = Wire(true,
                new Point3D(0, 0, 0), new Point3D(0.005, 0, 0), new Point3D(0.005, 0.005, 0), new Point3D(0, 0.005, 0));

            // Act
            GapFill.GapFillResult result = GapFill.FromNakedWires(new List<OcctNakedWire> { wire }, diagnostics, Core.Tolerance.Distance);

            // Assert - rejected (no patch), residual, warned.
            Assert.Empty(result.Patches);
            Assert.True(result.LoopOutcomes[0].Residual);
            Assert.True(HasWarning(diagnostics));
        }

        [Fact]
        public void FromNakedWires_NestedCoplanarLoops_EmitsNestedWarning()
        {
            // Arrange - a small closed loop fully inside a larger coplanar closed loop (a hole/nested case).
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            OcctNakedWire outer = Wire(true,
                new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(0, 4, 0));
            OcctNakedWire inner = Wire(true,
                new Point3D(1, 1, 0), new Point3D(2, 1, 0), new Point3D(2, 2, 0), new Point3D(1, 2, 0));

            // Act
            GapFill.GapFillResult result = GapFill.FromNakedWires(new List<OcctNakedWire> { outer, inner }, diagnostics, Core.Tolerance.Distance);

            // Assert - the nested/hole case is surfaced as a warning (documented P5 scope cut), never silently mis-patched.
            Assert.Contains(diagnostics.OfCode(DiagnosticCode.NakedLoop),
                d => d.Severity == OcctDiagnosticSeverity.Warning && d.Message.Contains("nested"));
        }

        [Fact]
        public void FromNakedWires_NoWires_ReturnsEmptyResult()
        {
            // Arrange
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            GapFill.GapFillResult result = GapFill.FromNakedWires(new List<OcctNakedWire>(), diagnostics, Core.Tolerance.Distance);

            // Assert
            Assert.Empty(result.Patches);
            Assert.Empty(result.LoopOutcomes);
        }
    }
}
