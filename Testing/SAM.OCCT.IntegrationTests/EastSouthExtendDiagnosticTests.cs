// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Diagnostic test pinning whether Extend3D is skipping vertical wall-to-cap extension for the East1|South1
    /// corner panels. The hypothesis: <c>NearestCoveringCap</c> requires cap↔wall plan overlap within
    /// <c>toleranceDistance</c> (1e-6 m), so a cap modeled with an edge just short of the wall footprint causes
    /// the wall's top/bottom extend to skip with <c>NoTargetWithinReach</c>. The Fill step (cap growth) runs
    /// AFTER Extend, so the wall never gets a second chance at the now-grown cap.
    /// </summary>
    public class EastSouthExtendDiagnosticTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ControlledWorkflow");

        private readonly ITestOutputHelper output;

        public EastSouthExtendDiagnosticTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>
        /// Runs Clean3D + Extend3D on the isolated EastSouth fixture and dumps every wall that skipped top/bottom
        /// extension with <c>NoTargetWithinReach</c> — the exact signature of a wall that could not find a cap
        /// overlapping it in plan before the Fill step grew the caps.
        /// </summary>
        [SkippableFact]
        public void Extend3D_WallToCapSkips_EastSouthWallsLackCoveringCapBeforeFill()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> panels = SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Panels-EastSouth-Isolated.sam")).OfType<Panel>().ToList();
            Assert.NotEmpty(panels);

            List<Panel> cleaned = panels.Clean3D(out _, out _, bucketBetweenLevels: 0.21);
            Assert.NotNull(cleaned);

            List<Panel> extended = cleaned.Extend3D(out List<string> diagnostics, out _,
                bucketBetweenLevels: 0.21, inputAlreadyClean: true, directionalCapGrow: true);
            Assert.NotNull(extended);

            // --- Part A: Dump all NoTargetWithinReach skips ---

            List<string> noTargetSkips = diagnostics.Where(d => d.StartsWith("SAM_OCCT_EXTEND3D_SKIP:")
                && d.Contains("NoTargetWithinReach")
                && (d.Contains("top:") || d.Contains("bottom:"))).ToList();

            output.WriteLine("=== NoTargetWithinReach skips (top+bottom, {0}) ===", noTargetSkips.Count);
            foreach (string line in noTargetSkips) { output.WriteLine(line); }

            output.WriteLine("");
            output.WriteLine("=== Open wall ends === ({0})", diagnostics.Count(d => d.StartsWith("SAM_OCCT_EXTEND3D_OPEN_ENDS:")));
            foreach (string line in diagnostics.Where(d => d.StartsWith("SAM_OCCT_EXTEND3D_OPEN_ENDS:"))) { output.WriteLine(line); }

            // --- Part B: Manual bbox-based cap coverage analysis for walls that skip top extension ---
            // Classify panels into walls and caps, then for each wall that has no cap above, report why.

            const double verticalNormalZ = 0.342; // sin(20 deg)
            List<Panel> walls = extended.Where(p =>
            {
                Face3D f = p?.GetFace3D();
                Vector3D n = f?.GetPlane()?.Normal?.Unit;
                return n != null && System.Math.Abs(n.Z) <= verticalNormalZ;
            }).ToList();

            List<Panel> caps = extended.Where(p =>
            {
                Face3D f = p?.GetFace3D();
                Vector3D n = f?.GetPlane()?.Normal?.Unit;
                return n != null && System.Math.Abs(n.Z) > verticalNormalZ;
            }).ToList();

            output.WriteLine("");
            output.WriteLine("=== Wall-vs-cap plan overlap analysis ({0} walls, {1} caps) ===", walls.Count, caps.Count);

            foreach (Panel wall in walls)
            {
                BoundingBox3D wallBox = wall.GetFace3D()?.GetBoundingBox();
                if (wallBox == null) continue;

                double wallTop = wallBox.Max.Z;
                double wallBottom = wallBox.Min.Z;

                // Which caps overlap this wall in plan AND sit above it?
                List<Panel> capsAbove = new List<Panel>();
                List<Panel> capsBelow = new List<Panel>();
                foreach (Panel cap in caps)
                {
                    BoundingBox3D capBox = cap.GetFace3D()?.GetBoundingBox();
                    if (capBox == null) continue;
                    double capZ = 0.5 * (capBox.Min.Z + capBox.Max.Z);

                    bool overlapsPlan = wallBox.Min.X <= capBox.Max.X + 0.01 && wallBox.Max.X >= capBox.Min.X - 0.01
                        && wallBox.Min.Y <= capBox.Max.Y + 0.01 && wallBox.Max.Y >= capBox.Min.Y - 0.01;

                    if (!overlapsPlan) continue;

                    if (capZ > wallTop + 0.01) capsAbove.Add(cap);
                    if (capZ < wallBottom - 0.01) capsBelow.Add(cap);
                }

                bool skipsTop = noTargetSkips.Any(s => s.Contains(wall.Guid.ToString()));
                output.WriteLine("  Wall {0} Z=[{1:0.###},{2:0.###}] capsAbove={3} capsBelow={4} topSkip={5}",
                    wall.Guid, wallBottom, wallTop, capsAbove.Count, capsBelow.Count, skipsTop);

                if (capsAbove.Count == 0 && wallTop < 17.0) // walls below top level should have a cap
                {
                    // Report nearest cap distance and overlap details
                    double bestDist = double.MaxValue;
                    Panel bestCap = null;
                    foreach (Panel cap in caps)
                    {
                        BoundingBox3D capBox = cap.GetFace3D()?.GetBoundingBox();
                        if (capBox == null) continue;
                        double capZ = 0.5 * (capBox.Min.Z + capBox.Max.Z);
                        if (capZ <= wallTop + 0.01) continue; // cap not above

                        double dx = System.Math.Max(0, System.Math.Max(wallBox.Min.X - capBox.Max.X, capBox.Min.X - wallBox.Max.X));
                        double dy = System.Math.Max(0, System.Math.Max(wallBox.Min.Y - capBox.Max.Y, capBox.Min.Y - wallBox.Max.Y));
                        double dist = System.Math.Sqrt(dx * dx + dy * dy);
                        if (dist < bestDist) { bestDist = dist; bestCap = cap; }
                    }

                    if (bestCap != null)
                    {
                        BoundingBox3D capBox = bestCap.GetFace3D()?.GetBoundingBox();
                        output.WriteLine("    -> nearest cap above: {0} at Z={1:0.###}; plan gap={2:0.###}m; wall XY=[{3:0.###},{4:0.###}]x[{5:0.###},{6:0.###}]; cap XY=[{7:0.###},{8:0.###}]x[{9:0.###},{10:0.###}]",
                            bestCap.Guid, 0.5 * (capBox.Min.Z + capBox.Max.Z), bestDist,
                            wallBox.Min.X, wallBox.Max.X, wallBox.Min.Y, wallBox.Max.Y,
                            capBox.Min.X, capBox.Max.X, capBox.Min.Y, capBox.Max.Y);
                    }
                    else
                    {
                        output.WriteLine("    -> NO cap above exists at any elevation");
                    }
                }
            }

            // Part C: report which caps exist by level
            output.WriteLine("");
            output.WriteLine("=== Caps by elevation ===");
            foreach (Panel cap in caps)
            {
                BoundingBox3D capBox = cap.GetFace3D()?.GetBoundingBox();
                if (capBox == null) continue;
                double capZ = 0.5 * (capBox.Min.Z + capBox.Max.Z);
                output.WriteLine("  Cap {0} Z={1:0.###} XY=[{2:0.###},{3:0.###}]x[{4:0.###},{5:0.###}]",
                    cap.Guid, capZ, capBox.Min.X, capBox.Max.X, capBox.Min.Y, capBox.Max.Y);
            }

            // Assertion: there ARE skipped walls — these are the East1/South1 walls.
            Assert.True(noTargetSkips.Count > 0,
                "Expected at least some walls to skip cap extension.");
        }

        /// <summary>
        /// Runs the full chain (Clean3D → Extend3D with fillMargin=1.0 directionalCapGrow=false → AdjacencyCluster → SpaceMatcher)
        /// on the isolated EastSouth fixture. After the fillMargin fix, all 3 spaces should match.
        /// </summary>
        [SkippableFact]
        public void FullChain_IsolatedFixture_ReportsCurrentMatchCount()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> panels = SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Panels-EastSouth-Isolated.sam")).OfType<Panel>().ToList();
            List<Space> spaces = SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Spaces-EastSouth-Isolated.sam")).OfType<Space>().ToList();

            List<Panel> cleaned = panels.Clean3D(out _, out _, bucketBetweenLevels: 0.21);
            List<Panel> extended = cleaned.Extend3D(out _, out _, bucketBetweenLevels: 0.21, inputAlreadyClean: true, fillMargin: 1.0, directionalCapGrow: false);

            SpaceMatchOptions options = new SpaceMatchOptions { LevelBand = 0.21, LevelGroupBand = 0.21 };
            ExpectedSpaceSet expectedSpaceSet = ExpectedSpaceSet.Create(spaces, cleaned, options, Array.Empty<Guid>());

            AdjacencyCluster cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(
                expectedSpaceSet.ToSeedSpaces(), extended, out SAM.Geometry.OCCT.OcctCellComplexResult result, new SAM.Core.Log(),
                new SAM.Core.OCCT.OcctBuildOptions { Tolerance = SAM.Core.Tolerance.Distance, FuzzyTolerance = SAM.Core.Tolerance.MacroDistance, AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 });

            List<CellGeometry> cells = CellGeometry.FromComplex(result);
            SpaceMatchReport report = SpaceMatcher.Match(expectedSpaceSet, cells, Array.Empty<Guid>(), cluster, panels);
            foreach (string line in report.ToLines()) output.WriteLine(line);

            int matched = report.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Matched);
            int missing = report.SpaceMatches.Count(x => x.Outcome == SpaceMatchOutcome.Missing);
            output.WriteLine("Matched: {0}, Missing: {1}", matched, missing);

            Assert.Equal(3, matched);
            Assert.Equal(0, missing);
        }
    }
}
