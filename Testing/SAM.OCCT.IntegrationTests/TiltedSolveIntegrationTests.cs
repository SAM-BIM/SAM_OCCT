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
    /// End-to-end <see cref="Modify.Solve3D"/> regressions on tilted real-model fixtures. The whole building is
    /// tilted (floors/roofs share a non-Z normal), so these exercise the level-frame extend AND the back-to-back
    /// partition collapse (<c>Panel3DSnapSolver.SnapOpposedPartitions</c>) that lets adjacent rooms each form
    /// their own cell. Native-gated; results are deterministic on a given OCCT build.
    /// </summary>
    public class TiltedSolveIntegrationTests
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

        private static int CellCount(List<string> diagnostics)
        {
            // "...; N cell(s); ..."
            foreach (string d in diagnostics ?? new List<string>())
            {
                int i = d.IndexOf(" cell(s)");
                if (i < 0)
                {
                    continue;
                }

                int start = d.LastIndexOf(';', i) + 1;
                if (int.TryParse(d.Substring(start, i - start).Trim(), out int cells))
                {
                    return cells;
                }
            }

            return -1;
        }

        [SkippableTheory]
        [InlineData("tilted-two-spaces.sam", 2)]   // two tilted rooms sharing one back-to-back partition
        [InlineData("whole-level-tilted.sam", 22)] // a whole tilted level of 22 rooms
        public void Solve3D_TiltedFixture_ClosesEveryRoomWithoutNakedEdges(string fixture, int expectedCells)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, fixture);
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> panels = LoadPanels(path);
            Assert.NotEmpty(panels);

            List<Panel> solved = panels.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> diagnostics);

            Assert.NotNull(solved);
            Assert.NotEmpty(solved);
            // Every room closes into its own cell ...
            Assert.Equal(expectedCells, CellCount(diagnostics));
            // ... and the envelope is watertight (no unresolved gaps).
            Assert.Empty(nakedPoint3Ds);
        }

        /// <summary>
        /// Two stacked tilted levels (287 panels). This well-modelled export resolves directly through the kernel
        /// via the solver's raw-first path, so every room on both storeys forms its own cell (~44). The old
        /// clean+extend pipeline under-counted at 32 cells with ~7 residual naked edges at the inter-level slab
        /// corners; the watertightness is asserted by <see cref="Solve3D_TwoLevelTilted_FullyCloses"/>.
        /// </summary>
        [SkippableFact]
        public void Solve3D_TwoLevelTilted_SolvesAndImprovesClosure()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, "two-level-tilted.sam");
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> panels = LoadPanels(path);
            Assert.NotEmpty(panels);

            List<Panel> solved = panels.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> diagnostics);

            Assert.NotNull(solved);
            // Both storeys' rooms form (raw-first yields ~44 cells; the old pipeline reached only 32).
            Assert.True(CellCount(diagnostics) >= 40, $"Expected >= 40 cells, got {CellCount(diagnostics)}");
        }

        /// <summary>
        /// Strict target for the two-level tilted model: every space fully enclosed (no naked edges). Now met by
        /// the raw-first solve path (the well-modelled export resolves watertight directly through the kernel,
        /// where the managed clean+extend left ~7-19 naked edges at the inter-level slab corners).
        /// </summary>
        [SkippableFact]
        public void Solve3D_TwoLevelTilted_FullyCloses()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, "two-level-tilted.sam");
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> panels = LoadPanels(path);
            panels.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> _);
            Assert.Empty(nakedPoint3Ds);
        }
    }
}
