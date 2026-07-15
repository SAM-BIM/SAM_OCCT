// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Golden-master lock (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md, Phase 0): freezes the
    /// CURRENT <see cref="Modify.Solve3D"/> closure signature - cell count, total volume, naked-edge
    /// count - on both the raw-first path (the default, exercised by every other solver test) and
    /// the managed clean/extend/resolve path (forced via <c>forceManagedPipeline</c>, see
    /// <see cref="Panel3DSnapSolver.ForceManagedPipeline"/>) for the five reference fixtures. This is
    /// a tripwire, not a correctness assertion about which path is "better": a later phase that
    /// changes either pipeline's closure must update the expected values here and state the delta
    /// in its PR (the golden-master contract in TESTING.md).
    /// </summary>
    public class GoldenMasterIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

        private readonly ITestOutputHelper output;

        public GoldenMasterIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static List<Panel> LoadPanels(string path)
        {
            List<IJSAMObject> objects = SAM.Core.Convert.ToSAM(path);
            List<Panel> result = new List<Panel>();
            foreach (IJSAMObject sAMObject in objects ?? new List<IJSAMObject>())
            {
                switch (sAMObject)
                {
                    case AnalyticalModel analyticalModel:
                        result.AddRange(analyticalModel.GetPanels() ?? new List<Panel>());
                        break;
                    case AdjacencyCluster adjacencyCluster:
                        result.AddRange(adjacencyCluster.GetPanels() ?? new List<Panel>());
                        break;
                    case Panel panel:
                        result.Add(panel);
                        break;
                }
            }

            return result
                .Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid())
                .ToList();
        }

        /// <summary>
        /// Independently decodes a <see cref="Modify.Solve3D"/> result back into a zoned cell complex
        /// (<c>AvoidInternalShapes = false</c>, matching the solver's own internal resolve options)
        /// purely to read cell count/volumes for the signature - reusing the existing cell metadata
        /// (<c>OcctCellComplexResult</c>/<c>sam_occt_result_cell_*</c>), not a new native operation.
        /// The naked-edge count is the one the solver itself already validated, not re-measured here.
        /// </summary>
        private static ClosureSignature3D CaptureSignature(List<Panel> solved, List<Point3D> nakedPoint3Ds)
        {
            List<Face3D> face3Ds = (solved ?? new List<Panel>())
                .Where(x => x != null && x.PanelType != PanelType.Air)
                .Select(x => x.GetFace3D())
                .Where(x => x != null && x.IsValid())
                .ToList();

            OcctBuildOptions options = new OcctBuildOptions
            {
                AvoidInternalShapes = false,
                SewBeforeBuild = true,
                SewingTolerance = 0.01
            };

            SAM.Geometry.OCCT.Create.Shells(face3Ds, out OcctCellComplexResult result, options);
            try
            {
                return ClosureSignature3D.FromCellComplexResult(result, nakedPoint3Ds?.Count ?? 0, face3Ds.Count);
            }
            finally
            {
                result?.Dispose();
            }
        }

        public static IEnumerable<object[]> Fixtures()
        {
            // fixture, minimum cell count, maximum naked-edge count - the raw-first path's own
            // documented targets (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §E Phase 0),
            // already asserted individually by FlatSolveIntegrationTests/TiltedSolveIntegrationTests.
            yield return new object[] { "whole-level-flat.sam", 22, 0 };
            yield return new object[] { "tilted-two-spaces.sam", 2, 0 };
            yield return new object[] { "whole-level-tilted.sam", 22, 0 };
            yield return new object[] { "two-level-tilted.sam", 40, 0 };
            yield return new object[] { "whole-level-towers.sam", 31, 0 };
        }

        [SkippableTheory]
        [MemberData(nameof(Fixtures))]
        public void Solve3D_RawPath_ClosureSignatureMatchesGoldenMaster(string fixture, int minCellCount, int maxNakedEdgeCount)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, fixture);
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> panels = LoadPanels(path);
            Assert.NotEmpty(panels);

            List<Panel> solved = panels.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> diagnostics);
            Assert.NotNull(solved);
            Assert.NotEmpty(solved);

            foreach (string d in diagnostics ?? new List<string>()) { output.WriteLine(d); }

            ClosureSignature3D signature = CaptureSignature(solved, nakedPoint3Ds);
            output.WriteLine(string.Format("{0} [raw]: {1}", fixture, signature));

            Assert.True(signature.CellCount >= minCellCount, string.Format("{0}: expected >= {1} cell(s), got {2}", fixture, minCellCount, signature.CellCount));
            Assert.True(signature.NakedEdgeCount <= maxNakedEdgeCount, string.Format("{0}: expected <= {1} naked edge(s), got {2}", fixture, maxNakedEdgeCount, signature.NakedEdgeCount));
        }

        /// <summary>
        /// Managed-path baseline: fixture, expected cell count, expected naked-edge count, expected total
        /// volume (m3). Cell/naked counts are exact; volume is asserted within
        /// <see cref="Core.Tolerance.MacroDistance"/>, so harmless floating-point noise does not fail the
        /// test while any real drift still does. This is a tripwire on the managed conditioning pipeline,
        /// NOT a correctness claim about which path is better; the golden-master contract (TESTING.md) is
        /// that a phase legitimately changing this pipeline updates these values with a stated delta.
        ///
        /// E1 re-baseline (docs/EXTEND3D_ROBUST_HANDOVER.md, PR #48): the SnappedPanel extend/trim
        /// primitives were rebuilt as profile-preserving plane-ops (walls no longer collapse or verticalize).
        /// The RAW golden master and the production raw-first path are byte-identical (they never call these
        /// primitives). Two managed pins are unchanged (whole-level-flat, tilted-two-spaces). Three moved
        /// and are re-baselined here (per-fixture mechanism table in TESTING.md "E1" section):
        ///   - whole-level-tilted: 22c/0n unchanged; volume 3377.828 -> 3377.841 (+0.0004%) because tilted
        ///     walls now keep their true plane instead of being verticalized (R5) by the old straight-up
        ///     re-extrude.
        ///   - whole-level-towers: 22c/12n -> 21c/8n (naked improved 12 -> 8): sloped/non-rectangular walls
        ///     that the old re-extrude collapsed to slivers now extend to their full profile.
        ///   - two-level-tilted: 29c/29n -> 15c/32n on this already-degraded managed fixture (raw-first,
        ///     the production path, closes it 40+c/0n and is unchanged). E2 (plane-target cap extension)
        ///     targets the residual managed closure here.
        /// </summary>
        public static IEnumerable<object[]> ManagedFixtures()
        {
            yield return new object[] { "whole-level-flat.sam", 22, 0, 3479.896696920142 };
            yield return new object[] { "tilted-two-spaces.sam", 2, 0, 723.6524777123251 };
            yield return new object[] { "whole-level-tilted.sam", 22, 0, 3377.840918174829 };
            yield return new object[] { "two-level-tilted.sam", 15, 32, 2307.8631371507768 };
            // Towers re-pin (towers gap-0.5 root-cause fix, this PR): 21 cells / 8689.707 m³ → 20 / 8610.924.
            // The graze-continuation gate in NearestCoveringCap stops two podium walls at the EAST tower
            // junction (centres (43.68, -9.80) / (43.68, -1.80), tops 15.41) being extended to the east
            // tower's z=27.49 plate they merely bbox-graze by ~50 mm in plan — 12 m phantom walls that
            // enclosed one artifact cell (-78.78 m³) on this forced-managed diagnostic path. The raw-path
            // golden (the production outcome) is UNCHANGED - the raw-first path never reaches NearestCoveringCap
            // (it adopts the watertight input directly), so this managed-path fix cannot move it.
            yield return new object[] { "whole-level-towers.sam", 20, 8, 8610.924 };
        }

        [SkippableTheory]
        [MemberData(nameof(ManagedFixtures))]
        public void Solve3D_ManagedPath_ClosureSignatureMatchesGoldenMaster(string fixture, int expectedCellCount, int expectedNakedEdgeCount, double expectedVolume)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, fixture);
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> panels = LoadPanels(path);
            Assert.NotEmpty(panels);

            // ForceManagedPipeline bypasses the raw-first shortcut so the managed clean/extend/resolve
            // pipeline runs on the SAME well-modelled fixtures it would otherwise never see (raw-first
            // adopts them directly). §A documents this pipeline as historically weaker on these exact
            // fixtures (merged rooms, residual naked edges at inter-level slab corners); the expected
            // values above are the Phase 6c/6d baseline this test now machine-enforces.
            List<Panel> solved = panels.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> diagnostics, forceManagedPipeline: true);
            Assert.NotNull(solved);
            Assert.NotEmpty(solved);

            ClosureSignature3D signature = CaptureSignature(solved, nakedPoint3Ds);
            output.WriteLine(string.Format("{0} [managed]: {1}", fixture, signature));

            Assert.True(signature.CellCount == expectedCellCount, string.Format("{0}: expected exactly {1} cell(s), got {2}", fixture, expectedCellCount, signature.CellCount));
            Assert.True(signature.NakedEdgeCount == expectedNakedEdgeCount, string.Format("{0}: expected exactly {1} naked edge(s), got {2}", fixture, expectedNakedEdgeCount, signature.NakedEdgeCount));

            double volumeDelta = System.Math.Abs(signature.TotalVolume - expectedVolume);
            Assert.True(volumeDelta <= Core.Tolerance.MacroDistance, string.Format("{0}: expected total volume {1:0.###} m3 (+/- {2}), got {3:0.###} m3 (delta {4})", fixture, expectedVolume, Core.Tolerance.MacroDistance, signature.TotalVolume, volumeDelta));
        }

        /// <summary>The Grasshopper 0.21 path is intentional and independently pinned. These pins
        /// do not replace the core-default managed goldens above: core default 0 remains byte-identical.</summary>
        public static IEnumerable<object[]> Managed021Fixtures()
        {
            yield return new object[] { "whole-level-flat.sam", 22, 0, 3479.896692853791, 219 };
            yield return new object[] { "tilted-two-spaces.sam", 2, 0, 723.6524834224442, 9 };
            yield return new object[] { "whole-level-tilted.sam", 22, 0, 3377.8738860857575, 184 };
            // Re-pins (towers gap-0.5 root-cause fix, this PR) — the graze-continuation gate in
            // NearestCoveringCap no longer lets a wall extend to a cap it merely bbox-grazes in plan
            // when that cap's surface is more than the overshoot band from the wall's own extreme:
            // • two-level-tilted: 9 cells / 24 naked → 10 / 21 (one more room closes, three fewer
            //   naked edges — strictly better closure once phantom cross-storey extensions are gone).
            // • whole-level-towers: cells (25), naked (0) and volume UNCHANGED (within tolerance); only the
            //   face count drops 231 → 228 (the phantom graze-extended wall faces disappear). Cell identities
            //   and adjacency are unchanged - see PR61GoldenEvidenceIntegrationTests / the base/head logs.
            yield return new object[] { "two-level-tilted.sam", 10, 21, 2194.4185132571847, 397 };
            yield return new object[] { "whole-level-towers.sam", 25, 0, 9281.10719060441, 228 };
        }

        [SkippableTheory]
        [MemberData(nameof(Managed021Fixtures))]
        public void Solve3D_ManagedPath021_ClosureSignatureMatchesGoldenMaster(
            string fixture,
            int expectedCellCount,
            int expectedNakedEdgeCount,
            double expectedVolume,
            int expectedFaceCount)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, fixture);
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);
            List<Panel> panels = LoadPanels(path);
            Assert.NotEmpty(panels);

            List<Panel> solved = panels.Solve3D(
                out List<Point3D> nakedPoint3Ds,
                out _,
                out _,
                out Solve3DReport report,
                forceManagedPipeline: true,
                bucketBetweenLevels: 0.21);

            Assert.NotNull(solved);
            Assert.NotEmpty(solved);
            ClosureSignature3D signature = CaptureSignature(solved, nakedPoint3Ds);
            output.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0} [managed-0.21]: cells={1}, naked={2}, volume={3:R}, faces={4}",
                fixture, signature.CellCount, signature.NakedEdgeCount, signature.TotalVolume, signature.FaceCount));

            Assert.Equal(expectedCellCount, signature.CellCount);
            Assert.Equal(expectedNakedEdgeCount, signature.NakedEdgeCount);
            Assert.Equal(expectedFaceCount, signature.FaceCount);
            Assert.True(System.Math.Abs(signature.TotalVolume - expectedVolume) <= Core.Tolerance.MacroDistance,
                string.Format(CultureInfo.InvariantCulture, "{0}: expected volume {1:R}, got {2:R}", fixture, expectedVolume, signature.TotalVolume));

            AssertIntended021Grouping(fixture, report);
            AssertClaimedCapsNormalizedToGroupDatum(report);
        }

        /// <summary>Classifies the two changed 0.21 fixtures at the source of the delta. Only slab-skin datums
        /// merge; the other nearby datums remain singleton groups rather than being chained or swallowed.</summary>
        private static void AssertIntended021Grouping(string fixture, Solve3DReport report)
        {
            if (fixture == "whole-level-towers.sam")
            {
                Assert.Equal(10, report.LevelFrames.Count);
                Assert.Equal(9, report.LevelGroups.Count);
                LevelGroup merged = report.LevelGroups.Single(x => System.Math.Abs(x.Elevation - 12.240) < 1e-6);
                Assert.Equal(new List<int> { 0, 1 }, merged.FrameIndices);
                Assert.Equal(new List<double> { 12.240000, 12.397978 }, merged.MemberElevations.Select(x => System.Math.Round(x, 6)).ToList());
                Assert.Contains(report.LevelGroups, x => x.FrameIndices.SequenceEqual(new[] { 2 }) && System.Math.Abs(x.Elevation - 12.602691) < 1e-6);
                Assert.Contains(report.LevelGroups, x => x.FrameIndices.SequenceEqual(new[] { 3 }) && System.Math.Abs(x.Elevation - 15.290000) < 1e-6);
                Assert.Contains(report.LevelGroups, x => x.FrameIndices.SequenceEqual(new[] { 4 }) && System.Math.Abs(x.Elevation - 15.572691) < 1e-6);
            }
            else if (fixture == "two-level-tilted.sam")
            {
                Assert.Equal(4, report.LevelFrames.Count);
                Assert.Equal(3, report.LevelGroups.Count);
                LevelGroup merged = report.LevelGroups.Single(x => x.FrameIndices.Count == 2);
                Assert.Equal(new List<int> { 2, 3 }, merged.FrameIndices);
                Assert.Equal(new List<double> { 0.950536, 1.124485 }, merged.MemberElevations.Select(x => System.Math.Round(x, 6)).ToList());
            }
        }

        /// <summary>Every clean face parallel to a group datum and inside its 0.21 claim band must be ON a group
        /// datum. This catches the prior defect where NormalizeCaps silently reselected an actual cap plane.</summary>
        private static void AssertClaimedCapsNormalizedToGroupDatum(Solve3DReport report)
        {
            List<LevelFrame> datums = report.LevelGroups.Select(x => x.ToDatumFrame()).ToList();
            double minDot = System.Math.Cos(LevelFrame.DEFAULT_NormalConeTolerance);
            foreach (Face3D face in report.CleanFace3Ds ?? new List<Face3D>())
            {
                Plane plane = face?.GetPlane();
                Point3D centroid = face?.GetBoundingBox()?.GetCentroid();
                if (plane == null || centroid == null)
                {
                    continue;
                }

                List<double> claimedDistances = datums
                    .Where(x => System.Math.Abs(x.Normal.DotProduct(plane.Normal)) >= minDot)
                    .Select(x => System.Math.Abs(x.Plane.Distance(centroid)))
                    .Where(x => x <= 0.21 + Core.Tolerance.Distance)
                    .ToList();
                if (claimedDistances.Count > 0)
                {
                    Assert.True(claimedDistances.Min() <= 1e-6,
                        string.Format(CultureInfo.InvariantCulture, "Claimed cap remained {0:R} m from its group datum.", claimedDistances.Min()));
                }
            }
        }
    }
}
