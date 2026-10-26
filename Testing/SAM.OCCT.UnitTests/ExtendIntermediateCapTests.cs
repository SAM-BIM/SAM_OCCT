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

        [Fact]
        public void ExtendWallsThenExtend_UpperStoreyContinuingPastTheCrossingWall_JunctionFillStopsAtItsLine()
        {
            // Arrange: the upper storey runs on to y = 6.8, past the crossing ground-floor wall at y = 6; the ground-floor
            // part stops at y = 5.85 (whole-level-towers #20: a fill overshooting the crossing wall left a fin and an
            // unclosed room).
            SnappedPanel wall = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(0, 5.85, 0), new Point3D(0, 5.85, 3), new Point3D(0, 6.8, 3), new Point3D(0, 6.8, 6), new Point3D(0, 0, 6)), 1, 0.3, 0.5);
            SnappedPanel crossing = new SnappedPanel(1, TestGeometry.CreatePlanarFace(
                new Point3D(-1, 6, 0), new Point3D(1, 6, 0), new Point3D(1, 6, 3), new Point3D(-1, 6, 3)), 1, 0.3, 0.5);
            List<SnappedPanel> panels = new List<SnappedPanel> { new SnappedPanel(100, WideCap(0), 1, 0.3, 0.5), new SnappedPanel(101, WideCap(3), 1, 0.3, 0.5), new SnappedPanel(102, WideCap(6), 1, 0.3, 0.5), wall, crossing };

            // Act
            Panel3DSnapSolver.ExtendWalls(panels, VerticalAngle, Overshoot, Tolerance);
            Panel3DSnapSolver.Extend(panels, VerticalAngle, Overshoot, Tolerance);

            // Assert: closed up to the crossing wall's line, nothing beyond it below the first floor.
            Assert.True(wall.Face3D.Inside(new Point3D(0, 5.95, 1.5), Tolerance), "The junction must be closed.");
            Assert.False(wall.Face3D.Inside(new Point3D(0, 6.03, 1.5), Tolerance), "No fin past the crossing wall.");
            Assert.False(wall.Face3D.Inside(new Point3D(0, 6.5, 1.5), Tolerance), "The upper storey's overhang must not be filled.");
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
        [InlineData(0.5)]
        [InlineData(2.0)]
        public void Extend_SplitLevelFloorBesideTheWall_DoesNotStopTheExtension(double maxExtension)
        {
            // Arrange / Act: the raised floor lies west of the wall only, the lower floor east of it only. The east room
            // continues past the raised floor's level to z = 0: the wall is the step between them.
            SnappedPanel wall = ExtendBesideRaisedFloor(-5, 0, maxExtension);

            // Assert: the base reaches below the floor at z = 0 along the whole wall.
            Assert.True(wall.Face3D.Inside(new Point3D(0, 2, 0.05), Tolerance));
            Assert.True(wall.Face3D.Inside(new Point3D(0, 5, 0.05), Tolerance));
        }

        [Fact]
        public void Extend_SplitLevelBeyondTheWallsReach_WallStaysOnTheRaisedFloor()
        {
            // Arrange / Act: the same split level, but the wall may move only 0.05 m: the 0.15 m growth past the raised
            // floor is justified by the geometry but not permitted.
            SnappedPanel wall = ExtendBesideRaisedFloor(-5, 0, 0.05);

            // Assert: no growth below the raised floor where it lies; the stretch beside no raised floor still extends.
            Assert.False(wall.Face3D.Inside(new Point3D(0, 2, 0.05), Tolerance));
            Assert.True(wall.Face3D.Inside(new Point3D(0, 5, 0.05), Tolerance));
        }

        [Theory]
        [InlineData(0.4, true)]
        [InlineData(0.1, false)]
        public void Extend_FloorStackedOverTheTargetOnOneSide_WallReachesTheTargetWithinItsReach(double maxExtension, bool reaches)
        {
            // Arrange: whole-level-towers - a floor at z = 0 under both sides of the wall, a second floor 0.144 m higher
            // stacked over it on the west side only, and the wall standing on the upper one (base 0.128). The east room's
            // floor is z = 0: its space continues past the upper floor's level to the target.
            SnappedPanel wall = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0.128), new Point3D(0, 6, 0.128), new Point3D(0, 6, 3), new Point3D(0, 0, 3)), 1, 0.3, maxExtension);
            List<SnappedPanel> panels = new List<SnappedPanel>
            {
                new SnappedPanel(100, TestGeometry.CreatePlanarFace(new Point3D(-5, 0, 0), new Point3D(5, 0, 0), new Point3D(5, 6, 0), new Point3D(-5, 6, 0)), 1, 0.3, 0.5),
                new SnappedPanel(101, TestGeometry.CreatePlanarFace(new Point3D(-5, 0, 0.144), new Point3D(0, 0, 0.144), new Point3D(0, 6, 0.144), new Point3D(-5, 6, 0.144)), 1, 0.3, 0.5),
                new SnappedPanel(102, TestGeometry.CreatePlanarFace(new Point3D(-5, 0, 3), new Point3D(5, 0, 3), new Point3D(5, 6, 3), new Point3D(-5, 6, 3)), 1, 0.3, 0.5),
                wall,
            };

            // Act
            Panel3DSnapSolver.Extend(panels, VerticalAngle, Overshoot, Tolerance);

            // Assert
            Assert.Equal(reaches, wall.Face3D.Inside(new Point3D(0, 3, 0.02), Tolerance));
            Assert.Equal(reaches ? -Overshoot : 0.128, wall.GetBoundingBox().Min.Z, 4);
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

        [Fact]
        public void ExtendWallsThenExtend_RaisedFloorEndingAtTheCrossingWallsFace_ClipStepsAtTheCrossingWallsLine()
        {
            // Arrange: whole-level-towers #142 - an exterior wall (y = 0, x 0..10, base 0.2) standing on a room floor at 0.144
            // that ends at the inner face of a crossing wall (x 0..5.9, the crossing wall's line at x = 6), and a floor at
            // z = 0 on the other side of that crossing wall (x 6..10) reaching under the wall. Read as modelled, the clip
            // would step the wall's base at x = 5.9, 0.1 m beside the junction, and leave an open sliver there.
            SnappedPanel wall = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0.2), new Point3D(10, 0, 0.2), new Point3D(10, 0, 3), new Point3D(0, 0, 3)), 1, 0.3, 0.4);
            SnappedPanel crossing = new SnappedPanel(1, TestGeometry.CreatePlanarFace(
                new Point3D(6, 0, 0.2), new Point3D(6, 5, 0.2), new Point3D(6, 5, 3), new Point3D(6, 0, 3)), 1, 0.3, 0.4);
            List<SnappedPanel> panels = new List<SnappedPanel>
            {
                new SnappedPanel(100, TestGeometry.CreatePlanarFace(new Point3D(0, 0.1, 0.144), new Point3D(5.9, 0.1, 0.144), new Point3D(5.9, 5, 0.144), new Point3D(0, 5, 0.144)), 1, 0.3, 0.5),
                new SnappedPanel(101, TestGeometry.CreatePlanarFace(new Point3D(6, -0.1, 0), new Point3D(10, -0.1, 0), new Point3D(10, 5, 0), new Point3D(6, 5, 0)), 1, 0.3, 0.5),
                new SnappedPanel(102, TestGeometry.CreatePlanarFace(new Point3D(0, -0.1, 3), new Point3D(10, -0.1, 3), new Point3D(10, 5, 3), new Point3D(0, 5, 3)), 1, 0.3, 0.5),
                wall,
                crossing,
            };

            // Act
            Panel3DSnapSolver.ExtendWalls(panels, VerticalAngle, Overshoot, Tolerance);
            Panel3DSnapSolver.Extend(panels, VerticalAngle, Overshoot, Tolerance);

            // Assert: beside the raised floor the base rests on it; beyond the crossing wall's line it reaches z = 0.
            Assert.False(wall.Face3D.Inside(new Point3D(3, 0, 0.05), Tolerance), "No growth through the room's own floor.");
            Assert.False(wall.Face3D.Inside(new Point3D(5.95, 0, 0.05), Tolerance), "The step must be at the crossing wall's line, not at the floor's edge.");
            Assert.True(wall.Face3D.Inside(new Point3D(6.05, 0, 0.05), Tolerance), "Beyond the crossing wall the room floor is z = 0.");
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

        [Fact]
        public void Extend_FloorWithSeveralOpeningsAtTheWall_EachOpeningKeepsTheExtension()
        {
            // Arrange: the merged-L wall under a first floor (x -4..10) with two stair openings over the wall line (y 2.5..3.2
            // and y 4..5.5). A plan face built with several openings at once unions them, which can leave null edges
            // (Softhouse); each opening must still count.
            Face3D floor = Face3D.Create(new List<IClosedPlanar3D>
            {
                new Polygon3D(new List<Point3D> { new Point3D(-4, 0, 3), new Point3D(10, 0, 3), new Point3D(10, 6, 3), new Point3D(-4, 6, 3) }),
                new Polygon3D(new List<Point3D> { new Point3D(-1, 2.5, 3), new Point3D(1, 2.5, 3), new Point3D(1, 3.2, 3), new Point3D(-1, 3.2, 3) }),
                new Polygon3D(new List<Point3D> { new Point3D(-1, 4, 3), new Point3D(1, 4, 3), new Point3D(1, 5.5, 3), new Point3D(-1, 5.5, 3) }),
            });
            SnappedPanel wall = MergedLWall();
            List<SnappedPanel> panels = new List<SnappedPanel> { new SnappedPanel(100, WideCap(0), 1, 0.3, 0.5), new SnappedPanel(101, floor, 1, 0.3, 0.5), new SnappedPanel(102, WideCap(6), 1, 0.3, 0.5), wall };

            // Act
            Panel3DSnapSolver.Extend(panels, VerticalAngle, Overshoot, Tolerance);

            // Assert: under each opening the existing extend applies; elsewhere the floor stops the wall.
            Assert.True(wall.Face3D.Inside(new Point3D(0, 2.85, 1.5), 1e-4));
            Assert.True(wall.Face3D.Inside(new Point3D(0, 4.75, 1.5), 1e-4));
            Assert.False(wall.Face3D.Inside(new Point3D(0, 3.6, 1.5), 1e-4));
            Assert.False(wall.Face3D.Inside(new Point3D(0, 5.8, 1.5), 1e-4));
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

        // ── Crossing walls merged across storeys (review of 2026-10-26, M2) ─────────────────────────────
        // A junction is read from the crossing wall's material at this wall's line in the storey beyond the level, not
        // from its overall extent: one stepped face (as Clean3D merges it) and the same wall as two storey rectangles
        // must give the same result.

        /// <summary>A crossing wall at y = 6 reaching the west wall's line (x -1..1) in one storey and stopping 0.4 m short
        /// of it (x 0.4..1) in the other: one stepped face or two storey rectangles. No lateral reach of its own, so its
        /// geometry at the junction is what the test sets.</summary>
        private static List<SnappedPanel> CrossingWall(bool merged, bool meetsBelow)
        {
            double lower = meetsBelow ? -1 : 0.4, upper = meetsBelow ? 0.4 : -1;
            if (merged)
            {
                return new List<SnappedPanel> { new SnappedPanel(1, TestGeometry.CreatePlanarFace(
                    new Point3D(lower, 6, 0), new Point3D(1, 6, 0), new Point3D(1, 6, 6), new Point3D(upper, 6, 6), new Point3D(upper, 6, 3), new Point3D(lower, 6, 3)), 1, 0.3, 0) };
            }

            return new List<SnappedPanel>
            {
                new SnappedPanel(1, TestGeometry.CreatePlanarFace(new Point3D(lower, 6, 0), new Point3D(1, 6, 0), new Point3D(1, 6, 3), new Point3D(lower, 6, 3)), 1, 0.3, 0),
                new SnappedPanel(2, TestGeometry.CreatePlanarFace(new Point3D(upper, 6, 3), new Point3D(1, 6, 3), new Point3D(1, 6, 6), new Point3D(upper, 6, 6)), 1, 0.3, 0),
            };
        }

        /// <summary>The west wall (x = 0, reach 0.5) with one storey stopping 0.15 m short of the crossing wall's line
        /// (y 0..5.85) and the other reaching it (y 0..6): the lower storey short (<paramref name="shortBelow"/>, extended
        /// down through the first floor) or the upper one (extended up through it), with the crossing walls, in the
        /// stack of wide caps; optionally the lateral extend first.</summary>
        private static SnappedPanel ExtendBesideCrossingWalls(bool shortBelow, List<SnappedPanel> crossings, bool lateral)
        {
            SnappedPanel wall = shortBelow
                ? ShortGroundFloorWall(0.5)
                : new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                    new Point3D(0, 0, 0), new Point3D(0, 6, 0), new Point3D(0, 6, 3), new Point3D(0, 5.85, 3), new Point3D(0, 5.85, 6), new Point3D(0, 0, 6)), 1, 0.3, 0.5);
            List<SnappedPanel> panels = new List<SnappedPanel> { new SnappedPanel(100, WideCap(0), 1, 0.3, 0.5), new SnappedPanel(101, WideCap(3), 1, 0.3, 0.5), new SnappedPanel(102, WideCap(6), 1, 0.3, 0.5), wall };
            panels.AddRange(crossings);

            if (lateral)
            {
                Panel3DSnapSolver.ExtendWalls(panels, VerticalAngle, Overshoot, Tolerance);
            }

            Panel3DSnapSolver.Extend(panels, VerticalAngle, Overshoot, Tolerance);
            return wall;
        }

        [Theory]
        [InlineData(true, false, false)]  // the review's counterexample: before the fix the lower junction was filled
        [InlineData(true, true, false)]
        [InlineData(false, false, false)]  // the same wall as two storey rectangles (always open)
        [InlineData(false, true, false)]
        [InlineData(true, false, true)]   // upward: a crossing wall meeting the line downstairs only
        [InlineData(true, true, true)]
        [InlineData(false, false, true)]
        [InlineData(false, true, true)]
        public void Extend_CrossingWallMeetingTheLineInTheOtherStoreyOnly_SetBackStaysOpenInAnyRepresentation(bool merged, bool lateral, bool upward)
        {
            // Arrange: the crossing wall meets the west wall's line only in the storey the west wall reaches it in; in the
            // set-back storey it stops 0.4 m short - no wall to meet there.
            bool shortBelow = !upward;

            // Act
            SnappedPanel wall = ExtendBesideCrossingWalls(shortBelow, CrossingWall(merged, meetsBelow: !shortBelow), lateral);
            SnappedPanel split = ExtendBesideCrossingWalls(shortBelow, CrossingWall(false, meetsBelow: !shortBelow), lateral);

            // Assert: the set-back stays open, as with the split representation.
            Point3D setBack = new Point3D(0, 5.925, shortBelow ? 1.5 : 4.5);
            Assert.False(wall.Face3D.Inside(setBack, Tolerance), "No crossing wall meets the line in the set-back storey: no junction to close.");
            Assert.True(wall.Face3D.Inside(new Point3D(0, 3, shortBelow ? -0.025 : 6.025), Tolerance), "The wall must still reach its cap.");
            Assert.Equal(split.GetArea(), wall.GetArea(), 6);
        }

        [Theory]
        [InlineData(true, false, false)]
        [InlineData(true, true, false)]
        [InlineData(false, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, false, true)]
        [InlineData(true, true, true)]
        [InlineData(false, false, true)]
        [InlineData(false, true, true)]
        public void Extend_CrossingWallMeetingTheLineInTheSetBackStorey_JunctionClosedInAnyRepresentation(bool merged, bool lateral, bool upward)
        {
            // Arrange: control - the crossing wall meets the west wall's line in the set-back storey (and stops short of it
            // in the other): the set-back is a junction within the wall's reach.
            bool shortBelow = !upward;

            // Act
            SnappedPanel wall = ExtendBesideCrossingWalls(shortBelow, CrossingWall(merged, meetsBelow: shortBelow), lateral);
            SnappedPanel split = ExtendBesideCrossingWalls(shortBelow, CrossingWall(false, meetsBelow: shortBelow), lateral);

            // Assert: closed up to the crossing wall's line, no fin past it.
            Assert.True(wall.Face3D.Inside(new Point3D(0, 5.925, shortBelow ? 1.5 : 4.5), Tolerance), "The junction with the crossing wall must be closed.");
            Assert.False(wall.Face3D.Inside(new Point3D(0, 6.03, shortBelow ? 1.5 : 4.5), Tolerance), "No fin past the crossing wall.");
            Assert.Equal(split.GetArea(), wall.GetArea(), 6);
        }

        /// <summary>A crossing wall at y = 6 (x -1..1, z 0..6) with one opening (x <paramref name="openingFrom"/>..
        /// <paramref name="openingTo"/>, z 0.2..2.8) in the lower storey.</summary>
        private static List<SnappedPanel> CrossingWallWithOpening(double openingFrom, double openingTo)
        {
            Face3D face3D = Face3D.Create(new List<IClosedPlanar3D>
            {
                new Polygon3D(new List<Point3D> { new Point3D(-1, 6, 0), new Point3D(1, 6, 0), new Point3D(1, 6, 6), new Point3D(-1, 6, 6) }),
                new Polygon3D(new List<Point3D> { new Point3D(openingFrom, 6, 0.2), new Point3D(openingTo, 6, 0.2), new Point3D(openingTo, 6, 2.8), new Point3D(openingFrom, 6, 2.8) }),
            });

            return new List<SnappedPanel> { new SnappedPanel(1, face3D, 1, 0.3, 0) };
        }

        [Fact]
        public void ExtendWallsThenExtend_CrossingWallOpenAtTheJunction_FillOnlyWhereItHasMaterial()
        {
            // Arrange: the crossing wall has an opening over the junction (x -0.3..0.3) in the lower storey.
            List<SnappedPanel> crossings = CrossingWallWithOpening(-0.3, 0.3);

            // Act
            SnappedPanel wall = ExtendBesideCrossingWalls(true, crossings, true);

            // Assert: the junction is closed below and above the opening, open beside it.
            Assert.True(wall.Face3D.Inside(new Point3D(0, 5.925, 0.1), Tolerance), "Below the opening the crossing wall meets the line.");
            Assert.False(wall.Face3D.Inside(new Point3D(0, 5.925, 1.5), Tolerance), "Beside the opening no wall meets the line.");
            Assert.True(wall.Face3D.Inside(new Point3D(0, 5.925, 2.9), Tolerance), "Above the opening the crossing wall meets the line.");
        }

        [Fact]
        public void ExtendWallsThenExtend_CrossingWallOpeningAwayFromTheJunction_JunctionClosed()
        {
            // Arrange: control - the same opening 0.4 m away from the junction (x 0.4..0.8).
            List<SnappedPanel> crossings = CrossingWallWithOpening(0.4, 0.8);

            // Act
            SnappedPanel wall = ExtendBesideCrossingWalls(true, crossings, true);

            // Assert
            Assert.True(wall.Face3D.Inside(new Point3D(0, 5.925, 1.5), Tolerance), "The junction with the crossing wall must be closed.");
        }

        [Fact]
        public void ClipBeyondLevels_KeptExtensionNotConnectedToTheWall_IsReportedNotDroppedSilently()
        {
            // Arrange: a wall at x = 0 (u = y) whose original is the upper storey (y 0..4, z 3..6) with a stub (y 0..1, z 2.5..3)
            // below the level at z = 3, extended by a strip (y 1..1.15, z -0.05..3). A crossing wall at y = 1.15 has material
            // at the junction over z 1..2 only, so the kept junction fill there touches neither the stub nor the storey above.
            // Extend's own fill always attaches such a piece (the stub's column is filled to the target); this drives the
            // clip directly to check the report (review L2).
            Face3D original = TestGeometry.CreatePlanarFace(new Point3D(0, 0, 6), new Point3D(0, 0, 2.5), new Point3D(0, 1, 2.5), new Point3D(0, 1, 3), new Point3D(0, 4, 3), new Point3D(0, 4, 6));
            SnappedPanel wall = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 6), new Point3D(0, 0, 2.5), new Point3D(0, 1, 2.5), new Point3D(0, 1, -0.05), new Point3D(0, 1.15, -0.05), new Point3D(0, 1.15, 3), new Point3D(0, 4, 3), new Point3D(0, 4, 6)), 1, 0.3, 0.5);
            List<(double Z, IList<(double From, double To)> Columns, IList<(double U, double ZMin, double ZMax)> Crossings)> levels = new List<(double, IList<(double, double)>, IList<(double, double, double)>)>
            {
                (3.0, new List<(double, double)> { (-0.3, 4.3) }, new List<(double, double, double)> { (1.15, 1.0, 2.0) }),
            };

            // Act
            bool clipped = wall.ClipBeyondLevels(original, levels, false, Overshoot, Tolerance);

            // Assert: the wall keeps the piece holding the original; the disconnected 0.15 m2 is reported.
            Assert.True(clipped);
            Assert.True(wall.Face3D.Inside(new Point3D(0, 0.5, 2.75), Tolerance), "The original must be kept.");
            Assert.False(wall.Face3D.Inside(new Point3D(0, 1.075, 1.5), Tolerance));
            Assert.Contains(wall.ExtendDiagnostics, x => x.StartsWith("SAM_OCCT_EXTEND3D_LEVEL_CLIP_FAILED") && x.Contains("0.15 m2") && x.Contains("not connected"));
        }

        // ── Residual pieces judged by their own width (review of 2026-10-26 revision 3, finding 2) ─────────
        // The clip compared areas with tolerance x the whole wall's perimeter: on a long wall at a coarse tolerance a real
        // removal read as noise. Each removed, lost or discarded piece is now judged by its own mean width.

        [Theory]
        [InlineData(1e-6)]
        [InlineData(1e-4)]
        [InlineData(1e-3)] // 2ab5316: 0.06 m2 removed < 1e-3 x 72 m perimeter: filled silently, no clip, no failure
        public void Extend_LongWallWithNarrowSetBack_StaysOpenAtAnyTolerance(double tolerance)
        {
            // Arrange: the review's case - a 30 m wall whose ground-floor part stops 20 mm short of its end, no crossing wall.
            SnappedPanel wall = new SnappedPanel(0, TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(0, 29.98, 0), new Point3D(0, 29.98, 3), new Point3D(0, 30, 3), new Point3D(0, 30, 6), new Point3D(0, 0, 6)), 1, 0.3, 0.5);
            List<SnappedPanel> panels = new List<SnappedPanel> { new SnappedPanel(100, LongCap(0), 1, 0.3, 0.5), new SnappedPanel(101, LongCap(3), 1, 0.3, 0.5), new SnappedPanel(102, LongCap(6), 1, 0.3, 0.5), wall };

            // Act
            Panel3DSnapSolver.Extend(panels, VerticalAngle, Overshoot, tolerance);

            // Assert: the 20 mm x 3 m set-back is a real removal at every tolerance, made and reported as a clip.
            Assert.False(wall.Face3D.Inside(new Point3D(0, 29.99, 1.5), Tolerance), "The 20 mm set-back must stay open.");
            Assert.True(wall.Face3D.Inside(new Point3D(0, 15, -0.025), Tolerance), "The wall must still reach its floor.");
            Assert.Contains(wall.ExtendDiagnostics, x => x.StartsWith("SAM_OCCT_EXTEND3D_LEVEL_CLIP:"));
            Assert.DoesNotContain(wall.ExtendDiagnostics, x => x.StartsWith("SAM_OCCT_EXTEND3D_LEVEL_CLIP_FAILED"));
        }

        private static Face3D LongCap(double z)
        {
            return TestGeometry.CreatePlanarFace(new Point3D(-1, -1, z), new Point3D(1, -1, z), new Point3D(1, 31, z), new Point3D(-1, 31, z));
        }

        // ── Cap openings (Softhouse crash at 696dce0, review L1) ──────────────────────────────────────────
        // Softhouse (2026-10-26) Clean3D output reduced to the two faces that reproduce the revision-2 crash at 696dce0: the
        // ground-floor slab with its three openings and one wall over it, exactly as saved (each face's plane and its loops
        // in that plane, 1e-6 m). Rebuilding the slab from 3D points or re-orienting its loops does not reproduce the crash.

        // SoftHouseGroundSlab: plane origin, normal, axisY; then the outline and openings as (x, y) in that plane.
        private static readonly double[] SoftHouseGroundSlabPlane = { 12.400476316074108, 5.5989756584167472, 0.02500000037252904, 0, 0, -1, 0, -1, 0 };
        private static readonly double[][] SoftHouseGroundSlabLoops =
        {
            new[] { -13.302869, -2.873884, -15.549644, -2.873884, -15.547286, -2.923884, -19.040527, -2.923884, -19.040527, 2.923884, -19.040527, 4.044038, -18.803114, 4.044038, -18.928027, 6.056521, -19.040526, 6.280036, -19.040527, 6.280036, -19.040527, 6.280037, -19.040527, 6.281521, -19.800527, 6.281521, -19.800527, 8.125654, -19.800527, 12.216521, -13.741941, 12.216521, -13.522404, 10.171087, -13.74194, 12.216521, -7.744485, 12.216521, -7.484221, 8.125655, -7.744484, 12.216521, -1.828074, 12.216521, 4.109638, 12.216521, 4.109638, 8.125655, 4.109638, 2.979185, 2.823719, 2.948884, 2.837124, 2.948884, 2.862125, 2.948884, 4.109638, 2.948884, 4.109638, 0.680318, 4.109638, -0.739842, 4.109638, -2.923884, 1.785825, -2.923884, -1.674221, -2.923884, -1.674221, -2.873884, -3.985999, -2.873884, -3.984175, -2.923884, -7.484221, -2.923884, -9.774175, -2.923884, -13.302869, -2.923884 },
            new[] { -2.923321, 2.948884, -1.849221, 2.973884, -1.786721, 4.044037, -1.674221, 8.042336, -1.674221, 8.03532, -1.674221, 6.26532, -1.674221, 4.044038, -1.674221, 2.948884, -2.898321, 2.948884, -2.92332, 2.948884 },
            new[] { -7.596721, 4.044038, -7.484221, 8.111016, -7.484221, 8.033471, -7.484221, 6.303471, -7.484221, 4.044038, -7.484221, 2.948884, -8.726309, 2.948884, -8.743873, 2.948884, -7.659221, 2.973884 },
            new[] { -13.415368, 4.044038, -13.352868, 8.125654, -13.302869, 8.125654, -13.302869, 4.044038, -13.302869, 2.948884, -14.533287, 2.948884, -14.550331, 2.948884, -13.477868, 2.973884 },
        };

        // SoftHouseWall: plane origin, normal, axisY; then the outline and openings as (x, y) in that plane.
        private static readonly double[] SoftHouseWallPlane = { 2.3735876600162102, 2.7174809934699637, 3.4000000953674316, -0.99905387287327585, 0.043489758528973313, 0, 0, 0, -1 };
        private static readonly double[][] SoftHouseWallLoops =
        {
            new[] { -1.163644, -0.075, -1.163644, 3.375, -0.042429, 3.375, 5.810876, 3.375, 5.810876, -0.075, -0.042429, -0.075 },
        };

        /// <summary>A face as SAM stores it: <paramref name="plane"/> (origin, normal, axis Y) and its outline and openings
        /// as (x, y) in that plane, read without re-orienting or re-uniting the loops.</summary>
        private static Face3D SavedFace3D(double[] plane, double[][] loops)
        {
            System.Text.Json.Nodes.JsonObject Vector(string type, int i) => new System.Text.Json.Nodes.JsonObject { ["_type"] = type, ["X"] = plane[i], ["Y"] = plane[i + 1], ["Z"] = plane[i + 2] };
            System.Text.Json.Nodes.JsonObject Loop(double[] xy) => new System.Text.Json.Nodes.JsonObject
            {
                ["_type"] = "SAM.Geometry.Planar.Polygon2D,SAM.Geometry",
                ["Points"] = new System.Text.Json.Nodes.JsonArray(Enumerable.Range(0, xy.Length / 2).Select(i => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
                {
                    ["_type"] = "SAM.Geometry.Planar.Point2D,SAM.Geometry", ["X"] = xy[2 * i], ["Y"] = xy[2 * i + 1],
                }).ToArray()),
            };

            return new Face3D(new System.Text.Json.Nodes.JsonObject
            {
                ["_type"] = "SAM.Geometry.Spatial.Face3D,SAM.Geometry",
                ["ExternalEdge2D"] = Loop(loops[0]),
                ["InternalEdge2Ds"] = new System.Text.Json.Nodes.JsonArray(loops.Skip(1).Select(x => (System.Text.Json.Nodes.JsonNode)Loop(x)).ToArray()),
                ["Plane"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["_type"] = "SAM.Geometry.Spatial.Plane,SAM.Geometry",
                    ["Origin"] = Vector("SAM.Geometry.Spatial.Point3D,SAM.Geometry", 0),
                    ["Normal"] = Vector("SAM.Geometry.Spatial.Vector3D,SAM.Geometry", 3),
                    ["AxisY"] = Vector("SAM.Geometry.Spatial.Vector3D,SAM.Geometry", 6),
                },
            });
        }

        [Fact]
        public void Extend_SoftHouseGroundSlabWithThreeOpenings_CompletesAndKeepsTheWall()
        {
            // Arrange: SideCoverage at 696dce0 built the slab's plan face with all its openings at once; SAM united them into a
            // face with a null internal edge and the intersection with the wall's band threw (NullReferenceException in
            // FixEdges). The two-opening case above does not reproduce it. Tolerances as Extend3D uses them.
            Face3D slab = SavedFace3D(SoftHouseGroundSlabPlane, SoftHouseGroundSlabLoops);
            SnappedPanel wall = new SnappedPanel(1, SavedFace3D(SoftHouseWallPlane, SoftHouseWallLoops), 1, 0.3, 0.5);
            double area = wall.GetArea();
            List<SnappedPanel> panels = new List<SnappedPanel> { new SnappedPanel(0, slab, 1, 0.3, 0.5), wall };

            // Act
            System.Exception exception = Record.Exception(() => Panel3DSnapSolver.Extend(panels, VerticalAngle, Overshoot, SAM.Core.Tolerance.Distance));

            // Assert
            Assert.Equal(3, slab.GetInternalEdge3Ds().Count);
            Assert.Null(exception);
            Assert.True(wall.Face3D.IsValid());
            Assert.True(wall.GetArea() >= area - 1e-6, "The wall must keep its original area.");
        }

        // Softhouse (2026-10-26) Clean3D output reduced to the three faces on which the level clip judged a numerical
        // sliver as a clip (review L2): the first-floor slab with its seven openings, the roof over the wall and a wall
        // stepped at both floors, exactly as saved.
        private static readonly double[] SoftHouseFirstFloorPlane = { -6.8886449337005615, -1.6046118005828858, 3.4750000238418575, 0, 0, -1, 0, -1, 0 };
        private static readonly double[][] SoftHouseFirstFloorLoops =
        {
            new[] { 9.514946, -10.127471, 8.871261, -10.127471, 5.986253, -10.127471, 3.741835, -10.127472, 3.087584, -10.127471, 0.248594, -10.127471, 0.248594, -6.491673, 0.248594, -5.431673, 0.248594, -4.783551, 0.248594, -4.279703, 0.248594, -3.15955, 0.248594, -0.923551, 3.507584, -0.923551, 3.507584, -3.15955, 3.41325, -3.15955, 3.466073, -4.279704, 3.489833, -4.783551, 3.570469, -6.493551, 3.570471, -6.493551, 4.730834, -6.493551, 4.730834, -4.254889, 3.554334, -4.282314, 3.534464, -1.213806, 3.522153, -0.192, 3.507584, 0.829782, 0.248594, 0.829782, 0.248594, -0.922067, -0.248594, -0.922067, -0.248594, 0.922067, -0.511406, 0.922066, -0.511406, 5.012933, 5.54718, 5.012933, 11.544637, 5.012933, 17.461047, 5.012933, 17.60135, 1.282359, 11.80479, 1.282333, 11.78215, 1.279669, 11.8049, 0.922067, 17.6149, 0.922067, 17.6149, 1.144846, 23.39876, 1.144846, 23.39876, -4.224402, 22.11282, -4.254704, 22.126246, -4.254704, 22.151246, -4.254704, 23.39876, -4.254704, 23.39876, -6.496417, 23.39876, -6.523269, 23.39876, -7.943429, 23.398759, -8.198545, 23.39876, -8.198545, 23.39876, -10.127471, 21.074946, -10.127471, 20.480451, -10.127471, 20.48045, -8.198545, 20.48045, -10.127471, 17.6149, -10.127471, 15.304947, -10.127471, 14.695476, -10.127471, 11.804901, -10.127471, 9.514947, -10.127471 },
            new[] { 9.405673, -7.617228, 9.311261, -7.617228, 9.311261, -7.617224, 9.136631, -5.467228, 9.312081, -5.467228 },
            new[] { 9.435306, -4.254704, 9.2593, -4.254704, 9.251379, -4.072756 },
            new[] { 5.986253, -4.254704, 4.738775, -4.254704, 5.811252, -4.229704, 5.873752, -3.15955, 5.910377, -0.767707, 6.086796, -0.942228, 5.986253, -0.942228, 5.986253, -3.15955 },
            new[] { 6.03654, 0.922067, 5.936252, 0.922067, 5.898394, 0.922067, 0.248594, 0.922067, 0.248594, 1.28226, 5.898393, 1.282446, 5.898393, 1.282346, 5.947584, 1.282346, 5.94949, 1.264587, 5.986177, 1.282309, 5.986258, 1.282309 },
            new[] { 17.6149, -4.254704, 16.390801, -4.254704, 16.365801, -4.254704, 16.3658, -4.254704, 17.439901, -4.229704, 17.502401, -3.15955, 17.6149, 0.838749, 17.6149, 0.831732, 20.952301, 0.831732, 20.952301, -0.938268, 17.6149, -0.938268, 17.6149, -3.15955 },
            new[] { 10.562812, -4.254704, 10.545195, -4.254704, 11.6299, -4.229703, 11.6924, -3.159549, 11.804901, 0.90744, 11.804901, 0.829884, 15.124149, 0.829884, 15.124149, -0.900116, 11.804901, -0.900116, 11.804901, -3.15955, 11.804901, -4.254704 },
            new[] { 8.884077, 1.282346, 11.781976, 1.282383, 11.781979, 1.282346 },
        };

        private static readonly double[] SoftHouseRoofPlane = { 15.015178442001345, 6.7073322534561157, 9.4499998092651314, 0, 0, -1, 0, -1, 0 };
        private static readonly double[][] SoftHouseRoofLoops =
        {
            new[] { -15.917571, -1.815527, -18.816239, -1.815527, -21.655229, -1.815527, -21.655229, 1.820271, -21.655229, 2.880271, -21.655229, 3.528393, -21.655229, 9.59429, -16.00543, 9.59429, -15.917571, 9.59429, -10.098923, 9.59429, -4.288923, 9.59429, 1.494936, 9.59429, 1.494936, 7.274402, 1.494936, 1.815527, 1.494936, -1.815527, -1.423373, -1.815527, -1.494936, -1.815527, -4.288923, -1.815527, -10.098923, -1.815527, -13.032562, -1.815527 },
        };

        private static readonly double[] SoftHouseSteppedWallPlane = { -0.90239229798316956, 3.773711621761322, 1.7500000121071937, -1, 0, 0, 0, 0, -1 };
        private static readonly double[][] SoftHouseSteppedWallLoops =
        {
            new[] { -6.660669, -7.7, -6.660669, -4.725, -7.118981, -4.725, -7.118981, -1.7, -6.660669, -1.7, -6.300391, -1.7, -6.300391, 1.725, -2.218774, 1.725, -1.12362, 1.725, -1.09862, 1.725, 1.115228, 1.725, 1.12362, 1.725, 2.565106, 1.725, 4.749148, 1.725, 4.749148, -1.7, 4.749148, -1.725, 4.749148, -4.725, 4.749147, -4.725, 4.749147, -7.7, 1.118094, -7.7, 1.115228, -7.7, 0.088905, -7.7, -4.363802, -7.7 },
        };

        [Fact]
        public void Extend_ShadowGrazingTheExtension_IsNotAClipNorAFailure()
        {
            // Arrange: the clip's shadow grazes the wall's top extension (about 1e-5 m2 along a 12 m wall); compared with the
            // 1e-6 distance tolerance as an area, that sliver made the clip run and then report the original 1.5e-6 m2 short
            // (SAM_OCCT_EXTEND3D_LEVEL_CLIP_FAILED at 5600f90; silently kept whole before).
            SnappedPanel wall = new SnappedPanel(2, SavedFace3D(SoftHouseSteppedWallPlane, SoftHouseSteppedWallLoops), 1, 0.3, 0.5);
            List<SnappedPanel> panels = new List<SnappedPanel>
            {
                new SnappedPanel(0, SavedFace3D(SoftHouseFirstFloorPlane, SoftHouseFirstFloorLoops), 1, 0.3, 0.5),
                new SnappedPanel(1, SavedFace3D(SoftHouseRoofPlane, SoftHouseRoofLoops), 1, 0.3, 0.5),
                wall,
            };

            // Act
            Panel3DSnapSolver.Extend(panels, VerticalAngle, Overshoot, SAM.Core.Tolerance.Distance);

            // Assert: nothing material to clip at the top, and nothing reported as failed.
            Assert.DoesNotContain(wall.ExtendDiagnostics, x => x.StartsWith("SAM_OCCT_EXTEND3D_LEVEL_CLIP_FAILED"));
            Assert.DoesNotContain(wall.ExtendDiagnostics, x => x.StartsWith("SAM_OCCT_EXTEND3D_LEVEL_CLIP:") && x.Contains(" top extension"));
            Assert.True(wall.Face3D.IsValid());
        }
    }
}
