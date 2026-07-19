// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// PR #61 OcctBuildOptions persistent default tests.
    ///
    /// Component contract tests (GUID, inputs, outputs, order, access, defaults)
    /// that actually instantiate Grasshopper components live in the dedicated
    /// Testing/SAM.OCCT.GrasshopperTests project, which references the Rhino/Grasshopper
    /// SDK assemblies needed to construct GH component instances.
    /// </summary>
    public class GHComponentContractTests
    {
        [Fact]
        public void OcctBuildOptions_MergeCoplanarBeforeBuild_DefaultIsFalse()
        {
            var opts = new OcctBuildOptions();
            Assert.False(opts.MergeCoplanarBeforeBuild);
        }

        [Fact]
        public void OcctBuildOptions_SewBeforeBuild_DefaultIsFalse()
        {
            var opts = new OcctBuildOptions();
            Assert.False(opts.SewBeforeBuild);
        }

        [Fact]
        public void OcctBuildOptions_AvoidInternalShapes_DefaultIsTrue()
        {
            var opts = new OcctBuildOptions();
            Assert.True(opts.AvoidInternalShapes);
        }
    }
}
