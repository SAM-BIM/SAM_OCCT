// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Wall-to-cap SELECTION contract (Root Cause B2): a wall extends to a cap only when the cap's ACTUAL
    /// face relates to the wall — Case A (the wall sample projects inside the real cap face, valid at any
    /// vertical gap) or Case B (a graze: the sample is outside the face, valid only within the local-level
    /// continuation band <see cref="Panel3DSnapSolver.CAP_LOCAL_LEVEL_CONTINUATION"/>). No dependence on the
    /// cap's axis-aligned bounding box, world <c>normal.Z</c> flatness, a pitch threshold, or a pitched-cap
    /// exemption.
    /// <para>The "real cap face" excludes internal openings: Case A uses <see cref="Face3D.On"/>
    /// (outer-boundary-inclusive, holes excluded), not <see cref="Face3D.InRange"/> (outer loop only). A
    /// wall sample under an atrium/stairwell opening is therefore NOT physically covered — it is eligible
    /// only through the bounded Case B continuation, exactly like a sample outside the cap footprint. Tests
    /// 13–17 exercise this; test 15 fails before the hole-aware swap.</para>
    /// <para>These exercise the production selection through the public <see cref="Panel3DSnapSolver.Extend"/>
    /// (pure managed, no native) and assert the observable outcome — the wall's post-extend Z extent — rather
    /// than reimplementing the selection. Cases marked "fails at 20a7665" are the ones that distinguish this
    /// rule from the superseded AABB-containment + 15°-flatness rule.</para>
    /// </summary>
    public class WallCapSelectionTests
    {
        private const double VerticalAngle = 20 * (System.Math.PI / 180);
        private const double Overshoot = 0.05;
        private const double Distance = 1e-6;
        private const double RoofOvershoot = 0.5;

        // Wall centre (2,0), base z=0, top z=3, plane y=0 (normal ±Y => vertical).
        private const double WallTop = 3.0;
        private const double WallBase = 0.0;

        private static SnappedPanel Wall()
        {
            Face3D face = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, WallBase), new Point3D(4, 0, WallBase),
                new Point3D(4, 0, WallTop), new Point3D(0, 0, WallTop));
            return new SnappedPanel(0, face, 1.0, 0.3, Panel3DSnapSolver.DEFAULT_MaxExtension);
        }

        private static SnappedPanel FlatCap(double zc, double x0, double x1, double y0, double y1)
        {
            Face3D face = TestGeometry.CreatePlanarFace(
                new Point3D(x0, y0, zc), new Point3D(x1, y0, zc),
                new Point3D(x1, y1, zc), new Point3D(x0, y1, zc));
            return new SnappedPanel(1, face, 1.0, 0.3, Panel3DSnapSolver.DEFAULT_MaxExtension);
        }

        // A flat cap at elevation zc spanning [x0,x1]x[y0,y1] with a rectangular internal opening
        // [hx0,hx1]x[hy0,hy1] — the atrium/stairwell hole that makes Case A hole-aware. The real cap
        // FACE covers the annulus around the opening but NOT the opening itself.
        private static SnappedPanel HoledFlatCap(double zc, double x0, double x1, double y0, double y1,
            double hx0, double hx1, double hy0, double hy1)
        {
            Face3D face = TestGeometry.CreatePlanarFaceWithOpening(
                new[] { new Point3D(x0, y0, zc), new Point3D(x1, y0, zc), new Point3D(x1, y1, zc), new Point3D(x0, y1, zc) },
                new[] { new Point3D(hx0, hy0, zc), new Point3D(hx1, hy0, zc), new Point3D(hx1, hy1, zc), new Point3D(hx0, hy1, zc) });
            return new SnappedPanel(1, face, 1.0, 0.3, Panel3DSnapSolver.DEFAULT_MaxExtension);
        }

        // A long vertical wall, plan centre (3.9,0): base seg (1.8,0)-(6,0), plane y=0 (normal ±Y), z∈[0,3].
        // Paired with SlopedGrazeRoof so the wall centre falls 0.1 m OUTSIDE the roof's near edge (and its
        // AABB) while the wall bbox still overlaps the roof bbox — the graze the old AABB-containment free pass
        // does not mask, so the old 15°-flatness band genuinely applies.
        private static SnappedPanel WallLong()
        {
            Face3D face = TestGeometry.CreatePlanarFace(
                new Point3D(1.8, 0, WallBase), new Point3D(6, 0, WallBase),
                new Point3D(6, 0, WallTop), new Point3D(1.8, 0, WallTop));
            return new SnappedPanel(0, face, 1.0, 0.3, Panel3DSnapSolver.DEFAULT_MaxExtension);
        }

        /// <summary>A rectangular roof x∈[4,8], y∈[-1,1] pitched about the Y axis by <paramref name="pitchDeg"/>,
        /// highest along its near edge (x=4, z=<paramref name="nearEdgeZ"/>) and falling toward x=8. The long
        /// wall's centre (3.9,0) sits 0.1 m outside the near edge, so the wall bbox overlaps the roof bbox but
        /// the wall centre is outside the roof's AABB — a graze whose vertical gap (~1.4 m) is independent of
        /// pitch, isolating the pitch handling.</summary>
        private static SnappedPanel SlopedGrazeRoof(double pitchDeg, double nearEdgeZ)
        {
            double drop = 4 * System.Math.Tan(pitchDeg * System.Math.PI / 180);
            Face3D face = TestGeometry.CreatePlanarFace(
                new Point3D(4, -1, nearEdgeZ), new Point3D(8, -1, nearEdgeZ - drop),
                new Point3D(8, 1, nearEdgeZ - drop), new Point3D(4, 1, nearEdgeZ));
            return new SnappedPanel(1, face, 1.0, 0.3, Panel3DSnapSolver.DEFAULT_MaxExtension);
        }

        private static double MaxZ(SnappedPanel p) => p.GetBoundingBox().Max.Z;
        private static double MinZ(SnappedPanel p) => p.GetBoundingBox().Min.Z;

        private static void RunExtend(List<SnappedPanel> panels) =>
            Panel3DSnapSolver.Extend(panels, VerticalAngle, Overshoot, Distance, RoofOvershoot, includeRoofs: true);

        // 1. Projected sample inside the actual cap face (Case A): extends even beyond the continuation band.
        [Fact]
        public void Extend_SampleInsideCapFace_ExtendsAtAnyGap()
        {
            SnappedPanel wall = Wall();
            // Flat cap 3 m above the wall top (gap 3.0 > band 2.0) that fully covers the wall centre.
            SnappedPanel cap = FlatCap(6.0, -1, 5, -1, 1);
            RunExtend(new List<SnappedPanel> { wall, cap });

            Assert.True(MaxZ(wall) > 5.9, $"Case A must extend the wall to the covering cap; MaxZ={MaxZ(wall)}");
        }

        // 2. Cap AABB contains the sample but the real (L-shaped) face does NOT, at a large gap: rejected.
        //    FAILS at 20a7665 (AABB containment trusts it at any distance and the wall extends to z=6).
        [Fact]
        public void Extend_AabbContainsButRealFaceDoesNot_Rejected()
        {
            // Wall centre at the L's re-entrant notch: base seg (3,2.5)-(3,3.5), plane x=3 (normal ±X).
            Face3D wallFace = TestGeometry.CreatePlanarFace(
                new Point3D(3, 2.5, 0), new Point3D(3, 3.5, 0),
                new Point3D(3, 3.5, 3), new Point3D(3, 2.5, 3));
            SnappedPanel wall = new SnappedPanel(0, wallFace, 1.0, 0.3, Panel3DSnapSolver.DEFAULT_MaxExtension);

            // L-shaped cap at z=6: covers [0,4]x[0,2] and [0,2]x[0,4]; the [2,4]x[2,4] quadrant (holding the
            // wall centre (3,3)) is the notch. AABB = [0,4]x[0,4] DOES contain (3,3); the real face does not.
            Face3D lCap = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 6), new Point3D(4, 0, 6), new Point3D(4, 2, 6),
                new Point3D(2, 2, 6), new Point3D(2, 4, 6), new Point3D(0, 4, 6));
            SnappedPanel cap = new SnappedPanel(1, lCap, 1.0, 0.3, Panel3DSnapSolver.DEFAULT_MaxExtension);

            RunExtend(new List<SnappedPanel> { wall, cap });

            Assert.True(MaxZ(wall) < 3.0 + 0.5,
                $"A cap the wall centre only falls inside by AABB (not by the real face) at a 3 m gap must be rejected; MaxZ={MaxZ(wall)}");
        }

        // 3. Legitimate neighbouring tile within the continuation band (Case B graze just below 2.0 m): extends.
        //    FAILS at 20a7665 (the old flat band was 0.5 m, so a 1.9 m graze was wrongly rejected).
        [Fact]
        public void Extend_GrazeWithinContinuationBand_Extends()
        {
            SnappedPanel wall = Wall();
            // Flat tile beside the wall: x∈[2.1,5] leaves the wall centre (2,0) 0.1 m outside in x (graze).
            // Gap 1.9 m (cap z=4.9, wall top 3.0) < band 2.0.
            SnappedPanel cap = FlatCap(4.9, 2.1, 5, -1, 1);
            RunExtend(new List<SnappedPanel> { wall, cap });

            Assert.True(MaxZ(wall) > 4.8, $"A graze within the continuation band must extend; MaxZ={MaxZ(wall)}");
        }

        // 4. Identical tile just BEYOND the continuation band (graze at 2.1 m > 2.0): rejected.
        [Fact]
        public void Extend_GrazeBeyondContinuationBand_Rejected()
        {
            SnappedPanel wall = Wall();
            SnappedPanel cap = FlatCap(5.1, 2.1, 5, -1, 1); // gap 2.1 m > band 2.0
            RunExtend(new List<SnappedPanel> { wall, cap });

            Assert.True(MaxZ(wall) < 3.5, $"A graze beyond the continuation band must be rejected; MaxZ={MaxZ(wall)}");
        }

        // 5. Shallow roof BELOW the old 15° line (12°), grazed at a 1.0 m gap: extends.
        //    FAILS at 20a7665 (a sub-15° roof read as "flat" and its 1.0 m graze exceeded the 0.5 m band).
        [Fact]
        public void Extend_ShallowRoofBelowOldThreshold_Extends()
        {
            SnappedPanel wall = WallLong();
            // 12° roof grazed 0.1 m off its near edge (z=4.4), gap ~1.4 m over the wall top (< band 2.0).
            SnappedPanel cap = SlopedGrazeRoof(12, 4.4);
            RunExtend(new List<SnappedPanel> { wall, cap });

            Assert.True(MaxZ(wall) > 3.6, $"A shallow-roof graze within the band must extend; MaxZ={MaxZ(wall)}");
        }

        // 6. Shallow roof ABOVE the old 15° line (18°), same graze geometry and gap: extends too.
        //    Together with case 5 this shows the rule is continuous across the old 15° discontinuity.
        [Fact]
        public void Extend_ShallowRoofAboveOldThreshold_Extends()
        {
            SnappedPanel wall = WallLong();
            SnappedPanel cap = SlopedGrazeRoof(18, 4.4);
            RunExtend(new List<SnappedPanel> { wall, cap });

            Assert.True(MaxZ(wall) > 3.6, $"An 18° roof graze within the band must extend; MaxZ={MaxZ(wall)}");
        }

        // 7. Steep pitched roof (45°), grazed within the band: extends.
        [Fact]
        public void Extend_SteepRoofGrazeWithinBand_Extends()
        {
            SnappedPanel wall = WallLong();
            SnappedPanel cap = SlopedGrazeRoof(45, 4.4);
            RunExtend(new List<SnappedPanel> { wall, cap });

            Assert.True(MaxZ(wall) > 3.6, $"A steep-roof graze within the band must extend; MaxZ={MaxZ(wall)}");
        }

        // 8. Rigidly rotated (30° yaw about Z) copy of the Case A scene gives the equivalent result.
        [Fact]
        public void Extend_RigidlyRotatedGeometry_EquivalentResult()
        {
            const double a = 30 * System.Math.PI / 180;
            Point3D Rot(double x, double y, double z) =>
                new Point3D(x * System.Math.Cos(a) - y * System.Math.Sin(a), x * System.Math.Sin(a) + y * System.Math.Cos(a), z);

            Face3D wallFace = TestGeometry.CreatePlanarFace(
                Rot(0, 0, WallBase), Rot(4, 0, WallBase), Rot(4, 0, WallTop), Rot(0, 0, WallTop));
            SnappedPanel wall = new SnappedPanel(0, wallFace, 1.0, 0.3, Panel3DSnapSolver.DEFAULT_MaxExtension);
            Face3D capFace = TestGeometry.CreatePlanarFace(
                Rot(-1, -1, 6), Rot(5, -1, 6), Rot(5, 1, 6), Rot(-1, 1, 6));
            SnappedPanel cap = new SnappedPanel(1, capFace, 1.0, 0.3, Panel3DSnapSolver.DEFAULT_MaxExtension);

            RunExtend(new List<SnappedPanel> { wall, cap });

            // Z is preserved by a yaw rotation, so the contained cap at z=6 is reached exactly as in case 1.
            Assert.True(MaxZ(wall) > 5.9, $"Rotation about Z must not change the selection; MaxZ={MaxZ(wall)}");
        }

        // 9. Unrelated cap a whole storey up (flat graze at 3.0 m > band): rejected.
        [Fact]
        public void Extend_CapFromAnotherStorey_Rejected()
        {
            SnappedPanel wall = Wall();
            SnappedPanel cap = FlatCap(6.0, 2.1, 5, -1, 1); // graze (x starts 0.1 past the wall centre), gap 3.0 m
            RunExtend(new List<SnappedPanel> { wall, cap });

            Assert.True(MaxZ(wall) < 3.5, $"A grazing cap a storey up must be rejected; MaxZ={MaxZ(wall)}");
        }

        // 10. A cap with a null face / null plane normal is skipped without throwing; a valid cap still wins.
        //     Tests the selection guard directly (NearestCoveringCap is internal) because SnappedPanel rejects
        //     a null face, so a malformed cap cannot be injected through the Extend collection path.
        [Fact]
        public void NearestCoveringCap_NullFaceOrNormal_SkippedValidCapWins()
        {
            // Wall centre (2,0), top 3, growing up.
            BoundingBox3D wallBox = new BoundingBox3D(new Point3D(0, 0, 0), new Point3D(4, 0, 3));

            // A valid flat cap at z=5 covering the wall centre (Case A).
            Face3D validFace = TestGeometry.CreatePlanarFace(
                new Point3D(-1, -1, 5), new Point3D(5, -1, 5), new Point3D(5, 1, 5), new Point3D(-1, 1, 5));

            // Index 0: a malformed cap — box + null plane + null face (a null/degenerate plane normal). Its box
            // overlaps the wall and its (fallback) elevation z=4 is nearer than the valid cap, so a rule that
            // failed to skip it would select it.
            var capBoxes = new List<BoundingBox3D>
            {
                new BoundingBox3D(new Point3D(0, -1, 4), new Point3D(4, 1, 4)),
                validFace.GetBoundingBox(),
            };
            var capPlanes = new List<Plane> { null, validFace.GetPlane() };
            var capFace3Ds = new List<Face3D> { null, validFace };

            int index = 0;
            var ex = Record.Exception(() => index = Panel3DSnapSolver.NearestCoveringCap(
                wallBox, 2, 0, capBoxes, capPlanes, capFace3Ds, 1e-6, up: true, wallExtreme: 3));

            Assert.Null(ex); // no throw on the null face / null normal
            Assert.Equal(1, index); // the malformed cap (index 0) is skipped; the valid covering cap wins
        }

        // 11. Upward extension to a covering cap above (Case A up path).
        [Fact]
        public void Extend_Upward_ExtendsTopToCap()
        {
            SnappedPanel wall = Wall();
            SnappedPanel cap = FlatCap(4.5, -1, 5, -1, 1); // gap 1.5 m, covers centre
            RunExtend(new List<SnappedPanel> { wall, cap });

            Assert.True(MaxZ(wall) > 4.4, $"Upward extension must reach the ceiling; MaxZ={MaxZ(wall)}");
            Assert.True(MinZ(wall) <= WallBase + Distance, $"Base must be unchanged; MinZ={MinZ(wall)}");
        }

        // 12. Downward extension to a covering floor below (Case A down path).
        [Fact]
        public void Extend_Downward_ExtendsBaseToFloor()
        {
            SnappedPanel wall = Wall();
            SnappedPanel floor = FlatCap(-1.5, -1, 5, -1, 1); // 1.5 m below the base, covers centre
            RunExtend(new List<SnappedPanel> { wall, floor });

            Assert.True(MinZ(wall) < -1.4, $"Downward extension must reach the floor; MinZ={MinZ(wall)}");
            Assert.True(MaxZ(wall) >= WallTop - Distance, $"Top must be unchanged; MaxZ={MaxZ(wall)}");
        }

        // ── Hole-aware Case A (Root Cause B2, Sol Phase-1): a cap's real face is the annulus around an
        //    internal opening, not the opening itself. A wall sample under the opening is NOT physically
        //    covered — it is only ever eligible through the bounded Case B continuation, exactly like a
        //    sample outside the cap footprint. Case A now uses Face3D.On (hole-aware) instead of
        //    Face3D.InRange (outer-loop only). ──

        // 13. Sample in solid cap material with the opening off to the side: Case A still extends at any
        //     gap, through a holed cap. (Guards that the hole-aware swap did not break ordinary coverage.)
        [Fact]
        public void Extend_HoledCapSampleInSolidMaterial_ExtendsAtAnyGap()
        {
            SnappedPanel wall = Wall(); // plan centre (2,0)
            // Cap 3 m above the wall top (gap 3.0 > band 2.0); opening [3,4]x[-0.5,0.5] does NOT hold (2,0).
            SnappedPanel cap = HoledFlatCap(6.0, -1, 5, -1, 1, 3, 4, -0.5, 0.5);
            RunExtend(new List<SnappedPanel> { wall, cap });

            Assert.True(MaxZ(wall) > 5.9,
                $"Case A through solid cap material must extend at any gap; MaxZ={MaxZ(wall)}");
        }

        // 14. Sample inside the opening, cap within the continuation band (gap 1.9 < 2.0): NOT Case A, but
        //     admitted via Case B — an in-opening sample behaves exactly like an outside-footprint graze.
        [Fact]
        public void Extend_SampleInsideOpeningNearCap_ExtendsViaCaseB()
        {
            SnappedPanel wall = Wall();
            // Opening [1.5,2.5]x[-0.5,0.5] holds the wall centre (2,0); cap z=4.9, gap 1.9 < band 2.0.
            SnappedPanel cap = HoledFlatCap(4.9, -1, 5, -1, 1, 1.5, 2.5, -0.5, 0.5);
            RunExtend(new List<SnappedPanel> { wall, cap });

            Assert.True(MaxZ(wall) > 4.8,
                $"A sample inside the opening within the Case B band must extend; MaxZ={MaxZ(wall)}");
        }

        // 15. Sample inside the opening, cap a storey up (gap 3.0 > 2.0): rejected. THE Sol-defect regression
        //     test — at HEAD Face3D.InRange ignores the hole, treats the sample as covered (Case A) and
        //     extends the wall to z=6 at unlimited gap. Passes only after the Face3D.On swap.
        [Fact]
        public void Extend_SampleInsideOpeningFarCap_Rejected()
        {
            SnappedPanel wall = Wall();
            SnappedPanel cap = HoledFlatCap(6.0, -1, 5, -1, 1, 1.5, 2.5, -0.5, 0.5); // gap 3.0 > band 2.0
            RunExtend(new List<SnappedPanel> { wall, cap });

            Assert.True(MaxZ(wall) < 3.5,
                $"A cap whose opening sits over the wall, a storey up, must be rejected; MaxZ={MaxZ(wall)}");
        }

        // 16. Opening-boundary determinism. A sample exactly ON the opening edge counts as cap material
        //     (hole containment is edge-exclusive) → Case A → extends far. The same sample 1 mm inside the
        //     opening is NOT material → Case B → rejected at a 3 m gap. Deterministic across the tolerance.
        [Fact]
        public void Extend_SampleOnOpeningBoundary_DeterministicToleranceBehaviour()
        {
            // D1: opening left edge x=2 passes exactly through the wall centre (2,0) → on-edge = material.
            SnappedPanel wallOn = Wall();
            SnappedPanel capOn = HoledFlatCap(6.0, -1, 5, -1, 1, 2, 3, -0.5, 0.5); // gap 3.0
            RunExtend(new List<SnappedPanel> { wallOn, capOn });
            Assert.True(MaxZ(wallOn) > 5.9,
                $"A sample on the opening boundary is cap material (Case A) and must extend; MaxZ={MaxZ(wallOn)}");

            // D2: opening now starts at x=1.999, so the same (2,0) is 1 mm INSIDE the opening → not material.
            SnappedPanel wallIn = Wall();
            SnappedPanel capIn = HoledFlatCap(6.0, -1, 5, -1, 1, 1.999, 3, -0.5, 0.5); // gap 3.0
            RunExtend(new List<SnappedPanel> { wallIn, capIn });
            Assert.True(MaxZ(wallIn) < 3.5,
                $"A sample 1 mm inside the opening is not covered and must be rejected at a 3 m gap; MaxZ={MaxZ(wallIn)}");
        }

        // 17. Downward variant: a floor whose opening sits under the wall a storey below (gap 3.0) is
        //     rejected; the same holed floor within the band (gap 1.5) extends via Case B.
        [Fact]
        public void Extend_SampleInsideOpeningDownward_FarFloorRejectedNearFloorExtends()
        {
            // Far: floor z=-3, opening [1.5,2.5]x[-0.5,0.5] holds the centre; gap 3.0 > band 2.0 → rejected.
            SnappedPanel wallFar = Wall();
            SnappedPanel floorFar = HoledFlatCap(-3.0, -1, 5, -1, 1, 1.5, 2.5, -0.5, 0.5);
            RunExtend(new List<SnappedPanel> { wallFar, floorFar });
            Assert.True(MinZ(wallFar) > -0.1,
                $"A floor whose opening sits under the wall a storey below must be rejected; MinZ={MinZ(wallFar)}");

            // Near: identical holed floor at z=-1.5, gap 1.5 < band 2.0 → Case B extends downward.
            SnappedPanel wallNear = Wall();
            SnappedPanel floorNear = HoledFlatCap(-1.5, -1, 5, -1, 1, 1.5, 2.5, -0.5, 0.5);
            RunExtend(new List<SnappedPanel> { wallNear, floorNear });
            Assert.True(MinZ(wallNear) < -1.4,
                $"A holed floor within the continuation band must extend downward via Case B; MinZ={MinZ(wallNear)}");
        }
    }
}
