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
    }
}
