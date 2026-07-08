// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Truth table for <see cref="Panel3DSnapSolver.EvaluateRawAdoption"/> - the raw-first (L0)
    /// adoption gate's decision rule (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md, Phase 1).
    /// Pure and native-free: no OCCT DLL required.
    /// </summary>
    public class RawAdoptionGateTests
    {
        private const double MinVolume = 0.05;
        private const double MaxDroppedRatio = 0.10;

        [Fact]
        public void EvaluateRawAdoption_AllChecksPass_ReturnsAdopted()
        {
            RawAdoptionOutcome outcome = Panel3DSnapSolver.EvaluateRawAdoption(
                cellCount: 22, resolvedFaceCount: 148, nakedEdgeCount: 0, sliverCellCount: 0, droppedRatio: 0.05, maxDroppedRatio: MaxDroppedRatio);

            Assert.Equal(RawAdoptionOutcome.Adopted, outcome);
        }

        [Fact]
        public void EvaluateRawAdoption_ZeroCells_ReturnsRejectedNoCells()
        {
            RawAdoptionOutcome outcome = Panel3DSnapSolver.EvaluateRawAdoption(
                cellCount: 0, resolvedFaceCount: 10, nakedEdgeCount: 0, sliverCellCount: 0, droppedRatio: 0, maxDroppedRatio: MaxDroppedRatio);

            Assert.Equal(RawAdoptionOutcome.RejectedNoCells, outcome);
        }

        [Fact]
        public void EvaluateRawAdoption_ZeroResolvedFaces_ReturnsRejectedNoCells()
        {
            RawAdoptionOutcome outcome = Panel3DSnapSolver.EvaluateRawAdoption(
                cellCount: 1, resolvedFaceCount: 0, nakedEdgeCount: 0, sliverCellCount: 0, droppedRatio: 0, maxDroppedRatio: MaxDroppedRatio);

            Assert.Equal(RawAdoptionOutcome.RejectedNoCells, outcome);
        }

        [Fact]
        public void EvaluateRawAdoption_NakedEdgesPresent_ReturnsRejectedNakedEdges()
        {
            RawAdoptionOutcome outcome = Panel3DSnapSolver.EvaluateRawAdoption(
                cellCount: 5, resolvedFaceCount: 20, nakedEdgeCount: 1, sliverCellCount: 0, droppedRatio: 0, maxDroppedRatio: MaxDroppedRatio);

            Assert.Equal(RawAdoptionOutcome.RejectedNakedEdges, outcome);
        }

        [Fact]
        public void EvaluateRawAdoption_SliverCellPresent_ReturnsRejectedSliverCell()
        {
            RawAdoptionOutcome outcome = Panel3DSnapSolver.EvaluateRawAdoption(
                cellCount: 5, resolvedFaceCount: 20, nakedEdgeCount: 0, sliverCellCount: 1, droppedRatio: 0, maxDroppedRatio: MaxDroppedRatio);

            Assert.Equal(RawAdoptionOutcome.RejectedSliverCell, outcome);
        }

        [Fact]
        public void EvaluateRawAdoption_DroppedRatioBeyondMax_ReturnsRejectedDroppedRatio()
        {
            RawAdoptionOutcome outcome = Panel3DSnapSolver.EvaluateRawAdoption(
                cellCount: 5, resolvedFaceCount: 20, nakedEdgeCount: 0, sliverCellCount: 0, droppedRatio: 0.11, maxDroppedRatio: MaxDroppedRatio);

            Assert.Equal(RawAdoptionOutcome.RejectedDroppedRatio, outcome);
        }

        [Fact]
        public void EvaluateRawAdoption_DroppedRatioExactlyAtMax_ReturnsAdopted()
        {
            // The gate rejects only when the ratio EXCEEDS the max (strictly greater than), so a model
            // sitting exactly on the configured ceiling is still trusted.
            RawAdoptionOutcome outcome = Panel3DSnapSolver.EvaluateRawAdoption(
                cellCount: 5, resolvedFaceCount: 20, nakedEdgeCount: 0, sliverCellCount: 0, droppedRatio: MaxDroppedRatio, maxDroppedRatio: MaxDroppedRatio);

            Assert.Equal(RawAdoptionOutcome.Adopted, outcome);
        }

        [Fact]
        public void EvaluateRawAdoption_NakedEdgesAndSliverCellBothPresent_NakedEdgesTakePrecedence()
        {
            // Precedence order: no-cells, then naked edges, then sliver cell, then dropped ratio - matches
            // TryRawResolve's short-circuit (it never measures sliver/dropped once a naked edge is found).
            RawAdoptionOutcome outcome = Panel3DSnapSolver.EvaluateRawAdoption(
                cellCount: 5, resolvedFaceCount: 20, nakedEdgeCount: 2, sliverCellCount: 3, droppedRatio: 0.99, maxDroppedRatio: MaxDroppedRatio);

            Assert.Equal(RawAdoptionOutcome.RejectedNakedEdges, outcome);
        }

        [Fact]
        public void EvaluateRawAdoption_SliverCellAndDroppedRatioBothPresent_SliverCellTakesPrecedence()
        {
            RawAdoptionOutcome outcome = Panel3DSnapSolver.EvaluateRawAdoption(
                cellCount: 5, resolvedFaceCount: 20, nakedEdgeCount: 0, sliverCellCount: 1, droppedRatio: 0.99, maxDroppedRatio: MaxDroppedRatio);

            Assert.Equal(RawAdoptionOutcome.RejectedSliverCell, outcome);
        }

        [Theory]
        [InlineData(0.05, 0.05, RawAdoptionOutcome.Adopted)]      // matches the default golden-master margin
        [InlineData(0.25, 0.30, RawAdoptionOutcome.Adopted)]      // tilted-two-spaces' observed natural ratio, under the calibrated default
        [InlineData(0.31, 0.30, RawAdoptionOutcome.RejectedDroppedRatio)]
        public void EvaluateRawAdoption_DroppedRatioAgainstCalibratedDefault_MatchesExpectedOutcome(double droppedRatio, double maxDroppedRatio, RawAdoptionOutcome expected)
        {
            RawAdoptionOutcome outcome = Panel3DSnapSolver.EvaluateRawAdoption(
                cellCount: 1, resolvedFaceCount: 6, nakedEdgeCount: 0, sliverCellCount: 0, droppedRatio: droppedRatio, maxDroppedRatio: maxDroppedRatio);

            Assert.Equal(expected, outcome);
        }
    }
}
