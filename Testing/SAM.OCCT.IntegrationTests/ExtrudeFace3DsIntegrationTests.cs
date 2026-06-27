// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare a static Query class.
using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// End-to-end tests that drive the real native OCCT prism extrusion
    /// (issue #30). Each test auto-skips (via SkippableFact) when the native
    /// library is absent, so the suite is safe on agents without OCCT while
    /// still exercising the extrude + decode path wherever SAM.Occt.Native is
    /// built.
    /// </summary>
    public class ExtrudeFace3DsIntegrationTests
    {
        [SkippableFact]
        public void ExtrudeFace3Ds_UnitFootprintByHeight_ProducesPrismVolume()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a 1 x 1 footprint in the z = 0 plane.
            Face3D footprint = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0),
                new Point3D(1, 0, 0),
                new Point3D(1, 1, 0),
                new Point3D(0, 1, 0));

            // Act - extrude 3 m vertically.
            List<Shell> shells = GeometryQuery.ExtrudeFace3Ds(new List<Face3D> { footprint }, 3.0, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert - one closed solid of volume 1 x 1 x 3 = 3 m³.
            Assert.True(result.NativeAvailable);
            Assert.True(result.Success);
            Assert.NotNull(shells);
            Assert.Single(result.Cells);
            Assert.Equal(3.0, result.Cells.Sum(x => x.Volume), 6);
        }

        [SkippableFact]
        public void ExtrudeFace3Ds_TwoFootprints_ProducesTwoSolids()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - two separate 1 x 1 footprints.
            Face3D footprintA = TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0),
                new Point3D(1, 0, 0),
                new Point3D(1, 1, 0),
                new Point3D(0, 1, 0));
            Face3D footprintB = TestGeometry.CreatePlanarFace(
                new Point3D(3, 0, 0),
                new Point3D(4, 0, 0),
                new Point3D(4, 1, 0),
                new Point3D(3, 1, 0));

            // Act
            List<Shell> shells = GeometryQuery.ExtrudeFace3Ds(new List<Face3D> { footprintA, footprintB }, 2.0, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert - two independent prisms, each of volume 2 m³.
            Assert.True(result.Success);
            Assert.NotNull(shells);
            Assert.Equal(2, result.Cells.Count);
            Assert.Equal(4.0, result.Cells.Sum(x => x.Volume), 6);
        }
    }
}
