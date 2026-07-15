// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// PR #61 permanent golden evidence for the three accepted managed/0.21 signature changes of the
    /// hole-aware Root Cause B2 cap-selection fix. Each golden-master ROW is already pinned numerically in
    /// <see cref="GoldenMasterIntegrationTests"/> (managed band-0 towers = 20 cells; managed-0.21 towers =
    /// 25 cells / 228 faces; managed-0.21 two-level-tilted = 10 cells). This suite adds the GEOMETRIC
    /// CLASSIFICATION those bare counts cannot express — the evidence Sol requires:
    /// <list type="bullet">
    /// <item>the removed towers cell is a cross-storey PHANTOM (absent under the fix; the real room is kept
    /// on both production paths), not a lost room;</item>
    /// <item>the added two-level-tilted cell is the legitimate MIRROR TWIN of an existing room (equal
    /// volume/area/Z, symmetric, non-overlapping, inside the envelope), not a spurious closure;</item>
    /// <item>the towers-0.21 face drop is FACE CLEANUP with no topology change (same cell identities by
    /// sorted volume, same total volume, same adjacency count; only redundant faces removed).</item>
    /// </list>
    /// Decode is the exact <see cref="GoldenMasterIntegrationTests"/> path (Solve3D → Create.Shells), so the
    /// cell counts here are the same states those rows pin. The pre-fix (OLD-state) numbers that cannot be
    /// produced by the current code — the 21st phantom cell, the 231-face count, the 9-cell two-level state —
    /// are recorded in docs/reviews/evidence/PR61_GOLDEN_EVIDENCE_BASE.log (a real run at PR #61 head
    /// 7a677de). No byte-identity is claimed anywhere: no canonical output hash is generated.
    /// </summary>
    public class PR61GoldenEvidenceIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        private readonly ITestOutputHelper output;

        public PR61GoldenEvidenceIntegrationTests(ITestOutputHelper output) => this.output = output;

        // Probe tolerances. AbsTol pins measured volumes/areas; ProbeRadius matches a probe point to its cell.
        private const double AbsTol = 0.05;
        private const double ProbeRadius = 0.45;

        // ── The removed towers phantom (managed band 0): centroid + full fingerprint (BASE log, 21-cell run).
        private const double PhantomX = 39.467, PhantomY = -6.160, PhantomZ = 13.837;

        // ── two-level-tilted mirror twins (managed 0.21): recovered + pre-existing.
        private const double TwinRecoveredX = 43.452, TwinRecoveredY = -3.533, TwinRecoveredZ = 6.143;
        private const double TwinExistingX = 43.452, TwinExistingY = -22.467, TwinExistingZ = 6.143;

        private static List<Panel> LoadPanels(string fixture)
        {
            string path = Path.Combine(FixturesDirectory, fixture);
            List<IJSAMObject> objects = SAM.Core.Convert.ToSAM(path);
            List<Panel> result = new List<Panel>();
            foreach (IJSAMObject o in objects ?? new List<IJSAMObject>())
            {
                switch (o)
                {
                    case AnalyticalModel am: result.AddRange(am.GetPanels() ?? new List<Panel>()); break;
                    case AdjacencyCluster ac: result.AddRange(ac.GetPanels() ?? new List<Panel>()); break;
                    case Panel p: result.Add(p); break;
                }
            }
            return result
                .Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid())
                .ToList();
        }

        /// <summary>The solved-panel faces fed into the cell-complex build — the same face set (and count)
        /// <see cref="GoldenMasterIntegrationTests.CaptureSignature"/> uses; its count is the pinned "face
        /// count".</summary>
        private static List<Face3D> SolvedFaces(List<Panel> solved) => (solved ?? new List<Panel>())
            .Where(x => x != null && x.PanelType != PanelType.Air)
            .Select(x => x.GetFace3D())
            .Where(x => x != null && x.IsValid())
            .ToList();

        private static OcctCellComplexResult Decode(List<Face3D> face3Ds)
        {
            OcctBuildOptions options = new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
            SAM.Geometry.OCCT.Create.Shells(face3Ds, out OcctCellComplexResult result, options);
            return result;
        }

        private static double Dist(OcctCell c, double x, double y, double z)
        {
            double dx = (c.Center?.X ?? double.MaxValue) - x;
            double dy = (c.Center?.Y ?? double.MaxValue) - y;
            double dz = (c.Center?.Z ?? double.MaxValue) - z;
            return System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static OcctCell NearestCell(IReadOnlyList<OcctCell> cells, double x, double y, double z, double radius)
        {
            OcctCell best = null;
            double bestDist = radius;
            foreach (OcctCell c in cells ?? new List<OcctCell>())
            {
                double d = Dist(c, x, y, z);
                if (d < bestDist) { bestDist = d; best = c; }
            }
            return best;
        }

        /// <summary>Cell floor area = summed area of its clearly-downward faces (tilt ≥ 170°, flat floors —
        /// the same metric <see cref="PR61TowersQuantitativeValidationTests"/> uses). When a cell has none —
        /// a rigidly TILTED level whose floor tilts with the frame (tilt ≈ 167°) — it falls back to the area
        /// of the single most-downward face, so tilted rooms remain comparable.</summary>
        private static double CellFloorArea(OcctCell c)
        {
            double flat = 0, mostDownArea = 0, mostDownZ = 0;
            foreach (OcctCellFace f in c.Faces ?? (IReadOnlyList<OcctCellFace>)new List<OcctCellFace>())
            {
                if (double.IsNaN(f.Area)) continue;
                double t = f.Tilt;
                if (!double.IsNaN(t) && t >= 170.0) flat += f.Area;
                Vector3D n = f.Normal?.Unit;
                if (n != null && n.Z < mostDownZ) { mostDownZ = n.Z; mostDownArea = f.Area; }
            }
            return flat > 0 ? flat : mostDownArea;
        }

        private static (double minX, double maxX, double minY, double maxY, double minZ, double maxZ) CellZBox(OcctCell c)
        {
            BoundingBox3D bb = c.Shell?.GetBoundingBox();
            return bb == null
                ? (double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN)
                : (bb.Min.X, bb.Max.X, bb.Min.Y, bb.Max.Y, bb.Min.Z, bb.Max.Z);
        }

        // ────────────────────────────────────────────────────────────────────────────────────────────────
        // (a) whole-level-towers, managed band 0: the removed ≈78.784 m³ cell is a cross-storey phantom.
        //     Golden row: GoldenMasterIntegrationTests managed towers = 20 cells (was 21).
        // ────────────────────────────────────────────────────────────────────────────────────────────────
        [SkippableFact]
        public void TowersManagedBand0_PhantomRemoved_RealRoomKeptOnProductionPaths()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            List<Panel> panels = LoadPanels("whole-level-towers.sam");
            Assert.NotEmpty(panels);

            // NEW managed band-0 state (forced managed pipeline, the golden-pinned diagnostic path).
            List<Panel> managed = panels.Solve3D(out List<Point3D> managedNaked, out _, forceManagedPipeline: true);
            using (OcctCellComplexResult mr = Decode(SolvedFaces(managed)))
            {
                OcctCell phantom = NearestCell(mr.Cells, PhantomX, PhantomY, PhantomZ, ProbeRadius);
                output.WriteLine("[a] managed band 0: cells={0} naked={1} phantomProbe({2:F3},{3:F3},{4:F3})={5}",
                    mr.Cells.Count, managedNaked?.Count ?? 0, PhantomX, PhantomY, PhantomZ,
                    phantom == null ? "ABSENT" : $"PRESENT vol={phantom.Volume:F3}");

                // The golden-pinned NEW state: 20 cells, and the phantom is gone.
                Assert.Equal(20, mr.Cells.Count);
                Assert.Null(phantom); // the ≈78.784 m³ cross-storey closure no longer forms

                // The wall/cap condition that created the false closure: the two podium walls at the east
                // tower junction (plan ≈ (43.68, -9.80)/(43.68, -1.80)) no longer reach the tower's z≈27.49
                // plate they merely bbox-graze — they top out an order of a storey lower. Assert the tallest
                // vertical wall near that junction stays well below the plate (the phantom needed ≈27.5).
                double tallestNearJunction = TallestVerticalWallTopNear(managed, 43.68, -5.8, planRadius: 6.0);
                output.WriteLine("    tallest vertical wall top near east-tower junction = {0:F3} (phantom needed ≈27.49)", tallestNearJunction);
                Assert.True(tallestNearJunction < 16.0,
                    $"Podium walls must not be extended to the z≈27.49 tower plate; tallest={tallestNearJunction:F3}");
            }

            // Real room preserved on production path 1: raw-first (the default, production) path = 31 cells,
            // room present at the phantom's plan location (a DIFFERENT, legitimate cell there).
            List<Panel> raw = panels.Solve3D(out List<Point3D> rawNaked, out _);
            using (OcctCellComplexResult rr = Decode(SolvedFaces(raw)))
            {
                OcctCell room = NearestCell(rr.Cells, PhantomX, PhantomY, 13.765, 1.0);
                output.WriteLine("[a] raw path: cells={0} naked={1} room@({2:F3},{3:F3})={4}",
                    rr.Cells.Count, rawNaked?.Count ?? 0, PhantomX, PhantomY,
                    room == null ? "ABSENT" : $"PRESENT vol={room.Volume:F3} z=[{CellZBox(room).minZ:F3},{CellZBox(room).maxZ:F3}]");
                // The raw-first production path is UNAFFECTED by the managed-path fix (NearestCoveringCap is
                // never reached on raw-first); the room is present and the complex is not collapsed. The exact
                // count is decode-dependent — Create.Shells here yields 32; the Extend3D→AdjacencyCluster GH
                // decode yields 31 (PR61TowersQuantitativeValidationTests). Assert the raw golden's own bound.
                Assert.True(rr.Cells.Count >= 31, $"Raw path must not collapse; cells={rr.Cells.Count}");
                Assert.NotNull(room);
            }

            // Real room preserved on production path 2: the GH band-0.21 managed path = 25 cells, room present.
            List<Panel> managed021 = panels.Solve3D(out _, out _, out _, out _, forceManagedPipeline: true, bucketBetweenLevels: 0.21);
            using (OcctCellComplexResult r21 = Decode(SolvedFaces(managed021)))
            {
                OcctCell room = NearestCell(r21.Cells, PhantomX, PhantomY, 13.765, 1.0);
                output.WriteLine("[a] managed 0.21: cells={0} room@({1:F3},{2:F3})={3}",
                    r21.Cells.Count, PhantomX, PhantomY,
                    room == null ? "ABSENT" : $"PRESENT vol={room.Volume:F3}");
                Assert.Equal(25, r21.Cells.Count);
                Assert.NotNull(room); // the real room survives; only the phantom band-0 closure was removed
                Assert.InRange(room.Volume, 82.696 - AbsTol, 82.696 + AbsTol); // the doc-cited 82.696 m³ room
            }
        }

        // ────────────────────────────────────────────────────────────────────────────────────────────────
        // (b) two-level-tilted, managed 0.21: the added ≈112.009 m³ cell is the legitimate mirror twin.
        //     Golden row: GoldenMasterIntegrationTests managed-0.21 two-level-tilted = 10 cells (was 9).
        // ────────────────────────────────────────────────────────────────────────────────────────────────
        [SkippableFact]
        public void TwoLevelTiltedManaged021_MirrorTwinRecovered()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            List<Panel> panels = LoadPanels("two-level-tilted.sam");
            Assert.NotEmpty(panels);

            (double eMinX, double eMaxX, double eMinY, double eMaxY, double eMinZ, double eMaxZ) env = InputEnvelope(panels);

            List<Panel> managed = panels.Solve3D(out _, out _, out _, out _, forceManagedPipeline: true, bucketBetweenLevels: 0.21);
            using (OcctCellComplexResult mr = Decode(SolvedFaces(managed)))
            {
                Assert.Equal(10, mr.Cells.Count); // the golden-pinned NEW state (was 9)

                OcctCell recovered = NearestCell(mr.Cells, TwinRecoveredX, TwinRecoveredY, TwinRecoveredZ, 0.3);
                OcctCell existing = NearestCell(mr.Cells, TwinExistingX, TwinExistingY, TwinExistingZ, 0.3);
                Assert.NotNull(recovered);
                Assert.NotNull(existing);
                Assert.NotSame(recovered, existing);

                double vR = recovered.Volume, vE = existing.Volume;
                double aR = CellFloorArea(recovered), aE = CellFloorArea(existing);
                var bR = CellZBox(recovered);
                var bE = CellZBox(existing);

                output.WriteLine("[b] two-level-tilted managed 0.21: cells={0}", mr.Cells.Count);
                output.WriteLine("    recovered ({0:F3},{1:F3},{2:F3}) vol={3:F3} area={4:F3} z=[{5:F3},{6:F3}]",
                    recovered.Center.X, recovered.Center.Y, recovered.Center.Z, vR, aR, bR.minZ, bR.maxZ);
                output.WriteLine("    existing  ({0:F3},{1:F3},{2:F3}) vol={3:F3} area={4:F3} z=[{5:F3},{6:F3}]",
                    existing.Center.X, existing.Center.Y, existing.Center.Z, vE, aE, bE.minZ, bE.maxZ);
                output.WriteLine("    envelope y=[{0:F3},{1:F3}] mid={2:F3}", env.eMinY, env.eMaxY, 0.5 * (env.eMinY + env.eMaxY));

                // Equal volume, equal floor area, equal Z interval — the two rooms are the same room mirrored.
                Assert.InRange(vR, vE - AbsTol, vE + AbsTol);
                Assert.InRange(aR, aE - AbsTol, aE + AbsTol);
                Assert.InRange(bR.minZ, bE.minZ - 0.01, bE.minZ + 0.01);
                Assert.InRange(bR.maxZ, bE.maxZ - 0.01, bE.maxZ + 0.01);

                // Same X band (mirror axis is Y), and the two Y-centres are symmetric about the model centreline.
                Assert.InRange(recovered.Center.X, existing.Center.X - AbsTol, existing.Center.X + AbsTol);
                double yMid = 0.5 * (env.eMinY + env.eMaxY);
                Assert.True(System.Math.Abs((recovered.Center.Y + existing.Center.Y) - 2 * yMid) < 0.2,
                    $"Twin Y-centres must be symmetric about the envelope centreline {yMid:F3}; got {recovered.Center.Y:F3}/{existing.Center.Y:F3}");

                // Non-overlap: the two cell bounding boxes are disjoint in Y (distinct rooms, not a double count).
                Assert.True(bR.maxY < bE.minY - AbsTol || bE.maxY < bR.minY - AbsTol,
                    $"Twin bounding boxes must be Y-disjoint; R=[{bR.minY:F3},{bR.maxY:F3}] E=[{bE.minY:F3},{bE.maxY:F3}]");

                // Both inside the input building envelope (a legitimate room, correctly bounded).
                foreach (var b in new[] { bR, bE })
                {
                    Assert.True(b.minX >= env.eMinX - AbsTol && b.maxX <= env.eMaxX + AbsTol
                        && b.minY >= env.eMinY - AbsTol && b.maxY <= env.eMaxY + AbsTol
                        && b.minZ >= env.eMinZ - AbsTol && b.maxZ <= env.eMaxZ + AbsTol,
                        "Twin cell must lie inside the input building envelope.");
                }
            }
        }

        // ────────────────────────────────────────────────────────────────────────────────────────────────
        // (c) whole-level-towers, managed 0.21: 231 → 228 faces is redundant-face cleanup, not topology change.
        //     Golden row: GoldenMasterIntegrationTests managed-0.21 towers = 25 cells / 228 faces / 9281.107 m³.
        // ────────────────────────────────────────────────────────────────────────────────────────────────
        [SkippableFact]
        public void TowersManaged021_FaceCleanupWithoutTopologyChange()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            List<Panel> panels = LoadPanels("whole-level-towers.sam");
            Assert.NotEmpty(panels);

            List<Panel> managed = panels.Solve3D(out _, out _, out _, out _, forceManagedPipeline: true, bucketBetweenLevels: 0.21);
            List<Face3D> faces = SolvedFaces(managed);
            using (OcctCellComplexResult mr = Decode(faces))
            {
                mr.BuildFaceAdjacencies();

                int cellCount = mr.Cells.Count;
                int faceCount = faces.Count; // the pinned "face count" definition (input faces to Create.Shells)
                double totalVolume = mr.Cells.Sum(c => c.Volume);
                int adjacencyCount = mr.FaceAdjacencies.Count;
                List<double> sortedVolumes = mr.Cells.Select(c => c.Volume).OrderBy(v => v).ToList();

                output.WriteLine("[c] towers managed 0.21: cells={0} faces={1} totalVolume={2:F3} adjacencies={3}",
                    cellCount, faceCount, totalVolume, adjacencyCount);
                output.WriteLine("    sorted per-cell volumes (cell-identity witness; identical old↔new at 231↔228 faces):");
                output.WriteLine("    [{0}]", string.Join(", ", sortedVolumes.Select(v => v.ToString("F3", CultureInfo.InvariantCulture))));

                // Topology unchanged old↔new (BASE log shows the same at 231 faces): 25 cells, same total
                // volume, same adjacency count, same sorted per-cell volume set. Only the face count fell 3.
                Assert.Equal(25, cellCount);
                Assert.Equal(228, faceCount); // was 231 — three redundant faces removed
                Assert.True(System.Math.Abs(totalVolume - 9281.107) <= 0.05,
                    $"Total volume must be unchanged (≈9281.107); got {totalVolume:F3}");

                // Per-cell identity witness: pinned sorted volumes (measured on this NEW 228-face state; the
                // BASE log prints the SAME set at 231 faces). NOT a byte-identity claim.
                double[] expectedSortedVolumes = PinnedTowers021SortedVolumes;
                Assert.Equal(expectedSortedVolumes.Length, sortedVolumes.Count);
                for (int i = 0; i < expectedSortedVolumes.Length; i++)
                {
                    Assert.True(System.Math.Abs(sortedVolumes[i] - expectedSortedVolumes[i]) <= AbsTol,
                        $"Cell-identity witness volume [{i}] drifted: expected {expectedSortedVolumes[i]:F3}, got {sortedVolumes[i]:F3}");
                }

                Assert.Equal(PinnedTowers021AdjacencyCount, adjacencyCount);
            }
        }

        // Measured on the NEW 228-face state (see the [c] evidence line and
        // docs/reviews/evidence/PR61_GOLDEN_EVIDENCE_HEAD.log). The BASE log (PR #61 head 7a677de) prints the
        // SAME 25-value set at 231 faces — the cell-identity witness that the 231→228 face drop changes no
        // cell. NOT a byte-identity claim (no canonical output hash is generated).
        private static readonly double[] PinnedTowers021SortedVolumes =
        {
            73.998, 80.817, 82.696, 86.381, 105.259, 114.778, 115.356, 119.577, 124.117, 153.037,
            159.373, 163.261, 174.145, 250.827, 609.444, 609.444, 625.860, 625.860, 625.860, 625.860,
            625.860, 625.860, 625.861, 625.865, 1251.712,
        };
        private const int PinnedTowers021AdjacencyCount = 19;

        // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Tallest top-Z among (near-)vertical wall faces whose plan centre is within
        /// <paramref name="planRadius"/> of (<paramref name="x"/>, <paramref name="y"/>). Used to prove the
        /// podium walls are not extended to a far tower plate.</summary>
        private static double TallestVerticalWallTopNear(List<Panel> solved, double x, double y, double planRadius)
        {
            double best = double.NegativeInfinity;
            foreach (Panel p in solved ?? new List<Panel>())
            {
                Face3D f = p?.GetFace3D();
                Vector3D n = f?.GetPlane()?.Normal?.Unit;
                BoundingBox3D bb = f?.GetBoundingBox();
                if (n == null || bb == null) continue;
                if (System.Math.Abs(n.Z) > 0.2) continue; // not a vertical wall
                Point3D c = bb.GetCentroid();
                double dx = c.X - x, dy = c.Y - y;
                if (dx * dx + dy * dy > planRadius * planRadius) continue;
                if (bb.Max.Z > best) best = bb.Max.Z;
            }
            return best;
        }

        private static (double minX, double maxX, double minY, double maxY, double minZ, double maxZ) InputEnvelope(List<Panel> panels)
        {
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (Panel p in panels ?? new List<Panel>())
            {
                BoundingBox3D bb = p?.GetFace3D()?.GetBoundingBox();
                if (bb == null) continue;
                minX = System.Math.Min(minX, bb.Min.X); maxX = System.Math.Max(maxX, bb.Max.X);
                minY = System.Math.Min(minY, bb.Min.Y); maxY = System.Math.Max(maxY, bb.Max.Y);
                minZ = System.Math.Min(minZ, bb.Min.Z); maxZ = System.Math.Max(maxZ, bb.Max.Z);
            }
            return (minX, maxX, minY, maxY, minZ, maxZ);
        }
    }
}
