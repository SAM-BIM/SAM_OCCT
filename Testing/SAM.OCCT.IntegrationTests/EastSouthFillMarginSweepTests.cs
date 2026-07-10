// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Sweeps Extend3D fillMargin + directionalCapGrow to find a config that closes the East1|South1 gap.
    /// </summary>
    public class EastSouthFillMarginSweepTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ControlledWorkflow");

        private readonly ITestOutputHelper output;

        public EastSouthFillMarginSweepTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [SkippableFact]
        public void FillMarginSweep_IsolatedFixture_ReportsBestMatch()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> panels = SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Panels-EastSouth-Isolated.sam")).OfType<Panel>().ToList();
            List<Space> spaces = SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Spaces-EastSouth-Isolated.sam")).OfType<Space>().ToList();

            double[] margins = { 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.5, 2.0, 3.0 };
            bool[] directional = { true, false };

            output.WriteLine("fillMargin | dirCapGrow | matched | missing | extra | cells | summary");
            output.WriteLine("-----------|------------|---------|---------|-------|-------|--------");

            int bestMatched = 0;
            double bestMargin = 0;
            bool bestDir = false;

            // Pre-clean once (cleaning is deterministic and same for all fillMargin values).
            List<Panel> cleaned = panels.Clean3D(out _, out _, bucketBetweenLevels: 0.21);

            foreach (double fillMargin in margins)
            {
                foreach (bool dirGrow in directional)
                {
                    List<Panel> extended = cleaned.Extend3D(out _, out _,
                        bucketBetweenLevels: 0.21, inputAlreadyClean: true,
                        fillMargin: fillMargin, directionalCapGrow: dirGrow);

                    SpaceMatchOptions opts = new SpaceMatchOptions { LevelBand = 0.21, LevelGroupBand = 0.21 };
                    ExpectedSpaceSet ess = ExpectedSpaceSet.Create(spaces, cleaned, opts, Array.Empty<Guid>());

                    AdjacencyCluster cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                        ess.ToSeedSpaces(), extended, out OcctCellComplexResult result, new SAM.Core.Log(),
                        new SAM.Core.OCCT.OcctBuildOptions { Tolerance = SAM.Core.Tolerance.Distance, FuzzyTolerance = SAM.Core.Tolerance.MacroDistance, AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 });

                    List<CellGeometry> cells = CellGeometry.FromComplex(result);
                    SpaceMatchReport report = SpaceMatcher.Match(ess, cells, Array.Empty<Guid>(), cluster, panels);

                    int matched = report.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Matched);
                    int missing = report.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Missing);
                    int extra = report.CellMatches.Count(x => x.Outcome == SpaceMatchOutcome.Extra);
                    int cellCount = result.Cells.Count;

                    string missingNames = missing > 0
                        ? string.Join(",", report.SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Missing).Select(x => x.Name))
                        : "-";

                    output.WriteLine("{0,-11} | {1,-10} | {2,-7} | {3,-7} | {4,-5} | {5,-5} | missing={6}",
                        fillMargin, dirGrow, matched, missing, extra, cellCount, missingNames);

                    if (matched > bestMatched || (matched == bestMatched && missing < bestMatched))
                    {
                        bestMatched = matched;
                        bestMargin = fillMargin;
                        bestDir = dirGrow;
                    }
                }
            }

            output.WriteLine("");
            output.WriteLine("Best: fillMargin={0}, dirCapGrow={1} -> matched={2}", bestMargin, bestDir, bestMatched);

            Assert.True(bestMatched >= 1, "Should at least match South2.");
        }

        [SkippableFact]
        public void FillMarginSweep_Full9SpaceFixture_DetailedTable()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> panels = SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Panels-9SpacesModel.sam")).OfType<Panel>().ToList();
            List<Space> spaces = SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Spaces-9SpacesModel.sam")).OfType<Space>().ToList();
            Guid west3 = new Guid("02a1ae27-5461-4b41-ad07-008ccd9d1159");

            double[] margins = { 0.5, 0.6, 1.0 };
            bool[] directional = { true, false };

            output.WriteLine("fillMargin | dirCapGrow | matched | missing | extra | cells | missing_names");
            output.WriteLine("-----------|------------|---------|---------|-------|-------|-------------");

            // Pre-clean once.
            List<Panel> cleaned = panels.Clean3D(out _, out _, bucketBetweenLevels: 0.21);

            foreach (double fillMargin in margins)
            {
                foreach (bool dirGrow in directional)
                {
                    List<Panel> extended = cleaned.Extend3D(out _, out _,
                        bucketBetweenLevels: 0.21, inputAlreadyClean: true,
                        fillMargin: fillMargin, directionalCapGrow: dirGrow);

                    SpaceMatchOptions opts = new SpaceMatchOptions { LevelBand = 0.21, LevelGroupBand = 0.21 };
                    ExpectedSpaceSet ess = ExpectedSpaceSet.Create(spaces, cleaned, opts, new[] { west3 });

                    AdjacencyCluster cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                        ess.ToSeedSpaces(), extended, out OcctCellComplexResult result, new SAM.Core.Log(),
                        new SAM.Core.OCCT.OcctBuildOptions { Tolerance = SAM.Core.Tolerance.Distance, FuzzyTolerance = SAM.Core.Tolerance.MacroDistance, AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 });

                    List<CellGeometry> cells = CellGeometry.FromComplex(result);
                    SpaceMatchReport report = SpaceMatcher.Match(ess, cells, new[] { west3 }, cluster, panels);

                    int matched = report.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Matched);
                    int missing = report.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Missing);
                    int extra = report.CellMatches.Count(x => x.Outcome == SpaceMatchOutcome.Extra);
                    int merged = report.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Merged);
                    int cellCount = result.Cells.Count;

                    string missingNames = missing > 0
                        ? string.Join(",", report.SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Missing).Select(x => x.Name).OrderBy(x => x))
                        : "-";

                    output.WriteLine("{0,-11} | {1,-10} | {2,-7} | {3,-7} | {4,-5} | {5,-5} | {6}",
                        fillMargin, dirGrow, matched, missing, extra, cellCount, missingNames);
                }
            }

            Assert.True(true);
        }
    }
}
