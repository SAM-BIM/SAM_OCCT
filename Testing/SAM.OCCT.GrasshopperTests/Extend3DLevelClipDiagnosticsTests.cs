// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Analytical.Grasshopper.OCCT;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Core.Grasshopper.OCCT;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SAM.OCCT.GrasshopperTests
{
    /// <summary>
    /// SAMOCCT.Extend3D raises the per-panel extend diagnostics with the severity recorded where each is produced (review
    /// of 2026-10-26, revision 3, finding 1): a level clip that could not be made is a Grasshopper Error at every detail
    /// level. The failure comes from the real clip routine (internal, reached by reflection as the test assembly has no
    /// access to it) and the real adapter; the component keeps its GUID, version and outputs.
    /// </summary>
    public class Extend3DLevelClipDiagnosticsTests
    {
        /// <summary>A wall whose level clip cannot keep its original (a 50 mm square apart from the far larger extension).</summary>
        private static SnappedPanel FailedClip()
        {
            Face3D original = new Face3D(new Polygon3D(new List<Point3D> { new Point3D(0, 10, 5), new Point3D(0, 10.05, 5), new Point3D(0, 10.05, 5.05), new Point3D(0, 10, 5.05) }));
            SnappedPanel wall = new SnappedPanel(7, new Face3D(new Polygon3D(new List<Point3D> { new Point3D(0, 0, -0.05), new Point3D(0, 4, -0.05), new Point3D(0, 4, 6), new Point3D(0, 0, 6) })), 1, 0.3, 0.5);
            List<(double, IList<(double, double)>, IList<(double, double, double)>)> levels = new List<(double, IList<(double, double)>, IList<(double, double, double)>)>
            {
                (3.0, new List<(double, double)> { (-0.3, 4.3) }, new List<(double, double, double)>()),
            };

            MethodInfo clip = typeof(SnappedPanel).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).Single(x => x.Name == "ClipBeyondLevels" && x.GetParameters().Length == 6);
            object[] arguments = { original, levels, false, 0.05, 1e-6, null };
            clip.Invoke(wall, arguments);
            Assert.True((bool)arguments[5], "The clip must fail.");
            return wall;
        }

        [Fact]
        public void Extend3D_GuidVersionAndDiagnosticsOutput_Unchanged()
        {
            // Arrange
            SAMOCCTExtend3D component = new SAMOCCTExtend3D();

            // Act
            int index = component.Params.IndexOfOutputParam("Diagnostics");

            // Assert: GUID, outputs and inputs kept; the version is a minor bump for the changed computed result.
            Assert.Equal(new Guid("a7e4c92f-1b53-4d8a-9f26-3c70e1b8d4a5"), component.ComponentGuid);
            Assert.Equal("0.11.0", component.LatestComponentVersion); // minor: computed panels changed (GH-F2/H2)
            Assert.True(index >= 0);
            Assert.Equal(GH_ParamAccess.list, component.Params.Output[index].Access);
            Assert.Equal(-1, component.Params.IndexOfInputParam(OcctDiagnosticReporter.DetailInputName));
        }

        [Theory]
        [InlineData(DiagnosticDetail.Summary)]
        [InlineData(DiagnosticDetail.Standard)]
        [InlineData(DiagnosticDetail.Detailed)]
        public void Report_FailedLevelClip_IsAnErrorOnExtend3DAtEveryDetailLevel(DiagnosticDetail detail)
        {
            // Arrange
            SnappedPanel wall = FailedClip();
            List<DiagnosticRecord> records = SolverReportFormat.FormatExtendPanelDiagnosticRecords(new List<SnappedPanel> { wall });
            SAMOCCTExtend3D component = new SAMOCCTExtend3D();

            // Act
            List<string> lines = OcctDiagnosticReporter.Report(component, records, detail);

            // Assert
            string failed = wall.ExtendDiagnostics.Single();
            Assert.StartsWith("SAM_OCCT_EXTEND3D_LEVEL_CLIP_FAILED", failed);
            Assert.Contains(failed, lines);
            Assert.Equal(new[] { failed }, component.RuntimeMessages(GH_RuntimeMessageLevel.Error).ToArray());
            Assert.Empty(component.RuntimeMessages(GH_RuntimeMessageLevel.Warning));
        }
    }
}
