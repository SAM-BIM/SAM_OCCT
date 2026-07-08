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
    /// Phase 1's headline fixture (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §E): two rooms
    /// whose dividing partition stops short of the ceiling - too undersized for the raw kernel to use
    /// as a cell boundary, so the raw solve leaves a single watertight-but-wrong cell (the two rooms
    /// silently merge) even though the outer envelope has no naked edges. Before Phase 1, the raw gate
    /// (cells >= 1 &amp;&amp; naked == 0) adopted this merged result outright. The hardened gate's dropped-face
    /// ratio check now catches it (the undersized partition bounds no closed cell, so it is "dropped")
    /// and falls through to the managed pipeline, which extends the partition up to the ceiling like
    /// any other wall and correctly separates the two rooms.
    /// </summary>
    public class RawGateMergedCellsIntegrationTests
    {
        /// <summary>4x4 m box, 3 m tall, with a partition at x=2 that stops <paramref name="shortfall"/>
        /// metres short of the z=3 ceiling.</summary>
        private static List<Face3D> UndersizedPartitionBox(double shortfall)
        {
            List<Face3D> box = new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 0, 3), new Point3D(0, 0, 3)), // y=0
                TestGeometry.CreatePlanarFace(new Point3D(0, 4, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(0, 4, 3)), // y=4
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(0, 4, 0), new Point3D(0, 4, 3), new Point3D(0, 0, 3)), // x=0
                TestGeometry.CreatePlanarFace(new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(4, 0, 3)), // x=4
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(0, 4, 0)), // floor
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 3), new Point3D(4, 0, 3), new Point3D(4, 4, 3), new Point3D(0, 4, 3)), // ceiling
            };

            double partitionTop = 3 - shortfall;
            Face3D partition = TestGeometry.CreatePlanarFace(
                new Point3D(2, 0, 0), new Point3D(2, 4, 0), new Point3D(2, 4, partitionTop), new Point3D(2, 0, partitionTop));

            box.Add(partition);
            return box;
        }

        [SkippableFact]
        public void Execute_UndersizedPartitionMergesRoomsOnRawPath_RawGateRejectsAndManagedSeparatesRooms()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // 0.5 m short of a 3 m ceiling is far beyond any sew/fuzzy tolerance - the kernel genuinely
            // cannot use this face as a cell boundary, so it bounds no closed cell (dropped), not just
            // a residual sub-cm gap sewing would bridge.
            List<Face3D> face3Ds = UndersizedPartitionBox(shortfall: 0.5);

            // MaxDroppedRatio is set explicitly here (the plan's originally-proposed 0.10) rather than
            // relying on the calibrated-for-real-fixtures 0.30 default (Panel3DSnapSolver.MaxDroppedRatio):
            // on this minimal 7-face synthetic fixture, one dropped face is already a 1/7 (~14%) ratio -
            // meaningful on a small, targeted model but not a signal a real multi-hundred-face building
            // should be judged against (see the default's XML doc for the calibration data).
            Panel3DSnapSolver solver = new Panel3DSnapSolver(face3Ds) { MaxDroppedRatio = 0.10 };
            solver.Execute(new OcctBuildOptions());

            // The raw gate saw the merged (watertight-but-wrong) single cell, measured the dropped
            // partition against the 10% ceiling, and rejected it - never silently adopting the merge.
            Assert.Contains(solver.Diagnostics.All, d => d.Code == DiagnosticCode.DroppedFace);

            // The managed clean/extend pipeline picks up where raw left off: it extends the undersized
            // partition up to the ceiling like any other wall, closing the gap the raw kernel could not
            // use, so the two rooms now form their own cells instead of merging into one.
            Assert.True(solver.NativeResolved, "Expected the managed fallback to still resolve natively");
            Assert.True(solver.ResolvedCellCount >= 2, $"Expected the managed fallback to separate the two rooms, got {solver.ResolvedCellCount} cell(s)");

            // Phase 5a: the managed pipeline now populates Signature too (previously only the adopted raw
            // path did), because AutoTune3D reads it as its loop condition. The raw attempt was rejected,
            // but the managed result carries a valid signature reflecting the separated rooms.
            Assert.NotNull(solver.Signature);
            Assert.True(solver.Signature.CellCount >= 2, $"Expected the managed signature to reflect >= 2 cells, got {solver.Signature.CellCount}");
        }

        [SkippableFact]
        public void Execute_UndersizedPartitionWithDefaultMaxDroppedRatio_RawGateStillAdoptsOnSmallFixture()
        {
            // Sanity check on the OTHER side of the calibration: with the real-world-calibrated default
            // (0.30), this fixture's ~14% dropped ratio does NOT trip the gate, so the merged (wrong)
            // single-cell result IS adopted - demonstrating why the default cannot be tightened to 0.10
            // globally without the explicit per-model override exercised above.
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Face3D> face3Ds = UndersizedPartitionBox(shortfall: 0.5);

            Panel3DSnapSolver solver = new Panel3DSnapSolver(face3Ds);
            solver.Execute(new OcctBuildOptions());

            Assert.NotNull(solver.Signature); // adopted at the calibrated default
            Assert.Equal(1, solver.ResolvedCellCount); // the two rooms merged into one cell
        }
    }
}
