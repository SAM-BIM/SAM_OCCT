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
    }
}
