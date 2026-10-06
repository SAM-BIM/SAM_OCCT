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
using SAM.Geometry.Spatial;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    public class MultiFixtureAdjacencyDiagnosticTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        private readonly ITestOutputHelper output;

        public MultiFixtureAdjacencyDiagnosticTests(ITestOutputHelper output) { this.output = output; }

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
        public void Extend3D_AllFixtures_AdjacencySharedWallReport()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var fixtures = new (string path, string label, double band, double fill, bool dir)[]
            {
                ("whole-level-flat.sam",          "flat",              0.21, 0.5, true),
                ("whole-level-tilted.sam",        "tilted",            0.21, 0.5, true),
                ("whole-level-towers.sam",        "towers",            0.4,  0.4, false),
                ("two-level-tilted.sam",          "two-level-tilted",  0.21, 0.5, true),
                ("three-spaces.sam",              "three-spaces",      0.21, 0.5, true),
                ("tilted-two-spaces.sam",         "tilted-two",        0.21, 0.5, true),
            };

            output.WriteLine("fixture          | band | fill | dir   | spaces | panels | shared | single | adjacencies | parity");
            output.WriteLine("-----------------|------|------|-------|--------|--------|--------|--------|-------------|-------");

            foreach (var (path, label, band, fill, dir) in fixtures)
            {
                string fullPath = Path.Combine(FixturesDirectory, path);
                if (!File.Exists(fullPath)) { output.WriteLine("{0,-17} | SKIP (not found)", label); continue; }

                var panels = LoadPanels(fullPath);
                if (panels.Count < 4) { output.WriteLine("{0,-17} | SKIP ({1} panels)", label, panels.Count); continue; }

                var extended = panels.Extend3D(out _, out _,
                    bucketBetweenLevels: band, fillMargin: fill, directionalCapGrow: dir);
                var nonAir = (extended ?? new List<Panel>())
                    .Where(x => x?.GetFace3D() != null).ToList();

                var opts = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
                var cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                    null, nonAir, out OcctCellComplexResult cr, new SAM.Core.Log(), opts);

                int spaces = cluster?.GetSpaces()?.Count ?? 0;
                int panelCount = cluster?.GetPanels()?.Count ?? 0;
                int shared = 0, single = 0;
                foreach (var p in cluster?.GetPanels() ?? new List<Panel>())
                {
                    int n = (cluster?.GetSpaces(p)?.Count ?? 0);
                    if (n >= 2) shared++; else if (n == 1) single++;
                }
                int adj = cr?.FaceAdjacencies?.Count ?? 0;
                var parity = cr?.Diagnostics?.FirstOrDefault(d => d.Code == "SAM_OCCT_ANALYTICAL_PARITY");
                string parityStr = parity == null ? "none" : (parity.Severity == OcctDiagnosticSeverity.Info ? "clean" : "WARN");

                output.WriteLine("{0,-17} | {1,4} | {2,4} | {3,-5} | {4,6} | {5,6} | {6,6} | {7,6} | {8,11} | {9}",
                    label, band, fill, dir, spaces, panelCount, shared, single, adj, parityStr);
                cr?.Dispose();
            }
        }

        /// <summary>
        /// doubleWallGap regression sweep: every fixture run with the lever OFF (0, must equal today's
        /// behaviour) and at the towers-validated 0.4, so a cross-fixture surprise (a real corridor/shaft
        /// swallowed, a collapse) is visible fixture-by-fixture rather than discovered in the field.
        /// </summary>
        [SkippableFact]
        public void Extend3D_AllFixtures_DoubleWallGapComparison()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var fixtures = new (string path, string label, double band, double fill, bool dir)[]
            {
                ("whole-level-flat.sam",          "flat",              0.21, 0.5, true),
                ("whole-level-tilted.sam",        "tilted",            0.21, 0.5, true),
                ("whole-level-towers.sam",        "towers",            0.4,  0.4, false),
                ("two-level-tilted.sam",          "two-level-tilted",  0.21, 0.5, true),
                ("three-spaces.sam",              "three-spaces",      0.21, 0.5, true),
                ("tilted-two-spaces.sam",         "tilted-two",        0.21, 0.5, true),
                ("Face3D-home.sam",               "face3d-home",       0.21, 0.5, true),
                ("Revit-home-panels.sam",         "revit-home",        0.21, 0.5, true),
                ("AdjacencyCluster-home.sam",     "cluster-home",      0.21, 0.5, true),
            };

            output.WriteLine("fixture          | gap=0: cells/spaces/shared | gap=0.4: cells/spaces/shared | delta");
            output.WriteLine("-----------------|----------------------------|------------------------------|------");

            foreach (var (path, label, band, fill, dir) in fixtures)
            {
                string fullPath = Path.Combine(FixturesDirectory, path);
                if (!File.Exists(fullPath)) { output.WriteLine("{0,-17} | SKIP (not found)", label); continue; }

                var panels = LoadPanels(fullPath);
                if (panels.Count < 4) { output.WriteLine("{0,-17} | SKIP ({1} panels)", label, panels.Count); continue; }

                var off = RunChainCounts(panels, band, fill, dir, doubleWallGap: 0.0);
                var on = RunChainCounts(panels, band, fill, dir, doubleWallGap: 0.4);

                output.WriteLine("{0,-17} | {1,5}/{2,5}/{3,6}         | {4,5}/{5,5}/{6,6}           | {7}",
                    label, off.cells, off.spaces, off.shared, on.cells, on.spaces, on.shared,
                    on.cells == off.cells ? "none" : string.Format("{0:+#;-#;0} cells", on.cells - off.cells));
            }
        }

        private static (int cells, int spaces, int shared) RunChainCounts(List<Panel> panels, double band, double fill, bool dir, double doubleWallGap)
        {
            var extended = panels.Extend3D(out List<string> _,
                bucketBetweenLevels: band, fillMargin: fill, directionalCapGrow: dir,
                doubleWallGap: doubleWallGap);
            var nonAir = (extended ?? new List<Panel>()).Where(x => x?.GetFace3D() != null).ToList();

            var opts = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
            var cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                null, nonAir, out OcctCellComplexResult cr, new SAM.Core.Log(), opts);

            int cells = cr?.Cells?.Count ?? 0;
            int spaces = cluster?.GetSpaces()?.Count ?? 0;
            int shared = 0;
            foreach (var p in cluster?.GetPanels() ?? new List<Panel>())
            {
                if ((cluster?.GetSpaces(p)?.Count ?? 0) >= 2) shared++;
            }
            cr?.Dispose();
            return (cells, spaces, shared);
        }
    }
}
