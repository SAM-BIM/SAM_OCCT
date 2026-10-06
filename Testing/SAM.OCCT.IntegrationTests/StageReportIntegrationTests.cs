// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Phase 8c acceptance: the <c>Clean3D</c>/<c>Extend3D</c>/<c>AutoTune3D</c> overloads with an
    /// <c>out Solve3DReport report</c> solve identically to the existing overloads and additionally
    /// report Stage A/pre-resolve diagnostics and source mapping
    /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md Phase 8). Native-gated (Panel3DSnapSolver's
    /// managed stages run without the kernel, but the fixture construction is shared with the
    /// native-gated suite for consistency).
    /// </summary>
    public class StageReportIntegrationTests
    {
        private static Face3D Rect(Point3D a, Point3D b, Point3D c, Point3D d)
        {
            return TestGeometry.CreatePlanarFace(a, b, c, d);
        }

        private static List<Panel> SealedRoomPanels()
        {
            Construction wallConstruction = new Construction("Test Wall");
            return new List<Panel>
            {
                global::SAM.Analytical.Create.Panel(new Construction("Floor"), PanelType.Floor, Rect(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(0, 4, 0))),
                global::SAM.Analytical.Create.Panel(new Construction("Roof"), PanelType.Roof, Rect(new Point3D(0, 0, 3), new Point3D(4, 0, 3), new Point3D(4, 4, 3), new Point3D(0, 4, 3))),
                global::SAM.Analytical.Create.Panel(wallConstruction, PanelType.Wall, Rect(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 0, 3), new Point3D(0, 0, 3))),
                global::SAM.Analytical.Create.Panel(wallConstruction, PanelType.Wall, Rect(new Point3D(0, 4, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(0, 4, 3))),
                global::SAM.Analytical.Create.Panel(wallConstruction, PanelType.Wall, Rect(new Point3D(0, 0, 0), new Point3D(0, 4, 0), new Point3D(0, 4, 3), new Point3D(0, 0, 3))),
                global::SAM.Analytical.Create.Panel(wallConstruction, PanelType.Wall, Rect(new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(4, 0, 3))),
            };
        }

        [SkippableFact]
        public void Clean3D_SealedRoom_ReportHasSourceMapAndNoNativeFields()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> cleaned = SealedRoomPanels().Clean3D(out List<string> diagnostics, out Solve3DReport report);

            Assert.NotNull(cleaned);
            Assert.NotEmpty(cleaned);
            Assert.NotNull(report);
            Assert.Null(report.Signature); // Stage A never runs a native resolve
            Assert.Empty(report.Cells);
            Assert.NotEmpty(report.FormatSourceMap()); // every clean face is attributed to >= 1 source
        }

        [SkippableFact]
        public void Extend3D_SealedRoom_ReportHasSourceMapAndNoNativeFields()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> extended = SealedRoomPanels().Extend3D(out List<string> diagnostics, out Solve3DReport report);

            Assert.NotNull(extended);
            Assert.NotEmpty(extended);
            Assert.NotNull(report);
            Assert.Null(report.Signature);
            Assert.Empty(report.Cells);
            Assert.NotEmpty(report.FormatSourceMap());
        }

        [SkippableFact]
        public void AutoTune3D_WatertightBaseline_ReportMatchesSolve3DBehaviour()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> solved = SealedRoomPanels().AutoTune3D(out List<Point3D> nakedPoint3Ds, out List<string> diagnostics, out List<OrphanedAperture> orphanedApertures, out Solve3DReport report);

            Assert.NotNull(solved);
            Assert.NotEmpty(solved);
            Assert.NotNull(report);
            Assert.Equal(0, report.Rounds); // an already-watertight baseline never engages AutoTune
            Assert.Equal(0, report.RoundsAccepted);
            Assert.NotNull(report.Signature);
            Assert.Equal(0, report.Signature.NakedEdgeCount);
            Assert.Contains("AutoTune rounds: 0 accepted of 0 attempted", report.ClosureReportText);
        }
    }
}
