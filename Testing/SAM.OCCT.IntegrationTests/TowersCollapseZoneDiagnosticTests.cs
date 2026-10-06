// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
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
    public class TowersCollapseZoneDiagnosticTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        private readonly ITestOutputHelper output;

        public TowersCollapseZoneDiagnosticTests(ITestOutputHelper output) { this.output = output; }

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
        public void Towers_CollapseZone_Diagnostics_PanelCountChanges()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(panels);

            output.WriteLine("align | ext_panels | walls | caps | cells");
            output.WriteLine("------|------------|-------|------|------");

            foreach (double al in new[] { 0.2, 0.3, 0.35, 0.38, 0.4, 0.42, 0.45, 0.5 })
            {
                var extended = panels.Extend3D(out List<string> diags,
                    minBucketSize: 0.4, alignColinearOffset: al,
                    bucketBetweenLevels: 0.4, fillMargin: 0.4, directionalCapGrow: false);

                int walls = (extended ?? new List<Panel>()).Count(x => x?.PanelType == PanelType.Wall);
                int caps = (extended ?? new List<Panel>()).Count(x => x?.PanelType != PanelType.Wall);
                int total = extended?.Count ?? 0;

                var nonAir = (extended ?? new List<Panel>()).Where(x => x?.GetFace3D() != null).ToList();
                var opts = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
                var cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                    null, nonAir, out OcctCellComplexResult cr, new SAM.Core.Log(), opts);
                int cells = cr?.Cells?.Count ?? 0;
                cr?.Dispose();

                // Count merged walls from clean diagnostics
                int snapped = diags.Count(d => d.Contains("snapped-to-backer"));
                int coplanar = diags.Count(d => d.Contains("coplanar-merged"));
                int opposed = diags.Count(d => d.Contains("opposed-collapsed"));

                output.WriteLine("{0,5} | {1,10} | {2,5} | {3,4} | {4,5}  (snap={5} coplanar={6} opposed={7})",
                    al, total, walls, caps, cells, snapped, coplanar, opposed);
            }
        }

        [SkippableFact]
        public void Towers_OptimalConfig_VerifyCells()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(panels);

            double band = 0.4;
            double fill = 0.4;
            double bucket = 0.5;
            double align = 0.45;
            bool dir = false;

            var extended = panels.Extend3D(out List<string> diags,
                minBucketSize: bucket, alignColinearOffset: align,
                bucketBetweenLevels: band, fillMargin: fill, directionalCapGrow: dir);

            int snapped = diags.Count(d => d.Contains("snapped-to-backer"));
            int coplanar = diags.Count(d => d.Contains("coplanar-merged"));
            int opposed = diags.Count(d => d.Contains("opposed-collapsed"));

            var nonAir = (extended ?? new List<Panel>()).Where(x => x?.GetFace3D() != null).ToList();
            var opts = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
            var cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                null, nonAir, out OcctCellComplexResult cr, new SAM.Core.Log(), opts);

            var ordered = (cr?.Cells?.ToList() ?? new List<OcctCell>()).OrderBy(c => c.Volume).ToList();

            output.WriteLine("=== Optimal: bucket={0} align={1} band={2} fill={3} dir={4} ===", bucket, align, band, fill, dir);
            output.WriteLine("Extended panels: {0}, merged: snap={1} coplanar={2} opposed={3}",
                extended?.Count ?? 0, snapped, coplanar, opposed);
            output.WriteLine("Cells: {0}, min vol: {1:0.##} m3", ordered.Count,
                ordered.Where(c => c.Volume > 0).Select(c => c.Volume).DefaultIfEmpty(double.NaN).Min());

            int tinyCount = ordered.Count(c => c.Volume > 0 && c.Volume < 1.0);
            Assert.Equal(0, tinyCount);

            // Cell 22 (1.76, -23.39) should be normal size
            double cell22vol = ordered
                .Where(c => System.Math.Abs((c.Center?.X ?? 0) - 1.76) < 1 &&
                           System.Math.Abs((c.Center?.Y ?? 0) + 23.39) < 2)
                .Select(c => c.Volume).FirstOrDefault();
            output.WriteLine("Cell 22 (1.76, -23.39) volume: {0:0.##} m3", cell22vol);

            // Cell 26 (-4.14, -13, 13.76) should still exist
            double cell26vol = ordered
                .Where(c => System.Math.Abs((c.Center?.X ?? 0) + 4.14) < 1 &&
                           System.Math.Abs((c.Center?.Y ?? 0) + 13) < 2 &&
                           System.Math.Abs((c.Center?.Z ?? 0) - 13.76) < 2)
                .Select(c => c.Volume).FirstOrDefault();
            output.WriteLine("Cell 26 (-4.14, -13) volume: {0:0.##} m3", cell26vol);

            cr?.Dispose();

            // Confirm at least 30 cells and no sub-1m3 cells
            Assert.True(ordered.Count >= 30, "Should have at least 30 cells.");
        }
    }
}
