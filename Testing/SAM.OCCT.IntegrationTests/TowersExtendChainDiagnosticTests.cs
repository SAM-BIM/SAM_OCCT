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
    public class TowersExtendChainDiagnosticTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

        private readonly ITestOutputHelper output;

        public TowersExtendChainDiagnosticTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [SkippableFact]
        public void Extend3D_Towers_FillMargin03vs04_SpaceCount()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            string path = Path.Combine(FixturesDirectory, "whole-level-towers.sam");
            Assert.True(File.Exists(path), "Fixture not found: " + path);

            List<Panel> original = new List<Panel>();
            foreach (var obj in SAM.Core.Convert.ToSAM(path) ?? new List<IJSAMObject>())
            {
                if (obj is AnalyticalModel am) original.AddRange(am.GetPanels() ?? new List<Panel>());
                else if (obj is AdjacencyCluster ac) original.AddRange(ac.GetPanels() ?? new List<Panel>());
                else if (obj is Panel p) original.Add(p);
            }
            Assert.NotEmpty(original);

            output.WriteLine("=== fillMargin=0.3 (discovery result) ===");
            RunChain(original, fillMargin: 0.3, band: 0.4, dir: false);

            output.WriteLine("");
            output.WriteLine("=== fillMargin=0.4 (known working) ===");
            RunChain(original, fillMargin: 0.4, band: 0.4, dir: false);

            output.WriteLine("");
            output.WriteLine("=== fillMargin=0.5 (default) ===");
            RunChain(original, fillMargin: 0.5, band: 0.4, dir: false);
        }

        private void RunChain(List<Panel> original, double fillMargin, double band, bool dir)
        {
            // Exact GH chain: Extend3D(raw) -> CreateAdjacencyCluster
            List<Panel> extended = original.Extend3D(out List<string> diags, out _,
                bucketBetweenLevels: band, fillMargin: fillMargin,
                directionalCapGrow: dir);

            int openEnds = diags.Count(d => d.Contains("SAM_OCCT_EXTEND3D_OPEN_ENDS"));
            output.WriteLine("Open ends line: {0}", diags.FirstOrDefault(d => d.Contains("OPEN_ENDS")) ?? "(none)");
            output.WriteLine("Extended panel count: {0}", extended?.Count ?? 0);

            var nonAir = (extended ?? new List<Panel>())
                .Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null)
                .ToList();

            var cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                null, nonAir, out OcctCellComplexResult result, new SAM.Core.Log(),
                new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 });

            int spaces = cluster?.GetSpaces()?.Count ?? 0;
            int panels = cluster?.GetPanels()?.Count ?? 0;
            output.WriteLine("AdjacencyCluster: {0} spaces, {1} panels, {2} cells", spaces, panels, result.Cells?.Count ?? 0);

            // If spaces are very low, check what happened.
            if (spaces <= 5)
            {
                output.WriteLine("WARNING: Very low space count. Check Extended panel quality:");
                int walls = nonAir.Count(p => {
                    var n = p.GetFace3D()?.GetPlane()?.Normal?.Unit;
                    return n != null && System.Math.Abs(n.Z) <= 0.342;
                });
                int caps = nonAir.Count - walls;
                output.WriteLine("  Walls: {0}, Caps: {1}", walls, caps);
            }
        }
    }
}
