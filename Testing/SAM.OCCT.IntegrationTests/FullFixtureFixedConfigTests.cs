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
    /// Verifies the full 9-space fixture with the winning config (fillMargin=1.0, directionalCapGrow=false).
    /// </summary>
    public class FullFixtureFixedConfigTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ControlledWorkflow");
        private static readonly Guid West3Guid = new Guid("02a1ae27-5461-4b41-ad07-008ccd9d1159");

        private readonly ITestOutputHelper output;

        public FullFixtureFixedConfigTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [SkippableFact]
        public void FullFixture_FillMargin1_DirectionalCapGrowFalse_AllNineMatch()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> panels = SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Panels-9SpacesModel.sam")).OfType<Panel>().ToList();
            List<Space> spaces = SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Spaces-9SpacesModel.sam")).OfType<Space>().ToList();

            List<Panel> cleaned = panels.Clean3D(out _, out _, bucketBetweenLevels: 0.21);
            List<Panel> extended = cleaned.Extend3D(out _, out _,
                bucketBetweenLevels: 0.21, inputAlreadyClean: true,
                fillMargin: 1.0, directionalCapGrow: false);

            SpaceMatchOptions opts = new SpaceMatchOptions { LevelBand = 0.21, LevelGroupBand = 0.21 };
            ExpectedSpaceSet ess = ExpectedSpaceSet.Create(spaces, cleaned, opts, new[] { West3Guid });

            AdjacencyCluster cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                ess.ToSeedSpaces(), extended, out OcctCellComplexResult result, new SAM.Core.Log(),
                new SAM.Core.OCCT.OcctBuildOptions { Tolerance = SAM.Core.Tolerance.Distance, FuzzyTolerance = SAM.Core.Tolerance.MacroDistance, AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 });

            List<CellGeometry> cells = CellGeometry.FromComplex(result);
            SpaceMatchReport report = SpaceMatcher.Match(ess, cells, new[] { West3Guid }, cluster, panels);

            foreach (string line in report.ToLines()) output.WriteLine(line);

            int matched = report.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Matched);
            int missing = report.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Missing);
            int extra = report.CellMatches.Count(x => x.Outcome == SpaceMatchOutcome.Extra);
            int merged = report.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Merged);

            output.WriteLine("Matched: {0}, Missing: {1}, Extra: {2}, Merged: {3}", matched, missing, extra, merged);

            Assert.Equal(9, matched);
            Assert.Empty(report.SpaceMatches.Where(x => x.Outcome == SpaceMatchOutcome.Missing));
            Assert.True(report.DoubleHeightOk.TryGetValue(West3Guid, out bool west3Ok) && west3Ok, "West3 double-height must be preserved.");
        }
    }
}
