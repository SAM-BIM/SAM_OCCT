// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Small geometry factory for unit tests. Uses only the public SAM.Geometry APIs
    /// already exercised by the production code (Point3D, Polygon3D, Face3D, Shell).
    /// </summary>
    internal static class TestGeometry
    {
        public static Face3D CreatePlanarFace(params Point3D[] points)
        {
            List<Point3D> point3Ds = new List<Point3D>(points);
            List<IClosedPlanar3D> loops = new List<IClosedPlanar3D> { new Polygon3D(point3Ds) };
            return Face3D.Create(loops);
        }

        public static Face3D CreateTriangleFace()
        {
            return CreatePlanarFace(
                new Point3D(0, 0, 0),
                new Point3D(1, 0, 0),
                new Point3D(0, 1, 0));
        }

        public static Face3D CreateUnitQuadFace()
        {
            return CreatePlanarFace(
                new Point3D(0, 0, 0),
                new Point3D(1, 0, 0),
                new Point3D(1, 1, 0),
                new Point3D(0, 1, 0));
        }

        public static Shell CreateSingleFaceShell()
        {
            return new Shell(new List<Face3D> { CreateUnitQuadFace() });
        }
    }
}
