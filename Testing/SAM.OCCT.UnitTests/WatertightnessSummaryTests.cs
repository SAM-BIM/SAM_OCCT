// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Native;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Pure-managed welded-edge watertightness summary that gates the BOP-glue
    /// path (issue #37 follow-on): glue is only enabled when every edge is shared
    /// by exactly two faces. Needs no native OCCT library.
    /// </summary>
    public class WatertightnessSummaryTests
    {
        [Fact]
        public void AnalyzeWatertightness_ClosedBox_IsCleanForGlue()
        {
            // Arrange - a closed cube: every edge shared by exactly two faces.
            List<Face3D> face3Ds = TestGeometry.CreateClosedBoxFaces();

            // Act
            OcctOpenShellAnalysis.WatertightnessSummary summary = OcctOpenShellAnalysis.AnalyzeWatertightness(face3Ds, new OcctBuildOptions());

            // Assert
            Assert.True(summary.EdgeCount > 0);
            Assert.Equal(0, summary.NakedEdgeCount);
            Assert.Equal(0, summary.NonManifoldEdgeCount);
            Assert.True(summary.IsCleanForGlue);
        }

        [Fact]
        public void AnalyzeWatertightness_OpenBox_IsNotCleanForGlue()
        {
            // Arrange - a cube missing its top face leaves four naked rim edges.
            List<Face3D> face3Ds = TestGeometry.CreateOpenBoxFaces();

            // Act
            OcctOpenShellAnalysis.WatertightnessSummary summary = OcctOpenShellAnalysis.AnalyzeWatertightness(face3Ds, new OcctBuildOptions());

            // Assert
            Assert.Equal(4, summary.NakedEdgeCount);
            Assert.False(summary.IsCleanForGlue);
        }

        [Fact]
        public void AnalyzeWatertightness_NullInput_IsNotCleanForGlue()
        {
            OcctOpenShellAnalysis.WatertightnessSummary summary = OcctOpenShellAnalysis.AnalyzeWatertightness(null, new OcctBuildOptions());

            Assert.Equal(0, summary.EdgeCount);
            Assert.False(summary.IsCleanForGlue);
        }
    }
}
