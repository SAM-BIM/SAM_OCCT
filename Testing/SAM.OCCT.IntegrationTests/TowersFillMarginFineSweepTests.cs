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
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    public class TowersFillMarginFineSweepTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        private readonly ITestOutputHelper output;

        public TowersFillMarginFineSweepTests(ITestOutputHelper output) { this.output = output; }

        [SkippableFact]
        public void Towers_Band04_FineFillSweep_CountSpaces()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = new List<Panel>();
            foreach (var o in SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "whole-level-towers.sam")) ?? new List<IJSAMObject>())
            {
                if (o is AnalyticalModel am) panels.AddRange(am.GetPanels() ?? new List<Panel>());
                else if (o is AdjacencyCluster ac) panels.AddRange(ac.GetPanels() ?? new List<Panel>());
                else if (o is Panel p) panels.Add(p);
            }

            var lines = new List<string>();
            lines.Add("fill | dir   | spaces | cells | shared | adjacencies");
            lines.Add("-----|-------|--------|-------|--------|------------");

            foreach (double fill in new[] { 0.3, 0.35, 0.4, 0.45, 0.5, 0.55, 0.6 })
            {
                foreach (bool dir in new[] { false, true })
                {
                    var ext = panels.Extend3D(out _, out _,
                        bucketBetweenLevels: 0.4, fillMargin: fill, directionalCapGrow: dir);
                    var nonAir = ext.Where(x => x?.GetFace3D() != null).ToList();

                    var opts = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
                    var cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                        null, nonAir, out OcctCellComplexResult cr, new SAM.Core.Log(), opts);

                    int spaces = cluster?.GetSpaces()?.Count ?? 0;
                    int cells = cr?.Cells?.Count ?? 0;
                    int shared = cluster?.GetPanels()?.Count(p => (cluster?.GetSpaces(p)?.Count ?? 0) >= 2) ?? 0;
                    int adj = cr?.FaceAdjacencies?.Count ?? 0;
                    cr?.Dispose();

                    string warn = spaces != 31 ? (spaces < 31 ? " LOW" : " HIGH") : " OK";
                    lines.Add(string.Format("{0,4} | {1,-5} | {2,6} | {3,5} | {4,6} | {5,11}{6}",
                        fill, dir, spaces, cells, shared, adj, warn));
                }
            }

            string outPath = Path.Combine(AppContext.BaseDirectory, "towers_fill_sweep.txt");
            File.WriteAllLines(outPath, lines);
            output.WriteLine("Written to: {0}", outPath);
            foreach (string line in lines) output.WriteLine(line);
        }
    }
}
