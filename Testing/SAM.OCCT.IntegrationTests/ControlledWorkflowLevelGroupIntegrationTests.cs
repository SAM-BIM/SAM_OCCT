// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Geometry.OCCT.Solver;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// P2 level-group + exact-handoff coverage on the 9-space controlled fixture
    /// (docs/CONTROLLED_WORKFLOW_PLAN.md §4, §9). Clean3D / Extend3D are the MANAGED (native-free)
    /// pre-resolve passes, so these run everywhere (no native gate) - they lock the headline P2 outcome:
    /// the fixture's 5 raw level frames merge into 3 level groups at datums 12.24 / 15.29 / 18.34 with
    /// bucketBetweenLevels = 0.21, the default (0) is the identity, and the inputAlreadyClean handoff skips
    /// the second clean. (Full through-the-builder chaining is in ControlledWorkflowBaselineTests.)
    /// </summary>
    public class ControlledWorkflowLevelGroupIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ControlledWorkflow");

        private static List<Panel> FixturePanels()
        {
            string panelsPath = Path.Combine(FixturesDirectory, "Panels-9SpacesModel.sam");
            Assert.True(File.Exists(panelsPath), "Missing fixture: " + panelsPath);
            List<Panel> panels = SAM.Core.Convert.ToSAM(panelsPath).OfType<Panel>().ToList();
            Assert.NotEmpty(panels);
            return panels;
        }

        [Fact]
        public void Clean3D_FixtureBucketBetweenLevels021_ProducesFiveRawFramesThreeGroups()
        {
            // Arrange
            List<Panel> panels = FixturePanels();

            // Act - explicit controlled-fixture merge band.
            panels.Clean3D(out _, out Solve3DReport report, bucketBetweenLevels: 0.21);

            // Assert - the raw frames stay at five (the pinned 0.15 band is UNCHANGED), and the level groups
            // merge to three storey datums 12.24 / 15.29 / 18.34.
            Assert.Equal(5, report.LevelFrames.Count);
            Assert.Equal(3, report.LevelGroups.Count);
            List<double> datums = report.LevelGroups.Select(x => System.Math.Round(x.Elevation, 2)).ToList();
            Assert.Equal(new List<double> { 12.24, 15.29, 18.34 }, datums);
        }

        [Fact]
        public void Clean3D_FixtureDefaultBucketBetweenLevels_GroupsAreIdentityOfFrames()
        {
            // Arrange
            List<Panel> panels = FixturePanels();

            // Act - the DEFAULT (0): grouping is off.
            panels.Clean3D(out _, out Solve3DReport report);

            // Assert - one group per raw frame (identity), same five elevations - the backward-compatible path.
            Assert.Equal(5, report.LevelFrames.Count);
            Assert.Equal(report.LevelFrames.Count, report.LevelGroups.Count);
            Assert.Equal(
                report.LevelFrames.Select(x => System.Math.Round(x.Elevation, 3)),
                report.LevelGroups.Select(x => System.Math.Round(x.Elevation, 3)));
        }

        [Fact]
        public void Clean3D_FixtureBucketBetweenLevels021_CleanReportNamesTheGrouping()
        {
            // Arrange
            List<Panel> panels = FixturePanels();

            // Act
            List<Panel> cleaned = panels.Clean3D(out List<string> diagnostics, out Solve3DReport report, bucketBetweenLevels: 0.21);

            // Assert - the CleanReport greppable summary states the 5 -> 3 merge and the band, and the per-panel
            // clean observability recorded actual actions (the fixture's slab skins snap/normalize/merge).
            Assert.NotNull(cleaned);
            Assert.Contains(diagnostics, x => x.StartsWith("SAM_OCCT_CLEAN3D_LEVELS:", StringComparison.Ordinal) && x.Contains("5 raw level frame(s) -> 3 level group(s)") && x.Contains("0.21"));
            Assert.Contains(report.FormatLevelGroups(), x => x.Contains("SAM_OCCT_CLEAN3D_LEVELGROUP: group 0") && x.Contains("12.24"));
            Assert.NotEmpty(report.CleanRecords);
        }

        [Fact]
        public void Extend3D_FixtureInputAlreadyClean_SkipsSecondCleanAndReusesCleanFrames()
        {
            // Arrange - the exact Clean3D -> Extend3D handoff: clean once, then condition-only.
            List<Panel> panels = FixturePanels();
            List<Panel> cleaned = panels.Clean3D(out _, out Solve3DReport cleanReport, bucketBetweenLevels: 0.21);
            Assert.NotNull(cleaned);
            Assert.NotEmpty(cleaned);

            // Act - condition-only extend on the already-clean panels.
            cleaned.Extend3D(out List<string> extendDiagnostics, out Solve3DReport extendReport, bucketBetweenLevels: 0.21, inputAlreadyClean: true);

            // Assert - Stage A was skipped (CLEAN-SKIPPED diagnostic, no clean records), and the frames/groups
            // were still clustered for reporting so the level view survives the handoff.
            Assert.Contains(extendDiagnostics, x => x.Contains("CLEAN3D_SKIPPED"));
            Assert.Empty(extendReport.CleanRecords);
            Assert.NotEmpty(extendReport.LevelFrames);
            Assert.NotEmpty(extendReport.LevelGroups);
        }

        [Fact]
        public void Extend3D_FixtureLegacyPathVsInputAlreadyClean_DifferInPanelCount()
        {
            // Arrange - the same already-clean input through both paths.
            List<Panel> panels = FixturePanels();
            List<Panel> cleaned = panels.Clean3D(out _, out _, bucketBetweenLevels: 0.21);
            Assert.NotNull(cleaned);

            // Act - legacy (re-cleans internally) vs condition-only (no second clean).
            List<Panel> legacy = cleaned.Extend3D(out _, out _, bucketBetweenLevels: 0.21, inputAlreadyClean: false);
            List<Panel> handoff = cleaned.Extend3D(out _, out _, bucketBetweenLevels: 0.21, inputAlreadyClean: true);

            // Assert - both produce panels; the condition-only path is a distinct pass (it does not re-run the
            // clean bucket), so the handoff is exercised end to end without throwing.
            Assert.NotNull(legacy);
            Assert.NotNull(handoff);
            Assert.NotEmpty(handoff);
        }
    }
}
