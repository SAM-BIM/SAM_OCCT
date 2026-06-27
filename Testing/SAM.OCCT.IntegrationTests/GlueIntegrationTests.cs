// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare a static Query class.
using GeometryCreate = SAM.Geometry.OCCT.Create;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// End-to-end tests for the BOP-glue cell-complex path (issue #37 follow-on):
    /// glue is applied only to validated-clean input and degrades gracefully
    /// otherwise. Auto-skip when the native library is absent.
    /// </summary>
    public class GlueIntegrationTests
    {
        /// <summary>A single closed unit box as a 6-face soup.</summary>
        private static List<Face3D> CreateUnitBoxFaceSoup(double oz)
        {
            double z0 = oz, z1 = oz + 1.0;
            Point3D b00 = new Point3D(0, 0, z0), b10 = new Point3D(1, 0, z0), b11 = new Point3D(1, 1, z0), b01 = new Point3D(0, 1, z0);
            Point3D t00 = new Point3D(0, 0, z1), t10 = new Point3D(1, 0, z1), t11 = new Point3D(1, 1, z1), t01 = new Point3D(0, 1, z1);

            return new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(b00, b10, b11, b01), // bottom
                TestGeometry.CreatePlanarFace(t00, t10, t11, t01), // top
                TestGeometry.CreatePlanarFace(b00, b10, t10, t00), // front
                TestGeometry.CreatePlanarFace(b01, b11, t11, t01), // back
                TestGeometry.CreatePlanarFace(b00, b01, t01, t00), // left
                TestGeometry.CreatePlanarFace(b10, b11, t11, t10)  // right
            };
        }

        /// <summary>An open 5-face box soup (top-left open) the glue gate must reject.</summary>
        private static List<Face3D> CreateOpenBoxFaceSoup()
        {
            Point3D b00 = new Point3D(0, 0, 0), b10 = new Point3D(1, 0, 0), b11 = new Point3D(1, 1, 0), b01 = new Point3D(0, 1, 0);
            Point3D t00 = new Point3D(0, 0, 1), t10 = new Point3D(1, 0, 1), t11 = new Point3D(1, 1, 1), t01 = new Point3D(0, 1, 1);

            return new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(b00, b10, b11, b01),
                TestGeometry.CreatePlanarFace(b00, b10, t10, t00),
                TestGeometry.CreatePlanarFace(b01, b11, t11, t01),
                TestGeometry.CreatePlanarFace(b00, b01, t01, t00),
                TestGeometry.CreatePlanarFace(b10, b11, t11, t10)
            };
        }

        [SkippableFact]
        public void Shells_GlueFull_CleanInput_BuildsWithGlue()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a clean closed box with full glue requested.
            List<Face3D> face3Ds = CreateUnitBoxFaceSoup(0);
            OcctBuildOptions options = new OcctBuildOptions { GlueMode = OcctGlueMode.Full };

            // Act
            List<Shell> shells = GeometryCreate.Shells(face3Ds, out OcctCellComplexResult result, options);

            // Assert - built one closed cell via the glue path.
            Assert.True(result.Success);
            Assert.NotNull(shells);
            Assert.Single(shells);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_GLUE_SUCCESS");

            double volume = result.Cells.Sum(x => System.Math.Abs(x.Volume));
            Assert.True(System.Math.Abs(volume - 1.0) < 0.05, string.Format("Volume should be ~1.0, was {0}.", volume));
        }

        [SkippableFact]
        public void Shells_GlueFull_OpenInput_SkipsGlueAndDoesNotClose()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - an open soup the glue gate must reject (gaps would let
            // glue corrupt the geometry).
            List<Face3D> face3Ds = CreateOpenBoxFaceSoup();
            OcctBuildOptions options = new OcctBuildOptions { GlueMode = OcctGlueMode.Full };

            // Act
            List<Shell> shells = GeometryCreate.Shells(face3Ds, out OcctCellComplexResult result, options);

            // Assert - glue was skipped on the open input and the build did not close.
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_GLUE_SKIPPED");
            Assert.False(result.Success);
            Assert.Null(shells);
        }
    }
}
