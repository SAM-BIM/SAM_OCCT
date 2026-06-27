// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare static Create/Query classes.
using GeometryCreate = SAM.Geometry.OCCT.Create;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Drives the real native OCCT shells-from-walls build (issue #24): vertical wall
    /// Face3Ds plus generated horizontal patches go through one BOPAlgo_MakerVolume
    /// pass; the closed cells are the shells and the horizontal cell faces are the
    /// generated floors/roofs. Auto-skips when the native library is absent.
    /// </summary>
    public class ShellsFromVerticalFace3DsIntegrationTests
    {
        private static Face3D CreateWallFace(double x0, double y0, double x1, double y1, double zMin, double zMax)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(x0, y0, zMin),
                new Point3D(x1, y1, zMin),
                new Point3D(x1, y1, zMax),
                new Point3D(x0, y0, zMax));
        }

        /// <summary>The four vertical walls of a rectangular room - no floor, no roof.</summary>
        private static List<Face3D> CreateRoomWalls(double x0, double y0, double x1, double y1, double zMin, double zMax)
        {
            return new List<Face3D>
            {
                CreateWallFace(x0, y0, x1, y0, zMin, zMax), // front (y0)
                CreateWallFace(x1, y0, x1, y1, zMin, zMax), // right (x1)
                CreateWallFace(x1, y1, x0, y1, zMin, zMax), // back (y1)
                CreateWallFace(x0, y1, x0, y0, zMin, zMax)  // left (x0)
            };
        }

        [SkippableFact]
        public void ShellsFromVerticalFace3Ds_FourWallsOfBox_ProducesOneShellWithFloorAndRoof()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - the four walls of a 1 x 1 x 2 room; floor and roof are missing.
            List<Face3D> walls = CreateRoomWalls(0, 0, 1, 1, 0, 2);

            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(walls, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result);

            // Assert
            Assert.True(result.NativeAvailable);
            Assert.True(result.Success);
            Assert.NotNull(shells);
            Assert.Single(shells);
            Assert.Equal(2.0, result.Cells[0].Volume, 2);
            Assert.NotNull(horizontalFace3Ds);
            Assert.Equal(2, horizontalFace3Ds.Count);
            Assert.All(horizontalFace3Ds, face3D => Assert.Equal(1.0, face3D.GetArea(), 2));
            List<double> elevations = horizontalFace3Ds.ConvertAll(x => x.GetBoundingBox().Min.Z);
            elevations.Sort();
            Assert.Equal(0.0, elevations[0], 2);
            Assert.Equal(2.0, elevations[1], 2);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_BUILD_SUCCESS");
        }

        [SkippableFact]
        public void ShellsFromVerticalFace3Ds_TwoRoomsSharedWall_ProducesTwoShellsAndFourHorizontalFaces()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - 2 x 1 plan split by an internal wall at x = 1; 7 walls, height 2.
            List<Face3D> walls = new List<Face3D>
            {
                CreateWallFace(0, 0, 1, 0, 0, 2), // front, room A
                CreateWallFace(1, 0, 2, 0, 0, 2), // front, room B
                CreateWallFace(2, 0, 2, 1, 0, 2), // right
                CreateWallFace(2, 1, 1, 1, 0, 2), // back, room B
                CreateWallFace(1, 1, 0, 1, 0, 2), // back, room A
                CreateWallFace(0, 1, 0, 0, 0, 2), // left
                CreateWallFace(1, 0, 1, 1, 0, 2)  // shared internal wall
            };

            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(walls, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result);

            // Assert
            Assert.True(result.NativeAvailable);
            Assert.True(result.Success);
            Assert.NotNull(shells);
            Assert.Equal(2, shells.Count);
            Assert.All(result.Cells, cell => Assert.Equal(2.0, cell.Volume, 2));
            Assert.NotNull(horizontalFace3Ds);
            Assert.Equal(4, horizontalFace3Ds.Count);
            Assert.All(horizontalFace3Ds, face3D => Assert.Equal(1.0, face3D.GetArea(), 2));
        }

        [SkippableFact]
        public void ShellsFromVerticalFace3Ds_DifferentWallHeights_ProducesStackedCells()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - room A (0..1) is 2 high, room B (1..2) is 3 high; the shared
            // wall is full height. Elevations {0, 2, 3} stack room B into two cells.
            List<Face3D> walls = new List<Face3D>
            {
                CreateWallFace(0, 0, 1, 0, 0, 2), // front, room A
                CreateWallFace(1, 0, 2, 0, 0, 3), // front, room B
                CreateWallFace(2, 0, 2, 1, 0, 3), // right
                CreateWallFace(2, 1, 1, 1, 0, 3), // back, room B
                CreateWallFace(1, 1, 0, 1, 0, 2), // back, room A
                CreateWallFace(0, 1, 0, 0, 0, 2), // left
                CreateWallFace(1, 0, 1, 1, 0, 3)  // shared internal wall, full height
            };

            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(walls, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result);

            // Assert
            Assert.True(result.NativeAvailable);
            Assert.True(result.Success);
            Assert.NotNull(shells);
            Assert.Equal(3, shells.Count);
            Assert.Equal(5.0, result.Cells.Sum(x => x.Volume), 2);
            // z = 0: two floors; z = 2: room A roof + room B intermediate floor
            // (shared by the stacked cells, deduped to one); z = 3: room B roof.
            Assert.NotNull(horizontalFace3Ds);
            Assert.Equal(5, horizontalFace3Ds.Count);
        }

        [SkippableFact]
        public void ShellsFromVerticalFace3Ds_WallTopsWithinSnapTolerance_ProducesSingleShell()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - one wall top is 0.5 * MacroDistance above the others; the
            // elevation clustering must not create a micro-sliver level for it.
            double snapTolerance = SAM.Core.Tolerance.MacroDistance;
            List<Face3D> walls = new List<Face3D>
            {
                CreateWallFace(0, 0, 1, 0, 0, 2),
                CreateWallFace(1, 0, 1, 1, 0, 2 + (0.5 * snapTolerance)),
                CreateWallFace(1, 1, 0, 1, 0, 2),
                CreateWallFace(0, 1, 0, 0, 0, 2)
            };

            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(walls, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result);

            // Assert
            Assert.True(result.NativeAvailable);
            Assert.True(result.Success);
            Assert.NotNull(shells);
            Assert.Single(shells);
            Assert.Equal(2.0, result.Cells[0].Volume, 2);
            Assert.NotNull(horizontalFace3Ds);
            Assert.Equal(2, horizontalFace3Ds.Count);
        }

        [SkippableFact]
        public void ShellsFromVerticalFace3Ds_SlopedModeEqualWalls_ProducesOnePlanarFlatRoof()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - four equal-height walls; in Sloped mode the coplanar (here flat)
            // wall tops collapse to a single planar roof, so the result matches Flat mode.
            List<Face3D> walls = CreateRoomWalls(0, 0, 1, 1, 0, 2);

            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(walls, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result, roofMode: OcctRoofMode.Sloped);

            // Assert
            Assert.True(result.NativeAvailable);
            Assert.True(result.Success);
            Assert.NotNull(shells);
            Assert.Single(shells);
            Assert.Equal(2.0, result.Cells[0].Volume, 2);
            Assert.NotNull(horizontalFace3Ds);
            Assert.Equal(2, horizontalFace3Ds.Count);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_ROOF_PLANAR");
        }

        [SkippableFact]
        public void ShellsFromVerticalFace3Ds_SlopedModeMonoPitch_ProducesOneTiltedPlanarRoof()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a unit footprint whose four wall tops all lie on the plane
            // z = 2 + y (a mono-pitch roof). The two y-walls are rectangles at z = 2 and
            // z = 3; the two x-walls are trapezoids sloping between them.
            List<Face3D> walls = new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(1, 0, 2), new Point3D(0, 0, 2)), // y = 0, top z = 2
                TestGeometry.CreatePlanarFace(new Point3D(0, 1, 0), new Point3D(1, 1, 0), new Point3D(1, 1, 3), new Point3D(0, 1, 3)), // y = 1, top z = 3
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(0, 1, 0), new Point3D(0, 1, 3), new Point3D(0, 0, 2)), // x = 0 trapezoid
                TestGeometry.CreatePlanarFace(new Point3D(1, 0, 0), new Point3D(1, 1, 0), new Point3D(1, 1, 3), new Point3D(1, 0, 2))  // x = 1 trapezoid
            };

            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(walls, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result, roofMode: OcctRoofMode.Sloped);

            // Assert
            Assert.True(result.NativeAvailable);
            Assert.True(result.Success);
            Assert.NotNull(shells);
            Assert.Single(shells);
            // Volume under z = 2 + y over the unit square is 2 + 0.5 = 2.5.
            Assert.Equal(2.5, result.Cells[0].Volume, 2);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_ROOF_PLANAR");
            // One floor (horizontal) plus one tilted roof face.
            Assert.NotNull(horizontalFace3Ds);
            Assert.Equal(2, horizontalFace3Ds.Count);
            Assert.Contains(horizontalFace3Ds, face3D => System.Math.Abs(face3D.GetPlane().Normal.Z) < 0.99);
        }

        [SkippableFact]
        public void ShellsFromVerticalFace3Ds_SlopedModeGable_ProducesTriangulatedRoof()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a 2 x 1 footprint with a gable: the two long eaves walls top out
            // at z = 2, the two short end walls are pentagons peaking at a ridge z = 3.
            // The wall tops are not coplanar, so the roof is triangulated.
            List<Face3D> walls = new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 0, 2), new Point3D(0, 0, 2)), // eaves y = 0
                TestGeometry.CreatePlanarFace(new Point3D(0, 1, 0), new Point3D(2, 1, 0), new Point3D(2, 1, 2), new Point3D(0, 1, 2)), // eaves y = 1
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(0, 1, 0), new Point3D(0, 1, 2), new Point3D(0, 0.5, 3), new Point3D(0, 0, 2)), // gable end x = 0
                TestGeometry.CreatePlanarFace(new Point3D(2, 0, 0), new Point3D(2, 1, 0), new Point3D(2, 1, 2), new Point3D(2, 0.5, 3), new Point3D(2, 0, 2))  // gable end x = 2
            };

            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(walls, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result, roofMode: OcctRoofMode.Sloped);

            // Assert
            Assert.True(result.NativeAvailable);
            Assert.True(result.Success);
            Assert.NotNull(shells);
            Assert.Single(shells);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_ROOF_TRIANGULATED");
            // The ridge lifts the volume above the flat-eaves box (2 x 1 x 2 = 4).
            Assert.True(result.Cells[0].Volume > 4.0);
            Assert.True(result.Cells[0].Volume <= 6.0);
            // At least one generated face is a tilted roof slope.
            Assert.NotNull(horizontalFace3Ds);
            Assert.Contains(horizontalFace3Ds, face3D => System.Math.Abs(face3D.GetPlane().Normal.Z) < 0.99);
        }

        [SkippableFact]
        public void ShellsFromVerticalFace3Ds_OpenWalls_ReturnsNoShellsAndReportsOpenBoundary()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - only three walls; the plan is open, so no closed cell exists.
            List<Face3D> walls = new List<Face3D>
            {
                CreateWallFace(0, 0, 1, 0, 0, 2),
                CreateWallFace(1, 0, 1, 1, 0, 2),
                CreateWallFace(1, 1, 0, 1, 0, 2)
            };

            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(walls, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result);

            // Assert - no shells, and the open boundary is reported so the gap is actionable.
            Assert.True(result.NativeAvailable);
            Assert.True(shells == null || shells.Count == 0);
            Assert.True(horizontalFace3Ds == null || horizontalFace3Ds.Count == 0);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_OPEN" && x.Severity == OcctDiagnosticSeverity.Warning);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_OPEN_SHELL_ANALYSIS");
        }

        [SkippableFact]
        public void ShellsFromVerticalFace3Ds_PointInOneRoom_KeepsOnlyThatShell()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - two rooms sharing a wall; one selection point inside room A.
            List<Face3D> walls = new List<Face3D>
            {
                CreateWallFace(0, 0, 1, 0, 0, 2),
                CreateWallFace(1, 0, 2, 0, 0, 2),
                CreateWallFace(2, 0, 2, 1, 0, 2),
                CreateWallFace(2, 1, 1, 1, 0, 2),
                CreateWallFace(1, 1, 0, 1, 0, 2),
                CreateWallFace(0, 1, 0, 0, 0, 2),
                CreateWallFace(1, 0, 1, 1, 0, 2)
            };
            List<Point3D> points = new List<Point3D> { new Point3D(0.5, 0.5, 1) };

            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(walls, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result, roofMode: OcctRoofMode.Flat, point3Ds: points);

            // Assert - only room A survives, with its own floor and roof.
            Assert.True(result.Success);
            Assert.NotNull(shells);
            Assert.Single(shells);
            Assert.Equal(1.0, shells[0].GetBoundingBox().Max.X, 2); // room A is x:0..1, room B (x:1..2) was dropped
            Assert.True(shells[0].Inside(new Point3D(0.5, 0.5, 1)));
            Assert.Equal(2, horizontalFace3Ds.Count);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_POINT_SELECTION");
        }

        [SkippableFact]
        public void ShellsFromVerticalFace3Ds_PointOutsideAllRooms_ReturnsEmptyWithDiagnostic()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a single closed room and a point well outside it.
            List<Face3D> walls = CreateRoomWalls(0, 0, 1, 1, 0, 2);
            List<Point3D> points = new List<Point3D> { new Point3D(10, 10, 1) };

            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(walls, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result, point3Ds: points);

            // Assert - build succeeded but nothing matched, so an empty (not null) result.
            Assert.NotNull(shells);
            Assert.Empty(shells);
            Assert.NotNull(horizontalFace3Ds);
            Assert.Empty(horizontalFace3Ds);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_POINT_NONE" && x.Severity == OcctDiagnosticSeverity.Warning);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_POINT_UNMATCHED");
        }

        [SkippableFact]
        public void ShellsFromVerticalFace3Ds_TwoPointsInOneRoom_WarnsDuplicate()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - one room, two selection points both inside it.
            List<Face3D> walls = CreateRoomWalls(0, 0, 1, 1, 0, 2);
            List<Point3D> points = new List<Point3D> { new Point3D(0.3, 0.3, 1), new Point3D(0.7, 0.7, 1) };

            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(walls, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result, point3Ds: points);

            // Assert
            Assert.NotNull(shells);
            Assert.Single(shells);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_POINT_DUPLICATE" && x.Severity == OcctDiagnosticSeverity.Warning);
        }

        /// <summary>Four walls of a 1 x 1 x 2 room with the front-right corner split by ~0.05 (drifted input).</summary>
        private static List<Face3D> CreateDriftedRoomWalls()
        {
            return new List<Face3D>
            {
                CreateWallFace(0, 0, 1, 0, 0, 2),       // front ends at (1, 0)
                CreateWallFace(1.05, 0, 1.05, 1, 0, 2), // right starts at (1.05, 0) - 0.05 gap
                CreateWallFace(1.05, 1, 0, 1, 0, 2),    // back
                CreateWallFace(0, 1, 0, 0, 0, 2)        // left
            };
        }

        [SkippableFact]
        public void ShellsFromVerticalFace3Ds_DriftedWallsNoWeld_DoesNotClose()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - the front-right corner is split by 0.05, beyond the default weld.
            List<Face3D> walls = CreateDriftedRoomWalls();

            // Act - no weld tolerance.
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(walls, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result);

            // Assert - the gap leaves the volume open.
            Assert.True(result.NativeAvailable);
            Assert.True(shells == null || shells.Count == 0);
        }

        [SkippableFact]
        public void ShellsFromVerticalFace3Ds_DriftedWallsWithWeld_ProducesClosedShell()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - same drifted walls.
            List<Face3D> walls = CreateDriftedRoomWalls();

            // Act - weld the 0.05 gap with a 0.1 weld tolerance.
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(walls, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result, weldTolerance: 0.1);

            // Assert - welding snaps the split corner together, so the room closes.
            Assert.True(result.Success);
            Assert.NotNull(shells);
            Assert.Single(shells);
            Assert.InRange(result.Cells[0].Volume, 1.8, 2.2);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_WELDED");
        }

        [SkippableFact]
        public void ShellsFromVerticalFace3Ds_BatteredWalls_TreatedAsWallsNotCaps()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a frustum: four inward-leaning (battered) walls, each tilted ~18
            // degrees from vertical (normal Z ~ 0.316). They must be classified as walls,
            // not caps - otherwise the floor and roof are wrongly suppressed.
            List<Face3D> walls = new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(4, 0, 0), new Point3D(3, 1, 3), new Point3D(1, 1, 3)),
                TestGeometry.CreatePlanarFace(new Point3D(4, 4, 0), new Point3D(0, 4, 0), new Point3D(1, 3, 3), new Point3D(3, 3, 3)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 4, 0), new Point3D(0, 0, 0), new Point3D(1, 1, 3), new Point3D(1, 3, 3)),
                TestGeometry.CreatePlanarFace(new Point3D(4, 0, 0), new Point3D(4, 4, 0), new Point3D(3, 3, 3), new Point3D(3, 1, 3))
            };

            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(walls, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result);

            // Assert - the battered walls are walls, so the floor and roof are generated.
            Assert.True(result.Success);
            Assert.NotNull(shells);
            Assert.Single(shells);
            Assert.DoesNotContain(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_USER_CAP");
            Assert.NotNull(horizontalFace3Ds);
            Assert.Equal(2, horizontalFace3Ds.Count);
        }

        /// <summary>A horizontal cap (floor or roof) spanning the unit footprint at the given elevation.</summary>
        private static Face3D CreateHorizontalCap(double x0, double y0, double x1, double y1, double z)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(x0, y0, z),
                new Point3D(x1, y0, z),
                new Point3D(x1, y1, z),
                new Point3D(x0, y1, z));
        }

        [SkippableFact]
        public void ShellsFromVerticalFace3Ds_WallsPlusSuppliedRoof_GeneratesOnlyFloor()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - four walls of a 1 x 1 x 2 room plus a supplied roof at z = 2.
            // Only the missing floor should be generated; the roof is the user's.
            List<Face3D> face3Ds = CreateRoomWalls(0, 0, 1, 1, 0, 2);
            face3Ds.Add(CreateHorizontalCap(0, 0, 1, 1, 2));

            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(face3Ds, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(shells);
            Assert.Single(shells);
            Assert.Equal(2.0, result.Cells[0].Volume, 2);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_USER_CAP");
            // Only the floor (z = 0) is reported as newly created; the supplied roof is not.
            Assert.NotNull(horizontalFace3Ds);
            Assert.Single(horizontalFace3Ds);
            Assert.Equal(0.0, horizontalFace3Ds[0].GetBoundingBox().Min.Z, 2);
        }

        [SkippableFact]
        public void ShellsFromVerticalFace3Ds_WallsPlusSuppliedFloor_GeneratesOnlyRoof()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - four walls plus a supplied floor at z = 0; only the roof is missing.
            List<Face3D> face3Ds = CreateRoomWalls(0, 0, 1, 1, 0, 2);
            face3Ds.Add(CreateHorizontalCap(0, 0, 1, 1, 0));

            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(face3Ds, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result);

            // Assert
            Assert.True(result.Success);
            Assert.Single(shells);
            Assert.Equal(2.0, result.Cells[0].Volume, 2);
            // Only the roof (z = 2) is reported as newly created; the supplied floor is not.
            Assert.NotNull(horizontalFace3Ds);
            Assert.Single(horizontalFace3Ds);
            Assert.Equal(2.0, horizontalFace3Ds[0].GetBoundingBox().Min.Z, 2);
        }

        [SkippableFact]
        public void ShellsFromVerticalFace3Ds_SlopedModeSuppliedRoof_SkipsGeneratedRoof()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - four walls plus a supplied roof at the wall top; in Sloped mode
            // the supplied cap must be used instead of generating a wall-top roof.
            List<Face3D> face3Ds = CreateRoomWalls(0, 0, 1, 1, 0, 2);
            face3Ds.Add(CreateHorizontalCap(0, 0, 1, 1, 2));

            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(face3Ds, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result, roofMode: OcctRoofMode.Sloped);

            // Assert
            Assert.True(result.Success);
            Assert.Single(shells);
            Assert.Equal(2.0, result.Cells[0].Volume, 2);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_ROOF_SUPPLIED");
            Assert.DoesNotContain(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_ROOF_PLANAR" || x.Code == "SAM_OCCT_VERTICAL_SHELLS_ROOF_TRIANGULATED");
        }
    }
}
