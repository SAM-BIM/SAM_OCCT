// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Analytical.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

// Modify exists in SAM.Analytical, SAM.Analytical.Solver AND SAM.Analytical.OCCT.Solver - alias the OCCT one
// (whose internal parameter-precedence resolvers this test exercises) so the bare name is unambiguous.
using Modify = SAM.Analytical.OCCT.Solver.Modify;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for the parameter-precedence resolvers (docs/CONTROLLED_WORKFLOW_PLAN.md §3, D6): a valid
    /// per-panel stamp always wins; otherwise derive; otherwise the solver default. Covers stamped/derived/
    /// default (and min-floor) for BucketSize, Weight and MaxExtend, plus their provenance tags. Pure-managed
    /// (no native OCCT); exercises <see cref="Modify"/>'s internal resolvers via InternalsVisibleTo.
    /// </summary>
    public class ParameterPrecedenceTests
    {
        private static Face3D WallFace(double x)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(x, 0, 0), new Point3D(x, 4, 0), new Point3D(x, 4, 3), new Point3D(x, 0, 3));
        }

        private static Panel Wall(double x, Construction construction = null)
        {
            return global::SAM.Analytical.Create.Panel(construction ?? new Construction("Wall"), PanelType.Wall, WallFace(x));
        }

        // ── Weight ──────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void ResolveWeights_StampedWeight_WinsOverDerived()
        {
            // Arrange - two walls; the first carries a hand-set Weight stamp that must survive derivation.
            Panel stamped = Wall(0);
            stamped.SetValue(SolverParameter.Weight, 0.9);
            List<Panel> sources = new List<Panel> { stamped, Wall(5) };

            // Act
            List<double> weights = Modify.ResolveWeights(null, sources, out List<ParameterProvenance> provenance);

            // Assert - the stamp wins (0.9), reported as Stamped; the unstamped wall derives.
            Assert.Equal(0.9, weights[0], 6);
            Assert.Equal(ParameterProvenance.Stamped, provenance[0]);
            Assert.Equal(ParameterProvenance.DerivedLength, provenance[1]);
        }

        [Fact]
        public void ResolveWeights_UnstampedWeights_DeriveByLength()
        {
            // Arrange - two unstamped walls of different length.
            List<Panel> sources = new List<Panel> { Wall(0), Wall(5) };

            // Act
            List<double> weights = Modify.ResolveWeights(null, sources, out List<ParameterProvenance> provenance);

            // Assert - both derived (the SAM_Solver length remap, in [0.2, 1.0]); none defaulted.
            Assert.All(provenance, p => Assert.Equal(ParameterProvenance.DerivedLength, p));
            Assert.All(weights, w => Assert.InRange(w, 0.2, 1.0));
        }

        // ── MaxExtend ───────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void ResolveMaxExtends_StampedMaxExtend_Wins()
        {
            // Arrange
            Panel stamped = Wall(0);
            stamped.SetValue(SolverParameter.MaxExtend, 1.25);
            List<Panel> sources = new List<Panel> { stamped, Wall(5) };

            // Act
            List<double> maxExtends = Modify.ResolveMaxExtends(null, sources, out List<ParameterProvenance> provenance);

            // Assert - the stamp wins; the unstamped wall falls back to the solver default (0.4, P2).
            Assert.Equal(1.25, maxExtends[0], 6);
            Assert.Equal(ParameterProvenance.Stamped, provenance[0]);
            Assert.Equal(0.4, maxExtends[1], 6);
            Assert.Equal(ParameterProvenance.Default, provenance[1]);
        }

        // ── BucketSize ──────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void BucketSize_StampedBucket_Wins()
        {
            // Arrange
            Panel panel = Wall(0);
            panel.SetValue(SolverParameter.BucketSize, 0.85);

            // Act
            double bucket = Modify.BucketSize(panel, 0.4, 0.6);
            List<ParameterProvenance> provenance = Modify.BucketProvenances(new List<Panel> { panel }, 0.4, 0.6);

            // Assert
            Assert.Equal(0.85, bucket, 6);
            Assert.Equal(ParameterProvenance.Stamped, provenance[0]);
        }

        [Fact]
        public void BucketSize_ThickConstruction_DerivesFromThickness()
        {
            // Arrange - a 1.0 m-thick construction: thickness × 0.6 = 0.6 m, above the 0.4 m floor.
            Construction thick = new Construction("Thick", new List<ConstructionLayer> { new ConstructionLayer("Core", 1.0) });
            Panel panel = Wall(0, thick);

            // Act
            double bucket = Modify.BucketSize(panel, 0.4, 0.6);
            List<ParameterProvenance> provenance = Modify.BucketProvenances(new List<Panel> { panel }, 0.4, 0.6);

            // Assert - derived from thickness (0.6), not floored.
            Assert.Equal(0.6, bucket, 6);
            Assert.Equal(ParameterProvenance.DerivedThickness, provenance[0]);
        }

        [Fact]
        public void BucketSize_NoThickness_FloorsAtMin()
        {
            // Arrange - a construction with no layers has no thickness.
            Panel panel = Wall(0);

            // Act
            double bucket = Modify.BucketSize(panel, 0.4, 0.6);
            List<ParameterProvenance> provenance = Modify.BucketProvenances(new List<Panel> { panel }, 0.4, 0.6);

            // Assert - floored at minBucketSize.
            Assert.Equal(0.4, bucket, 6);
            Assert.Equal(ParameterProvenance.MinFloor, provenance[0]);
        }

        [Fact]
        public void ProvenanceToTag_EachValue_ReturnsKebabTag()
        {
            Assert.Equal("stamped", ParameterProvenance.Stamped.ToTag());
            Assert.Equal("derived-length", ParameterProvenance.DerivedLength.ToTag());
            Assert.Equal("derived-thickness", ParameterProvenance.DerivedThickness.ToTag());
            Assert.Equal("min-floor", ParameterProvenance.MinFloor.ToTag());
            Assert.Equal("default", ParameterProvenance.Default.ToTag());
        }
    }
}
