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
    /// Reusing one <see cref="Panel3DSnapSolver"/> instance across two <see cref="Panel3DSnapSolver.Execute"/>
    /// calls must not leak per-run output from the first call into the second: every reported field is reset
    /// at the top of <c>Execute</c>. <c>ResolveHistorySourceMap</c>/<c>NakedWires</c> were previously missing
    /// from that reset block (every other output field already was reset) - a run that captures exact native
    /// history, followed on the SAME instance by a run that returns early via <c>StopAfterClean</c> (before
    /// the resolve stage that would otherwise set them), used to still report the first run's stale
    /// <c>ResolveHistorySourceMap</c>. Native-gated (needs the real kernel to capture history on the first run).
    /// </summary>
    public class ExecuteResetIntegrationTests
    {
        private static Face3D Face(params Point3D[] points)
        {
            return TestGeometry.CreatePlanarFace(points);
        }

        /// <summary>The same 7-face fixture (unit box + a mid-plane face) <c>HistoryExportIntegrationTests</c>
        /// already proves captures native history on the direct build_cell_complex path.</summary>
        private static List<Face3D> SplitFixture()
        {
            List<Face3D> face3Ds = TestGeometry.CreateUnitBox(0, 0, 0).Face3Ds.ToList(); // 6 faces
            face3Ds.Add(Face(new Point3D(0, 0, 0.5), new Point3D(1, 0, 0.5), new Point3D(1, 1, 0.5), new Point3D(0, 1, 0.5))); // mid, index 6
            return face3Ds;
        }

        private static OcctBuildOptions DirectBuildOptions()
        {
            // SewBeforeBuild = false forces the direct build_cell_complex path that captures history
            // (matching HistoryExportIntegrationTests.DirectBuildOptions); AvoidInternalShapes = false keeps
            // the mid face as a shared internal wall so the resolve has something to attribute history over.
            return new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = false, GlueMode = OcctGlueMode.Off };
        }

        [SkippableFact]
        public void Execute_ReusedInstance_ClearsResolveHistorySourceMapOnEveryCall()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            Panel3DSnapSolver solver = new Panel3DSnapSolver(SplitFixture()) { ForceManagedPipeline = true };

            // First run: the direct-build path captures exact native history - the precondition the reset bug
            // needs to be observable.
            solver.Execute(DirectBuildOptions());
            Assert.NotNull(solver.ResolveHistorySourceMap);

            // Second run, SAME instance: StopAfterClean returns before the resolve stage ever runs, so a
            // correct reset must clear the first run's history map rather than leaving it stale.
            solver.StopAfterClean = true;
            solver.Execute(DirectBuildOptions());

            Assert.Null(solver.ResolveHistorySourceMap);
            Assert.Empty(solver.NakedWires);
        }
    }
}
