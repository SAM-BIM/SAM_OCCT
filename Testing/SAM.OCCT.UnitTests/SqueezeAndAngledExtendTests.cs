// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Mechanism-level, synthetic demonstrations (no native OCCT) of the two controlled-workflow primitives a
    /// user tunes from Grasshopper:
    /// <list type="bullet">
    /// <item><b>Squeeze</b> - the bucket-snap that collapses two near-coplanar parallel skins onto one plane
    /// (<see cref="Panel3DSnapSolver.Snap"/> for a same-facing double-wall, <see cref="Panel3DSnapSolver.SnapOpposedPartitions"/>
    /// for an anti-parallel back-to-back partition). Shown for VERTICAL, HORIZONTAL and TILTED panels, and at
    /// the in-plane-overlap gate that decides collapse-vs-keep (the exact edge behind the 9-space fixture's
    /// East1|South1 gap: two skins at ~0.967 overlap, just under the 0.97 floor, are correctly left apart).</item>
    /// <item><b>Angled extend</b> - the cap-extend that grows a wall up to a ceiling/roof
    /// (<see cref="Panel3DSnapSolver.Extend"/>). Shown across tilt angles: a wall within the verticality
    /// tolerance reaches the cap; one tilted beyond it is left untouched (it is no longer treated as a wall to
    /// raise), and the wall's own plane is preserved either way.</item>
    /// </list>
    /// These pin the observable input effects so "the levers do something" is regression-guarded, and they
    /// isolate the collapse/extend gates from the full 9-space chain.
    /// </summary>
    public class SqueezeAndAngledExtendTests
    {
        private const double ParallelTolerance = 5 * Math.PI / 180;
        private const double ArcTolerance = 0.3 * Math.PI / 180;
        private const double VerticalTolerance = 20 * Math.PI / 180;

        private static Face3D Quad(params Point3D[] points) => TestGeometry.CreatePlanarFace(points);

        /// <summary>Two same-facing parallel skins offset by <paramref name="separation"/> along their shared
        /// normal, at the given orientation, with a full (100%) in-plane overlap. Backer weight 2, candidate
        /// weight 1, so the candidate is the one that snaps onto the backer.</summary>
        private static (SnappedPanel backer, SnappedPanel candidate) ParallelPair(string orientation, double separation)
        {
            Face3D a, b;
            switch (orientation)
            {
                case "vertical": // Y-normal wall, rectangle in X-Z
                    a = Quad(new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 0, 3), new Point3D(0, 0, 3));
                    b = Quad(new Point3D(0, separation, 0), new Point3D(2, separation, 0), new Point3D(2, separation, 3), new Point3D(0, separation, 3));
                    break;
                case "horizontal": // Z-normal slab, rectangle in X-Y
                    a = Quad(new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 3, 0), new Point3D(0, 3, 0));
                    b = Quad(new Point3D(0, 0, separation), new Point3D(2, 0, separation), new Point3D(2, 3, separation), new Point3D(0, 3, separation));
                    break;
                case "tilted": // 30 deg off vertical, second skin offset by `separation` in +Y
                    double t = 30 * Math.PI / 180, sin = Math.Sin(t), cos = Math.Cos(t);
                    a = Quad(new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 3 * sin, 3 * cos), new Point3D(0, 3 * sin, 3 * cos));
                    b = Quad(new Point3D(0, separation, 0), new Point3D(2, separation, 0), new Point3D(2, separation + 3 * sin, 3 * cos), new Point3D(0, separation + 3 * sin, 3 * cos));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(orientation), orientation, null);
            }

            return (new SnappedPanel(0, a, weight: 2.0, bucketSize: 0.4, maxExtension: 0.5),
                    new SnappedPanel(1, b, weight: 1.0, bucketSize: 0.4, maxExtension: 0.5));
        }

        // ── Squeeze: same-facing parallel skins collapse onto one plane, every orientation ──────────────

        [Theory]
        [InlineData("vertical")]
        [InlineData("horizontal")]
        [InlineData("tilted")]
        public void Snap_ParallelSkinsWithinBucket_SqueezeOntoOnePlane(string orientation)
        {
            (SnappedPanel backer, SnappedPanel candidate) = ParallelPair(orientation, separation: 0.15);

            Assert.True(backer.PerpendicularSeparation(candidate) > 0.1, "Skins start apart.");
            Assert.True(backer.BucketContains(candidate, out _), "Skins are within the bucket slab.");

            bool changed = Panel3DSnapSolver.Snap(new List<SnappedPanel> { backer, candidate }, ParallelTolerance, ArcTolerance);

            Assert.True(changed);
            Assert.True(candidate.Snapped, "The lower-weight skin snaps onto the backer.");
            Assert.True(backer.PerpendicularSeparation(candidate) < 1e-6, "The two skins are now coplanar (squeezed to one plane).");
        }

        [Fact]
        public void Snap_ParallelSkinsBeyondBucket_NotSqueezed()
        {
            // 0.6 m apart, well outside the 0.4 m bucket: distinct walls a room apart, not a double-wall.
            (SnappedPanel backer, SnappedPanel candidate) = ParallelPair("vertical", separation: 0.6);

            bool changed = Panel3DSnapSolver.Snap(new List<SnappedPanel> { backer, candidate }, ParallelTolerance, ArcTolerance);

            Assert.False(changed);
            Assert.False(candidate.Snapped);
            Assert.True(backer.PerpendicularSeparation(candidate) > 0.5, "Skins beyond the bucket stay apart.");
        }

        // ── Squeeze: the opposed-partition in-plane-overlap gate (the East1|South1 edge) ────────────────

        /// <summary>A back-to-back (anti-parallel) partition whose two skins share their full footprint
        /// collapses onto one plane.</summary>
        [Fact]
        public void SnapOpposedPartitions_FullOverlap_Collapses()
        {
            (SnappedPanel a, SnappedPanel b) = OpposedPair(lateralShift: 0.0);

            Assert.True(a.InPlaneOverlapRatio(b) > 0.97, "Full-footprint overlap, above the 0.97 collapse floor.");
            Panel3DSnapSolver.SnapOpposedPartitions(new List<SnappedPanel> { a, b }, ParallelTolerance, 1e-6);

            Assert.True(a.Snapped || b.Snapped, "A full-overlap opposed partition collapses.");
            Assert.True(a.PerpendicularSeparation(b) < 1e-6, "The skins are now coplanar.");
        }

        /// <summary>The 9-space fixture's East1|South1 signature in isolation: two anti-parallel skins whose
        /// in-plane overlap sits just under the 0.97 floor (here ~0.965, matching the real ~0.9676) are
        /// correctly left apart - they read as two distinct walls, not one partition, so no per-panel input can
        /// force the collapse (the gate is footprint geometry, not capture distance).</summary>
        [Fact]
        public void SnapOpposedPartitions_JustUnderOverlapFloor_LeftApart()
        {
            (SnappedPanel a, SnappedPanel b) = OpposedPair(lateralShift: 0.07);

            double overlap = a.InPlaneOverlapRatio(b);
            Assert.InRange(overlap, 0.95, Panel3DSnapSolver.OPPOSED_PARTITION_MIN_OVERLAP_RATIO); // just under 0.97
            double separationBefore = a.PerpendicularSeparation(b);

            Panel3DSnapSolver.SnapOpposedPartitions(new List<SnappedPanel> { a, b }, ParallelTolerance, 1e-6);

            Assert.False(a.Snapped, "Below the overlap floor the pair is left apart (distinct walls).");
            Assert.False(b.Snapped);
            Assert.Equal(separationBefore, a.PerpendicularSeparation(b), 6);
        }

        /// <summary>Two anti-parallel (opposed) skins 0.15 m apart, the second laterally shifted by
        /// <paramref name="lateralShift"/> so their in-plane overlap drops below 1.0.</summary>
        private static (SnappedPanel a, SnappedPanel b) OpposedPair(double lateralShift)
        {
            Face3D a = Quad(new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 0, 3), new Point3D(0, 0, 3));
            // Reversed winding flips b's normal so it opposes a (anti-parallel), i.e. a genuine back-to-back partition.
            Face3D b = Quad(new Point3D(lateralShift, 0.15, 3), new Point3D(2 + lateralShift, 0.15, 3), new Point3D(2 + lateralShift, 0.15, 0), new Point3D(lateralShift, 0.15, 0));
            return (new SnappedPanel(0, a, 2.0, 0.4, 0.5), new SnappedPanel(1, b, 1.0, 0.4, 0.5));
        }

        // ── Angled extend: a wall grows to its cap within the verticality tolerance, not beyond ─────────

        [Theory]
        [InlineData(0.0)]
        [InlineData(10.0)]
        [InlineData(20.0)]
        public void Extend_WallWithinVerticalTolerance_ReachesCapAndKeepsPlane(double tiltDegrees)
        {
            (SnappedPanel wall, SnappedPanel cap) = WallUnderFlatCap(tiltDegrees, capZ: 4.0);
            Vector3D normalBefore = wall.Face3D.GetPlane().Normal.Unit;

            Panel3DSnapSolver.Extend(new List<SnappedPanel> { wall, cap }, VerticalTolerance, overshoot: 0.05, toleranceDistance: 1e-6);

            Assert.True(wall.GetBoundingBox().Max.Z > 3.9, $"A wall {tiltDegrees} deg off vertical should reach the cap at z=4.");
            Assert.Equal(1.0, Math.Abs(normalBefore.DotProduct(wall.Face3D.GetPlane().Normal.Unit)), 3); // extended in its own plane
        }

        [Theory]
        [InlineData(30.0)]
        [InlineData(45.0)]
        public void Extend_WallBeyondVerticalTolerance_LeftUntouched(double tiltDegrees)
        {
            // Past the 20 deg verticality tolerance the panel is no longer classified as a wall to raise, so the
            // cap-extend pass leaves it exactly as-is (rather than distorting a steeply-sloped panel).
            (SnappedPanel wall, SnappedPanel cap) = WallUnderFlatCap(tiltDegrees, capZ: 4.0);
            double topBefore = wall.GetBoundingBox().Max.Z;

            Panel3DSnapSolver.Extend(new List<SnappedPanel> { wall, cap }, VerticalTolerance, overshoot: 0.05, toleranceDistance: 1e-6);

            Assert.Equal(topBefore, wall.GetBoundingBox().Max.Z, 6);
        }

        /// <summary>A wall tilted <paramref name="tiltDegrees"/> off vertical (leaning in +Y as it rises to
        /// z=3), with a flat cap spanning above it at <paramref name="capZ"/>.</summary>
        private static (SnappedPanel wall, SnappedPanel cap) WallUnderFlatCap(double tiltDegrees, double capZ)
        {
            double t = tiltDegrees * Math.PI / 180, depth = 3 * Math.Tan(t);
            SnappedPanel wall = new SnappedPanel(0, Quad(
                new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(4, depth, 3), new Point3D(0, depth, 3)), 1, 0.3, 0.5);
            SnappedPanel cap = new SnappedPanel(1, Quad(
                new Point3D(-1, -1, capZ), new Point3D(5, -1, capZ), new Point3D(5, 2, capZ), new Point3D(-1, 2, capZ)), 1, 0.3, 0.5);
            return (wall, cap);
        }
    }
}
