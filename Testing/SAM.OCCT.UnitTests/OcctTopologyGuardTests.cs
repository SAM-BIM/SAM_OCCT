// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare static Create/Query classes.
using GeometryCreate = SAM.Geometry.OCCT.Create;
using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Pure-managed guard tests for the persistent topology API (issue #14) -
    /// input validation paths that must not touch the native library.
    /// </summary>
    public class OcctTopologyGuardTests
    {
        [Fact]
        public void Topology_NullShells_ReturnsNullWithInputEmptyDiagnostic()
        {
            // Act
            OcctTopology topology = GeometryCreate.Topology((IEnumerable<Shell>)null, out OcctCellComplexResult result);

            // Assert
            Assert.Null(topology);
            Assert.NotNull(result);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_TOPOLOGY_INPUT_EMPTY");
        }

        [Fact]
        public void Topology_NullFace3Ds_ReturnsNullWithInputEmptyDiagnostic()
        {
            // Act
            OcctTopology topology = GeometryCreate.Topology((IEnumerable<Face3D>)null, out OcctCellComplexResult result);

            // Assert
            Assert.Null(topology);
            Assert.NotNull(result);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_TOPOLOGY_INPUT_EMPTY");
        }

        [Fact]
        public void CellComplexResult_NullTopology_ReturnsFailureWithDisposedDiagnostic()
        {
            // Act
            OcctCellComplexResult result = GeometryQuery.CellComplexResult(null);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_TOPOLOGY_DISPOSED");
        }
    }
}
