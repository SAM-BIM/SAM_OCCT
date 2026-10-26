// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Vertical extend (<see cref="Panel3DSnapSolver.Extend"/>) of walls in a two-storey stack: a wall extends to the cap
    /// it rests on and never through another storey's floor to a cap a storey away.
    /// <list type="bullet">
    /// <item>A wall whose base already passes its floor by no more than the overshoot (the state Extend leaves a wall
    /// in) rests on that floor; before the fix it was extended to the ground floor.</item>
    /// <item>A wall merged across storeys whose ground-floor part is narrower keeps its notch below the first floor;
    /// before the fix the notch was filled down to the ground floor.</item>
    /// </list>
    /// Pure managed: no native OCCT required.
    /// </summary>
    [Collection(ExtendCensusCollection.Name)]
    public class ExtendIntermediateCapTests
    {
        private const double Overshoot = 0.05;
        private const double Tolerance = 1e-6;
        private static readonly double VerticalAngle = 20 * (System.Math.PI / 180);

        // Two storeys of 3 m on a 10 x 6 m plan: floors at z = 0 and z = 3, roof at z = 6.
        private static List<SnappedPanel> Caps()
        {
            return new List<SnappedPanel>
            {
                new SnappedPanel(100, Floor(0), 1, 0.3, 0.5),
                new SnappedPanel(101, Floor(3), 1, 0.3, 0.5),
                new SnappedPanel(102, Floor(6), 1, 0.3, 0.5),
            };
        }

        private static Face3D Floor(double z)
        {
            return TestGeometry.CreatePlanarFace(new Point3D(0, 0, z), new Point3D(10, 0, z), new Point3D(10, 6, z), new Point3D(0, 6, z));
        }

        /// <summary>An upper-storey wall set back to y = 1, x 0..10, from <paramref name="baseZ"/> to the roof.</summary>
        private static SnappedPanel SetBackWall(double baseZ)
        {
            return new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 1, baseZ), new Point3D(10, 1, baseZ), new Point3D(10, 1, 6), new Point3D(0, 1, 6)), 1, 0.3, 0.5);
        }

        /// <summary>The west wall (x = 0) merged across both storeys: y 0..6 on the upper storey, y 0..2 on the ground floor.</summary>
        private static SnappedPanel MergedLWall()
        {
            return new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(0, 2, 0), new Point3D(0, 2, 3), new Point3D(0, 6, 3), new Point3D(0, 6, 6), new Point3D(0, 0, 6)), 1, 0.3, 0.5);
        }

        private static void Extend(params SnappedPanel[] walls)
        {
            List<SnappedPanel> panels = Caps();
            panels.AddRange(walls);
            Panel3DSnapSolver.Extend(panels, VerticalAngle, Overshoot, Tolerance);
        }

        [Theory]
        [InlineData(2.975, 2.975)] // 25 mm below its floor (Softhouse: a 1F wall snapped to 3.45 under a slab normalised to 3.475): already meets it
        [InlineData(2.95, 2.95)]   // exactly Extend's own output (floor - overshoot): already meets it
        [InlineData(3.0, 2.95)]    // on its floor: overshoots it as before
        [InlineData(3.025, 2.95)]  // 25 mm above its floor: extended to it as before
        public void Extend_UpperWallBaseNearItsFloor_RestsOnThatFloor(double baseZ, double expectedBase)
        {
            // Arrange
            SnappedPanel wall = SetBackWall(baseZ);

            // Act
            Extend(wall);

            // Assert: the base never reaches the ground floor (z = -0.05).
            Assert.Equal(expectedBase, wall.GetBoundingBox().Min.Z, 4);
            Assert.Equal(6.0 + Overshoot, wall.GetBoundingBox().Max.Z, 4);
        }

        [Fact]
        public void Extend_UpperWallBaseFarBelowItsFloor_StillExtendsToTheFloorBelow()
        {
            // Arrange: a base 0.2 m below the first floor is not resting on it (beyond the overshoot); the existing
            // nearest-cap rule applies unchanged.
            SnappedPanel wall = SetBackWall(2.8);

            // Act
            Extend(wall);

            // Assert
            Assert.Equal(0.0 - Overshoot, wall.GetBoundingBox().Min.Z, 4);
        }

        [Fact]
        public void Extend_TopJustAboveItsCeiling_RestsOnThatCeiling()
        {
            // Arrange: a ground-floor wall (y = 1, z 0..3.025) whose top passes the first floor by 25 mm.
            SnappedPanel wall = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 1, 0), new Point3D(10, 1, 0), new Point3D(10, 1, 3.025), new Point3D(0, 1, 3.025)), 1, 0.3, 0.5);

            // Act
            Extend(wall);

            // Assert: the top already meets the first floor; it is not grown to the roof (z = 6.05).
            Assert.Equal(3.025, wall.GetBoundingBox().Max.Z, 4);
        }

        [Theory]
        [InlineData(0.05)]  // a threshold stub as tall as the overshoot (Revit-home #21)
        [InlineData(0.095)]
        public void Extend_ShortWallStandingOnFloor_StillExtendsToTheCeiling(double height)
        {
            // Arrange: a ground-floor wall stub z 0..height standing on the ground floor. Its top is within the overshoot
            // of the floor it stands on, which must not be taken for the cap it rests on above.
            SnappedPanel wall = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 1, 0), new Point3D(10, 1, 0), new Point3D(10, 1, height), new Point3D(0, 1, height)), 1, 0.3, 0.5);

            // Act
            Extend(wall);

            // Assert
            Assert.Equal(3.0 + Overshoot, wall.GetBoundingBox().Max.Z, 4);
            Assert.Equal(0.0 - Overshoot, wall.GetBoundingBox().Min.Z, 4);
        }

        [Fact]
        public void Extend_RunTwice_SecondRunChangesNothing()
        {
            // Arrange
            SnappedPanel setBack = SetBackWall(2.975);
            SnappedPanel merged = MergedLWall();
            Extend(setBack, merged);
            double areaSetBack = setBack.GetArea(), areaMerged = merged.GetArea();
            BoundingBox3D boxSetBack = setBack.GetBoundingBox();

            // Act
            Extend(setBack, merged);

            // Assert
            Assert.Equal(areaSetBack, setBack.GetArea(), 4);
            Assert.Equal(areaMerged, merged.GetArea(), 4);
            Assert.Equal(boxSetBack.Min.Z, setBack.GetBoundingBox().Min.Z, 4);
        }

        [Fact]
        public void Extend_WallMergedAcrossStoreysWithNarrowerGroundFloor_KeepsNotchBelowFirstFloor()
        {
            // Arrange: 24 m2 (y 0..2 on the ground floor, y 0..6 above).
            SnappedPanel wall = MergedLWall();

            // Act
            Extend(wall);

            // Assert: no wall where the ground floor has none; the ground-floor part still reaches below its floor.
            Face3D face3D = wall.Face3D;
            Assert.False(face3D.Inside(new Point3D(0, 4, 1.5), Tolerance), "The ground-floor notch (y 2..6) was filled.");
            Assert.False(face3D.Inside(new Point3D(0, 4, 0.5), Tolerance), "The ground-floor notch (y 2..6) was filled.");
            Assert.True(face3D.Inside(new Point3D(0, 1, -0.025), Tolerance), "The ground-floor part must still reach below its floor.");
            Assert.True(face3D.Inside(new Point3D(0, 4, 6.025), Tolerance), "The top must still reach past the roof.");
            // Original 24 m2 + 2 x 0.05 under the ground-floor part + up to 4 x 0.05 under the notch + 6 x 0.05 at the top.
            Assert.InRange(wall.GetArea(), 24.0, 24.0 + 12 * Overshoot + 1e-4);
        }

        /// <summary>The west wall merged across both storeys with its ground-floor part stopping 0.15 m short of the end
        /// (y 0..5.85 below the first floor, y 0..6 above), in a stack whose caps extend past the wall (x -1..11,
        /// y -1..7).</summary>
        private static SnappedPanel ShortGroundFloorWall(double maxExtension)
        {
            return new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(0, 5.85, 0), new Point3D(0, 5.85, 3), new Point3D(0, 6, 3), new Point3D(0, 6, 6), new Point3D(0, 0, 6)), 1, 0.3, maxExtension);
        }

        private static Face3D WideCap(double z)
        {
            return TestGeometry.CreatePlanarFace(new Point3D(-1, -1, z), new Point3D(11, -1, z), new Point3D(11, 7, z), new Point3D(-1, 7, z));
        }

        [Theory]
        [InlineData(0.10)]
        [InlineData(0.50)]
        [InlineData(2.00)]
        public void Extend_NarrowSetBackWithoutCrossingWall_StaysOpenWhateverTheReach(double maxExtension)
        {
            // Arrange: an intentional 0.15 m lower-storey set-back with no wall at its end (independent review, finding 2).
            // The wall's lateral reach permits a move; it is not evidence that the lower storey continues.
            SnappedPanel wall = ShortGroundFloorWall(maxExtension);
            List<SnappedPanel> panels = new List<SnappedPanel> { new SnappedPanel(100, WideCap(0), 1, 0.3, 0.5), new SnappedPanel(101, WideCap(3), 1, 0.3, 0.5), new SnappedPanel(102, WideCap(6), 1, 0.3, 0.5), wall };

            // Act
            Panel3DSnapSolver.Extend(panels, VerticalAngle, Overshoot, Tolerance);

            // Assert
            Assert.False(wall.Face3D.Inside(new Point3D(0, 5.925, 1.5), Tolerance), "The set-back must not be filled through the first floor.");
            Assert.True(wall.Face3D.Inside(new Point3D(0, 3, -0.025), Tolerance));
        }

        /// <summary>The short ground-floor wall (lateral reach <paramref name="maxExtension"/>) with a crossing wall at
        /// y = 6 (x -1..1) from <paramref name="crossingBase"/> to <paramref name="crossingTop"/>, through the conditioning
        /// order: lateral extend, then vertical extend.</summary>
        private static SnappedPanel ExtendWithCrossingWall(double maxExtension, double crossingBase, double crossingTop)
        {
            SnappedPanel wall = ShortGroundFloorWall(maxExtension);
            SnappedPanel crossing = new SnappedPanel(1, TestGeometry.CreatePlanarFace(
                new Point3D(-1, 6, crossingBase), new Point3D(1, 6, crossingBase), new Point3D(1, 6, crossingTop), new Point3D(-1, 6, crossingTop)), 1, 0.3, 0.5);
            List<SnappedPanel> panels = new List<SnappedPanel> { new SnappedPanel(100, WideCap(0), 1, 0.3, 0.5), new SnappedPanel(101, WideCap(3), 1, 0.3, 0.5), new SnappedPanel(102, WideCap(6), 1, 0.3, 0.5), wall, crossing };

            Panel3DSnapSolver.ExtendWalls(panels, VerticalAngle, Overshoot, Tolerance);
            Panel3DSnapSolver.Extend(panels, VerticalAngle, Overshoot, Tolerance);
            return wall;
        }

        [Fact]
        public void ExtendWallsThenExtend_GroundFloorStoppingShortOfACrossingWall_ClosesTheJunction()
        {
            // Arrange / Act: the ground-floor part ends 0.15 m short of a crossing ground-floor wall (z 0..3), within the
            // wall's reach. The lateral extend reads the merged wall's whole plan extent (y 0..6) and leaves it; the
            // crossing wall in that storey is the evidence that the gap is a junction.
            SnappedPanel wall = ExtendWithCrossingWall(0.5, 0, 3);

            // Assert
            Assert.True(wall.Face3D.Inside(new Point3D(0, 5.925, 1.5), Tolerance), "The junction with the crossing wall must be closed.");
        }

        [Theory]
        [InlineData(0.5, 3.0, 6.0)]  // the crossing wall is upstairs only: no wall to meet below the first floor
        [InlineData(0.1, 0.0, 3.0)]  // the junction is 0.15 m away, beyond the wall's 0.1 m reach: no permission to move
        public void ExtendWallsThenExtend_NoCrossingWallInReachBelowTheLevel_SetBackStaysOpen(double maxExtension, double crossingBase, double crossingTop)
        {
            // Arrange / Act
            SnappedPanel wall = ExtendWithCrossingWall(maxExtension, crossingBase, crossingTop);

            // Assert
            Assert.False(wall.Face3D.Inside(new Point3D(0, 5.925, 1.5), Tolerance));
        }

        /// <summary>A ground-floor wall (x = 0) with a stepped base, z 0.15 over y 0..4 and z 0.1 over y 4..6, under a ceiling
        /// at z = 3, with a floor at z = 0 on its east side and a second floor 0.15 m higher (z 0.15, y 0..4) spanning
        /// x <paramref name="raisedFrom"/>..<paramref name="raisedTo"/>.</summary>
        private static SnappedPanel ExtendBesideRaisedFloor(double raisedFrom, double raisedTo, double maxExtension)
        {
            SnappedPanel wall = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0.15), new Point3D(0, 4, 0.15), new Point3D(0, 4, 0.1), new Point3D(0, 6, 0.1), new Point3D(0, 6, 3), new Point3D(0, 0, 3)), 1, 0.3, maxExtension);
            List<SnappedPanel> panels = new List<SnappedPanel>
            {
                new SnappedPanel(100, TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(5, 0, 0), new Point3D(5, 6, 0), new Point3D(0, 6, 0)), 1, 0.3, 0.5),
                new SnappedPanel(101, TestGeometry.CreatePlanarFace(new Point3D(raisedFrom, 0, 0.15), new Point3D(raisedTo, 0, 0.15), new Point3D(raisedTo, 4, 0.15), new Point3D(raisedFrom, 4, 0.15)), 1, 0.3, 0.5),
                new SnappedPanel(102, TestGeometry.CreatePlanarFace(new Point3D(-5, 0, 3), new Point3D(5, 0, 3), new Point3D(5, 6, 3), new Point3D(-5, 6, 3)), 1, 0.3, 0.5),
                wall,
            };

            Panel3DSnapSolver.Extend(panels, VerticalAngle, Overshoot, Tolerance);
            return wall;
        }

        [Theory]
        [InlineData(0.05)]
        [InlineData(0.5)]
        [InlineData(2.0)]
        public void Extend_SplitLevelFloorBesideTheWall_DoesNotStopTheExtension(double maxExtension)
        {
            // Arrange / Act: the raised floor lies west of the wall only, the lower floor east of it only (whole-level-towers:
            // floors at 12.24 and 12.384). The wall is the step between them: the east room reaches z = 0.
            SnappedPanel wall = ExtendBesideRaisedFloor(-5, 0, maxExtension);

            // Assert: the base reaches below the floor at z = 0 along the whole wall, whatever the wall's lateral reach.
            Assert.True(wall.Face3D.Inside(new Point3D(0, 2, 0.05), Tolerance));
            Assert.True(wall.Face3D.Inside(new Point3D(0, 5, 0.05), Tolerance));
        }

        [Theory]
        [InlineData(0.05)]
        [InlineData(0.5)]
        [InlineData(2.0)]
        public void Extend_DistinctLevelUnderBothSidesOfTheWall_IsKept(double maxExtension)
        {
            // Arrange / Act: control for the split level - the same floor 0.15 m higher, but under both sides of the wall
            // (x -5..5): it is the wall's own floor, a distinct level above the floor at z = 0, however close.
            SnappedPanel wall = ExtendBesideRaisedFloor(-5, 5, maxExtension);

            // Assert: the part standing on the raised floor (y 0..4) does not grow through it to z = 0.
            Assert.False(wall.Face3D.Inside(new Point3D(0, 2, 0.05), Tolerance), "The wall must not grow through its own floor.");
        }

        // ── Representation independence (independent review, finding 1) ────────────────────────────────
        // The merged-L wall with two floors of identical footprint at z = 3.00 and z = 3.04. The lower one in one piece,
        // split equally, split unequally, or listed twice must give the same wall; the two floors stay two levels.

        public static IEnumerable<object[]> LowerFloorRepresentations()
        {
            yield return new object[] { "one piece", new[] { new[] { (-1.0, -1.0), (11.0, -1.0), (11.0, 7.0), (-1.0, 7.0) } } };
            yield return new object[] { "two equal pieces", new[] { new[] { (-1.0, -1.0), (11.0, -1.0), (11.0, 3.0), (-1.0, 3.0) }, new[] { (-1.0, 3.0), (11.0, 3.0), (11.0, 7.0), (-1.0, 7.0) } } };
            yield return new object[] { "three unequal pieces", new[] { new[] { (-1.0, -1.0), (11.0, -1.0), (11.0, 0.5), (-1.0, 0.5) }, new[] { (-1.0, 0.5), (4.0, 0.5), (4.0, 7.0), (-1.0, 7.0) }, new[] { (4.0, 0.5), (11.0, 0.5), (11.0, 7.0), (4.0, 7.0) } } };
            yield return new object[] { "listed twice", new[] { new[] { (-1.0, -1.0), (11.0, -1.0), (11.0, 7.0), (-1.0, 7.0) }, new[] { (-1.0, -1.0), (11.0, -1.0), (11.0, 7.0), (-1.0, 7.0) } } };
        }

        private static SnappedPanel ExtendWithNearbyFloors((double X, double Y)[][] lowerPieces)
        {
            SnappedPanel wall = MergedLWall();
            List<SnappedPanel> panels = new List<SnappedPanel> { new SnappedPanel(100, WideCap(0), 1, 0.3, 0.5), new SnappedPanel(102, WideCap(6), 1, 0.3, 0.5) };
            for (int i = 0; i < lowerPieces.Length; i++)
            {
                panels.Add(new SnappedPanel(103 + i, TestGeometry.CreatePlanarFace(lowerPieces[i].Select(p => new Point3D(p.X, p.Y, 3)).ToArray()), 1, 0.3, 0.5));
            }

            panels.Add(new SnappedPanel(110, WideCap(3.04), 1, 0.3, 0.5));
            panels.Add(wall);
            Panel3DSnapSolver.Extend(panels, VerticalAngle, Overshoot, Tolerance);
            return wall;
        }

        [Theory]
        [MemberData(nameof(LowerFloorRepresentations))]
        public void Extend_NearbyFloorsInAnyRepresentation_SameWallBoundary(string representation, (double X, double Y)[][] lowerPieces)
        {
            // Arrange
            (double Area, string Inside) expected = Signature(ExtendWithNearbyFloors((((double X, double Y)[][])LowerFloorRepresentations().First()[1])), 0, 0, 0);

            // Act
            SnappedPanel wall = ExtendWithNearbyFloors(lowerPieces);

            // Assert: same boundary as the floor in one piece; the notch is clipped below the upper of the two floors,
            // which is a level of its own (the review's probe point 6.7 mm below it is outside the wall).
            (double Area, string Inside) actual = Signature(wall, 0, 0, 0);
            Assert.True(expected.Area == actual.Area, representation);
            Assert.True(expected.Inside == actual.Inside, representation);
            Assert.False(wall.Face3D.Inside(new Point3D(0, 4, 2.966), 1e-6), representation);
            Assert.False(wall.Face3D.Inside(new Point3D(0, 4, 1.5), 1e-6), representation);
        }

        [Fact]
        public void Extend_NonRectangularWallContinuousThroughFirstFloor_ExtendsWholeBase()
        {
            // Arrange: a two-storey wall (y = 0, x 0..10) with a stepped top (z 6 over x 0..5, z 5 over x 5..10) and its
            // base 0.1 m above the ground floor. It continues through the first floor in every column, so nothing is
            // clipped.
            SnappedPanel wall = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0.1), new Point3D(10, 0, 0.1), new Point3D(10, 0, 5), new Point3D(5, 0, 5), new Point3D(5, 0, 6), new Point3D(0, 0, 6)), 1, 0.3, 0.5);

            // Act
            Extend(wall);

            // Assert
            Assert.True(wall.Face3D.Inside(new Point3D(2, 0, 0), Tolerance));
            Assert.True(wall.Face3D.Inside(new Point3D(8, 0, 0), Tolerance));
            Assert.Equal(0.0 - Overshoot, wall.GetBoundingBox().Min.Z, 4);
        }

        [Fact]
        public void Extend_DoorNotchWithinOneStorey_IsStillClosedToTheFloor()
        {
            // Arrange: a ground-floor wall (y = 0, x 0..4, z 0.1..3) with a door notch x 1..2 up to z 2; no level lies
            // within the storey, so the existing notch fill is unchanged.
            SnappedPanel wall = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0.1), new Point3D(1, 0, 0.1), new Point3D(1, 0, 2), new Point3D(2, 0, 2), new Point3D(2, 0, 0.1),
                new Point3D(4, 0, 0.1), new Point3D(4, 0, 3), new Point3D(0, 0, 3)), 1, 0.3, 0.5);
            List<SnappedPanel> panels = new List<SnappedPanel> { new SnappedPanel(100, Floor(0), 1, 0.3, 0.5), new SnappedPanel(101, Floor(3), 1, 0.3, 0.5), wall };

            // Act
            Panel3DSnapSolver.Extend(panels, VerticalAngle, Overshoot, Tolerance);

            // Assert
            Assert.True(wall.Face3D.Inside(new Point3D(1.5, 0, 1), Tolerance));
            Assert.Equal(0.0 - Overshoot, wall.GetBoundingBox().Min.Z, 4);
        }

        [Fact]
        public void Extend_PlainRectangularStoreyWalls_Unchanged()
        {
            // Arrange: ground-floor and upper-storey walls exactly between their floors take the rectangular fast path.
            SnappedPanel ground = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(10, 0, 0), new Point3D(10, 0, 3), new Point3D(0, 0, 3)), 1, 0.3, 0.5);
            SnappedPanel upper = SetBackWall(3.0);

            // Act
            Extend(ground, upper);

            // Assert
            Assert.Equal(-Overshoot, ground.GetBoundingBox().Min.Z, 4);
            Assert.Equal(3.0 + Overshoot, ground.GetBoundingBox().Max.Z, 4);
            Assert.Equal(10 * (3.0 + 2 * Overshoot), ground.GetArea(), 4);
            Assert.Equal(10 * (3.0 + 2 * Overshoot), upper.GetArea(), 4);
        }

        // ── Equivalent floor geometry gives the same wall ───────────────────────────────────────────────
        // The first floor of the merged-L model as one outline or split into pieces. Where the floor covers the wall is
        // read from its actual geometry (concave corners and openings kept), so every split of the same floor must give
        // the same wall boundary. Plan points are given as (x, y) and moved by (dx, dy, degrees).

        private static readonly (double X, double Y)[] FullFloor = { (0, 0), (10, 0), (10, 6), (0, 6) };

        public static IEnumerable<object[]> EquivalentFloors()
        {
            // A: the floor covers the whole wall line.
            yield return new object[] { "A", new[] { FullFloor }, null };
            yield return new object[] { "A", new[] { new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 3.0), (0.0, 3.0) }, new[] { (0.0, 3.0), (10.0, 3.0), (10.0, 6.0), (0.0, 6.0) } }, null };
            yield return new object[] { "A", new[] { new[] { (0.0, 0.0), (5.0, 0.0), (5.0, 6.0), (0.0, 6.0) }, new[] { (5.0, 0.0), (10.0, 0.0), (10.0, 6.0), (5.0, 6.0) } }, null };
            yield return new object[] { "A", new[] { new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 4.0), (6.0, 4.0), (6.0, 6.0), (0.0, 6.0) } }, null };

            // B: a double-height corner at the wall (no floor over x 0..3, y 4..6): one concave outline or pieces.
            yield return new object[] { "B", new[] { new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 6.0), (3.0, 6.0), (3.0, 4.0), (0.0, 4.0) } }, null };
            yield return new object[] { "B", new[] { new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 4.0), (0.0, 4.0) }, new[] { (3.0, 4.0), (10.0, 4.0), (10.0, 6.0), (3.0, 6.0) } }, null };
            yield return new object[] { "B", new[] { new[] { (0.0, 0.0), (3.0, 0.0), (3.0, 4.0), (0.0, 4.0) }, new[] { (3.0, 0.0), (10.0, 0.0), (10.0, 6.0), (3.0, 6.0) } }, null };

            // C: a stair opening over the wall line (x -1..1, y 4..5.5) in a floor reaching x = -4: one floor with an
            // opening, or four pieces around it.
            yield return new object[] { "C", new[] { new[] { (-4.0, 0.0), (10.0, 0.0), (10.0, 6.0), (-4.0, 6.0) } }, new[] { (-1.0, 4.0), (1.0, 4.0), (1.0, 5.5), (-1.0, 5.5) } };
            yield return new object[] { "C", new[]
            {
                new[] { (-4.0, 0.0), (10.0, 0.0), (10.0, 4.0), (-4.0, 4.0) }, new[] { (-4.0, 5.5), (10.0, 5.5), (10.0, 6.0), (-4.0, 6.0) },
                new[] { (-4.0, 4.0), (-1.0, 4.0), (-1.0, 5.5), (-4.0, 5.5) }, new[] { (1.0, 4.0), (10.0, 4.0), (10.0, 5.5), (1.0, 5.5) },
            }, null };
        }

        /// <summary>The merged-L west wall extended in the two-storey model with the given first-floor pieces (and an
        /// optional opening in the first piece), moved in plan by (dx, dy, degrees).</summary>
        private static SnappedPanel ExtendWithFloor((double X, double Y)[][] pieces, (double X, double Y)[] opening, double dx, double dy, double degrees)
        {
            double angle = degrees * System.Math.PI / 180;
            Point3D T(double x, double y, double z) => new Point3D(dx + x * System.Math.Cos(angle) - y * System.Math.Sin(angle), dy + x * System.Math.Sin(angle) + y * System.Math.Cos(angle), z);
            Face3D Outline((double X, double Y)[] points, double z) => TestGeometry.CreatePlanarFace(points.Select(p => T(p.X, p.Y, z)).ToArray());

            List<SnappedPanel> panels = new List<SnappedPanel>
            {
                new SnappedPanel(100, Outline(new[] { (-5.0, -1.0), (11.0, -1.0), (11.0, 7.0), (-5.0, 7.0) }, 0), 1, 0.3, 0.5),
                new SnappedPanel(101, Outline(new[] { (-5.0, -1.0), (11.0, -1.0), (11.0, 7.0), (-5.0, 7.0) }, 6), 1, 0.3, 0.5),
            };

            for (int i = 0; i < pieces.Length; i++)
            {
                Face3D face3D = Outline(pieces[i], 3);
                if (i == 0 && opening != null)
                {
                    face3D = Face3D.Create(new List<IClosedPlanar3D> { new Polygon3D(pieces[i].Select(p => T(p.X, p.Y, 3)).ToList()), new Polygon3D(opening.Select(p => T(p.X, p.Y, 3)).ToList()) });
                }

                panels.Add(new SnappedPanel(102 + i, face3D, 1, 0.3, 0.5));
            }

            SnappedPanel wall = new SnappedPanel(0, TestGeometry.CreatePlanarFace(T(0, 0, 0), T(0, 2, 0), T(0, 2, 3), T(0, 6, 3), T(0, 6, 6), T(0, 0, 6)), 1, 0.3, 0.5);
            panels.Add(wall);
            Panel3DSnapSolver.Extend(panels, VerticalAngle, Overshoot, Tolerance);
            return wall;
        }

        /// <summary>Area and inside/outside over a grid of the wall's plane (y -1..7, z -1..7 in the unmoved frame).</summary>
        private static (double Area, string Inside) Signature(SnappedPanel wall, double dx, double dy, double degrees)
        {
            double angle = degrees * System.Math.PI / 180;
            Point3D T(double x, double y, double z) => new Point3D(dx + x * System.Math.Cos(angle) - y * System.Math.Sin(angle), dy + x * System.Math.Sin(angle) + y * System.Math.Cos(angle), z);
            System.Text.StringBuilder inside = new System.Text.StringBuilder();
            for (double y = -0.75; y < 7; y += 0.25)
            {
                for (double z = -0.75; z < 7; z += 0.25)
                {
                    inside.Append(wall.Face3D.Inside(T(0, y + 0.01, z + 0.01), 1e-4) ? '1' : '0');
                }
            }

            return (System.Math.Round(wall.GetArea(), 4), inside.ToString());
        }

        [Theory]
        [MemberData(nameof(EquivalentFloors))]
        public void Extend_EquivalentFloorPieces_SameWallBoundary(string group, (double X, double Y)[][] pieces, (double X, double Y)[] opening)
        {
            // Arrange: the reference is the first floor of the group in one piece.
            object[] reference = EquivalentFloors().First(x => (string)x[0] == group);

            foreach ((double dx, double dy, double degrees) in new[] { (0.0, 0.0, 0.0), (12.5, -7.25, 90.0), (-3.0, 4.0, 211.0) })
            {
                // Act
                (double Area, string Inside) expected = Signature(ExtendWithFloor(((double X, double Y)[][])reference[1], ((double X, double Y)[])reference[2], dx, dy, degrees), dx, dy, degrees);
                (double Area, string Inside) actual = Signature(ExtendWithFloor(pieces, opening, dx, dy, degrees), dx, dy, degrees);

                // Assert
                Assert.Equal(expected.Area, actual.Area, 3);
                Assert.Equal(expected.Inside, actual.Inside);
            }
        }

        [Theory]
        [InlineData("A", 3.0, false)] // covered by the floor: no wall below it
        [InlineData("A", 5.0, false)]
        [InlineData("B", 3.0, false)]
        [InlineData("B", 5.0, true)]  // no floor over the double-height corner: the existing extend applies
        [InlineData("C", 3.0, false)]
        [InlineData("C", 4.75, true)] // under the stair opening: the existing extend applies
        public void Extend_FloorGeometryAtTheWall_DecidesWhereTheNotchStays(string group, double y, bool filled)
        {
            // Arrange
            object[] reference = EquivalentFloors().First(x => (string)x[0] == group);

            // Act
            SnappedPanel wall = ExtendWithFloor(((double X, double Y)[][])reference[1], ((double X, double Y)[])reference[2], 0, 0, 0);

            // Assert
            Assert.Equal(filled, wall.Face3D.Inside(new Point3D(0, y, 1.5), 1e-4));
            Assert.True(wall.Face3D.Inside(new Point3D(0, 1, -0.025), 1e-4));
        }

        [Theory]
        [InlineData(0.0, 0.0, 0.0)]
        [InlineData(0.0, 0.0, 37.0)]
        [InlineData(12.5, -7.25, 90.0)]
        [InlineData(-3.0, 4.0, 211.0)]
        public void Extend_MergedLWallTranslatedOrRotated_SameResult(double dx, double dy, double degrees)
        {
            // Arrange: the merged-L case moved and turned in plan (caps enlarged so they still cover the wall).
            double angle = degrees * System.Math.PI / 180;
            Point3D T(double x, double y, double z) => new Point3D(dx + x * System.Math.Cos(angle) - y * System.Math.Sin(angle), dy + x * System.Math.Sin(angle) + y * System.Math.Cos(angle), z);
            Face3D Cap(double z) => TestGeometry.CreatePlanarFace(T(-1, -1, z), T(11, -1, z), T(11, 7, z), T(-1, 7, z));
            SnappedPanel wall = new SnappedPanel(0, TestGeometry.CreatePlanarFace(T(0, 0, 0), T(0, 2, 0), T(0, 2, 3), T(0, 6, 3), T(0, 6, 6), T(0, 0, 6)), 1, 0.3, 0.5);
            List<SnappedPanel> panels = new List<SnappedPanel> { new SnappedPanel(100, Cap(0), 1, 0.3, 0.5), new SnappedPanel(101, Cap(3), 1, 0.3, 0.5), new SnappedPanel(102, Cap(6), 1, 0.3, 0.5), wall };

            // Act
            Panel3DSnapSolver.Extend(panels, VerticalAngle, Overshoot, Tolerance);

            // Assert
            Assert.False(wall.Face3D.Inside(T(0, 4, 1.5), 1e-4));
            Assert.True(wall.Face3D.Inside(T(0, 1, -0.025), 1e-4));
            Assert.InRange(wall.GetArea(), 24.0, 24.0 + 12 * Overshoot + 1e-4);
        }
    }
}
