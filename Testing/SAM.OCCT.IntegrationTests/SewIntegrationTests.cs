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
using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// End-to-end tests for the native watertight sew-and-heal path (issue #37).
    /// They drive the real OCCT BRepBuilderAPI_Sewing + ShapeFix pipeline and
    /// auto-skip (via SkippableFact) when the native library is absent.
    /// </summary>
    public class SewIntegrationTests
    {
        // A gap larger than the default fuzzy tolerance (1e-3) so a direct
        // MakerVolume cannot close the box, but smaller than the sewing tolerance
        // used below so sew-and-heal can.
        private const double Gap = 3e-3;
        private const double SewingTolerance = 1e-2;

        /// <summary>
        /// A unit box as a triangulated face soup (each of the 6 faces split into
        /// 2 triangles) whose top face is lifted by <paramref name="gap"/>, so the
        /// side-wall tops (z = 1) and the top face (z = 1 + gap) do not meet:
        /// a direct MakerVolume leaves it open, while sewing joins them.
        /// </summary>
        private static List<Face3D> CreateGappedTriangulatedBoxFaceSoup(double gap)
        {
            double zt = 1.0 + gap;

            // Bottom (z = 0)
            Point3D b00 = new Point3D(0, 0, 0), b10 = new Point3D(1, 0, 0), b11 = new Point3D(1, 1, 0), b01 = new Point3D(0, 1, 0);
            // Side-wall tops (z = 1)
            Point3D s00 = new Point3D(0, 0, 1), s10 = new Point3D(1, 0, 1), s11 = new Point3D(1, 1, 1), s01 = new Point3D(0, 1, 1);
            // Lifted top face (z = 1 + gap)
            Point3D t00 = new Point3D(0, 0, zt), t10 = new Point3D(1, 0, zt), t11 = new Point3D(1, 1, zt), t01 = new Point3D(0, 1, zt);

            return new List<Face3D>
            {
                // bottom
                TestGeometry.CreatePlanarFace(b00, b10, b11),
                TestGeometry.CreatePlanarFace(b00, b11, b01),
                // top (lifted)
                TestGeometry.CreatePlanarFace(t00, t10, t11),
                TestGeometry.CreatePlanarFace(t00, t11, t01),
                // front (y = 0)
                TestGeometry.CreatePlanarFace(b00, b10, s10),
                TestGeometry.CreatePlanarFace(b00, s10, s00),
                // back (y = 1)
                TestGeometry.CreatePlanarFace(b01, b11, s11),
                TestGeometry.CreatePlanarFace(b01, s11, s01),
                // left (x = 0)
                TestGeometry.CreatePlanarFace(b00, b01, s01),
                TestGeometry.CreatePlanarFace(b00, s01, s00),
                // right (x = 1)
                TestGeometry.CreatePlanarFace(b10, b11, s11),
                TestGeometry.CreatePlanarFace(b10, s11, s10)
            };
        }

        [SkippableFact]
        public void Shells_GappedFaceSoup_DirectBuildLeavesItOpen()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a triangulated box with a 3 mm gap at the top.
            List<Face3D> face3Ds = CreateGappedTriangulatedBoxFaceSoup(Gap);

            // Act - a direct MakerVolume (no sewing) on the gapped soup.
            List<Shell> result_Shells = GeometryCreate.Shells(face3Ds, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert - the gap exceeds the fuzzy tolerance, so it cannot close.
            Assert.False(result.Success);
            Assert.Null(result_Shells);
        }

        [SkippableFact]
        public void Sew_GappedFaceSoup_ClosesToUnitVolume()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            List<Face3D> face3Ds = CreateGappedTriangulatedBoxFaceSoup(Gap);
            OcctBuildOptions options = new OcctBuildOptions { SewingTolerance = SewingTolerance };

            // Act - sew the face soup into a closed solid.
            List<Shell> result_Shells = GeometryQuery.Sew(face3Ds, out OcctCellComplexResult result, options);

            // Assert - one closed cell of ~unit volume.
            Assert.True(result.NativeAvailable);
            Assert.True(result.Success);
            Assert.NotNull(result_Shells);
            Assert.Single(result_Shells);

            double volume = result.Cells.Sum(x => System.Math.Abs(x.Volume));
            Assert.True(System.Math.Abs(volume - 1.0) < 0.05, string.Format("Volume should be ~1.0, was {0}.", volume));
        }

        [SkippableFact]
        public void Shells_SewBeforeBuild_RecoversGappedFaceSoup()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - the same gapped soup that the direct build cannot close.
            List<Face3D> face3Ds = CreateGappedTriangulatedBoxFaceSoup(Gap);
            OcctBuildOptions options = new OcctBuildOptions { SewBeforeBuild = true, SewingTolerance = SewingTolerance };

            // Act - Create.Shells now heals first, then builds the volume.
            List<Shell> result_Shells = GeometryCreate.Shells(face3Ds, out OcctCellComplexResult result, options);

            // Assert - the cell complex closes via the sew-before-build path.
            Assert.True(result.Success);
            Assert.NotNull(result_Shells);
            Assert.Single(result_Shells);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_SUCCESS");
        }

        [SkippableFact]
        public void Shells_HardCloseFailure_RecoversViaSewRetry()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - SewBeforeBuild stays false, so the direct MakerVolume runs
            // first and fails; a positive SewingTolerance lets the automatic retry
            // close the gap.
            List<Face3D> face3Ds = CreateGappedTriangulatedBoxFaceSoup(Gap);
            OcctBuildOptions options = new OcctBuildOptions { SewingTolerance = SewingTolerance };

            // Act
            List<Shell> result_Shells = GeometryCreate.Shells(face3Ds, out OcctCellComplexResult result, options);

            // Assert - recovered after the direct build's hard failure.
            Assert.True(result.Success);
            Assert.NotNull(result_Shells);
            Assert.Single(result_Shells);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_SUCCESS");
        }
    }
}
