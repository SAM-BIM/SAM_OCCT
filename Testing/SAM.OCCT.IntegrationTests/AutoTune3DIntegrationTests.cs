// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Phase 5e native fixtures for <see cref="AutoTune3DSolver"/> (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md
    /// §E/§J; owner engagement refinement 2026-07-03), run end-to-end through the real OCCT kernel. AutoTune
    /// engages when the baseline leaves naked edges OR closes only by fabricating GapFill/HoleFill patches, and
    /// escalates the implicated walls' <c>MaxExtend</c> to close/replace them with measured extension.
    /// <list type="bullet">
    /// <item><b>GappyRoom</b> - the baseline closes a short-wall gap by fabricating patches; AutoTune replaces
    /// them with real extension (fabricated patch count -&gt; 0), engaging because patches were present.</item>
    /// <item><b>UnclosableGap</b> - the gap is wider than the ladder reach; AutoTune cannot reduce the fabricated
    /// patches, so the round is rejected and it stops boundedly with residual diagnostics (best-effort, no throw).</item>
    /// <item><b>WideShaft</b> - a 0.35 m shaft/partition survives the AutoTune solve (both skins kept, not fused).</item>
    /// <item><b>WatertightBaseline</b> - a cleanly-closed baseline (no naked, no fabrication) does not engage AutoTune.</item>
    /// </list>
    /// The escalation state machine and acceptance gates are unit-tested without native in <c>AutoTune3DTests</c>.
    /// All tests auto-skip when the native library is absent.
    /// </summary>
    public class AutoTune3DIntegrationTests
    {
        /// <summary>A tiny baseline reach, so the managed baseline cannot close the seeded gap by extension and must
        /// either fabricate a patch (which AutoTune then replaces) or leave it - the "escalation reach" guard (§J.1).</summary>
        private const double BaselineReach = 0.02;

        private static OcctBuildOptions Options()
        {
            return new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
        }

        private static Face3D Quad(Point3D a, Point3D b, Point3D c, Point3D d)
        {
            return TestGeometry.CreatePlanarFace(a, b, c, d);
        }

        /// <summary>The six faces of an axis-aligned closed room [x0,x1] x [y0,y1] x [z0,z1] (wall x1 is index 5).</summary>
        private static List<Face3D> ClosedRoom(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            return new List<Face3D>
            {
                Quad(new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x0, y1, z0)), // 0 floor
                Quad(new Point3D(x0, y0, z1), new Point3D(x1, y0, z1), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1)), // 1 ceiling
                Quad(new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y0, z1), new Point3D(x0, y0, z1)), // 2 y0
                Quad(new Point3D(x0, y1, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1)), // 3 y1
                Quad(new Point3D(x0, y0, z0), new Point3D(x0, y1, z0), new Point3D(x0, y1, z1), new Point3D(x0, y0, z1)), // 4 x0
                Quad(new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x1, y0, z1))  // 5 x1
            };
        }

        private static int VerticalFacesNearX(IEnumerable<Face3D> face3Ds, double x, double tolerance)
        {
            int count = 0;
            foreach (Face3D face3D in face3Ds ?? new List<Face3D>())
            {
                Vector3D normal = face3D?.GetPlane()?.Normal?.Unit;
                Point3D centroid = face3D?.GetBoundingBox()?.GetCentroid();
                if (normal != null && centroid != null && System.Math.Abs(normal.X) > 0.99 && System.Math.Abs(centroid.X - x) < tolerance)
                {
                    count++;
                }
            }

            return count;
        }

        private static List<int> EscalatedSourceIndices(AutoTune3DSolver solver, double baseline)
        {
            List<int> result = new List<int>();
            IReadOnlyList<double> maxExtends = solver.MaxExtensions ?? new List<double>();
            for (int i = 0; i < maxExtends.Count; i++)
            {
                if (maxExtends[i] > baseline + 1e-6)
                {
                    result.Add(i);
                }
            }

            return result;
        }

        private static int FabricatedPatchCount(AutoTune3DSolver solver)
        {
            return (solver.HoleFillFace3Ds ?? new List<Face3D>()).Count(x => x != null && x.IsValid() && x.GetArea() > 1e-4);
        }

        // ── 1. GappyRoom: engage on fabricated patches, replace them with measured extension ──────────────

        [SkippableFact]
        public void Execute_GappyRoom_ReplacesFabricatedPatchesWithMeasuredExtension()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a 4x4x3 room whose far wall (index 5) is pulled 0.2 m short of the y=0 wall. At the tiny
            // baseline reach the managed pipeline cannot extend it, so GapFill fabricates patches to close the gap.
            const int perturbedWall = 5;
            List<Face3D> faces = ClosedRoom(0, 0, 0, 4, 4, 3);
            faces[perturbedWall] = Quad(new Point3D(4, 0.2, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(4, 0.2, 3));
            List<double> maxExtends = Enumerable.Repeat(BaselineReach, faces.Count).ToList();

            // Baseline snapshot (MaxRounds = 0 short-circuits the escalation loop): the gap is closed only by
            // fabricated patches.
            AutoTune3DSolver baseline = new AutoTune3DSolver(faces, null, null, maxExtends);
            baseline.Execute(Options(), new AutoTune3DOptions { MaxRounds = 0 });
            Assert.Equal(0, baseline.Rounds);
            Assert.True(FabricatedPatchCount(baseline) > 0, "expected the baseline to close the gap with fabricated patches");

            // Act - full AutoTune.
            AutoTune3DSolver solver = new AutoTune3DSolver(faces, null, null, maxExtends);
            solver.Execute(Options(), new AutoTune3DOptions());

            // Assert - AutoTune engaged (because the baseline fabricated), replaced the patches with measured
            // extension (fabricated count -> 0), stayed watertight, and kept the single room cell, within budget.
            Assert.True(solver.NativeResolved);
            Assert.True(solver.Rounds >= 1 && solver.Rounds <= 3, $"expected 1..3 rounds, got {solver.Rounds}");
            Assert.True(solver.RoundsAccepted >= 1, "expected at least one accepted escalation round");
            Assert.Equal(0, solver.Signature.NakedEdgeCount);
            Assert.Equal(0, FabricatedPatchCount(solver));
            Assert.Equal(1, solver.ResolvedCellCount);

            // The perturbed wall was escalated, and an EscalatedPanel Info diagnostic recorded it.
            Assert.Contains(perturbedWall, EscalatedSourceIndices(solver, BaselineReach));
            Assert.Contains(solver.Diagnostics.OfCode(DiagnosticCode.EscalatedPanel),
                d => d.Severity == OcctDiagnosticSeverity.Info);
        }

        // ── 2. UnclosableGap: no patch reduction -> reject the round and stop boundedly, best-effort ──────

        [SkippableFact]
        public void Execute_UnclosableGap_RejectsRoundAndStopsBoundedlyWithResidualDiagnostics()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - the far wall is only 0.5 m long (short by 3.5 m): the gap far exceeds the ladder reach
            // (1.5 m) and the wall's own length cap, so escalation cannot close it. The baseline fabricates
            // patches to keep it watertight; AutoTune cannot reduce them.
            List<Face3D> faces = ClosedRoom(0, 0, 0, 4, 4, 3);
            faces[5] = Quad(new Point3D(4, 3.5, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(4, 3.5, 3));
            List<double> maxExtends = Enumerable.Repeat(BaselineReach, faces.Count).ToList();

            AutoTune3DSolver baseline = new AutoTune3DSolver(faces, null, null, maxExtends);
            baseline.Execute(Options(), new AutoTune3DOptions { MaxRounds = 0 });
            int baselineFabricated = FabricatedPatchCount(baseline);
            Assert.True(baselineFabricated > 0, "expected the baseline to fabricate patches for the wide gap");

            // Act - must never throw.
            AutoTune3DSolver solver = new AutoTune3DSolver(faces, null, null, maxExtends);
            System.Exception thrown = Record.Exception(() => solver.Execute(Options(), new AutoTune3DOptions()));

            // Assert - best-effort: engaged and tried a round, could not reduce the fabricated patches, so rejected
            // it and stopped boundedly (nothing accepted). A result is returned and the residual is diagnosed.
            Assert.Null(thrown);
            Assert.True(solver.Rounds >= 1 && solver.Rounds <= 3, $"expected 1..3 rounds attempted, got {solver.Rounds}");
            Assert.Equal(0, solver.RoundsAccepted);
            Assert.NotNull(solver.ResolvedFace3Ds);
            Assert.True(FabricatedPatchCount(solver) >= baselineFabricated, "a rejected round must not reduce the fabricated patches");
            // The rejection reason and the residual fabrication are both recorded (never silently dropped).
            Assert.Contains(solver.Diagnostics.OfCode(DiagnosticCode.EscalatedPanel),
                d => d.Severity == OcctDiagnosticSeverity.Warning);
            Assert.NotEmpty(solver.Diagnostics.OfCode(DiagnosticCode.NakedLoop));
        }

        // ── 3. WideShaft: a 0.35 m partition survives the AutoTune solve (not fused/collapsed) ────────────

        [SkippableFact]
        public void Execute_WideShaft_PartitionSurvivesAutoTune()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a room split by a 0.35 m shaft (skins at x=2.0 and x=2.35, wide enough to survive Stage A's
            // opposed-partition collapse and far wider than the capped sew), with the right wall pulled 0.2 m short.
            const double shaftA = 2.0, shaftB = 2.35;
            List<Face3D> faces = new List<Face3D>
            {
                Quad(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 3, 0), new Point3D(0, 3, 0)), // floor
                Quad(new Point3D(0, 0, 3), new Point3D(4, 0, 3), new Point3D(4, 3, 3), new Point3D(0, 3, 3)), // ceiling
                Quad(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 0, 3), new Point3D(0, 0, 3)), // y0
                Quad(new Point3D(0, 3, 0), new Point3D(4, 3, 0), new Point3D(4, 3, 3), new Point3D(0, 3, 3)), // y3
                Quad(new Point3D(0, 0, 0), new Point3D(0, 3, 0), new Point3D(0, 3, 3), new Point3D(0, 0, 3)), // x0
                Quad(new Point3D(4, 0.2, 0), new Point3D(4, 3, 0), new Point3D(4, 3, 3), new Point3D(4, 0.2, 3)), // x4 SHORT
                Quad(new Point3D(shaftA, 0, 0), new Point3D(shaftA, 3, 0), new Point3D(shaftA, 3, 3), new Point3D(shaftA, 0, 3)), // shaft skin A
                Quad(new Point3D(shaftB, 0, 0), new Point3D(shaftB, 3, 0), new Point3D(shaftB, 3, 3), new Point3D(shaftB, 0, 3))  // shaft skin B
            };

            AutoTune3DSolver solver = new AutoTune3DSolver(faces, null, null, Enumerable.Repeat(BaselineReach, faces.Count).ToList());

            // Act
            System.Exception thrown = Record.Exception(() => solver.Execute(Options(), new AutoTune3DOptions()));

            // Assert - the room closed and the partition survived: both parallel skins are still present (the shaft
            // was neither fused by the capped sew nor collapsed by the snap), giving at least two cells.
            Assert.Null(thrown);
            Assert.True(solver.NativeResolved);
            Assert.Equal(0, solver.Signature.NakedEdgeCount);
            Assert.True(solver.ResolvedCellCount >= 2, $"expected >= 2 cells (partitioned room), got {solver.ResolvedCellCount}");
            Assert.True(VerticalFacesNearX(solver.ResolvedFace3Ds, shaftA, 0.02) >= 1, "shaft skin A (x=2.00) missing - the partition was fused/collapsed");
            Assert.True(VerticalFacesNearX(solver.ResolvedFace3Ds, shaftB, 0.02) >= 1, "shaft skin B (x=2.35) missing - the partition was fused/collapsed");
        }

        // ── 4. WatertightBaseline: a cleanly-closed baseline is returned unchanged, no escalation ─────────

        [SkippableFact]
        public void Execute_WatertightBaseline_ReturnsBaselineWithoutEscalating()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a single watertight room, closed by real geometry (no naked edges, no fabricated patches):
            // AutoTune must engage 0 rounds. This is the golden-master posture - AutoTune never disturbs a solve
            // that closed cleanly without fabrication (owner required behaviour 4).
            List<Face3D> faces = ClosedRoom(0, 0, 0, 4, 4, 3);
            AutoTune3DSolver solver = new AutoTune3DSolver(faces, null, null, Enumerable.Repeat(BaselineReach, faces.Count).ToList());

            // Act
            solver.Execute(Options(), new AutoTune3DOptions());

            // Assert
            Assert.True(solver.NativeResolved);
            Assert.NotNull(solver.Signature);
            Assert.Equal(0, solver.Signature.NakedEdgeCount);
            Assert.Equal(0, FabricatedPatchCount(solver));
            Assert.Equal(1, solver.ResolvedCellCount);
            Assert.Equal(0, solver.Rounds);
            Assert.Equal(0, solver.RoundsAccepted);
            Assert.Empty(EscalatedSourceIndices(solver, BaselineReach));
        }
    }
}
