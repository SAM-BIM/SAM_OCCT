// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SAM.Analytical;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Verifies ParameterDiscoverySolver on the 9-space and whole-level-towers fixtures.
    /// The solver sweeps bucketBetweenLevels × fillMargin × directionalCapGrow, scores each
    /// trial, and documents the best configuration found.
    /// </summary>
    public class ParameterDiscoveryIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

        private readonly ITestOutputHelper output;

        public ParameterDiscoveryIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static List<Panel> LoadPanels(string relativePath)
        {
            return SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, relativePath))
                .OfType<Panel>().ToList();
        }

        [SkippableFact]
        public void ParameterDiscovery_NineSpaceFixture_FindsBestConfig()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> panels = LoadPanels(Path.Combine("ControlledWorkflow", "Panels-9SpacesModel.sam"));
            Assert.NotEmpty(panels);

            List<Face3D> face3Ds = panels.Select(p => p.GetFace3D()).Where(f => f != null).ToList();
            Assert.NotEmpty(face3Ds);

            var discovery = new ParameterDiscoverySolver(face3Ds);
            discovery.Execute(
                new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 },
                sweepBands: new[] { 0.15, 0.21, 0.3, 0.4, 0.5 },
                sweepMargins: new[] { 0.3, 0.5, 0.7, 1.0 });

            output.WriteLine("=== Parameter Discovery Results ===");
            foreach (string diag in discovery.Diagnostics)
            {
                output.WriteLine(diag);
            }

            output.WriteLine("");
            output.WriteLine("=== All Trials ({0}) ===", discovery.Trials.Count);
            foreach (var trial in discovery.Trials.OrderByDescending(t => t.CellCount).ThenBy(t => t.NakedEdgeCount))
            {
                output.WriteLine(trial.ToString());
            }

            Assert.NotNull(discovery.BestTrial);
            output.WriteLine("");
            output.WriteLine("=== Recommended Configuration ===");
            output.WriteLine("bucketBetweenLevels: {0:0.###}", discovery.BestTrial.BucketBetweenLevels);
            output.WriteLine("fillMargin: {0:0.###}", discovery.BestTrial.FillMargin);
            output.WriteLine("directionalCapGrow: {0}", discovery.BestTrial.DirectionalCapGrow);
            output.WriteLine("Result: {0} cells, {1} naked edges, {2:0.#} m3 volume",
                discovery.BestTrial.CellCount, discovery.BestTrial.NakedEdgeCount, discovery.BestTrial.TotalVolume);

            Assert.True(discovery.BestTrial.Adopted,
                "Best trial should have a valid signature.");
            Assert.True(discovery.BestTrial.CellCount > 0,
                "Should produce cells.");
        }

        [SkippableFact]
        public void ParameterDiscovery_TowersFixture_FindsBestConfig()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> panels = LoadPanels("whole-level-towers.sam");
            Assert.NotEmpty(panels);

            List<Face3D> face3Ds = panels.Select(p => p.GetFace3D()).Where(f => f != null).ToList();
            Assert.NotEmpty(face3Ds);

            var discovery = new ParameterDiscoverySolver(face3Ds);
            // Towers needs a wider band sweep due to tilted level frames.
            discovery.Execute(
                new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 },
                sweepBands: new[] { 0.15, 0.21, 0.3, 0.4, 0.5, 0.7 },
                sweepMargins: new[] { 0.3, 0.4, 0.5, 0.7, 1.0 });

            output.WriteLine("=== Parameter Discovery Results ===");
            foreach (string diag in discovery.Diagnostics)
            {
                output.WriteLine(diag);
            }

            output.WriteLine("");
            output.WriteLine("=== All Trials ({0}) ===", discovery.Trials.Count);
            foreach (var trial in discovery.Trials.OrderByDescending(t => t.CellCount).ThenBy(t => t.NakedEdgeCount))
            {
                output.WriteLine(trial.ToString());
            }

            Assert.NotNull(discovery.BestTrial);
            output.WriteLine("");
            output.WriteLine("=== Recommended Configuration ===");
            output.WriteLine("bucketBetweenLevels: {0:0.###}", discovery.BestTrial.BucketBetweenLevels);
            output.WriteLine("fillMargin: {0:0.###}", discovery.BestTrial.FillMargin);
            output.WriteLine("directionalCapGrow: {0}", discovery.BestTrial.DirectionalCapGrow);
            output.WriteLine("Result: {0} cells, {1} naked edges, {2:0.#} m3 volume",
                discovery.BestTrial.CellCount, discovery.BestTrial.NakedEdgeCount, discovery.BestTrial.TotalVolume);

            Assert.True(discovery.BestTrial.Adopted);
            Assert.True(discovery.BestTrial.CellCount > 0);
        }
    }
}
