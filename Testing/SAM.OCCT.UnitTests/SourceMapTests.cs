// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Compose / merge / split invariants for <see cref="SourceMap"/> - the provenance record threaded
    /// through the managed solver stages (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §G,
    /// Phase 2). Pure-managed: no OCCT DLL required.
    /// </summary>
    public class SourceMapTests
    {
        [Fact]
        public void Record_SingleSourceToFace_FacesOfAndSourcesOfRoundTrip()
        {
            SourceMap map = new SourceMap();

            map.Record(3, new FaceKey(7), Provenance.Snapped);

            Assert.Equal(new[] { new FaceKey(7) }, map.FacesOf(3));
            Assert.Equal(new[] { 3 }, map.SourcesOf(new FaceKey(7)));
        }

        [Fact]
        public void Record_DuplicateEntry_NotDoubleCounted()
        {
            SourceMap map = new SourceMap();

            map.Record(1, new FaceKey(1), Provenance.Snapped);
            map.Record(1, new FaceKey(1), Provenance.Snapped);

            Assert.Single(map.FacesOf(1));
        }

        [Fact]
        public void RecordMerge_SeveralSourcesToOneFace_AllSourcesResolve()
        {
            SourceMap map = new SourceMap();

            map.RecordMerge(new[] { 2, 4, 6 }, new FaceKey(9), Provenance.Snapped);

            IReadOnlyList<int> sources = map.SourcesOf(new FaceKey(9));
            Assert.Equal(new[] { 2, 4, 6 }, sources.OrderBy(x => x).ToArray());
            Assert.Equal(new[] { new FaceKey(9) }, map.FacesOf(2));
            Assert.Equal(new[] { new FaceKey(9) }, map.FacesOf(4));
        }

        [Fact]
        public void RecordSplit_OneSourceToSeveralFaces_AllFacesResolve()
        {
            SourceMap map = new SourceMap();

            map.RecordSplit(5, new[] { new FaceKey(1), new FaceKey(2), new FaceKey(3) }, Provenance.Resolved);

            IReadOnlyList<FaceKey> faces = map.FacesOf(5);
            Assert.Equal(new[] { 1, 2, 3 }, faces.Select(x => x.Value).OrderBy(x => x).ToArray());
        }

        [Fact]
        public void RecordFabricated_NoSource_HasSourceIsFalse()
        {
            SourceMap map = new SourceMap();

            map.RecordFabricated(new FaceKey(11), Provenance.GapFill);

            Assert.False(map.HasSource(new FaceKey(11)));
            Assert.Equal(new[] { SourceMap.FabricatedSource }, map.SourcesOf(new FaceKey(11)));
        }

        [Fact]
        public void HasSource_FaceWithRealSource_ReturnsTrue()
        {
            SourceMap map = new SourceMap();

            map.Record(0, new FaceKey(4), Provenance.Snapped);

            Assert.True(map.HasSource(new FaceKey(4)));
        }

        [Fact]
        public void Compose_ChainedOneToOne_YieldsSourceToOutput()
        {
            // Stage 1: source 3 -> intermediate face 7. Stage 2: intermediate face 7 -> output face 2.
            SourceMap first = new SourceMap();
            first.Record(3, new FaceKey(7), Provenance.Snapped);

            SourceMap second = new SourceMap();
            second.Record(7, new FaceKey(2), Provenance.Resolved);

            SourceMap composed = first.Compose(second);

            Assert.Equal(new[] { new FaceKey(2) }, composed.FacesOf(3));
            Assert.Equal(new[] { 3 }, composed.SourcesOf(new FaceKey(2)));
        }

        [Fact]
        public void Compose_MergeThenSplit_TransitivelyTracksAllSources()
        {
            // Stage 1 merge: sources 1,2 -> intermediate face 5.
            SourceMap first = new SourceMap();
            first.RecordMerge(new[] { 1, 2 }, new FaceKey(5), Provenance.Snapped);

            // Stage 2 split: intermediate face 5 -> output faces 8, 9.
            SourceMap second = new SourceMap();
            second.RecordSplit(5, new[] { new FaceKey(8), new FaceKey(9) }, Provenance.Resolved);

            SourceMap composed = first.Compose(second);

            // Both original sources reach both output faces.
            Assert.Equal(new[] { 8, 9 }, composed.FacesOf(1).Select(x => x.Value).OrderBy(x => x).ToArray());
            Assert.Equal(new[] { 8, 9 }, composed.FacesOf(2).Select(x => x.Value).OrderBy(x => x).ToArray());
            Assert.Equal(new[] { 1, 2 }, composed.SourcesOf(new FaceKey(8)).OrderBy(x => x).ToArray());
        }

        [Fact]
        public void Compose_IntermediateNotInNext_DropsThatChain()
        {
            // Stage 1: source 0 -> intermediate faces 1 and 2; stage 2 only maps face 1 onward.
            SourceMap first = new SourceMap();
            first.RecordSplit(0, new[] { new FaceKey(1), new FaceKey(2) }, Provenance.Snapped);

            SourceMap second = new SourceMap();
            second.Record(1, new FaceKey(10), Provenance.Resolved);

            SourceMap composed = first.Compose(second);

            // Face 2 had no continuation, so only the face-1 chain survives.
            Assert.Equal(new[] { new FaceKey(10) }, composed.FacesOf(0));
        }

        [Fact]
        public void Compose_Null_ReturnsSelf()
        {
            SourceMap map = new SourceMap();
            map.Record(0, new FaceKey(0), Provenance.Input);

            SourceMap composed = map.Compose(null);

            Assert.Same(map, composed);
        }
    }
}
