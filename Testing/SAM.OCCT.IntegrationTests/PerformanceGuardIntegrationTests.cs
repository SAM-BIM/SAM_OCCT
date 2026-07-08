// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Phase 5f performance guard (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §J fixture 8 / §K / §M):
    /// runs the ~1,500-face <see cref="BenchmarkFixture.Benchmark1500"/> through <see cref="AutoTune3DSolver"/>
    /// and asserts it completes within a 90 s soft ceiling, with an exact, deterministic closure outcome.
    /// </summary>
    /// <remarks>
    /// This is a PERFORMANCE/SCALING guard only (owner decision 2026-07-04, see <see cref="BenchmarkFixture"/>'s
    /// remarks) - the fixture is watertight by construction and is not expected to escalate any rounds.
    /// Round-forcing, GapFill replacement, residual diagnostics and the bounded-rounds acceptance gate are
    /// exercised at small scale by <c>AutoTune3DIntegrationTests</c> (Phase 5e), which does not need to be
    /// re-proven at 1,500-face scale.
    /// <para>
    /// <b>Skip seam:</b> set <see cref="SkipEnvironmentVariable"/> (any non-empty value) to opt out of running
    /// this test - e.g. on a resource-constrained or shared CI agent where a 90 s test is undesirable. The skip
    /// reason names the variable explicitly so it reads as a deliberate opt-out, distinct from the ordinary
    /// native-missing skip every other integration test in this suite already uses (checked independently, in
    /// either order).
    /// </para>
    /// </remarks>
    public class PerformanceGuardIntegrationTests
    {
        /// <summary>Soft wall-clock ceiling (seconds) for the benchmark solve (docs §J fixture 8 / §M).</summary>
        private const double CeilingSeconds = 90.0;

        /// <summary>Environment variable that opts out of this test (review §K sub-phase 5f skip seam).</summary>
        private const string SkipEnvironmentVariable = "SAM_OCCT_SKIP_PERF";

        private readonly ITestOutputHelper output;

        public PerformanceGuardIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static OcctBuildOptions Options()
        {
            return new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
        }

        [SkippableFact]
        public void AutoTune3D_Benchmark1500_CompletesWithinNinetySecondsWithExactClosure()
        {
            // The skip-by-opt-out check runs (and reports) BEFORE the native-missing check, so the environment
            // variable's intent is visible even in a report gathered from a native-less agent.
            bool skipRequested = !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(SkipEnvironmentVariable));
            output.WriteLine(string.Format("Benchmark1500: {0}={1}.", SkipEnvironmentVariable, skipRequested ? "set (skip requested)" : "not set"));
            Skip.If(skipRequested, string.Format(
                "{0} is set; skipping the ~90 s Benchmark1500 performance guard by explicit opt-out.", SkipEnvironmentVariable));
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            (List<Face3D> face3Ds, int expectedCellCount) = BenchmarkFixture.Benchmark1500();
            output.WriteLine(string.Format("Benchmark1500: {0} face(s), {1} expected cell(s), {2} s soft ceiling.", face3Ds.Count, expectedCellCount, CeilingSeconds));

            AutoTune3DSolver solver = new AutoTune3DSolver(face3Ds);

            Stopwatch stopwatch = Stopwatch.StartNew();
            solver.Execute(Options(), new AutoTune3DOptions());
            stopwatch.Stop();

            double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            output.WriteLine(string.Format(
                "Benchmark1500: elapsed={0:0.0}s; naked={1}; cells={2}; rounds={3} attempted, {4} accepted.",
                elapsedSeconds, solver.Signature?.NakedEdgeCount, solver.ResolvedCellCount, solver.Rounds, solver.RoundsAccepted));

            // Performance: the soft ceiling from the design review (§J fixture 8 / §M).
            Assert.True(elapsedSeconds < CeilingSeconds,
                string.Format("Benchmark1500 exceeded the {0:0} s soft ceiling: took {1:0.0}s.", CeilingSeconds, elapsedSeconds));

            // Closure sanity: every room is independently watertight by construction, so the expected outcome is
            // exact, not a range or a "no worse than" bound.
            Assert.True(solver.NativeResolved, "expected the native resolve to run");
            Assert.NotNull(solver.Signature);
            Assert.Equal(0, solver.Signature.NakedEdgeCount);
            Assert.Equal(expectedCellCount, solver.ResolvedCellCount);

            // A watertight-by-construction fixture should need no escalation - confirming AutoTune3D's
            // already-closed short-circuit (Phase 5e's golden-master-safety posture: AutoTune never touches an
            // already-good solve) costs nothing extra at this scale.
            Assert.Equal(0, solver.Rounds);
            Assert.Equal(0, solver.RoundsAccepted);
        }
    }
}
