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
    /// Validates the managed guard clauses of <see cref="Modify.MergeCoplanarPanels"/>.
    /// These cover null and empty inputs and require neither the native OCCT library
    /// nor real geometry, so they run deterministically on any CI agent.
    /// </summary>
    public class MergeCoplanarPanelsGuardTests
    {
        [Fact]
        public void MergeCoplanarPanels_NullPanels_ReturnsNullWithDiagnostic()
        {
            // Act
            List<Panel> result = ((IEnumerable<Panel>)null).MergeCoplanarPanels(out List<string> diagnostics);

            // Assert
            Assert.Null(result);
            Assert.Contains(diagnostics, x => x.StartsWith("SAM_OCCT_MERGE_PANELS_INPUT_EMPTY", StringComparison.Ordinal));
        }

        [Fact]
        public void MergeCoplanarPanels_AllNullEntries_ReturnsNullWithDiagnostic()
        {
            // Arrange
            List<Panel> panels = new List<Panel> { null, null };

            // Act
            List<Panel> result = panels.MergeCoplanarPanels(out List<string> diagnostics);

            // Assert
            Assert.Null(result);
            Assert.Contains(diagnostics, x => x.StartsWith("SAM_OCCT_MERGE_PANELS_INPUT_EMPTY", StringComparison.Ordinal));
        }

        [Fact]
        public void MergeCoplanarPanels_NullCluster_ReturnsNullWithDiagnostic()
        {
            // Act
            AdjacencyCluster result = ((AdjacencyCluster)null).MergeCoplanarPanels(out List<string> diagnostics);

            // Assert
            Assert.Null(result);
            Assert.Contains(diagnostics, x => x.StartsWith("SAM_OCCT_MERGE_PANELS_INPUT_NULL", StringComparison.Ordinal));
        }

        [Fact]
        public void MergeCoplanarPanels_EmptyCluster_ReturnsClusterWithDiagnostic()
        {
            // Arrange
            AdjacencyCluster adjacencyCluster = new AdjacencyCluster();

            // Act
            AdjacencyCluster result = adjacencyCluster.MergeCoplanarPanels(out List<string> diagnostics);

            // Assert
            Assert.NotNull(result);
            Assert.Contains(diagnostics, x => x.StartsWith("SAM_OCCT_MERGE_PANELS_INPUT_EMPTY", StringComparison.Ordinal));
        }
    }
}
