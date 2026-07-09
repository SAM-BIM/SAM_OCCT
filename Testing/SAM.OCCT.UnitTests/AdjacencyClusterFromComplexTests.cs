// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Pure-managed unit tests for <see cref="Create.AdjacencyCluster(IEnumerable{Panel}, ResolvedCellComplex, out List{string}, double, double, double, double, IEnumerable{int})"/>
    /// (the P2 direct-consume overload, docs/CELLCOMPLEX_FIRST_HANDOVER.md) - specifically the panel-identity
    /// inheritance branch, which matches a resolved cell face to a supplied source panel by geometry
    /// (coplanar + interior-point containment) and re-stamps the new panel with the source's Guid.
    /// </summary>
    public class AdjacencyClusterFromComplexTests
    {
        private static ResolvedCellComplex TwoCellFacesOneCellOwnerEach(Face3D faceA, Face3D faceB)
        {
            List<ResolvedCell> cells = new List<ResolvedCell> { new ResolvedCell(0, 10, new Point3D(2.5, 0, 1.5)), new ResolvedCell(1, 10, new Point3D(7.5, 0, 1.5)) };
            List<ResolvedCellFace> faces = new List<ResolvedCellFace>
            {
                new ResolvedCellFace(faceA, 1, new List<int> { 0 }, new List<int> { 0 }),
                new ResolvedCellFace(faceB, 2, new List<int> { 1 }, new List<int> { 1 }),
            };
            return new ResolvedCellComplex(Guid.NewGuid(), cells, faces, null, null, 0);
        }

        [Fact]
        public void AdjacencyCluster_TwoCellFacesMatchOneSourcePanel_SecondFaceDoesNotClaimDuplicateGuid()
        {
            // One source wall panel spans BOTH resolved cell faces' footprints (a wall imprinted past an
            // internal separator): faceA covers x[0,5], faceB covers x[5,10], the source panel spans x[0,10].
            // MatchPanelByGeometry finds exactly one (unambiguous) candidate for EACH face - the same source
            // panel - so before the fix both faces would inherit the SAME Guid.
            Face3D faceA = TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(5, 0, 0), new Point3D(5, 0, 3), new Point3D(0, 0, 3));
            Face3D faceB = TestGeometry.CreatePlanarFace(new Point3D(5, 0, 0), new Point3D(10, 0, 0), new Point3D(10, 0, 3), new Point3D(5, 0, 3));
            Face3D sourceFace = TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(10, 0, 0), new Point3D(10, 0, 3), new Point3D(0, 0, 3));
            Panel sourcePanel = global::SAM.Analytical.Create.Panel(new Construction("Source Wall"), PanelType.Wall, sourceFace);

            ResolvedCellComplex complex = TwoCellFacesOneCellOwnerEach(faceA, faceB);

            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.AdjacencyCluster(new List<Panel> { sourcePanel }, complex, out List<string> diagnostics);

            Assert.NotNull(adjacencyCluster);
            List<Panel> panels = adjacencyCluster.GetPanels();
            // Both cell faces became distinct panels (no Guid collision silently dropped one).
            Assert.Equal(2, panels.Count);
            Assert.Equal(2, panels.Select(x => x.Guid).Distinct().Count());
            // Exactly one of the two inherited the source's identity; the other fell back to a defaulted
            // Guid/construction rather than colliding.
            Assert.Single(panels, x => x.Guid == sourcePanel.Guid);
            Assert.Contains(diagnostics, x => x.Contains("SAM_OCCT_ANALYTICAL_PANEL_IDENTITY") && x.Contains("1 of them because the matched source panel's Guid was already claimed"));
        }

        [Fact]
        public void AdjacencyCluster_SingleUnambiguousMatch_InheritsSourceGuid()
        {
            // Sanity check: the ordinary 1:1 case still inherits identity (the fix must not defeat the
            // intended-and-working single-match path).
            Face3D face = TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(5, 0, 0), new Point3D(5, 0, 3), new Point3D(0, 0, 3));
            Panel sourcePanel = global::SAM.Analytical.Create.Panel(new Construction("Source Wall"), PanelType.Wall, face);

            List<ResolvedCell> cells = new List<ResolvedCell> { new ResolvedCell(0, 10, new Point3D(2.5, 0, 1.5)) };
            List<ResolvedCellFace> faces = new List<ResolvedCellFace> { new ResolvedCellFace(face, 1, new List<int> { 0 }, new List<int> { 0 }) };
            ResolvedCellComplex complex = new ResolvedCellComplex(Guid.NewGuid(), cells, faces, null, null, 0);

            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.AdjacencyCluster(new List<Panel> { sourcePanel }, complex, out List<string> diagnostics);

            Assert.NotNull(adjacencyCluster);
            Panel panel = Assert.Single(adjacencyCluster.GetPanels());
            Assert.Equal(sourcePanel.Guid, panel.Guid);
            Assert.Contains(diagnostics, x => x.Contains("1 panel(s) inherited identity"));
        }
    }
}
