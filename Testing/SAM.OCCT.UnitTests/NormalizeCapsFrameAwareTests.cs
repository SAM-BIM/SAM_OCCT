// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for the Phase 6c frame-aware cap normalization
    /// (<see cref="Panel3DSnapSolver.NormalizeCaps(List{SnappedPanel}, System.Collections.Generic.IReadOnlyList{LevelFrame}, double, double, double)"/>):
    /// caps are normalized onto their own <see cref="LevelFrame"/>'s datum (a ~0.15 m elevation band) instead of
    /// the legacy flat 0.3 m band, so a split-level landing keeps its own elevation instead of being flattened
    /// onto the floor below it (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §E Phase 6, "do not normalize
    /// away split levels"). Pure-managed: no native OCCT DLL required.
    /// <para>
    /// Scope note: Phase 6c lands the frame-aware NormalizeCaps only. The per-frame extend/fill conditioning was
    /// deferred (it regressed the whole-level-tilted raw golden master, whose single analytical level spans two
    /// very different cap tilts); these tests therefore exercise NormalizeCaps directly, not the whole pipeline.
    /// </para>
    /// </summary>
    public class NormalizeCapsFrameAwareTests
    {
        private const double DegToRad = Math.PI / 180.0;

        /// <summary>A horizontal (normal +Z) square cap tile of side <paramref name="size"/> at elevation
        /// <paramref name="z"/>, its lower-left corner at the origin, wrapped as a <see cref="SnappedPanel"/>
        /// carrying source index <paramref name="index"/>.</summary>
        private static SnappedPanel Cap(int index, double size, double z)
        {
            Face3D face3D = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, z),
                new Point3D(size, 0, z),
                new Point3D(size, size, z),
                new Point3D(0, size, z));
            return new SnappedPanel(index, face3D, 1.0, 0.3, 0.4);
        }

        /// <summary>Elevation (world Z) of a horizontal cap panel - its bounding box floor.</summary>
        private static double Elevation(SnappedPanel panel)
        {
            return panel.GetBoundingBox().Min.Z;
        }

        private static List<LevelFrame> ClusterCaps(IEnumerable<SnappedPanel> panels)
        {
            return LevelFrame.Cluster(panels.Select(p => p.Face3D).ToList());
        }

        /// <summary>A large floor at z=0, a small same-level tile 2 cm above it, a split-level landing 25 cm
        /// above it, and a ceiling at z=3.</summary>
        private static List<SnappedPanel> LandingScene()
        {
            return new List<SnappedPanel>
            {
                Cap(0, 4.0, 0.00), // main floor (backer)
                Cap(1, 1.0, 0.02), // same-level tile (import noise, 2 cm)
                Cap(2, 1.0, 0.25), // split-level landing (a real step)
                Cap(3, 4.0, 3.00), // ceiling
            };
        }

        [Fact]
        public void NormalizeCaps_FrameAware_PreservesSplitLevelLanding()
        {
            // Arrange - floor, a 2 cm same-level tile, a 25 cm landing, and a ceiling.
            List<SnappedPanel> panels = LandingScene();
            List<LevelFrame> frames = ClusterCaps(panels);

            // Act - normalize per level frame (0.15 m band).
            Panel3DSnapSolver.NormalizeCaps(panels, frames, toleranceAngle: 5 * DegToRad, toleranceDistance: 1e-6);

            // Assert - the 2 cm tile snaps onto the floor (same frame); the 25 cm landing is PRESERVED
            // (its own frame, never merged onto the floor); the ceiling is untouched.
            Assert.Equal(0.00, Elevation(panels[0]), 3); // floor
            Assert.Equal(0.00, Elevation(panels[1]), 3); // same-level tile snapped onto the floor datum
            Assert.Equal(0.25, Elevation(panels[2]), 3); // landing preserved - the Phase 6c fix
            Assert.Equal(3.00, Elevation(panels[3]), 3); // ceiling
        }

        [Fact]
        public void NormalizeCaps_Legacy_MergesSplitLevelLandingAway()
        {
            // Contrast/regression lock: the legacy world-frame overload (0.3 m NormalizeCapOffset) flattens the
            // 0.25 m landing onto the floor (0.25 < 0.3), the exact defect the frame-aware overload fixes.
            SnappedPanel floor = Cap(0, 4.0, 0.00);
            SnappedPanel landing = Cap(1, 1.0, 0.25);
            List<SnappedPanel> panels = new List<SnappedPanel> { floor, landing };

            Panel3DSnapSolver.NormalizeCaps(panels, toleranceAngle: 5 * DegToRad, normalizeCapOffset: 0.3, toleranceDistance: 1e-6);

            Assert.Equal(0.00, Elevation(landing), 3); // legacy merges the landing onto the floor
        }

        [Fact]
        public void NormalizeCaps_FrameAware_SnapsGenuineSameLevelTilesToDatum()
        {
            // The fix must not stop normalizing genuine same-level tiles: three floor tiles within the 0.15 m
            // band (0, 3 cm, 6 cm) all snap onto the largest tile's plane (the datum).
            SnappedPanel backer = Cap(0, 4.0, 0.00);
            SnappedPanel tileA = Cap(1, 1.0, 0.03);
            SnappedPanel tileB = Cap(2, 1.0, 0.06);
            List<SnappedPanel> panels = new List<SnappedPanel> { backer, tileA, tileB };
            List<LevelFrame> frames = ClusterCaps(panels);

            Panel3DSnapSolver.NormalizeCaps(panels, frames, toleranceAngle: 5 * DegToRad, toleranceDistance: 1e-6);

            Assert.Equal(0.00, Elevation(backer), 3);
            Assert.Equal(0.00, Elevation(tileA), 3); // snapped onto the datum
            Assert.Equal(0.00, Elevation(tileB), 3); // snapped onto the datum
        }

        [Fact]
        public void NormalizeCaps_FrameAware_IsDeterministicUnderInputOrder()
        {
            // Shuffling the input order must not change which caps are normalized or where they land.
            List<SnappedPanel> order1 = LandingScene();
            List<SnappedPanel> order2 = LandingScene();
            order2.Reverse();

            Panel3DSnapSolver.NormalizeCaps(order1, ClusterCaps(order1), 5 * DegToRad, 1e-6);
            Panel3DSnapSolver.NormalizeCaps(order2, ClusterCaps(order2), 5 * DegToRad, 1e-6);

            Dictionary<int, double> byIndex2 = order2.ToDictionary(p => p.SourceIndices[0], p => Elevation(p));
            foreach (SnappedPanel panel in order1)
            {
                Assert.Equal(Elevation(panel), byIndex2[panel.SourceIndices[0]], 6);
            }
        }

        [Fact]
        public void NormalizeCaps_FrameAware_TwoStackedLevelsDoNotMerge()
        {
            // Two genuinely stacked levels (floor+ceiling at z=0/3 and z=3/6): the shared slab region at z=3 is a
            // single frame, but the two storeys' floors (z=0, z=3) are metres apart and must stay distinct - no
            // frame collapses one level onto another.
            List<SnappedPanel> panels = new List<SnappedPanel>
            {
                Cap(0, 4.0, 0.0),
                Cap(1, 4.0, 3.0),
                Cap(2, 4.0, 6.0),
            };
            List<LevelFrame> frames = ClusterCaps(panels);

            Panel3DSnapSolver.NormalizeCaps(panels, frames, 5 * DegToRad, 1e-6);

            Assert.Equal(0.0, Elevation(panels[0]), 3);
            Assert.Equal(3.0, Elevation(panels[1]), 3);
            Assert.Equal(6.0, Elevation(panels[2]), 3);
        }

        [Fact]
        public void NormalizeCaps_FrameAware_NoFrames_IsNoOp()
        {
            // With no level frames the frame-aware overload does nothing (the caller falls back to the legacy
            // world-frame overload); the caps keep their original elevations.
            SnappedPanel floor = Cap(0, 4.0, 0.00);
            SnappedPanel landing = Cap(1, 1.0, 0.25);
            List<SnappedPanel> panels = new List<SnappedPanel> { floor, landing };

            Panel3DSnapSolver.NormalizeCaps(panels, new List<LevelFrame>(), 5 * DegToRad, 1e-6);

            Assert.Equal(0.00, Elevation(floor), 3);
            Assert.Equal(0.25, Elevation(landing), 3);
        }
    }
}
