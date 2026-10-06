// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Solver;
using System.Collections.Generic;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="ClosureSignature3D"/> - the 3D golden-master fingerprint
    /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md, Phase 0). All tests are pure-managed:
    /// no native OCCT DLL required.
    /// </summary>
    public class ClosureSignature3DTests
    {
        private static ClosureSignature3D Make(int cellCount, IEnumerable<double> cellVolumes, int nakedEdgeCount, int faceCount = 0, int droppedCount = 0)
        {
            return new ClosureSignature3D(cellCount, cellVolumes, nakedEdgeCount, faceCount, droppedCount);
        }

        // ──────────────────────────────────────────────────────────────
        // Construction
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void Constructor_CellVolumesSupplied_TotalVolumeIsSum()
        {
            ClosureSignature3D signature = Make(2, new List<double> { 10.0, 15.5 }, 0);

            Assert.Equal(25.5, signature.TotalVolume, 6);
            Assert.Equal(2, signature.CellVolumes.Count);
        }

        [Fact]
        public void Constructor_NullCellVolumes_TotalVolumeIsZero()
        {
            ClosureSignature3D signature = Make(0, null, 0);

            Assert.Equal(0.0, signature.TotalVolume, 6);
            Assert.Empty(signature.CellVolumes);
        }

        [Fact]
        public void Constructor_FieldsSupplied_RoundTrip()
        {
            ClosureSignature3D signature = Make(22, new List<double> { 1, 2, 3 }, nakedEdgeCount: 4, faceCount: 90, droppedCount: 1);

            Assert.Equal(22, signature.CellCount);
            Assert.Equal(4, signature.NakedEdgeCount);
            Assert.Equal(90, signature.FaceCount);
            Assert.Equal(1, signature.DroppedCount);
        }

        // ──────────────────────────────────────────────────────────────
        // IsRegressionOf - truth table
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void IsRegressionOf_NullOther_ReturnsFalse()
        {
            ClosureSignature3D signature = Make(1, new List<double> { 1 }, 0);

            Assert.False(signature.IsRegressionOf(null));
        }

        [Fact]
        public void IsRegressionOf_IdenticalSignature_ReturnsFalse()
        {
            ClosureSignature3D baseline = Make(22, new List<double> { 10, 10 }, 0);
            ClosureSignature3D candidate = Make(22, new List<double> { 10, 10 }, 0);

            Assert.False(candidate.IsRegressionOf(baseline));
        }

        [Fact]
        public void IsRegressionOf_MoreNakedEdges_ReturnsTrue()
        {
            ClosureSignature3D baseline = Make(22, new List<double> { 20 }, 0);
            ClosureSignature3D candidate = Make(22, new List<double> { 20 }, 1);

            Assert.True(candidate.IsRegressionOf(baseline));
        }

        [Fact]
        public void IsRegressionOf_FewerNakedEdges_ReturnsFalse()
        {
            ClosureSignature3D baseline = Make(22, new List<double> { 20 }, 5);
            ClosureSignature3D candidate = Make(22, new List<double> { 20 }, 1);

            Assert.False(candidate.IsRegressionOf(baseline));
        }

        [Fact]
        public void IsRegressionOf_FewerCells_ReturnsTrue()
        {
            ClosureSignature3D baseline = Make(22, new List<double> { 20 }, 0);
            ClosureSignature3D candidate = Make(21, new List<double> { 20 }, 0);

            Assert.True(candidate.IsRegressionOf(baseline));
        }

        [Fact]
        public void IsRegressionOf_MoreCells_ReturnsFalse()
        {
            ClosureSignature3D baseline = Make(22, new List<double> { 20 }, 0);
            ClosureSignature3D candidate = Make(23, new List<double> { 20 }, 0);

            Assert.False(candidate.IsRegressionOf(baseline));
        }

        [Fact]
        public void IsRegressionOf_VolumeDropBeyondTolerance_ReturnsTrue()
        {
            ClosureSignature3D baseline = Make(22, new List<double> { 100 }, 0);
            ClosureSignature3D candidate = Make(22, new List<double> { 99 }, 0); // 1 m3 drop

            Assert.True(candidate.IsRegressionOf(baseline, volumeTolerance: 0.5));
        }

        [Fact]
        public void IsRegressionOf_VolumeDropWithinTolerance_ReturnsFalse()
        {
            ClosureSignature3D baseline = Make(22, new List<double> { 100 }, 0);
            ClosureSignature3D candidate = Make(22, new List<double> { 99.99 }, 0); // 0.01 m3 drop

            Assert.False(candidate.IsRegressionOf(baseline, volumeTolerance: 0.1));
        }

        [Fact]
        public void IsRegressionOf_VolumeIncrease_ReturnsFalse()
        {
            ClosureSignature3D baseline = Make(22, new List<double> { 100 }, 0);
            ClosureSignature3D candidate = Make(22, new List<double> { 150 }, 0);

            Assert.False(candidate.IsRegressionOf(baseline));
        }

        [Fact]
        public void IsRegressionOf_ImprovedOnEveryAxis_ReturnsFalse()
        {
            ClosureSignature3D baseline = Make(18, new List<double> { 80 }, 12);
            ClosureSignature3D candidate = Make(22, new List<double> { 100 }, 0);

            Assert.False(candidate.IsRegressionOf(baseline));
        }

        // ──────────────────────────────────────────────────────────────
        // FromCellComplexResult
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void FromCellComplexResult_NullResult_ReturnsEmptySignature()
        {
            ClosureSignature3D signature = ClosureSignature3D.FromCellComplexResult(null, nakedEdgeCount: 3, faceCount: 5);

            Assert.Equal(0, signature.CellCount);
            Assert.Equal(0.0, signature.TotalVolume, 6);
            Assert.Equal(3, signature.NakedEdgeCount);
            Assert.Equal(5, signature.FaceCount);
        }

        [Fact]
        public void FromCellComplexResult_ResultWithCells_ReadsCountAndVolumes()
        {
            OcctCellComplexResult result = new OcctCellComplexResult();
            result.AddCell(new OcctCell(TestGeometry.CreateSingleFaceShell(), 12.5));
            result.AddCell(new OcctCell(TestGeometry.CreateSingleFaceShell(), 7.5));

            ClosureSignature3D signature = ClosureSignature3D.FromCellComplexResult(result, nakedEdgeCount: 0, faceCount: 2, droppedCount: 1);

            Assert.Equal(2, signature.CellCount);
            Assert.Equal(20.0, signature.TotalVolume, 6);
            Assert.Equal(1, signature.DroppedCount);
        }

        // ──────────────────────────────────────────────────────────────
        // SliverCellCount (Phase 5a - additive)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void Constructor_NoSliverCountSupplied_DefaultsToZero()
        {
            // Arrange & Act - the pre-5a 5-arg constructor path (no sliver term).
            ClosureSignature3D signature = new ClosureSignature3D(2, new List<double> { 10, 15 }, 0, 6, 0);

            // Assert - additive term defaults to 0, so existing callers are unchanged.
            Assert.Equal(0, signature.SliverCellCount);
        }

        [Fact]
        public void Constructor_SliverCountSupplied_RoundTrips()
        {
            // Arrange & Act
            ClosureSignature3D signature = new ClosureSignature3D(3, new List<double> { 10, 15, 0.001 }, 0, 9, 0, sliverCellCount: 1);

            // Assert
            Assert.Equal(1, signature.SliverCellCount);
        }

        [Fact]
        public void FromCellComplexResult_WithMinCellVolume_CountsCellsBelowThreshold()
        {
            // Arrange - two genuine cells and one sliver (0.001 m3, below the 0.05 floor).
            OcctCellComplexResult result = new OcctCellComplexResult();
            result.AddCell(new OcctCell(TestGeometry.CreateSingleFaceShell(), 12.5));
            result.AddCell(new OcctCell(TestGeometry.CreateSingleFaceShell(), 0.001));
            result.AddCell(new OcctCell(TestGeometry.CreateSingleFaceShell(), 7.5));

            // Act
            ClosureSignature3D signature = ClosureSignature3D.FromCellComplexResult(result, nakedEdgeCount: 0, faceCount: 3, droppedCount: 0, minCellVolume: 0.05);

            // Assert - the sliver term counts only the sub-threshold cell; totals are unaffected.
            Assert.Equal(3, signature.CellCount);
            Assert.Equal(1, signature.SliverCellCount);
            Assert.Equal(20.001, signature.TotalVolume, 6);
        }

        [Fact]
        public void FromCellComplexResult_WithoutMinCellVolume_LeavesSliverCountZero()
        {
            // Arrange - a sub-0.05 cell present, but the 4-arg (pre-5a) overload does not measure slivers.
            OcctCellComplexResult result = new OcctCellComplexResult();
            result.AddCell(new OcctCell(TestGeometry.CreateSingleFaceShell(), 0.001));

            // Act
            ClosureSignature3D signature = ClosureSignature3D.FromCellComplexResult(result, nakedEdgeCount: 0, faceCount: 1, droppedCount: 0);

            // Assert
            Assert.Equal(0, signature.SliverCellCount);
        }

        [Fact]
        public void ToString_WithSliverCount_IncludesSliverTerm()
        {
            // Arrange
            ClosureSignature3D signature = new ClosureSignature3D(1, new List<double> { 5 }, 0, 6, 0, sliverCellCount: 2);

            // Act
            string text = signature.ToString();

            // Assert
            Assert.Contains("2 sliver cell(s)", text);
        }

        // ──────────────────────────────────────────────────────────────
        // IsRegressionOf semantics UNCHANGED by the additive sliver term
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void IsRegressionOf_MoreSliverCellsOnly_ReturnsFalse()
        {
            // Arrange - candidate is worse ONLY on the (new) sliver axis; every locked axis is equal.
            ClosureSignature3D baseline = new ClosureSignature3D(22, new List<double> { 100 }, 0, 90, 0, sliverCellCount: 0);
            ClosureSignature3D candidate = new ClosureSignature3D(22, new List<double> { 100 }, 0, 90, 0, sliverCellCount: 5);

            // Assert - IsRegressionOf ignores the sliver term (its semantics are locked); the
            // sliver gate lives in the separate Phase-5e acceptance helper, not here.
            Assert.False(candidate.IsRegressionOf(baseline));
        }

        [Fact]
        public void IsRegressionOf_FewerSliverCellsOnly_ReturnsFalse()
        {
            // Arrange - candidate is better on the sliver axis; still not a regression, and not
            // spuriously "improved" via a term IsRegressionOf must not read.
            ClosureSignature3D baseline = new ClosureSignature3D(22, new List<double> { 100 }, 0, 90, 0, sliverCellCount: 5);
            ClosureSignature3D candidate = new ClosureSignature3D(22, new List<double> { 100 }, 0, 90, 0, sliverCellCount: 0);

            // Assert
            Assert.False(candidate.IsRegressionOf(baseline));
        }
    }
}
