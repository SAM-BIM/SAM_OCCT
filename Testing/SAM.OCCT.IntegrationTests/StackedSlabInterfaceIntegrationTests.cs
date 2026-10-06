// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Phase 6d stacked-slab interface handling, exercised end-to-end through the real kernel
    /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §E Phase 6): a two-storey stacked floor/ceiling
    /// interface resolves cleanly with both analytical source panels represented and the interface detected; a
    /// genuine wide cavity is rejected (kept separate) and survives; a split-level landing is not mistaken for a
    /// slab interface. Detection is observational - it changes no geometry (the golden masters prove that
    /// separately), so these tests assert the closure is clean AND the detector/provenance behaviour is correct.
    /// </summary>
    public class StackedSlabInterfaceIntegrationTests
    {
        // A 4x4 horizontal cap at elevation z; up=true -> +Z normal (floor), up=false -> -Z normal (ceiling).
        private static Face3D Cap(double z, bool up)
        {
            return up
                ? TestGeometry.CreatePlanarFace(new Point3D(0, 0, z), new Point3D(4, 0, z), new Point3D(4, 4, z), new Point3D(0, 4, z))
                : TestGeometry.CreatePlanarFace(new Point3D(0, 0, z), new Point3D(0, 4, z), new Point3D(4, 4, z), new Point3D(4, 0, z));
        }

        // The 4 vertical walls of a 4x4 room between zB and zT.
        private static IEnumerable<Face3D> Walls(double zB, double zT)
        {
            yield return TestGeometry.CreatePlanarFace(new Point3D(0, 0, zB), new Point3D(4, 0, zB), new Point3D(4, 0, zT), new Point3D(0, 0, zT));
            yield return TestGeometry.CreatePlanarFace(new Point3D(0, 4, zB), new Point3D(4, 4, zB), new Point3D(4, 4, zT), new Point3D(0, 4, zT));
            yield return TestGeometry.CreatePlanarFace(new Point3D(0, 0, zB), new Point3D(0, 4, zB), new Point3D(0, 4, zT), new Point3D(0, 0, zT));
            yield return TestGeometry.CreatePlanarFace(new Point3D(4, 0, zB), new Point3D(4, 4, zB), new Point3D(4, 4, zT), new Point3D(4, 0, zT));
        }

        [SkippableFact]
        public void TwoStoreyStacked_ResolvesCleanly_BothSourcesRepresented_InterfaceDetected()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Two stacked rooms sharing a mid interface modelled as two coincident, opposite-facing skins at z=3
            // (room 0's ceiling and room 1's floor) - the zero-thickness inter-storey convention.
            List<Face3D> faces = new List<Face3D>
            {
                Cap(0, up: true),   // 0 - room 0 floor
                Cap(3, up: false),  // 1 - room 0 ceiling  (mid skin A)
                Cap(3, up: true),   // 2 - room 1 floor     (mid skin B)
                Cap(6, up: false),  // 3 - room 1 ceiling
            };
            faces.AddRange(Walls(0, 3)); // 4..7
            faces.AddRange(Walls(3, 6)); // 8..11

            Panel3DSnapSolver solver = new Panel3DSnapSolver(faces);
            solver.Execute(new OcctBuildOptions());

            // Clean closure: the two rooms form two cells, watertight, no unexpected naked edges.
            Assert.True(solver.NativeResolved, "Expected a native resolve");
            Assert.Equal(2, solver.ResolvedCellCount);
            Assert.Empty(solver.NakedEdgePoint3Ds);

            // The interface is detected across the two skins (indices 1 and 2).
            StackedSlabInterface detected = Assert.Single(solver.StackedSlabInterfaces);
            Assert.Equal(1, detected.LowerSourceIndex);
            Assert.Equal(2, detected.UpperSourceIndex);

            // Both analytical source panels of the interface survive into the output (provenance preserved).
            Assert.NotEmpty(solver.SourceMap.FacesOf(1));
            Assert.NotEmpty(solver.SourceMap.FacesOf(2));
            Assert.Contains(solver.Diagnostics.All, d => d.Code == DiagnosticCode.AdoptedLevel
                && d.Message.Contains("both analytical source panels", System.StringComparison.OrdinalIgnoreCase));
        }

        [SkippableFact]
        public void WideCavity_RejectedAsUnsafeMerge_CavitySurvives()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Two rooms with a genuine 0.4 m void between them: room 0 ceiling at z=3.0, room 1 floor at z=3.4.
            // Opposite-facing and congruent but far beyond a wall thickness - a real cavity/plenum, not a slab.
            List<Face3D> faces = new List<Face3D>
            {
                Cap(0, up: true),   // 0
                Cap(3.0, up: false),// 1 - room 0 ceiling
                Cap(3.4, up: true), // 2 - room 1 floor (0.4 m above)
                Cap(6.4, up: false),// 3
            };
            faces.AddRange(Walls(0, 3.0));   // 4..7
            faces.AddRange(Walls(3.4, 6.4)); // 8..11

            Panel3DSnapSolver solver = new Panel3DSnapSolver(faces);
            solver.Execute(new OcctBuildOptions());

            // The wide pair is NOT treated as a stacked-slab interface (it would collapse a real void).
            Assert.DoesNotContain(solver.StackedSlabInterfaces, x =>
                (x.LowerSourceIndex == 1 && x.UpperSourceIndex == 2) || (x.LowerSourceIndex == 2 && x.UpperSourceIndex == 1));
            Assert.Contains(solver.Diagnostics.All, d => d.Code == DiagnosticCode.RejectedCollapse);

            // The cavity survives: both rooms remain their own watertight cells (the void is not merged away).
            Assert.True(solver.ResolvedCellCount >= 2, $"Expected the void to be kept (>= 2 cells), got {solver.ResolvedCellCount}");
            Assert.NotEmpty(solver.SourceMap.FacesOf(1));
            Assert.NotEmpty(solver.SourceMap.FacesOf(2));
        }

        [SkippableFact]
        public void SplitLevelLanding_NotDetectedAsStackedInterface()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // A single room whose floor carries a small raised landing 0.25 m above it (a step) - a split level,
            // not an inter-storey slab. The landing must not be detected as a stacked interface.
            List<Face3D> faces = new List<Face3D>
            {
                Cap(0, up: true), // 0 - main floor 4x4
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0.25), new Point3D(1, 0, 0.25), new Point3D(1, 1, 0.25), new Point3D(0, 1, 0.25)), // 1 - small landing
                Cap(3, up: false), // 2 - ceiling
            };
            faces.AddRange(Walls(0, 3)); // 3..6

            Panel3DSnapSolver solver = new Panel3DSnapSolver(faces);
            solver.Execute(new OcctBuildOptions());

            // No stacked-slab interface involves the landing (index 1): a same-facing / partial step is not a
            // floor/ceiling inter-storey pair.
            Assert.DoesNotContain(solver.StackedSlabInterfaces, x => x.LowerSourceIndex == 1 || x.UpperSourceIndex == 1);
        }
    }
}
