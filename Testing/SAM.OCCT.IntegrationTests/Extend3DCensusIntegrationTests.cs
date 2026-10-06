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
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// E1 extend-path census (docs/EXTEND3D_ROBUST_HANDOVER.md): records, per golden fixture, how many
    /// SnappedPanel extend/footprint operations took the byte-identical legacy re-extrude fast path vs
    /// the profile-preserving plane-ops path. This is OBSERVATIONAL evidence for review, not a freeze
    /// gate: the fast path is NOT expected to fire for every wall (these fixtures contain non-rectangular
    /// walls the old code collapsed/verticalized), which is exactly why E1 re-baselines the three moved
    /// managed golden signatures (GoldenMasterIntegrationTests.ManagedFixtures) - see the E1 section in
    /// TESTING.md. Native-free (Extend3D stops before the resolve), so it runs anywhere the fixtures load.
    /// </summary>
    public class Extend3DCensusIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

        private readonly ITestOutputHelper output;

        public Extend3DCensusIntegrationTests(ITestOutputHelper output)
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
                    case AnalyticalModel analyticalModel: result.AddRange(analyticalModel.GetPanels() ?? new List<Panel>()); break;
                    case AdjacencyCluster adjacencyCluster: result.AddRange(adjacencyCluster.GetPanels() ?? new List<Panel>()); break;
                    case Panel panel: result.Add(panel); break;
                }
            }

            return result.Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid()).ToList();
        }

        [SkippableTheory]
        [InlineData("whole-level-flat.sam")]
        [InlineData("tilted-two-spaces.sam")]
        [InlineData("whole-level-tilted.sam")]
        [InlineData("two-level-tilted.sam")]
        [InlineData("whole-level-towers.sam")]
        public void Extend3D_GoldenFixture_RecordsFastVsPlaneOpsSplit(string fixture)
        {
            string path = Path.Combine(FixturesDirectory, fixture);
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> panels = LoadPanels(path);
            Assert.NotEmpty(panels);

            SnappedPanel.ResetExtendCensus();
            panels.Extend3D(out List<string> _);

            int fast = SnappedPanel.FastPathExtendCount;
            int planeOps = SnappedPanel.PlaneOpsExtendCount;
            int holeDropped = SnappedPanel.HoleDroppedCount;
            output.WriteLine(string.Format("{0}: fast-path={1} plane-ops={2} hole-dropped={3}", fixture, fast, planeOps, holeDropped));

            // Sanity only: the conditioning ran and exercised the primitives. The split itself is the
            // observation (logged above), not an assertion - see the class summary.
            Assert.True(fast + planeOps > 0, "Extend3D should exercise the extend/footprint primitives.");
        }
    }
}
