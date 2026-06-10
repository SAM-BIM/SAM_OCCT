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
        public void ShellsFromVerticalFace3Ds_OpenWalls_ReturnsNoShells()
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

            // Assert
            Assert.True(result.NativeAvailable);
            Assert.True(shells == null || shells.Count == 0);
            Assert.True(horizontalFace3Ds == null || horizontalFace3Ds.Count == 0);
        }
    }
}
