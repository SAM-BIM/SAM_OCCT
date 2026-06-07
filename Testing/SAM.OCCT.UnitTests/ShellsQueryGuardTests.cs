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
    /// Validates the public Query guard clauses and the graceful degradation path
    /// when the native OCCT library is not present. None of these require the
    /// native DLL, so they run deterministically on any platform / CI agent.
    /// </summary>
    public class ShellsQueryGuardTests
    {
        [Fact]
        public void ShellsUnion_NullInput_ReturnsNullWithEmptyDiagnostic()
        {
            // Act
            List<Shell> shells = GeometryQuery.ShellsUnion((IEnumerable<Shell>)null, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.Null(shells);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_UNION_INPUT_EMPTY" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void ShellsUnion_AllNullEntries_ReturnsNullWithEmptyDiagnostic()
        {
            // Arrange
            List<Shell> input = new List<Shell> { null, null };

            // Act
            List<Shell> shells = GeometryQuery.ShellsUnion(input, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.Null(shells);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_UNION_INPUT_EMPTY");
        }
    }
}
