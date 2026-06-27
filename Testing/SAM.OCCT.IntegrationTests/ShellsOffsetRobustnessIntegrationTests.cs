// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Robustness regression (issue #29): a real user model whose geometry made
    /// OCCT raise a hardware fault (access violation) deep inside the offset
    /// kernel. Under the default exception model that fault escaped catch(...)
    /// and tore down the whole host process (Rhino). The native library is now
    /// built with /EHa and installs OCCT signal translation, so the fault must
    /// surface as a clean result - never a process crash. The mere fact that
    /// these tests return (rather than aborting the test host) is the assertion;
    /// a regression would crash the run, not just fail it.
    /// </summary>
    public class ShellsOffsetRobustnessIntegrationTests
    {
        private static List<Shell> LoadCrashModel()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Robustness", "offset-crash-input.sam");
            Assert.True(File.Exists(path), $"Missing robustness fixture: {path}");

            List<IJSAMObject> objects = SAM.Core.Convert.ToSAM(path);
            return objects?.OfType<Shell>().ToList() ?? new List<Shell>();
        }

        [SkippableFact]
        public void ShellsOffset_FaultProneModel_ReturnsCleanlyWithoutCrashingHost()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Shell> shells = LoadCrashModel();
            Assert.NotEmpty(shells);

            // Act - this used to access-violate inside OCCT and kill the process.
            List<Shell> result_Shells = GeometryQuery.ShellsOffset(shells, -0.1, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert - we got back a result object (success or a clean diagnostic);
            // either way the native layer caught the fault instead of crashing.
            Assert.NotNull(result);
            Assert.True(result.NativeAvailable);
        }

        [SkippableFact]
        public void ShellsThicken_FaultProneModel_ReturnsCleanlyWithoutCrashingHost()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            List<Shell> shells = LoadCrashModel();
            Assert.NotEmpty(shells);

            // Act
            List<Shell> result_Shells = GeometryQuery.ShellsThicken(shells, 0.1, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert - no crash; a result object came back.
            Assert.NotNull(result);
            Assert.True(result.NativeAvailable);
        }
    }
}
