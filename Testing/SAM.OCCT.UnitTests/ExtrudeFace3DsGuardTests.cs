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
    /// Validates the ExtrudeFace3Ds guard clauses (issue #30). These reject
    /// empty input and a zero-length direction before any native call, so they
    /// run deterministically on any platform / CI agent without the OCCT library.
    /// </summary>
    public class ExtrudeFace3DsGuardTests
    {
        [Fact]
        public void ExtrudeFace3Ds_NullInput_ReturnsNullWithEmptyDiagnostic()
        {
            // Act
            List<Shell> shells = GeometryQuery.ExtrudeFace3Ds((IEnumerable<Face3D>)null, 3.0, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.Null(shells);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_EXTRUDE_INPUT_EMPTY" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void ExtrudeFace3Ds_AllNullEntries_ReturnsNullWithEmptyDiagnostic()
        {
            // Arrange
            List<Face3D> input = new List<Face3D> { null, null };

            // Act
            List<Shell> shells = GeometryQuery.ExtrudeFace3Ds(input, 3.0, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.Null(shells);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_EXTRUDE_INPUT_EMPTY");
        }

        [Fact]
        public void ExtrudeFace3Ds_ZeroHeight_ReturnsNullWithDirectionDiagnostic()
        {
            // Arrange
            List<Face3D> input = new List<Face3D> { TestGeometry.CreateUnitQuadFace() };

            // Act - a zero height yields a zero-length direction vector.
            List<Shell> shells = GeometryQuery.ExtrudeFace3Ds(input, 0.0, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.Null(shells);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_EXTRUDE_DIRECTION_ZERO");
        }
    }
}
