// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Native;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Exercises the pure-managed input serializer that turns SAM geometry into the
    /// flat arrays handed to the native OCCT layer. Reachable via InternalsVisibleTo.
    /// </summary>
    public class OcctNativeInputBuilderTests
    {
        [Fact]
        public void TryBuild_EmptyFaces_ReturnsFalseWithInputEmptyDiagnostic()
        {
            // Arrange
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool built = OcctNativeInputBuilder.TryBuild(
                new List<Face3D>(),
                new OcctBuildOptions(),
                result,
                out OcctNativeInput input);

            // Assert
            Assert.False(built);
            Assert.Null(input);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_INPUT_EMPTY" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void TryBuild_SingleTriangle_ReturnsTrueWithCoherentArrays()
        {
            // Arrange
            OcctCellComplexResult result = new OcctCellComplexResult();
            List<Face3D> face3Ds = new List<Face3D> { TestGeometry.CreateTriangleFace() };

            // Act
            bool built = OcctNativeInputBuilder.TryBuild(face3Ds, new OcctBuildOptions(), result, out OcctNativeInput input);

            // Assert
            Assert.True(built);
            Assert.NotNull(input);
            Assert.Equal(1, input.FaceCount);
            Assert.Single(input.FaceLoopCounts);
            Assert.All(input.LoopPointCounts, count => Assert.True(count >= 3));
            // Three coordinates (x, y, z) per emitted point.
            Assert.Equal(0, input.Coordinates.Length % 3);
            Assert.Equal(input.LoopPointCounts.Sum(), input.Coordinates.Length / 3);
        }

        [Fact]
        public void TryBuild_NullShells_ReturnsFalseWithSuppliedEmptyDiagnostic()
        {
            // Arrange
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool built = OcctNativeInputBuilder.TryBuild(
                (IEnumerable<Shell>)null,
                new OcctBuildOptions(),
                result,
                "SAM_OCCT_TEST_EMPTY",
                "no shells",
                out OcctNativeInput input);

            // Assert
            Assert.False(built);
            Assert.Null(input);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_TEST_EMPTY" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void TryBuild_SingleFaceShell_PopulatesShellMetadata()
        {
            // Arrange
            OcctCellComplexResult result = new OcctCellComplexResult();
            List<Shell> shells = new List<Shell> { TestGeometry.CreateSingleFaceShell() };

            // Act
            bool built = OcctNativeInputBuilder.TryBuild(
                shells,
                new OcctBuildOptions(),
                result,
                "SAM_OCCT_TEST_EMPTY",
                "no shells",
                out OcctNativeInput input);

            // Assert
            Assert.True(built);
            Assert.NotNull(input);
            Assert.Equal(1, input.ShellCount);
            Assert.Single(input.ShellFaceCounts);
            Assert.Equal(input.FaceCount, input.FaceLoopCounts.Length);
        }
    }
}
