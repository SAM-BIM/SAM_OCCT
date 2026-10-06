// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="StackedSlabInterfaceDetector"/> - the Phase 6d inter-storey stacked-slab
    /// interface detection (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §E Phase 6). Pure-managed: no native
    /// OCCT DLL required. Covers the acceptance list: a valid opposed floor/ceiling interface is detected; a
    /// split-level landing (co-parallel same-facing) and a partial step (low overlap) are rejected; a wide
    /// cavity/shaft is rejected; detection is deterministic; and the provenance verification maps both sources.
    /// </summary>
    public class StackedSlabInterfaceDetectorTests
    {
        private const double DegToRad = System.Math.PI / 180.0;

        /// <summary>A horizontal square cap of side <paramref name="size"/> at elevation <paramref name="z"/>,
        /// with its lower-left corner at (<paramref name="x0"/>, <paramref name="y0"/>). <paramref name="up"/>
        /// picks the winding so the normal is +Z (a floor facing up) or -Z (a ceiling facing down).</summary>
        private static Face3D Cap(double x0, double y0, double size, double z, bool up)
        {
            return up
                ? TestGeometry.CreatePlanarFace(
                    new Point3D(x0, y0, z), new Point3D(x0 + size, y0, z), new Point3D(x0 + size, y0 + size, z), new Point3D(x0, y0 + size, z))
                : TestGeometry.CreatePlanarFace(
                    new Point3D(x0, y0, z), new Point3D(x0, y0 + size, z), new Point3D(x0 + size, y0 + size, z), new Point3D(x0 + size, y0, z));
        }

        /// <summary>A vertical wall in the plane x = <paramref name="x"/> - included to prove walls are ignored.</summary>
        private static Face3D Wall(double x)
        {
            return TestGeometry.CreatePlanarFace(new Point3D(x, 0, 0), new Point3D(x, 4, 0), new Point3D(x, 4, 3), new Point3D(x, 0, 3));
        }

        [Fact]
        public void DetectStackedInterfaces_OpposedCongruentSkins_DetectsOneInterface()
        {
            // A ceiling-below (-Z) at z=2.85 and a floor-above (+Z) at z=3.0: opposite-facing, congruent, 0.15 m
            // apart (a slab thickness) - a genuine inter-storey interface. A wall is present to prove it is skipped.
            List<Face3D> faces = new List<Face3D>
            {
                Cap(0, 0, 4, 2.85, up: false), // index 0 - ceiling of the level below
                Cap(0, 0, 4, 3.00, up: true),  // index 1 - floor of the level above
                Wall(0),                        // index 2 - ignored
            };

            List<StackedSlabInterface> interfaces = StackedSlabInterfaceDetector.DetectStackedInterfaces(faces);

            StackedSlabInterface detected = Assert.Single(interfaces);
            Assert.Equal(0, detected.LowerSourceIndex); // z=2.85 is lower
            Assert.Equal(1, detected.UpperSourceIndex); // z=3.00 is upper
            Assert.True(detected.OverlapRatio > 0.99, "Congruent skins should overlap ~fully");
            Assert.True(detected.Separation > 0.14 && detected.Separation < 0.16, $"Separation should be ~0.15 m, got {detected.Separation}");
        }

        [Fact]
        public void DetectStackedInterfaces_CoincidentOpposedSkins_DetectsOneInterface()
        {
            // The zero-thickness analytical convention: ceiling-below and floor-above modelled coincident at z=3.
            List<Face3D> faces = new List<Face3D>
            {
                Cap(0, 0, 4, 3.0, up: false),
                Cap(0, 0, 4, 3.0, up: true),
            };

            StackedSlabInterface detected = Assert.Single(StackedSlabInterfaceDetector.DetectStackedInterfaces(faces));
            Assert.True(detected.Separation < 1e-6, "Coincident skins have ~zero separation");
        }

        [Fact]
        public void DetectStackedInterfaces_CoParallelSplitLevel_RejectedNotDetected()
        {
            // Two SAME-facing (both +Z) congruent floors 0.25 m apart - a split-level whole floor / double skin,
            // NOT an opposite-rooms stacked interface. Must not be detected, and the ambiguity is diagnosed.
            List<Face3D> faces = new List<Face3D>
            {
                Cap(0, 0, 4, 0.00, up: true),
                Cap(0, 0, 4, 0.25, up: true),
            };
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            List<StackedSlabInterface> interfaces = StackedSlabInterfaceDetector.DetectStackedInterfaces(faces, diagnostics: diagnostics);

            Assert.Empty(interfaces);
            Assert.Contains(diagnostics.All, d => d.Code == DiagnosticCode.RejectedCollapse);
        }

        [Fact]
        public void DetectStackedInterfaces_PartialStepLanding_RejectedLowOverlap()
        {
            // A small raised landing (1x1) sitting opposite a large floor (4x4) 0.2 m below it: opposite-facing but
            // the footprints are far from congruent (overlap ~1/16), so it is a split-level step, not a slab skin.
            List<Face3D> faces = new List<Face3D>
            {
                Cap(0, 0, 4, 0.0, up: true),  // large floor
                Cap(0, 0, 1, 0.2, up: false), // small landing above a corner, opposite-facing
            };
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            List<StackedSlabInterface> interfaces = StackedSlabInterfaceDetector.DetectStackedInterfaces(faces, diagnostics: diagnostics);

            Assert.Empty(interfaces);
            Assert.Contains(diagnostics.All, d => d.Code == DiagnosticCode.RejectedCollapse);
        }

        [Fact]
        public void DetectStackedInterfaces_WideCavity_RejectedNotDetected()
        {
            // Opposite-facing congruent caps 0.6 m apart - a genuine plenum/cavity/shaft the plan requires be kept,
            // not a structural slab. Rejected (beyond the 0.3 m slab band) and diagnosed.
            List<Face3D> faces = new List<Face3D>
            {
                Cap(0, 0, 4, 3.0, up: false),
                Cap(0, 0, 4, 3.6, up: true),
            };
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            List<StackedSlabInterface> interfaces = StackedSlabInterfaceDetector.DetectStackedInterfaces(faces, diagnostics: diagnostics);

            Assert.Empty(interfaces);
            Assert.Contains(diagnostics.All, d => d.Code == DiagnosticCode.RejectedCollapse);
        }

        [Fact]
        public void DetectStackedInterfaces_SideBySideCaps_NotDetected()
        {
            // Two opposite-facing caps at a slab separation but in DIFFERENT plan locations (no XY overlap):
            // adjacent rooms, not one interface. The plan pre-filter drops them; nothing is detected.
            List<Face3D> faces = new List<Face3D>
            {
                Cap(0, 0, 4, 3.0, up: false),
                Cap(10, 0, 4, 3.15, up: true),
            };

            Assert.Empty(StackedSlabInterfaceDetector.DetectStackedInterfaces(faces));
        }

        [Fact]
        public void DetectStackedInterfaces_MultipleInterfaces_DeterministicOrderByElevation()
        {
            // Two stacked interfaces (a 3-storey stack). Detection order is deterministic (by elevation) and
            // independent of the input face order.
            List<Face3D> ordered = new List<Face3D>
            {
                Cap(0, 0, 4, 3.0, up: false), // 0
                Cap(0, 0, 4, 3.0, up: true),  // 1  -> interface 0/1 at z=3
                Cap(0, 0, 4, 6.0, up: false), // 2
                Cap(0, 0, 4, 6.0, up: true),  // 3  -> interface 2/3 at z=6
            };
            List<Face3D> shuffled = new List<Face3D> { ordered[3], ordered[1], ordered[2], ordered[0] };

            List<StackedSlabInterface> a = StackedSlabInterfaceDetector.DetectStackedInterfaces(ordered);
            List<StackedSlabInterface> b = StackedSlabInterfaceDetector.DetectStackedInterfaces(ordered);

            Assert.Equal(2, a.Count);
            Assert.True(a[0].Elevation < a[1].Elevation, "Interfaces should be ordered by ascending elevation");
            // Determinism: same input -> identical (index, elevation) sequence.
            Assert.Equal(a.Select(x => (x.LowerSourceIndex, x.UpperSourceIndex)), b.Select(x => (x.LowerSourceIndex, x.UpperSourceIndex)));
            // And the shuffled input still finds the same two interfaces (by source index pair).
            List<StackedSlabInterface> c = StackedSlabInterfaceDetector.DetectStackedInterfaces(shuffled);
            Assert.Equal(2, c.Count);
        }

        [Fact]
        public void VerifyRepresented_BothSourcesMapped_EmitsPreservedInfo()
        {
            StackedSlabInterface iface = new StackedSlabInterface(0, 1, 0, 0, 0.0, 1.0, 3.0);
            SourceMap sourceMap = new SourceMap();
            sourceMap.RecordMerge(new[] { 0, 1 }, new FaceKey(7), Provenance.Resolved); // both map to the shared face
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            StackedSlabInterfaceDetector.VerifyRepresented(new[] { iface }, sourceMap, diagnostics);

            Assert.Contains(diagnostics.All, d => d.Code == DiagnosticCode.AdoptedLevel && d.Severity == OcctDiagnosticSeverity.Info);
            Assert.DoesNotContain(diagnostics.All, d => d.Code == DiagnosticCode.DroppedFace);
        }

        [Fact]
        public void VerifyRepresented_OneSourceMissing_EmitsIncompleteWarning()
        {
            StackedSlabInterface iface = new StackedSlabInterface(0, 1, 0, 0, 0.0, 1.0, 3.0);
            SourceMap sourceMap = new SourceMap();
            sourceMap.Record(0, new FaceKey(7), Provenance.Resolved); // only source 0 is represented
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            StackedSlabInterfaceDetector.VerifyRepresented(new[] { iface }, sourceMap, diagnostics);

            SolverDiagnostic warning = Assert.Single(diagnostics.All.Where(d => d.Code == DiagnosticCode.DroppedFace));
            Assert.Equal(OcctDiagnosticSeverity.Warning, warning.Severity);
        }
    }
}
