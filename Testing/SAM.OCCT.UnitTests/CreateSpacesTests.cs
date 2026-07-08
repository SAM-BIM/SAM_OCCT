// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.OCCT.Solver;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Truth table for <see cref="Create.ShouldRefuseSpaces"/> - the Phase 7c closure-gate decision rule
    /// (docs/P6_ARCHITECTURE_REVIEW.md §P sub-step 7c). Pure and native-free: no OCCT DLL required.
    /// </summary>
    public class CreateSpacesTests
    {
        [Fact]
        public void ShouldRefuseSpaces_ZeroNakedEdges_ReturnsFalse()
        {
            Assert.False(Create.ShouldRefuseSpaces(0));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(29)]
        public void ShouldRefuseSpaces_NakedEdgesPresent_ReturnsTrue(int nakedEdgeCount)
        {
            Assert.True(Create.ShouldRefuseSpaces(nakedEdgeCount));
        }
    }
}
