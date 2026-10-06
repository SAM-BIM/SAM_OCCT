// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Analytical.Solver;
using SAM.Core;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Phase 4 acceptance (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md): a solved panel keeps its
    /// source's Guid, parameters and construction, and re-hosts its trimmed aperture, on the simplest
    /// case a native resolve can produce - a single sealed room with no split or merge (the raw-first
    /// path adopts the input as-is, watertight with zero naked edges). Split/merge Guid policy is
    /// exercised without native geometry in <c>PanelReconstructionTests</c> (unit); this test locks the
    /// end-to-end wiring through the real kernel. Native-gated.
    /// </summary>
    public class AperturePreservationIntegrationTests
    {
        private static Face3D Rect(Point3D a, Point3D b, Point3D c, Point3D d)
        {
            return TestGeometry.CreatePlanarFace(a, b, c, d);
        }

        /// <summary>4x4x3 m sealed room: 4 walls + floor + roof, all coplanar-clean and gap-free so the
        /// raw-first path adopts it directly with zero naked edges (no split/merge expected).</summary>
        private static List<Panel> SealedRoomPanels(Construction wallConstruction, out Panel windowWall)
        {
            List<Panel> panels = new List<Panel>
            {
                global::SAM.Analytical.Create.Panel(new Construction("Floor"), PanelType.Floor, Rect(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(0, 4, 0))),
                global::SAM.Analytical.Create.Panel(new Construction("Roof"), PanelType.Roof, Rect(new Point3D(0, 0, 3), new Point3D(4, 0, 3), new Point3D(4, 4, 3), new Point3D(0, 4, 3))),
                global::SAM.Analytical.Create.Panel(wallConstruction, PanelType.Wall, Rect(new Point3D(0, 4, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(0, 4, 3))), // y=4
                global::SAM.Analytical.Create.Panel(wallConstruction, PanelType.Wall, Rect(new Point3D(0, 0, 0), new Point3D(0, 4, 0), new Point3D(0, 4, 3), new Point3D(0, 0, 3))), // x=0
                global::SAM.Analytical.Create.Panel(wallConstruction, PanelType.Wall, Rect(new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(4, 4, 3), new Point3D(4, 0, 3))), // x=4
            };

            // The window wall (y=0) gets built separately so its Guid/aperture can be tracked below.
            Panel wall = global::SAM.Analytical.Create.Panel(wallConstruction, PanelType.Wall, Rect(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, 0, 3), new Point3D(0, 0, 3)));

            ApertureConstruction windowConstruction = new ApertureConstruction("Test Window", ApertureType.Window);
            Aperture window = new Aperture(windowConstruction, new Polygon3D(new List<Point3D>
            {
                new Point3D(1, 0, 1), new Point3D(2, 0, 1), new Point3D(2, 0, 2), new Point3D(1, 0, 2)
            }));

            windowWall = global::SAM.Analytical.Create.Panel(wall.Guid, wall, wall.GetFace3D(), new[] { window }, true, Tolerance.MacroDistance, Tolerance.MacroDistance);
            panels.Add(windowWall);

            return panels;
        }

        [SkippableFact]
        public void Solve3D_SealedRoomWithWindow_PreservesGuidConstructionParametersAndAperture()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            Construction wallConstruction = new Construction("Test Wall");
            List<Panel> input = SealedRoomPanels(wallConstruction, out Panel windowWall);

            Assert.NotEmpty(windowWall.Apertures); // sanity: the aperture attached to the source itself

            List<Panel> solved = input.Solve3D(out List<Point3D> nakedPoint3Ds, out List<string> diagnostics, out List<OrphanedAperture> orphanedApertures);

            Assert.NotNull(solved);
            Assert.NotEmpty(solved);
            Assert.Empty(orphanedApertures); // nothing should have failed to re-host on a clean sealed box

            // The window wall should map 1:1 (a closed, gap-free box gives the raw path nothing to split or
            // merge), so it keeps the source's own Guid rather than a fresh one.
            Panel resolvedWindowWall = solved.FirstOrDefault(x => x.Guid == windowWall.Guid);
            Assert.NotNull(resolvedWindowWall);
            Assert.Equal(wallConstruction.Guid, resolvedWindowWall.Construction.Guid);
            Assert.Equal(PanelType.Wall, resolvedWindowWall.PanelType);

            List<Aperture> resolvedApertures = resolvedWindowWall.Apertures;
            Assert.NotNull(resolvedApertures);
            Aperture resolvedWindow = Assert.Single(resolvedApertures);
            Assert.Equal(windowWall.Apertures[0].Guid, resolvedWindow.Guid);

            // The window's own geometry survives essentially unchanged (no trimming was needed on an
            // already-closed box), confirming the re-host was not a fabricated/degenerate stand-in.
            Assert.True(resolvedWindow.GetFace3D().GetArea() > 0.9); // the original 1x1 m window, minus float noise

            // Parameter round-trip: the source's own bucket/weight/max-extend stamps (default-derived, since
            // none were supplied here) survive reconstruction, matching the 2D output contract.
            Assert.True(resolvedWindowWall.TryGetValue(SolverParameter.Weight, out double _));
        }
    }
}
