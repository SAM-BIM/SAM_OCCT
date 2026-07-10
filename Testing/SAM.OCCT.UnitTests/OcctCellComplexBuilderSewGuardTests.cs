// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Native;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    public class OcctCellComplexBuilderSewGuardTests
    {
        [Fact]
        public void CanAttemptSew_FaceCountAboveLimit_ReturnsFalseWithDiagnostic()
        {
            // Arrange
            OcctBuildOptions options = new OcctBuildOptions { MaxSewFaceCount = 1 };
            OcctCellComplexResult result = new OcctCellComplexResult();
            List<Face3D> face3Ds = new List<Face3D>
            {
                TestGeometry.CreateTriangleFace(),
                TestGeometry.CreateUnitQuadFace()
            };

            // Act
            bool canAttempt = OcctCellComplexBuilder.CanAttemptSew(face3Ds, options, result);

            // Assert
            Assert.False(canAttempt);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_SKIPPED_LARGE_INPUT" && x.Severity == OcctDiagnosticSeverity.Warning);
        }

        [Fact]
        public void CanAttemptSew_NonPositiveLimit_ReturnsTrue()
        {
            // Arrange
            OcctBuildOptions options = new OcctBuildOptions { MaxSewFaceCount = 0 };
            OcctCellComplexResult result = new OcctCellComplexResult();
            List<Face3D> face3Ds = new List<Face3D>
            {
                TestGeometry.CreateTriangleFace(),
                TestGeometry.CreateUnitQuadFace()
            };

            // Act
            bool canAttempt = OcctCellComplexBuilder.CanAttemptSew(face3Ds, options, result);

            // Assert
            Assert.True(canAttempt);
            Assert.Empty(result.Diagnostics);
        }
    }
}
