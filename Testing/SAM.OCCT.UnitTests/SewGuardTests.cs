// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Native;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare a static Query class.
using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Validates the native sew-and-heal guard clauses and status mapping (issue
    /// #37). The guards reject empty input before any native call and the status
    /// describer is pure managed, so they run deterministically on any platform /
    /// CI agent without OCCT.
    /// </summary>
    public class SewGuardTests
    {
        [Fact]
        public void Sew_NullFaceInput_ReturnsNullWithEmptyDiagnostic()
        {
            List<Shell> shells = GeometryQuery.Sew((IEnumerable<Face3D>)null, out OcctCellComplexResult result, new OcctBuildOptions());

            Assert.Null(shells);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_INPUT_EMPTY" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void Sew_EmptyFaceInput_ReturnsNullWithEmptyDiagnostic()
        {
            List<Shell> shells = GeometryQuery.Sew(new List<Face3D>(), out OcctCellComplexResult result, new OcctBuildOptions());

            Assert.Null(shells);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_INPUT_EMPTY");
        }

        [Fact]
        public void Sew_NullShellInput_ReturnsNullWithEmptyDiagnostic()
        {
            List<Shell> shells = GeometryQuery.Sew((IEnumerable<Shell>)null, out OcctCellComplexResult result, new OcctBuildOptions());

            Assert.Null(shells);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_INPUT_EMPTY" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Theory]
        [InlineData(10, "null argument")]
        [InlineData(20, "no OCCT faces")]
        [InlineData(30, "sewing/healing failed")]
        [InlineData(40, "no closed shell")]
        [InlineData(50, "shape handle was invalid")]
        [InlineData(99, "unexpected native exception")]
        public void DescribeSewStatus_KnownStatus_ReturnsExplanation(int status, string fragment)
        {
            string message = OcctOpenShellAnalysis.DescribeSewStatus(status);

            Assert.Contains(fragment, message);
        }

        [Fact]
        public void DescribeSewStatus_UnknownStatus_ReturnsFallback()
        {
            Assert.Equal("unrecognised native status", OcctOpenShellAnalysis.DescribeSewStatus(123));
        }
    }
}
