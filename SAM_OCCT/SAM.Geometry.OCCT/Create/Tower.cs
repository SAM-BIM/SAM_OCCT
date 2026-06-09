// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Geometry.OCCT
{
    public static partial class Create
    {
        /// <summary>
        /// Builds the bounding faces of a zoned, optionally twisted tower, ready for the OCCT
        /// cell-complex build (<c>BOPAlgo_MakerVolume</c> splits them into one cell per zone).
        /// Each floor is a square ring (outer perimeter minus a square core inset) partitioned
        /// into four perimeter zones plus a central core. Only the facade twists: the outer
        /// walls loft from the floor's bottom rotation to its top rotation, while the core
        /// walls and the diagonal partition walls are strictly VERTICAL, fixed at the floor's
        /// bottom rotation - internal separations stay plumb the way a BEMS model expects.
        /// Per floor the faces are: four facade walls (two planar triangles each when twisted,
        /// planar quads when not), four vertical core walls, and four vertical diagonal
        /// partitions spanning core corner to outer corner; plus one full floor plate per level.
        /// The vertical partitions seal exactly at the floor's bottom profile and poke through
        /// the receding twisted facade above it - MakerVolume trims the excess and drops the
        /// unbounded outside fragments, so the cells still close. Pure SAM.Geometry - no
        /// native call - so it is unit-testable without the native runtime. Note: with a large
        /// per-floor twist and a shallow core inset (core corner radius exceeding the outer
        /// apothem) the rotated facade can clip the core; the default 20 m / 5 m profile is
        /// safe for any twist.
        /// </summary>
        /// <param name="height">Total tower height (m). Must be positive.</param>
        /// <param name="twistAngle">Total facade twist over the full height (radians). Applied linearly per floor; 0 gives an untwisted prism.</param>
        /// <param name="floors">Number of storeys. Must be at least 1.</param>
        /// <param name="width">Outer square plan dimension (m). Defaults to 20 m (the issue #12 profile).</param>
        /// <param name="coreInset">Inward offset of the core from the perimeter (m). Defaults to 5 m. Must be greater than 0 and less than half the width.</param>
        /// <returns>The tower bounding faces, or <c>null</c> when the arguments are invalid.</returns>
        public static List<Face3D> Tower(double height, double twistAngle, int floors, double width = 20.0, double coreInset = 5.0)
        {
            if (double.IsNaN(height) || height <= 0 || double.IsNaN(twistAngle) || floors < 1)
            {
                return null;
            }

            double halfWidth = width * 0.5;
            double halfCore = halfWidth - coreInset;
            if (double.IsNaN(width) || halfWidth <= 0 || double.IsNaN(coreInset) || halfCore <= 0)
            {
                return null;
            }

            double heightStep = height / floors;
            double angleStep = twistAngle / floors;
            bool twisted = angleStep != 0;

            // Plan corners, counter-clockwise, matching the issue #12 base profile order.
            double[][] outerPlan = { new[] { -halfWidth, halfWidth }, new[] { halfWidth, halfWidth }, new[] { halfWidth, -halfWidth }, new[] { -halfWidth, -halfWidth } };
            double[][] corePlan = { new[] { -halfCore, halfCore }, new[] { halfCore, halfCore }, new[] { halfCore, -halfCore }, new[] { -halfCore, -halfCore } };

            List<Face3D> result = new List<Face3D>();

            // One full floor plate per level; MakerVolume splits it into per-zone pieces.
            for (int level = 0; level <= floors; level++)
            {
                Point3D[] corners = PlanPoints(outerPlan, level * angleStep, level * heightStep);
                result.Add(Polygon(corners[0], corners[1], corners[2], corners[3]));
            }

            for (int floor = 0; floor < floors; floor++)
            {
                double zBottom = floor * heightStep;
                double zTop = (floor + 1) * heightStep;
                double angleBottom = floor * angleStep;
                double angleTop = (floor + 1) * angleStep;

                Point3D[] outerBottom = PlanPoints(outerPlan, angleBottom, zBottom);
                Point3D[] outerTop = PlanPoints(outerPlan, angleTop, zTop);

                // Vertical internals: top corners reuse the BOTTOM rotation, so the core and
                // partition walls are plumb regardless of the facade twist.
                Point3D[] outerBottomRaised = PlanPoints(outerPlan, angleBottom, zTop);
                Point3D[] coreBottom = PlanPoints(corePlan, angleBottom, zBottom);
                Point3D[] coreBottomRaised = PlanPoints(corePlan, angleBottom, zTop);

                for (int edge = 0; edge < 4; edge++)
                {
                    int next = (edge + 1) % 4;

                    // Facade: lofted between the floor's bottom and top rotations. A twisted
                    // facade quad is non-planar, and the native make_face requires planar
                    // wires, so it is emitted as two planar triangles.
                    if (twisted)
                    {
                        result.Add(Polygon(outerBottom[edge], outerBottom[next], outerTop[next]));
                        result.Add(Polygon(outerBottom[edge], outerTop[next], outerTop[edge]));
                    }
                    else
                    {
                        result.Add(Polygon(outerBottom[edge], outerBottom[next], outerTop[next], outerTop[edge]));
                    }

                    // Core wall: vertical.
                    result.Add(Polygon(coreBottom[edge], coreBottom[next], coreBottomRaised[next], coreBottomRaised[edge]));

                    // Diagonal partition: vertical plane through the plan diagonal, spanning
                    // core corner to outer corner for the full floor height.
                    result.Add(Polygon(coreBottom[edge], outerBottom[edge], outerBottomRaised[edge], coreBottomRaised[edge]));
                }
            }

            return result;
        }

        private static Point3D[] PlanPoints(double[][] plan, double angle, double z)
        {
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);

            Point3D[] result = new Point3D[plan.Length];
            for (int i = 0; i < plan.Length; i++)
            {
                double x = plan[i][0];
                double y = plan[i][1];
                result[i] = new Point3D(x * cos - y * sin, x * sin + y * cos, z);
            }

            return result;
        }

        private static Face3D Polygon(params Point3D[] point3Ds)
        {
            return new Polygon3D(new List<Point3D>(point3Ds)).ToFace3D();
        }
    }
}
