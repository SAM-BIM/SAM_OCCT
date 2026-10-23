// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Native;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using Xunit;
using Xunit.Abstractions;
using SolverParameter = SAM.Analytical.Solver.SolverParameter;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// The consolidation follow of the North0 room on the ORIGINAL nine-space fixture (record:
    /// documentation/North0-Consolidation-Follow-Q4.md). When the West3|North0 pair (0.474 m) is captured, North0's
    /// west wall ca160254 consolidates onto the West block's east wall edb39f15 (x 0.129 -&gt; -0.345). Two of its
    /// neighbours must follow inside <see cref="Panel3DSnapSolver.ConsolidateWallStacks"/>: the north wall a577d53d,
    /// whose end an earlier align snap moved 0.046 m past the old foot (y -2.306 -&gt; -2.26), and the roof 770b8239,
    /// whose edge is authored 0.088 m inside the wall. These tests pin both follows at the clean stage, their absence
    /// below the gap, that the neighbouring North1 room is unchanged, and the chain outcome with only the pair stamped.
    /// The full nine-space contract stays in <see cref="NineSpaceWallCaptureIntegrationTests"/>.
    /// </summary>
    public class North0ConsolidationFollowIntegrationTests
    {
        private const string SourceEndMessage = "dragged a source-associated wall end";
        private const string InsetEdgeMessage = "moved an inset cap edge";

        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ControlledWorkflow");

        private static readonly Guid West3Guid = new Guid("02a1ae27-5461-4b41-ad07-008ccd9d1159");

        private static readonly Guid WestBlockEastWall = new Guid("edb39f15-e5a1-4bae-8481-3aec45ed2b1e");

        private static readonly Guid North0WestWall = new Guid("ca160254-72b3-4f3e-987e-a15f5ce15db9");

        private readonly ITestOutputHelper output;

        public North0ConsolidationFollowIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        public static IEnumerable<object[]> AboveGapCases()
        {
            yield return new object[] { 0.55, false };
            yield return new object[] { 0.65, false };
            yield return new object[] { 0.55, true };
        }

        [SkippableTheory]
        [MemberData(nameof(AboveGapCases))]
        public void CleanStage_WestNorthConsolidated_NorthWallEndAndInsetRoofEdgeFollowTheMovedWall(double capture, bool pairOnly)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            List<Panel> input = Stamped(capture, pairOnly);
            Panel westWall = Find(input, "ca160254");
            Panel northWall = Find(input, "a577d53d");
            Panel roof = Find(input, "770b8239");
            double wallMove = westWall.GetFace3D().GetBoundingBox().Min.X - Find(input, "edb39f15").GetFace3D().GetBoundingBox().Min.X;
            BoundingBox3D roofSource = roof.GetFace3D().GetBoundingBox();

            // Act
            input.Clean3D(out List<string> diagnostics, out Solve3DReport report, bucketBetweenLevels: 0.21);
            BoundingBox3D northClean = CleanBox(report, input, northWall);
            BoundingBox3D roofClean = CleanBox(report, input, roof);
            output.WriteLine("north wall " + Format(northClean) + "; roof " + Format(roofClean));

            // Assert: the west wall consolidated by the full 0.474 m (the precondition of both follows).
            Assert.Equal(0.474, wallMove, 3);
            Assert.Contains(report.CleanRecords, x => x.Kind == CleanRecordKind.StackConsolidated && x.SourceIndex == input.IndexOf(westWall) && System.Math.Abs(x.DistanceMoved - wallMove) <= 1e-3);

            // The north wall's west end reached the new plane; its earlier snap onto y -2.26 and its east end stay.
            Assert.Equal(-0.345, northClean.Min.X, 3);
            Assert.Equal(3.657, northClean.Max.X, 3);
            Assert.Equal(-2.26, northClean.Min.Y, 3);
            Assert.Equal(-2.26, northClean.Max.Y, 3);

            // Only the roof's west edge moved, by exactly the wall's move: the authored 0.088 m inset is kept for the fill.
            Assert.Equal(roofSource.Min.X - wallMove, roofClean.Min.X, 3);
            Assert.Equal(roofSource.Max.X, roofClean.Max.X, 3);
            Assert.Equal(roofSource.Min.Y, roofClean.Min.Y, 3);
            Assert.Equal(roofSource.Max.Y, roofClean.Max.Y, 3);

            // Each new follow fired exactly once - for this pair only.
            Assert.Single(diagnostics, x => x.Contains(SourceEndMessage));
            Assert.Single(diagnostics, x => x.Contains(InsetEdgeMessage));

            // Neighbouring North1 keeps the clean geometry of the base (no altered pre-snap weights).
            AssertBox(CleanBox(report, input, Find(input, "e121fb75")), 3.755, 7.283, -9.126, -1.926);
            AssertBox(CleanBox(report, input, Find(input, "d5c1f0bc")), 3.675, 3.737, -9.506, -2.121);
        }

        [SkippableTheory]
        [InlineData(0.45)]
        [InlineData(0.0)]
        public void CleanStage_WestNorthNotCaptured_NoSourceEndOrInsetEdgeFollow(double capture)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange: 0.45 sits below the 0.474 m gap (owner-accepted control); 0 = no stamps.
            List<Panel> input = capture > 0 ? Stamped(capture, false) : Clones();
            Panel northWall = Find(input, "a577d53d");
            Panel roof = Find(input, "770b8239");

            // Act
            input.Clean3D(out List<string> diagnostics, out Solve3DReport report, bucketBetweenLevels: 0.21);

            // Assert: the real 0.474 m void is not consolidated, so neither follow fires anywhere in the model.
            Assert.DoesNotContain(report.CleanRecords, x => x.Kind == CleanRecordKind.StackConsolidated && x.SourceIndex == input.IndexOf(Find(input, "ca160254")));
            Assert.DoesNotContain(diagnostics, x => x.Contains(SourceEndMessage) || x.Contains(InsetEdgeMessage));
            Assert.Equal(0.129, CleanBox(report, input, northWall).Min.X, 3);
            Assert.Equal(roof.GetFace3D().GetBoundingBox().Min.X, CleanBox(report, input, roof).Min.X, 3);
        }

        [SkippableFact]
        public void Chain_OnlyWestNorthPairStamped_KeepsNorth0AndLeavesUnstampedBandUnchanged()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Act
            using (Chain chain = Chain.Run(Stamped(0.55, true)))
            {
                // Assert: North0 is its own cell beside West3 (shared boundary) and North1, both rooms distinct.
                SpaceMatchRecord north0 = chain.Report.SpaceMatches.Single(x => x.Name == "North0");
                SpaceMatchRecord west3 = chain.Report.SpaceMatches.Single(x => x.Name == "West3");
                SpaceMatchRecord north1 = chain.Report.SpaceMatches.Single(x => x.Name == "North1");
                Assert.Equal(SpaceMatchOutcome.Matched, north0.Outcome);
                Assert.Equal(SpaceMatchOutcome.Matched, west3.Outcome);
                Assert.NotEqual(north0.CellIndices.Single(), west3.CellIndices.Single());
                Assert.True(chain.Report.DoubleHeightOk.TryGetValue(West3Guid, out bool doubleHeight) && doubleHeight);
                Assert.Contains("North0|West3", chain.Adjacencies);
                Assert.Contains("North0|North1", chain.Adjacencies);

                // North1 keeps its base cell (80.047 m3; the pre-Clean3D probe edits gave 79.538 m3).
                Assert.Equal(80.047, chain.Result.Cells[north1.CellIndices.Single()].Volume, 2);

                // The unstamped East1|South1 band is untouched: its two known Extra cells remain, same size and place.
                List<CellMatchRecord> extra = chain.Report.CellMatches.Where(x => x.Outcome == SpaceMatchOutcome.Extra).OrderByDescending(x => x.Volume).ToList();
                Assert.Equal(2, extra.Count);
                Assert.Equal(6.573, extra[0].Volume, 2);
                Assert.Equal(0.935, extra[1].Volume, 2);
                Assert.Equal(-23.173, extra[0].Center.Y, 2);
                Assert.Equal(-23.363, extra[1].Center.Y, 2);
                Assert.Empty(OcctUnderSplitGuard.ValidateCells(chain.Result.Cells, chain.Options, System.Math.Max(chain.Options.Tolerance, chain.Options.FuzzyTolerance)));
            }
        }

        private static List<Panel> Clones()
        {
            return SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Panels-9SpacesModel.sam")).OfType<Panel>()
                .Select(x => global::SAM.Analytical.Create.Panel(x)).ToList();
        }

        /// <summary>Fresh clones with <paramref name="capture"/> stamped on every wall, or only on the West3|North0 pair.</summary>
        private static List<Panel> Stamped(double capture, bool pairOnly)
        {
            List<Panel> panels = Clones();
            foreach (Panel panel in panels.Where(IsWall))
            {
                if (!pairOnly || panel.Guid == WestBlockEastWall || panel.Guid == North0WestWall)
                {
                    panel.SetValue(SolverParameter.BucketSize, capture);
                }
            }

            return panels;
        }

        private static Panel Find(List<Panel> panels, string prefix)
        {
            return panels.Single(x => x.Guid.ToString().StartsWith(prefix, StringComparison.Ordinal));
        }

        /// <summary>The bounding box of the clean face(s) the source panel maps to (one face expected).</summary>
        private static BoundingBox3D CleanBox(Solve3DReport report, List<Panel> input, Panel panel)
        {
            IReadOnlyList<FaceKey> faces = report.SourceMap.FacesOf(input.IndexOf(panel));
            return report.CleanFace3Ds[faces.Single().Value].GetBoundingBox();
        }

        private static void AssertBox(BoundingBox3D box, double minX, double maxX, double minY, double maxY)
        {
            Assert.Equal(minX, box.Min.X, 3);
            Assert.Equal(maxX, box.Max.X, 3);
            Assert.Equal(minY, box.Min.Y, 3);
            Assert.Equal(maxY, box.Max.Y, 3);
        }

        private static bool IsWall(Panel panel)
        {
            return System.Math.Abs(panel.GetFace3D().GetPlane().Normal.Unit.Z) < System.Math.Sin(20 * System.Math.PI / 180);
        }

        private static string Format(BoundingBox3D box)
        {
            return string.Format(CultureInfo.InvariantCulture, "x[{0:0.###},{1:0.###}] y[{2:0.###},{3:0.###}]", box.Min.X, box.Max.X, box.Min.Y, box.Max.Y);
        }

        /// <summary>The controlled chain exactly as <see cref="NineSpaceWallCaptureIntegrationTests"/> runs it.</summary>
        private sealed class Chain : IDisposable
        {
            public OcctBuildOptions Options { get; private set; }

            public OcctCellComplexResult Result { get; private set; }

            public SpaceMatchReport Report { get; private set; }

            public List<string> Adjacencies { get; private set; }

            public static Chain Run(List<Panel> input)
            {
                List<Panel> original = SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Panels-9SpacesModel.sam")).OfType<Panel>().ToList();
                List<Space> spaces = SAM.Core.Convert.ToSAM(Path.Combine(FixturesDirectory, "Spaces-9SpacesModel.sam")).OfType<Space>().ToList();
                List<Panel> cleaned = input.Clean3D(out List<string> _, out Solve3DReport _, bucketBetweenLevels: 0.21);
                List<Panel> extended = cleaned.Extend3D(out List<string> _, bucketBetweenLevels: 0.21, inputAlreadyClean: true, directionalCapGrow: true);
                SpaceMatchOptions matchOptions = new SpaceMatchOptions { LevelBand = 0.21, LevelGroupBand = 0.21 };
                ExpectedSpaceSet expected = ExpectedSpaceSet.Create(spaces, cleaned, matchOptions, new[] { West3Guid });

                Chain chain = new Chain
                {
                    Options = new OcctBuildOptions { Tolerance = Tolerance.Distance, FuzzyTolerance = Tolerance.MacroDistance, AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01, MergeCoplanarBeforeBuild = true, ValidateSewCoverage = true },
                };
                AdjacencyCluster cluster = global::SAM.Analytical.OCCT.Create.AdjacencyCluster(expected.ToSeedSpaces(), extended, out OcctCellComplexResult result, new Log(), chain.Options);
                chain.Result = result;
                chain.Report = SpaceMatcher.Match(expected, CellGeometry.FromComplex(result), new[] { West3Guid }, cluster, original);

                Dictionary<int, string> names = chain.Report.CellMatches.ToDictionary(
                    x => x.CellIndex,
                    x => x.Outcome == SpaceMatchOutcome.Extra ? "Extra" + x.CellIndex.ToString(CultureInfo.InvariantCulture) : string.Join("+", x.SpaceGuids.Select(g => expected.TryGetSpace(g)?.Name).OrderBy(n => n, StringComparer.Ordinal)));
                chain.Adjacencies = (result.FaceAdjacencies ?? new List<OcctCellFaceAdjacency>())
                    .Where(x => x.CellIndex1 != x.CellIndex2 && names.ContainsKey(x.CellIndex1) && names.ContainsKey(x.CellIndex2))
                    .Select(x => string.Join("|", new[] { names[x.CellIndex1], names[x.CellIndex2] }.OrderBy(n => n, StringComparer.Ordinal)))
                    .Distinct()
                    .ToList();
                return chain;
            }

            public void Dispose()
            {
                Result?.Dispose();
            }
        }
    }
}
