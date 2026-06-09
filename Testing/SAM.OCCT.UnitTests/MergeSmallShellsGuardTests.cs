// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using Xunit;

// Both SAM.Geometry.OCCT and SAM.Geometry.Spatial declare a static Modify class.
using GeometryModify = SAM.Geometry.OCCT.Modify;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Validates the managed guard clauses of <see cref="Modify.MergeSmallShells"/>.
    /// The empty-input paths return before any native OCCT union call, so they run
    /// deterministically on any platform / CI agent.
    /// </summary>
    public class MergeSmallShellsGuardTests
    {
        [Fact]
        public void MergeSmallShells_NullInput_ReturnsNullWithDiagnostic()
        {
            // Act
            List<Shell> result = GeometryModify.MergeSmallShells(
                (IEnumerable<Shell>)null,
                out List<Shell> mergedSmallShells,
                out List<Shell> unmergedSmallShells,
                out List<string> report);

            // Assert
            Assert.Null(result);
            Assert.Empty(mergedSmallShells);
            Assert.Empty(unmergedSmallShells);
            Assert.Contains(report, x => x.StartsWith("SAM_OCCT_MERGE_SHELLS_INPUT_EMPTY", StringComparison.Ordinal));
        }

        [Fact]
        public void MergeSmallShells_AllNullEntries_ReturnsNullWithDiagnostic()
        {
            // Arrange
            List<Shell> input = new List<Shell> { null, null };

            // Act
            List<Shell> result = GeometryModify.MergeSmallShells(
                input,
                out List<Shell> mergedSmallShells,
                out List<Shell> unmergedSmallShells,
                out List<string> report);

            // Assert
            Assert.Null(result);
            Assert.Empty(mergedSmallShells);
            Assert.Empty(unmergedSmallShells);
            Assert.Contains(report, x => x.StartsWith("SAM_OCCT_MERGE_SHELLS_INPUT_EMPTY", StringComparison.Ordinal));
        }
    }
}
