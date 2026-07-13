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

using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// PR #61 review metrics harness — report-only diagnostic measurements.
    /// Never asserts pipeline success. Temporary; retained only if it proves a
    /// valuable permanent regression assertion.
    /// </summary>
    public class PR61ReviewMetricsHarness
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        private static readonly string CwDirectory = Path.Combine(FixturesDirectory, "ControlledWorkflow");
        private static readonly Guid West3Guid = new Guid("02a1ae27-5461-4b41-ad07-008ccd9d1159");
        private readonly ITestOutputHelper output;

        public PR61ReviewMetricsHarness(ITestOutputHelper output) { this.output = output; }

        private static List<Panel> LoadPanels(string path)
        {
            var objects = SAM.Core.Convert.ToSAM(path);
            var result = new List<Panel>();
            foreach (var obj in objects ?? new List<IJSAMObject>())
            {
                if (obj is AnalyticalModel m) result.AddRange(m.GetPanels() ?? new List<Panel>());
                else if (obj is AdjacencyCluster c) result.AddRange(c.GetPanels() ?? new List<Panel>());
                else if (obj is Panel p) result.Add(p);
            }
            return result.Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid()).ToList();
        }

        private static List<Space> LoadSpaces(string path) => SAM.Core.Convert.ToSAM(path).OfType<Space>().ToList();

        private static OcctBuildOptions CwOptions() => new OcctBuildOptions
        {
            Tolerance = Tolerance.Distance,
            FuzzyTolerance = Tolerance.MacroDistance,
            AvoidInternalShapes = false,
            SewBeforeBuild = true,
            SewingTolerance = 0.01,
            MergeCoplanarBeforeBuild = true
        };

        private static OcctBuildOptions CwOptionsNoMerge()
        {
            var o = CwOptions();
            o.MergeCoplanarBeforeBuild = false;
            return o;
        }

        private (AdjacencyCluster cluster, OcctCellComplexResult result) BuildCluster(List<Panel> panels, List<Space> seeds, OcctBuildOptions options)
        {
            var cluster = AnalyticalOcctCreate.AdjacencyCluster(seeds, panels, out var result, null, options);
            return (cluster, result);
        }

        // ═══ Fixture 1: 9-Space Model ═══

        [SkippableFact]
        public void Metrics_Fixture_9Space_FullChain()
        {
            Skip.IfNot(NativeProbe.Available);

            var original = LoadPanels(Path.Combine(CwDirectory, "Panels-9SpacesModel.sam"));
            var expected = LoadSpaces(Path.Combine(CwDirectory, "Spaces-9SpacesModel.sam"));

            output.WriteLine("=== 9-SPACE FIXTURE ===");
            output.WriteLine("Input: {0} panels ({1}W {2}F {3}R)  total area={4:F2} m²",
                original.Count,
                original.Count(x => x.PanelType == PanelType.Wall),
                original.Count(x => x.PanelType == PanelType.Floor),
                original.Count(x => x.PanelType == PanelType.Roof),
                original.Sum(x => x.GetFace3D()?.GetArea() ?? 0));
            output.WriteLine("Expected: {0} spaces", expected.Count);

            // Chain
            var cleaned = original.Clean3D(out _, out var cleanReport, bucketBetweenLevels: 0.21);
            output.WriteLine("Clean3D(band=0.21): {0} panels, {1} frames, {2} groups",
                cleaned.Count, cleanReport.LevelFrames.Count, cleanReport.LevelGroups.Count);

            var extended = cleaned.Extend3D(out _, out var extReport,
                bucketBetweenLevels: 0.21, inputAlreadyClean: true, directionalCapGrow: true);
            output.WriteLine("Extend3D(band=0.21,clean,directed): {0} panels, total area={1:F2} m²",
                extended.Count, extended.Sum(x => x.GetFace3D()?.GetArea() ?? 0));

            // Build + match (MergeCoplanarBeforeBuild=true)
            var opts = new SpaceMatchOptions { LevelBand = 0.21, LevelGroupBand = 0.21 };
            var ess = ExpectedSpaceSet.Create(expected, cleaned, opts, new[] { West3Guid });
            var (cluster, result) = BuildCluster(extended, ess.ToSeedSpaces(), CwOptions());
            var cells = CellGeometry.FromComplex(result);
            var report = SpaceMatcher.Match(ess, cells, new[] { West3Guid }, cluster, original);

            output.WriteLine("");
            output.WriteLine("--- SPACE MATCH (MergeCoplanarBeforeBuild=true) ---");
            foreach (var line in report.ToLines()) output.WriteLine(line);

            output.WriteLine("");
            output.WriteLine("--- CELL METRICS ---");
            output.WriteLine("Cells: {0}  Total volume: {1:F3} m³",
                cells.Count, cells.Sum(c => c.Volume));

            // GH parity without MergeCoplanarBeforeBuild
            var (clusterNo, resultNo) = BuildCluster(extended, ess.ToSeedSpaces(), CwOptionsNoMerge());
            var cellsNo = CellGeometry.FromComplex(resultNo);
            var reportNo = SpaceMatcher.Match(ess, cellsNo, new[] { West3Guid }, clusterNo, original);

            output.WriteLine("");
            output.WriteLine("--- GH PARITY (MergeCoplanarBeforeBuild=false) ---");
            output.WriteLine("Cells: {0}  Volume: {1:F3} m³  Matched: {2}  Missing: {3}",
                cellsNo.Count,
                cellsNo.Sum(c => c.Volume),
                reportNo.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Matched),
                reportNo.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Missing));

            output.WriteLine("");
            output.WriteLine("--- COMPARISON ---");
            output.WriteLine("MergeCoplanar=true:  cells={0} matched={1} missing={2} extra={3}",
                cells.Count,
                report.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Matched),
                report.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Missing),
                report.CellMatches.Count(x => x.Outcome == SpaceMatchOutcome.Extra));
            output.WriteLine("MergeCoplanar=false: cells={0} matched={1} missing={2} extra={3}",
                cellsNo.Count,
                reportNo.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Matched),
                reportNo.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Missing),
                reportNo.CellMatches.Count(x => x.Outcome == SpaceMatchOutcome.Extra));
        }

        // ═══ Fixture 2: Towers (31→29 behaviour) ═══

        [SkippableFact]
        public void Metrics_Fixture_Towers()
        {
            Skip.IfNot(NativeProbe.Available);

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            output.WriteLine("=== TOWERS ===");
            output.WriteLine("Input: {0} panels  total area={1:F2} m²",
                panels.Count, panels.Sum(x => x.GetFace3D()?.GetArea() ?? 0));

            // Solve3D baseline
            var solved = panels.Solve3D(out _, out _, out _, out var solveReport);
            output.WriteLine("Solve3D raw: cells={0}", solveReport.ResolvedCellCount);

            // Tuned Extend3D config (band=0.4, fill=0.4)
            output.WriteLine("");
            output.WriteLine("--- Configs ---");
            foreach (var (label, band, fill, dir, gap) in new (string, double, double, bool, double)[]
            {
                ("band=0.4 fill=0.4 noDir noGap", 0.4, 0.4, false, 0.0),
                ("band=0.4 fill=0.4 noDir gap=0.4", 0.4, 0.4, false, 0.4),
                ("band=0.4 fill=0.4 dir    noGap", 0.4, 0.4, true, 0.0),
                ("band=0.15 fill=0.3 noDir noGap (solver default)", 0.15, 0.3, false, 0.0),
            })
            {
                var ext = panels.Extend3D(out _, out var rpt,
                    bucketBetweenLevels: band, fillMargin: fill,
                    directionalCapGrow: dir, doubleWallGap: gap);
                var face3Ds = ext.Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null).Select(x => x.GetFace3D()).ToList();
                var shells = SAM.Geometry.OCCT.Create.Shells(face3Ds, out var cres, CwOptions());
                double vol = shells?.Sum(s => s?.Volume(Tolerance.MacroDistance, Tolerance.Distance) ?? 0) ?? 0;
                output.WriteLine("{0}: cells={1} volume={2:F2} m³", label, shells?.Count ?? 0, vol);

                // Full cluster
                var (cl, _) = BuildCluster(ext, null, CwOptions());
                output.WriteLine("  → AdjacencyCluster: spaces={0}", cl?.GetSpaces()?.Count ?? 0);
            }
        }

        // ═══ Fixture 3: EastSouth Isolated ═══

        [SkippableFact]
        public void Metrics_Fixture_EastSouthIsolated()
        {
            Skip.IfNot(NativeProbe.Available);

            var original = LoadPanels(Path.Combine(CwDirectory, "Panels-EastSouth-Isolated.sam"));
            var expected = LoadSpaces(Path.Combine(CwDirectory, "Spaces-EastSouth-Isolated.sam"));
            output.WriteLine("=== EAST-SOUTH ISOLATED ===");
            output.WriteLine("Input: {0} panels  expected: {1} spaces", original.Count, expected.Count);

            var cleaned = original.Clean3D(out _, out _, bucketBetweenLevels: 0.21);
            var extended = cleaned.Extend3D(out _, out _,
                bucketBetweenLevels: 0.21, inputAlreadyClean: true, directionalCapGrow: true);

            var opts = new SpaceMatchOptions { LevelBand = 0.21, LevelGroupBand = 0.21 };
            var ess = ExpectedSpaceSet.Create(expected, cleaned, opts);
            var (cluster, result) = BuildCluster(extended, ess.ToSeedSpaces(), CwOptions());
            var cells = CellGeometry.FromComplex(result);
            var report = SpaceMatcher.Match(ess, cells, Array.Empty<Guid>(), cluster, original);

            foreach (var line in report.ToLines()) output.WriteLine(line);
            output.WriteLine("Total volume: {0:F3} m³", cells.Sum(c => c.Volume));
        }

        // ═══ Fixture 4: Golden Masters ═══

        [SkippableTheory]
        [MemberData(nameof(GmFixtures))]
        public void Metrics_Fixtures_GoldenMasters(string fixture)
        {
            Skip.IfNot(NativeProbe.Available);

            var path = Path.Combine(FixturesDirectory, fixture);
            if (!File.Exists(path)) { output.WriteLine("SKIP: {0} not found", fixture); return; }
            var panels = LoadPanels(path);
            output.WriteLine("=== GOLDEN MASTER: {0} ===", fixture);
            output.WriteLine("Input: {0} panels  area={1:F2} m²",
                panels.Count, panels.Sum(x => x.GetFace3D()?.GetArea() ?? 0));

            // Raw path
            var solved = panels.Solve3D(out _, out _, out _, out var report);
            output.WriteLine("Solve3D raw:                  cells={0} naked={1} volume={2:F3}",
                report.ResolvedCellCount,
                report.Signature?.NakedEdgeCount ?? -1,
                report.Signature?.CellVolumes?.Sum() ?? 0);

            // Managed path
            var solvedM = panels.Solve3D(out _, out _, out _, out var reportM, forceManagedPipeline: true);
            output.WriteLine("Solve3D managed:              cells={0} naked={1} volume={2:F3}",
                reportM.ResolvedCellCount,
                reportM.Signature?.NakedEdgeCount ?? -1,
                reportM.Signature?.CellVolumes?.Sum() ?? 0);

            // Extend3D chain
            var extended = panels.Extend3D(out _, out var extReport, bucketBetweenLevels: 0.21);
            var f3d = extended.Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null).Select(x => x.GetFace3D()).ToList();
            var shells = SAM.Geometry.OCCT.Create.Shells(f3d, out var sr, CwOptions());
            output.WriteLine("Extend3D(band=0.21)+Shells:   cells={0} volume={1:F3}",
                shells?.Count ?? 0, shells?.Sum(s => s?.Volume(Tolerance.MacroDistance, Tolerance.Distance) ?? 0) ?? 0);

            // AdjacencyCluster
            var (cl, _) = BuildCluster(extended, null, CwOptions());
            var spaces = cl?.GetSpaces() ?? new List<Space>();
            var panels02 = cl?.GetPanels() ?? new List<Panel>();
            output.WriteLine("AdjacencyCluster:             spaces={0} panels={1}", spaces.Count, panels02.Count);
        }

        public static IEnumerable<object[]> GmFixtures()
        {
            yield return new object[] { "whole-level-flat.sam" };
            yield return new object[] { "tilted-two-spaces.sam" };
            yield return new object[] { "whole-level-tilted.sam" };
            yield return new object[] { "two-level-tilted.sam" };
            yield return new object[] { "whole-level-towers.sam" };
        }

        // ═══ Fixture 5: Parity fixtures (Face3D, Revit, AdjCluster, three-spaces) ═══

        [SkippableTheory]
        [MemberData(nameof(ParityFixtures))]
        public void Metrics_Fixtures_ParitySweep(string fixture)
        {
            Skip.IfNot(NativeProbe.Available);
            var path = Path.Combine(FixturesDirectory, fixture);
            if (!File.Exists(path)) { output.WriteLine("SKIP: {0} not found", fixture); return; }

            var panels = LoadPanels(path);
            output.WriteLine("=== PARITY: {0} ===", fixture);
            output.WriteLine("Input: {0} panels", panels.Count);

            var solved = panels.Solve3D(out _, out _, out _, out var report);
            output.WriteLine("Solve3D raw: cells={0}", report.ResolvedCellCount);

            var extended = panels.Extend3D(out _, out _, bucketBetweenLevels: 0.21);
            var f3d = extended.Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null).Select(x => x.GetFace3D()).ToList();

            var shellsM = SAM.Geometry.OCCT.Create.Shells(f3d, out _, CwOptions());
            output.WriteLine("Extend3D+Shells(w/merge): cells={0}", shellsM?.Count ?? 0);

            var shellsNo = SAM.Geometry.OCCT.Create.Shells(f3d, out _, CwOptionsNoMerge());
            output.WriteLine("Extend3D+Shells(no merge): cells={0}", shellsNo?.Count ?? 0);

            var (cl, _) = BuildCluster(extended, null, CwOptions());
            var spaces = cl?.GetSpaces() ?? new List<Space>();
            output.WriteLine("AdjacencyCluster: spaces={0}", spaces.Count);
        }

        public static IEnumerable<object[]> ParityFixtures()
        {
            yield return new object[] { "Face3D-home.sam" };
            yield return new object[] { "Revit-home-panels.sam" };
            yield return new object[] { "AdjacencyCluster-home.sam" };
            yield return new object[] { "three-spaces.sam" };
        }
    }
}
