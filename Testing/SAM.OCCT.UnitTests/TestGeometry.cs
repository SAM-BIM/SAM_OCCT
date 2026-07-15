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

        /// <summary>
        /// A planar face with a single internal opening (hole). Both loops are given as their own
        /// ordered corner lists; <see cref="Face3D.Create(System.Collections.Generic.IEnumerable{IClosedPlanar3D}, bool)"/>
        /// selects the larger-area loop as the external boundary and orients the smaller as the hole,
        /// so the caller need not pre-orient. Used to exercise hole-aware cap containment
        /// (<see cref="Face3D.On(Point3D, double)"/>) in the wall-to-cap selection.
        /// </summary>
        public static Face3D CreatePlanarFaceWithOpening(Point3D[] outerLoop, Point3D[] openingLoop)
        {
            List<IClosedPlanar3D> loops = new List<IClosedPlanar3D>
            {
                new Polygon3D(new List<Point3D>(outerLoop)),
                new Polygon3D(new List<Point3D>(openingLoop)),
            };
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

        /// <summary>
        /// The six quad faces of the unit cube (0,0,0)-(1,1,1). Every cube edge is
        /// shared by exactly two faces, so the set is watertight (no naked edges).
        /// </summary>
        public static List<Face3D> CreateClosedBoxFaces()
        {
            return new List<Face3D>
            {
                // bottom (z = 0) and top (z = 1)
                CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(1, 1, 0), new Point3D(0, 1, 0)),
                CreatePlanarFace(new Point3D(0, 0, 1), new Point3D(1, 0, 1), new Point3D(1, 1, 1), new Point3D(0, 1, 1)),
                // front (y = 0) and back (y = 1)
                CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(1, 0, 1), new Point3D(0, 0, 1)),
                CreatePlanarFace(new Point3D(0, 1, 0), new Point3D(1, 1, 0), new Point3D(1, 1, 1), new Point3D(0, 1, 1)),
                // left (x = 0) and right (x = 1)
                CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(0, 1, 0), new Point3D(0, 1, 1), new Point3D(0, 0, 1)),
                CreatePlanarFace(new Point3D(1, 0, 0), new Point3D(1, 1, 0), new Point3D(1, 1, 1), new Point3D(1, 0, 1))
            };
        }

        /// <summary>
        /// The unit cube with the top (z = 1) face removed, leaving the four top
        /// rim edges used by a single face each - i.e. four naked (open) edges.
        /// </summary>
        public static List<Face3D> CreateOpenBoxFaces()
        {
            List<Face3D> face3Ds = CreateClosedBoxFaces();
            face3Ds.RemoveAt(1);
            return face3Ds;
        }
    }
}
