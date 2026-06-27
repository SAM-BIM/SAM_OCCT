// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// End-to-end coverage for OCCT coplanar face merging (ShapeUpgrade_UnifySameDomain).
    /// Auto-skips when the native library is absent.
    /// </summary>
    public class MergeCoplanarIntegrationTests
    {
        [SkippableFact]
        public void MergeCoplanarFace3Ds_TwoAdjacentCoplanarSquares_ProduceSingleFace()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Two unit squares on z = 0 sharing the edge x = 1; coplanar and adjacent.
            Face3D squareA = new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(0, 0, 0),
                new Point3D(1, 0, 0),
                new Point3D(1, 1, 0),
                new Point3D(0, 1, 0)
            }));

            Face3D squareB = new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(1, 0, 0),
                new Point3D(2, 0, 0),
                new Point3D(2, 1, 0),
                new Point3D(1, 1, 0)
            }));

            // Act
            List<Face3D> merged = GeometryQuery.MergeCoplanarFace3Ds(new List<Face3D> { squareA, squareB }, out OcctCellComplexResult result, Tolerance.Angle, new OcctBuildOptions());

            // Assert
            Assert.True(result.NativeAvailable);
            Assert.DoesNotContain(result.Diagnostics, x => x.Severity == OcctDiagnosticSeverity.Error);
            Assert.NotNull(merged);

            // The two coplanar squares should collapse into a single 2 x 1 face.
            Assert.Single(merged);
            Assert.Equal(2.0, merged[0].GetArea(), 3);
        }

        [SkippableFact]
        public void MergeCoplanarFace3Ds_DifferentPlanes_AreKeptSeparate()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // One face on z = 0 and one vertical face on x = 0; not coplanar.
            Face3D horizontal = new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(0, 0, 0),
                new Point3D(1, 0, 0),
                new Point3D(1, 1, 0),
                new Point3D(0, 1, 0)
            }));

            Face3D vertical = new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(0, 0, 0),
                new Point3D(0, 1, 0),
                new Point3D(0, 1, 1),
                new Point3D(0, 0, 1)
            }));

            // Act
            List<Face3D> merged = GeometryQuery.MergeCoplanarFace3Ds(new List<Face3D> { horizontal, vertical }, out OcctCellComplexResult result, Tolerance.Angle, new OcctBuildOptions());

            // Assert
            Assert.True(result.NativeAvailable);
            Assert.NotNull(merged);
            Assert.Equal(2, merged.Count);
        }
    }
}
