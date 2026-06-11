// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare static Query/Create classes.
using GeometryCreate = SAM.Geometry.OCCT.Create;
using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Validates the ShellsFromVerticalFace3Ds guard clauses and the elevation
    /// clustering helper, which run before any native call - so these are
    /// deterministic on any platform / CI agent (issue #24).
    /// </summary>
    public class ShellsFromVerticalFace3DsGuardTests
    {
        private static Face3D CreateWallFace(double x0, double y0, double x1, double y1, double zMin, double zMax)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(x0, y0, zMin),
                new Point3D(x1, y1, zMin),
                new Point3D(x1, y1, zMax),
                new Point3D(x0, y0, zMax));
        }

        [Fact]
        public void ShellsFromVerticalFace3Ds_NullInput_ReturnsNullWithEmptyDiagnostic()
        {
            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(null, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result);

            // Assert
            Assert.Null(shells);
            Assert.Null(horizontalFace3Ds);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_INPUT_EMPTY" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void ShellsFromVerticalFace3Ds_EmptyInput_ReturnsNullWithEmptyDiagnostic()
        {
            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(new List<Face3D>(), out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result);

            // Assert
            Assert.Null(shells);
            Assert.Null(horizontalFace3Ds);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_INPUT_EMPTY" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void ShellsFromVerticalFace3Ds_SingleElevationOverride_ReturnsNullWithInsufficientElevationsDiagnostic()
        {
            // Arrange
            List<Face3D> face3Ds = new List<Face3D> { CreateWallFace(0, 0, 1, 0, 0, 2) };

            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(face3Ds, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result, new List<double> { 0 });

            // Assert
            Assert.Null(shells);
            Assert.Null(horizontalFace3Ds);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_INSUFFICIENT_ELEVATIONS" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void ShellsFromVerticalFace3Ds_HorizontalFaceInput_TreatedAsUserCap()
        {
            // Arrange - a horizontal face is a supplied cap (mixed input), not a wall.
            // With a single elevation override and no walls the call returns before any
            // native build, but the cap is recognised.
            List<Face3D> face3Ds = new List<Face3D> { TestGeometry.CreateUnitQuadFace() };

            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(face3Ds, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result, new List<double> { 0 });

            // Assert
            Assert.Null(shells);
            Assert.Null(horizontalFace3Ds);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_USER_CAP" && x.Severity == OcctDiagnosticSeverity.Info);
        }

        [Fact]
        public void ShellsFromVerticalFace3Ds_SlopedInsufficientWallTops_ReturnsNullWithRoofFailedDiagnostic()
        {
            // Arrange - one wall cannot define a roof (its top is a single edge, so the
            // wall-top envelope has fewer than three plan locations). Sloped mode then
            // returns before any native build.
            List<Face3D> face3Ds = new List<Face3D> { CreateWallFace(0, 0, 1, 0, 0, 2) };

            // Act
            List<Shell> shells = GeometryCreate.ShellsFromVerticalFace3Ds(face3Ds, out List<Face3D> horizontalFace3Ds, out OcctCellComplexResult result, roofMode: OcctRoofMode.Sloped);

            // Assert
            Assert.Null(shells);
            Assert.Null(horizontalFace3Ds);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VERTICAL_SHELLS_ROOF_FAILED" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void ClusteredElevations_WallsWithNearLevels_MergesWithinSnapTolerance()
        {
            // Arrange - two walls sharing the bottom; tops 0.5 * MacroDistance apart.
            double snapTolerance = Core.Tolerance.MacroDistance;
            List<Face3D> face3Ds = new List<Face3D>
            {
                CreateWallFace(0, 0, 1, 0, 0, 2.0),
                CreateWallFace(1, 0, 1, 1, 0, 2.0 + (0.5 * snapTolerance))
            };

            // Act
            List<double> elevations = GeometryQuery.ClusteredElevations(face3Ds, snapTolerance);

            // Assert
            Assert.Equal(2, elevations.Count);
            Assert.Equal(0.0, elevations[0], 6);
            Assert.True(System.Math.Abs(elevations[1] - 2.0) <= snapTolerance);
        }

        [Fact]
        public void ClusteredElevations_WallsWithDistinctLevels_KeepsSeparateElevations()
        {
            // Arrange - tops 0.1 apart, far beyond the default MacroDistance snap.
            List<Face3D> face3Ds = new List<Face3D>
            {
                CreateWallFace(0, 0, 1, 0, 0, 2.0),
                CreateWallFace(1, 0, 1, 1, 0, 2.1)
            };

            // Act
            List<double> elevations = GeometryQuery.ClusteredElevations(face3Ds);

            // Assert
            Assert.Equal(3, elevations.Count);
            Assert.Equal(0.0, elevations[0], 6);
            Assert.Equal(2.0, elevations[1], 6);
            Assert.Equal(2.1, elevations[2], 6);
        }

        [Fact]
        public void ClusteredElevations_ChainedValues_MergeIntoSingleCluster()
        {
            // Arrange - consecutive gaps are within the snap tolerance, so the chain
            // collapses into one cluster represented by its mean.
            List<double> values = new List<double> { 0.0, 0.004, 0.008 };

            // Act
            List<double> elevations = GeometryQuery.ClusteredElevations(values, 0.005);

            // Assert
            Assert.Single(elevations);
            Assert.Equal(0.004, elevations[0], 6);
        }

        [Fact]
        public void ClusteredElevations_NullOrEmpty_ReturnsEmpty()
        {
            // Act
            List<double> elevations_NullFaces = GeometryQuery.ClusteredElevations((List<Face3D>)null);
            List<double> elevations_NullValues = GeometryQuery.ClusteredElevations((List<double>)null);
            List<double> elevations_Empty = GeometryQuery.ClusteredElevations(new List<Face3D>());

            // Assert
            Assert.Empty(elevations_NullFaces);
            Assert.Empty(elevations_NullValues);
            Assert.Empty(elevations_Empty);
        }
    }
}
