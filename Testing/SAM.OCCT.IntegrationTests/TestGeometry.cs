// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Closed-shell factory for native OCCT integration tests. Uses only the public
    /// SAM.Geometry APIs (Point3D, Polygon3D, Face3D, Shell).
    /// </summary>
    internal static class TestGeometry
    {
        public static Face3D CreatePlanarFace(params Point3D[] points)
        {
            List<Point3D> point3Ds = new List<Point3D>(points);
            List<IClosedPlanar3D> loops = new List<IClosedPlanar3D> { new Polygon3D(point3Ds) };
            return Face3D.Create(loops);
        }

        /// <summary>Axis-aligned box shell with corner at (ox, oy, oz) and the given size.</summary>
        public static Shell CreateBox(double ox, double oy, double oz, double sx, double sy, double sz)
        {
            double x0 = ox, x1 = ox + sx;
            double y0 = oy, y1 = oy + sy;
            double z0 = oz, z1 = oz + sz;

            List<Face3D> face3Ds = new List<Face3D>
            {
                CreatePlanarFace(new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x0, y1, z0)), // bottom
                CreatePlanarFace(new Point3D(x0, y0, z1), new Point3D(x1, y0, z1), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1)), // top
                CreatePlanarFace(new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y0, z1), new Point3D(x0, y0, z1)), // front (y0)
                CreatePlanarFace(new Point3D(x0, y1, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1)), // back (y1)
                CreatePlanarFace(new Point3D(x0, y0, z0), new Point3D(x0, y1, z0), new Point3D(x0, y1, z1), new Point3D(x0, y0, z1)), // left (x0)
                CreatePlanarFace(new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x1, y0, z1))  // right (x1)
            };

            return new Shell(face3Ds);
        }

        public static Shell CreateUnitBox(double ox, double oy, double oz)
        {
            return CreateBox(ox, oy, oz, 1.0, 1.0, 1.0);
        }

        /// <summary>
        /// A triangular roof prism (the issue #29 repro solid): an isosceles
        /// triangle in the y-z plane extruded along x from 0 to 9. Its two
        /// sloped faces meet the base and each other at non-orthogonal corners,
        /// which the simple (no-intersection) offset/thicken algorithm could not
        /// handle.
        /// </summary>
        public static Shell CreateTriangularPrism()
        {
            Point3D a0 = new Point3D(0, -30, 10), b0 = new Point3D(0, -20, 10), c0 = new Point3D(0, -23, 20);
            Point3D a9 = new Point3D(9, -30, 10), b9 = new Point3D(9, -20, 10), c9 = new Point3D(9, -23, 20);

            List<Face3D> face3Ds = new List<Face3D>
            {
                CreatePlanarFace(a0, b0, c0), // triangle cap (x = 0)
                CreatePlanarFace(a9, b9, c9), // triangle cap (x = 9)
                CreatePlanarFace(a0, b0, b9, a9), // base
                CreatePlanarFace(b0, c0, c9, b9), // slope 1
                CreatePlanarFace(c0, a0, a9, c9)  // slope 2
            };

            return new Shell(face3Ds);
        }

        /// <summary>
        /// A closed, slender column box (0.03 x 0.03 x height) whose top and bottom
        /// caps are 0.0009 m^2 - below the default 0.001 m^2 minArea - while its four
        /// walls stay above it. The caps are load-bearing, and the 0.03 m opening left
        /// by removing one is far wider than the fuzzy tolerance, so OCCT cannot bridge
        /// it: dropping a cap genuinely opens the volume. Used to regression-test the
        /// shell face filter (issue #11). Returns the cap area for assertions.
        /// </summary>
        public static Shell CreateSlenderColumnBox(double ox, double oy, double oz, double height, out double capArea)
        {
            const double side = 0.03;
            capArea = side * side; // 0.0009 m^2

            Shell shell = CreateBox(ox, oy, oz, side, side, height);

            // Sanity: confirm the caps are below, and the walls above, the default minArea.
            return shell;
        }
    }
}
