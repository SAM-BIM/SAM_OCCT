// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using SAM.Analytical.Grasshopper.OCCT;
using SAM.Core.OCCT;
using System;
using System.Linq;
using Xunit;

namespace SAM.OCCT.GrasshopperTests
{
    /// <summary>
    /// PR #61 GH component contract tests.
    /// Actually instantiates the Grasshopper components and inspects their GUIDs,
    /// inputs, outputs, names, order, access, and defaults.
    /// </summary>
    public class GHComponentContractTests
    {
        // ── Legacy AutoTune3D (original GUID 9de8b4c0) ──

        [SkippableFact]
        public void AutoTune3D_Legacy_ComponentGuid_IsOriginal()
        {
            SkipIfNoGrasshopper();
            var comp = new SAMOCCTAutoTune3DLegacy();
            Assert.Equal(new Guid("9de8b4c0-14f6-4828-b966-aa57cf58143b"), comp.ComponentGuid);
        }

        [SkippableFact]
        public void AutoTune3D_Legacy_Version_Is010()
        {
            SkipIfNoGrasshopper();
            var comp = new SAMOCCTAutoTune3DLegacy();
            Assert.Equal("0.1.0", comp.LatestComponentVersion);
        }

        [SkippableFact]
        public void AutoTune3D_Legacy_DisplayName_IsExpected()
        {
            SkipIfNoGrasshopper();
            var comp = new SAMOCCTAutoTune3DLegacy();
            Assert.Equal("SAMOCCT.AutoTune3D", comp.Name);
            Assert.Equal("SAMOCCT.AutoTune3D", comp.NickName);
        }

        [SkippableFact]
        public void AutoTune3D_Legacy_Inputs_OrderAndNames()
        {
            SkipIfNoGrasshopper();
            var comp = new SAMOCCTAutoTune3DLegacy();
            var inputs = comp.Params.Input;
            Assert.NotNull(inputs);
            Assert.Equal(9, inputs.Count);

            Assert.Equal("_panels", inputs[0].Name);
            Assert.Equal("minBucketSize_", inputs[1].Name);
            Assert.Equal("thicknessFactor_", inputs[2].Name);
            Assert.Equal("alignColinearOffset_", inputs[3].Name);
            Assert.Equal("normalizeCapOffset_", inputs[4].Name);
            Assert.Equal("maxRounds_", inputs[5].Name);
            Assert.Equal("maxExtendLadder_", inputs[6].Name);
            Assert.Equal("escalateBucket_", inputs[7].Name);
            Assert.Equal("_run", inputs[8].Name);
        }

        [SkippableFact]
        public void AutoTune3D_Legacy_InputAccess_IsExpected()
        {
            SkipIfNoGrasshopper();
            var comp = new SAMOCCTAutoTune3DLegacy();
            var inputs = comp.Params.Input;

            Assert.Equal(GH_ParamAccess.list, inputs[0].Access);
            Assert.Equal(GH_ParamAccess.item, inputs[1].Access);
            Assert.Equal(GH_ParamAccess.item, inputs[2].Access);
            Assert.Equal(GH_ParamAccess.item, inputs[3].Access);
            Assert.Equal(GH_ParamAccess.item, inputs[4].Access);
            Assert.Equal(GH_ParamAccess.item, inputs[5].Access);
            Assert.Equal(GH_ParamAccess.list, inputs[6].Access);
            Assert.Equal(GH_ParamAccess.item, inputs[7].Access);
            Assert.Equal(GH_ParamAccess.item, inputs[8].Access);
        }

        [SkippableFact]
        public void AutoTune3D_Legacy_InputDefaults_AreExpected()
        {
            SkipIfNoGrasshopper();
            var comp = new SAMOCCTAutoTune3DLegacy();
            var inputs = comp.Params.Input;

            // minBucketSize_ default 0.4
            double v;
            Assert.True(((Grasshopper.Kernel.Parameters.Param_Number)inputs[1]).GetPersistentData(0, out v));
            Assert.Equal(0.4, v, 5);

            // thicknessFactor_ default 0.6
            Assert.True(((Grasshopper.Kernel.Parameters.Param_Number)inputs[2]).GetPersistentData(0, out v));
            Assert.Equal(0.6, v, 5);

            // alignColinearOffset_ default 0.3
            Assert.True(((Grasshopper.Kernel.Parameters.Param_Number)inputs[3]).GetPersistentData(0, out v));
            Assert.Equal(0.3, v, 5);

            // normalizeCapOffset_ default 0.3
            Assert.True(((Grasshopper.Kernel.Parameters.Param_Number)inputs[4]).GetPersistentData(0, out v));
            Assert.Equal(0.3, v, 5);

            // escalateBucket_ default false
            bool b;
            Assert.True(((Grasshopper.Kernel.Parameters.Param_Boolean)inputs[7]).GetPersistentData(0, out b));
            Assert.False(b);
        }

        [SkippableFact]
        public void AutoTune3D_Legacy_Outputs_OrderAndNames()
        {
            SkipIfNoGrasshopper();
            var comp = new SAMOCCTAutoTune3DLegacy();
            var outputs = comp.Params.Output;
            Assert.NotNull(outputs);
            Assert.Equal(9, outputs.Count);

            Assert.Equal("Panels", outputs[0].Name);
            Assert.Equal("NakedPoints", outputs[1].Name);
            Assert.Equal("Diagnostics", outputs[2].Name);
            Assert.Equal("Successful", outputs[3].Name);
            Assert.Equal("NakedWires", outputs[4].Name);
            Assert.Equal("SourceMap", outputs[5].Name);
            Assert.Equal("ClosureReport", outputs[6].Name);
            Assert.Equal("Rounds", outputs[7].Name);
            Assert.Equal("RoundsAccepted", outputs[8].Name);
        }

        // ── New AutoTune3D Discover (GUID dce4ce6d) ──

        [SkippableFact]
        public void AutoTune3D_Discover_ComponentGuid_IsExpected()
        {
            SkipIfNoGrasshopper();
            var comp = new SAMOCCTAutoTune3DDiscover();
            Assert.Equal(new Guid("dce4ce6d-581a-4225-b792-3ad04f239460"), comp.ComponentGuid);
        }

        [SkippableFact]
        public void AutoTune3D_LegacyAndDiscover_HaveDistinctGuids()
        {
            SkipIfNoGrasshopper();
            var legacy = new SAMOCCTAutoTune3DLegacy();
            var discover = new SAMOCCTAutoTune3DDiscover();
            Assert.NotEqual(legacy.ComponentGuid, discover.ComponentGuid);
        }

        [SkippableFact]
        public void AutoTune3D_Discover_HasDiscoveryInputs()
        {
            SkipIfNoGrasshopper();
            var comp = new SAMOCCTAutoTune3DDiscover();
            var inputNames = comp.Params.Input.Select(i => i.Name).ToList();

            Assert.Contains("_discover", inputNames);
            Assert.Contains("sweepBands_", inputNames);
            Assert.Contains("sweepMargins_", inputNames);
            Assert.Contains("_panels", inputNames);
            Assert.Contains("_run", inputNames);
        }

        // ── CreateAdjacencyCluster ──

        [SkippableFact]
        public void CreateAdjacencyCluster_Guid_IsExpected()
        {
            SkipIfNoGrasshopper();
            var comp = new SAMOCCTCreateAdjacencyCluster();
            Assert.Equal(new Guid("c104f272-9b10-454f-9825-16e2d41adfa6"), comp.ComponentGuid);
        }

        [SkippableFact]
        public void CreateAdjacencyCluster_InputOrder_Keeps_RunAt5()
        {
            SkipIfNoGrasshopper();
            var comp = new SAMOCCTCreateAdjacencyCluster();
            var inputs = comp.Params.Input;
            Assert.NotNull(inputs);
            Assert.True(inputs.Count >= 6, "Expected at least 6 inputs");

            // _run must stay at original position (index 5)
            Assert.Equal("_run", inputs[5].Name);
        }

        [SkippableFact]
        public void CreateAdjacencyCluster_InputOrder_MergeCoplanarAfterRun()
        {
            SkipIfNoGrasshopper();
            var comp = new SAMOCCTCreateAdjacencyCluster();
            var inputs = comp.Params.Input;
            Assert.NotNull(inputs);
            Assert.True(inputs.Count >= 7, "Expected at least 7 inputs");

            // mergeCoplanarBeforeBuild_ appended after _run
            Assert.Equal("_run", inputs[5].Name);
            Assert.Equal("mergeCoplanarBeforeBuild_", inputs[6].Name);
        }

        [SkippableFact]
        public void CreateAdjacencyCluster_MergeCoplanar_DefaultIsFalse()
        {
            SkipIfNoGrasshopper();
            var comp = new SAMOCCTCreateAdjacencyCluster();
            var inputs = comp.Params.Input;
            var mergeInput = inputs.FirstOrDefault(i => i.Name == "mergeCoplanarBeforeBuild_");
            Assert.NotNull(mergeInput);

            bool val = true; // should be overwritten by persistent data
            Assert.True(((Grasshopper.Kernel.Parameters.Param_Boolean)mergeInput).GetPersistentData(0, out val));
            Assert.False(val, "mergeCoplanarBeforeBuild_ must default to false");
        }

        [SkippableFact]
        public void CreateAdjacencyCluster_IsVoluntaryOrVolatile()
        {
            SkipIfNoGrasshopper();
            var comp = new SAMOCCTCreateAdjacencyCluster();
            var inputs = comp.Params.Input;

            Assert.Equal(GH_ParamAccess.list, inputs[0].Access);
            Assert.Equal(GH_ParamAccess.item, inputs[5].Access);
            Assert.Equal(GH_ParamAccess.item, inputs[6].Access);
        }

        private static void SkipIfNoGrasshopper()
        {
            try
            {
                var t = typeof(GH_Component);
            }
            catch (TypeLoadException)
            {
                Skip.If(true, "Grasshopper SDK not available in this test environment.");
            }
        }
    }
}
