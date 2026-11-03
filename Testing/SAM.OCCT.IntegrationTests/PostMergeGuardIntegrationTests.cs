// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// End-to-end regressions for the coplanar post-merge guard (<see cref="PostMergeGuard"/>), through the public
    /// <see cref="Modify.Solve3D"/> on the two-level tilted fixture. With the wall-run correction the native coplanar
    /// post-merge of the resolved faces re-decoded to fewer cells than MakerVolume had formed: at bucketBetweenLevels 0
    /// both north rooms (161.96 and 83.92 m3) vanished, at 0.21 the 120.97 m3 and 162.23 m3 rooms fused into one
    /// 283.20 m3 cell. Cell volumes and centres are those measured on the pre-merge decode (MakerVolume result), so
    /// they do not depend on how the merged faces are presented. Native-gated.
    /// </summary>
    public class PostMergeGuardIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

        private static List<Panel> LoadPanels(string fixture)
        {
            string path = Path.Combine(FixturesDirectory, fixture);
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> result = new List<Panel>();
            foreach (IJSAMObject sAMObject in SAM.Core.Convert.ToSAM(path) ?? new List<IJSAMObject>())
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

        private static Solve3DReport SolveManaged(string fixture, double bucketBetweenLevels, out List<string> diagnostics, out int nakedCount)
        {
            List<Panel> panels = LoadPanels(fixture);
            panels.Solve3D(out List<Point3D> nakedPoint3Ds, out diagnostics, out _, out Solve3DReport report, forceManagedPipeline: true, bucketBetweenLevels: bucketBetweenLevels);
            nakedCount = nakedPoint3Ds?.Count ?? 0;
            return report;
        }

        private static bool HasCell(Solve3DReport report, double volume, double x, double y, double volumeTolerance = 0.05, double centreTolerance = 0.2)
        {
            return report.Cells.Any(c =>
                System.Math.Abs(c.Volume - volume) <= volumeTolerance &&
                System.Math.Abs(c.Center.X - x) <= centreTolerance &&
                System.Math.Abs(c.Center.Y - y) <= centreTolerance);
        }

        [SkippableFact]
        public void Solve3D_TwoLevelTiltedBucket0_KeepsBothNorthRooms()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            Solve3DReport report = SolveManaged("two-level-tilted.sam", 0.0, out _, out _);

            // S75-S80 (161.96 m3) and S63-S68 (83.92 m3): both form in the MakerVolume result and must survive the post-merge.
            Assert.True(HasCell(report, 161.958, 33.84, -3.38), "North room 161.96 m3 near (33.84, -3.38) is missing.");
            Assert.True(HasCell(report, 83.924, 38.62, -2.33), "North room 83.92 m3 near (38.62, -2.33) is missing.");
        }

        [SkippableFact]
        public void Solve3D_TwoLevelTiltedBucket021_KeepsTheTwoSouthRoomsSeparate()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            Solve3DReport report = SolveManaged("two-level-tilted.sam", 0.21, out _, out _);

            Assert.True(HasCell(report, 120.973, 20.25, -22.46), "South room 120.97 m3 near (20.25, -22.46) is missing.");
            Assert.True(HasCell(report, 162.229, 14.93, -22.62), "South room 162.23 m3 near (14.93, -22.62) is missing.");
            // The fused cell is the sum of the two (283.20 m3) with the shared wall removed.
            Assert.False(HasCell(report, 283.202, 17.29, -22.62), "The two south rooms fused into one 283.20 m3 cell.");
        }

        [SkippableTheory]
        [InlineData("two-level-tilted.sam", 0.0)]
        [InlineData("two-level-tilted.sam", 0.21)]
        public void Solve3D_TwoLevelTilted_ReportsEveryMergeRevertInDiagnostics(string fixture, double bucketBetweenLevels)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            SolveManaged(fixture, bucketBetweenLevels, out List<string> diagnostics, out _);

            // A guard that changes the output must say so (never silent): the Resolve-stage warning names the reverted groups and lost cells.
            Assert.Contains(diagnostics, d => d.Contains("RejectedMerge"));
        }

        [SkippableTheory]
        [InlineData("tilted-two-spaces.sam", 2, 723.65)]
        [InlineData("whole-level-tilted.sam", 22, 3377.84)]
        [InlineData("whole-level-flat.sam", 22, 3479.90)]
        [InlineData("three-spaces.sam", 3, 264.82)]
        public void Solve3D_OtherFixtures_GuardAcceptsTheMergeAndKeepsTheClosure(string fixture, int expectedCells, double expectedVolume)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            Solve3DReport report = SolveManaged(fixture, 0.0, out List<string> diagnostics, out int naked);

            Assert.Equal(expectedCells, report.ResolvedCellCount);
            Assert.Equal(0, naked);
            Assert.InRange(report.Cells.Sum(c => c.Volume), expectedVolume - 0.05, expectedVolume + 0.05);
            Assert.DoesNotContain(diagnostics, d => d.Contains("RejectedMerge"));
        }
    }
}
