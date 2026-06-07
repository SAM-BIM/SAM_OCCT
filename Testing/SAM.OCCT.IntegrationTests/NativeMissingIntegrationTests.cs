// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare a static Query class.
using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Validates the graceful-degradation contract when the native OCCT library is
    /// not loadable. This is environment-dependent, so it is gated to run only when
    /// the native library is absent (the inverse of the boolean-op tests) and skips
    /// when it is present - keeping the assertion off the host DLL search state.
    /// </summary>
    public class NativeMissingIntegrationTests
    {
        [SkippableFact]
        public void ShellsUnion_NativeUnavailable_ReturnsNullAndReportsNativeMissing()
        {
            Skip.If(NativeProbe.Available, "Native SAM.Occt.Native library is available; missing-native path does not apply.");

            // Arrange - geometry is serializable, so control reaches the native call.
            List<Shell> input = new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) };

            // Act
            List<Shell> shells = GeometryQuery.ShellsUnion(input, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.Null(shells);
            Assert.False(result.NativeAvailable);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_NATIVE_MISSING" && x.Severity == OcctDiagnosticSeverity.Error);
        }
    }
}
