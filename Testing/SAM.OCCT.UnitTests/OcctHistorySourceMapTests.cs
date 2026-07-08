// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Solver;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Pure-managed algebra of <see cref="HistorySourceMap.ToSourceMap"/> - the Phase 3 adapter that turns a
    /// native <see cref="OcctHistory"/> snapshot into a composable <see cref="SourceMap"/> hop
    /// (docs/P3_ABI_V4_NATIVE_HISTORY_DESIGN_REVIEW.md §K). No OCCT DLL required: the history is constructed
    /// directly through the public snapshot constructor.
    /// </summary>
    public class OcctHistorySourceMapTests
    {
        private static IReadOnlyList<IReadOnlyList<int>> Rows(params int[][] rows)
        {
            return rows.Select(x => (IReadOnlyList<int>)x).ToList();
        }

        [Fact]
        public void ToSourceMap_Split_OneInputFansOutToManyOrdinals()
        {
            // Input face 0 was split (Modified) into output ordinals 0 and 1.
            OcctHistory history = new OcctHistory(1, deletedInputs: null, modifiedOrdinals: Rows(new[] { 0, 1 }));

            SourceMap map = HistorySourceMap.ToSourceMap(history);

            Assert.Equal(new[] { new FaceKey(0), new FaceKey(1) }, map.FacesOf(0));
            Assert.Equal(new[] { 0 }, map.SourcesOf(new FaceKey(0)));
            Assert.Equal(new[] { 0 }, map.SourcesOf(new FaceKey(1)));
        }

        [Fact]
        public void ToSourceMap_Merge_ManyInputsCollapseToOneOrdinal()
        {
            // Inputs 0 and 1 were both Modified into the same output ordinal 0 (same-domain merge).
            OcctHistory history = new OcctHistory(2, deletedInputs: null, modifiedOrdinals: Rows(new[] { 0 }, new[] { 0 }));

            SourceMap map = HistorySourceMap.ToSourceMap(history);

            Assert.Equal(new[] { 0, 1 }, map.SourcesOf(new FaceKey(0)).OrderBy(x => x).ToArray());
        }

        [Fact]
        public void ToSourceMap_Deleted_InputExcludedAndNoGapDiagnostic()
        {
            OcctHistory history = new OcctHistory(2, deletedInputs: new[] { 1 }, modifiedOrdinals: Rows(new[] { 0 }, System.Array.Empty<int>()));
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            SourceMap map = HistorySourceMap.ToSourceMap(history, Provenance.Resolved, diagnostics);

            Assert.Empty(map.FacesOf(1));                       // deleted input maps nowhere
            Assert.Equal(new[] { 0 }, map.SourcesOf(new FaceKey(0)));
            Assert.Empty(diagnostics.OfCode(DiagnosticCode.HistoryGap)); // deletion is not a gap
        }

        [Fact]
        public void ToSourceMap_UnmappedNonDeletedInput_EmitsHistoryGapWarning()
        {
            // Input 1 is neither mapped nor deleted - a genuine gap.
            OcctHistory history = new OcctHistory(2, deletedInputs: null, modifiedOrdinals: Rows(new[] { 0 }, System.Array.Empty<int>()));
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            HistorySourceMap.ToSourceMap(history, Provenance.Resolved, diagnostics);

            SolverDiagnostic gap = Assert.Single(diagnostics.OfCode(DiagnosticCode.HistoryGap));
            Assert.Equal(OcctDiagnosticSeverity.Warning, gap.Severity);
        }

        [Fact]
        public void ToSourceMap_NullHistory_ReturnsNullForV3Fallback()
        {
            Assert.Null(HistorySourceMap.ToSourceMap(null));
        }

        [Fact]
        public void ToSourceMap_DeletedThenRegeneratedChain_ComposesToNothing()
        {
            // Hop A: input 0 -> intermediate ordinal 0. Hop B: intermediate face 0 deleted.
            OcctHistory hopA = new OcctHistory(1, deletedInputs: null, modifiedOrdinals: Rows(new[] { 0 }));
            OcctHistory hopB = new OcctHistory(1, deletedInputs: new[] { 0 }, modifiedOrdinals: Rows(System.Array.Empty<int>()));

            SourceMap composed = HistorySourceMap.ToSourceMap(hopA).Compose(HistorySourceMap.ToSourceMap(hopB));

            Assert.Empty(composed.FacesOf(0)); // the face deleted downstream leaves no output
        }

        [Fact]
        public void ToSourceMap_SharedFaceTwoOrdinals_ComposesAcrossDecodeOrder()
        {
            // The decode-order invariant: a face shared by two cells has two flat ordinals; a later
            // identity hop keyed by those ordinals must round-trip both (docs review §H).
            OcctHistory build = new OcctHistory(1, deletedInputs: null, modifiedOrdinals: Rows(new[] { 2, 5 }));
            SourceMap next = new SourceMap();
            next.Record(2, new FaceKey(2), Provenance.Resolved); // identity over the two shared ordinals
            next.Record(5, new FaceKey(5), Provenance.Resolved);

            SourceMap composed = HistorySourceMap.ToSourceMap(build).Compose(next);

            Assert.Equal(new[] { new FaceKey(2), new FaceKey(5) }, composed.FacesOf(0));
        }

        [Fact]
        public void ReportReverseGaps_OutputWithNoSource_EmitsHistoryGap()
        {
            SourceMap map = new SourceMap();
            map.Record(0, new FaceKey(0), Provenance.Resolved); // ordinal 1 has no source
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            HistorySourceMap.ReportReverseGaps(map, outputCount: 2, diagnostics: diagnostics);

            SolverDiagnostic gap = Assert.Single(diagnostics.OfCode(DiagnosticCode.HistoryGap));
            Assert.Equal(OcctDiagnosticSeverity.Warning, gap.Severity);
        }

        [Fact]
        public void OcctHistory_DeletedInputs_ReportsDeletedIndices()
        {
            OcctHistory history = new OcctHistory(3, deletedInputs: new[] { 0, 2 }, modifiedOrdinals: Rows(System.Array.Empty<int>(), new[] { 0 }, System.Array.Empty<int>()));

            Assert.Equal(new[] { 0, 2 }, history.DeletedInputs.OrderBy(x => x).ToArray());
            Assert.True(history.IsDeleted(0));
            Assert.False(history.IsDeleted(1));
        }
    }
}
