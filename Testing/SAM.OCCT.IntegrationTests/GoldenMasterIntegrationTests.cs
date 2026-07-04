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
        /// Managed-path baseline pinned to the Phase 6c/6d documented values (TESTING.md "Per-level
        /// frames" §6c/§6e, <c>docs/P6_ARCHITECTURE_REVIEW.md</c> §O.O1): fixture, expected cell count,
        /// expected naked-edge count, expected total volume (m3). Cell/naked counts are exact; volume is
        /// asserted within <see cref="ClosureSignature3D.IsRegressionOf"/>'s own tolerance
        /// (<see cref="Core.Tolerance.MacroDistance"/>), not string-formatted equality, so harmless
        /// floating-point noise in the native volume sum does not fail the test while any real drift
        /// still does. Three of five fixtures are unchanged from Phase 5; `two-level-tilted` and
        /// `whole-level-towers` are the intentional Phase 6c re-baseline. A value changing here without
        /// an explicit, reasoned PR delta is a regression, per the golden-master contract in TESTING.md.
        /// </summary>
        public static IEnumerable<object[]> ManagedFixtures()
        {
            yield return new object[] { "whole-level-flat.sam", 22, 0, 3479.896696920142 };
            yield return new object[] { "tilted-two-spaces.sam", 2, 0, 723.6524777123251 };
            yield return new object[] { "whole-level-tilted.sam", 22, 0, 3377.8280592825126 };
            yield return new object[] { "two-level-tilted.sam", 29, 29, 2213.30306718148 };
            yield return new object[] { "whole-level-towers.sam", 22, 12, 8777.056042123724 };
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
    }
}
