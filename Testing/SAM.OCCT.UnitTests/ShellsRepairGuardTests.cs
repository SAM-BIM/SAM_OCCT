// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare a static Query class.
using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Validates the ShellsRepair guard clauses and the pure-managed tiny-face
    /// removal helper (issue #11). None of these require the native OCCT library,
    /// so they run deterministically on any platform / CI agent.
    /// </summary>
    public class ShellsRepairGuardTests
    {
        private static Face3D CreateSmallQuadFace(double side)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0),
                new Point3D(side, 0, 0),
                new Point3D(side, side, 0),
                new Point3D(0, side, 0));
        }

        [Fact]
        public void ShellsRepair_NullInput_ReturnsNullWithEmptyDiagnostic()
        {
            // Act
            List<Shell> shells = GeometryQuery.ShellsRepair((IEnumerable<Shell>)null, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.Null(shells);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_REPAIR_INPUT_EMPTY" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void ShellsRepair_AllNullEntries_ReturnsNullWithEmptyDiagnostic()
        {
            // Arrange
            List<Shell> input = new List<Shell> { null, null };

            // Act
            List<Shell> shells = GeometryQuery.ShellsRepair(input, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.Null(shells);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_REPAIR_INPUT_EMPTY");
        }

        [Fact]
        public void RemoveSmallFace3Ds_NullInput_ReturnsNullAndZeroRemoved()
        {
            // Act
            List<Face3D> result = GeometryQuery.RemoveSmallFace3Ds(null, 0.01, out int removedCount);

            // Assert
            Assert.Null(result);
            Assert.Equal(0, removedCount);
        }

        [Fact]
        public void RemoveSmallFace3Ds_MinAreaZero_KeepsEveryFace()
        {
            // Arrange
            List<Face3D> face3Ds = new List<Face3D>
            {
                TestGeometry.CreateUnitQuadFace(),
                CreateSmallQuadFace(0.001)
            };

            // Act
            List<Face3D> result = GeometryQuery.RemoveSmallFace3Ds(face3Ds, 0.0, out int removedCount);

            // Assert
            Assert.Equal(2, result.Count);
            Assert.Equal(0, removedCount);
        }

        [Fact]
        public void RemoveSmallFace3Ds_TinyFaceBelowThreshold_IsRemoved()
        {
            // Arrange - one 1 m² face and one ~0.0001 m² sliver (0.01 x 0.01).
            Face3D unitFace = TestGeometry.CreateUnitQuadFace();
            Face3D sliver = CreateSmallQuadFace(0.01);
            List<Face3D> face3Ds = new List<Face3D> { unitFace, sliver };

            // Act
            List<Face3D> result = GeometryQuery.RemoveSmallFace3Ds(face3Ds, 0.01, out int removedCount);

            // Assert
            Assert.Single(result);
            Assert.Equal(1, removedCount);
            Assert.Same(unitFace, result[0]);
        }

        [Fact]
        public void RemoveSmallFace3Ds_FacesAtOrAboveThreshold_AreKept()
        {
            // Arrange - a 0.25 m² face (0.5 x 0.5) is well above a 0.01 m² threshold.
            List<Face3D> face3Ds = new List<Face3D> { CreateSmallQuadFace(0.5) };

            // Act
            List<Face3D> result = GeometryQuery.RemoveSmallFace3Ds(face3Ds, 0.01, out int removedCount);

            // Assert
            Assert.Single(result);
            Assert.Equal(0, removedCount);
        }

        [Fact]
        public void RemoveSmallFace3Ds_SkipsNullEntries()
        {
            // Arrange
            List<Face3D> face3Ds = new List<Face3D> { null, TestGeometry.CreateUnitQuadFace(), null };

            // Act
            List<Face3D> result = GeometryQuery.RemoveSmallFace3Ds(face3Ds, 0.01, out int removedCount);

            // Assert
            Assert.Single(result);
            Assert.Equal(0, removedCount);
        }
    }
}
