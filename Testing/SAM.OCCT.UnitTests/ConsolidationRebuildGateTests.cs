// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Truth table for <see cref="Panel3DSnapSolver.AcceptConsolidationRebuild"/> - the consolidation-rebuild
    /// acceptance rule hardened in P4 (codex #3, docs/CELLCOMPLEX_FIRST_HANDOVER.md). Pure and native-free.
    /// The regression baseline is the APPENDED set's own decoded cell/naked counts (the fallback the rebuild
    /// would replace), not the lower pre-append resolve count that previously let a separator-dissolving
    /// rebuild through.
    /// </summary>
    public class ConsolidationRebuildGateTests
    {
        [Fact]
        public void AcceptConsolidationRebuild_KeepsCellsAndNaked_Accepts()
        {
            // The rebuild imprints the separators cleanly: same cells, same (zero) naked as the fallback.
            Assert.True(Panel3DSnapSolver.AcceptConsolidationRebuild(
                rebuiltFaceCount: 40, rebuiltCellCount: 2, rebuiltNakedEdgeCount: 0, appendedCellCount: 2, appendedNakedEdgeCount: 0));
        }

        [Fact]
        public void AcceptConsolidationRebuild_ImprovesCellsAndNaked_Accepts()
        {
            // A strict improvement (more cells formed, fewer naked edges) is of course accepted.
            Assert.True(Panel3DSnapSolver.AcceptConsolidationRebuild(
                rebuiltFaceCount: 60, rebuiltCellCount: 3, rebuiltNakedEdgeCount: 0, appendedCellCount: 2, appendedNakedEdgeCount: 4));
        }

        [Fact]
        public void AcceptConsolidationRebuild_DissolvesSeparator_RejectsFewerCellsThanAppended()
        {
            // The codex #3 case: the direct rebuild dissolves a shared separator, forming FEWER cells than the
            // appended fallback (2 rooms -> 1). It must be rejected so the appended set (which keeps both rooms)
            // is retained. Pre-fix this compared against the lower pre-append count and was wrongly accepted.
            Assert.False(Panel3DSnapSolver.AcceptConsolidationRebuild(
                rebuiltFaceCount: 30, rebuiltCellCount: 1, rebuiltNakedEdgeCount: 0, appendedCellCount: 2, appendedNakedEdgeCount: 0));
        }

        [Fact]
        public void AcceptConsolidationRebuild_OpensNakedEdges_Rejects()
        {
            // Same cells but the rebuild opened new naked edges versus the fallback: a regression, rejected.
            Assert.False(Panel3DSnapSolver.AcceptConsolidationRebuild(
                rebuiltFaceCount: 40, rebuiltCellCount: 2, rebuiltNakedEdgeCount: 6, appendedCellCount: 2, appendedNakedEdgeCount: 0));
        }

        [Fact]
        public void AcceptConsolidationRebuild_NoFaces_Rejects()
        {
            // An empty rebuild (native produced nothing usable) is never adopted.
            Assert.False(Panel3DSnapSolver.AcceptConsolidationRebuild(
                rebuiltFaceCount: 0, rebuiltCellCount: 5, rebuiltNakedEdgeCount: 0, appendedCellCount: 2, appendedNakedEdgeCount: 0));
        }
    }
}
