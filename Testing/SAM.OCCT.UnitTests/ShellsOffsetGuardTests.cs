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
    /// Validates the ShellsOffset / ShellsThicken guard clauses (issue #29).
    /// These reject empty input and a zero distance before any native call, so
    /// they run deterministically on any platform / CI agent without OCCT.
    /// </summary>
    public class ShellsOffsetGuardTests
    {
        [Fact]
        public void ShellsOffset_NullInput_ReturnsNullWithEmptyDiagnostic()
        {
            List<Shell> shells = GeometryQuery.ShellsOffset((IEnumerable<Shell>)null, 0.1, out OcctCellComplexResult result, new OcctBuildOptions());

            Assert.Null(shells);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_OFFSET_INPUT_EMPTY" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void ShellsOffset_ZeroDistance_ReturnsNullWithZeroDiagnostic()
        {
            List<Shell> input = new List<Shell> { TestGeometry.CreateSingleFaceShell() };

            List<Shell> shells = GeometryQuery.ShellsOffset(input, 0.0, out OcctCellComplexResult result, new OcctBuildOptions());

            Assert.Null(shells);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_OFFSET_ZERO");
        }

        [Fact]
        public void ShellsThicken_NullInput_ReturnsNullWithEmptyDiagnostic()
        {
            List<Shell> shells = GeometryQuery.ShellsThicken((IEnumerable<Shell>)null, 0.1, out OcctCellComplexResult result, new OcctBuildOptions());

            Assert.Null(shells);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_THICKEN_INPUT_EMPTY");
        }

        [Fact]
        public void ShellsThicken_ZeroThickness_ReturnsNullWithZeroDiagnostic()
        {
            List<Shell> input = new List<Shell> { TestGeometry.CreateSingleFaceShell() };

            List<Shell> shells = GeometryQuery.ShellsThicken(input, 0.0, out OcctCellComplexResult result, new OcctBuildOptions());

            Assert.Null(shells);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_THICKEN_ZERO");
        }
    }
}
