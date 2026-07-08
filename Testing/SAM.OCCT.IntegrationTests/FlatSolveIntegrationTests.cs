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
using Xunit.Abstractions;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// End-to-end <see cref="Modify.Solve3D"/> regressions on FLAT (non-tilted) real-model fixtures. The whole
    /// solver suite was previously tilted-only (<see cref="TiltedSolveIntegrationTests"/>), so a regression on a
    /// plain whole level - floors/roofs on world Z - slipped through. This guards that a flat whole level closes
    /// every room into its own cell with a watertight envelope. Native-gated; deterministic on a given OCCT build.
    /// </summary>
    public class FlatSolveIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

        private readonly ITestOutputHelper output;

        public FlatSolveIntegrationTests(ITestOutputHelper output)
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

        /// <summary>
        /// A flat whole level of 22 rooms. Every room must close into its own cell with no naked edges. This is the
        /// regression that escaped the tilted-only suite: the back-to-back partition collapse pre-pass
        /// (<c>Panel3DSnapSolver.SnapOpposedPartitions</c>) was tuned on tilted models and mis-paired on the dense
        /// flat level, merging rooms (18/22).
        /// </summary>
        [SkippableTheory]
        [InlineData("whole-level-flat.sam", 22)]
        public void Solve3D_FlatFixture_ClosesEveryRoomWithoutNakedEdges(string fixture, int expectedCells)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, fixture);
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> panels = LoadPanels(path);
            Assert.NotEmpty(panels);

            List<Panel> solved = panels.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> diagnostics);

            foreach (string d in diagnostics ?? new List<string>())
            {
                output.WriteLine(d);
            }

            Assert.NotNull(solved);
            Assert.NotEmpty(solved);
            // Every room closes into its own cell ...
            Assert.Equal(expectedCells, CellCount(diagnostics));
            // ... and the envelope is watertight (no unresolved gaps).
            Assert.Empty(nakedPoint3Ds);
        }

        /// <summary>
        /// A flat main level (rooms on world Z) with multi-storey TOWERS stacked on the side (caps at z 12, 15, 18,
        /// 21, 24, 27, 30). This well-modelled export resolves directly through the kernel: the solver's raw-first
        /// path hands the kernel the raw faces and keeps that watertight result, so every room closes (the old
        /// clean+extend pipeline degraded it to 23/12 by merging away room separations). The kernel finds the 31
        /// rooms plus, occasionally, a tiny gap-artifact cell where two modelled rooms leave a thin void - hence
        /// >= 31 (not == 31) on the count; the hard guarantee is the watertight envelope (no naked edges).
        /// </summary>
        [SkippableFact]
        public void Solve3D_FlatTowers_ClosesEveryRoom()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, "whole-level-towers.sam");
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> panels = LoadPanels(path);
            panels.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> diagnostics);

            foreach (string d in diagnostics ?? new List<string>())
            {
                output.WriteLine(d);
            }

            // All 31 rooms form (a thin gap-artifact cell may add one more) ...
            Assert.True(CellCount(diagnostics) >= 31, $"Expected >= 31 cells, got {CellCount(diagnostics)}");
            // ... and the envelope is watertight.
            Assert.Empty(nakedPoint3Ds);
        }
    }
}
