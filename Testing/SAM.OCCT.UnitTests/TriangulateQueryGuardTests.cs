// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

// SAM.Geometry.OCCT and other modules each declare a static Create class.
using GeometryCreate = SAM.Geometry.OCCT.Create;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Validates the Create.Triangulate guard clauses. None of these require the
    /// native OCCT DLL, so they run deterministically on any platform / CI agent.
    /// </summary>
    public class TriangulateQueryGuardTests
    {
        [Fact]
        public void Triangulate_NullInput_ReturnsNullWithEmptyDiagnostic()
        {
            // Act
            List<Face3D> face3Ds = GeometryCreate.Triangulate((IEnumerable<Face3D>)null, out OcctCellComplexResult result, 0.1, 0.5, false, new OcctBuildOptions());

            // Assert
            Assert.Null(face3Ds);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_INPUT_EMPTY" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void Triangulate_AllNullEntries_ReturnsNullWithEmptyDiagnostic()
        {
            // Arrange
            List<Face3D> input = new List<Face3D> { null, null };

            // Act
            List<Face3D> face3Ds = GeometryCreate.Triangulate(input, out OcctCellComplexResult result, 0.1, 0.5, false, new OcctBuildOptions());

            // Assert
            Assert.Null(face3Ds);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_INPUT_EMPTY");
        }
    }
}
