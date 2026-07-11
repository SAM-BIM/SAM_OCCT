// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SAM.Analytical;
using SAM.Analytical.OCCT;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    public class TowersMissingSpaceDiagnosticTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        private readonly ITestOutputHelper output;

        public TowersMissingSpaceDiagnosticTests(ITestOutputHelper output) { this.output = output; }

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
        public void Extend3D_Towers_MinBucketSizeSweep_SpaceCount()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(panels);

            double band = 0.4;
            double fill = 0.4;
            bool dir = false;

            var lines = new List<string>();
            lines.Add(string.Format("fixture: whole-level-towers.sam  band={0:0.###}  fill={1:0.###}  dir={2}", band, fill, dir));
            lines.Add("");
            lines.Add("bucket | spaces | cells | panels | shared | single | adjacencies | open_ends | gap_slits");
            lines.Add("-------|--------|-------|--------|--------|--------|-------------|-----------|----------");

            foreach (double bucket in new[] { 0.4, 0.5, 0.6, 0.7, 0.8, 1.0 })
            {
                var extended = panels.Extend3D(out List<string> diags, out _,
                    minBucketSize: bucket, bucketBetweenLevels: band,
                    fillMargin: fill, directionalCapGrow: dir);

                int openEnds = ParseFirstInteger(diags, "OPEN_ENDS");
                int gapSlits = diags.Count(d => d.Contains("SAM_OCCT_EXTEND3D_SLITS"));

                var nonAir = (extended ?? new List<Panel>())
                    .Where(x => x?.GetFace3D() != null).ToList();

                var opts = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
                var cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                    null, nonAir, out OcctCellComplexResult cr, new SAM.Core.Log(), opts);

                var merged = cluster?.MergeCoplanarPanels(out List<string> mergeDiags, Tolerance.Distance, Tolerance.Angle);

                int spaces = merged?.GetSpaces()?.Count ?? 0;
                int panelCount = merged?.GetPanels()?.Count ?? 0;
                int cells = cr?.Cells?.Count ?? 0;
                int shared = 0, single = 0;
                foreach (var p in merged?.GetPanels() ?? new List<Panel>())
                {
                    int n = (merged?.GetSpaces(p)?.Count ?? 0);
                    if (n >= 2) shared++; else if (n == 1) single++;
                }
                int adj = cr?.FaceAdjacencies?.Count ?? 0;
                cr?.Dispose();

                string status = spaces >= 32 ? " OK" : spaces < 31 ? " LOW" : " WARN";
                lines.Add(string.Format("{0,6} | {1,6} | {2,5} | {3,6} | {4,6} | {5,6} | {6,11} | {7,9} | {8,8}{9}",
                    bucket, spaces, cells, panelCount, shared, single, adj, openEnds, gapSlits, status));
            }

            string outPath = Path.Combine(AppContext.BaseDirectory, "towers_bucket_sweep.txt");
            File.WriteAllLines(outPath, lines);
            output.WriteLine("Written to: {0}", outPath);
            foreach (string line in lines) output.WriteLine(line);
        }

        [SkippableFact]
        public void Extend3D_Towers_FullChain_DiagnosticsDump()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(panels);

            double band = 0.4;
            double fill = 0.4;
            bool dir = false;
            double bucket = 0.6;

            output.WriteLine("=== Full production-chain diagnostics ===");
            output.WriteLine("chain: Extend3D(minBucket={0}) -> CreateAdjacencyCluster -> MergeCoplanarAdjacencyCluster", bucket);
            output.WriteLine("band={0} fill={1} dirGrow={2}", band, fill, dir);
            output.WriteLine("");

            // Extend3D diagnostics
            var extended = panels.Extend3D(out List<string> extendDiags, out _,
                minBucketSize: bucket, bucketBetweenLevels: band,
                fillMargin: fill, directionalCapGrow: dir);

            output.WriteLine("--- Extend3D Diagnostics ---");
            foreach (string d in extendDiags) output.WriteLine("  {0}", d);

            // Open wall ends
            var openPanels = panels.OpenPanels3D(out List<Point3D> openEnds, out List<string> openDiags,
                minBucketSize: bucket, bucketBetweenLevels: band,
                directionalCapGrow: dir);
            output.WriteLine("");
            output.WriteLine("--- Open Wall Ends ---");
            output.WriteLine("  Open end count: {0}", openEnds?.Count ?? 0);
            output.WriteLine("  Open panels: {0}", openPanels?.Count ?? 0);
            foreach (string d in openDiags)
            {
                if (d.Contains("OPENPANELS3D_RESULT") || d.Contains("OPEN_ENDS"))
                    output.WriteLine("  {0}", d);
            }

            // CreateAdjacencyCluster
            var nonAir = (extended ?? new List<Panel>())
                .Where(x => x?.GetFace3D() != null).ToList();
            var opts = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
            var cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                null, nonAir, out OcctCellComplexResult cr, new SAM.Core.Log(), opts);

            output.WriteLine("");
            output.WriteLine("--- CreateAdjacencyCluster ---");
            output.WriteLine("  Spaces: {0}", cluster?.GetSpaces()?.Count ?? 0);
            output.WriteLine("  Panels: {0}", cluster?.GetPanels()?.Count ?? 0);
            output.WriteLine("  Cells:  {0}", cr?.Cells?.Count ?? 0);
            if (cr?.Diagnostics != null)
                foreach (var d in cr.Diagnostics)
                    output.WriteLine("  {0}", d.ToString());

            // Space-by-space breakdown
            output.WriteLine("");
            output.WriteLine("--- Space Breakdown ---");
            foreach (var space in (cluster?.GetSpaces() ?? new List<Space>()).OrderBy(s => s?.Name ?? ""))
            {
                var spacePanels = cluster.GetPanels(space) ?? new List<Panel>();
                output.WriteLine("  {0}: {1} panels", space?.Name ?? "(unnamed)", spacePanels.Count);
            }

            // MergeCoplanarAdjacencyCluster
            var merged = cluster.MergeCoplanarPanels(out List<string> mergeDiags, Tolerance.Distance, Tolerance.Angle);

            output.WriteLine("");
            output.WriteLine("--- MergeCoplanarAdjacencyCluster ---");
            output.WriteLine("  Spaces: {0}", merged?.GetSpaces()?.Count ?? 0);
            output.WriteLine("  Panels: {0}", merged?.GetPanels()?.Count ?? 0);
            foreach (string d in mergeDiags) output.WriteLine("  {0}", d);

            // Shared walls / adjacencies
            output.WriteLine("");
            output.WriteLine("--- Adjacency Summary ---");
            int shared = 0, single = 0;
            foreach (var p in merged?.GetPanels() ?? new List<Panel>())
            {
                int n = (merged?.GetSpaces(p)?.Count ?? 0);
                if (n >= 2) shared++; else if (n == 1) single++;
            }
            output.WriteLine("  Shared walls: {0}", shared);
            output.WriteLine("  Single walls: {0}", single);
            output.WriteLine("  Adjacencies:  {0}", cr?.FaceAdjacencies?.Count ?? 0);
            cr?.Dispose();

            // MergeCoplanar space breakdown
            output.WriteLine("");
            output.WriteLine("--- Merged Space Breakdown ---");
            foreach (var space in (merged?.GetSpaces() ?? new List<Space>()).OrderBy(s => s?.Name ?? ""))
            {
                var spacePanels = merged.GetPanels(space) ?? new List<Panel>();
                output.WriteLine("  {0}: {1} panels", space?.Name ?? "(unnamed)", spacePanels.Count);
            }
        }

        private static int ParseFirstInteger(List<string> lines, string contains)
        {
            var line = lines?.FirstOrDefault(d => d.Contains(contains));
            if (line == null) return 0;
            var parts = line.Split(' ');
            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out int n)) return n;
            }
            return 0;
        }
    }
}
