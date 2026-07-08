// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Native-gated coverage for Phase E2 (docs/EXTEND3D_ROBUST_HANDOVER.md): walls extend to the ACTUAL cap
    /// plane. A cap that is flat RELATIVE TO ITS WALL (a level floor/ceiling, including a rigidly tilted level
    /// where wall and slab tilt together) keeps the pre-E2 scalar target - so the tilted golden fixtures are
    /// byte-identical (asserted by GoldenMasterIntegrationTests; guarded again here). Only a cap genuinely
    /// PITCHED relative to the wall (a real sloped roof over a vertical wall) is followed as a sloped plane,
    /// which is what improves the messy real-export fixtures. These pins record E2's managed-solve improvement
    /// on those fixtures (the re-baseline table in TESTING.md "E2").
    /// </summary>
    public class Extend3DPlaneTargetIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

        private static List<Panel> LoadPanels(string path)
        {
            List<IJSAMObject> objects = SAM.Core.Convert.ToSAM(path);
            List<Panel> result = new List<Panel>();
            foreach (IJSAMObject sAMObject in objects ?? new List<IJSAMObject>())
            {
                switch (sAMObject)
                {
                    case AnalyticalModel analyticalModel: result.AddRange(analyticalModel.GetPanels() ?? new List<Panel>()); break;
                    case AdjacencyCluster adjacencyCluster: result.AddRange(adjacencyCluster.GetPanels() ?? new List<Panel>()); break;
                    case Panel panel: result.Add(panel); break;
                }
            }

            return result.Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid()).ToList();
        }

        private static (int cells, int naked) SolveManaged(string fixture)
        {
            string path = Path.Combine(FixturesDirectory, fixture);
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);
            List<Panel> panels = LoadPanels(path);
            panels.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> _, out _, out Solve3DReport report, forceManagedPipeline: true);
            return (report.ResolvedCellCount, nakedPoint3Ds?.Count ?? 0);
        }

        // E2 wins on the real-export fixtures (pitched roofs), managed path (report closure).
        // Pre-E2 (E1) baselines in the comments; the deltas are the plane-target improvement.

        [SkippableFact]
        public void Solve3D_ManagedPath_RevitHome_PlaneTargetClosesWatertight()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            // E1: 12 cells / 4 naked. E2 plane-targeting the pitched roofs closes it watertight and finds more
            // cells: 18 cells / 0 naked (naked strictly improved).
            (int cells, int naked) = SolveManaged("Revit-home-panels.sam");
            Assert.Equal(18, cells);
            Assert.Equal(0, naked);
        }

        [SkippableFact]
        public void Solve3D_ManagedPath_AdjacencyClusterHome_PlaneTargetCutsNaked()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            // E1: 18 cells / 25 naked. E2 plane-targeting cuts the naked count dramatically and separates more
            // cells: 20 cells / 4 naked (naked 25 -> 4).
            (int cells, int naked) = SolveManaged("AdjacencyCluster-home.sam");
            Assert.Equal(20, cells);
            Assert.Equal(4, naked);
        }

        [SkippableFact]
        public void Solve3D_ManagedPath_Face3DHome_PlaneTargetRebaseline()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            // E1: 25 cells / 3 naked. E2 plane-targeting gives a coarser-but-clean managed decomposition:
            // 22 cells / 4 naked. The +1 solver-internal naked is the accepted net-tradeoff (owner decision
            // 2026-07-08); both workflows stay parity-clean (WorkflowParityIntegrationTests).
            (int cells, int naked) = SolveManaged("Face3D-home.sam");
            Assert.Equal(22, cells);
            Assert.Equal(4, naked);
        }

        [SkippableFact]
        public void Solve3D_ManagedPath_TiltedLevel_UnchangedByPlaneTarget()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            // Discriminator guard: a rigidly tilted level's caps are flat RELATIVE to their walls, so E2 leaves
            // it byte-identical to E1 - the two-level-tilted managed closure is unchanged (32 report cells /
            // 32 naked). This is what keeps the naive world-Z plane target (which collapsed this fixture to 3
            // cells in development) from ever engaging on a tilted-flat level.
            (int cells, int naked) = SolveManaged("two-level-tilted.sam");
            Assert.Equal(32, cells);
            Assert.Equal(32, naked);
        }
    }
}
