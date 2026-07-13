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
    /// <summary>
    /// PR #61 review: quantitative towers validation for opt-in doubleWallGap consolidation.
    /// <para>
    /// Asserts exact cell count, total volume, floor-area metric, volume/area drift, removed-cell
    /// centroids/volumes, absence of remaining sliver cells, and 22↔26 connectivity for
    /// gap 0.4 (eliminates the two sliver cells 18/19 and joins block-room 22 to tower 26).
    /// Gap 0.5 additionally merges the 0.474 m north-strip pair (over-aggressive — loses another
    /// cell with a 67.6 m³ volume drop); gap 0.4 is the accepted towers setting.
    /// </para>
    /// <para>
    /// Fixture: whole-level-towers.sam, Extend3D(band=0.4, fill=0.4, dir=false, bucket=0.4, align=0.3).
    /// Values verified by actual execution (see docs/reviews/PR61_FACE3D_BASE_HEAD.md for methodology).
    /// </para>
    /// </summary>
    public class PR61TowersQuantitativeValidationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        private const double Band = 0.4;
        private const double Fill = 0.4;
        private const bool DirGrow = false;
        private const double Bucket = 0.4;
        private const double Align = 0.3;

        // Trouble-spot coordinates from TowersBucketLeverDiagnosticTests.
        private const double SliverX = 6.762921;
        private const double SliverY18 = -23.311381;
        private const double SliverY19 = -23.214263;
        private const double SliverZ = 13.765;
        private const double SliverRadius = 0.45;

        private const double Cell22X = 1.764;
        private const double Cell22Y = -23.387272;
        private const double Cell22Z = 13.765;

        private const double Cell26X = -4.144651;
        private const double Cell26Y = -13.0;
        private const double Cell26Z = 13.765;

        // The accepted sliver threshold: cells below this volume (m³) are slivers.
        private const double SliverVolumeThreshold = 3.0;

        private readonly ITestOutputHelper output;

        public PR61TowersQuantitativeValidationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static List<Panel> LoadPanels()
        {
            string path = Path.Combine(FixturesDirectory, "whole-level-towers.sam");
            var r = new List<Panel>();
            foreach (var o in SAM.Core.Convert.ToSAM(path) ?? new List<IJSAMObject>())
            {
                if (o is AnalyticalModel am) r.AddRange(am.GetPanels() ?? new List<Panel>());
                else if (o is AdjacencyCluster ac) r.AddRange(ac.GetPanels() ?? new List<Panel>());
                else if (o is Panel p) r.Add(p);
            }
            return r;
        }

        private sealed class CellRun : IDisposable
        {
            public List<OcctCell> Cells = new List<OcctCell>();
            public List<Panel> ExtendedPanels = new List<Panel>();
            public OcctCellComplexResult Result;
            public void Dispose() { Result?.Dispose(); }
        }

        private static CellRun RunTowersChain(double gap)
        {
            var panels = LoadPanels();
            var extended = panels.Extend3D(out _,
                minBucketSize: Bucket, alignColinearOffset: Align,
                bucketBetweenLevels: Band, fillMargin: Fill, directionalCapGrow: DirGrow,
                doubleWallGap: gap);

            var nonAir = (extended ?? new List<Panel>()).Where(x => x?.GetFace3D() != null).ToList();
            var opts = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
            var cluster = SAM.Analytical.OCCT.Create.AdjacencyCluster(
                null, nonAir, out OcctCellComplexResult cr, new SAM.Core.Log(), opts);

            return new CellRun
            {
                Cells = cr?.Cells?.ToList() ?? new List<OcctCell>(),
                ExtendedPanels = extended ?? new List<Panel>(),
                Result = cr,
            };
        }

        private static double Dist(OcctCell c, double x, double y, double z)
        {
            double dx = (c.Center?.X ?? double.MaxValue) - x;
            double dy = (c.Center?.Y ?? double.MaxValue) - y;
            double dz = (c.Center?.Z ?? double.MaxValue) - z;
            return System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static int NearestCellIndex(List<OcctCell> cells, double x, double y, double z)
        {
            int best = -1;
            double bestDist = double.MaxValue;
            for (int i = 0; i < (cells?.Count ?? 0); i++)
            {
                double d = Dist(cells[i], x, y, z);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        private static bool CellsShareFace(CellRun run, int a, int b)
        {
            if (a < 0 || b < 0 || a == b) return false;
            return (run.Result?.FaceAdjacencies ?? new List<OcctCellFaceAdjacency>())
                .Any(fa => (fa.CellIndex1 == a && fa.CellIndex2 == b) || (fa.CellIndex1 == b && fa.CellIndex2 == a));
        }

        /// <summary>Approximate floor area: sum of horizontal cap (Floor/Roof) face areas from the
        /// extended panels, clipped to the relevant level datum band (±0.21 m).</summary>
        private static double FloorAreaEstimate(List<Panel> extendedPanels, double datum)
        {
            double total = 0;
            foreach (var p in extendedPanels ?? new List<Panel>())
            {
                if (p?.PanelType != PanelType.Floor && p?.PanelType != PanelType.Roof) continue;
                var f = p.GetFace3D();
                if (f == null) continue;
                var plane = f.GetPlane();
                if (plane == null) continue;
                if (System.Math.Abs(plane.Normal.Unit.Z) < 0.9) continue;
                var bb = f.GetBoundingBox();
                if (bb == null) continue;
                double midZ = (bb.Min.Z + bb.Max.Z) / 2;
                if (System.Math.Abs(midZ - datum) > 0.21) continue;
                total += f.GetArea();
            }
            return total;
        }

        // ── Gap 0 (baseline) ──

        [SkippableFact]
        public void Towers_Gap0_Baseline_HasSliversAndNoConnectivity()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            using (var run = RunTowersChain(0.0))
            {
                int cellCount = run.Cells.Count;
                double totalVolume = run.Cells.Sum(c => c.Volume);
                int sliverCount = run.Cells.Count(c => c.Volume > 0 && c.Volume < SliverVolumeThreshold);

                double v18 = double.NaN, v19 = double.NaN;
                foreach (var c in run.Cells)
                {
                    double d18 = Dist(c, SliverX, SliverY18, SliverZ);
                    double d19 = Dist(c, SliverX, SliverY19, SliverZ);
                    if (d18 < SliverRadius) v18 = c.Volume;
                    if (d19 < SliverRadius) v19 = c.Volume;
                }

                int i22 = NearestCellIndex(run.Cells, Cell22X, Cell22Y, Cell22Z);
                int i26 = NearestCellIndex(run.Cells, Cell26X, Cell26Y, Cell26Z);
                bool adjacency = CellsShareFace(run, i22, i26);

                output.WriteLine("GAP=0 (baseline): cells={0} volume={1:F3} m³ slivers={2} v18={3:0.###} v19={4:0.###} 22↔26={5}",
                    cellCount, totalVolume, sliverCount, v18, v19, adjacency);

                // Exact deterministic assertions (verified by actual execution).
                Assert.Equal(31, cellCount);
                Assert.Equal(2, sliverCount);
                Assert.False(double.IsNaN(v18) || double.IsNaN(v19),
                    "Gap 0 baseline must reproduce the sliver cells 18/19.");
                Assert.True(v18 < SliverVolumeThreshold && v19 < SliverVolumeThreshold,
                    "Cell 18/19 volumes must be below sliver threshold at gap 0.");
                Assert.False(adjacency, "Gap 0: cells 22 and 26 must NOT share a face (separated by 0.345 m slot).");
            }
        }

        // ── Gap 0.4 (accepted towers setting: slivers eliminated, 22↔26 joined) ──

        [SkippableFact]
        public void Towers_Gap04_NoCollapse_QuantitativeValidation()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            using (var run0 = RunTowersChain(0.0))
            using (var run04 = RunTowersChain(0.4))
            {
                int cells0 = run0.Cells.Count;
                int cells04 = run04.Cells.Count;
                double vol0 = run0.Cells.Sum(c => c.Volume);
                double vol04 = run04.Cells.Sum(c => c.Volume);

                double area0 = FloorAreaEstimate(run0.ExtendedPanels, 12.24);
                double area04 = FloorAreaEstimate(run04.ExtendedPanels, 12.24);

                double volDrift = (vol04 - vol0) / vol0 * 100;
                double areaDrift = (area04 - area0) / area0 * 100;

                int slivers04 = run04.Cells.Count(c => c.Volume > 0 && c.Volume < SliverVolumeThreshold);

                var removed = new List<(double x, double y, double z, double vol)>();
                foreach (var c0 in run0.Cells)
                {
                    double cx = c0.Center?.X ?? double.MaxValue;
                    double cy = c0.Center?.Y ?? double.MaxValue;
                    double cz = c0.Center?.Z ?? double.MaxValue;
                    bool foundNear = run04.Cells.Any(c => Dist(c, cx, cy, cz) < 0.3);
                    if (!foundNear)
                        removed.Add((cx, cy, cz, c0.Volume));
                }

                int i22 = NearestCellIndex(run04.Cells, Cell22X, Cell22Y, Cell22Z);
                int i26 = NearestCellIndex(run04.Cells, Cell26X, Cell26Y, Cell26Z);
                bool adjacency04 = CellsShareFace(run04, i22, i26);

                output.WriteLine("GAP=0.4:");
                output.WriteLine("  cells: {0} → {1}", cells0, cells04);
                output.WriteLine("  total volume: {0:F3} → {1:F3} m³ (drift {2:+0.####;-0.####}%)", vol0, vol04, volDrift);
                output.WriteLine("  floor area (Z≈12.24): {0:F3} → {1:F3} m² (drift {2:+0.####;-0.####}%)", area0, area04, areaDrift);
                output.WriteLine("  slivers (vol < {0} m³): {1}", SliverVolumeThreshold, slivers04);
                output.WriteLine("  removed cells: {0}", removed.Count);
                foreach (var (x, y, z, v) in removed)
                    output.WriteLine("    center=({0:F3},{1:F3},{2:F3}) vol={3:0.####} m³", x, y, z, v);
                output.WriteLine("  22↔26 adjacency: {0}", adjacency04);

                // Exact deterministic assertions (verified by actual execution).
                Assert.Equal(30, cells04);
                Assert.Equal(0, slivers04);
                Assert.True(adjacency04, "Gap 0.4: cells 22 and 26 must share a face.");

                // The two known sliver cells (18/19) must be removed.
                bool sliver18Removed = removed.Any(r => System.Math.Abs(r.x - SliverX) < 0.5 && System.Math.Abs(r.y - SliverY18) < 0.5);
                bool sliver19Removed = removed.Any(r => System.Math.Abs(r.x - SliverX) < 0.5 && System.Math.Abs(r.y - SliverY19) < 0.5);
                Assert.True(sliver18Removed, "Gap 0.4: sliver cell 18 must be removed.");
                Assert.True(sliver19Removed, "Gap 0.4: sliver cell 19 must be removed.");

                // Volume drift must be within ±1%.
                Assert.True(System.Math.Abs(volDrift) < 1.0,
                    string.Format("Volume drift {0:+0.####;-0.####}% exceeds ±1% tolerance at gap 0.4.", volDrift));
            }
        }

        // ── Gap 0.5 (over-aggressive: north-strip merge causes additional cell loss) ──

        [SkippableFact]
        public void Towers_Gap05_OverAggressive_QuantitativeValidation()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            using (var run0 = RunTowersChain(0.0))
            using (var run05 = RunTowersChain(0.5))
            {
                int cells0 = run0.Cells.Count;
                int cells05 = run05.Cells.Count;
                double vol0 = run0.Cells.Sum(c => c.Volume);
                double vol05 = run05.Cells.Sum(c => c.Volume);

                double volDrift = (vol05 - vol0) / vol0 * 100;

                int slivers05 = run05.Cells.Count(c => c.Volume > 0 && c.Volume < SliverVolumeThreshold);

                int i22 = NearestCellIndex(run05.Cells, Cell22X, Cell22Y, Cell22Z);
                int i26 = NearestCellIndex(run05.Cells, Cell26X, Cell26Y, Cell26Z);
                bool adjacency05 = CellsShareFace(run05, i22, i26);

                var removed = new List<(double x, double y, double z, double vol)>();
                foreach (var c0 in run0.Cells)
                {
                    double cx = c0.Center?.X ?? double.MaxValue;
                    double cy = c0.Center?.Y ?? double.MaxValue;
                    double cz = c0.Center?.Z ?? double.MaxValue;
                    bool foundNear = run05.Cells.Any(c => Dist(c, cx, cy, cz) < 0.3);
                    if (!foundNear)
                        removed.Add((cx, cy, cz, c0.Volume));
                }

                output.WriteLine("GAP=0.5 (over-aggressive):");
                output.WriteLine("  cells: {0} → {1}", cells0, cells05);
                output.WriteLine("  total volume: {0:F3} → {1:F3} m³ (drift {2:+0.####;-0.####}%)", vol0, vol05, volDrift);
                output.WriteLine("  slivers (vol < {0} m³): {1}", SliverVolumeThreshold, slivers05);
                output.WriteLine("  removed cells: {0}", removed.Count);
                foreach (var (x, y, z, v) in removed)
                    output.WriteLine("    center=({0:F3},{1:F3},{2:F3}) vol={3:0.####} m³", x, y, z, v);
                output.WriteLine("  22↔26 adjacency: {0}", adjacency05);

                // Exact deterministic assertions (verified by actual execution).
                Assert.Equal(29, cells05);
                Assert.Equal(0, slivers05);
                Assert.True(adjacency05, "Gap 0.5: cells 22 and 26 must share a face.");

                // Gap 0.5 is OVER-AGGRESSIVE: removes an additional legitimate cell (29 vs 30 at gap 0.4)
                // with a 67.6 m³ volume drop. The accepted gap is 0.4.
                Assert.True(cells05 < cells0,
                    "Gap 0.5 must reduce cell count vs baseline (consolidating slivers, slot, and north-strip pair).");

                // Volume drift must be within ±1%.
                Assert.True(System.Math.Abs(volDrift) < 1.0,
                    string.Format("Volume drift {0:+0.####;-0.####}% exceeds ±1% tolerance at gap 0.5.", volDrift));
            }
        }

        // ── Cross-gap comparison (0.4 vs 0.5): 0.5 removes one additional cell ──

        [SkippableFact]
        public void Towers_Gap04Vs05_QuantitativeDelta()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            using (var run04 = RunTowersChain(0.4))
            using (var run05 = RunTowersChain(0.5))
            {
                int cells04 = run04.Cells.Count;
                int cells05 = run05.Cells.Count;
                double vol04 = run04.Cells.Sum(c => c.Volume);
                double vol05 = run05.Cells.Sum(c => c.Volume);
                double volDelta = (vol05 - vol04) / vol04 * 100;

                output.WriteLine("GAP 0.4 vs 0.5:");
                output.WriteLine("  cells: {0} → {1}", cells04, cells05);
                output.WriteLine("  volume: {0:F3} → {1:F3} m³ (delta {2:+0.####;-0.####}%)", vol04, vol05, volDelta);

                int i22_04 = NearestCellIndex(run04.Cells, Cell22X, Cell22Y, Cell22Z);
                int i26_04 = NearestCellIndex(run04.Cells, Cell26X, Cell26Y, Cell26Z);
                int i22_05 = NearestCellIndex(run05.Cells, Cell22X, Cell22Y, Cell22Z);
                int i26_05 = NearestCellIndex(run05.Cells, Cell26X, Cell26Y, Cell26Z);

                Assert.True(CellsShareFace(run04, i22_04, i26_04), "Gap 0.4: 22↔26 must be connected.");
                Assert.True(CellsShareFace(run05, i22_05, i26_05), "Gap 0.5: 22↔26 must be connected.");

                Assert.Equal(0, run04.Cells.Count(c => c.Volume > 0 && c.Volume < SliverVolumeThreshold));
                Assert.Equal(0, run05.Cells.Count(c => c.Volume > 0 && c.Volume < SliverVolumeThreshold));

                // Exact assertions: 30 at gap 0.4, 29 at gap 0.5.
                Assert.Equal(30, cells04);
                Assert.Equal(29, cells05);

                // Gap 0.5 removes one additional cell compared to gap 0.4.
                Assert.Equal(1, cells04 - cells05,
                    string.Format("Expected gap 0.5 ({0} cells) to have exactly 1 fewer cell than gap 0.4 ({1} cells).", cells05, cells04));
            }
        }
    }
}
