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
    public class EastSouthFullSweepDetailedTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ControlledWorkflow");
        private static readonly Guid West3Guid = new Guid("02a1ae27-5461-4b41-ad07-008ccd9d1159");

        private readonly ITestOutputHelper output;

        public EastSouthFullSweepDetailedTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [SkippableFact]
        public void FullSweep_0_1_to_1_0_DetailedResults()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> panels = SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Panels-9SpacesModel.sam")).OfType<Panel>().ToList();
            List<Space> spaces = SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Spaces-9SpacesModel.sam")).OfType<Space>().ToList();

            double[] margins = { 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0 };
            bool[] directional = { true, false };

            List<string> results = new List<string>();
            results.Add("fillMargin | dirCapGrow | matched | missing | extra | cells | missing_names");
            results.Add("-----------|------------|---------|---------|-------|-------|-------------");

            List<Panel> cleaned = panels.Clean3D(out _, out _, bucketBetweenLevels: 0.21);

            foreach (double fm in margins)
            {
                foreach (bool dg in directional)
                {
                    List<Panel> extended = cleaned.Extend3D(out _, out _,
                        bucketBetweenLevels: 0.21, inputAlreadyClean: true,
                        fillMargin: fm, directionalCapGrow: dg);

                    SpaceMatchOptions opts = new SpaceMatchOptions { LevelBand = 0.21, LevelGroupBand = 0.21 };
                    ExpectedSpaceSet ess = ExpectedSpaceSet.Create(spaces, cleaned, opts, new[] { West3Guid });

                    AdjacencyCluster cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                        ess.ToSeedSpaces(), extended, out OcctCellComplexResult r, new SAM.Core.Log(),
                        new SAM.Core.OCCT.OcctBuildOptions { Tolerance = SAM.Core.Tolerance.Distance, FuzzyTolerance = SAM.Core.Tolerance.MacroDistance, AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 });

                    List<CellGeometry> cells = CellGeometry.FromComplex(r);
                    SpaceMatchReport report = SpaceMatcher.Match(ess, cells, new[] { West3Guid }, cluster, panels);

                    int matched = report.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Matched);
                    int missing = report.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Missing);
                    int extra = report.CellMatches.Count(x => x.Outcome == SpaceMatchOutcome.Extra);

                    string mNames = missing > 0
                        ? string.Join(",", report.SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Missing).Select(x => x.Name).OrderBy(x => x))
                        : "-";

                    string line = string.Format("{0,-11} | {1,-10} | {2,-7} | {3,-7} | {4,-5} | {5,-5} | {6}",
                        fm, dg, matched, missing, extra, r.Cells.Count, mNames);
                    results.Add(line);
                }
            }

            output.WriteLine("=== FULL SWEEP 0.1-1.0 (9-space fixture) ===");
            foreach (string line in results)
            {
                output.WriteLine(line);
            }

            // Write to file so we can read it even if xunit truncates.
            string outPath = Path.Combine(AppContext.BaseDirectory, "sweep_0.1_1.0.txt");
            File.WriteAllLines(outPath, results);
            output.WriteLine("Written to: {0}", outPath);

            Assert.True(true);
        }
    }
}
