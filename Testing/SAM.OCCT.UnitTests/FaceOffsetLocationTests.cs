// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// The last space-location fallback of the direct OCCT rebuild (<c>Create.FaceOffsetLocation</c>): a point just inside one
    /// of a cell's faces. Needed for strongly non-convex cells whose centres fall outside them and whose decoded shell SAM's
    /// <see cref="Shell.InternalPoint3D"/> refuses (it requires <see cref="Shell.IsClosed"/>, which a small misfit between decoded faces defeats).
    /// Before it, one such cell abandoned the whole direct rebuild.
    /// </summary>
    public class FaceOffsetLocationTests
    {
        /// <summary>
        /// A U-shaped room 3 m high: 6 x 4 m with a 2 x 3 m notch in the middle of one side, so the bounding-box centre is in
        /// the notch. The wall at x = 0 is split in two faces at z = 1.5 and the upper one sits 5 mm out: the misfit of a decoded
        /// cell that SAM does not count as closed.
        /// </summary>
        private static List<Face3D> URoom()
        {
            List<(double X, double Y)> plan = new List<(double X, double Y)> { (0, 0), (6, 0), (6, 4), (4, 4), (4, 1), (2, 1), (2, 4), (0, 4) };
            List<Face3D> result = new List<Face3D>
            {
                new Face3D(new Polygon3D(plan.ConvertAll(p => new Point3D(p.X, p.Y, 0)))),
                new Face3D(new Polygon3D(plan.ConvertAll(p => new Point3D(p.X, p.Y, 3)))),
            };

            for (int i = 0; i < plan.Count; i++)
            {
                (double X, double Y) a = plan[i], b = plan[(i + 1) % plan.Count];
                if (a.X == 0 && b.X == 0)
                {
                    result.Add(TestGeometry.CreatePlanarFace(new Point3D(a.X, a.Y, 0), new Point3D(b.X, b.Y, 0), new Point3D(b.X, b.Y, 1.5), new Point3D(a.X, a.Y, 1.5)));
                    result.Add(TestGeometry.CreatePlanarFace(new Point3D(a.X - 0.005, a.Y, 1.5), new Point3D(b.X - 0.005, b.Y, 1.5), new Point3D(b.X - 0.005, b.Y, 3), new Point3D(a.X - 0.005, a.Y, 3)));
                    continue;
                }

                result.Add(TestGeometry.CreatePlanarFace(new Point3D(a.X, a.Y, 0), new Point3D(b.X, b.Y, 0), new Point3D(b.X, b.Y, 3), new Point3D(a.X, a.Y, 3)));
            }

            return result;
        }

        [Fact]
        public void FaceOffsetLocation_NonConvexCellWithoutInternalPoint_ReturnsPointInsideTheCell()
        {
            // Arrange
            List<Face3D> face3Ds = URoom();
            Shell shell = new Shell(face3Ds);
            OcctBuildOptions options = new OcctBuildOptions();
            Point3D centre = shell.GetBoundingBox().GetCentroid();

            // The situation of the Softhouse cells: no centre inside, no SAM internal point.
            Assert.False(shell.Inside(centre, options.FuzzyTolerance, options.Tolerance));
            Assert.Null(shell.InternalPoint3D(options.FuzzyTolerance, options.Tolerance));

            // Act
            Point3D location = Analytical.OCCT.Create.FaceOffsetLocation(face3Ds, shell, options);

            // Assert
            Assert.NotNull(location);
            Assert.True(shell.Inside(location, options.FuzzyTolerance, options.Tolerance));
        }

        [Fact]
        public void FaceOffsetLocation_NoFaces_ReturnsNull()
        {
            // Arrange
            Shell shell = new Shell(URoom());

            // Act
            Point3D location = Analytical.OCCT.Create.FaceOffsetLocation(new List<Face3D>(), shell, new OcctBuildOptions());

            // Assert
            Assert.Null(location);
        }
    }
}
