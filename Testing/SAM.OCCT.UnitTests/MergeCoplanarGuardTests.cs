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
    /// Guard-clause coverage for the coplanar-merge Query. None of these require the
    /// native OCCT DLL, so they run deterministically on any platform / CI agent.
    /// </summary>
    public class MergeCoplanarGuardTests
    {
        [Fact]
        public void MergeCoplanarFace3Ds_NullInput_ReturnsNullWithEmptyDiagnostic()
        {
            // Act
            List<Face3D> face3Ds = GeometryQuery.MergeCoplanarFace3Ds((IEnumerable<Face3D>)null, out OcctCellComplexResult result);

            // Assert
            Assert.Null(face3Ds);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_INPUT_EMPTY" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void MergeCoplanarFace3Ds_AllNullEntries_ReturnsNullWithEmptyDiagnostic()
        {
            // Arrange
            List<Face3D> input = new List<Face3D> { null, null };

            // Act
            List<Face3D> face3Ds = GeometryQuery.MergeCoplanarFace3Ds(input, out OcctCellComplexResult result);

            // Assert
            Assert.Null(face3Ds);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_INPUT_EMPTY");
        }
    }
}
