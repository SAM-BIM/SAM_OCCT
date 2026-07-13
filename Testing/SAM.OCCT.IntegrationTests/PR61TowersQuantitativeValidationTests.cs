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
    /// One identical pipeline (Extend3D(band=0.4, fill=0.4, dir=false, bucket=0.4, align=0.3) →
    /// Create.AdjacencyCluster) is asserted exactly at gaps 0, 0.4 and 0.5: cell count, total
    /// volume, total floor area (cell-derived and level-datum panel metric), volume drift,
    /// floor-area drift, sliver count, 22↔26 connectivity, and the identity of every affected cell.
    /// </para>
    /// <para>
    /// Gap 0.4 (accepted): the two sliver cells (1.095 / 2.738 m³) are absorbed volume-preservingly
    /// into their neighbour, one oversized 157.574 m³ cell splits exactly into the two legitimate
    /// north-strip rooms (78.049 + 79.526 m³), and the +15.010 m³ total-volume increase is the
    /// reclaimed double-wall void volume of two legitimate rooms (+7.610 m³ east-tower room,
    /// +7.427 m³ block-room 22 — the growth that joins it to tower 26). Room topology is preserved.
    /// </para>
    /// <para>
    /// Gap 0.5 (over-aggressive): consolidating the 0.474 m north-strip wall pair leaves the
    /// west north-strip room (78.049 m³, 28.809 m² floor — 26× the sliver threshold) unclosed;
    /// it drops out of the cell complex entirely while its sibling survives with unchanged
    /// volume (destroyed, not merged), and raising fillMargin to 0.5 does not rescue it
    /// (Towers_DoubleWallGap_FillMargin_DefectDiagnostic). Net −67.640 m³
    /// (−78.049 lost room + 10.408 unrelated southeast consolidation growth).
    /// </para>
    /// <para>
    /// Fixture: whole-level-towers.sam. Values captured by actual execution — see
    /// docs/reviews/evidence/PR61_TOWERS_VALIDATION.log.
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

        // North-strip pair (created at gap 0.4 by the volume-preserving split; the west member
        // is destroyed at gap 0.5).
        private const double NorthStripParentX = 3.7398;
        private const double NorthStripParentY = -5.8367;
        private const double NorthStripWestX = 1.7164;
        private const double NorthStripWestY = -5.9059;
        private const double NorthStripEastX = 5.5283;
        private const double NorthStripEastY = -5.8367;
        private const double NorthStripZ = 13.765;

        // The accepted sliver threshold: cells below this volume (m³) are slivers.
        private const double SliverVolumeThreshold = 3.0;

        // Exact expected metrics (m³ / m²), captured by actual execution
        // (docs/reviews/evidence/PR61_TOWERS_VALIDATION.log). AbsTol is the pinning tolerance.
        private const double AbsTol = 0.05;
        private const double Volume0 = 9605.396;
        private const double Volume04 = 9620.406;
        private const double Volume05 = 9552.766;
        private const double CellFloorArea0 = 3160.075;
        private const double CellFloorArea04 = 3168.220;
        private const double CellFloorArea05 = 3142.820;
        private const double PanelFloorArea0 = 1336.727;
        private const double PanelFloorArea04 = 1335.811;
        private const double PanelFloorArea05 = 1330.101;
        private const double Sliver18Volume = 1.095;
        private const double Sliver19Volume = 2.738;
        private const double NorthStripParentVolume = 157.574;
        private const double NorthStripWestVolume = 78.049;
        private const double NorthStripEastVolume = 79.526;

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

        private static OcctCell FindCell(CellRun run, double x, double y, double z, double radius)
        {
            OcctCell best = null;
            double bestDist = double.MaxValue;
            foreach (var c in run.Cells)
            {
                double d = Dist(c, x, y, z);
                if (d < bestDist) { bestDist = d; best = c; }
            }
            return bestDist <= radius ? best : null;
        }

        private static bool CellsShareFace(CellRun run, int a, int b)
        {
            if (a < 0 || b < 0 || a == b) return false;
            return (run.Result?.FaceAdjacencies ?? new List<OcctCellFaceAdjacency>())
                .Any(fa => (fa.CellIndex1 == a && fa.CellIndex2 == b) || (fa.CellIndex1 == b && fa.CellIndex2 == a));
        }

        /// <summary>Cells of <paramref name="a"/> with no near-centroid (&lt; 0.3 m) counterpart
        /// in <paramref name="b"/> — the cells removed (or re-centred by split/merge) between runs.</summary>
        private static List<OcctCell> RemovedCells(CellRun a, CellRun b)
        {
            var removed = new List<OcctCell>();
            foreach (var ca in a.Cells)
            {
                double cx = ca.Center?.X ?? double.MaxValue;
                double cy = ca.Center?.Y ?? double.MaxValue;
                double cz = ca.Center?.Z ?? double.MaxValue;
                if (!b.Cells.Any(cb => Dist(cb, cx, cy, cz) < 0.3))
                    removed.Add(ca);
            }
            return removed;
        }

        /// <summary>Total floor area derived from the built cell complex: per cell, the summed
        /// area of its downward-facing faces (outward-normal tilt ≥ 170°). This measures the
        /// geometry the solver actually produced, not the input panels.</summary>
        private static double CellFloorArea(CellRun run)
        {
            double total = 0;
            foreach (var c in run.Cells)
            {
                foreach (var f in c.Faces ?? (IReadOnlyList<OcctCellFace>)new List<OcctCellFace>())
                {
                    double t = f.Tilt;
                    if (!double.IsNaN(t) && t >= 170.0)
                    {
                        double area = f.Area;
                        if (!double.IsNaN(area)) total += area;
                    }
                }
            }
            return total;
        }

        /// <summary>Level-datum floor area: sum of horizontal cap (Floor/Roof) face areas from the
        /// extended panels, clipped to the relevant level datum band (±0.21 m at Z≈12.24).</summary>
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
                double cellFloorArea = CellFloorArea(run);
                double panelFloorArea = FloorAreaEstimate(run.ExtendedPanels, 12.24);
                int sliverCount = run.Cells.Count(c => c.Volume > 0 && c.Volume < SliverVolumeThreshold);

                // The two sliver centroids are only 0.097 m apart, so each probe point must be
                // matched to its NEAREST cell (an any-within-radius scan matches both points to
                // the same cell).
                var sliver18Cell = FindCell(run, SliverX, SliverY18, SliverZ, SliverRadius);
                var sliver19Cell = FindCell(run, SliverX, SliverY19, SliverZ, SliverRadius);
                double v18 = sliver18Cell?.Volume ?? double.NaN;
                double v19 = sliver19Cell?.Volume ?? double.NaN;

                int i22 = NearestCellIndex(run.Cells, Cell22X, Cell22Y, Cell22Z);
                int i26 = NearestCellIndex(run.Cells, Cell26X, Cell26Y, Cell26Z);
                bool adjacency = CellsShareFace(run, i22, i26);

                output.WriteLine("GAP=0 (baseline): cells={0} volume={1:F3} m³ cellFloorArea={2:F3} m² panelFloorArea={3:F3} m² slivers={4} v18={5:0.###} v19={6:0.###} 22↔26={7}",
                    cellCount, totalVolume, cellFloorArea, panelFloorArea, sliverCount, v18, v19, adjacency);

                // Exact deterministic assertions (verified by actual execution).
                Assert.Equal(31, cellCount);
                Assert.InRange(totalVolume, Volume0 - AbsTol, Volume0 + AbsTol);
                Assert.InRange(cellFloorArea, CellFloorArea0 - AbsTol, CellFloorArea0 + AbsTol);
                Assert.InRange(panelFloorArea, PanelFloorArea0 - AbsTol, PanelFloorArea0 + AbsTol);
                Assert.Equal(2, sliverCount);
                Assert.False(double.IsNaN(v18) || double.IsNaN(v19),
                    "Gap 0 baseline must reproduce the sliver cells 18/19.");
                Assert.NotSame(sliver18Cell, sliver19Cell);
                Assert.InRange(v18, Sliver18Volume - AbsTol, Sliver18Volume + AbsTol);
                Assert.InRange(v19, Sliver19Volume - AbsTol, Sliver19Volume + AbsTol);
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

                double cellArea0 = CellFloorArea(run0);
                double cellArea04 = CellFloorArea(run04);
                double panelArea0 = FloorAreaEstimate(run0.ExtendedPanels, 12.24);
                double panelArea04 = FloorAreaEstimate(run04.ExtendedPanels, 12.24);

                double volDrift = (vol04 - vol0) / vol0 * 100;
                double panelAreaDrift = (panelArea04 - panelArea0) / panelArea0 * 100;

                int slivers04 = run04.Cells.Count(c => c.Volume > 0 && c.Volume < SliverVolumeThreshold);
                var removed = RemovedCells(run0, run04);

                int i22 = NearestCellIndex(run04.Cells, Cell22X, Cell22Y, Cell22Z);
                int i26 = NearestCellIndex(run04.Cells, Cell26X, Cell26Y, Cell26Z);
                bool adjacency04 = CellsShareFace(run04, i22, i26);

                output.WriteLine("GAP=0.4:");
                output.WriteLine("  cells: {0} → {1}", cells0, cells04);
                output.WriteLine("  total volume: {0:F3} → {1:F3} m³ (drift {2:+0.####;-0.####}%)", vol0, vol04, volDrift);
                output.WriteLine("  cell floor area: {0:F3} → {1:F3} m²", cellArea0, cellArea04);
                output.WriteLine("  panel floor area (Z≈12.24): {0:F3} → {1:F3} m² (drift {2:+0.####;-0.####}%)", panelArea0, panelArea04, panelAreaDrift);
                output.WriteLine("  slivers (vol < {0} m³): {1}", SliverVolumeThreshold, slivers04);
                output.WriteLine("  removed cells: {0}", removed.Count);
                foreach (var c in removed)
                    output.WriteLine("    center=({0:F3},{1:F3},{2:F3}) vol={3:0.####} m³",
                        c.Center?.X ?? double.NaN, c.Center?.Y ?? double.NaN, c.Center?.Z ?? double.NaN, c.Volume);
                output.WriteLine("  22↔26 adjacency: {0}", adjacency04);

                // Exact deterministic assertions (verified by actual execution).
                Assert.Equal(30, cells04);
                Assert.InRange(vol04, Volume04 - AbsTol, Volume04 + AbsTol);
                Assert.InRange(cellArea04, CellFloorArea04 - AbsTol, CellFloorArea04 + AbsTol);
                Assert.InRange(panelArea04, PanelFloorArea04 - AbsTol, PanelFloorArea04 + AbsTol);
                Assert.Equal(0, slivers04);
                Assert.True(adjacency04, "Gap 0.4: cells 22 and 26 must share a face.");

                // Volume drift +0.156% (the reclaimed double-wall void volume, see below);
                // level-datum floor-area drift −0.069% (negligible).
                Assert.InRange(volDrift, 0.10, 0.20);
                Assert.InRange(panelAreaDrift, -0.15, 0.0);

                // The two known sliver cells (18/19) must be removed…
                bool sliver18Removed = removed.Any(r => System.Math.Abs((r.Center?.X ?? double.MaxValue) - SliverX) < 0.5 && System.Math.Abs((r.Center?.Y ?? double.MaxValue) - SliverY18) < 0.5);
                bool sliver19Removed = removed.Any(r => System.Math.Abs((r.Center?.X ?? double.MaxValue) - SliverX) < 0.5 && System.Math.Abs((r.Center?.Y ?? double.MaxValue) - SliverY19) < 0.5);
                Assert.True(sliver18Removed, "Gap 0.4: sliver cell 18 must be removed.");
                Assert.True(sliver19Removed, "Gap 0.4: sliver cell 19 must be removed.");

                // …absorbed volume-preservingly into their neighbour (70.628 → 74.461 = +slivers).
                var sliverNeighbour0 = FindCell(run0, SliverX, -25.1287, SliverZ, 0.45);
                var sliverNeighbour04 = FindCell(run04, SliverX, -25.0316, SliverZ, 0.45);
                Assert.NotNull(sliverNeighbour0);
                Assert.NotNull(sliverNeighbour04);
                double absorbed = sliverNeighbour04.Volume - sliverNeighbour0.Volume;
                Assert.InRange(absorbed, Sliver18Volume + Sliver19Volume - AbsTol, Sliver18Volume + Sliver19Volume + AbsTol);

                // The oversized 157.574 m³ north-strip cell must split volume-preservingly into
                // the two legitimate pair rooms (78.049 + 79.526 = 157.574): room topology GAINED.
                var parent0 = FindCell(run0, NorthStripParentX, NorthStripParentY, NorthStripZ, 0.3);
                var west04 = FindCell(run04, NorthStripWestX, NorthStripWestY, NorthStripZ, 0.3);
                var east04 = FindCell(run04, NorthStripEastX, NorthStripEastY, NorthStripZ, 0.3);
                Assert.NotNull(parent0);
                Assert.NotNull(west04);
                Assert.NotNull(east04);
                Assert.InRange(parent0.Volume, NorthStripParentVolume - AbsTol, NorthStripParentVolume + AbsTol);
                Assert.InRange(west04.Volume, NorthStripWestVolume - AbsTol, NorthStripWestVolume + AbsTol);
                Assert.InRange(east04.Volume, NorthStripEastVolume - AbsTol, NorthStripEastVolume + AbsTol);
                Assert.InRange(west04.Volume + east04.Volume, parent0.Volume - AbsTol, parent0.Volume + AbsTol);

                // Affected-cell audit: every removed cell is either a sub-threshold sliver or the
                // volume-preserving split parent — gap 0.4 destroys NO legitimate room.
                foreach (var c in removed)
                {
                    bool isSliver = c.Volume < SliverVolumeThreshold;
                    bool isSplitParent = Dist(c, NorthStripParentX, NorthStripParentY, NorthStripZ) < 0.3;
                    Assert.True(isSliver || isSplitParent,
                        string.Format("Gap 0.4 removed an unexplained cell: vol={0:F3} at ({1:F3},{2:F3},{3:F3}).",
                            c.Volume, c.Center?.X ?? double.NaN, c.Center?.Y ?? double.NaN, c.Center?.Z ?? double.NaN));
                }

                // Geometric attribution of the +15.010 m³: two legitimate rooms expand into the
                // closed double-wall voids — east-tower room +7.610, block-room 22 +7.427 (the
                // growth that makes it share a face with tower 26).
                var eastRoom0 = FindCell(run0, 43.6809, -5.8001, SliverZ, 0.3);
                var eastRoom04 = FindCell(run04, 43.8369, -5.8001, SliverZ, 0.3);
                Assert.NotNull(eastRoom0);
                Assert.NotNull(eastRoom04);
                Assert.InRange(eastRoom04.Volume - eastRoom0.Volume, 7.610 - AbsTol, 7.610 + AbsTol);

                var room22_0 = FindCell(run0, Cell22X, Cell22Y, Cell22Z, 0.3);
                var room22_04 = FindCell(run04, 1.5915, Cell22Y, Cell22Z, 0.3);
                Assert.NotNull(room22_0);
                Assert.NotNull(room22_04);
                Assert.InRange(room22_04.Volume - room22_0.Volume, 7.427 - AbsTol, 7.427 + AbsTol);
            }
        }

        // ── Gap 0.5 (over-aggressive: north-strip west room destroyed) ──

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

                double cellArea0 = CellFloorArea(run0);
                double cellArea05 = CellFloorArea(run05);
                double panelArea0 = FloorAreaEstimate(run0.ExtendedPanels, 12.24);
                double panelArea05 = FloorAreaEstimate(run05.ExtendedPanels, 12.24);

                double volDrift = (vol05 - vol0) / vol0 * 100;
                double panelAreaDrift = (panelArea05 - panelArea0) / panelArea0 * 100;

                int slivers05 = run05.Cells.Count(c => c.Volume > 0 && c.Volume < SliverVolumeThreshold);
                var removed = RemovedCells(run0, run05);

                int i22 = NearestCellIndex(run05.Cells, Cell22X, Cell22Y, Cell22Z);
                int i26 = NearestCellIndex(run05.Cells, Cell26X, Cell26Y, Cell26Z);
                bool adjacency05 = CellsShareFace(run05, i22, i26);

                output.WriteLine("GAP=0.5 (over-aggressive):");
                output.WriteLine("  cells: {0} → {1}", cells0, cells05);
                output.WriteLine("  total volume: {0:F3} → {1:F3} m³ (drift {2:+0.####;-0.####}%)", vol0, vol05, volDrift);
                output.WriteLine("  cell floor area: {0:F3} → {1:F3} m²", cellArea0, cellArea05);
                output.WriteLine("  panel floor area (Z≈12.24): {0:F3} → {1:F3} m² (drift {2:+0.####;-0.####}%)", panelArea0, panelArea05, panelAreaDrift);
                output.WriteLine("  slivers (vol < {0} m³): {1}", SliverVolumeThreshold, slivers05);
                output.WriteLine("  removed cells: {0}", removed.Count);
                foreach (var c in removed)
                    output.WriteLine("    center=({0:F3},{1:F3},{2:F3}) vol={3:0.####} m³",
                        c.Center?.X ?? double.NaN, c.Center?.Y ?? double.NaN, c.Center?.Z ?? double.NaN, c.Volume);
                output.WriteLine("  22↔26 adjacency: {0}", adjacency05);

                // Exact deterministic assertions (verified by actual execution).
                Assert.Equal(29, cells05);
                Assert.InRange(vol05, Volume05 - AbsTol, Volume05 + AbsTol);
                Assert.InRange(cellArea05, CellFloorArea05 - AbsTol, CellFloorArea05 + AbsTol);
                Assert.InRange(panelArea05, PanelFloorArea05 - AbsTol, PanelFloorArea05 + AbsTol);
                Assert.Equal(0, slivers05);
                Assert.True(adjacency05, "Gap 0.5: cells 22 and 26 must share a face.");

                // Gap 0.5 is OVER-AGGRESSIVE: volume drifts −0.548% vs baseline and the
                // level-datum floor area drops −0.496% (vs −0.069% at gap 0.4) — a legitimate
                // room's floor is gone, not an artifact's.
                Assert.InRange(volDrift, -0.60, -0.45);
                Assert.InRange(panelAreaDrift, -0.60, -0.40);

                // The destroyed geometry: the west north-strip room (78.049 m³ — 26× the sliver
                // threshold, a legitimate room) exists at gap 0.4 but has NO successor at gap 0.5,
                // while its pair sibling survives with unchanged volume. The room is destroyed
                // (left unclosed by the 0.474 m wall-pair consolidation; fillMargin=0.5 does not
                // rescue it), not merged into the sibling.
                using (var run04 = RunTowersChain(0.4))
                {
                    var west04 = FindCell(run04, NorthStripWestX, NorthStripWestY, NorthStripZ, 0.3);
                    Assert.NotNull(west04);
                    Assert.InRange(west04.Volume, NorthStripWestVolume - AbsTol, NorthStripWestVolume + AbsTol);
                    Assert.True(west04.Volume > 25 * SliverVolumeThreshold,
                        "The gap-0.5 casualty must be a legitimate room, far above the sliver threshold.");

                    var west05 = FindCell(run05, NorthStripWestX, NorthStripWestY, NorthStripZ, 0.3);
                    Assert.Null(west05);

                    var east04 = FindCell(run04, NorthStripEastX, NorthStripEastY, NorthStripZ, 0.3);
                    var east05 = FindCell(run05, NorthStripEastX, NorthStripEastY, NorthStripZ, 0.3);
                    Assert.NotNull(east04);
                    Assert.NotNull(east05);
                    Assert.InRange(east05.Volume, east04.Volume - AbsTol, east04.Volume + AbsTol);
                }
            }
        }

        // ── Cross-gap comparison (0.4 vs 0.5): 0.5 destroys one legitimate room ──

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
                double volDelta = vol05 - vol04;

                var removed = RemovedCells(run04, run05);

                output.WriteLine("GAP 0.4 vs 0.5:");
                output.WriteLine("  cells: {0} → {1}", cells04, cells05);
                output.WriteLine("  volume: {0:F3} → {1:F3} m³ (delta {2:+0.###;-0.###} m³)", vol04, vol05, volDelta);
                output.WriteLine("  removed cells: {0}", removed.Count);
                foreach (var c in removed)
                    output.WriteLine("    center=({0:F3},{1:F3},{2:F3}) vol={3:0.####} m³",
                        c.Center?.X ?? double.NaN, c.Center?.Y ?? double.NaN, c.Center?.Z ?? double.NaN, c.Volume);

                int i22_04 = NearestCellIndex(run04.Cells, Cell22X, Cell22Y, Cell22Z);
                int i26_04 = NearestCellIndex(run04.Cells, Cell26X, Cell26Y, Cell26Z);
                int i22_05 = NearestCellIndex(run05.Cells, Cell22X, Cell22Y, Cell22Z);
                int i26_05 = NearestCellIndex(run05.Cells, Cell26X, Cell26Y, Cell26Z);

                Assert.True(CellsShareFace(run04, i22_04, i26_04), "Gap 0.4: 22↔26 must be connected.");
                Assert.True(CellsShareFace(run05, i22_05, i26_05), "Gap 0.5: 22↔26 must be connected.");

                Assert.Equal(0, run04.Cells.Count(c => c.Volume > 0 && c.Volume < SliverVolumeThreshold));
                Assert.Equal(0, run05.Cells.Count(c => c.Volume > 0 && c.Volume < SliverVolumeThreshold));

                // Exact assertions: 30 at gap 0.4, 29 at gap 0.5 — exactly one cell lost.
                Assert.Equal(30, cells04);
                Assert.Equal(29, cells05);

                // The −67.640 m³ delta is fully attributed: −78.049 (west north-strip room
                // destroyed) + 10.408 (unrelated southeast consolidation growth).
                Assert.InRange(volDelta, -67.640 - AbsTol, -67.640 + AbsTol);

                Assert.Single(removed);
                var casualty = removed[0];
                Assert.InRange(casualty.Volume, NorthStripWestVolume - AbsTol, NorthStripWestVolume + AbsTol);
                Assert.True(Dist(casualty, NorthStripWestX, NorthStripWestY, NorthStripZ) < 0.3,
                    "The gap-0.5 casualty must be the west north-strip room.");

                var southeast04 = FindCell(run04, 42.5574, -22.5386, 13.765, 0.3);
                var southeast05 = FindCell(run05, 42.7708, -22.5386, 13.765, 0.3);
                Assert.NotNull(southeast04);
                Assert.NotNull(southeast05);
                Assert.InRange(southeast05.Volume - southeast04.Volume, 10.408 - AbsTol, 10.408 + AbsTol);

                // Attribution completeness: casualty + southeast growth ≈ the whole delta.
                Assert.InRange(-casualty.Volume + (southeast05.Volume - southeast04.Volume),
                    volDelta - AbsTol, volDelta + AbsTol);
            }
        }
    }
}
