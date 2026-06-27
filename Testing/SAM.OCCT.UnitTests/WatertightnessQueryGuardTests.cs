// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Guards the public Query.Watertightness wrapper used by the mesh-input pre-check
    /// in SAMOCCT.CreateAdjacencyClusterByShells. It bridges the internal welded-edge
    /// analysis into a public, native-free API so the component can report an open mesh
    /// (naked edges) instead of surfacing an opaque OCCT build failure.
    /// </summary>
    public class WatertightnessQueryGuardTests
    {
        [Fact]
        public void Watertightness_ClosedBoxFaces_ReportsNoNakedEdges()
        {
            // Arrange - a closed cube: every edge shared by exactly two faces.
            List<Face3D> face3Ds = TestGeometry.CreateClosedBoxFaces();

            // Act
            bool analysed = face3Ds.Watertightness(new OcctBuildOptions(), out int edgeCount, out int nakedEdgeCount, out int nonManifoldEdgeCount);

            // Assert
            Assert.True(analysed);
            Assert.True(edgeCount > 0);
            Assert.Equal(0, nakedEdgeCount);
            Assert.Equal(0, nonManifoldEdgeCount);
        }

        [Fact]
        public void Watertightness_OpenBoxFaces_ReportsNakedEdges()
        {
            // Arrange - a cube missing its top face leaves four naked rim edges.
            List<Face3D> face3Ds = TestGeometry.CreateOpenBoxFaces();

            // Act
            bool analysed = face3Ds.Watertightness(new OcctBuildOptions(), out int edgeCount, out int nakedEdgeCount, out _);

            // Assert
            Assert.True(analysed);
            Assert.True(edgeCount > 0);
            Assert.Equal(4, nakedEdgeCount);
        }

        [Fact]
        public void Watertightness_NullFaces_ReturnsFalse()
        {
            bool analysed = global::SAM.Geometry.OCCT.Query.Watertightness(null, new OcctBuildOptions(), out int edgeCount, out int nakedEdgeCount, out int nonManifoldEdgeCount);

            Assert.False(analysed);
            Assert.Equal(0, edgeCount);
            Assert.Equal(0, nakedEdgeCount);
            Assert.Equal(0, nonManifoldEdgeCount);
        }

        [Fact]
        public void Watertightness_NullOptions_UsesDefaultsAndStillAnalyses()
        {
            // The wrapper must tolerate null options (fall back to defaults), since the
            // pre-check is convenience tooling that should never throw on the happy path.
            List<Face3D> face3Ds = TestGeometry.CreateClosedBoxFaces();

            bool analysed = face3Ds.Watertightness(null, out int edgeCount, out int nakedEdgeCount, out _);

            Assert.True(analysed);
            Assert.True(edgeCount > 0);
            Assert.Equal(0, nakedEdgeCount);
        }
    }
}
