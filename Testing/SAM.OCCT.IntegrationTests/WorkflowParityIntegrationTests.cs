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
// SAM.Core.OCCT and SAM.Geometry.OCCT both declare a static Query class.
using GeometryQuery = SAM.Geometry.OCCT.Query;

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
    /// (docs/CELLCOMPLEX_FIRST_HANDOVER.md §3, P4) phases re-run against.
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

        /// <summary>Fixture-specific tuning reported from Grasshopper. This is deliberately not the generic
        /// default: 0.4 crosses the fixture's 0.283/0.363 m level near-misses. It improves workflow B from
        /// 28 to all 32 reference spaces with no open wall ends and clean analytical parity. The last one, the
        /// cell near (19.584,-4.909,13.925), was a 12-28 mm floor/north-wall slot left by the uniform Fill; the
        /// residual cap-edge closure (documentation/Towers-Residual-Closure-Q4.md) recovers it (was 31 of 32;
        /// this test was named ..._Produces31ParityCleanSpaces).</summary>
        [SkippableFact]
        public void WorkflowParity_WholeLevelTowers_FixtureTuning04_Produces32ParityCleanSpaces()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            List<Panel> inputPanels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(inputPanels);

            List<Panel> extended = inputPanels.Extend3D(
                out List<string> diagnostics,
                out Solve3DReport report,
                fillMargin: 0.4,
                bucketBetweenLevels: 0.4);
            List<Panel> nonAir = (extended ?? new List<Panel>())
                .Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid())
                .ToList();
            WorkflowResult workflow = BuildWorkflow("B-clean-extend-0.4", nonAir, SolverMatchedOptions());

            output.WriteLine("FILL=0.4; BAND=0.4; " + FormatWorkflow(workflow));
            output.WriteLine(string.Format("RAW_FRAMES={0}; GROUPS={1}", report.LevelFrames.Count, report.LevelGroups.Count));
            foreach (string line in diagnostics.Where(x =>
                x.Contains("OPEN_ENDS") || x.Contains("LevelBandNearMiss") || x.Contains("CLEAN3D_LEVEL")))
            {
                output.WriteLine(line);
            }

            Assert.NotNull(workflow.Cluster);
            Assert.Equal(32, workflow.Cluster.GetSpaces()?.Count);
            Assert.Equal(7, report.LevelGroups.Count);
            Assert.Contains(diagnostics, x => x.Contains("SAM_OCCT_EXTEND3D_OPEN_ENDS: 0 wall end"));
            Assert.Equal(OcctDiagnosticSeverity.Info,
                workflow.CellComplexResult.Diagnostics.Last(x => x.Code == "SAM_OCCT_ANALYTICAL_PARITY").Severity);
            Assert.DoesNotContain(workflow.Cluster.GetPanels() ?? new List<Panel>(), x =>
                (workflow.Cluster.GetSpaces(x) ?? new List<Space>()).Count == 0);
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
            // The native solver's own closure first: it does not depend on how the solved faces are presented.
            AssertSolverInvariants(fixture, report, nakedPoint3Ds, solvedNonAir);

            // solver-matched A and B are the two workflows this harness holds to account; A-old is
            // logged for the P1 options-bugfix comparison only, never asserted as "should be correct".
            AssertWorkflowAgainstExpectation(fixture, "A-solver-matched", workflowAMatched, report.ResolvedCellCount, output);
            AssertWorkflowAgainstExpectation(fixture, "B-clean-extend", workflowB, report.ResolvedCellCount, output);
        }

        /// <summary>
        /// Closure the native solver itself must reach on a fixture, asserted on <c>Solve3D</c>'s own result (resolved
        /// cells and naked points) and not on a rebuild of its output. Where a fixture's workflow row below is marked
        /// <see cref="Expectation.SpacesCountIsDiagnostic"/>, this is what holds that fixture to account.
        /// </summary>
        private class SolverInvariant
        {
            public int ResolvedCells;
            public int NakedPoints;
            public string Support;
        }

        private static readonly Dictionary<string, SolverInvariant> SolverInvariants = new Dictionary<string, SolverInvariant>
        {
            // Face3D-home. 20 cells: PR61Face3DHomeParityTests (solver cells=20 executed at base c643b29) and
            // Extend3DPlaneTargetIntegrationTests (20 cells on the forced managed path, owner decision 2026-07-08).
            // 0 naked points: the default Solve3D path measured 0 (the managed path's 4 is a different path, pinned there);
            // documentation/Sew-Guard-Invalid-Cells-Q4.md records Face3D-home solved with 0 naked. Cell volume is not
            // asserted: 601.30 m3 is recorded there and in every presentation that reaches 20 cells, but it comes from the
            // same solver, so it is not independent evidence of the enclosed volume.
            ["Face3D-home.sam"] = new SolverInvariant
            {
                ResolvedCells = 20,
                NakedPoints = 0,
                Support = "PR61Face3DHomeParityTests (c643b29), Extend3DPlaneTargetIntegrationTests, Sew-Guard-Invalid-Cells-Q4 record"
            },
        };

        private static void AssertSolverInvariants(string fixture, Solve3DReport solveReport, List<Point3D> nakedPoint3Ds, List<Panel> solvedNonAir)
        {
            if (!SolverInvariants.TryGetValue(fixture, out SolverInvariant invariant))
            {
                return;
            }

            // The solver publishes an empty naked-point list when its own validation produced no report, so an empty
            // list (or a zero count read from it) is not by itself a closure measurement. Require that the native
            // resolve ran and the list exists, then re-measure the solved faces with a validation call whose
            // return value and report are checked.
            Assert.NotNull(solveReport);
            Assert.True(solveReport.NativeResolved, fixture + ": the native resolve did not run, so the solver closure was not measured");
            Assert.NotNull(solveReport.Signature);
            Assert.NotNull(nakedPoint3Ds);

            int resolvedCells = solveReport.ResolvedCellCount;
            int nakedPoints = nakedPoint3Ds.Count;
            Assert.True(resolvedCells == invariant.ResolvedCells && nakedPoints == invariant.NakedPoints,
                string.Format("{0}: native solver closure is {1} cells / {2} naked points, expected {3} / {4} (support: {5})",
                    fixture, resolvedCells, nakedPoints, invariant.ResolvedCells, invariant.NakedPoints, invariant.Support));

            // The same faces in the order the solver validated them, with the options it uses: this shows the measurement was
            // made and agrees with the solver's report. It is not an order-independent invariant - the sewing-based naked-edge
            // count of the same solved faces depends on list order and loop start (documentation/Wall-Run-Face-Order-Abut-Q4.md).
            List<Face3D> solvedFace3Ds = solvedNonAir.Select(x => x.GetFace3D()).ToList();
            Assert.NotEmpty(solvedFace3Ds);

            bool validated = GeometryQuery.Validate(solvedFace3Ds, out OcctValidationReport closureReport, out OcctCellComplexResult closureResult, SolverMatchedOptions(), false);
            using (closureResult)
            {
                Assert.True(validated, fixture + ": validating the solved faces failed: " + string.Join("; ", closureResult?.Diagnostics?.Select(x => x.ToString()) ?? Enumerable.Empty<string>()));
                Assert.True(closureResult.NativeAvailable, fixture + ": the native validation did not run");
                Assert.NotNull(closureReport);
                Assert.True(closureReport.CountOf(OcctValidationIssueCategory.NakedEdge) == invariant.NakedPoints,
                    string.Format("{0}: independent validation of the solved faces reports {1} naked edge(s), expected {2}",
                        fixture, closureReport.CountOf(OcctValidationIssueCategory.NakedEdge), invariant.NakedPoints));
            }
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
        /// silently skipped (docs/CELLCOMPLEX_FIRST_HANDOVER.md §3, P1). A fixture/workflow not
        /// listed here is expected clean on all three invariants.
        /// </summary>
        private static readonly Dictionary<(string fixture, string workflow), Expectation> Expectations = new Dictionary<(string, string), Expectation>
        {
            // Values current after E2 (docs/EXTEND3D_ROBUST_HANDOVER.md): plane-target cap extension. On the
            // rigidly-tilted and flat fixtures E2 is BYTE-IDENTICAL (their caps are level relative to their
            // walls, so the discriminator routes them to the pre-E2 scalar path) - those rows are unchanged
            // from E1. E2's plane-targeting engages only on the messy real-export fixtures with genuine pitched
            // roofs (Revit-home-panels, AdjacencyCluster-home, Face3D-home), raising solver closure. Every
            // workflow below stays parity-clean with zero orphans - E2 adds no workflow-level naked regression;
            // the one +1 on Face3D-home is on the solver's internal closure metric (no golden pin), accepted as
            // part of a large net improvement (owner decision 2026-07-08).

            // whole-level-towers: A-solver-matched closes 32/32; workflow B still under-closes (28 vs 32).
            // UNCHANGED by E2 - a rigidly-tilted level, byte-identical (frame-aware extension, not E2's
            // plane-targeting, is what would close the residual; deferred).
            [("whole-level-towers.sam", "B-clean-extend")] = new Expectation
            {
                SpacesMatchResolvedCellCount = false,
                TrackingComment = "workflow B under-closes whole-level-towers (28 vs 32 solver cells) - tilted level, byte-identical under E2; needs frame-aware extension"
            },
            // Face3D-home (real export, pitched roofs): E2 (review-corrected) changed the managed fallback
            // (solver 25 -> 20 cells). Both A-solver-matched and B-clean-extend under-close 19 vs 20, parity
            // clean. Matching c643b29 (pre-PR61 baseline) run gives identical 19-vs-20 result, proving the
            // gap is pre-existing (E2 plane-targeting change) and NOT a PR #61 regression.
            // The 19-vs-20 mismatch this row used to require is not asserted any more: the sewing rebuild's space count
            // is presentation dependent (the same solved geometry gives 11 to 20 spaces by face order and loop start;
            // documentation/Wall-Run-Face-Order-Abut-Q4.md, "Face3D-home parity"), and at the sow/2026-Q4 base it was 15, not 19.
            // The native solver's closure (20 cells, 0 naked) is asserted in SolverInvariants; the count is diagnostic.
            [("Face3D-home.sam", "A-solver-matched")] = new Expectation
            {
                SpacesCountIsDiagnostic = true,
                ParityClean = true,
                TrackingComment = "Face3D-home A-solver-matched: sew-rebuild space count is diagnostic only; solver closure asserted in SolverInvariants; parity clean"
            },
            [("Face3D-home.sam", "B-clean-extend")] = new Expectation
            {
                SpacesMatchResolvedCellCount = false,
                ParityClean = true,
                TrackingComment = "Face3D-home B-clean-extend under-closes (19 vs 20 solver cells); identical at c643b29 base - pre-existing, not a PR #61 regression"
            },
            // Revit-home-panels (real export, pitched roofs): E2 (review-corrected) closed the managed solve
            // watertight (12/4 -> 14/0). A-solver-matched closes cleanly 14/14 (no entry). Workflow B still
            // under-closes 9 vs 14.
            [("Revit-home-panels.sam", "B-clean-extend")] = new Expectation
            {
                SpacesMatchResolvedCellCount = false,
                TrackingComment = "Revit-home-panels workflow B under-closes (9 vs 14 solver cells); E2 closed the managed solve watertight (12/4 -> 14/0)"
            },
            // AdjacencyCluster-home (real export, pitched roofs): E2 (review-corrected column-wise clamped
            // plane target) cut managed naked 25 -> 4 at the same 18-cell decomposition, and BOTH
            // solver-matched workflows close 18/18 parity-clean - no expectation entries needed (they pass
            // on the default match + clean).
            // two-level-tilted: raw-first (production) adopts 44 cells. UNCHANGED by E2 (rigidly-tilted level,
            // byte-identical). A-solver-matched rebuilds 43; workflow B is far off (25 vs 44) with a parity
            // warning - needs frame-aware extension (not E2's plane-targeting), deferred.
            [("two-level-tilted.sam", "A-solver-matched")] = new Expectation
            {
                SpacesMatchResolvedCellCount = false,
                TrackingComment = "two-level-tilted A-solver-matched rebuild is short one cell (43 vs 44 solver cells) - unchanged by E2"
            },
            [("two-level-tilted.sam", "B-clean-extend")] = new Expectation
            {
                SpacesMatchResolvedCellCount = false,
                ParityClean = false,
                TrackingComment = "two-level-tilted workflow B under-closes (25 vs 44 solver cells) with a parity warning - tilted level, unchanged by E2; needs frame-aware extension"
            },
        };

        private class Expectation
        {
            public bool SpacesMatchResolvedCellCount = true;
            /// <summary>The space count of a sewing rebuild of the solver output depends on the order and loop start of the
            /// faces it is given (same geometry, 11 to 20 spaces on Face3D-home), so it is not a pass/fail criterion where
            /// this is set: it is written to the test output and <see cref="SpacesMatchResolvedCellCount"/> is not read.</summary>
            public bool SpacesCountIsDiagnostic;
            public bool ParityClean = true;
            public bool RelationInvariantAcrossMerge = true;
            public string TrackingComment;
        }

        private static void AssertWorkflowAgainstExpectation(string fixture, string workflowName, WorkflowResult workflowResult, int resolvedCellCount, ITestOutputHelper output)
        {
            Expectations.TryGetValue((fixture, workflowName), out Expectation expectation);
            expectation = expectation ?? new Expectation();

            bool spacesMatch = workflowResult.Cluster != null && workflowResult.Cluster.GetSpaces()?.Count == resolvedCellCount;
            if (expectation.SpacesCountIsDiagnostic)
            {
                // The count is not asserted, but the rebuild must have succeeded: a failed rebuild must not pass as a diagnostic.
                Assert.True(workflowResult.Cluster != null && workflowResult.Merged != null && workflowResult.CellComplexResult != null,
                    string.Format("{0}/{1}: the rebuild produced no cluster, merged cluster or cell-complex result (tracking: {2})", fixture, workflowName, expectation.TrackingComment ?? "none"));
                Assert.True((workflowResult.Cluster.GetSpaces()?.Count ?? 0) > 0,
                    string.Format("{0}/{1}: the rebuild produced no spaces (tracking: {2})", fixture, workflowName, expectation.TrackingComment ?? "none"));
                Assert.True(ParityClean(workflowResult.CellComplexResult) != null,
                    string.Format("{0}/{1}: the rebuild reported no analytical parity diagnostic (tracking: {2})", fixture, workflowName, expectation.TrackingComment ?? "none"));                output.WriteLine(string.Format("[diagnostic] {0}/{1}: sew-rebuild spaces={2}, solver cells={3} (not asserted; presentation dependent)",
                    fixture, workflowName, workflowResult.Cluster?.GetSpaces()?.Count ?? -1, resolvedCellCount));
            }
            else
            {
                Assert.True(spacesMatch == expectation.SpacesMatchResolvedCellCount,
                    string.Format("{0}/{1}: spaces=={2} match expected {3} (tracking: {4})", fixture, workflowName, resolvedCellCount, expectation.SpacesMatchResolvedCellCount, expectation.TrackingComment ?? "none"));
            }

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
