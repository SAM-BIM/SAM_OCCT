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
        // Phase 0: Root-cause diagnostic matrix {gap} x {fillMargin}.
        // Pin the defect: which cell is lost at gap 0.5, why, and can fillMargin 0.5 rescue it.
        // ---------------------------------------------------------------------------------------
        [SkippableFact]
        public void Towers_DoubleWallGap_FillMargin_DefectDiagnostic()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(panels);

            const double storeyPitch = 3.05;
            const double superTallThreshold = 1.5 * storeyPitch; // 4.575 m

            // Baseline: gap=0.4, fillMargin=0.4 (known 29-cell result).
            var baseline = RunUserChain(panels, bucket: 0.4, align: 0.3, doubleWallGap: 0.4, fillMargin: 0.4);
            var baselineCells = baseline.Cells.OrderBy(c => c.Volume).ToList();

            output.WriteLine("=== BASELINE gap=0.4 fill=0.4: {0} cells ===", baselineCells.Count);
            for (int i = 0; i < baselineCells.Count; i++)
            {
                var c = baselineCells[i];
                output.WriteLine("  [{0,2}] vol={1,10:0.####} center=({2,10:F6}, {3,10:F6}, {4,7:F3})",
                    i, c.Volume, c.Center?.X ?? 0, c.Center?.Y ?? 0, c.Center?.Z ?? 0);
            }
            output.WriteLine("");

            // Cell nearest the user's point (3.706, -5.836, 13.966) in baseline - the strip space.
            int baselineStripIdx = NearestCellIndex(baselineCells, 3.706, -5.836, 13.966);
            var baselineStrip = baselineStripIdx >= 0 ? baselineCells[baselineStripIdx] : null;
            output.WriteLine("--- Baseline strip cell [{0}]: vol={1} center=({2:F4},{3:F4},{4:F4}) ---",
                baselineStripIdx,
                baselineStrip?.Volume ?? double.NaN,
                baselineStrip?.Center?.X ?? double.NaN,
                baselineStrip?.Center?.Y ?? double.NaN,
                baselineStrip?.Center?.Z ?? double.NaN);

            // Matrix sweep.
            var configs = new (double gap, double fillMargin)[]
            {
                (0.4, 0.4), // baseline
                (0.4, 0.5),
                (0.47, 0.4),
                (0.47, 0.5),
                (0.5, 0.4),
                (0.5, 0.5),
            };

            foreach (var (gap, fillMargin) in configs)
            {
                output.WriteLine("");
                output.WriteLine("################ gap={0} fillMargin={1} ################", gap, fillMargin);

                var run = RunUserChain(panels, bucket: 0.4, align: 0.3, doubleWallGap: gap, fillMargin: fillMargin);
                var cells = run.Cells.OrderBy(c => c.Volume).ToList();

                output.WriteLine("cells={0}  spaces(after merge)={1}", cells.Count, run.SpacesAfterMerge);

                // (a) Per-cell dump + diff vs baseline.
                output.WriteLine("--- cells diff vs gap=0.4/fill=0.4 baseline ---");
                double tolerance = 0.001;
                for (int i = 0; i < cells.Count; i++)
                {
                    var c = cells[i];
                    double vol = c.Volume;
                    double cx = c.Center?.X ?? 0;
                    double cy = c.Center?.Y ?? 0;
                    double cz = c.Center?.Z ?? 0;

                    // Match to nearest baseline cell by centre distance.
                    double bestDist = double.MaxValue;
                    int bestMatch = -1;
                    for (int j = 0; j < baselineCells.Count; j++)
                    {
                        var bc = baselineCells[j];
                        double dx = (bc.Center?.X ?? double.MaxValue) - cx;
                        double dy = (bc.Center?.Y ?? double.MaxValue) - cy;
                        double dz = (bc.Center?.Z ?? double.MaxValue) - cz;
                        double d = dx * dx + dy * dy + dz * dz;
                        if (d < bestDist) { bestDist = d; bestMatch = j; }
                    }

                    double volDiff = bestMatch >= 0 ? vol - baselineCells[bestMatch].Volume : double.NaN;
                    bool moved = double.IsNaN(volDiff) || System.Math.Abs(volDiff) > System.Math.Max(tolerance, 0.01 * baselineCells[bestMatch].Volume);
                    string change = double.IsNaN(volDiff) ? "NEW?"
                        : !moved ? "same"
                        : (volDiff > 0 ? "+" : "") + (volDiff / baselineCells[bestMatch].Volume * 100).ToString("F0") + "%";

                    output.WriteLine("  [{0,2}] vol={1,10:0.####} center=({2,10:F6}, {3,10:F6}, {4,7:F3})  (match [{5,2}], {6})",
                        i, vol, cx, cy, cz, bestMatch, change);
                }

                if (cells.Count < baselineCells.Count)
                {
                    output.WriteLine("--- VANISHED cell(s) vs baseline ---");
                    for (int j = 0; j < baselineCells.Count; j++)
                    {
                        var bc = baselineCells[j];
                        double bx = bc.Center?.X ?? 0;
                        double by = bc.Center?.Y ?? 0;
                        double bz = bc.Center?.Z ?? 0;
                        bool found = cells.Any(c =>
                        {
                            double dx = (c.Center?.X ?? double.MaxValue) - bx;
                            double dy = (c.Center?.Y ?? double.MaxValue) - by;
                            double dz = (c.Center?.Z ?? double.MaxValue) - bz;
                            return dx * dx + dy * dy + dz * dz < 0.25; // within 0.5 m
                        });
                        if (!found)
                        {
                            output.WriteLine("  [{0,2}] vol={1,10:0.####} center=({2,10:F6}, {3,10:F6}, {4,7:F3})  <-- VANISHED",
                                j, bc.Volume, bx, by, bz);
                        }
                    }
                }

                // (b) Wall report: planes x in [-0.8, 0.4] AND near user's point (3.71, -5.84).
                output.WriteLine("");
                output.WriteLine("--- walls near the merged plane (x in [-0.8,0.4]) and user point (3.71,-5.84) ---");
                var extended = panels.Extend3D(out List<string> extDiags, out Solve3DReport report,
                    minBucketSize: 0.4, alignColinearOffset: 0.3,
                    bucketBetweenLevels: Band, fillMargin: fillMargin, directionalCapGrow: DirGrow,
                    doubleWallGap: gap);
                var allWalls = (extended ?? new List<Panel>())
                    .Where(x => x?.GetFace3D() != null && x.PanelType == PanelType.Wall)
                    .ToList();

                var reportWalls = allWalls.Where(p =>
                {
                    var bb = p.GetFace3D()?.GetBoundingBox();
                    var c = bb?.GetCentroid();
                    if (bb == null || c == null) return false;
                    // x in [-0.8, 0.4] OR near user point (3.71, -5.84) within 2 m.
                    bool inBand = bb.Min.X < 0.4 && bb.Max.X > -0.8;
                    bool nearUser = (c.X - 3.71) * (c.X - 3.71) + (c.Y - (-5.84)) * (c.Y - (-5.84)) < 4.0;
                    return inBand || nearUser;
                }).ToList();

                foreach (var p in reportWalls)
                {
                    var f = p.GetFace3D();
                    var bb = f?.GetBoundingBox();
                    var c = bb?.GetCentroid();
                    var n = f?.GetPlane()?.Normal;
                    double zSpan = (bb?.Max.Z ?? 0) - (bb?.Min.Z ?? 0);
                    bool superTall = zSpan > superTallThreshold;

                    // Coplanar duplicate detection: another wall with near-identical plane and overlapping bbox.
                    bool hasCoplanarDup = false;
                    if (bb != null && n != null)
                    {
                        foreach (var other in reportWalls)
                        {
                            if (ReferenceEquals(p, other)) continue;
                            var otherF = other.GetFace3D();
                            var otherN = otherF?.GetPlane()?.Normal;
                            if (otherN == null) continue;
                            double dot = System.Math.Abs(n.Unit.DotProduct(otherN.Unit));
                            if (dot < 0.999) continue;
                            double sep = otherF.GetPlane()?.Distance(bb.GetCentroid()) ?? double.MaxValue;
                            if (sep < 0.01)
                            {
                                hasCoplanarDup = true;
                                break;
                            }
                        }
                    }

                    // Plan feet.
                    var plane = f?.GetPlane();
                    Segment3D foot = null;
                    if (plane != null && bb != null)
                    {
                        double atZ = bb.Min.Z;
                        foot = new Segment3D(
                            plane.Project(new Point3D(bb.Min.X, bb.Min.Y, atZ)),
                            plane.Project(new Point3D(bb.Max.X, bb.Max.Y, atZ)));
                    }

                    output.WriteLine("  wall c=({0,8:F3},{1,8:F3},{2,7:F3}) z=[{3,6:F2},{4,6:F2}] zSpan={5,5:F2} superTall={6,5} coplanarDup={7,5} foot=({8,6:F2},{9,6:F2})-({10,6:F2},{11,6:F2})",
                        c?.X ?? 0, c?.Y ?? 0, c?.Z ?? 0,
                        bb?.Min.Z ?? 0, bb?.Max.Z ?? 0,
                        zSpan, superTall, hasCoplanarDup,
                        foot?.GetStart().X ?? 0, foot?.GetStart().Y ?? 0,
                        foot?.GetEnd().X ?? 0, foot?.GetEnd().Y ?? 0);
                }

                // (c) E3 ExtendRecords for those walls + NoTargetWithinReach roll-up.
                output.WriteLine("");
                output.WriteLine("--- E3 ExtendRecords (wall caps + NoTargetWithinReach) ---");
                var extendRecords = report?.ExtendRecords ?? new List<ExtendRecord>();
                int noTargetTop = 0;
                int noTargetBottom = 0;
                foreach (var rec in extendRecords)
                {
                    if (rec.Kind == ExtendOperationKind.Top || rec.Kind == ExtendOperationKind.Bottom)
                    {
                        if (rec.SkipReason == ExtendSkipReason.NoTargetWithinReach)
                        {
                            if (rec.Kind == ExtendOperationKind.Top) noTargetTop++;
                            else noTargetBottom++;
                        }
                        output.WriteLine("  {0} src={1} from→to={2:F4}→{3:F4} targetIdx={4} skip={5}",
                            rec.Kind, rec.SourceIndex, rec.FromValue, rec.ToValue, rec.TargetPanelIndex, rec.SkipReason);
                    }
                }
                output.WriteLine("  NoTargetWithinReach: top={0} bottom={1}", noTargetTop, noTargetBottom);

                // (d) Cap bbox edges crossing the seam x in [-0.3448, +0.1289].
                output.WriteLine("");
                output.WriteLine("--- Caps whose bbox crosses the seam x in [-0.3448, +0.1289] ---");
                var allCaps = (extended ?? new List<Panel>())
                    .Where(x => x?.GetFace3D() != null && x.PanelType != PanelType.Wall)
                    .ToList();
                var seamCaps = allCaps.Where(p =>
                {
                    var bb = p.GetFace3D()?.GetBoundingBox();
                    if (bb == null) return false;
                    return bb.Min.X < 0.1289 && bb.Max.X > -0.3448;
                }).ToList();
                bool anyFloorBridgedSeam = seamCaps.Any(p =>
                {
                    var bb = p.GetFace3D()?.GetBoundingBox();
                    return bb != null && bb.Min.X <= -0.3448 - 0.01 && bb.Max.X >= 0.1289 + 0.01;
                });
                output.WriteLine("  count={0}  anyFloorBridgedSeam={1}", seamCaps.Count, anyFloorBridgedSeam);
                foreach (var p in seamCaps)
                {
                    var bb = p.GetFace3D()?.GetBoundingBox();
                    output.WriteLine("  {0} x=[{1,8:F4},{2,8:F4}] y=[{3,8:F4},{4,8:F4}] z=[{5,6:F2},{6,6:F2}]",
                        p.PanelType, bb?.Min.X ?? 0, bb?.Max.X ?? 0, bb?.Min.Y ?? 0, bb?.Max.Y ?? 0, bb?.Min.Z ?? 0, bb?.Max.Z ?? 0);
                }

                run.Dispose();
            }

            // (c) Conclusion: does fillMargin 0.5 alone rescue the space?
            output.WriteLine("");
            output.WriteLine("=== CONCLUSION ===");
            var gap05Fill04 = RunUserChain(panels, bucket: 0.4, align: 0.3, doubleWallGap: 0.5, fillMargin: 0.4);
            var gap05Fill05 = RunUserChain(panels, bucket: 0.4, align: 0.3, doubleWallGap: 0.5, fillMargin: 0.5);
            output.WriteLine("gap=0.5 fill=0.4: cells={0}", gap05Fill04.Cells.Count);
            output.WriteLine("gap=0.5 fill=0.5: cells={0}", gap05Fill05.Cells.Count);
            output.WriteLine("fillMargin 0.5 rescues the space: {0}", gap05Fill05.Cells.Count > gap05Fill04.Cells.Count);
            gap05Fill04.Dispose();
            gap05Fill05.Dispose();
            baseline.Dispose();
        }

        // ---------------------------------------------------------------------------------------
        // Phase 1d: Acceptance test — with the cap-follow fix, gap 0.5 must give 29 cells
        // (the strip space near (3.706,-5.836) survives), a tower↔strip adjacency appears,
        // no super-tall wall at the merged plane. At gap 0.47: 29 cells, no merge, near-miss.
        // ---------------------------------------------------------------------------------------
        [SkippableFact]
        public void Towers_DoubleWallGap_FixAcceptance_Gap05StripSurvives()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(panels);

            // gap 0.5: the 0.474 pair merges, the strip space survives, new adjacency.
            var run05 = RunUserChain(panels, bucket: 0.4, align: 0.3, doubleWallGap: 0.5, fillMargin: 0.4);
            output.WriteLine("gap=0.5: cells={0}", run05.Cells.Count);
            Assert.Equal(29, run05.Cells.Count); // strip space survives

            // gap 0.47: the 0.474 pair does NOT merge (0.47 < 0.474), so cells stay at 30
            // (the 0.345 pair already merged at 0.4, eliminating the two sliver cells).
            var run047 = RunUserChain(panels, bucket: 0.4, align: 0.3, doubleWallGap: 0.47, fillMargin: 0.4);
            output.WriteLine("gap=0.47: cells={0}", run047.Cells.Count);
            Assert.Equal(30, run047.Cells.Count); // 0.345 merges (slivers gone), 0.474 pair stays

            run05.Dispose();
            run047.Dispose();
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
            public List<Panel> ExtendedPanels = new List<Panel>();
            public OcctCellComplexResult Result;
            public void Dispose() { Result?.Dispose(); }
        }

        private static ChainRun RunUserChain(List<Panel> panels, double bucket, double align, double doubleWallGap = 0.0, double fillMargin = Fill)
        {
            var extended = panels.Extend3D(out List<string> extendDiags,
                minBucketSize: bucket, alignColinearOffset: align,
                bucketBetweenLevels: Band, fillMargin: fillMargin, directionalCapGrow: DirGrow,
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
                ExtendedPanels = extended ?? new List<Panel>(),
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

        // ---------------------------------------------------------------------------------------
        // G. North-strip pair gap sweep: 0.5 merges the 0.474 m pair, 0.47 emits near-miss.
        // ---------------------------------------------------------------------------------------
        [SkippableFact]
        public void Towers_NorthStripPair_GapSweep_MergedAt05_NearMissAt047()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(panels);

            // Dump all N-S wall planes (no Y filter) to pin the actual planes in the model.
            DumpNsWallPlanes(panels, "ALL raw N-S walls", yMin: double.MinValue, yMax: double.MaxValue);

            int cellsAt047 = -1, cellsAt05 = -1;
            bool nearMissAt047 = false;

            foreach (double gap in new[] { 0.47, 0.5 })
            {
                var run = RunUserChain(panels, bucket: 0.4, align: 0.3, doubleWallGap: gap, fillMargin: 0.4);
                int cellCount = run.Cells.Count;

                output.WriteLine("");
                output.WriteLine("=== gap={0}: cells={1} ===", gap, cellCount);

                if (gap == 0.47) { cellsAt047 = cellCount; }
                if (gap == 0.5) { cellsAt05 = cellCount; }

                if (gap == 0.47)
                {
                    nearMissAt047 = run.ExtendDiagnostics.Any(d =>
                        d.Contains("near-miss", StringComparison.OrdinalIgnoreCase));
                    output.WriteLine("  near-miss diagnostic: {0}", nearMissAt047);
                    if (nearMissAt047)
                    {
                        foreach (string d in run.ExtendDiagnostics.Where(d =>
                            d.Contains("near-miss", StringComparison.OrdinalIgnoreCase)))
                            output.WriteLine("    {0}", d);
                    }
                }

                DumpProbeCells(run, string.Format("gap={0}", gap));
                run.Dispose();
            }

            // Assert: fewer cells at gap=0.5 than at 0.47 (a pair merges).
            Assert.True(cellsAt05 < cellsAt047,
                string.Format("Gap 0.5 should reduce cells vs 0.47: {0} < {1}", cellsAt05, cellsAt047));

            // Assert: near-miss emitted at gap=0.47.
            Assert.True(nearMissAt047, "Gap=0.47 must emit a consolidation near-miss diagnostic.");
        }

        // ---------------------------------------------------------------------------------------
        // H. Stamped BucketSize on one N-S wall makes it participate in consolidation even
        //    with global doubleWallGap=0.
        // ---------------------------------------------------------------------------------------
        [SkippableFact]
        public void Towers_NorthStripPair_StampedBucket_MergesWithGlobalOff()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            var panels = LoadPanels(Path.Combine(FixturesDirectory, "whole-level-towers.sam"));
            Assert.NotEmpty(panels);

            // The known 0.474 m pair: tower east face at x≈-0.3449, north-strip west wall at x≈+0.1289.
            Panel towerFace = FindNSPanelAt(panels, targetX: -0.3449);
            Panel stripWall = FindNSPanelAt(panels, targetX: +0.1289);
            Assert.NotNull(towerFace);
            Assert.NotNull(stripWall);

            output.WriteLine("Tower east face: Origin.X={0:F4} area={1:0.##}",
                towerFace.GetFace3D()?.GetPlane()?.Origin?.X ?? 0,
                towerFace.GetFace3D()?.GetArea() ?? 0);
            output.WriteLine("Strip west wall: Origin.X={0:F4} area={1:0.##}",
                stripWall.GetFace3D()?.GetPlane()?.Origin?.X ?? 0,
                stripWall.GetFace3D()?.GetArea() ?? 0);

            // Stamp BucketSize=0.5 on the strip west wall (one-sided capture).
            stripWall.SetValue(SolverParameter.BucketSize, 0.5);
            output.WriteLine("Stamped BucketSize=0.5 on strip west wall.");

            // Run with gap=0, stamped panel.
            var run = RunUserChain(panels, bucket: 0.4, align: 0.3, doubleWallGap: 0.0, fillMargin: 0.4);

            // _INPUT_OVERRIDDEN diagnostic.
            bool inputOverridden = run.ExtendDiagnostics.Any(d =>
                d.StartsWith("SAM_OCCT_EXTEND3D_INPUT_OVERRIDDEN", StringComparison.OrdinalIgnoreCase));
            output.WriteLine("_INPUT_OVERRIDDEN: {0}", inputOverridden);
            if (inputOverridden)
            {
                foreach (string d in run.ExtendDiagnostics.Where(d =>
                    d.StartsWith("SAM_OCCT_EXTEND3D_INPUT_OVERRIDDEN", StringComparison.OrdinalIgnoreCase)))
                    output.WriteLine("  {0}", d);
            }

            // stack-consolidated diagnostic.
            bool stackConsolidated = run.ExtendDiagnostics.Any(d =>
                d.IndexOf("stack-consolidated", StringComparison.OrdinalIgnoreCase) >= 0
                || d.IndexOf("ConsolidatedStack", StringComparison.OrdinalIgnoreCase) >= 0);
            output.WriteLine("stack-consolidated: {0}", stackConsolidated);

            // Also check formatted diagnostics for ConsolidatedStack enum.
            if (!stackConsolidated)
            {
                foreach (string d in run.ExtendDiagnostics)
                {
                    if (d.Contains("stack", StringComparison.OrdinalIgnoreCase)
                        || d.Contains("consolidat", StringComparison.OrdinalIgnoreCase))
                        output.WriteLine("  [diag] {0}", d);
                }
            }

            Assert.True(inputOverridden, "Stamped run must emit _INPUT_OVERRIDDEN diagnostic.");
            Assert.True(stackConsolidated, "Stamped run must emit stack-consolidated diagnostic.");

            run.Dispose();
        }

        /// <summary>
        /// Plane-X values (distinct, clustered within 0.05 m) of N-S walls (normal mostly X)
        /// in a Y zone. Used to count distinct wall planes.</summary>
        private static List<double> NsWallPlaneXs(List<Panel> panels, double yMin, double yMax)
        {
            var raw = new List<double>();
            foreach (var p in panels ?? new List<Panel>())
            {
                if (p?.PanelType != PanelType.Wall) continue;
                var plane = p.GetFace3D()?.GetPlane();
                if (plane == null) continue;
                var n = plane.Normal?.Unit;
                if (n == null || System.Math.Abs(n.X) < 0.7) continue;
                var c = p.GetFace3D()?.GetBoundingBox()?.GetCentroid();
                if (c == null || c.Y < yMin || c.Y > yMax) continue;
                double planeX = plane.Origin.X;
                raw.Add(planeX);
            }
            raw.Sort();
            var clustered = new List<double>();
            foreach (double x in raw)
            {
                if (clustered.Count == 0 || System.Math.Abs(x - clustered[clustered.Count - 1]) > 0.05)
                    clustered.Add(x);
            }
            return clustered;
        }

        /// <summary>Finds the closest near-parallel N-S wall pair in a Y zone.</summary>
        private static (Panel panelA, double planeA, Panel panelB, double planeB, double separation)
            NearestParallelNsPair(List<Panel> panels, double yMin, double yMax)
        {
            var entries = new List<(Panel panel, double planeX)>();
            foreach (var p in panels ?? new List<Panel>())
            {
                if (p?.PanelType != PanelType.Wall) continue;
                var plane = p.GetFace3D()?.GetPlane();
                if (plane == null) continue;
                var n = plane.Normal?.Unit;
                if (n == null || System.Math.Abs(n.X) < 0.7) continue;
                var c = p.GetFace3D()?.GetBoundingBox()?.GetCentroid();
                if (c == null || c.Y < yMin || c.Y > yMax) continue;
                double planeX = plane.Origin.X;
                entries.Add((p, planeX));
            }

            double bestSep = double.MaxValue;
            Panel bestA = null, bestB = null;
            double bestXA = 0, bestXB = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                for (int j = i + 1; j < entries.Count; j++)
                {
                    double sep = System.Math.Abs(entries[i].planeX - entries[j].planeX);
                    // Skip near-coplanar pairs (same plane, different segments) — consolidateWallStacks
                    // only acts on genuinely separated walls.
                    if (sep > 0.1 && sep < bestSep)
                    {
                        bestSep = sep;
                        bestA = entries[i].panel;
                        bestB = entries[j].panel;
                        bestXA = entries[i].planeX;
                        bestXB = entries[j].planeX;
                    }
                }
            }
            return (bestA, bestXA, bestB, bestXB, bestSep);
        }

        private void DumpNsWallPlanes(List<Panel> panels, string label, double yMin, double yMax)
        {
            var planes = NsWallPlaneXs(panels, yMin, yMax);
            output.WriteLine("  {0} N-S wall planes (y in [{1},{2}]): {3}",
                label, yMin, yMax, string.Join(", ", planes.Select(x => x.ToString("F4"))));
        }

        private static Panel FindNSPanelAt(List<Panel> panels, double targetX)
        {
            Panel best = null;
            double bestDist = double.MaxValue;
            foreach (var p in panels ?? new List<Panel>())
            {
                if (p?.PanelType != PanelType.Wall) continue;
                var plane = p.GetFace3D()?.GetPlane();
                if (plane == null) continue;
                var n = plane.Normal?.Unit;
                if (n == null || System.Math.Abs(n.X) < 0.7) continue;
                double dist = System.Math.Abs(plane.Origin.X - targetX);
                if (dist < bestDist && dist < 0.2)
                {
                    bestDist = dist;
                    best = p;
                }
            }
            return best;
        }

        private void DumpProbeCells(ChainRun run, string label)
        {
            var cells = run.Cells ?? new List<OcctCell>();
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                double cx = c.Center?.X ?? 0;
                double cy = c.Center?.Y ?? 0;
                // Cells near the 0.474 m pair zone: x between -6 and +4, y near -23.
                if (cx > -6 && cx < 4 && cy > -27 && cy < -18 && c.Volume > 0.5)
                {
                    output.WriteLine("  [{0}] {1} vol={2,10:0.####} center=({3,10:F6},{4,10:F6},{5,7:F3})",
                        i, label, c.Volume, cx, cy, c.Center?.Z ?? 0);
                }
            }
        }
    }
}
