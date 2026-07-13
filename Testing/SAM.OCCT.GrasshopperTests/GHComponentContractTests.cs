// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using SAM.Analytical.Grasshopper.OCCT;
using SAM.Core.Grasshopper;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SAM.OCCT.GrasshopperTests
{
    /// <summary>
    /// PR #61 GH component contract tests.
    /// Instantiates production components and inspects registered inputs, outputs,
    /// GUIDs, names, access, and persistent defaults via the Grasshopper API.
    /// Uses reflection to access the declared Inputs/Outputs properties, avoiding
    /// the need for a Rhino document context.
    /// </summary>
    public class GHComponentContractTests
    {
        // ── Legacy AutoTune3D (original GUID 9de8b4c0) ──

        [SkippableFact]
        public void AutoTune3D_Legacy_ComponentGuid_IsOriginal()
        {
            SkipIfNoGrasshopper();
            AssertAutoTune3DLegacy_Guid();
        }

        [SkippableFact]
        public void AutoTune3D_Legacy_Version_Is010()
        {
            SkipIfNoGrasshopper();
            AssertAutoTune3DLegacy_Version();
        }

        [SkippableFact]
        public void AutoTune3D_Legacy_DisplayName_IsExpected()
        {
            SkipIfNoGrasshopper();
            AssertAutoTune3DLegacy_DisplayName();
        }

        [SkippableFact]
        public void AutoTune3D_Legacy_Inputs_OrderAndNames()
        {
            SkipIfNoGrasshopper();
            AssertAutoTune3DLegacy_Inputs();
        }

        [SkippableFact]
        public void AutoTune3D_Legacy_InputAccess_IsExpected()
        {
            SkipIfNoGrasshopper();
            AssertAutoTune3DLegacy_Access();
        }

        [SkippableFact]
        public void AutoTune3D_Legacy_InputDefaults_AreExpected()
        {
            SkipIfNoGrasshopper();
            AssertAutoTune3DLegacy_Defaults();
        }

        [SkippableFact]
        public void AutoTune3D_Legacy_Outputs_OrderAndNames()
        {
            SkipIfNoGrasshopper();
            AssertAutoTune3DLegacy_Outputs();
        }

        // ── New AutoTune3D Discover (GUID dce4ce6d) ──

        [SkippableFact]
        public void AutoTune3D_Discover_ComponentGuid_IsExpected()
        {
            SkipIfNoGrasshopper();
            AssertAutoTune3DDiscover_Guid();
        }

        [SkippableFact]
        public void AutoTune3D_LegacyAndDiscover_HaveDistinctGuids()
        {
            SkipIfNoGrasshopper();
            AssertAutoTune3D_DistinctGuids();
        }

        [SkippableFact]
        public void AutoTune3D_Discover_HasDiscoveryInputs()
        {
            SkipIfNoGrasshopper();
            AssertAutoTune3DDiscover_Inputs();
        }

        // ── CreateAdjacencyCluster ──

        [SkippableFact]
        public void CreateAdjacencyCluster_Guid_IsExpected()
        {
            SkipIfNoGrasshopper();
            AssertCreateAdjacencyCluster_Guid();
        }

        [SkippableFact]
        public void CreateAdjacencyCluster_InputOrder_Keeps_RunAt5()
        {
            SkipIfNoGrasshopper();
            AssertCreateAdjacencyCluster_RunAt5();
        }

        [SkippableFact]
        public void CreateAdjacencyCluster_InputOrder_MergeCoplanarAfterRun()
        {
            SkipIfNoGrasshopper();
            AssertCreateAdjacencyCluster_MergeAfterRun();
        }

        [SkippableFact]
        public void CreateAdjacencyCluster_MergeCoplanar_DefaultIsFalse()
        {
            SkipIfNoGrasshopper();
            AssertCreateAdjacencyCluster_MergeDefault();
        }

        [SkippableFact]
        public void CreateAdjacencyCluster_IsVoluntaryOrVolatile()
        {
            SkipIfNoGrasshopper();
            AssertCreateAdjacencyCluster_Voluntary();
        }

        // ── Implementation (only JIT'd after Grasshopper is confirmed available) ──

        private static GH_SAMParam[] GetInputs(GH_Component comp)
        {
            var prop = comp.GetType().GetProperty("Inputs",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (prop == null)
                throw new InvalidOperationException("Inputs property not found on " + comp.GetType().Name);
            return (GH_SAMParam[])prop.GetValue(comp);
        }

        private static GH_SAMParam[] GetOutputs(GH_Component comp)
        {
            var prop = comp.GetType().GetProperty("Outputs",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (prop == null)
                throw new InvalidOperationException("Outputs property not found on " + comp.GetType().Name);
            return (GH_SAMParam[])prop.GetValue(comp);
        }

        private static void AssertAutoTune3DLegacy_Guid()
        {
            var comp = new SAMOCCTAutoTune3DLegacy();
            Assert.Equal(new Guid("9de8b4c0-14f6-4828-b966-aa57cf58143b"), comp.ComponentGuid);
        }

        private static void AssertAutoTune3DLegacy_Version()
        {
            var comp = new SAMOCCTAutoTune3DLegacy();
            Assert.Equal("0.1.0", comp.LatestComponentVersion);
        }

        private static void AssertAutoTune3DLegacy_DisplayName()
        {
            var comp = new SAMOCCTAutoTune3DLegacy();
            Assert.Equal("SAMOCCT.AutoTune3D", comp.Name);
            Assert.Equal("SAMOCCT.AutoTune3D", comp.NickName);
        }

        private static void AssertAutoTune3DLegacy_Inputs()
        {
            var comp = new SAMOCCTAutoTune3DLegacy();
            var inputs = GetInputs(comp);
            Assert.NotNull(inputs);
            Assert.Equal(9, inputs.Length);

            Assert.Equal("_panels", inputs[0].Param.Name);
            Assert.Equal("minBucketSize_", inputs[1].Param.Name);
            Assert.Equal("thicknessFactor_", inputs[2].Param.Name);
            Assert.Equal("alignColinearOffset_", inputs[3].Param.Name);
            Assert.Equal("normalizeCapOffset_", inputs[4].Param.Name);
            Assert.Equal("maxRounds_", inputs[5].Param.Name);
            Assert.Equal("maxExtendLadder_", inputs[6].Param.Name);
            Assert.Equal("escalateBucket_", inputs[7].Param.Name);
            Assert.Equal("_run", inputs[8].Param.Name);
        }

        private static void AssertAutoTune3DLegacy_Access()
        {
            var comp = new SAMOCCTAutoTune3DLegacy();
            var inputs = GetInputs(comp);

            Assert.Equal(GH_ParamAccess.list, inputs[0].Param.Access);
            Assert.Equal(GH_ParamAccess.item, inputs[1].Param.Access);
            Assert.Equal(GH_ParamAccess.item, inputs[2].Param.Access);
            Assert.Equal(GH_ParamAccess.item, inputs[3].Param.Access);
            Assert.Equal(GH_ParamAccess.item, inputs[4].Param.Access);
            Assert.Equal(GH_ParamAccess.item, inputs[5].Param.Access);
            Assert.Equal(GH_ParamAccess.list, inputs[6].Param.Access);
            Assert.Equal(GH_ParamAccess.item, inputs[7].Param.Access);
            Assert.Equal(GH_ParamAccess.item, inputs[8].Param.Access);
        }

        private static void AssertAutoTune3DLegacy_Defaults()
        {
            var comp = new SAMOCCTAutoTune3DLegacy();
            var inputs = GetInputs(comp);

            double v;
            bool b;
            var path = new GH_Path(0);

            v = ((GH_Number)((Grasshopper.Kernel.Parameters.Param_Number)inputs[1].Param).PersistentData.get_DataItem(path, 0)).Value;
            Assert.Equal(0.4, v, 5);

            v = ((GH_Number)((Grasshopper.Kernel.Parameters.Param_Number)inputs[2].Param).PersistentData.get_DataItem(path, 0)).Value;
            Assert.Equal(0.6, v, 5);

            v = ((GH_Number)((Grasshopper.Kernel.Parameters.Param_Number)inputs[3].Param).PersistentData.get_DataItem(path, 0)).Value;
            Assert.Equal(0.3, v, 5);

            v = ((GH_Number)((Grasshopper.Kernel.Parameters.Param_Number)inputs[4].Param).PersistentData.get_DataItem(path, 0)).Value;
            Assert.Equal(0.3, v, 5);

            b = ((GH_Boolean)((Grasshopper.Kernel.Parameters.Param_Boolean)inputs[7].Param).PersistentData.get_DataItem(path, 0)).Value;
            Assert.False(b);
        }

        private static void AssertAutoTune3DLegacy_Outputs()
        {
            var comp = new SAMOCCTAutoTune3DLegacy();
            var outputs = GetOutputs(comp);
            Assert.NotNull(outputs);
            Assert.Equal(9, outputs.Length);

            Assert.Equal("Panels", outputs[0].Param.Name);
            Assert.Equal("NakedPoints", outputs[1].Param.Name);
            Assert.Equal("Diagnostics", outputs[2].Param.Name);
            Assert.Equal("Successful", outputs[3].Param.Name);
            Assert.Equal("NakedWires", outputs[4].Param.Name);
            Assert.Equal("SourceMap", outputs[5].Param.Name);
            Assert.Equal("ClosureReport", outputs[6].Param.Name);
            Assert.Equal("Rounds", outputs[7].Param.Name);
            Assert.Equal("RoundsAccepted", outputs[8].Param.Name);
        }

        private static void AssertAutoTune3DDiscover_Guid()
        {
            var comp = new SAMOCCTAutoTune3DDiscover();
            Assert.Equal(new Guid("dce4ce6d-581a-4225-b792-3ad04f239460"), comp.ComponentGuid);
        }

        private static void AssertAutoTune3D_DistinctGuids()
        {
            var legacy = new SAMOCCTAutoTune3DLegacy();
            var discover = new SAMOCCTAutoTune3DDiscover();
            Assert.NotEqual(legacy.ComponentGuid, discover.ComponentGuid);
        }

        private static void AssertAutoTune3DDiscover_Inputs()
        {
            var comp = new SAMOCCTAutoTune3DDiscover();
            var inputNames = GetInputs(comp).Select(i => i.Param.Name).ToList();

            Assert.Contains("_discover", inputNames);
            Assert.Contains("sweepBands_", inputNames);
            Assert.Contains("sweepMargins_", inputNames);
            Assert.Contains("_panels", inputNames);
            Assert.Contains("_run", inputNames);
        }

        private static void AssertCreateAdjacencyCluster_Guid()
        {
            var comp = new SAMOCCTCreateAdjacencyCluster();
            Assert.Equal(new Guid("c104f272-9b10-454f-9825-16e2d41adfa6"), comp.ComponentGuid);
        }

        private static void AssertCreateAdjacencyCluster_RunAt5()
        {
            var comp = new SAMOCCTCreateAdjacencyCluster();
            var inputs = GetInputs(comp);
            Assert.NotNull(inputs);
            Assert.True(inputs.Length >= 6, "Expected at least 6 inputs");

            Assert.Equal("_run", inputs[5].Param.Name);
        }

        private static void AssertCreateAdjacencyCluster_MergeAfterRun()
        {
            var comp = new SAMOCCTCreateAdjacencyCluster();
            var inputs = GetInputs(comp);
            Assert.NotNull(inputs);
            Assert.True(inputs.Length >= 7, "Expected at least 7 inputs");

            Assert.Equal("_run", inputs[5].Param.Name);
            Assert.Equal("mergeCoplanarBeforeBuild_", inputs[6].Param.Name);
        }

        private static void AssertCreateAdjacencyCluster_MergeDefault()
        {
            var comp = new SAMOCCTCreateAdjacencyCluster();
            var inputs = GetInputs(comp);
            var mergeInput = inputs.FirstOrDefault(i => i.Param.Name == "mergeCoplanarBeforeBuild_");
            Assert.NotNull(mergeInput);

            bool val = ((GH_Boolean)((Grasshopper.Kernel.Parameters.Param_Boolean)mergeInput.Param).PersistentData.get_DataItem(new GH_Path(0), 0)).Value;
            Assert.False(val, "mergeCoplanarBeforeBuild_ must default to false");
        }

        private static void AssertCreateAdjacencyCluster_Voluntary()
        {
            var comp = new SAMOCCTCreateAdjacencyCluster();
            var inputs = GetInputs(comp);

            Assert.Equal(GH_ParamAccess.list, inputs[0].Param.Access);
            Assert.Equal(GH_ParamAccess.item, inputs[5].Param.Access);
            Assert.Equal(GH_ParamAccess.item, inputs[6].Param.Access);
        }

        private static void SkipIfNoGrasshopper()
        {
            try
            {
                var t = typeof(GH_Component);
            }
            catch
            {
                Skip.If(true, "Grasshopper SDK not available in this test environment.");
            }
        }
    }
}

