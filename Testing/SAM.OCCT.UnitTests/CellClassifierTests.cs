// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Truth table for <see cref="CellClassifier.Classify"/> - the Phase 7b cell-role decision rule
    /// (docs/P6_ARCHITECTURE_REVIEW.md §P sub-step 7b). Pure and native-free: no OCCT DLL required.
    /// </summary>
    public class CellClassifierTests
    {
        private const double MinVolume = 0.05;

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [InlineData(null)]
        public void Classify_VolumeBelowMinimum_ReturnsSliverRegardlessOfInsideEnvelope(bool? insideEnvelope)
        {
            CellRole role = CellClassifier.Classify(volume: 0.01, minCellVolume: MinVolume, insideEnvelope: insideEnvelope);

            Assert.Equal(CellRole.Sliver, role);
        }

        [Fact]
        public void Classify_VolumeAtMinimumInsideEnvelope_ReturnsInterior()
        {
            // volume == minCellVolume is NOT a sliver: the gate is strictly "< minimum".
            CellRole role = CellClassifier.Classify(volume: MinVolume, minCellVolume: MinVolume, insideEnvelope: true);

            Assert.Equal(CellRole.Interior, role);
        }

        [Fact]
        public void Classify_VolumeAboveMinimumInsideEnvelope_ReturnsInterior()
        {
            CellRole role = CellClassifier.Classify(volume: 10.0, minCellVolume: MinVolume, insideEnvelope: true);

            Assert.Equal(CellRole.Interior, role);
        }

        [Fact]
        public void Classify_VolumeAboveMinimumOutsideEnvelope_ReturnsExterior()
        {
            CellRole role = CellClassifier.Classify(volume: 10.0, minCellVolume: MinVolume, insideEnvelope: false);

            Assert.Equal(CellRole.Exterior, role);
        }

        [Fact]
        public void Classify_VolumeAboveMinimumEnvelopeUnevaluated_ReturnsUnknown()
        {
            // Native kernel unavailable, or the cell had no decoded centre - never silently Interior.
            CellRole role = CellClassifier.Classify(volume: 10.0, minCellVolume: MinVolume, insideEnvelope: null);

            Assert.Equal(CellRole.Unknown, role);
        }

        [Fact]
        public void ClassifyCells_NullCells_ReturnsEmpty()
        {
            var roles = CellClassifier.ClassifyCells(cells: null, resolvedFace3Ds: null, minCellVolume: MinVolume, options: null);

            Assert.Empty(roles);
        }

        [Fact]
        public void ClassifyCells_EmptyCells_ReturnsEmpty()
        {
            var roles = CellClassifier.ClassifyCells(cells: new System.Collections.Generic.List<SolverCell>(), resolvedFace3Ds: null, minCellVolume: MinVolume, options: null);

            Assert.Empty(roles);
        }
    }
}
