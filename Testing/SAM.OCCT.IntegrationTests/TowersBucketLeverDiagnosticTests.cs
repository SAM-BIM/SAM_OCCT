// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT;
using SAM.Analytical.OCCT.Solver;
using SAM.Analytical.Solver;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Solver;
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
    /// Diagnostics for the user-reported dead <c>minBucketSize</c> lever on
    /// <c>whole-level-towers.sam</c> (tiny cells 18/19 at (6.76, -23.31)/(6.76, -23.21) and the
    /// cell 22 / cell 26 split at a ~0.34 m double wall). Runs the exact GH production chain
    /// [Extend3D] -> [CreateAdjacencyCluster] -> [MergeCoplanarAdjacencyCluster] and, separately,
    /// replays every Stage A pair gate (bucket snap, colinear abut, opposed collapse, void guard)
    /// over the wall pairs near the trouble coordinates so the blocking gate is named, not guessed.
    /// </summary>
    public class TowersBucketLeverDiagnosticTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        private readonly ITestOutputHelper output;

        public TowersBucketLeverDiagnosticTests(ITestOutputHelper output) { this.output = output; }

        // Towers AutoTune3D-discovered settings (MultiFixtureAdjacencyDiagnosticTests baseline).
        private const double Band = 0.4;
        private const double Fill = 0.4;
        private const bool DirGrow = false;

        // User-reported trouble coordinates.
        private static readonly (string name, double x, double y, double z, double r)[] TroubleSpots =
        {
            ("cell18-tiny", 6.762921, -23.311381, 13.765, 0.45),
            ("cell19-tiny", 6.762921, -23.214263, 13.765, 0.45),
            ("cell20-big",  6.762921, -20.588049, 13.765, 1.20),
            ("cell21-big",  6.762921, -25.128715, 13.765, 1.20),
            ("cell22",      1.764,    -23.387272, 13.765, 1.00),
            ("cell26",     -4.144651, -13.0,      13.765, 1.50),
        };

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

        // ---------------------------------------------------------------------------------------
        // A. Full user chain, one run: where are the trouble cells at GH defaults?
        // ---------------------------------------------------------------------------------------
        [SkippableFact]
        public void Towers_FullUserChain_Baseline_TroubleCellReport()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(panels);

            var run = RunUserChain(panels, bucket: 0.4, align: 0.3);

            output.WriteLine("=== chain: Extend3D(bucket=0.4, align=0.3, band={0}, fill={1}, dir={2}) -> AdjacencyCluster -> MergeCoplanarPanels ===", Band, Fill, DirGrow);
            output.WriteLine("cells={0}  spaces(before merge)={1}  spaces(after merge)={2}  extPanels={3}",
                run.Cells.Count, run.SpacesBeforeMerge, run.SpacesAfterMerge, run.ExtendedPanelCount);
            output.WriteLine("");

            output.WriteLine("--- all cells by volume ---");
            foreach (var c in run.Cells.OrderBy(c => c.Volume))
            {
                string tags = string.Join("+", TroubleSpots
                    .Where(t => Dist(c, t.x, t.y, t.z) < t.r)
                    .Select(t => t.name));
                output.WriteLine("  vol={0,10:0.####} center=({1,10:F6}, {2,10:F6}, {3,7:F3}) {4}",
                    c.Volume, c.Center?.X ?? 0, c.Center?.Y ?? 0, c.Center?.Z ?? 0, tags);
            }

            output.WriteLine("");
            output.WriteLine("--- Extend3D rejected/void/slit diagnostics ---");
            foreach (string d in run.ExtendDiagnostics.Where(d =>
                d.IndexOf("Rejected", StringComparison.OrdinalIgnoreCase) >= 0
                || d.IndexOf("void", StringComparison.OrdinalIgnoreCase) >= 0
                || d.IndexOf("SLIT", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                output.WriteLine("  {0}", d);
            }

            run.Dispose();
        }

        // ---------------------------------------------------------------------------------------
        // B. Stage A pair-gate replay: for every near-parallel wall pair around the trouble spots,
        //    name the gate that blocks the merge, at several bucket values.
        // ---------------------------------------------------------------------------------------
        [SkippableFact]
        public void Towers_WallPairGateAnalysis_WhyBucketDoesNothing()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(panels);

            var tol = new ToleranceBudget();

            foreach (double bucket in new[] { 0.4, 0.6, 1.0 })
            {
                output.WriteLine("");
                output.WriteLine("################ minBucketSize = {0} (align default 0.3) ################", bucket);

                // Mirror PrepareInput + Stage A order: strip holes, then opposed collapse, then inspect
                // what is left for the weighted bucket snap.
                List<SnappedPanel> snapped = BuildSnappedPanels(panels, bucket);
                foreach (var sp in snapped) sp.StripInternalEdges();

                var diagnostics = new SolverDiagnostics();
                var records = new List<CleanRecord>();
                Panel3DSnapSolver.SnapOpposedPartitions(snapped, tol.Angle, tol.Distance, diagnostics, records);

                output.WriteLine("opposed-collapsed pairs: {0}", records.Count(r => r.Kind == CleanRecordKind.OpposedCollapsed));
                foreach (var d in diagnostics.All ?? new List<SolverDiagnostic>())
                {
                    if (d.Message != null && d.Message.Contains("void"))
                        output.WriteLine("  [opposed-reject] {0}", d.Message);
                }

                // Pair replay over what survives for Snap().
                var live = snapped.Where(x => !x.Snapped && x.Plane != null).ToList();
                output.WriteLine("live panels after opposed pass: {0}", live.Count);
                output.WriteLine("");
                output.WriteLine("sep    | dot    | wall pair (centers)                                        | withinBkt | ovlInPln | ratio  | abut@.3 | voidGrd | verdict");
                output.WriteLine("-------|--------|------------------------------------------------------------|-----------|----------|--------|---------|---------|--------");

                var lines = new List<(double sep, string line)>();
                for (int i = 0; i < live.Count; i++)
                {
                    for (int j = i + 1; j < live.Count; j++)
                    {
                        var a = live[i];
                        var b = live[j];
                        if (!a.IsVertical(tol.VerticalAngle) || !b.IsVertical(tol.VerticalAngle)) continue;

                        double dot = a.Plane.Normal.Unit.DotProduct(b.Plane.Normal.Unit);
                        if (System.Math.Abs(dot) < System.Math.Cos(tol.Angle)) continue; // not near-parallel even at the loose gate

                        double sep = a.PerpendicularSeparation(b);
                        if (sep < tol.Distance || sep > 0.8) continue; // coplanar already, or far beyond any lever

                        if (!NearTrouble(a) && !NearTrouble(b)) continue;

                        lines.Add((sep, DescribePairGates(a, b, dot, sep, tol, alignColinearOffset: 0.3)));
                    }
                }

                foreach (var l in lines.OrderBy(x => x.sep))
                    output.WriteLine(l.line);
            }
        }

        // ---------------------------------------------------------------------------------------
        // C. Full user chain sweep: does ANY bucket value eliminate the tiny cells / join 22 to 26?
        // ---------------------------------------------------------------------------------------
        [SkippableFact]
        public void Towers_FullUserChain_BucketAlignSweep_UserHypothesis()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(panels);

            var configs = new (double bucket, double align)[]
            {
                (0.2, 0.3), (0.4, 0.3), (0.5, 0.3), (0.6, 0.3), (0.8, 0.3), (1.0, 0.3),
                (0.4, 0.45), (0.5, 0.45), (0.6, 0.45),
            };

            var lines = new List<string>
            {
                "bucket | align | cells | spaces | tiny<1 | tiny<3 | v@18     | v@19     | v@22     | v@26     | note",
                "-------|-------|-------|--------|--------|--------|----------|----------|----------|----------|-----",
            };

            foreach (var (bucket, align) in configs)
            {
                var run = RunUserChain(panels, bucket, align);

                double v18 = NearestVolume(run.Cells, 6.762921, -23.311381, 13.765, 0.45);
                double v19 = NearestVolume(run.Cells, 6.762921, -23.214263, 13.765, 0.45);
                double v22 = NearestVolume(run.Cells, 1.764, -23.387272, 13.765, 1.0);
                double v26 = NearestVolume(run.Cells, -4.144651, -13.0, 13.765, 1.5);
                int tiny1 = run.Cells.Count(c => c.Volume > 0 && c.Volume < 1.0);
                int tiny3 = run.Cells.Count(c => c.Volume > 0 && c.Volume < 3.0);

                string note = run.Cells.Count <= 4 ? "COLLAPSE"
                    : double.IsNaN(v18) && double.IsNaN(v19) ? "18/19 gone"
                    : "";

                lines.Add(string.Format("{0,6:0.##} | {1,5:0.##} | {2,5} | {3,6} | {4,6} | {5,6} | {6,8:0.###} | {7,8:0.###} | {8,8:0.###} | {9,8:0.###} | {10}",
                    bucket, align, run.Cells.Count, run.SpacesAfterMerge, tiny1, tiny3, v18, v19, v22, v26, note));

                run.Dispose();
            }

            string outPath = Path.Combine(AppContext.BaseDirectory, "towers_user_chain_bucket_sweep.txt");
            File.WriteAllLines(outPath, lines);
            output.WriteLine("Written to: {0}", outPath);
            foreach (string line in lines) output.WriteLine(line);
        }

        // ---------------------------------------------------------------------------------------
        // D. Identify the tiny cells' bounding faces and the wall stacks (raw vs extended) in the
        //    two trouble bands, so the sliver-forming walls are named exactly.
        // ---------------------------------------------------------------------------------------
        [SkippableFact]
        public void Towers_TinyCellBoundingFaces_WallStacks()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(panels);

            var extended = panels.Extend3D(out List<string> _,
                minBucketSize: 0.4, alignColinearOffset: 0.3,
                bucketBetweenLevels: Band, fillMargin: Fill, directionalCapGrow: DirGrow);

            var nonAir = (extended ?? new List<Panel>()).Where(x => x?.GetFace3D() != null).ToList();
            var opts = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
            var cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                null, nonAir, out OcctCellComplexResult cr, new SAM.Core.Log(), opts);

            output.WriteLine("=== bounding faces of the cells at the trouble spots ===");
            foreach (var spot in TroubleSpots)
            {
                var cell = (cr?.Cells ?? new List<OcctCell>())
                    .OrderBy(c => Dist(c, spot.x, spot.y, spot.z))
                    .FirstOrDefault();
                if (cell == null || Dist(cell, spot.x, spot.y, spot.z) > spot.r) continue;

                output.WriteLine("");
                output.WriteLine("--- {0}: vol={1:0.####} center=({2:F4},{3:F4},{4:F3}) faces={5} ---",
                    spot.name, cell.Volume, cell.Center?.X, cell.Center?.Y, cell.Center?.Z, cell.Faces?.Count ?? 0);
                foreach (var f in cell.Faces ?? new List<OcctCellFace>())
                {
                    var c = f.Face3D?.GetBoundingBox()?.GetCentroid();
                    var n = f.Normal;
                    output.WriteLine("  face area={0,8:0.###} tilt={1,5:F1} centroid=({2,8:F3},{3,8:F3},{4,7:F3}) n=({5,5:F2},{6,5:F2},{7,5:F2})",
                        f.Area, f.Tilt, c?.X ?? 0, c?.Y ?? 0, c?.Z ?? 0, n?.X ?? 0, n?.Y ?? 0, n?.Z ?? 0);
                }
            }

            output.WriteLine("");
            output.WriteLine("=== wall stack, band A (E-W walls, plane Y in [-23.7,-22.7], X hits [4.5,9.5]) ===");
            DumpWallStack(panels, "raw ", p => BandA(p));
            DumpWallStack(extended, "ext ", p => BandA(p));

            output.WriteLine("");
            output.WriteLine("=== wall stack, band B (N-S walls, plane X in [-0.8,0.4], Y hits [-26,-10]) ===");
            DumpWallStack(panels, "raw ", p => BandB(p));
            DumpWallStack(extended, "ext ", p => BandB(p));

            cr?.Dispose();
        }

        private void DumpWallStack(IEnumerable<Panel> panels, string label, Func<Panel, bool> filter)
        {
            foreach (var p in (panels ?? Enumerable.Empty<Panel>()).Where(x => x != null && filter(x)))
            {
                var f = p.GetFace3D();
                var bb = f?.GetBoundingBox();
                var c = bb?.GetCentroid();
                var n = f?.GetPlane()?.Normal;
                output.WriteLine("  {0} {1,-14} c=({2,8:F3},{3,8:F3},{4,7:F3}) x[{5,7:F3},{6,7:F3}] y[{7,7:F3},{8,7:F3}] z[{9,6:F2},{10,6:F2}] n=({11,5:F2},{12,5:F2},{13,5:F2}) A={14:0.##}",
                    label, p.PanelType, c?.X ?? 0, c?.Y ?? 0, c?.Z ?? 0,
                    bb?.Min?.X ?? 0, bb?.Max?.X ?? 0, bb?.Min?.Y ?? 0, bb?.Max?.Y ?? 0, bb?.Min?.Z ?? 0, bb?.Max?.Z ?? 0,
                    n?.X ?? 0, n?.Y ?? 0, n?.Z ?? 0, f?.GetArea() ?? 0);
            }
        }

        private static bool BandA(Panel p)
        {
            var f = p.GetFace3D();
            var n = f?.GetPlane()?.Normal?.Unit;
            var bb = f?.GetBoundingBox();
            if (n == null || bb == null) return false;
            if (System.Math.Abs(n.Y) < 0.7) return false; // E-W wall: normal mostly Y
            var c = bb.GetCentroid();
            return c.Y > -23.7 && c.Y < -22.7 && bb.Max.X > 4.5 && bb.Min.X < 9.5 && bb.Min.Z < 15.6 && bb.Max.Z > 12.0;
        }

        private static bool BandB(Panel p)
        {
            var f = p.GetFace3D();
            var n = f?.GetPlane()?.Normal?.Unit;
            var bb = f?.GetBoundingBox();
            if (n == null || bb == null) return false;
            if (System.Math.Abs(n.X) < 0.7) return false; // N-S wall: normal mostly X
            var c = bb.GetCentroid();
            return c.X > -0.8 && c.X < 0.4 && bb.Max.Y > -26 && bb.Min.Y < -10 && bb.Min.Z < 15.6 && bb.Max.Z > 12.0;
        }

        // ---------------------------------------------------------------------------------------
        // E. The fix: doubleWallGap sweep through the full user chain. 0.4 must eliminate the
        //    18/19 slivers AND make cell 22 share its west wall with tower cell 26.
        // ---------------------------------------------------------------------------------------
        [SkippableFact]
        public void Towers_FullUserChain_DoubleWallGap_FixesTinyCellsAndJoins22To26()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(panels);

            output.WriteLine("gap    | cells | spaces | tiny<3 | v@18     | v@19     | v@22     | v@26     | 22|26 adjacency");
            output.WriteLine("-------|-------|--------|--------|----------|----------|----------|----------|----------------");

            foreach (double gap in new[] { 0.0, 0.35, 0.4, 0.5 })
            {
                var run = RunUserChain(panels, bucket: 0.4, align: 0.3, doubleWallGap: gap);

                double v18 = NearestVolume(run.Cells, 6.762921, -23.311381, 13.765, 0.45);
                double v19 = NearestVolume(run.Cells, 6.762921, -23.214263, 13.765, 0.45);
                double v22 = NearestVolume(run.Cells, 1.764, -23.387272, 13.765, 1.2);
                double v26 = NearestVolume(run.Cells, -4.144651, -13.0, 13.765, 1.5);
                int tiny3 = run.Cells.Count(c => c.Volume > 0 && c.Volume < 3.0);
                bool adjacency = Cell22SharesWallWith26(run);

                output.WriteLine("{0,6:0.##} | {1,5} | {2,6} | {3,6} | {4,8:0.###} | {5,8:0.###} | {6,8:0.###} | {7,8:0.###} | {8}",
                    gap, run.Cells.Count, run.SpacesAfterMerge, tiny3, v18, v19, v22, v26, adjacency);

                if (gap >= 0.4)
                {
                    // The user's two reported defects, both closed by the explicit lever:
                    Assert.True(double.IsNaN(v18) && double.IsNaN(v19), "tiny cells 18/19 must be gone at gap>=0.4");
                    Assert.Equal(0, tiny3);
                    Assert.True(adjacency, "cell 22 must share its west wall with tower cell 26 at gap>=0.4");
                    Assert.True(run.Cells.Count >= 25, "must not collapse");
                }

                if (gap == 0.0)
                {
                    // The lever off = today's behaviour: slivers present, no shared wall.
                    Assert.False(double.IsNaN(v18) || double.IsNaN(v19), "baseline must reproduce the tiny cells");
                    Assert.False(adjacency, "baseline: 22 and 26 are separated by the 0.345 m slot");
                }

                run.Dispose();
            }
        }

        [SkippableFact]
        public void Towers_FullUserChain_Gap04_DumpCellsAndWestWall()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(panels);

            var extended = panels.Extend3D(out List<string> gapDiags,
                minBucketSize: 0.4, alignColinearOffset: 0.3,
                bucketBetweenLevels: Band, fillMargin: Fill, directionalCapGrow: DirGrow,
                doubleWallGap: 0.4);

            output.WriteLine("=== extend SKIP/OPEN/RISKY/stack diagnostics at gap=0.4 ===");
            foreach (string d in gapDiags.Where(d =>
                d.Contains("SKIP") || d.Contains("OPEN_ENDS") || d.Contains("RISKY") || d.Contains("stack-consolidated") || d.Contains("ConsolidatedStack") || d.Contains("consolidation")))
            {
                output.WriteLine("  {0}", d);
            }
            output.WriteLine("");

            output.WriteLine("=== ext wall stack, band B (N-S walls, plane X in [-0.8,0.4]) at gap=0.4 ===");
            DumpWallStack(extended, "ext ", p => BandB(p));

            var nonAir = (extended ?? new List<Panel>()).Where(x => x?.GetFace3D() != null).ToList();
            var opts = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
            var cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                null, nonAir, out OcctCellComplexResult cr, new SAM.Core.Log(), opts);

            output.WriteLine("");
            output.WriteLine("=== all cells at gap=0.4 (idx | vol | center) ===");
            var cells = cr?.Cells?.ToList() ?? new List<OcctCell>();
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                output.WriteLine("  [{0,2}] vol={1,10:0.####} center=({2,10:F6}, {3,10:F6}, {4,7:F3})",
                    i, c.Volume, c.Center?.X ?? 0, c.Center?.Y ?? 0, c.Center?.Z ?? 0);
            }

            int i22 = NearestCellIndex(cells, 1.764, -23.387272, 13.765);
            int i26 = NearestCellIndex(cells, -4.144651, -13.0, 13.765);
            output.WriteLine("");
            output.WriteLine("nearest to 22-spot: [{0}], nearest to 26-spot: [{1}]", i22, i26);
            output.WriteLine("adjacencies touching [{0}]:", i22);
            foreach (var a in cr?.FaceAdjacencies ?? new List<OcctCellFaceAdjacency>())
            {
                if (a.CellIndex1 == i22 || a.CellIndex2 == i22)
                    output.WriteLine("  {0} <-> {1}", a.CellIndex1, a.CellIndex2);
            }

            output.WriteLine("");
            output.WriteLine("--- native diagnostics (gap=0.4) ---");
            foreach (var d in cr?.Diagnostics ?? new List<OcctDiagnostic>())
            {
                output.WriteLine("  {0}", d);
            }

            output.WriteLine("");
            output.WriteLine("--- ext walls around missing cell 22 (x in [-1,4.2], y in [-27.5,-19], any orient) ---");
            DumpWallStack(extended, "ext ", p =>
            {
                var bb = p.GetFace3D()?.GetBoundingBox();
                if (bb == null) return false;
                var c = bb.GetCentroid();
                return c.X > -1 && c.X < 4.2 && c.Y > -27.5 && c.Y < -19 && p.PanelType == PanelType.Wall;
            });
            output.WriteLine("--- ext caps around missing cell 22 ---");
            DumpWallStack(extended, "cap ", p =>
            {
                var bb = p.GetFace3D()?.GetBoundingBox();
                if (bb == null) return false;
                var c = bb.GetCentroid();
                return c.X > -1 && c.X < 4.2 && c.Y > -27.5 && c.Y < -19 && p.PanelType != PanelType.Wall && bb.Min.Z < 15.6;
            });

            output.WriteLine("--- ANY wall whose plan bbox touches the open corner (0.003,-19.856) +-0.06 ---");
            DumpWallStack(extended, "ext ", p =>
            {
                var bb = p.GetFace3D()?.GetBoundingBox();
                if (bb == null || p.PanelType != PanelType.Wall) return false;
                return bb.Min.X - 0.06 < 0.003 && bb.Max.X + 0.06 > 0.003 && bb.Min.Y - 0.06 < -19.856 && bb.Max.Y + 0.06 > -19.856;
            });
            DumpWallStack(panels, "raw ", p =>
            {
                var bb = p.GetFace3D()?.GetBoundingBox();
                if (bb == null || p.PanelType != PanelType.Wall) return false;
                return bb.Min.X - 0.06 < 0.003 && bb.Max.X + 0.06 > 0.003 && bb.Min.Y - 0.06 < -19.856 && bb.Max.Y + 0.06 > -19.856;
            });

            cr?.Dispose();
        }

        /// <summary>True when the run's face adjacencies join the cell nearest cell 22's centre to the cell
        /// nearest cell 26's centre - the "spaces become neighbours through one shared wall" outcome.</summary>
        private static bool Cell22SharesWallWith26(ChainRun run)
        {
            int i22 = NearestCellIndex(run.Cells, 1.764, -23.387272, 13.765);
            int i26 = NearestCellIndex(run.Cells, -4.144651, -13.0, 13.765);
            if (i22 < 0 || i26 < 0 || i22 == i26)
            {
                return false;
            }

            return (run.Result?.FaceAdjacencies ?? new List<OcctCellFaceAdjacency>())
                .Any(a => (a.CellIndex1 == i22 && a.CellIndex2 == i26) || (a.CellIndex1 == i26 && a.CellIndex2 == i22));
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

        // ------------------------------------------------------------------ helpers

        private sealed class ChainRun : IDisposable
        {
            public List<OcctCell> Cells = new List<OcctCell>();
            public int SpacesBeforeMerge;
            public int SpacesAfterMerge;
            public int ExtendedPanelCount;
            public List<string> ExtendDiagnostics = new List<string>();
            public OcctCellComplexResult Result;
            public void Dispose() { Result?.Dispose(); }
        }

        private static ChainRun RunUserChain(List<Panel> panels, double bucket, double align, double doubleWallGap = 0.0)
        {
            var extended = panels.Extend3D(out List<string> extendDiags,
                minBucketSize: bucket, alignColinearOffset: align,
                bucketBetweenLevels: Band, fillMargin: Fill, directionalCapGrow: DirGrow,
                doubleWallGap: doubleWallGap);

            var nonAir = (extended ?? new List<Panel>()).Where(x => x?.GetFace3D() != null).ToList();

            var opts = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
            var cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                null, nonAir, out OcctCellComplexResult cr, new SAM.Core.Log(), opts);

            var merged = cluster?.MergeCoplanarPanels(out List<string> _, Tolerance.Distance, Tolerance.Angle);

            return new ChainRun
            {
                Cells = cr?.Cells?.ToList() ?? new List<OcctCell>(),
                SpacesBeforeMerge = cluster?.GetSpaces()?.Count ?? 0,
                SpacesAfterMerge = merged?.GetSpaces()?.Count ?? 0,
                ExtendedPanelCount = extended?.Count ?? 0,
                ExtendDiagnostics = extendDiags ?? new List<string>(),
                Result = cr,
            };
        }

        /// <summary>Mirrors Solve.PrepareInput + Solve.BucketSize (stamp -> thickness*0.6 -> floor).</summary>
        private static List<SnappedPanel> BuildSnappedPanels(List<Panel> panels, double minBucketSize, double thicknessFactor = 0.6)
        {
            var result = new List<SnappedPanel>();
            int index = 0;
            foreach (var panel in panels.Where(x => x != null && x.PanelType != PanelType.Air))
            {
                var face3D = panel.GetFace3D();
                if (face3D == null || !face3D.IsValid()) { continue; }

                double bucket;
                if (panel.TryGetValue(SolverParameter.BucketSize, out double stamped) && !double.IsNaN(stamped) && stamped > 0)
                {
                    bucket = stamped;
                }
                else
                {
                    double thickness = panel.Construction?.GetThickness() ?? double.NaN;
                    bucket = double.IsNaN(thickness) || thickness <= 0
                        ? minBucketSize
                        : System.Math.Max(minBucketSize, thickness * thicknessFactor);
                }

                result.Add(new SnappedPanel(index, face3D, 1.0, bucket, Panel3DSnapSolver.DEFAULT_MaxExtension));
                index++;
            }
            return result;
        }

        private static bool NearTrouble(SnappedPanel p)
        {
            var c = p.GetBoundingBox()?.GetCentroid();
            if (c == null) return false;
            // Region A: the 18/19 sliver walls. Region B: generous box around the 22|26 boundary.
            bool regionA = c.X > 4.5 && c.X < 9.5 && c.Y > -25.5 && c.Y < -21.0 && c.Z > 12.0 && c.Z < 15.6;
            bool regionB = c.X > -6.5 && c.X < 3.5 && c.Y > -26.0 && c.Y < -10.0 && c.Z > 12.0 && c.Z < 15.6;
            return regionA || regionB;
        }

        /// <summary>Replays the Snap() gate sequence for one pair, both directions, and names the outcome.</summary>
        private static string DescribePairGates(SnappedPanel a, SnappedPanel b, double dot, double sep, ToleranceBudget tol, double alignColinearOffset)
        {
            bool withinAB = a.BucketContains(b, out bool fullyAB);
            bool withinBA = b.BucketContains(a, out bool fullyBA);
            bool within = withinAB || withinBA;
            bool fully = fullyAB || fullyBA;

            // Snap(): angle tolerance depends on full capture.
            double angleTol = fully ? tol.Angle : tol.ArcAngle;
            bool parallel = System.Math.Abs(dot) >= System.Math.Cos(angleTol);

            bool ovl = a.OverlapsInPlane(b, tol.Distance) || b.OverlapsInPlane(a, tol.Distance);
            double ratio = System.Math.Max(a.InPlaneOverlapRatio(b), b.InPlaneOverlapRatio(a));
            bool ratioOk = ratio >= Panel3DSnapSolver.COPARALLEL_SNAP_MIN_OVERLAP_RATIO;

            bool abut = alignColinearOffset > tol.Distance
                && a.IsVertical(tol.VerticalAngle) && b.IsVertical(tol.VerticalAngle)
                && (a.AbutsColinearWithin(b, alignColinearOffset, tol.Distance) || b.AbutsColinearWithin(a, alignColinearOffset, tol.Distance));

            bool voidGuard = dot < 0 && sep > Panel3DSnapSolver.OPPOSED_PARTITION_MAX_SEPARATION;

            string verdict;
            if (!parallel) verdict = "SKIP angle(" + (fully ? "5deg" : "0.3deg") + ")";
            else if ((within && ovl && ratioOk) || abut)
                verdict = voidGuard ? "BLOCKED void-guard" : (within && ovl && ratioOk ? "MERGE bucket" : "MERGE abut");
            else
            {
                var blockers = new List<string>();
                if (!within) blockers.Add("bucket-reach");
                else if (!ovl) blockers.Add("no-inplane-ovl");
                else if (!ratioOk) blockers.Add("ratio<" + Panel3DSnapSolver.COPARALLEL_SNAP_MIN_OVERLAP_RATIO);
                if (!abut) blockers.Add("no-abut");
                verdict = "SKIP " + string.Join(",", blockers);
            }

            var ca = a.GetBoundingBox()?.GetCentroid();
            var cb = b.GetBoundingBox()?.GetCentroid();
            string pair = string.Format("({0,6:F2},{1,7:F2},{2,5:F2})<->({3,6:F2},{4,7:F2},{5,5:F2})",
                ca?.X ?? 0, ca?.Y ?? 0, ca?.Z ?? 0, cb?.X ?? 0, cb?.Y ?? 0, cb?.Z ?? 0);

            return string.Format("{0,6:F3} | {1,6:F3} | {2} | {3,9} | {4,8} | {5,6:F3} | {6,7} | {7,7} | {8}",
                sep, dot, pair, withinAB || withinBA, ovl, ratio, abut, voidGuard, verdict);
        }

        private static double Dist(OcctCell c, double x, double y, double z)
        {
            double dx = (c.Center?.X ?? double.MaxValue) - x;
            double dy = (c.Center?.Y ?? double.MaxValue) - y;
            double dz = (c.Center?.Z ?? double.MaxValue) - z;
            return System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static double NearestVolume(List<OcctCell> cells, double x, double y, double z, double radius)
        {
            double best = double.NaN;
            double bestDist = radius;
            foreach (var c in cells ?? new List<OcctCell>())
            {
                double d = Dist(c, x, y, z);
                if (d < bestDist) { bestDist = d; best = c.Volume; }
            }
            return best;
        }
    }
}
