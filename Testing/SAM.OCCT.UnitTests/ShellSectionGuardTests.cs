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
    /// Validates the ShellSectionByPlanes guard clauses, which return before any
    /// native call - so these run deterministically on any platform / CI agent.
    /// </summary>
    public class ShellSectionGuardTests
    {
        [Fact]
        public void ShellSectionByPlanes_NullShell_ReturnsNullWithEmptyDiagnostic()
        {
            // Arrange
            List<Plane> planes = new List<Plane> { new Plane(new Point3D(0, 0, 0.5), new Vector3D(0, 0, 1)) };

            // Act
            List<Shell> shells = GeometryQuery.ShellSectionByPlanes(null, planes, out List<Face3D> sectionFace3Ds, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.Null(shells);
            Assert.Null(sectionFace3Ds);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SECTION_INPUT_EMPTY" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void ShellSectionByPlanes_NullPlanes_ReturnsNullWithEmptyDiagnostic()
        {
            // Arrange
            Shell shell = TestGeometry.CreateSingleFaceShell();

            // Act
            List<Shell> shells = GeometryQuery.ShellSectionByPlanes(shell, null, out List<Face3D> sectionFace3Ds, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.Null(shells);
            Assert.Null(sectionFace3Ds);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SECTION_INPUT_EMPTY" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void ShellSectionByPlanes_AllNullPlaneEntries_ReturnsNullWithEmptyDiagnostic()
        {
            // Arrange
            Shell shell = TestGeometry.CreateSingleFaceShell();
            List<Plane> planes = new List<Plane> { null, null };

            // Act
            List<Shell> shells = GeometryQuery.ShellSectionByPlanes(shell, planes, out List<Face3D> sectionFace3Ds, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.Null(shells);
            Assert.Null(sectionFace3Ds);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SECTION_INPUT_EMPTY" && x.Severity == OcctDiagnosticSeverity.Error);
        }
    }
}
