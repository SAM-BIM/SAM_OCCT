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
    /// PR #61 review: Face3D-home parity comparison between base SHA c643b29 and PR head.
    /// <para>
    /// Proven by worktree execution with the identical native DLL
    /// (docs/reviews/evidence/PR61_FACE3D_BASE.log / PR61_FACE3D_HEAD.log):
    /// solver raw cells = 20 and A-solver-matched spaces = 19 at BOTH base and head — the
    /// A-path under-close (19 vs 20, introduced by the E2 plane-targeting change merged at
    /// base c643b29) is pre-existing, not a PR #61 regression.
    /// </para>
    /// <para>
    /// The B-clean-extend workflow is NOT at parity: base yields 20 spaces, head yields 19.
    /// The coplanar-cap coalescing pass added in PR #61 under-closes one Face3D-home space
    /// on the Clean3D → Extend3D → cluster path. Disclosed in the PR body; pinned below so
    /// any further movement is caught.
    /// </para>
    /// </summary>
    public class PR61Face3DHomeParityTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        private readonly ITestOutputHelper output;

        public PR61Face3DHomeParityTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static List<Panel> LoadPanels(string path)
        {
            var r = new List<Panel>();
            foreach (var o in SAM.Core.Convert.ToSAM(path) ?? new List<IJSAMObject>())
            {
                if (o is AnalyticalModel am) r.AddRange(am.GetPanels() ?? new List<Panel>());
                else if (o is AdjacencyCluster ac) r.AddRange(ac.GetPanels() ?? new List<Panel>());
                else if (o is Panel p) r.Add(p);
            }
            return r.Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid()).ToList();
        }

        private static OcctBuildOptions SolverMatchedOptions() =>
            new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };

        /// <summary>
        /// Captures the current Face3D-home state on the PR branch.
        /// <para>
        /// Solver raw path produces the resolved cell count. A-solver-matched workflow rebuilds
        /// from solver panels with solver-matched options. B-clean-extend workflow runs Clean3D →
        /// Extend3D → Create.AdjacencyCluster with band=0.21.
        /// </para>
        /// <para>
        /// Executed at base c643b29 (docs/reviews/evidence/PR61_FACE3D_BASE.log):
        /// solver cells=20, A-solver-matched=19, B-clean-extend=20. PR #61 keeps solver and
        /// A-matched identical and moves B-clean-extend to 19 (coplanar-cap coalescing).
        /// </para>
        /// </summary>
        [SkippableFact]
        public void Face3DHome_Parity_PR_Branch_RecordsCurrentState()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            string path = Path.Combine(FixturesDirectory, "Face3D-home.sam");
            Skip.IfNot(File.Exists(path), "Face3D-home.sam fixture not found.");

            var panels = LoadPanels(path);
            Assert.NotEmpty(panels);

            // Solver raw path
            var solved = panels.Solve3D(out List<Point3D> nakedPoints, out List<string> solveDiags, out _, out Solve3DReport report);
            int solverCells = report.ResolvedCellCount;
            output.WriteLine("Solver raw: cells={0} naked={1}", solverCells, nakedPoints?.Count ?? 0);

            // A-solver-matched: rebuild from solved panels with solver-matched options.
            var nonAir = solved.Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid()).ToList();
            var clusterA = SAM.Analytical.OCCT.Create.AdjacencyCluster(null, nonAir, out var crA, null, SolverMatchedOptions());
            int spacesA = clusterA?.GetSpaces()?.Count ?? 0;
            output.WriteLine("A-solver-matched: spaces={0} (solver cells={1})", spacesA, solverCells);

            // B-clean-extend: Clean3D → Extend3D → Create.AdjacencyCluster (band=0.21).
            var cleaned = panels.Clean3D(out _);
            var extended = cleaned.Extend3D(out _, out _, bucketBetweenLevels: 0.21);
            var nonAirB = extended.Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid()).ToList();
            var clusterB = SAM.Analytical.OCCT.Create.AdjacencyCluster(null, nonAirB, out var crB, null, SolverMatchedOptions());
            int spacesB = clusterB?.GetSpaces()?.Count ?? 0;
            output.WriteLine("B-clean-extend (band=0.21): spaces={0}", spacesB);

            // Pin the solver raw cell count — proven identical at c643b29 base
            // (docs/reviews/evidence/PR61_FACE3D_BASE.log).
            Assert.Equal(20, solverCells);

            // Pin the A-solver-matched under-close — proven pre-existing at c643b29 base.
            Assert.Equal(19, spacesA);

            // Pin the B-clean-extend result. NOT at parity with base: the identical workflow
            // yields 20 spaces at c643b29 and 19 at PR head — the coplanar-cap coalescing pass
            // under-closes one Face3D-home space. Disclosed in the PR body; pinned so any
            // further movement is caught.
            Assert.Equal(19, spacesB);

            output.WriteLine("PR-branch: solver={0} A-matched={1} B-clean-extend={2}",
                solverCells, spacesA, spacesB);

            crA?.Dispose();
            crB?.Dispose();
        }
    }
}
