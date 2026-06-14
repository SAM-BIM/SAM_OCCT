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
    /// Validates the ShellsDistance guard clauses (issue #28). These reject
    /// empty/null input before any native call, so they run deterministically
    /// on any platform / CI agent without the OCCT library.
    /// </summary>
    public class ShellsDistanceGuardTests
    {
        [Fact]
        public void ShellsDistance_NullFirstSet_ReturnsNullWithInputEmptyDiagnostic()
        {
            // Arrange
            List<Shell> otherShells = new List<Shell> { TestGeometry.CreateSingleFaceShell() };

            // Act
            double? distance = GeometryQuery.ShellsDistance((IEnumerable<Shell>)null, otherShells, out OcctCellComplexResult result, out Point3D pointA, out Point3D pointB, new OcctBuildOptions());

            // Assert
            Assert.Null(distance);
            Assert.Null(pointA);
            Assert.Null(pointB);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_DISTANCE_INPUT_EMPTY" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void ShellsDistance_NullOtherSet_ReturnsNullWithOtherEmptyDiagnostic()
        {
            // Arrange
            List<Shell> shells = new List<Shell> { TestGeometry.CreateSingleFaceShell() };

            // Act
            double? distance = GeometryQuery.ShellsDistance(shells, (IEnumerable<Shell>)null, out OcctCellComplexResult result, out Point3D pointA, out Point3D pointB, new OcctBuildOptions());

            // Assert
            Assert.Null(distance);
            Assert.Null(pointA);
            Assert.Null(pointB);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_DISTANCE_OTHER_EMPTY");
        }

        [Fact]
        public void ShellsDistance_EmptyFirstList_ReturnsNullWithInputEmptyDiagnostic()
        {
            // Arrange
            List<Shell> otherShells = new List<Shell> { TestGeometry.CreateSingleFaceShell() };

            // Act
            double? distance = GeometryQuery.ShellsDistance(new List<Shell>(), otherShells, out OcctCellComplexResult result, out Point3D pointA, out Point3D pointB, new OcctBuildOptions());

            // Assert
            Assert.Null(distance);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_DISTANCE_INPUT_EMPTY");
        }
    }
}
