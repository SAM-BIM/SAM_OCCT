// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Guid/parameter/aperture reconstruction policy of <see cref="PanelReconstruction.Build"/>
    /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md Phase 4). Pure managed: a hand-built
    /// <see cref="SourceMap"/> stands in for the native/geometric attribution, so no OCCT DLL is required.
    /// </summary>
    public class PanelReconstructionTests
    {
        private static readonly Construction WallConstruction = new Construction("Test Wall");
        private static readonly ApertureConstruction WindowConstruction = new ApertureConstruction("Test Window", ApertureType.Window);

        /// <summary>Axis-aligned rectangle in the z = 0 plane, y in [0, 1], x in [x0, x1].</summary>
        private static Face3D Rect(double x0, double x1)
        {
            List<Point3D> points = new List<Point3D>
            {
                new Point3D(x0, 0, 0), new Point3D(x1, 0, 0), new Point3D(x1, 1, 0), new Point3D(x0, 1, 0)
            };
            return Face3D.Create(new List<IClosedPlanar3D> { new Polygon3D(points) });
        }

        private static Panel MakePanel(Face3D face3D, IEnumerable<Aperture> apertures = null)
        {
            Panel panel = global::SAM.Analytical.Create.Panel(WallConstruction, PanelType.Wall, face3D);
            if (apertures != null && apertures.Any())
            {
                panel = global::SAM.Analytical.Create.Panel(panel.Guid, panel, face3D, apertures, true, Tolerance.MacroDistance, Tolerance.MacroDistance);
            }

            return panel;
        }

        private static Aperture MakeWindow(double x0, double x1)
        {
            List<Point3D> points = new List<Point3D>
            {
                new Point3D(x0, 0.3, 0), new Point3D(x1, 0.3, 0), new Point3D(x1, 0.7, 0), new Point3D(x0, 0.7, 0)
            };
            return new Aperture(WindowConstruction, new Polygon3D(points));
        }

        [Fact]
        public void Build_OneToOneMapping_PreservesSourceGuidConstructionAndParameters()
        {
            Face3D face3D = Rect(0, 4);
            Panel source = MakePanel(face3D);
            SourceMap sourceMap = new SourceMap();
            sourceMap.Record(0, new FaceKey(0), Provenance.Resolved);

            List<Panel> result = PanelReconstruction.Build(
                new List<Face3D> { face3D }, new List<Panel> { source }, sourceMap,
                new List<double> { 0.3 }, new List<double> { 1.5 }, new List<double> { 0.4 },
                Tolerance.Distance, out List<OrphanedAperture> orphaned);

            Panel panel = Assert.Single(result);
            Assert.Equal(source.Guid, panel.Guid);
            Assert.Equal(WallConstruction.Guid, panel.Construction.Guid);
            Assert.True(panel.TryGetValue(global::SAM.Analytical.Solver.SolverParameter.Weight, out double weight));
            Assert.Equal(1.5, weight);
            Assert.False(panel.TryGetValue(PanelProvenanceParameter.SourceGuid, out string _));
            Assert.Empty(orphaned);
        }

        [Fact]
        public void Build_SplitOneSourceTwoOutputs_AssignsNewGuidsAndStampsSourceGuid()
        {
            Face3D wholeFace3D = Rect(0, 4); // the source's own geometry, unused as an output face here
            Panel source = MakePanel(wholeFace3D);

            Face3D left = Rect(0, 2);
            Face3D right = Rect(2, 4);

            SourceMap sourceMap = new SourceMap();
            sourceMap.RecordSplit(0, new[] { new FaceKey(0), new FaceKey(1) }, Provenance.Resolved);

            List<Panel> result = PanelReconstruction.Build(
                new List<Face3D> { left, right }, new List<Panel> { source }, sourceMap,
                new List<double> { 0.3 }, new List<double> { 1.0 }, new List<double> { 0.4 },
                Tolerance.Distance, out List<OrphanedAperture> orphaned);

            Assert.Equal(2, result.Count);
            Assert.All(result, panel => Assert.NotEqual(source.Guid, panel.Guid));
            Assert.NotEqual(result[0].Guid, result[1].Guid);

            foreach (Panel panel in result)
            {
                Assert.True(panel.TryGetValue(PanelProvenanceParameter.SourceGuid, out string sourceGuid));
                Assert.Equal(source.Guid.ToString(), sourceGuid);
            }

            Assert.Empty(orphaned);
        }

        [Fact]
        public void Build_SplitWithApertureOnOnePiece_ApertureLandsOnlyOnContainingPiece()
        {
            Aperture window = MakeWindow(0.4, 0.8); // sits well inside the LEFT half [0, 2], far from the split at x = 2
            Face3D wholeFace3D = Rect(0, 4);
            Panel source = MakePanel(wholeFace3D, new[] { window });

            Face3D left = Rect(0, 2);
            Face3D right = Rect(2, 4);

            SourceMap sourceMap = new SourceMap();
            sourceMap.RecordSplit(0, new[] { new FaceKey(0), new FaceKey(1) }, Provenance.Resolved);

            List<Panel> result = PanelReconstruction.Build(
                new List<Face3D> { left, right }, new List<Panel> { source }, sourceMap,
                null, null, null, Tolerance.Distance, out List<OrphanedAperture> orphaned);

            Panel leftPanel = result[0];
            Panel rightPanel = result[1];

            Assert.Single(leftPanel.Apertures ?? new List<Aperture>());
            Assert.Empty(rightPanel.Apertures ?? new List<Aperture>());
            Assert.Empty(orphaned);
        }

        [Fact]
        public void Build_MergeTwoSourcesOneOutput_DominantKeepsGuidOthersStamped()
        {
            Face3D bigFace3D = Rect(0, 4);   // area 4
            Face3D smallFace3D = Rect(0, 1); // area 1 - the non-dominant source

            Panel big = MakePanel(bigFace3D);
            Panel small = MakePanel(smallFace3D);

            Face3D merged = Rect(0, 4);
            SourceMap sourceMap = new SourceMap();
            sourceMap.RecordMerge(new[] { 0, 1 }, new FaceKey(0), Provenance.Resolved);

            List<Panel> result = PanelReconstruction.Build(
                new List<Face3D> { merged }, new List<Panel> { big, small }, sourceMap,
                null, null, null, Tolerance.Distance, out List<OrphanedAperture> orphaned);

            Panel panel = Assert.Single(result);
            Assert.Equal(big.Guid, panel.Guid);
            Assert.True(panel.TryGetValue(PanelProvenanceParameter.MergedSourceGuids, out string mergedGuids));
            Assert.Equal(small.Guid.ToString(), mergedGuids);
            Assert.Empty(orphaned);
        }

        [Fact]
        public void Build_DominantSourceSplitAcrossTwoSeparateMerges_AssignsDistinctFreshGuids()
        {
            // A long wall (the dominant source on both pieces) split into two pieces, each piece separately
            // merging with a different smaller neighbour - the split+merge combination isMerge alone cannot
            // distinguish. Without treating "the dominant source itself maps to >1 output face" as its own
            // split signal, both pieces would incorrectly keep the SAME dominantSource.Guid (a collision).
            Face3D wholeFace3D = Rect(0, 8); // the source's own geometry, unused as an output face here
            Panel dominant = MakePanel(wholeFace3D);
            Panel smallA = MakePanel(Rect(0, 1));
            Panel smallB = MakePanel(Rect(4, 5));

            Face3D pieceA = Rect(0, 4);
            Face3D pieceB = Rect(4, 8);

            SourceMap sourceMap = new SourceMap();
            sourceMap.RecordMerge(new[] { 0, 1 }, new FaceKey(0), Provenance.Resolved);
            sourceMap.RecordMerge(new[] { 0, 2 }, new FaceKey(1), Provenance.Resolved);

            List<Panel> result = PanelReconstruction.Build(
                new List<Face3D> { pieceA, pieceB }, new List<Panel> { dominant, smallA, smallB }, sourceMap,
                null, null, null, Tolerance.Distance, out List<OrphanedAperture> orphaned);

            Assert.Equal(2, result.Count);
            Assert.NotEqual(result[0].Guid, result[1].Guid);
            Assert.All(result, panel => Assert.NotEqual(dominant.Guid, panel.Guid));

            foreach (Panel panel in result)
            {
                Assert.True(panel.TryGetValue(PanelProvenanceParameter.SourceGuid, out string sourceGuid));
                Assert.Equal(dominant.Guid.ToString(), sourceGuid);
            }

            Assert.True(result[0].TryGetValue(PanelProvenanceParameter.MergedSourceGuids, out string mergedA));
            Assert.Equal(smallA.Guid.ToString(), mergedA);
            Assert.True(result[1].TryGetValue(PanelProvenanceParameter.MergedSourceGuids, out string mergedB));
            Assert.Equal(smallB.Guid.ToString(), mergedB);
            Assert.Empty(orphaned);
        }

        [Fact]
        public void Build_ApertureInGapBetweenSplitPieces_IsOrphaned()
        {
            // The window sits at x in [1.8, 2.2] - the sliver the two produced pieces do not cover between
            // them ([0, 1.5] and [2.5, 4]), simulating a resolve that leaves a hairline structural gap.
            Aperture window = MakeWindow(1.8, 2.2);
            Face3D wholeFace3D = Rect(0, 4);
            Panel source = MakePanel(wholeFace3D, new[] { window });

            Face3D left = Rect(0, 1.5);
            Face3D right = Rect(2.5, 4);

            SourceMap sourceMap = new SourceMap();
            sourceMap.RecordSplit(0, new[] { new FaceKey(0), new FaceKey(1) }, Provenance.Resolved);

            List<Panel> result = PanelReconstruction.Build(
                new List<Face3D> { left, right }, new List<Panel> { source }, sourceMap,
                null, null, null, Tolerance.Distance, out List<OrphanedAperture> orphaned,
                Tolerance.MacroDistance, Tolerance.MacroDistance);

            Assert.All(result, panel => Assert.Empty(panel.Apertures ?? new List<Aperture>()));

            OrphanedAperture orphan = Assert.Single(orphaned);
            Assert.Equal(window.Guid, orphan.Aperture.Guid);
            Assert.Equal(source.Guid, orphan.SourceGuid);
        }

        [Fact]
        public void Build_FaceWithNoSourceMapEntry_FallsBackToNearestSourceIndex()
        {
            Face3D face3D = Rect(0, 4);
            Panel source = MakePanel(face3D);

            // Empty map: no attribution recorded for FaceKey(0) at all.
            SourceMap sourceMap = new SourceMap();

            List<Panel> result = PanelReconstruction.Build(
                new List<Face3D> { face3D }, new List<Panel> { source }, sourceMap,
                null, null, null, Tolerance.Distance, out List<OrphanedAperture> orphaned);

            Panel panel = Assert.Single(result);
            Assert.Equal(source.Construction.Guid, panel.Construction.Guid); // fallback still resolved a source
        }
    }
}
