// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT;
using System;
using System.Collections.Generic;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Validates the managed guard clauses of <see cref="Modify.MergeSmallSpaces"/>.
    /// These cover the null and empty inputs and require neither the native OCCT
    /// library nor real geometry, so they run deterministically on any CI agent.
    /// </summary>
    public class MergeSmallSpacesGuardTests
    {
        [Fact]
        public void MergeSmallSpaces_NullCluster_ReturnsNullWithDiagnostic()
        {
            // Act
            AdjacencyCluster result = ((AdjacencyCluster)null).MergeSmallSpaces(
                out List<Space> mergedSpaces,
                out List<Space> unmergedSmallSpaces,
                out List<string> report);

            // Assert
            Assert.Null(result);
            Assert.Empty(mergedSpaces);
            Assert.Empty(unmergedSmallSpaces);
            Assert.Contains(report, x => x.StartsWith("SAM_OCCT_MERGE_INPUT_NULL", StringComparison.Ordinal));
        }

        [Fact]
        public void MergeSmallSpaces_EmptyCluster_ReturnsClusterWithDiagnostic()
        {
            // Arrange
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();

            // Act
            AdjacencyCluster result = adjacencyCluster.MergeSmallSpaces(
                out List<Space> mergedSpaces,
                out List<Space> unmergedSmallSpaces,
                out List<string> report);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(mergedSpaces);
            Assert.Empty(unmergedSmallSpaces);
            Assert.Contains(report, x => x.StartsWith("SAM_OCCT_MERGE_INPUT_EMPTY", StringComparison.Ordinal));
        }
    }
}
