// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for P3 directional cap growth (docs/CONTROLLED_WORKFLOW_PLAN.md §5.2-§5.3):
    /// <see cref="SnappedPanel.GrowEdgesToWalls"/> grows each external cap edge by only its OWN measured gap to a
    /// wall that actually FACES it; an edge with no facing wall grows exactly 0 (the D4 false-floor guard), holes
    /// are preserved, and when the per-edge reconstruction finds no evidence the caller falls back to the legacy
    /// uniform grow with a <see cref="ExtendRiskFlag.LegacyUniformCapGrow"/> risk flag. Pure-managed (no native).
    /// </summary>
    public class FillDirectionalTests
    {
        private const double VerticalAngle = 20 * System.Math.PI / 180;
        private const double Tolerance = 1e-6;

        /// <summary>A 2x2 floor cap in the z=0 plane (Z-normal), external boundary CCW.</summary>
        private static SnappedPanel Cap2x2()
        {
            return new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 2, 0), new Point3D(0, 2, 0)), 1, 0.3, 0.5);
        }

        /// <summary>A vertical wall panel in the plane x=<paramref name="x"/> (normal ±X), spanning y[0,2] z[0,3] -
        /// it runs ALONG (faces) the cap's right edge, positioned outward of it by (x - 2).</summary>
        private static SnappedPanel FacingWallAtX(double x)
        {
            return new SnappedPanel(1, TestGeometry.CreatePlanarFace(
                new Point3D(x, 0, 0), new Point3D(x, 2, 0), new Point3D(x, 2, 3), new Point3D(x, 0, 3)), 1, 0.3, 0.5);
        }

        [Fact]
        public void GrowEdgesToWalls_EdgeWithFacingWall_GrowsOnlyThatEdgeByMeasuredGapPlusOvershoot()
        {
            // Arrange - a 2x2 cap with one wall facing its right (+X) edge, 0.3 m outward (at x=2.3).
            SnappedPanel cap = Cap2x2();
            List<SnappedPanel> walls = new List<SnappedPanel> { FacingWallAtX(2.3) };
            double areaBefore = cap.GetArea();

            // Act
            bool grew = cap.GrowEdgesToWalls(walls, maxReach: 0.5, overshoot: 0.05, tolerance: Tolerance);

            // Assert - the right edge grew by the measured gap (0.3) + overshoot (0.05) = 0.35; every other edge
            // stayed put (no facing wall), so only +X moved and the cap did not balloon uniformly.
            Assert.True(grew);
            BoundingBox3D box = cap.Face3D.GetBoundingBox();
            Assert.Equal(2.35, box.Max.X, 3);   // right edge reached the wall + overshoot
            Assert.Equal(0.0, box.Min.X, 3);    // left edge unmoved (no facing wall)
            Assert.Equal(0.0, box.Min.Y, 3);    // bottom edge unmoved
            Assert.Equal(2.0, box.Max.Y, 3);    // top edge unmoved
            Assert.True(cap.GetArea() > areaBefore + Tolerance);
        }

        [Fact]
        public void GrowEdgesToWalls_NoFacingWallWithinReach_ReturnsFalseAndLeavesCapUnchanged()
        {
            // Arrange - the only wall sits 3 m away (x=5), far beyond the 0.5 m reach: no edge has evidence.
            SnappedPanel cap = Cap2x2();
            List<SnappedPanel> walls = new List<SnappedPanel> { FacingWallAtX(5.0) };
            double areaBefore = cap.GetArea();

            // Act
            bool grew = cap.GrowEdgesToWalls(walls, maxReach: 0.5, overshoot: 0.05, tolerance: Tolerance);

            // Assert - fail closed: no growth, nothing moved, the caller is told to fall back.
            Assert.False(grew);
            Assert.Equal(areaBefore, cap.GetArea(), 6);
        }

        [Fact]
        public void GrowEdgesToWalls_CapWithHole_PreservesTheHole()
        {
            // Arrange - a 4x4 cap with a 1x1 internal hole, and a wall facing its right edge (at x=4.3).
            Face3D withHole = Face3D.Create(
                new List<Geometry.Spatial.IClosedPlanar3D>
                {
                    new Polygon3D(new List<Point3D> { new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(0, 4, 0) }),
                    new Polygon3D(new List<Point3D> { new Point3D(1, 1, 0), new Point3D(2, 1, 0), new Point3D(2, 2, 0), new Point3D(1, 2, 0) })
                });
            SnappedPanel cap = new SnappedPanel(0, withHole, 1, 0.3, 0.5);
            Assert.Equal(1, cap.Face3D.GetInternalEdge3Ds()?.Count ?? 0); // sanity: the hole is present before
            List<SnappedPanel> walls = new List<SnappedPanel> { FacingWallAtX(4.3) };

            // Act
            bool grew = cap.GrowEdgesToWalls(walls, maxReach: 0.5, overshoot: 0.05, tolerance: Tolerance);

            // Assert - grew AND the internal hole survived the boundary rebuild (§5.3 "holes preserved").
            Assert.True(grew);
            Assert.Equal(1, cap.Face3D.GetInternalEdge3Ds()?.Count ?? 0);
        }

        [Fact]
        public void Fill_DirectionalOnWithFacingWall_RecordsWallsDirectionalWithoutFallbackRisk()
        {
            // Arrange - one cap, one facing wall; directional growth should succeed for the cap.
            List<SnappedPanel> panels = new List<SnappedPanel> { Cap2x2(), FacingWallAtX(2.3) };
            List<ExtendRecord> records = new List<ExtendRecord>();

            // Act
            Panel3DSnapSolver.Fill(panels, VerticalAngle, margin: 0.5, toleranceDistance: Tolerance, overshoot: 0.05, records: records, directionalCapGrow: true);

            // Assert - the cap-grow record is tagged walls-directional and carries NO legacy-uniform fallback risk.
            ExtendRecord capGrow = Assert.Single(records, r => r.Kind == ExtendOperationKind.CapGrow && r.Outcome == ExtendOutcome.Applied);
            Assert.Equal("walls-directional", capGrow.TargetKind);
            Assert.DoesNotContain(ExtendRiskFlag.LegacyUniformCapGrow, capGrow.RiskFlags);
        }

        [Fact]
        public void Fill_DirectionalOnButNoFacingWall_FallsBackToFixedMarginAndFlagsLegacyUniform()
        {
            // Arrange - a lone cap with NO walls: directional growth and the measured grow both find no wall, so
            // the fixed-margin fallback runs - and P3 §5.5 flags that the requested directional mode was not used.
            List<SnappedPanel> panels = new List<SnappedPanel> { Cap2x2() };
            List<ExtendRecord> records = new List<ExtendRecord>();

            // Act
            Panel3DSnapSolver.Fill(panels, VerticalAngle, margin: 0.5, toleranceDistance: Tolerance, overshoot: 0.05, records: records, directionalCapGrow: true);

            // Assert - grew via the fixed-margin fallback, tagged and risk-flagged so the fallback is never silent.
            ExtendRecord capGrow = Assert.Single(records, r => r.Kind == ExtendOperationKind.CapGrow && r.Outcome == ExtendOutcome.Applied);
            Assert.Equal("fixed-margin", capGrow.TargetKind);
            Assert.Contains(ExtendRiskFlag.LegacyUniformCapGrow, capGrow.RiskFlags);
        }
    }
}
