// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for P3 skip/risk observability (docs/CONTROLLED_WORKFLOW_PLAN.md §5.4-§5.5): a real extend/fill
    /// decision point that leaves a panel untouched is recorded as an <see cref="ExtendOutcome.Skipped"/>
    /// <see cref="ExtendRecord"/> with a reason (never a silent no-op), and the formatter routes Applied records to
    /// the frozen <c>SAM_OCCT_EXTEND3D_PANEL:</c> line (plus one <c>_RISKY:</c> line per risk flag) and Skipped
    /// records to <c>SAM_OCCT_EXTEND3D_SKIP:</c>. Pure-managed (no native).
    /// </summary>
    public class ExtendSkipTests
    {
        private const double VerticalAngle = 20 * System.Math.PI / 180;
        private const double Tolerance = 1e-6;

        private static SnappedPanel Cap2x2()
        {
            return new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 2, 0), new Point3D(0, 2, 0)), 1, 0.3, 0.5);
        }

        private static SnappedPanel Wall()
        {
            return new SnappedPanel(1, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 0, 3), new Point3D(0, 0, 3)), 1, 0.3, 0.5);
        }

        [Fact]
        public void Fill_MarginAtOrBelowTolerance_RecordsFillTooSmallSkipForEachCapNeverSilent()
        {
            // Arrange - a cap + a wall, but the configured margin is below tolerance: nothing can grow.
            List<SnappedPanel> panels = new List<SnappedPanel> { Cap2x2(), Wall() };
            List<ExtendRecord> records = new List<ExtendRecord>();

            // Act - margin (1e-9) <= tolerance (1e-6): the early real decision point in Fill.
            Panel3DSnapSolver.Fill(panels, VerticalAngle, margin: 1e-9, toleranceDistance: Tolerance, overshoot: 0.05, records: records);

            // Assert - the cap (non-vertical) is recorded as a FillTooSmall skip; the wall is not a cap so it is
            // not recorded here. Never a silent return.
            ExtendRecord skip = Assert.Single(records);
            Assert.Equal(ExtendOutcome.Skipped, skip.Outcome);
            Assert.Equal(ExtendOperationKind.CapGrow, skip.Kind);
            Assert.Equal(ExtendSkipReason.FillTooSmall, skip.SkipReason);
        }

        [Fact]
        public void ExtendRecordSkip_Factory_CarriesSkippedOutcomeReasonAndDetail()
        {
            // Act
            ExtendRecord skip = ExtendRecord.Skip(
                panelIndex: 3, sourceIndex: 1, kind: ExtendOperationKind.Top,
                reason: ExtendSkipReason.NoTargetWithinReach, detail: "nearest cap 0.62 m above reach 0.40",
                at: new Point3D(1, 1, 2));

            // Assert - a Skip is Skipped (not Applied), carries its reason/detail, and describes itself for the
            // SAM_OCCT_EXTEND3D_SKIP: line body.
            Assert.Equal(ExtendOutcome.Skipped, skip.Outcome);
            Assert.Equal(ExtendSkipReason.NoTargetWithinReach, skip.SkipReason);
            Assert.Equal("nearest cap 0.62 m above reach 0.40", skip.SkipDetail);
            string describe = skip.DescribeSkip();
            Assert.Contains("top", describe);
            Assert.Contains("NoTargetWithinReach", describe);
        }

        [Fact]
        public void FormatExtendRecords_SkippedRecord_EmitsSkipLineNotPanelLine()
        {
            // Arrange
            List<ExtendRecord> records = new List<ExtendRecord>
            {
                ExtendRecord.Skip(0, -1, ExtendOperationKind.Bottom, ExtendSkipReason.AlreadyMeetsTarget, "already at cap")
            };

            // Act
            List<string> lines = SolverReportFormat.FormatExtendRecords(records, new List<Panel>());

            // Assert - the skip surfaces as a SKIP line, never as an applied PANEL line.
            Assert.Contains(lines, l => l.StartsWith("SAM_OCCT_EXTEND3D_SKIP:") && l.Contains("AlreadyMeetsTarget"));
            Assert.DoesNotContain(lines, l => l.StartsWith("SAM_OCCT_EXTEND3D_PANEL:"));
        }

        [Fact]
        public void FormatExtendRecords_AppliedRecordWithRiskFlag_EmitsFrozenPanelLinePlusRiskyLine()
        {
            // Arrange - an applied lateral move carrying a NearReachLimit risk flag.
            ExtendRecord applied = new ExtendRecord(
                panelIndex: 5, sourceIndex: -1, kind: ExtendOperationKind.PlanEnd,
                fromValue: 0, toValue: 0.38, measureUnit: "plan",
                from: new Point3D(0, 0, 0), to: new Point3D(0.38, 0, 0),
                targetPanelIndex: -1, targetSourceIndex: -1, targetKind: "walls", targetDescription: "2D plan-loop junction",
                overshoot: 0.05, maxExtendCapped: true);
            applied.AddRisk(ExtendRiskFlag.NearReachLimit);

            // Act
            List<string> lines = SolverReportFormat.FormatExtendRecords(new List<ExtendRecord> { applied }, new List<Panel>());

            // Assert - the frozen applied line AND a risky line naming the flag; no skip line.
            Assert.Contains(lines, l => l.StartsWith("SAM_OCCT_EXTEND3D_PANEL:"));
            Assert.Contains(lines, l => l.StartsWith("SAM_OCCT_EXTEND3D_RISKY:") && l.Contains("NearReachLimit"));
            Assert.DoesNotContain(lines, l => l.StartsWith("SAM_OCCT_EXTEND3D_SKIP:"));
        }
    }
}
