// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

// SAM.Analytical.OCCT and SAM.Analytical.OCCT.Solver both declare a static Create class - alias the
// one this file calls directly (Create.AdjacencyCluster). The bare `using SAM.Analytical.OCCT;`
// above is what brings the MergeCoplanarPanels extension methods (declared in that namespace's
// Modify class) into scope; Clean3D/Extend3D/Solve3D resolve the same way from .Solver.
using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// P1 (docs/CELLCOMPLEX_FIRST_HANDOVER.md §3): end-to-end parity harness across all 9 fixtures.
    /// For each fixture, three workflows build a SAM adjacency cluster from the same source panels
    /// and are each run through <c>MergeCoplanarPanels(AdjacencyCluster)</c>:
    /// <list type="bullet">
    /// <item>Workflow A (old defaults): <c>Solve3D</c> -&gt; <c>Create.AdjacencyCluster</c> with the
    /// pre-P1 production GH default options (<c>AvoidInternalShapes=true, SewBeforeBuild=false</c>) -
    /// kept here ONLY for comparison against the P1 bugfix, never as a recommended path.</item>
    /// <item>Workflow A (solver-matched): the same solved panels, rebuilt with the solver-matched
    /// options the P1 bugfix now uses in production (<c>AvoidInternalShapes=false,
    /// SewBeforeBuild=true, SewingTolerance=0.01</c>).</item>
    /// <item>Workflow B: <c>Clean3D</c> -&gt; <c>Extend3D</c> -&gt; <c>Create.AdjacencyCluster</c>
    /// (solver-matched options), the pre-conditioned pipeline that never runs Solve3D's own native
    /// resolve.</item>
    /// </list>
    /// This is observational: no solver-geometry or gate changes. Where a workflow is broken today
    /// (see the per-fixture table this test emits, mirrored into TESTING.md), the assertion pins the
    /// CURRENT value with a tracking comment rather than skipping - the harness is the measuring
    /// stick the E-track (docs/EXTEND3D_ROBUST_HANDOVER.md) and P4 gate-hardening
    /// (docs/CELLCOMPLEX_FIRST_HANDOVER.md §9-10) phases re-run against.
    /// </summary>
    public class WorkflowParityIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

        private readonly ITestOutputHelper output;

        public WorkflowParityIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static List<Panel> LoadPanels(string path)
        {
            List<IJSAMObject> objects = SAM.Core.Convert.ToSAM(path);
            List<Panel> result = new List<Panel>();
            foreach (IJSAMObject sAMObject in objects ?? new List<IJSAMObject>())
            {
                switch (sAMObject)
                {
                    case AnalyticalModel analyticalModel:
                        result.AddRange(analyticalModel.GetPanels() ?? new List<Panel>());
                        break;
                    case AdjacencyCluster adjacencyCluster:
                        result.AddRange(adjacencyCluster.GetPanels() ?? new List<Panel>());
                        break;
                    case Panel panel:
                        result.Add(panel);
                        break;
                }
            }

            return result
                .Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid())
                .ToList();
        }

        /// <summary>All 9 fixtures under Testing/SAM.OCCT.IntegrationTests/Fixtures (Appendix B of the
        /// CellComplex handover doc). Four have no prior golden-master/managed baseline at all
        /// (AdjacencyCluster-home, Face3D-home, Revit-home-panels, three-spaces) - this harness is
        /// the first workflow-level coverage they get.</summary>
        public static IEnumerable<object[]> Fixtures()
        {
            yield return new object[] { "whole-level-flat.sam" };
            yield return new object[] { "tilted-two-spaces.sam" };
            yield return new object[] { "whole-level-tilted.sam" };
            yield return new object[] { "two-level-tilted.sam" };
            yield return new object[] { "whole-level-towers.sam" };
            yield return new object[] { "AdjacencyCluster-home.sam" };
            yield return new object[] { "Face3D-home.sam" };
            yield return new object[] { "Revit-home-panels.sam" };
            yield return new object[] { "three-spaces.sam" };
        }

        private static OcctBuildOptions SolverMatchedOptions()
        {
            return new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
        }

        /// <summary>True when the SAM_OCCT_ANALYTICAL_PARITY diagnostic (P1, AdjacencyCluster.cs
        /// DirectAdjacencyCluster) reports Info severity - i.e. relations added == 2*FaceAdjacencies +
        /// envelope faces, no TopologyKey==0 faces, no zero-relation panels. Null (no diagnostic at
        /// all, e.g. the direct path never ran because it fell back to the geometric rebuild) is
        /// reported as "not applicable", distinct from a broken parity check.</summary>
        private static bool? ParityClean(OcctCellComplexResult cellComplexResult)
        {
            OcctDiagnostic diagnostic = cellComplexResult?.Diagnostics?.LastOrDefault(d => d.Code == "SAM_OCCT_ANALYTICAL_PARITY");
            if (diagnostic == null)
            {
                return null;
            }

            return diagnostic.Severity == OcctDiagnosticSeverity.Info;
        }

        /// <summary>The set of relations a cluster's panels imply, keyed by the space(s) each panel
        /// bounds (an unordered space-Guid pair for a 2-space/internal panel, a single space Guid for
        /// a 1-space/envelope panel). Space Guids survive <c>MergeCoplanarPanels(AdjacencyCluster)</c>
        /// (it copies each space via <c>new Space(space, space.Name, space.Location)</c>, which
        /// preserves <c>Guid</c> - <c>SAM.Core.Classes.Base.SAMObject(string, SAMObject)</c>). Comparing
        /// this set before/after merge is the "relation multiset invariant" P1 asserts: merging must
        /// consolidate panel geometry, never add or drop which spaces are adjacent to which.</summary>
        private static HashSet<string> RelationKeys(AdjacencyCluster adjacencyCluster, out int anomalyCount)
        {
            HashSet<string> result = new HashSet<string>();
            anomalyCount = 0;
            if (adjacencyCluster == null)
            {
                return result;
            }

            foreach (Panel panel in adjacencyCluster.GetPanels() ?? new List<Panel>())
            {
                List<Space> relatedSpaces = (adjacencyCluster.GetSpaces(panel) ?? new List<Space>()).Where(x => x != null).ToList();
                if (relatedSpaces.Count == 2)
                {
                    string a = relatedSpaces[0].Guid.ToString();
                    string b = relatedSpaces[1].Guid.ToString();
                    result.Add(string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a);
                }
                else if (relatedSpaces.Count == 1)
                {
                    result.Add("ENV|" + relatedSpaces[0].Guid);
                }
                else
                {
                    // 0 or 3+ related spaces: not a well-formed internal/envelope relation. Tracked
                    // separately (not part of the invariant set) so it surfaces in the table rather
                    // than silently inflating or masking the pair/envelope comparison.
                    anomalyCount++;
                }
            }

            return result;
        }

        /// <summary>One workflow's built-and-merged cluster, plus everything the per-fixture table
        /// and assertions read. Null <see cref="Cluster"/> means the direct OCCT rebuild AND the
        /// geometric fallback both failed to produce a cluster for this workflow.</summary>
        private class WorkflowResult
        {
            public string Name;
            public AdjacencyCluster Cluster;
            public OcctCellComplexResult CellComplexResult;
            public AdjacencyCluster Merged;
            public List<string> MergeDiagnostics;
        }

        private static WorkflowResult BuildWorkflow(string name, List<Panel> inputPanels, OcctBuildOptions options)
        {
            WorkflowResult result = new WorkflowResult { Name = name };
            if (inputPanels == null || inputPanels.Count == 0)
            {
                return result;
            }

            result.Cluster = AnalyticalOcctCreate.AdjacencyCluster(null, inputPanels, out OcctCellComplexResult cellComplexResult, null, options);
            result.CellComplexResult = cellComplexResult;

            if (result.Cluster != null)
            {
                result.Merged = result.Cluster.MergeCoplanarPanels(out List<string> mergeDiagnostics);
                result.MergeDiagnostics = mergeDiagnostics;
            }

            return result;
        }

        [SkippableTheory]
        [MemberData(nameof(Fixtures))]
        public void WorkflowParity_AllWorkflows_ObservedAcrossAllFixtures(string fixture)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, fixture);
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> inputPanels = LoadPanels(path);
            Assert.NotEmpty(inputPanels);

            // The solver's own resolved cell count - the reference workflows' space counts are
            // compared against this, not against each other.
            List<Panel> solved = inputPanels.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> solveDiagnostics, out _, out Solve3DReport report);
            Assert.NotNull(solved);
            List<Panel> solvedNonAir = solved.Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid()).ToList();

            OcctBuildOptions oldDefaultOptions = new OcctBuildOptions();
            OcctBuildOptions solverMatchedOptions = SolverMatchedOptions();

            WorkflowResult workflowAOld = BuildWorkflow("A-old-defaults", solvedNonAir, oldDefaultOptions);
            WorkflowResult workflowAMatched = BuildWorkflow("A-solver-matched", solvedNonAir, solverMatchedOptions);

            List<string> cleanDiagnostics;
            List<string> extendDiagnostics;
            List<Panel> clean = inputPanels.Clean3D(out cleanDiagnostics);
            List<Panel> extended = (clean ?? new List<Panel>()).Extend3D(out extendDiagnostics);
            List<Panel> extendedNonAir = (extended ?? new List<Panel>()).Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid()).ToList();
            WorkflowResult workflowB = BuildWorkflow("B-clean-extend", extendedNonAir, solverMatchedOptions);

            // --- Per-fixture markdown table row (TESTING.md "P1 workflow parity" section mirrors this) ---
            int droppedFaceCount = (solveDiagnostics ?? new List<string>()).Count(d => d.Contains("DroppedFace"));
            int retainedFaceCount = (solveDiagnostics ?? new List<string>()).Count(d => d.Contains("RetainedFace"));
            string verdict = Verdict(workflowAMatched, workflowB, report.ResolvedCellCount);

            output.WriteLine(string.Format(
                "| {0} | {1} | {2} (naked={3}) | {4} | {5} | {6} | {7}/{8} | {9} |",
                fixture,
                inputPanels.Count,
                report.ResolvedCellCount,
                nakedPoint3Ds?.Count ?? 0,
                FormatWorkflow(workflowAOld),
                FormatWorkflow(workflowAMatched),
                FormatWorkflow(workflowB),
                droppedFaceCount,
                retainedFaceCount,
                verdict));

            foreach (string d in solveDiagnostics ?? new List<string>()) { output.WriteLine("[solve] " + d); }
            foreach (string d in cleanDiagnostics ?? new List<string>()) { output.WriteLine("[clean] " + d); }
            foreach (string d in extendDiagnostics ?? new List<string>()) { output.WriteLine("[extend] " + d); }
            foreach (string d in workflowAOld.CellComplexResult?.Diagnostics?.Select(x => x.ToString()) ?? Enumerable.Empty<string>()) { output.WriteLine("[A-old] " + d); }
            foreach (string d in workflowAMatched.CellComplexResult?.Diagnostics?.Select(x => x.ToString()) ?? Enumerable.Empty<string>()) { output.WriteLine("[A-matched] " + d); }
            foreach (string d in workflowB.CellComplexResult?.Diagnostics?.Select(x => x.ToString()) ?? Enumerable.Empty<string>()) { output.WriteLine("[B] " + d); }

            // --- Assertions ---
            // solver-matched A and B are the two workflows this harness holds to account; A-old is
            // logged for the P1 options-bugfix comparison only, never asserted as "should be correct".
            AssertWorkflowAgainstExpectation(fixture, "A-solver-matched", workflowAMatched, report.ResolvedCellCount);
            AssertWorkflowAgainstExpectation(fixture, "B-clean-extend", workflowB, report.ResolvedCellCount);
        }

        private static string FormatWorkflow(WorkflowResult workflowResult)
        {
            if (workflowResult.Cluster == null)
            {
                return "no cluster";
            }

            int occtCells = workflowResult.CellComplexResult?.Cells?.Count ?? 0;
            int occtFaces = UniqueFaceCount(workflowResult.CellComplexResult);
            int sharedAdjacencies = workflowResult.CellComplexResult?.FaceAdjacencies?.Count ?? 0;
            int spaces = workflowResult.Cluster.GetSpaces()?.Count ?? 0;
            int panels = workflowResult.Cluster.GetPanels()?.Count ?? 0;
            int internalPanels = (workflowResult.Cluster.GetPanels() ?? new List<Panel>()).Count(p => (workflowResult.Cluster.GetSpaces(p) ?? new List<Space>()).Count == 2);
            int externalPanels = (workflowResult.Cluster.GetPanels() ?? new List<Panel>()).Count(p => (workflowResult.Cluster.GetSpaces(p) ?? new List<Space>()).Count == 1);
            int orphanPanels = (workflowResult.Cluster.GetPanels() ?? new List<Panel>()).Count(p => (workflowResult.Cluster.GetSpaces(p) ?? new List<Space>()).Count == 0);
            bool? parityClean = ParityClean(workflowResult.CellComplexResult);
            int mergedPanels = workflowResult.Merged?.GetPanels()?.Count ?? -1;

            return string.Format(
                "OCCT cells={0} faces={1} adj={2}; SAM spaces={3} panels={4} (int={5} ext={6} orphan={7}) parity={8} merged={9}",
                occtCells, occtFaces, sharedAdjacencies,
                spaces, panels, internalPanels, externalPanels, orphanPanels,
                parityClean == null ? "n/a" : (parityClean.Value ? "clean" : "WARN"),
                mergedPanels);
        }

        /// <summary>Count of distinct decoded faces (TopologyKey != 0) across all cells - the same
        /// per-decode dedup DirectAdjacencyCluster uses for its parity math, recomputed here purely
        /// for the diagnostic table (observational, no solver-geometry involvement).</summary>
        private static int UniqueFaceCount(OcctCellComplexResult cellComplexResult)
        {
            if (cellComplexResult?.Cells == null)
            {
                return 0;
            }

            HashSet<int> keys = new HashSet<int>();
            foreach (OcctCell cell in cellComplexResult.Cells)
            {
                foreach (OcctCellFace face in cell?.Faces ?? (IReadOnlyList<OcctCellFace>)Array.Empty<OcctCellFace>())
                {
                    if (face != null && face.TopologyKey != 0)
                    {
                        keys.Add(face.TopologyKey);
                    }
                }
            }

            return keys.Count;
        }

        /// <summary>One-line human verdict for the table: whether the two solver-matched workflows
        /// this harness holds to account (A-solver-matched, B-clean-extend) close to the solver's own
        /// resolved cell count.</summary>
        private static string Verdict(WorkflowResult workflowAMatched, WorkflowResult workflowB, int resolvedCellCount)
        {
            bool aMatches = workflowAMatched.Cluster != null && workflowAMatched.Cluster.GetSpaces()?.Count == resolvedCellCount;
            bool bMatches = workflowB.Cluster != null && workflowB.Cluster.GetSpaces()?.Count == resolvedCellCount;

            if (aMatches && bMatches)
            {
                return "OK";
            }

            if (aMatches)
            {
                return "workflow B under/over-closes";
            }

            if (bMatches)
            {
                return "workflow A under/over-closes";
            }

            return "both workflows diverge from solver cell count";
        }

        /// <summary>
        /// Records, per fixture and workflow, whether the invariants below CURRENTLY hold - "false"
        /// entries are known-broken today and are asserted as such (tracking comment on each), never
        /// silently skipped (docs/CELLCOMPLEX_FIRST_HANDOVER.md P1 task 3). A fixture/workflow not
        /// listed here is expected clean on all three invariants.
        /// </summary>
        private static readonly Dictionary<(string fixture, string workflow), Expectation> Expectations = new Dictionary<(string, string), Expectation>
        {
            // whole-level-towers: A-solver-matched closes 32/32 (the P1 bugfix fix), but workflow B's
            // Clean3D->Extend3D pre-conditioning under-closes this fixture's plan loops (26 spaces).
            // Tracked, not the E-track's problem to fix here - docs/EXTEND3D_ROBUST_HANDOVER.md E2
            // (plane-intersection cap targets) is the fix; P1 only measures and pins the gap.
            [("whole-level-towers.sam", "B-clean-extend")] = new Expectation
            {
                SpacesMatchResolvedCellCount = false,
                TrackingComment = "workflow B under-closes whole-level-towers (26 vs 32 solver cells) - see docs/EXTEND3D_ROBUST_HANDOVER.md E2"
            },
            // Face3D-home: the raw fixture geometry itself (no solver conditioning at all upstream of
            // A) does not sew cleanly under solver-matched options on this hand-built fixture - fewer
            // cells AND a parity warning (some relations lost to the coarser sew). A-old happens to
            // match here (13/13) only because AvoidInternalShapes=true papers over the same gap.
            [("Face3D-home.sam", "A-solver-matched")] = new Expectation
            {
                SpacesMatchResolvedCellCount = false,
                ParityClean = false,
                TrackingComment = "Face3D-home solver-matched rebuild merges cells (11 vs 13) with a parity warning - fixture has no prior baseline (Appendix B); tracked as a new finding, not yet triaged"
            },
            // Revit-home-panels: a genuinely messy Revit export (Appendix B: no prior baseline at
            // all). Both solver-matched workflows under-close relative to the solver's own resolved
            // cell count; A-old also mismatches (10 vs 13, unasserted). Tracked as a new finding.
            [("Revit-home-panels.sam", "A-solver-matched")] = new Expectation
            {
                SpacesMatchResolvedCellCount = false,
                TrackingComment = "Revit-home-panels solver-matched rebuild under-closes (7 vs 13 solver cells) - new fixture, no prior baseline; tracked, not yet triaged"
            },
            [("Revit-home-panels.sam", "B-clean-extend")] = new Expectation
            {
                SpacesMatchResolvedCellCount = false,
                TrackingComment = "Revit-home-panels workflow B under-closes (6 vs 13 solver cells) - new fixture, no prior baseline; tracked, not yet triaged"
            },
            // two-level-tilted: the pinned managed-path golden master (GoldenMasterIntegrationTests)
            // already documents this fixture as broken on the managed pipeline (29c/29n baseline).
            // A-solver-matched is off by one cell (43 vs 44 - likely one merged/dropped separator);
            // workflow B is far off (26 vs 44) with a parity warning - the E-track's target fixture.
            [("two-level-tilted.sam", "A-solver-matched")] = new Expectation
            {
                SpacesMatchResolvedCellCount = false,
                TrackingComment = "two-level-tilted A-solver-matched rebuild is short one cell (43 vs 44 solver cells)"
            },
            [("two-level-tilted.sam", "B-clean-extend")] = new Expectation
            {
                SpacesMatchResolvedCellCount = false,
                ParityClean = false,
                TrackingComment = "two-level-tilted workflow B under-closes (26 vs 44 solver cells) with a parity warning - matches the known-broken managed pipeline on this fixture (GoldenMasterIntegrationTests.ManagedFixtures); the E-track (docs/EXTEND3D_ROBUST_HANDOVER.md E2) targets this"
            },
            // AdjacencyCluster-home: no prior baseline (Appendix B). Both solver-matched workflows
            // merge far more cells than the solver resolved (8 and 16 vs 19) though parity stays
            // internally clean (every relation the rebuild DID form is complete/consistent).
            [("AdjacencyCluster-home.sam", "A-solver-matched")] = new Expectation
            {
                SpacesMatchResolvedCellCount = false,
                TrackingComment = "AdjacencyCluster-home A-solver-matched rebuild merges cells heavily (8 vs 19 solver cells) - new fixture, no prior baseline; tracked, not yet triaged"
            },
            [("AdjacencyCluster-home.sam", "B-clean-extend")] = new Expectation
            {
                SpacesMatchResolvedCellCount = false,
                TrackingComment = "AdjacencyCluster-home workflow B rebuild merges cells (16 vs 19 solver cells) - new fixture, no prior baseline; tracked, not yet triaged"
            },
        };

        private class Expectation
        {
            public bool SpacesMatchResolvedCellCount = true;
            public bool ParityClean = true;
            public bool RelationInvariantAcrossMerge = true;
            public string TrackingComment;
        }

        private static void AssertWorkflowAgainstExpectation(string fixture, string workflowName, WorkflowResult workflowResult, int resolvedCellCount)
        {
            Expectations.TryGetValue((fixture, workflowName), out Expectation expectation);
            expectation = expectation ?? new Expectation();

            bool spacesMatch = workflowResult.Cluster != null && workflowResult.Cluster.GetSpaces()?.Count == resolvedCellCount;
            Assert.True(spacesMatch == expectation.SpacesMatchResolvedCellCount,
                string.Format("{0}/{1}: spaces=={2} match expected {3} (tracking: {4})", fixture, workflowName, resolvedCellCount, expectation.SpacesMatchResolvedCellCount, expectation.TrackingComment ?? "none"));

            bool? parityClean = ParityClean(workflowResult.CellComplexResult);
            if (parityClean != null)
            {
                Assert.True(parityClean.Value == expectation.ParityClean,
                    string.Format("{0}/{1}: parity-clean expected {2}, was {3} (tracking: {4})", fixture, workflowName, expectation.ParityClean, parityClean, expectation.TrackingComment ?? "none"));
            }

            if (workflowResult.Cluster != null && workflowResult.Merged != null)
            {
                HashSet<string> before = RelationKeys(workflowResult.Cluster, out int anomaliesBefore);
                HashSet<string> after = RelationKeys(workflowResult.Merged, out int anomaliesAfter);
                bool invariant = before.SetEquals(after) && anomaliesBefore == 0 && anomaliesAfter == 0;
                Assert.True(invariant == expectation.RelationInvariantAcrossMerge,
                    string.Format("{0}/{1}: relation-invariant-across-merge expected {2}, was {3} (before={4} after={5} anomaliesBefore={6} anomaliesAfter={7}) (tracking: {8})",
                        fixture, workflowName, expectation.RelationInvariantAcrossMerge, invariant, before.Count, after.Count, anomaliesBefore, anomaliesAfter, expectation.TrackingComment ?? "none"));
            }
        }
    }
}
