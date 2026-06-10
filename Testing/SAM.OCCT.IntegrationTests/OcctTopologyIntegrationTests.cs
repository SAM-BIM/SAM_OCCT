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
    /// End-to-end tests for the persistent OCCT topology handle (issue #14).
    /// Each test auto-skips (via SkippableFact) when the native library is absent.
    /// </summary>
    public class OcctTopologyIntegrationTests
    {
        [SkippableFact]
        public void AbiVersion_NativeAvailable_ReportsVersion2()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - any tiny native round trip populates NativeVersion.
            List<Shell> shells = new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) };

            // Act
            GeometryQuery.ShellsUnion(shells, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert - the committed native build must expose ABI revision 2
            // (sam_occt_abi_version) so the shape-handle entry points exist.
            Assert.True(result.NativeAvailable);
            Assert.Equal("2", result.NativeVersion);
        }
    }
}
