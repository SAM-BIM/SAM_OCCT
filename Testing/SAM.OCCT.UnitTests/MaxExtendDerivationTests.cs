// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Analytical.Solver;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

using Modify = SAM.Analytical.OCCT.Solver.Modify;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests pinning how <see cref="Modify.ResolveMaxExtends(System.Collections.Generic.IEnumerable{double}, System.Collections.Generic.List{Panel}, out System.Collections.Generic.List{ParameterProvenance})"/>
    /// resolves the per-panel lateral reach (docs/CONTROLLED_WORKFLOW_PLAN.md §5.6, revised): a supplied list or a
    /// valid per-panel <c>SolverParameter.MaxExtend</c> stamp wins; otherwise the flat solver default 0.4 m.
    /// <para>
    /// These tests are also the REGRESSION GUARD for the P3 decision NOT to adopt the <c>SetMaxExtends</c>
    /// clone-derivation the plan first proposed: that derivation pre-caps the reach at 0.49x each panel's own
    /// in-plane length, which crushed short/segmented walls (e.g. to ~0.106 m) and regressed the managed golden
    /// masters. The unstamped fallback must therefore stay the flat 0.4 m default (byte-identical to pre-P3), with
    /// MaxExtend tuned per-panel via the stamp. Pure-managed (no native).
    /// </para>
    /// </summary>
    public class MaxExtendDerivationTests
    {
        private static Face3D WallFace(double x)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(x, 0, 0), new Point3D(x, 4, 0), new Point3D(x, 4, 3), new Point3D(x, 0, 3));
        }

        private static Panel Wall(double x)
        {
            return global::SAM.Analytical.Create.Panel(new Construction("Wall"), PanelType.Wall, WallFace(x));
        }

        [Fact]
        public void ResolveMaxExtends_StampedPanel_StampWinsReportedAsStamped()
        {
            // Arrange - a wall carrying a hand-set MaxExtend stamp.
            Panel stamped = Wall(0);
            stamped.SetValue(SolverParameter.MaxExtend, 1.25);
            List<Panel> sources = new List<Panel> { stamped };

            // Act
            List<double> maxExtends = Modify.ResolveMaxExtends(null, sources, out List<ParameterProvenance> provenance);

            // Assert
            Assert.Equal(1.25, maxExtends[0], 6);
            Assert.Equal(ParameterProvenance.Stamped, provenance[0]);
        }

        [Fact]
        public void ResolveMaxExtends_UnstampedPanel_FallsBackToFlat04NotDerived()
        {
            // Arrange - an unstamped wall.
            List<Panel> sources = new List<Panel> { Wall(0) };

            // Act
            List<double> maxExtends = Modify.ResolveMaxExtends(null, sources, out List<ParameterProvenance> provenance);

            // Assert - the flat solver default, reported as Default. This is the regression guard: it must NOT be a
            // SetMaxExtends-derived value (0.33 / 0.5 / 0.6, or a length-crushed ~0.1) - the derivation stays out.
            Assert.Equal(0.4, maxExtends[0], 6);
            Assert.Equal(Panel3DSnapSolver.DEFAULT_MaxExtension, maxExtends[0], 6);
            Assert.Equal(ParameterProvenance.Default, provenance[0]);
        }

        [Fact]
        public void ResolveMaxExtends_SuppliedList_WinsOverStampAndDefault()
        {
            // Arrange - a supplied override list alongside a stamped panel; the list is the explicit authority.
            Panel stamped = Wall(0);
            stamped.SetValue(SolverParameter.MaxExtend, 1.25);
            List<Panel> sources = new List<Panel> { stamped, Wall(5) };
            List<double> supplied = new List<double> { 0.9, 0.7 };

            // Act
            List<double> maxExtends = Modify.ResolveMaxExtends(supplied, sources, out List<ParameterProvenance> provenance);

            // Assert - the supplied values win for every panel, reported as Stamped (an explicit override).
            Assert.Equal(0.9, maxExtends[0], 6);
            Assert.Equal(0.7, maxExtends[1], 6);
            Assert.All(provenance, p => Assert.Equal(ParameterProvenance.Stamped, p));
        }

        [Fact]
        public void ResolveMaxExtends_InvalidStamp_FallsBackToDefault()
        {
            // Arrange - a wall stamped with a non-positive (invalid) MaxExtend, which must not be honoured.
            Panel invalid = Wall(0);
            invalid.SetValue(SolverParameter.MaxExtend, 0.0);
            List<Panel> sources = new List<Panel> { invalid };

            // Act
            List<double> maxExtends = Modify.ResolveMaxExtends(null, sources, out List<ParameterProvenance> provenance);

            // Assert - an invalid stamp is ignored; the flat default applies.
            Assert.Equal(0.4, maxExtends[0], 6);
            Assert.Equal(ParameterProvenance.Default, provenance[0]);
        }
    }
}
