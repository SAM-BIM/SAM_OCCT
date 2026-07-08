// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Phase 5e analytical entry point <see cref="Modify.AutoTune3D(System.Collections.Generic.IEnumerable{Panel}, out System.Collections.Generic.List{Point3D}, out System.Collections.Generic.List{string}, out System.Collections.Generic.List{OrphanedAperture}, System.Collections.Generic.IEnumerable{double}, System.Collections.Generic.IEnumerable{double}, double, double, double, double, OcctBuildOptions, AutoTune3DOptions, double, double)"/>:
    /// wraps <c>AutoTune3DSolver</c> and reconstructs panels (Guid/construction preserved) just like
    /// <c>Solve3D</c>. Verifies the end-to-end wiring through the real kernel on a gappy room whose baseline
    /// closes only by fabricated patches, which AutoTune replaces with measured extension. Native-gated.
    /// </summary>
    public class AutoTune3DAnalyticalIntegrationTests
    {
        private static OcctBuildOptions Options()
        {
            return new OcctBuildOptions { AvoidInternalShapes = false, SewBeforeBuild = true, SewingTolerance = 0.01 };
        }

        private static Face3D Rect(Point3D a, Point3D b, Point3D c, Point3D d)
        {
            return TestGeometry.CreatePlanarFace(a, b, c, d);
        }

        /// <summary>A 4x4x3 room as Panels, with the x=4 wall pulled 0.2 m short of the y=0 wall (the gap the tiny
        /// baseline reach must fabricate over, and AutoTune then closes by extension). Returns the floor's Guid.</summary>
        private static List<Panel> GappyRoomPanels(out System.Guid floorGuid)
        {
            Panel floor = global::SAM.Analytical.Create.Panel(new Construction("Floor"), PanelType.Floor, Rect(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(0, 4, 0)));
            floorGuid = floor.Guid;

            return new List<Panel>
            {
                floor,
                global::SAM.Analytical.Create.Panel(new Construction("Roof"), PanelType.Roof, Rect(new Point3D(0, 0, 3), new Point3D(4, 0, 3), new Point3D(4, 4, 3), new Point3D(0, 4, 3))),
                global::SAM.Analytical.Create.Panel(new Construction("Wall"), PanelType.Wall, Rect(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 0, 3), new Point3D(0, 0, 3))), // y=0
                global::SAM.Analytical.Create.Panel(new Construction("Wall"), PanelType.Wall, Rect(new Point3D(0, 4, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(0, 4, 3))), // y=4
                global::SAM.Analytical.Create.Panel(new Construction("Wall"), PanelType.Wall, Rect(new Point3D(0, 0, 0), new Point3D(0, 4, 0), new Point3D(0, 4, 3), new Point3D(0, 0, 3))), // x=0
                global::SAM.Analytical.Create.Panel(new Construction("Wall"), PanelType.Wall, Rect(new Point3D(4, 0.2, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(4, 0.2, 3))) // x=4 SHORT
            };
        }

        [SkippableFact]
        public void AutoTune3D_GappyRoom_ClosesAndReconstructsPanelsPreservingGuid()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            List<Panel> input = GappyRoomPanels(out System.Guid floorGuid);
            List<double> maxExtends = Enumerable.Repeat(0.02, input.Count).ToList(); // tiny baseline reach -> forces fabrication then escalation

            // Act
            List<Panel> solved = input.AutoTune3D(
                out List<Point3D> nakedPoint3Ds,
                out List<string> diagnostics,
                out List<OrphanedAperture> orphanedApertures,
                maxExtends: maxExtends,
                options: Options());

            // Assert - a reconstructed panel set is returned, the room closed (no residual naked edges), and the
            // wrapper surfaced the AutoTune result line plus at least one structured escalation diagnostic.
            Assert.NotNull(solved);
            Assert.NotEmpty(solved);
            Assert.Empty(nakedPoint3Ds);
            Assert.Empty(orphanedApertures);
            Assert.Contains(diagnostics, d => d.StartsWith("SAM_OCCT_AUTOTUNE3D_RESULT"));
            Assert.Contains(diagnostics, d => d.Contains("EscalatedPanel"));

            // 1:1 reconstruction keeps the source Guid/construction (the floor, unchanged by escalation, maps 1:1).
            Panel resolvedFloor = solved.FirstOrDefault(x => x.Guid == floorGuid);
            Assert.NotNull(resolvedFloor);
            Assert.Equal("Floor", resolvedFloor.Construction?.Name);
        }
    }
}
