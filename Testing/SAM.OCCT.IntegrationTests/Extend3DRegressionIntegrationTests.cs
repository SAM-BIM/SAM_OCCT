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
    /// Regression coverage for the bug PR #49 (`feature/extend3D`, superseded by the E-track -
    /// docs/EXTEND3D_ROBUST_HANDOVER.md) reported: real Revit-exported wall/floor/roof panels close but
    /// not touching ("walls disappear after SAMOCCT.Extend3D"). The four sample models PR #49 attached
    /// (converted from raw JSON to the compressed `.sam` fixture format - ~80% smaller) are kept in
    /// <c>Fixtures/Extend3D-Regression</c>, OUTSIDE the top-level fixture scan
    /// (<see cref="OcctFixtureIntegrationTests.Shells_UploadedSamFixtures_BuildCells"/> expects every
    /// top-level fixture to raw-build watertight; these are deliberately gappy - that is the bug they
    /// reproduce - so a naive raw build is not expected to succeed on all of them).
    /// <para>
    /// These pins are CURRENT-STATE measurements (first coverage these fixtures ever got), not a priori
    /// targets: a future phase that improves closure should update the pinned value with a stated
    /// mechanism, per this repo's golden-master convention - never silently re-baseline.
    /// </para>
    /// </summary>
    public class Extend3DRegressionIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(System.AppContext.BaseDirectory, "Fixtures", "Extend3D-Regression");

        private static List<Panel> LoadPanels(string path)
        {
            List<IJSAMObject> objects = SAM.Core.Convert.ToSAM(path);
            List<Panel> result = new List<Panel>();
            foreach (IJSAMObject sAMObject in objects ?? new List<IJSAMObject>())
            {
                if (sAMObject is Panel panel)
                {
                    result.Add(panel);
                }
            }

            return result.Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid()).ToList();
        }

        private static int WallCount(IEnumerable<Panel> panels)
        {
            return panels.Count(x => x != null && global::SAM.Analytical.Query.PanelGroup(x.PanelType) == PanelGroup.Wall);
        }

        [SkippableTheory]
        [InlineData("PR49-Test0-3spaces.sam", 12, 3, 0)]
        [InlineData("PR49-Test1.sam", 4, 2, 0)]
        [InlineData("PR49-Test2.sam", 88, 43, 0)]
        public void Solve3D_CleanPR49Fixtures_PreservesAllWallsAndCloses(string fixture, int expectedWalls, int expectedCells, int expectedNaked)
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, fixture);
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> panels = LoadPanels(path);
            int wallsIn = WallCount(panels);
            Assert.Equal(expectedWalls, wallsIn); // sanity: the fixture itself hasn't changed

            List<Panel> solved = panels.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> _, out _, out Solve3DReport report);
            List<Panel> solvedNonAir = (solved ?? new List<Panel>()).Where(x => x != null && x.PanelType != PanelType.Air).ToList();

            // The literal PR #49 bug: walls silently disappearing during the extend/resolve pass. On
            // these three (relatively clean) exports the raw-first path adopts directly and every wall
            // survives - the bug this test guards against would show up as WallCount(solvedNonAir) < wallsIn.
            Assert.Equal(wallsIn, WallCount(solvedNonAir));
            Assert.Equal(expectedCells, report.ResolvedCellCount);
            Assert.Equal(expectedNaked, nakedPoint3Ds?.Count ?? 0);
        }

        [SkippableFact]
        public void Solve3D_Test0aFixture_WallsSurviveButFixtureUnderClosesUnderDefaultSettings()
        {
            // PR49-Test0a-3spaces.sam is a harder variant of Test0 (a larger real gap between panels):
            // the raw-first attempt fails and the managed pipeline only partially resolves it under
            // DEFAULT settings (12 walls in -> 4 survive; 1 of the model's 3 spaces closes). This is NOT
            // the zero-walls PR #49 bug (some walls DO survive, and nothing crashes/naked-edges), but it
            // is a known residual gap-size limit, not a false "all clear" - tracked here rather than
            // silently dropped. A future MaxExtend/bucket-size tune (AutoTune3D) or frame-aware extension
            // may close this further; this pin should move only with a stated mechanism.
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, "PR49-Test0a-3spaces.sam");
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> panels = LoadPanels(path);
            int wallsIn = WallCount(panels);
            Assert.Equal(12, wallsIn);

            List<Panel> solved = panels.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> _, out _, out Solve3DReport report);
            List<Panel> solvedNonAir = (solved ?? new List<Panel>()).Where(x => x != null && x.PanelType != PanelType.Air).ToList();
            int wallsOut = WallCount(solvedNonAir);

            Assert.False(report.RawAdopted); // the raw-first attempt does not close this fixture directly
            Assert.True(wallsOut > 0, "Regression: all walls disappeared (the exact PR #49 bug) rather than a partial resolve.");
            Assert.Equal(4, wallsOut);
            Assert.Equal(1, report.ResolvedCellCount);
            Assert.Empty(nakedPoint3Ds ?? new List<Point3D>());
        }
    }
}
