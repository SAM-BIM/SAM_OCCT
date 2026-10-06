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

namespace SAM.OCCT.IntegrationTests
{
    public class TowersAlignFineSweepTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        private readonly ITestOutputHelper output;

        public TowersAlignFineSweepTests(ITestOutputHelper output) { this.output = output; }

        private static List<Panel> LoadPanels(string path)
        {
            var r = new List<Panel>();
            foreach (var o in SAM.Core.Convert.ToSAM(path) ?? new List<IJSAMObject>())
            {
                if (o is AnalyticalModel am) r.AddRange(am.GetPanels() ?? new List<Panel>());
                else if (o is AdjacencyCluster ac) r.AddRange(ac.GetPanels() ?? new List<Panel>());
                else if (o is Panel p) r.Add(p);
            }
            return r;
        }

        [SkippableFact]
        public void Towers_FineAlignSweep_WithBucket_MapSmallCells()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(panels);

            double band = 0.4;
            double fill = 0.4;
            bool dir = false;

            var lines = new List<string>();
            lines.Add("bucket | align | cells | tiny<1 | tiny<3 | vol@(6.76,-23.31) | vol@(6.76,-23.21) | vol@(1.76,-23.39)");
            lines.Add("-------|-------|-------|--------|--------|------------------|------------------|------------------");

            foreach (double bk in new[] { 0.3, 0.4, 0.5, 0.6 })
            {
                foreach (double al in new[] { 0.15, 0.2, 0.25, 0.3, 0.35, 0.38, 0.4, 0.42, 0.45, 0.5, 0.55 })
                {
                    var extended = panels.Extend3D(out List<string> _,
                        minBucketSize: bk, alignColinearOffset: al,
                        bucketBetweenLevels: band, fillMargin: fill, directionalCapGrow: dir);

                    var nonAir = (extended ?? new List<Panel>())
                        .Where(x => x?.GetFace3D() != null).ToList();

                    var opts = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
                    var cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                        null, nonAir, out OcctCellComplexResult cr, new SAM.Core.Log(), opts);

                    int cells = cr?.Cells?.Count ?? 0;
                    int tiny1 = cr?.Cells?.Count(c => c.Volume > 0 && c.Volume < 1.0) ?? 0;
                    int tiny3 = cr?.Cells?.Count(c => c.Volume > 0 && c.Volume < 3.0) ?? 0;

                    // Find volume of cells near the user's identified trouble spots
                    double v1 = FindCellVolume(cr?.Cells, 6.76, -23.31, 13.76, 2.0);
                    double v2 = FindCellVolume(cr?.Cells, 6.76, -23.21, 13.76, 2.0);
                    double v3 = FindCellVolume(cr?.Cells, 1.76, -23.39, 13.76, 2.0);

                    string tag = cells == 2 ? " COLLAPSE" : tiny1 > 0 ? " TINY<1" : tiny3 > 0 ? " TINY<3" : "";

                    lines.Add(string.Format("{0,6} | {1,5} | {2,5} | {3,6} | {4,6} | {5,16:F6} | {6,16:F6} | {7,16:F6}{8}",
                        bk, al, cells, tiny1, tiny3, v1, v2, v3, tag));

                    cr?.Dispose();
                }
            }

            string outPath = Path.Combine(AppContext.BaseDirectory, "towers_align_fine_sweep.txt");
            File.WriteAllLines(outPath, lines);
            output.WriteLine("Written to: {0}", outPath);
            foreach (string line in lines) output.WriteLine(line);
        }

        [SkippableFact]
        public void Towers_Align035_Bucket05_DumpCells()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(panels);

            double band = 0.4;
            double fill = 0.4;
            bool dir = false;
            double bk = 0.5;
            double al = 0.35;

            var extended = panels.Extend3D(out List<string> _,
                minBucketSize: bk, alignColinearOffset: al,
                bucketBetweenLevels: band, fillMargin: fill, directionalCapGrow: dir);

            var nonAir = (extended ?? new List<Panel>()).Where(x => x?.GetFace3D() != null).ToList();
            var opts = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
            var cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                null, nonAir, out OcctCellComplexResult cr, new SAM.Core.Log(), opts);

            var ordered = (cr?.Cells?.ToList() ?? new List<OcctCell>()).OrderBy(c => c.Volume).ToList();
            output.WriteLine("bucket={0} align={1}: {2} cells", bk, al, ordered.Count);
            foreach (var c in ordered)
            {
                output.WriteLine("  vol={0,10:0.######} center=({1,10:F6}, {2,10:F6}, {3,8:F3})",
                    c.Volume, c.Center?.X ?? 0, c.Center?.Y ?? 0, c.Center?.Z ?? 0);
            }

            // Find cells near the tiny spots
            output.WriteLine("");
            output.WriteLine("Cells near (6.76, -23.3):");
            var nearby = ordered.Where(c =>
                System.Math.Abs((c.Center?.X ?? 0) - 6.76) < 1.0 &&
                System.Math.Abs((c.Center?.Y ?? 0) + 23.3) < 5.0).ToList();
            foreach (var c in nearby)
                output.WriteLine("  vol={0,10:0.######} center=({1,10:F6}, {2,10:F6}, {3,8:F3})",
                    c.Volume, c.Center?.X ?? 0, c.Center?.Y ?? 0, c.Center?.Z ?? 0);

            cr?.Dispose();
        }

        private static double FindCellVolume(IReadOnlyList<OcctCell> cells, double cx, double cy, double cz, double radius)
        {
            foreach (var c in cells ?? new List<OcctCell>())
            {
                var cc = c.Center;
                if (cc == null) continue;
                double dx = cc.X - cx;
                double dy = cc.Y - cy;
                double dz = cc.Z - cz;
                if (dx * dx + dy * dy + dz * dz < radius * radius)
                    return c.Volume;
            }
            return double.NaN;
        }
    }
}
