// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.OCCT.Solver;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>The Grasshopper-facing default intentionally differs from the core/API default (0). The
    /// fallback is version-gated: a component saved before the voluntary input existed keeps 0, so an
    /// existing saved GH document never changes behavior on plugin update (the saved-component migration
    /// guarantee); only components placed at or after the introducing version get 0.21.</summary>
    public class SolverComponentDefaultsTests
    {
        [Fact]
        public void BucketBetweenLevels_GrasshopperDefault_Is021()
        {
            Assert.Equal(0.21, SolverComponentDefaults.BucketBetweenLevels, 6);
        }

        [Theory]
        [InlineData("0.5.0", "0.5.0", 0.21)] // fresh placement at the introducing version
        [InlineData("0.6.0", "0.5.0", 0.21)] // any later placement
        [InlineData("0.7.0", "0.7.0", 0.21)] // Extend3D introducing version
        [InlineData("0.8.0", "0.7.0", 0.21)] // Extend3D saved between introduction (0.7.0) and a later component bump keeps 0.21 - the gate argument is the INTRODUCING version, not the current one
        [InlineData("0.5.1", "0.5.0", 0.21)] // Clean3D saved after introduction but before a later bump keeps 0.21
        [InlineData("0.4.0", "0.5.0", 0.0)]  // Clean3D saved before P2 - document behavior frozen
        [InlineData("0.6.0", "0.7.0", 0.0)]  // Extend3D saved before P2
        [InlineData("0.5.0", "0.6.0", 0.0)]  // Solve3D saved before P2
        public void BucketBetweenLevelsFallback_VersionGate_OldSavedComponentsKeepZero(string componentVersion, string introducedIn, double expected)
        {
            Assert.Equal(expected, SolverComponentDefaults.BucketBetweenLevelsFallback(componentVersion, introducedIn), 6);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-version")]
        public void BucketBetweenLevelsFallback_UnknownVersion_NeverChangesASavedDocument(string componentVersion)
        {
            Assert.Equal(0.0, SolverComponentDefaults.BucketBetweenLevelsFallback(componentVersion, "0.5.0"), 6);
        }
    }
}
