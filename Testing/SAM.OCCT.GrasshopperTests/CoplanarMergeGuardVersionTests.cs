// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical.Grasshopper.OCCT;
using System;
using Xunit;

namespace SAM.OCCT.GrasshopperTests
{
    /// <summary>
    /// The Clean3D coplanar-merge coverage guard (SnapStage.Clean step 3a) changes the computed panels of every component
    /// that runs the managed clean stage: Clean3D, Extend3D (unless inputAlreadyClean), Solve3D and both AutoTune3D variants.
    /// Behavioural change, so each takes a minor bump (GH_COMPONENT_STANDARDS GH-F2/H2); GUIDs and parameters are unchanged.
    /// Components are named by string: a Grasshopper type in a test attribute would load Grasshopper during xUnit discovery,
    /// before the test assembly's resolver is installed.
    /// </summary>
    public class CoplanarMergeGuardVersionTests
    {
        private static (Guid Guid, string Version) Identity(string component)
        {
            switch (component)
            {
                case "Clean3D":
                    SAMOCCTClean3D clean3D = new SAMOCCTClean3D();
                    return (clean3D.ComponentGuid, clean3D.LatestComponentVersion);
                case "Extend3D":
                    SAMOCCTExtend3D extend3D = new SAMOCCTExtend3D();
                    return (extend3D.ComponentGuid, extend3D.LatestComponentVersion);
                case "Solve3D":
                    SAMOCCTSolve3D solve3D = new SAMOCCTSolve3D();
                    return (solve3D.ComponentGuid, solve3D.LatestComponentVersion);
                case "AutoTune3DDiscover":
                    SAMOCCTAutoTune3DDiscover discover = new SAMOCCTAutoTune3DDiscover();
                    return (discover.ComponentGuid, discover.LatestComponentVersion);
                case "AutoTune3DLegacy":
                    SAMOCCTAutoTune3DLegacy legacy = new SAMOCCTAutoTune3DLegacy();
                    return (legacy.ComponentGuid, legacy.LatestComponentVersion);
                default:
                    throw new ArgumentOutOfRangeException(nameof(component), component, null);
            }
        }

        [Theory]
        [InlineData("Clean3D", "8f2c1a47-6b39-4d2e-9a51-7c0e4b8d3f12", "0.7.0")]
        [InlineData("Extend3D", "a7e4c92f-1b53-4d8a-9f26-3c70e1b8d4a5", "0.11.0")]
        [InlineData("Solve3D", "3d6b9e02-4a17-4c8d-b5e3-1f9a2c7d4e8b", "0.8.0")]
        [InlineData("AutoTune3DDiscover", "dce4ce6d-581a-4225-b792-3ad04f239460", "0.5.0")]
        [InlineData("AutoTune3DLegacy", "9de8b4c0-14f6-4828-b966-aa57cf58143b", "0.2.0")]
        public void LatestComponentVersion_ComponentsRunningTheCleanStage_AreMinorBumpedWithGuidUnchanged(string component, string guid, string version)
        {
            // Arrange / Act
            (Guid Guid, string Version) identity = Identity(component);

            // Assert
            Assert.Equal(new Guid(guid), identity.Guid);
            Assert.Equal(version, identity.Version);
        }
    }
}
