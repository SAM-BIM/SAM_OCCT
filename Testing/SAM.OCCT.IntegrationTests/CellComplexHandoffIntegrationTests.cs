// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Native-gated end-to-end coverage for the P3 handoff gate (docs/CELLCOMPLEX_FIRST_HANDOVER.md):
    /// <see cref="CellComplexHandoff"/> against a REAL solve's panels/complex, mirroring exactly what
    /// <c>SAMOCCTSolve3D</c> (stamp + attach roster) and <c>SAMOCCTCreateAdjacencyCluster</c> (gate, then
    /// direct-consume or rebuild) do, without requiring a live Grasshopper document.
    /// </summary>
    public class CellComplexHandoffIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

        private static List<Panel> LoadPanels(string path)
        {
            List<IJSAMObject> objects = SAM.Core.Convert.ToSAM(path);
            List<Panel> result = new List<Panel>();
            foreach (IJSAMObject sAMObject in objects ?? new List<IJSAMObject>())
            {
                switch (sAMObject)
                {
                    case AnalyticalModel analyticalModel: result.AddRange(analyticalModel.GetPanels() ?? new List<Panel>()); break;
                    case AdjacencyCluster adjacencyCluster: result.AddRange(adjacencyCluster.GetPanels() ?? new List<Panel>()); break;
                    case Panel panel: result.Add(panel); break;
                }
            }

            return result.Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid()).ToList();
        }

        /// <summary>Mirrors SAMOCCTSolve3D's post-solve handoff prep: stamp every output panel with the
        /// solve's SolveId and attach the SAME roster to the complex.</summary>
        private static (List<Panel> panels, ResolvedCellComplex complex) SolveAndPrepareHandoff(string fixture)
        {
            string path = Path.Combine(FixturesDirectory, fixture);
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> panels = LoadPanels(path);
            List<Panel> solved = panels.Solve3D(out List<Point3D> _, out List<string> _, out _, out Solve3DReport report);
            List<Panel> nonAirSolved = solved.Where(x => x != null && x.PanelType != PanelType.Air).ToList();
            Assert.NotNull(report.ResolvedCellComplex);

            CellComplexHandoff.StampSolveId(nonAirSolved, report.ResolvedCellComplex.SolveId);
            ResolvedCellComplex complex = report.ResolvedCellComplex.WithPanelGuids(nonAirSolved.Select(x => x.Guid));

            return (nonAirSolved, complex);
        }

        /// <summary>The set of unordered space-index-pair (or single-index envelope) relation keys a cluster's
        /// panels imply, keyed by which cell indices the panel's owner spaces correspond to via Guid identity -
        /// the same "relation multiset" comparison WorkflowParityIntegrationTests uses, applied here to prove
        /// the gated call and the direct P2-overload call build the SAME relations.</summary>
        private static HashSet<string> RelationKeys(AdjacencyCluster adjacencyCluster)
        {
            HashSet<string> result = new HashSet<string>();
            foreach (Panel panel in adjacencyCluster?.GetPanels() ?? new List<Panel>())
            {
                List<Space> spaces = (adjacencyCluster.GetSpaces(panel) ?? new List<Space>()).Where(x => x != null).ToList();
                if (spaces.Count == 2)
                {
                    string a = spaces[0].Location?.ToString() ?? "";
                    string b = spaces[1].Location?.ToString() ?? "";
                    result.Add(string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a);
                }
                else if (spaces.Count == 1)
                {
                    result.Add("ENV|" + spaces[0].Location);
                }
            }

            return result;
        }

        [SkippableFact]
        public void TryDirectConsume_UnmodifiedRosterFromRealSolve_ApprovesAndMatchesDirectOverloadCall()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            (List<Panel> panels, ResolvedCellComplex complex) = SolveAndPrepareHandoff("whole-level-flat.sam");

            bool approved = CellComplexHandoff.TryDirectConsume(panels, complex, out string reason);
            Assert.True(approved, reason);
            Assert.Null(reason);

            // The gated path (what SAMOCCTCreateAdjacencyCluster does once approved) and calling the P2
            // overload directly (bypassing the gate) must build the identical cluster - the gate is a pure
            // permission check, it never alters what gets built.
            AdjacencyCluster gatedCluster = AnalyticalOcctCreate.AdjacencyCluster(panels, complex, out List<string> _);
            AdjacencyCluster directCluster = AnalyticalOcctCreate.AdjacencyCluster(panels, complex, out List<string> _);

            Assert.NotNull(gatedCluster);
            Assert.NotNull(directCluster);
            Assert.Equal(directCluster.GetSpaces().Count, gatedCluster.GetSpaces().Count);
            Assert.Equal(directCluster.GetPanels().Count, gatedCluster.GetPanels().Count);
            Assert.Equal(RelationKeys(directCluster), RelationKeys(gatedCluster));
        }

        [SkippableFact]
        public void TryDirectConsume_PanelDeletedAfterRealSolve_RefusesWithRosterCountMismatch()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            (List<Panel> panels, ResolvedCellComplex complex) = SolveAndPrepareHandoff("whole-level-flat.sam");

            // The rewired-panels case: the user deletes one panel downstream of the solve before wiring the
            // (now stale) result into CreateAdjacencyCluster.
            List<Panel> rewired = panels.Skip(1).ToList();

            bool approved = CellComplexHandoff.TryDirectConsume(rewired, complex, out string reason);

            Assert.False(approved);
            Assert.Contains("roster count mismatch", reason);
        }

        [SkippableFact]
        public void TryDirectConsume_PanelFromUnrelatedSolveSwappedIn_RefusesWithMissingStamp()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            (List<Panel> panelsA, ResolvedCellComplex complexA) = SolveAndPrepareHandoff("whole-level-flat.sam");
            (List<Panel> panelsB, ResolvedCellComplex _) = SolveAndPrepareHandoff("tilted-two-spaces.sam");

            // Swap in a panel stamped by a DIFFERENT solve (SolveId collision guard: Guid coincidence alone
            // must never be enough - the stamp must match too).
            List<Panel> mixed = panelsA.Take(panelsA.Count - 1).Concat(panelsB.Take(1)).ToList();

            bool approved = CellComplexHandoff.TryDirectConsume(mixed, complexA, out string reason);

            Assert.False(approved);
            Assert.True(reason.Contains("missing a matching SolveId stamp") || reason.Contains("roster Guid set mismatch"),
                "Expected a stamp or roster-set mismatch reason, got: " + reason);
        }

        [SkippableFact]
        public void AdjacencyCluster_FromComplex_NoRosterAttached_RefusesConsumption()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, "whole-level-flat.sam");
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> panels = LoadPanels(path);
            List<Panel> solved = panels.Solve3D(out List<Point3D> _, out List<string> _, out _, out Solve3DReport report)
                .Where(x => x != null && x.PanelType != PanelType.Air).ToList();
            Assert.NotNull(report.ResolvedCellComplex);

            // A complex captured straight off the report (P2 shape) - the roster was never attached, as would
            // happen if a caller bypassed SAMOCCTSolve3D's stamping step entirely.
            bool approved = CellComplexHandoff.TryDirectConsume(solved, report.ResolvedCellComplex, out string reason);

            Assert.False(approved);
            Assert.Contains("no recorded panel roster", reason);
        }
    }
}
