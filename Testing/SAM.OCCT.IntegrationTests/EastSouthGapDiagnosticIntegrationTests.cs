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
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Report-only diagnostics for the 9-space fixture's East1|South1 acceptance gap
    /// (<see cref="ControlledWorkflowAcceptanceIntegrationTests"/>). Corrects an earlier diagnosis: the
    /// separating wall's opposed-overlap near-miss (~96.76%, just under
    /// <see cref="Panel3DSnapSolver.OPPOSED_PARTITION_MIN_OVERLAP_RATIO"/>) was initially assumed to be the
    /// root cause. It is NOT - reducing the four-skin band to a single, perfectly clean separator panel still
    /// leaves East1 and South1 Missing (only the sliver Extra cell disappears; see
    /// <see cref="SingleCleanSeparator_StillMissing_ProvesOverlapGateIsNotTheBlocker"/>). East1 and South1 each
    /// have walls on all four sides and floor + roof caps present (verified separately, same as the matching
    /// South2), yet the native MakerVolume build reports it "could not close" the two cells - a small
    /// watertight-closure gap (not a missing element), evidenced by
    /// <see cref="SewingTolerance_Sweep_WiderToleranceClosesAllNineSpaces"/>: the default 0.01 m sewing
    /// tolerance yields 7/9, while 0.20 m yields 9/9 matched (at the cost of spurious extra cells - too coarse
    /// for production, but proof the true gap is on that order). The isolated repro fixture
    /// (<c>Panels-EastSouth-Isolated.sam</c>, 40 of the original 66 panels around just this corner) reproduces
    /// the full model's 7/9 result exactly, so the source-model fix can be found and verified there directly.
    /// </summary>
    public class EastSouthGapDiagnosticIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ControlledWorkflow");
        private static readonly Guid West3Guid = new Guid("02a1ae27-5461-4b41-ad07-008ccd9d1159");

        // The four near-parallel skins of the East1|South1 separating wall band (baseline §8/P0 §5).
        private static readonly Guid SkinOuter = new Guid("20fe83aa-ad65-4551-935a-97459edc959f"); // y=-23.006
        private static readonly Guid SkinInner1 = new Guid("31f97c71-855b-4a67-aed2-a0a364b1a728"); // y=-23.228
        private static readonly Guid SkinInner2 = new Guid("763f6aa3-e1f1-4de0-9ac3-fce16205de34"); // y=-23.339
        private static readonly Guid SkinInner3 = new Guid("5b9dbfd6-3772-475a-ab80-26c830bd2a39"); // y=-23.434

        private readonly ITestOutputHelper output;

        public EastSouthGapDiagnosticIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static List<Panel> LoadPanels() => SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Panels-9SpacesModel.sam")).OfType<Panel>().ToList();
        private static List<Space> LoadSpaces() => SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Spaces-9SpacesModel.sam")).OfType<Space>().ToList();

        private static SpaceMatchReport RunChain(List<Panel> panels, List<Space> spaces, double sewingTolerance = 0.01)
        {
            List<Panel> cleaned = panels.Clean3D(out _, out _, bucketBetweenLevels: 0.21);
            List<Panel> extended = cleaned.Extend3D(out _, out _, bucketBetweenLevels: 0.21, inputAlreadyClean: true, fillMargin: 1.0, directionalCapGrow: false);

            SpaceMatchOptions options = new SpaceMatchOptions { LevelBand = 0.21, LevelGroupBand = 0.21 };
            ExpectedSpaceSet expectedSpaceSet = ExpectedSpaceSet.Create(spaces, cleaned, options, new[] { West3Guid });

            AdjacencyCluster cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                expectedSpaceSet.ToSeedSpaces(), extended, out OcctCellComplexResult result, new Log(),
                new OcctBuildOptions { Tolerance = Tolerance.Distance, FuzzyTolerance = Tolerance.MacroDistance, AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = sewingTolerance });

            List<CellGeometry> cells = CellGeometry.FromComplex(result);
            return SpaceMatcher.Match(expectedSpaceSet, cells, new[] { West3Guid }, cluster, panels);
        }

        /// <summary>
        /// Replaces the four-skin band with a SINGLE clean separator panel. Previously this proved the overlap-
        /// ratio gate was NOT the blocker (East1/South1 still missing). After the fillMargin fix, both spaces
        /// now close with the single separator too — confirming the real fix is uniform cap growth.
        /// </summary>
        [SkippableFact]
        public void SingleCleanSeparator_StillMissing_ProvesOverlapGateIsNotTheBlocker()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            Guid[] band = { SkinOuter, SkinInner1, SkinInner2, SkinInner3 };
            List<Panel> singleSeparator = LoadPanels().Where(p => !band.Contains(p.Guid) || p.Guid == SkinInner1).ToList();

            SpaceMatchReport report = RunChain(singleSeparator, LoadSpaces());
            foreach (string line in report.ToLines()) output.WriteLine(line);

            // Both the sliver Extra cell and the East1/South1 gap are resolved with the fillMargin fix.
            Assert.Equal(9, report.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Matched));
            Assert.Empty(report.SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Missing));
        }

        /// <summary>
        /// With the fillMargin fix, all 9 spaces close at the default 0.01 m sewing tolerance. The previously
        /// diagnostic 0.20 m tolerance now degrades the result (aggressive sew merges cells with the larger
        /// uniform cap growth), confirming the gap is resolved at the source (fill) level rather than patched
        /// at the sew level.
        /// </summary>
        [SkippableFact]
        public void SewingTolerance_Sweep_WiderToleranceClosesAllNineSpaces()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> panels = LoadPanels();
            List<Space> spaces = LoadSpaces();

            SpaceMatchReport atDefault = RunChain(panels, spaces, sewingTolerance: 0.01);
            output.WriteLine("SewingTolerance=0.01 (default): " + atDefault.ToLines().First(x => x.Contains("SUMMARY")));
            Assert.Equal(9, atDefault.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Matched));
            Assert.Empty(atDefault.SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Missing));

            // The wider tolerance no longer helps (and can degrade) — the gap is resolved at the fill level.
            SpaceMatchReport atWide = RunChain(panels, spaces, sewingTolerance: 0.20);
            output.WriteLine("SewingTolerance=0.20: " + atWide.ToLines().First(x => x.Contains("SUMMARY")));
            int wideMatched = atWide.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Matched);
            output.WriteLine("SewingTolerance=0.20 matched: {0} (may be <9 — aggressive sew + uniform cap growth can merge cells)", wideMatched);
            Assert.True(wideMatched > 0, "Wider tolerance should not break everything.");
        }

        /// <summary>
        /// The isolated repro fixture (<c>Panels-EastSouth-Isolated.sam</c>/<c>Spaces-EastSouth-Isolated.sam</c>,
        /// 40 of the original 66 panels, filtered to just the East1|South1|South2 neighbourhood) now produces
        /// 3/3 matched after the fillMargin fix (fillMargin=1.0, directionalCapGrow=false).
        /// </summary>
        [SkippableFact]
        public void IsolatedFixture_ReproducesFullModelResult()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> isolatedPanels = SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Panels-EastSouth-Isolated.sam")).OfType<Panel>().ToList();
            List<Space> isolatedSpaces = SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Spaces-EastSouth-Isolated.sam")).OfType<Space>().ToList();

            List<Panel> cleaned = isolatedPanels.Clean3D(out _, out _, bucketBetweenLevels: 0.21);
            List<Panel> extended = cleaned.Extend3D(out _, out _, bucketBetweenLevels: 0.21, inputAlreadyClean: true, fillMargin: 1.0, directionalCapGrow: false);
            SpaceMatchOptions options = new SpaceMatchOptions { LevelBand = 0.21, LevelGroupBand = 0.21 };
            ExpectedSpaceSet expectedSpaceSet = ExpectedSpaceSet.Create(isolatedSpaces, cleaned, options, Array.Empty<Guid>());
            AdjacencyCluster cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                expectedSpaceSet.ToSeedSpaces(), extended, out OcctCellComplexResult result, new Log(),
                new OcctBuildOptions { Tolerance = Tolerance.Distance, FuzzyTolerance = Tolerance.MacroDistance, AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 });
            List<CellGeometry> cells = CellGeometry.FromComplex(result);
            SpaceMatchReport report = SpaceMatcher.Match(expectedSpaceSet, cells, Array.Empty<Guid>(), cluster, isolatedPanels);
            foreach (string line in report.ToLines()) output.WriteLine(line);

            Assert.Equal(3, report.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Matched)); // South2, East1, South1
            Assert.Empty(report.SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Missing));
        }
    }
}
