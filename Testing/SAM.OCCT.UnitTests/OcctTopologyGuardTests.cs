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

        [Fact]
        public void TopologyUnion_NullTopology_ReturnsNullWithDisposedDiagnostic()
        {
            // Act
            OcctTopology output = GeometryQuery.TopologyUnion(null, out OcctCellComplexResult result);

            // Assert
            Assert.Null(output);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_TOPOLOGY_DISPOSED");
        }

        [Fact]
        public void TopologyDifference_NullTarget_ReturnsNullWithDisposedDiagnostic()
        {
            // Act
            OcctTopology output = GeometryQuery.TopologyDifference(null, null, out OcctCellComplexResult result);

            // Assert
            Assert.Null(output);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_TOPOLOGY_DISPOSED");
        }

        [Fact]
        public void TopologyIntersection_NullTarget_ReturnsNullWithDisposedDiagnostic()
        {
            // Act
            OcctTopology output = GeometryQuery.TopologyIntersection(null, null, out OcctCellComplexResult result);

            // Assert
            Assert.Null(output);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_TOPOLOGY_DISPOSED");
        }

        [Fact]
        public void TopologyRepair_NullTopology_ReturnsNullWithDisposedDiagnostic()
        {
            // Act
            OcctTopology output = GeometryQuery.TopologyRepair(null, out OcctCellComplexResult result);

            // Assert
            Assert.Null(output);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_TOPOLOGY_DISPOSED");
        }

        [Fact]
        public void IsPointInside_NullTopology_ReturnsNull()
        {
            // Act
            bool? inside = GeometryQuery.IsPointInside(null, new SAM.Geometry.Spatial.Point3D(0, 0, 0));

            // Assert
            Assert.Null(inside);
        }

        [Fact]
        public void OcctBuildOptions_CopyConstructor_CopiesRetainTopology()
        {
            // Arrange
            SAM.Core.OCCT.OcctBuildOptions options = new SAM.Core.OCCT.OcctBuildOptions { RetainTopology = true };

            // Act
            SAM.Core.OCCT.OcctBuildOptions copy = new SAM.Core.OCCT.OcctBuildOptions(options);

            // Assert
            Assert.True(copy.RetainTopology);
        }

        [Fact]
        public void Result_Dispose_NoTopology_DoesNotThrow()
        {
            // Arrange
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act + Assert - legacy results without a retained topology are
            // safely disposable (and double-disposable).
            result.Dispose();
            result.Dispose();
            Assert.Null(result.Topology);
        }
    }
}
