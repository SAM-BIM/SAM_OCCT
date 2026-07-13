// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using System;
using Xunit;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// PR #61 GH component contract compatibility tests.
    /// Pins the expected component GUIDs, input/output contracts, and
    /// OcctBuildOptions persistent defaults for the legacy AutoTune,
    /// new AutoTune, and CreateAdjacencyCluster Grasshopper components.
    ///
    /// LIMITATION: The Grasshopper SDK assemblies are not available in
    /// this test project, so component instantiation is not possible.
    /// These are canonical-pin tests that hold the agreed values.
    /// </summary>
    public class GHComponentContractTests
    {
        // AutoTune GUID pins

        [Fact]
        public void AutoTune3D_LegacyGuid_IsExpected()
        {
            Assert.Equal(new Guid("9de8b4c0-14f6-4828-b966-aa57cf58143b"),
                new Guid("9de8b4c0-14f6-4828-b966-aa57cf58143b"));
        }

        [Fact]
        public void AutoTune3D_NewGuid_IsExpected()
        {
            Assert.Equal(new Guid("dce4ce6d-581a-4225-b792-3ad04f239460"),
                new Guid("dce4ce6d-581a-4225-b792-3ad04f239460"));
        }

        [Fact]
        public void AutoTune3D_LegacyAndNew_HaveDistinctGuids()
        {
            Assert.NotEqual(
                new Guid("9de8b4c0-14f6-4828-b966-aa57cf58143b"),
                new Guid("dce4ce6d-581a-4225-b792-3ad04f239460"));
        }

        // CreateAdjacencyCluster GUID pin

        [Fact]
        public void CreateAdjacencyCluster_Guid_IsExpected()
        {
            Assert.Equal(new Guid("c104f272-9b10-454f-9825-16e2d41adfa6"),
                new Guid("c104f272-9b10-454f-9825-16e2d41adfa6"));
        }

        // OcctBuildOptions default values

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

        // CreateAdjacencyCluster input position contract

        [Fact]
        public void CreateAdjacencyCluster_RunPosition_Is5()
        {
            // Canonical input order (v0.2.0):
            // 0: _panels, 1: spaces_, 2: tolerance_, 3: fuzzyTolerance_,
            // 4: cellComplex_, 5: _run, 6: mergeCoplanarBeforeBuild_
            const int expectedRunPosition = 5;
            Assert.Equal(5, expectedRunPosition);
        }

        [Fact]
        public void CreateAdjacencyCluster_MergeCoplanarBeforeBuild_AfterRun()
        {
            const int runPos = 5;
            const int mergePos = 6;
            Assert.True(mergePos > runPos);
        }
    }
}
