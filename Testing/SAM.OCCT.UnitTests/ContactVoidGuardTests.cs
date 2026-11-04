// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Planar;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;
using static SAM.OCCT.UnitTests.ContactFixtures;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// The decision of <see cref="ContactVoidGuard"/> (design rev 5, section 3, conditions 1-5), tested on the predicate itself with
    /// anti-parallel wall pairs standing at the D203 offsets. Pure managed.
    /// </summary>
    public class ContactVoidGuardTests
    {
        private static bool Releases(SnappedPanel a, SnappedPanel b)
        {
            return ContactVoidGuard.Releases(a, b, out string _);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Releases_D203ContactingSlabs_True(bool thinFacesPlusY)
        {
            // Arrange: 0.324 m apart, certified slabs 0.1 and 0.548 m: they touch exactly (gap 0), in both normal orientations.
            (SnappedPanel thin, SnappedPanel thick) = D203Pair(thinFacesPlusY);

            // Act
            bool forward = ContactVoidGuard.Releases(thin, thick, out string reason);
            bool backward = Releases(thick, thin);

            // Assert
            Assert.True(forward, reason);
            Assert.True(backward);
        }

        [Fact]
        public void Releases_D203CoverageOf87Percent_True()
        {
            // Arrange: D203 shares 87 % of the larger footprint (the stepped wall's thick skin is 13 % longer than the thin one). The coverage floor is
            // the 0.5 co-parallel ratio, not the 0.97 back-to-back-partition ratio, which would refuse this real pair.
            Face3D thin = Box(YThin, false, 0.39, Run);
            Face3D thick = Box(YThick, true, 0, Run);
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick));

            // Act
            bool released = ContactVoidGuard.Releases(a, b, out string reason);

            // Assert
            Assert.True(released, reason);
            Assert.Equal(0.87, (Run - 0.39) / Run, 6);
        }

        [Fact]
        public void Releases_CoverageBelowHalf_False()
        {
            // Arrange: the skins share 45 % of the larger footprint.
            Face3D thin = Box(YThin, false, 1.65, Run);
            Face3D thick = Box(YThick, true, 0, Run);
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick));

            // Act
            bool released = ContactVoidGuard.Releases(a, b, out string reason);

            // Assert
            Assert.False(released);
            Assert.Contains("covers", reason);
        }

        [Fact]
        public void Releases_NoRecordOnEitherPanel_False()
        {
            // Arrange
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thick = Box(YThick, true, 0, Run);
            SnappedPanel withRecord = Skin(0, thin, Evidence(thin, GThin));
            SnappedPanel withoutRecord = Skin(1, thick, null);

            // Act & Assert
            Assert.False(Releases(withRecord, withoutRecord));
            Assert.False(Releases(withoutRecord, withRecord));
        }

        [Fact]
        public void Releases_RecordWithoutRegionIntersection_False()
        {
            // Arrange: a record that cannot say how to intersect regions is no evidence (never a release by default).
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thick = Box(YThick, true, 0, Run);
            ContactEvidence noOperation = new ContactEvidence(thin.GetPlane(), GThin, new[] { thin.GetPlane().Convert(thin) }, new[] { thin.GetPlane().Convert(thin) }, null);
            SnappedPanel a = Skin(0, thin, noOperation);
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick));

            // Act & Assert
            Assert.False(Releases(a, b));
        }

        [Fact]
        public void Releases_ExtensionOnlyOverlap_False()
        {
            // Arrange (control B): the originals ran x 0..1.0 and 1.2..2.2, so the source solids never overlapped in plan; the
            // junction snap stretched both to the same box. The current faces overlap fully, the originals not at all.
            Face3D thin = Box(YThin, false, 0, 2.2);
            Face3D thick = Box(YThick, true, 0, 2.2);
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin, origin: new[] { Box(YThin, false, 0, 1.0) }));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick, origin: new[] { Box(YThick, true, 1.2, 2.2) }));

            // Act
            bool released = ContactVoidGuard.Releases(a, b, out string reason);

            // Assert
            Assert.False(released);
            Assert.False(string.IsNullOrEmpty(reason));
        }

        [Fact]
        public void Releases_NoDefiniteIntersection_False()
        {
            // Arrange: the kernel cannot decide (null) - fail closed.
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thick = Box(YThick, true, 0, Run);
            System.Func<IEnumerable<Face2D>, IEnumerable<Face2D>, double, List<Face2D>> undecided = (x, y, t) => null;
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin, intersect: undecided));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick, intersect: undecided));

            // Act & Assert
            Assert.False(Releases(a, b));
        }

        [Fact]
        public void Releases_PlanesNotOpposed_False()
        {
            // Arrange: a wall and a wall at right angles are not a skin pair; the angle term must refuse before any division.
            Face3D wallInX = Box(YThin, false, 0, Run);
            Face3D wallInY = TestGeometry.CreatePlanarFace(new Point3D(1, 0, 0), new Point3D(1, 3, 0), new Point3D(1, 3, 3), new Point3D(1, 0, 3));
            SnappedPanel a = Skin(0, wallInX, Evidence(wallInX, GThin));
            SnappedPanel b = Skin(1, wallInY, Evidence(wallInY, GThick));

            // Act & Assert
            Assert.False(Releases(a, b));
        }

        [Theory]
        [InlineData(0.0, 1.2)]
        [InlineData(0.9, 2.1)]
        [InlineData(2.7, 3.0)]
        public void Releases_PartialContactVoid_False(double voidFrom, double voidTo)
        {
            // Arrange (control C): the thick skin's certified material misses a strip of the overlap - 40 % at the end (60/40), 40 % in the middle, 10 % at the end
            // - so the slabs do not touch there: a real void behind that part of the wall. Support is the wall minus the strip.
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thick = Box(YThick, true, 0, Run);
            List<Face3D> support = SupportWithout(YThick, true, voidFrom, voidTo);
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick, support: support));

            // Act & Assert
            Assert.False(Releases(a, b));
            Assert.False(Releases(b, a));
        }

        [Fact]
        public void Releases_UnsupportedSliverBelowToleranceTimesPerimeter_True()
        {
            // Arrange: a 1e-8 m wide strip (3e-8 m2 over the 3 m height) is far below Tolerance.Distance x perimeter of the region (about 1.2e-5 m2).
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thick = Box(YThick, true, 0, Run);
            List<Face3D> support = SupportWithout(YThick, true, 1.5, 1.5 + 1e-8);
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick, support: support));

            // Act & Assert
            Assert.True(Releases(a, b));
        }

        [Fact]
        public void Releases_AlignedOpeningsInsideTheContact_False()
        {
            // Arrange (control F): both source solids have a through opening at the same place; the support excludes it, the origin (openings
            // filled) does not. Conservative: the region is not proved, so the guard stays.
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thick = Box(YThick, true, 0, Run);
            Face3D thinSupport = BoxWithOpening(YThin, false, 0, Run, 1.0, 1.5, 0.5, 1.5);
            Face3D thickSupport = BoxWithOpening(YThick, true, 0, Run, 1.0, 1.5, 0.5, 1.5);
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin, support: new[] { thinSupport }));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick, support: new[] { thickSupport }));

            // Act & Assert
            Assert.False(Releases(a, b));
        }

        [Fact]
        public void Releases_ContactTrimmedAway_False()
        {
            // Arrange (control T1): the originals overlap fully (so rev 4 admitted), but the thin skin's current face is an L (a 1 m strip and a
            // 0.5 m column): the true region it still shares with the thick skin is 4 of 9 m2 of the larger bounding box (0.444 < 0.5), while
            // the bounding boxes overlap fully (so the existing eligibility still passes).
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thinL = Wall(YThin, false, (0, 0), (3, 0), (3, 1), (0.5, 1), (0.5, 3), (0, 3));
            Face3D thick = Box(YThick, true, 0, Run);
            SnappedPanel a = Skin(0, thinL, Evidence(thin, GThin));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick));

            // Act
            bool released = ContactVoidGuard.Releases(a, b, out string reason);

            // Assert
            Assert.False(released, reason);
        }

        [Fact]
        public void Releases_VoidTrimmedAwayFromBothFaces_True()
        {
            // Arrange (control T2): the thick skin's material is missing for x 2..3, but both current faces were trimmed to x 0..1.8 by junction
            // snapping, so the void is no longer part of either panel and cannot veto; the clipped region is fully supported.
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thick = Box(YThick, true, 0, Run);
            Face3D thinTrimmed = Box(YThin, false, 0, 1.8);
            Face3D thickTrimmed = Box(YThick, true, 0, 1.8);
            SnappedPanel a = Skin(0, thinTrimmed, Evidence(thin, GThin));
            SnappedPanel b = Skin(1, thickTrimmed, Evidence(thick, GThick, support: SupportWithout(YThick, true, 2.0, 3.0)));

            // Act
            bool released = ContactVoidGuard.Releases(a, b, out string reason);

            // Assert
            Assert.True(released, reason);
        }

        [Fact]
        public void Releases_SameVoidWithoutTrimming_False()
        {
            // Arrange: the T2 geometry with the faces untrimmed - the void is inside the panels again.
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thick = Box(YThick, true, 0, Run);
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick, support: SupportWithout(YThick, true, 2.0, 3.0)));

            // Act & Assert
            Assert.False(Releases(a, b));
        }

        [Theory]
        [InlineData(0.05, false)]
        [InlineData(5e-7, true)]
        [InlineData(0.0, true)]
        public void Releases_WedgeOpeningTowardOneEnd_ByTheWholeRegionBound(double tilt, bool expected)
        {
            // Arrange (control W): the thick skin is tilted so its ends sit +-tilt from the centroid offset. The centroid separation is exactly
            // the contact offset, so the scalar test passes; the slabs separate by `tilt` at the ends. 0.05 m rejects, 5e-7 m is within
            // Tolerance.Distance (tolerance edge).
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thick = TiltedBox(YThick, tilt, true, 0, Run);
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick));

            // Act & Assert
            Assert.Equal(expected, Releases(a, b));
            Assert.Equal(expected, Releases(b, a));
        }

        [Theory]
        [InlineData(0.05, false)]
        [InlineData(-0.05, false)]
        public void Releases_WedgeOpeningTowardTheOtherEnd_ByTheWholeRegionBound(double tilt, bool expected)
        {
            // Arrange: the same wedge with the open end on the other side (a negative tilt flips which end opens).
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thick = TiltedBox(YThick, tilt, true, 0, Run);
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick));

            // Act & Assert
            Assert.Equal(expected, Releases(a, b));
        }

        [Fact]
        public void Releases_PlanesCrossInsideTheContact_False()
        {
            // Arrange: the thick skin tilts +-0.4 m about an offset of 0.324 m, so the planes cross inside the region (slab overlap at one end,
            // a 0.4 m gap at the other). The gap is convex in the position, so the maximum at the far vertices decides.
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thick = TiltedBox(YThick, 0.4, true, 0, Run);
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick));

            // Act & Assert
            Assert.False(Releases(a, b));
            Assert.False(Releases(b, a));
        }

        [Theory]
        [InlineData(0.5e-6, true)]
        [InlineData(2e-6, false)]
        public void Releases_ThicknessBoundAtToleranceEdge(double shortfall, bool expected)
        {
            // Arrange: the thick slab certified `shortfall` thinner on each side than needed to touch (the slabs miss by `shortfall`... g/2 each).
            (SnappedPanel thin, SnappedPanel thick) = D203Pair(gThick: GThick - 2 * shortfall);

            // Act & Assert
            Assert.Equal(expected, Releases(thin, thick));
        }

        [Fact]
        public void Releases_SlabsOverlapping_True()
        {
            // Arrange: thicker certified slabs than the offset needs - the slabs interpenetrate, which is contact.
            (SnappedPanel thin, SnappedPanel thick) = D203Pair(gThick: GThick + 0.2);

            // Act & Assert
            Assert.True(Releases(thin, thick));
        }

        [Fact]
        public void Releases_Slabs1mmApart_False()
        {
            // Arrange: certified slabs a millimetre short of touching.
            (SnappedPanel thin, SnappedPanel thick) = D203Pair(gThick: GThick - 0.002);

            // Act & Assert
            Assert.False(Releases(thin, thick));
        }

        /// <summary>The wall's face minus a vertical strip x in [from, to] (the part of the support where the thickness is not certified).</summary>
        private static List<Face3D> SupportWithout(double y, bool normalPlusY, double from, double to)
        {
            List<Face3D> result = new List<Face3D>();
            if (from > 0)
            {
                result.Add(Box(y, normalPlusY, 0, from));
            }

            if (to < Run)
            {
                result.Add(Box(y, normalPlusY, to, Run));
            }

            return result;
        }
    }
}
