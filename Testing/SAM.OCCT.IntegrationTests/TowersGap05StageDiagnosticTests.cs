// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
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
    /// PR #61 towers doubleWallGap=0.5 root-cause diagnostics: stage-by-stage vertical-panel tables
    /// around the west north-strip room (centroid ~(1.716, -5.906, 13.765)) that vanishes at gap 0.5.
    /// <para>
    /// Managed-only (Clean3D/Extend3D never call native), so no native gate. Stage A comes from the
    /// real <c>Extend3D</c> run (its <see cref="Solve3DReport.CleanFace3Ds"/> + CleanRecords); Stage B
    /// is replicated sub-stage by sub-stage (ExtendWalls â†’ Extend â†’ Fill) from those clean faces with
    /// the exact Execute re-registration (default bucket/weight, default MaxExtend â€” the towers chain
    /// stamps none) and cross-checked against the real Extend3D output at the end.
    /// </para>
    /// </summary>
    public class TowersGap05StageDiagnosticTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

        // Pipeline parameters fixed by the reproduction recipe.
        private const double Bucket = 0.4;
        private const double Align = 0.3;
        private const double Band = 0.4;   // bucketBetweenLevels
        private const double Fill = 0.4;   // fillMargin
        private const bool DirGrow = false;

        // Region of interest: the west north-strip room plus the tower face plane.
        private const double RegionMinX = -1.2, RegionMaxX = 4.6;
        private const double RegionMinY = -10.6, RegionMaxY = -1.4;

        private static readonly double VerticalAngle = 20 * (System.Math.PI / 180);

        private readonly ITestOutputHelper output;

        public TowersGap05StageDiagnosticTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static List<Panel> LoadPanels()
        {
            string path = Path.Combine(FixturesDirectory, "whole-level-towers.sam");
            var result = new List<Panel>();
            foreach (var o in SAM.Core.Convert.ToSAM(path) ?? new List<IJSAMObject>())
            {
                if (o is AnalyticalModel am) result.AddRange(am.GetPanels() ?? new List<Panel>());
                else if (o is AdjacencyCluster ac) result.AddRange(ac.GetPanels() ?? new List<Panel>());
                else if (o is Panel p) result.Add(p);
            }
            return result;
        }

        private static bool InRegion(BoundingBox3D bb)
        {
            if (bb == null) return false;
            return bb.Min.X <= RegionMaxX && bb.Max.X >= RegionMinX
                && bb.Min.Y <= RegionMaxY && bb.Max.Y >= RegionMinY;
        }

        private static bool IsVerticalFace(Face3D f)
        {
            var n = f?.GetPlane()?.Normal?.Unit;
            if (n == null) return false;
            // Same convention as SnappedPanel.IsVertical(20Â°): normal within 20Â° of horizontal.
            double horizontal = System.Math.Sqrt(n.X * n.X + n.Y * n.Y);
            return System.Math.Abs(System.Math.Atan2(System.Math.Abs(n.Z), horizontal)) <= VerticalAngle;
        }

        private void PrintFaceRow(string tag, int index, Face3D f)
        {
            var bb = f?.GetBoundingBox();
            if (bb == null) return;
            var c = bb.GetCentroid();
            var n = f.GetPlane()?.Normal?.Unit;
            output.WriteLine(
                "  {0} [{1,3}] {2} n=({3,5:F2},{4,5:F2},{5,5:F2}) c=({6,7:F3},{7,8:F3},{8,7:F3}) x=[{9,7:F3},{10,7:F3}] y=[{11,8:F3},{12,8:F3}] z=[{13,6:F3},{14,6:F3}] h={15,6:F3} area={16,8:F3}",
                tag, index, IsVerticalFace(f) ? "WALL" : "CAP ",
                n?.X ?? 0, n?.Y ?? 0, n?.Z ?? 0,
                c.X, c.Y, c.Z,
                bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y, bb.Min.Z, bb.Max.Z,
                bb.Max.Z - bb.Min.Z, f.GetArea());
        }

        private void PrintStage(string title, IReadOnlyList<Face3D> faces)
        {
            output.WriteLine("");
            output.WriteLine("â”€â”€â”€â”€ {0} â”€â”€â”€â”€", title);
            for (int i = 0; i < (faces?.Count ?? 0); i++)
            {
                var bb = faces[i]?.GetBoundingBox();
                if (bb == null || !InRegion(bb)) continue;
                PrintFaceRow("", i, faces[i]);
            }
        }

        private void PrintStagePanels(string title, List<SnappedPanel> panels)
        {
            output.WriteLine("");
            output.WriteLine("â”€â”€â”€â”€ {0} â”€â”€â”€â”€", title);
            for (int i = 0; i < (panels?.Count ?? 0); i++)
            {
                var f = panels[i]?.Face3D;
                var bb = f?.GetBoundingBox();
                if (bb == null || !InRegion(bb)) continue;
                PrintFaceRow("", i, f);
            }
        }

        /// <summary>Replicates Panel3DSnapSolver.CapZAtPlan: the cap plane's Z over plan point (x, y).</summary>
        private static double CapZAtPlan(Plane capPlane, double x, double y, double fallback)
        {
            var normal = capPlane?.Normal?.Unit;
            var origin = capPlane?.Origin;
            if (normal == null || origin == null || System.Math.Abs(normal.Z) <= 1e-9) return fallback;
            return origin.Z - (normal.X * (x - origin.X) + normal.Y * (y - origin.Y)) / normal.Z;
        }

        /// <summary>Replicates Panel3DSnapSolver.OverlapsInPlan (bbox intersect with tolerance).</summary>
        private static bool OverlapsInPlan(BoundingBox3D a, BoundingBox3D b, double tolerance)
        {
            return a.Min.X <= b.Max.X + tolerance && a.Max.X >= b.Min.X - tolerance
                && a.Min.Y <= b.Max.Y + tolerance && a.Max.Y >= b.Min.Y - tolerance;
        }

        /// <summary>For one wall, prints every cap the NearestCoveringCap scan would consider (plan
        /// overlap + surface side), and which one wins in each direction.</summary>
        private void PrintCapCandidates(string wallTag, SnappedPanel wall, List<SnappedPanel> panels)
        {
            var wallBox = wall?.GetBoundingBox();
            if (wallBox == null) return;
            double cx = 0.5 * (wallBox.Min.X + wallBox.Max.X);
            double cy = 0.5 * (wallBox.Min.Y + wallBox.Max.Y);
            double tol = Tolerance.Distance;

            output.WriteLine("");
            output.WriteLine("  CAP CANDIDATES for {0}: wall x=[{1:F3},{2:F3}] y=[{3:F3},{4:F3}] z=[{5:F3},{6:F3}] centre=({7:F3},{8:F3})",
                wallTag, wallBox.Min.X, wallBox.Max.X, wallBox.Min.Y, wallBox.Max.Y, wallBox.Min.Z, wallBox.Max.Z, cx, cy);

            for (int i = 0; i < panels.Count; i++)
            {
                var p = panels[i];
                if (p == null || ReferenceEquals(p, wall)) continue;
                var bb = p.GetBoundingBox();
                if (bb == null || p.IsVertical(VerticalAngle)) continue;

                bool overlaps = OverlapsInPlan(bb, wallBox, tol);
                double capZ = CapZAtPlan(p.Plane, cx, cy, bb.Max.Z);
                bool upCandidate = overlaps && capZ >= wallBox.Max.Z - tol;
                bool downCandidate = overlaps && capZ <= wallBox.Min.Z + tol;
                if (!overlaps && System.Math.Abs(capZ - wallBox.GetCentroid().Z) > 20) continue; // cut noise

                output.WriteLine("    cap[{0,3}] z@centre={1,7:F3} bbox x=[{2,8:F3},{3,8:F3}] y=[{4,8:F3},{5,8:F3}] z=[{6,6:F2},{7,6:F2}] planOverlap={8,5} upCand={9,5} downCand={10,5}",
                    i, capZ, bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y, bb.Min.Z, bb.Max.Z, overlaps, upCandidate, downCandidate);
            }
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.4)]
        [InlineData(0.5)]
        public void Towers_StageByStage_StripRoomWallTable(double gap)
        {
            var panels = LoadPanels();
            Assert.NotEmpty(panels);

            // â”€â”€ RAW â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // Mirror PrepareInput ordering (non-Air, valid face) so indices match solver sources.
            var sources = new List<Panel>();
            foreach (var panel in panels.Where(x => x != null && x.PanelType != PanelType.Air))
            {
                var f = panel.GetFace3D();
                if (f == null || !f.IsValid()) continue;
                sources.Add(panel);
            }

            output.WriteLine("################ gap={0} ################", gap);
            PrintStage("RAW input (source index = solver source index)",
                sources.Select(x => x.GetFace3D()).ToList());
            output.WriteLine("  raw GUIDs in region:");
            for (int i = 0; i < sources.Count; i++)
            {
                var bb = sources[i].GetFace3D()?.GetBoundingBox();
                if (bb == null || !InRegion(bb)) continue;
                output.WriteLine("    src[{0,3}] guid={1} type={2}", i, sources[i].Guid, sources[i].PanelType);
            }

            // â”€â”€ real Extend3D (Stage A + Stage B), with report â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var extended = panels.Extend3D(out List<string> diags, out Solve3DReport report,
                minBucketSize: Bucket, alignColinearOffset: Align,
                bucketBetweenLevels: Band, fillMargin: Fill, directionalCapGrow: DirGrow,
                doubleWallGap: gap);
            Assert.NotNull(extended);
            Assert.NotNull(report);

            // Level frames / groups (hypothesis 5: level bucketing).
            output.WriteLine("");
            output.WriteLine("â”€â”€â”€â”€ level frames / groups â”€â”€â”€â”€");
            foreach (var lf in report.LevelFrames ?? new List<LevelFrame>())
                output.WriteLine("  frame elev={0:F3}", lf.Elevation);
            foreach (var lg in report.LevelGroups ?? new List<LevelGroup>())
                output.WriteLine("  group datum={0:F3} frames={1}", lg.Elevation, lg.FrameIndices?.Count ?? 0);

            // Stage A output (clean faces; index space = Stage-B panel index = ExtendRecord targetIdx).
            var cleanFaces = report.CleanFace3Ds?.ToList() ?? new List<Face3D>();
            PrintStage("CLEAN (Stage A output)", cleanFaces);

            // Stage-A mutation records for region sources.
            output.WriteLine("");
            output.WriteLine("â”€â”€â”€â”€ CleanRecords (region sources) â”€â”€â”€â”€");
            var regionSources = new HashSet<int>();
            for (int i = 0; i < sources.Count; i++)
            {
                var bb = sources[i].GetFace3D()?.GetBoundingBox();
                if (bb != null && InRegion(bb)) regionSources.Add(i);
            }
            foreach (var r in report.CleanRecords ?? new List<CleanRecord>())
            {
                if (!regionSources.Contains(r.SourceIndex) && !regionSources.Contains(r.BackerSourceIndex)) continue;
                output.WriteLine("  {0} src={1} backer={2} moved={3:F4}", r.KindText(), r.SourceIndex, r.BackerSourceIndex, r.DistanceMoved);
            }

            // â”€â”€ Stage-B replication with per-sub-stage snapshots â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // Exact Execute re-registration: default bucket/weight; towers stamps no MaxExtend, so
            // every clean face carries DEFAULT_MaxExtension.
            var panelsB = new List<SnappedPanel>();
            for (int i = 0; i < cleanFaces.Count; i++)
            {
                panelsB.Add(new SnappedPanel(i, cleanFaces[i], Panel3DSnapSolver.DEFAULT_Weight,
                    Panel3DSnapSolver.DEFAULT_BucketSize, Panel3DSnapSolver.DEFAULT_MaxExtension));
            }

            var tol = new ToleranceBudget();
            var records = new List<ExtendRecord>();

            PrintStagePanels("B0: re-registered clean panels (pre-conditioning)", panelsB);

            Panel3DSnapSolver.ExtendWalls(panelsB, tol.VerticalAngle, 0.05, tol.Distance, records);
            PrintStagePanels("B1: after ExtendWalls (plan loop)", panelsB);

            // Cap-candidate audit for the strip-room walls BEFORE the vertical extend mutates them.
            output.WriteLine("");
            output.WriteLine("â”€â”€â”€â”€ cap-candidate audit (pre wall-to-cap) â”€â”€â”€â”€");
            for (int i = 0; i < panelsB.Count; i++)
            {
                var p = panelsB[i];
                var bb = p?.Face3D?.GetBoundingBox();
                if (bb == null || !InRegion(bb) || !p.IsVertical(VerticalAngle)) continue;
                var c = bb.GetCentroid();
                // Only the strip-room boundary walls: near y=-2.26 / y=-9.41 E-W, near x=0.13 / x=-0.345 / x=3.7 N-S.
                bool interesting =
                    (System.Math.Abs(c.Y - (-2.26)) < 0.6 || System.Math.Abs(c.Y - (-9.414)) < 0.6) ||
                    (System.Math.Abs(c.X - 0.129) < 0.35 || System.Math.Abs(c.X - (-0.345)) < 0.25 || System.Math.Abs(c.X - 3.71) < 0.35);
                if (!interesting) continue;
                PrintCapCandidates(string.Format("panelB[{0}] c=({1:F3},{2:F3},{3:F3})", i, c.X, c.Y, c.Z), p, panelsB);
            }

            Panel3DSnapSolver.Extend(panelsB, tol.VerticalAngle, 0.05, tol.Distance, 0.5, true, records);
            PrintStagePanels("B2: after Extend (wall-to-cap)", panelsB);

            Panel3DSnapSolver.Fill(panelsB, tol.VerticalAngle, Fill, tol.Distance, 0.05, records, DirGrow);
            PrintStagePanels("B3: after Fill (cap-to-wall)", panelsB);

            // Extend records for region panels (replicated Stage B).
            output.WriteLine("");
            output.WriteLine("â”€â”€â”€â”€ replicated ExtendRecords (region) â”€â”€â”€â”€");
            foreach (var rec in records)
            {
                int idx = rec.PanelIndex;
                if (idx < 0 || idx >= panelsB.Count) continue;
                var bb = panelsB[idx]?.Face3D?.GetBoundingBox();
                if (bb == null || !InRegion(bb)) continue;
                output.WriteLine("  panelB[{0,3}] {1,-9} fromâ†’to={2,8:F4}â†’{3,8:F4} targetIdx={4,3} skip={5}",
                    idx, rec.Kind, rec.FromValue, rec.ToValue, rec.TargetPanelIndex, rec.SkipReason);
            }

            // â”€â”€ cross-check: replicated B3 vs the real Extend3D output â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            PrintStage("REAL Extend3D output (cross-check)",
                (extended ?? new List<Panel>()).Select(x => x.GetFace3D()).Where(x => x != null).ToList());

            int replicated = panelsB.Count(p => p?.Face3D?.GetBoundingBox() != null && InRegion(p.Face3D.GetBoundingBox()));
            int real = (extended ?? new List<Panel>()).Count(x => x?.GetFace3D()?.GetBoundingBox() != null && InRegion(x.GetFace3D().GetBoundingBox()));
            output.WriteLine("");
            output.WriteLine("cross-check: region panel count replicated={0} real={1}", replicated, real);
        }
    }
}

