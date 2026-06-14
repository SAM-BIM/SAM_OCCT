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
    /// Validates the ShellsImprint guard clauses (issue #27). These reject
    /// empty/null input before any native call, so they run deterministically
    /// on any platform / CI agent without the OCCT library.
    /// </summary>
    public class ShellsImprintGuardTests
    {
        [Fact]
        public void ShellsImprint_NullInput_ReturnsNullWithEmptyDiagnostic()
        {
            // Act
            List<Shell> shells = GeometryQuery.ShellsImprint((IEnumerable<Shell>)null, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.Null(shells);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_IMPRINT_INPUT_EMPTY" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void ShellsImprint_AllNullEntries_ReturnsNullWithEmptyDiagnostic()
        {
            // Arrange
            List<Shell> input = new List<Shell> { null, null };

            // Act
            List<Shell> shells = GeometryQuery.ShellsImprint(input, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.Null(shells);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_IMPRINT_INPUT_EMPTY");
        }

        [Fact]
        public void ShellsImprint_EmptyList_ReturnsNullWithEmptyDiagnostic()
        {
            // Act
            List<Shell> shells = GeometryQuery.ShellsImprint(new List<Shell>(), out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.Null(shells);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_IMPRINT_INPUT_EMPTY");
        }
    }
}
