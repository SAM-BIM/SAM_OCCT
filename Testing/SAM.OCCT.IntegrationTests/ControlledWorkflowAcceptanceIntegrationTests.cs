// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// P4 nine-space acceptance (docs/CONTROLLED_WORKFLOW_PLAN.md §9) on the controlled fixture, through the
    /// exact chain: Clean3D(bucketBetweenLevels: 0.21) -&gt; Extend3D(inputAlreadyClean: true,
    /// directionalCapGrow: true, bucketBetweenLevels: 0.21) -&gt; Create.AdjacencyCluster rebuild path (seeds =
    /// ExpectedSpaceSet.ToSeedSpaces()) -&gt; SpaceMatcher.
    /// <para>
    /// Current status (see docs/CONTROLLED_WORKFLOW_BASELINE.md §9 addendum): 7 of 9 spaces match cleanly
    /// (North0/1/2, South2, West1/2, double-height West3), 3 level groups, 0 orphan cluster panels. East1 and
    /// South1 do not close: their separating wall is modeled as two panels (20fe83aa / 31f97c71) offset just
    /// enough that their in-plane overlap (~96.7%) narrowly misses <c>Panel3DSnapSolver.
    /// OPPOSED_PARTITION_MIN_OVERLAP_RATIO</c> (0.97) - a solver-threshold edge case, not a builder/validation
    /// defect, and not reachable through the sanctioned per-panel overrides (BucketSize/Weight/MaxExtend all
    /// leave it unchanged - verified). Per plan §10-P4 ("if a solver defect blocks acceptance, STOP and report
    /// instead of patching ad hoc"), this gap is pinned here rather than silently accepted or hacked around.
    /// </para>
    /// </summary>
    public class ControlledWorkflowAcceptanceIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ControlledWorkflow");

        private static readonly Guid West3Guid = new Guid("02a1ae27-5461-4b41-ad07-008ccd9d1159");

        /// <summary>The two spaces the known East1|South1 gap (see class remarks) currently leaves Missing.</summary>
        private static readonly HashSet<string> KnownGapNames = new HashSet<string> { "East1", "South1" };

        private readonly ITestOutputHelper output;

        public ControlledWorkflowAcceptanceIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static (List<Panel> extended, ExpectedSpaceSet expectedSpaceSet, AdjacencyCluster cluster, OcctCellComplexResult result, List<Panel> originalPanels) RunChain()
        {
            string panelsPath = Path.Combine(FixturesDirectory, "Panels-9SpacesModel.sam");
            string spacesPath = Path.Combine(FixturesDirectory, "Spaces-9SpacesModel.sam");
            List<Panel> originalPanels = SAM.Core.Convert.ToSAM(panelsPath).OfType<Panel>().ToList();
            List<Space> expectedSpaces = SAM.Core.Convert.ToSAM(spacesPath).OfType<Space>().ToList();

            // Clean3D -> Extend3D, the exact controlled-fixture chain (plan §9): 0.21 / inputAlreadyClean=true /
            // directionalCapGrow=true.
            List<Panel> cleaned = originalPanels.Clean3D(out _, out Solve3DReport cleanReport, bucketBetweenLevels: 0.21);
            List<Panel> extended = cleaned.Extend3D(out _, out _, bucketBetweenLevels: 0.21, inputAlreadyClean: true, directionalCapGrow: true);

            // The matcher's level datums come from the CLEANED panels (already normalized onto the 3 group
            // datums), not the raw original panels: ExpectedSpaceSet's own independent LevelFrame.Cluster over
            // RAW input caps sees 22 distinct slab-skin elevations and produces a DIFFERENT (wrong) grouping
            // than what Clean3D's post-bucket-snap frame clustering actually derives (verified: raw-original
            // source gives 12.160/12.503/15.210/18.260, four groups, none matching the real 12.24/15.29/18.34
            // datums the built cells actually sit on). Sourcing from the cleaned panels keeps the matcher
            // consistent with the geometry it is validating.
            SpaceMatchOptions options = new SpaceMatchOptions { LevelBand = 0.21, LevelGroupBand = 0.21 };
            ExpectedSpaceSet expectedSpaceSet = ExpectedSpaceSet.Create(expectedSpaces, cleaned, options, new[] { West3Guid });

            Log log = new Log();
            AdjacencyCluster cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                expectedSpaceSet.ToSeedSpaces(),
                extended,
                out OcctCellComplexResult result,
                log,
                new OcctBuildOptions { Tolerance = Tolerance.Distance, FuzzyTolerance = Tolerance.MacroDistance, AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 });

            return (extended, expectedSpaceSet, cluster, result, originalPanels);
        }

        [SkippableFact]
        public void AcceptanceChain_FixtureNineSpaces_SevenMatchCleanlyKnownEastSouthGapPinned()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            (List<Panel> extended, ExpectedSpaceSet expectedSpaceSet, AdjacencyCluster cluster, OcctCellComplexResult result, List<Panel> originalPanels) = RunChain();

            List<CellGeometry> cells = CellGeometry.FromComplex(result);
            SpaceMatchReport report = SpaceMatcher.Match(expectedSpaceSet, cells, new[] { West3Guid }, cluster, originalPanels);
            foreach (string line in report.ToLines())
            {
                output.WriteLine(line);
            }

            // Level grouping (plan §9): the fixture's 5 raw frames merge to exactly 3 group datums.
            Assert.Equal(new[] { 12.24, 15.29, 18.34 }, report.LevelGroupDatums.Select(x => System.Math.Round(x, 2)));

            // Every space NOT in the known East1|South1 gap matches cleanly (0 merged/split/incorrect anywhere).
            Assert.Empty(report.SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Merged));
            Assert.Empty(report.SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Split));
            Assert.Empty(report.SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.IncorrectlyBounded));

            List<string> missingNames = report.SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Missing).Select(x => x.Name).ToList();
            Assert.Equal(KnownGapNames.OrderBy(x => x), missingNames.OrderBy(x => x));

            List<string> matchedNames = report.SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Matched).Select(x => x.Name).ToList();
            Assert.Equal(7, matchedNames.Count);
            Assert.DoesNotContain(matchedNames, KnownGapNames.Contains);

            // West3 (GUID-backed, plan §0.1) is double-height in the resolved chain.
            Assert.True(report.DoubleHeightOk.TryGetValue(West3Guid, out bool west3Ok) && west3Ok, "West3 failed its double-height check.");

            // Builder-diagnostics gate (plan §9): no generated cluster panel bounds zero spaces.
            Assert.Empty(report.OrphanClusterPanelGuids);

            // Exactly one EXTRA cell - the thin sliver between East1/South1's not-quite-merged wall halves; not
            // asserted away (see class remarks), just pinned so a regression that produces MORE extras is caught.
            Assert.Single(report.CellMatches.Where(x => x.Outcome == SpaceMatchOutcome.Extra));
        }

        /// <summary>
        /// Documents the East1|South1 root cause precisely (class remarks): the wall panels 20fe83aa/31f97c71
        /// are present in the input and directly candidate for the separation, but their footprint overlap sits
        /// just under the solver's opposed-collapse floor. Pinned as evidence, not asserted as a pass.
        /// </summary>
        [SkippableFact]
        public void AcceptanceChain_EastSouthGap_SeparatorPanelsPresentButOverlapJustUnderThreshold()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            (List<Panel> _, ExpectedSpaceSet _, AdjacencyCluster _, OcctCellComplexResult _, List<Panel> originalPanels) = RunChain();

            Panel a = originalPanels.First(x => x.Guid == new Guid("20fe83aa-ad65-4551-935a-97459edc959f"));
            Panel b = originalPanels.First(x => x.Guid == new Guid("31f97c71-855b-4a67-aed2-a0a364b1a728"));

            BoundingBox3D boxA = a.GetFace3D().GetBoundingBox();
            BoundingBox3D boxB = b.GetFace3D().GetBoundingBox();

            double overlapX = System.Math.Min(boxA.Max.X, boxB.Max.X) - System.Math.Max(boxA.Min.X, boxB.Min.X);
            double overlapZ = System.Math.Min(boxA.Max.Z, boxB.Max.Z) - System.Math.Max(boxA.Min.Z, boxB.Min.Z);
            double overlapArea = overlapX * overlapZ;
            double areaA = a.GetFace3D().GetArea();
            double ratio = overlapArea / areaA;

            output.WriteLine("East1|South1 separator candidate overlap ratio: {0:0.0000} (solver floor: 0.97)", ratio);

            // Both panels are genuinely present (not the "genuinely absent" case) - separation is far below a
            // wall thickness, well inside the void-guard's collapse range.
            double separation = System.Math.Abs(boxA.Min.Y - boxB.Min.Y);
            Assert.True(separation < 0.3, "Expected the two candidate separator panels to sit within a plausible double-wall separation.");

            // The near-miss: overlap ratio close to, but under, the solver's 0.97 opposed-collapse floor.
            Assert.InRange(ratio, 0.90, 0.97);
        }
    }
}
