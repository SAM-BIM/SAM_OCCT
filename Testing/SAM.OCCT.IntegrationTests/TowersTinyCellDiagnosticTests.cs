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
    public class TowersTinyCellDiagnosticTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        private readonly ITestOutputHelper output;

        public TowersTinyCellDiagnosticTests(ITestOutputHelper output) { this.output = output; }

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
        public void Towers_BucketAlignSweep_TinyCellElimination()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(panels);

            double band = 0.4;
            double fill = 0.4;
            bool dir = false;

            // First run with defaults to see baseline
            output.WriteLine("=== BASELINE: bucket=0.4 align=0.3 ===");
            var baselineResult = RunChain(panels, band, fill, dir, bucket: 0.4, align: 0.3);
            DumpTinyCells(baselineResult);

            output.WriteLine("");
            output.WriteLine("=== SWEEP: bucket x align ===");
            output.WriteLine("bucket | align | cells | tiny<0.1m3 | tiny<0.5m3 | min_vol");
            output.WriteLine("-------|-------|-------|-----------|-----------|--------");

            foreach (double bk in new[] { 0.4, 0.5, 0.6, 0.7, 0.8, 1.0 })
            {
                foreach (double al in new[] { 0.3, 0.4, 0.5, 0.6 })
                {
                    var result = RunChain(panels, band, fill, dir, bucket: bk, align: al);
                    int tiny01 = result.Cells?.Count(c => c.Volume > 0 && c.Volume < 0.1) ?? 0;
                    int tiny05 = result.Cells?.Count(c => c.Volume > 0 && c.Volume < 0.5) ?? 0;
                    double minVol = result.Cells?.Where(c => c.Volume > 0).Select(c => c.Volume).DefaultIfEmpty(double.NaN).Min() ?? double.NaN;

                    output.WriteLine("{0,6} | {1,5} | {2,5} | {3,9} | {4,9} | {5,8:0.#####}",
                        bk, al, result.CellCount, tiny01, tiny05, minVol);

                    result.Dispose();
                }
            }
        }

        [SkippableFact]
        public void Towers_BucketAlignSweep_DetailedCellList()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(panels);

            double band = 0.4;
            double fill = 0.4;
            bool dir = false;

            foreach (double bk in new[] { 0.3, 0.4, 0.6, 0.8 })
            {
                foreach (double al in new[] { 0.2, 0.3, 0.5, 0.7 })
                {
                    var result = RunChain(panels, band, fill, dir, bucket: bk, align: al);
                    var ordered = result.Cells?.OrderBy(c => c.Volume).ToList() ?? new List<OcctCell>();
                    var tiny = ordered.Where(c => c.Volume > 0 && c.Volume < 0.5).ToList();

                    output.WriteLine("bucket={0} align={1}: {2} cells, {3} tiny (<0.5m3)", bk, al, result.CellCount, tiny.Count);
                    foreach (var c in tiny)
                    {
                        output.WriteLine("  tiny cell vol={0,8:0.######} center=({1:F3}, {2:F3}, {3:F3})",
                            c.Volume, c.Center?.X ?? 0, c.Center?.Y ?? 0, c.Center?.Z ?? 0);
                    }
                    result.Dispose();
                }
            }
        }

        private ChainResult RunChain(List<Panel> original, double band, double fill, bool dir, double bucket, double align)
        {
            var extended = original.Extend3D(out List<string> _,
                minBucketSize: bucket, alignColinearOffset: align,
                bucketBetweenLevels: band, fillMargin: fill, directionalCapGrow: dir);

            var nonAir = (extended ?? new List<Panel>())
                .Where(x => x?.GetFace3D() != null).ToList();

            var opts = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
            var cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                null, nonAir, out OcctCellComplexResult cr, new SAM.Core.Log(), opts);

            var cells = cr?.Cells?.ToList() ?? new List<OcctCell>();
            int spaces = cluster?.GetSpaces()?.Count ?? 0;

            // Don't dispose cr yet - caller will dispose
            return new ChainResult
            {
                CellCount = cells.Count,
                SpaceCount = spaces,
                Cells = cells,
                Result = cr,
                Cluster = cluster
            };
        }

        private void DumpTinyCells(ChainResult result)
        {
            var ordered = result.Cells?.OrderBy(c => c.Volume).ToList() ?? new List<OcctCell>();
            output.WriteLine("Total cells: {0}, spaces: {1}", result.CellCount, result.SpaceCount);
            output.WriteLine("Cells sorted by volume (smallest first):");
            for (int i = 0; i < ordered.Count; i++)
            {
                var c = ordered[i];
                output.WriteLine("  Cell {0,2}: vol={1,10:0.######} center=({2,10:F6}, {3,10:F6}, {4,8:F3})",
                    i, c.Volume, c.Center?.X ?? 0, c.Center?.Y ?? 0, c.Center?.Z ?? 0);
            }
            result.Dispose();
        }

        private class ChainResult
        {
            public int CellCount;
            public int SpaceCount;
            public List<OcctCell> Cells;
            public OcctCellComplexResult Result;
            public AdjacencyCluster Cluster;

            public void Dispose()
            {
                Result?.Dispose();
            }
        }
    }
}
