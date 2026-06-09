// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using GeometryCreate = SAM.Geometry.OCCT.Create;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// End-to-end coverage for OCCT triangulation of non-planar surface boundaries.
    /// Auto-skips when the native library is absent.
    /// </summary>
    public class TriangulateSurfaceIntegrationTests
    {
        [SkippableFact]
        public void Triangulate_WarpedQuads_ProducesPlanarTriangles()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Two warped quads from the PR #9 review: three corners sit on z = 21.136 and one
            // drops to z = 18.493, so each boundary is genuinely non-planar.
            List<IReadOnlyList<Point3D>> boundaryLoops = new List<IReadOnlyList<Point3D>>
            {
                new List<Point3D>
                {
                    new Point3D(79.1, 3.7, 18.493),
                    new Point3D(78.132967, 11.7, 21.136),
                    new Point3D(68.7, 11.7, 21.136),
                    new Point3D(64.7, 10.8, 21.136)
                },
                new List<Point3D>
                {
                    new Point3D(50.5, 7.7, 21.136),
                    new Point3D(51, 10.8, 21.136),
                    new Point3D(64.7, 10.8, 21.136),
                    new Point3D(62.423077, 3.7, 18.493)
                }
            };

            // Act
            List<Triangle3D> triangles = GeometryCreate.Triangulate(boundaryLoops, out OcctCellComplexResult result, 0.1, 0.5, false, new OcctBuildOptions());

            // Assert
            Assert.True(result.NativeAvailable);
            Assert.DoesNotContain(result.Diagnostics, x => x.Severity == OcctDiagnosticSeverity.Error);
            Assert.NotNull(triangles);

            // Each warped quad must yield at least two planar triangles.
            Assert.True(triangles.Count >= 4, $"Expected at least 4 triangles, got {triangles?.Count ?? 0}.");
            Assert.All(triangles, triangle => Assert.True(triangle.GetArea() > 0));

            // Triangulation must stay within the source z-range; nothing should be flattened away.
            List<Point3D> vertices = triangles.SelectMany(x => x.GetPoints()).ToList();
            Assert.Contains(vertices, point => point.Z < 19.0);
            Assert.All(vertices, point => Assert.InRange(point.Z, 18.0, 22.0));
        }
    }
}
