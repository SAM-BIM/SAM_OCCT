// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Phase 8 acceptance: the <c>Modify.Solve3D</c> overload with an <c>out Solve3DReport report</c>
    /// solves identically to the existing overloads and additionally reports the staged/diagnostic
    /// fields Grasshopper needs (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md Phase 8). Native-gated.
    /// </summary>
    public class Solve3DReportIntegrationTests
    {
        private static Face3D Rect(Point3D a, Point3D b, Point3D c, Point3D d)
        {
            return TestGeometry.CreatePlanarFace(a, b, c, d);
        }

        /// <summary>4x4x3 m sealed room: 4 walls + floor + roof, coplanar-clean and gap-free, so the
        /// raw-first path adopts it directly with zero naked edges.</summary>
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
        public void Solve3D_SealedRoom_ReportReflectsRawAdoptedResult()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> input = SealedRoomPanels();

            List<Panel> solved = input.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> diagnostics, out List<OrphanedAperture> orphanedApertures, out Solve3DReport report);

            Assert.NotNull(solved);
            Assert.NotEmpty(solved);
            Assert.NotNull(report);
            Assert.True(report.RawAdopted, "A clean, gap-free sealed box should adopt the raw-first attempt.");
            Assert.NotNull(report.Signature);
            Assert.Equal(0, report.Signature.NakedEdgeCount);
            Assert.NotEmpty(report.Cells);
            Assert.Equal(report.Signature.CellCount, report.Cells.Count);
            Assert.Empty(report.CellRoles); // classifyCells defaults to false
            Assert.Empty(report.LevelFrames); // the raw path never clusters level frames
            Assert.Empty(report.CleanFace3Ds); // Stage A never ran on the raw path
            Assert.Contains("Adopted path: Raw", report.ClosureReportText);
        }

        [SkippableFact]
        public void Solve3D_ForceManagedPipeline_ReportReflectsManagedResult()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> input = SealedRoomPanels();

            List<Panel> solved = input.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> diagnostics, out List<OrphanedAperture> orphanedApertures, out Solve3DReport report, forceManagedPipeline: true);

            Assert.NotNull(solved);
            Assert.NotNull(report);
            Assert.False(report.RawAdopted);
            Assert.Null(report.RawAttemptSignature); // ForceManagedPipeline skips the raw attempt entirely
            Assert.NotEmpty(report.CleanFace3Ds); // Stage A ran on the managed path
        }

        [SkippableFact]
        public void Solve3D_ClassifyCellsRequested_PopulatesCellRolesIndexAlignedWithCells()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Panel> input = SealedRoomPanels();

            input.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> diagnostics, out List<OrphanedAperture> orphanedApertures, out Solve3DReport report, classifyCells: true);

            Assert.NotNull(report);
            Assert.NotEmpty(report.Cells);
            Assert.Equal(report.Cells.Count, report.CellRoles.Count);
            Assert.All(report.CellRoles, role => Assert.Equal(CellRole.Interior, role)); // a single sealed room has no exterior/sliver cell
        }

        [SkippableFact]
        public void Solve3D_InputEmpty_ReturnsNonNullReport()
        {
            List<Panel> solved = new List<Panel>().Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> diagnostics, out List<OrphanedAperture> orphanedApertures, out Solve3DReport report);

            Assert.Null(solved);
            Assert.NotNull(report);
            Assert.Empty(report.Cells);
        }
    }
}
