// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="CellComplexHandoff"/> - the P3 roster gate
    /// (docs/CELLCOMPLEX_FIRST_HANDOVER.md). Pure managed: no native OCCT DLL, no Grasshopper document
    /// required (the gate is a plain Guid/string comparison shared by the GH components).
    /// </summary>
    public class CellComplexHandoffTests
    {
        private static Panel MakeWallPanel()
        {
            List<Point3D> points = new List<Point3D>
            {
                new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 1, 0), new Point3D(0, 1, 0)
            };
            Face3D face3D = Face3D.Create(new List<IClosedPlanar3D> { new Polygon3D(points) });
            return global::SAM.Analytical.Create.Panel(new Construction("Test Wall"), PanelType.Wall, face3D);
        }

        private static ResolvedCellComplex ComplexWithRoster(Guid solveId, params Guid[] panelGuids)
        {
            return new ResolvedCellComplex(solveId, null, null, null, null, 0, panelGuids);
        }

        [Fact]
        public void StampSolveId_Panels_SetsMatchingProvenanceOnEach()
        {
            Panel a = MakeWallPanel();
            Panel b = MakeWallPanel();
            Guid solveId = Guid.NewGuid();

            CellComplexHandoff.StampSolveId(new List<Panel> { a, b }, solveId);

            Assert.True(a.TryGetValue(PanelProvenanceParameter.SolveId, out string stampA));
            Assert.True(b.TryGetValue(PanelProvenanceParameter.SolveId, out string stampB));
            Assert.Equal(solveId.ToString(), stampA);
            Assert.Equal(solveId.ToString(), stampB);
        }

        [Fact]
        public void TryDirectConsume_MatchingStampAndRoster_ReturnsTrue()
        {
            Panel a = MakeWallPanel();
            Panel b = MakeWallPanel();
            Guid solveId = Guid.NewGuid();
            CellComplexHandoff.StampSolveId(new List<Panel> { a, b }, solveId);
            ResolvedCellComplex complex = ComplexWithRoster(solveId, a.Guid, b.Guid);

            bool result = CellComplexHandoff.TryDirectConsume(new List<Panel> { a, b }, complex, out string reason);

            Assert.True(result);
            Assert.Null(reason);
        }

        [Fact]
        public void TryDirectConsume_RosterOrderReversed_StillMatches()
        {
            // Order-independent: the gate compares a SET, not a sequence.
            Panel a = MakeWallPanel();
            Panel b = MakeWallPanel();
            Guid solveId = Guid.NewGuid();
            CellComplexHandoff.StampSolveId(new List<Panel> { a, b }, solveId);
            ResolvedCellComplex complex = ComplexWithRoster(solveId, b.Guid, a.Guid);

            bool result = CellComplexHandoff.TryDirectConsume(new List<Panel> { a, b }, complex, out string reason);

            Assert.True(result);
        }

        [Fact]
        public void TryDirectConsume_NullComplex_ReturnsFalseWithReason()
        {
            Panel a = MakeWallPanel();

            bool result = CellComplexHandoff.TryDirectConsume(new List<Panel> { a }, null, out string reason);

            Assert.False(result);
            Assert.Contains("no CellComplex supplied", reason);
        }

        [Fact]
        public void TryDirectConsume_ComplexWithNoRecordedRoster_ReturnsFalseWithReason()
        {
            // A complex captured but never given a roster (WithPanelGuids never called) must never be
            // consumed - an empty roster is "unknown", not "matches an empty panel set".
            Panel a = MakeWallPanel();
            CellComplexHandoff.StampSolveId(new List<Panel> { a }, Guid.NewGuid());
            ResolvedCellComplex complex = new ResolvedCellComplex(Guid.NewGuid(), null, null, null, null, 0);

            bool result = CellComplexHandoff.TryDirectConsume(new List<Panel> { a }, complex, out string reason);

            Assert.False(result);
            Assert.Contains("no recorded panel roster", reason);
        }

        [Fact]
        public void TryDirectConsume_MissingStamp_ReturnsFalseWithReason()
        {
            // A panel never stamped (e.g. hand-built, or from a different pipeline) breaks the gate even if
            // its Guid happens to be in the roster.
            Panel a = MakeWallPanel();
            Guid solveId = Guid.NewGuid();
            ResolvedCellComplex complex = ComplexWithRoster(solveId, a.Guid);

            bool result = CellComplexHandoff.TryDirectConsume(new List<Panel> { a }, complex, out string reason);

            Assert.False(result);
            Assert.Contains("missing a matching SolveId stamp", reason);
        }

        [Fact]
        public void TryDirectConsume_StampFromDifferentSolve_ReturnsFalseWithReason()
        {
            // Two solves on one canvas: a panel stamped by an EARLIER solve must not match a LATER complex's
            // SolveId, even if the Guid happens to be in both rosters (SolveId collision guard).
            Panel a = MakeWallPanel();
            CellComplexHandoff.StampSolveId(new List<Panel> { a }, Guid.NewGuid());
            ResolvedCellComplex complex = ComplexWithRoster(Guid.NewGuid(), a.Guid);

            bool result = CellComplexHandoff.TryDirectConsume(new List<Panel> { a }, complex, out string reason);

            Assert.False(result);
            Assert.Contains("missing a matching SolveId stamp", reason);
        }

        [Fact]
        public void TryDirectConsume_PanelDeleted_ReturnsFalseWithRosterCountReason()
        {
            // The rewired-panels case (docs/CELLCOMPLEX_FIRST_HANDOVER.md P3 acceptance test): one panel from
            // the original roster is missing from the incoming set.
            Panel a = MakeWallPanel();
            Panel b = MakeWallPanel();
            Guid solveId = Guid.NewGuid();
            CellComplexHandoff.StampSolveId(new List<Panel> { a, b }, solveId);
            ResolvedCellComplex complex = ComplexWithRoster(solveId, a.Guid, b.Guid);

            bool result = CellComplexHandoff.TryDirectConsume(new List<Panel> { a }, complex, out string reason); // b deleted

            Assert.False(result);
            Assert.Contains("roster count mismatch", reason);
        }

        [Fact]
        public void TryDirectConsume_PanelSwappedSameCount_ReturnsFalseWithSetMismatchReason()
        {
            // Same COUNT, different SET (one panel swapped for an unrelated one) - the count check alone
            // would miss this; the Guid-set check must catch it.
            Panel a = MakeWallPanel();
            Panel b = MakeWallPanel();
            Panel c = MakeWallPanel();
            Guid solveId = Guid.NewGuid();
            CellComplexHandoff.StampSolveId(new List<Panel> { a, b, c }, solveId);
            ResolvedCellComplex complex = ComplexWithRoster(solveId, a.Guid, b.Guid); // roster is {a, b}

            // Incoming set swaps b for c, keeping the same count.
            bool result = CellComplexHandoff.TryDirectConsume(new List<Panel> { a, c }, complex, out string reason);

            Assert.False(result);
            Assert.Contains("roster Guid set mismatch", reason); // {a,c} vs roster {a,b}: same count (2), different set
        }

        [Fact]
        public void TryDirectConsume_NoPanelsSupplied_ReturnsFalseWithReason()
        {
            ResolvedCellComplex complex = ComplexWithRoster(Guid.NewGuid(), Guid.NewGuid());

            bool result = CellComplexHandoff.TryDirectConsume(new List<Panel>(), complex, out string reason);

            Assert.False(result);
            Assert.Contains("no panels supplied", reason);
        }
    }
}
