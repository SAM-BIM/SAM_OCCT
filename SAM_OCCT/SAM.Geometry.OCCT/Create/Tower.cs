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
        /// Builds the closed shells of a zoned, optionally twisted tower. Each floor is a
        /// square ring (outer perimeter minus a square core inset) split into four perimeter
        /// prisms plus one central core prism, so every floor contributes five closed shells.
        /// The whole storey is rotated about the vertical axis by an angle that grows linearly
        /// with height, producing the twist. The shells are returned floor-major; within each
        /// floor the order is the four perimeter prisms (one per base edge) followed by the
        /// core. Pure SAM.Geometry - no OCCT/native call - so it is unit-testable without the
        /// native runtime. Feed the result to <c>SAM.Geometry.OCCT.Create.Shells</c>
        /// or <c>SAM.Analytical.OCCT.Create.Tower</c> to obtain a watertight cell complex.
        /// </summary>
        /// <param name="height">Total tower height (m). Must be positive.</param>
        /// <param name="twistAngle">Total twist over the full height (radians). Applied linearly per floor; 0 gives an untwisted prism.</param>
        /// <param name="floors">Number of storeys. Must be at least 1.</param>
        /// <param name="width">Outer square plan dimension (m). Defaults to 20 m (the issue #12 profile).</param>
        /// <param name="coreInset">Inward offset of the core from the perimeter (m). Defaults to 5 m. Must be greater than 0 and less than half the width.</param>
        /// <returns>The closed tower shells, or <c>null</c> when the arguments are invalid.</returns>
        public static List<Shell> Tower(double height, double twistAngle, int floors, double width = 20.0, double coreInset = 5.0)
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

            // Plan corners, counter-clockwise, matching the issue #12 base profile order.
            double[][] outerPlan = { new[] { -halfWidth, halfWidth }, new[] { halfWidth, halfWidth }, new[] { halfWidth, -halfWidth }, new[] { -halfWidth, -halfWidth } };
            double[][] corePlan = { new[] { -halfCore, halfCore }, new[] { halfCore, halfCore }, new[] { halfCore, -halfCore }, new[] { -halfCore, -halfCore } };

            List<Shell> result = new List<Shell>();
            for (int floor = 0; floor < floors; floor++)
            {
                double zBottom = floor * heightStep;
                double zTop = (floor + 1) * heightStep;
                double angleBottom = floor * angleStep;
                double angleTop = (floor + 1) * angleStep;

                Point3D[] outerBottom = PlanPoints(outerPlan, angleBottom, zBottom);
                Point3D[] outerTop = PlanPoints(outerPlan, angleTop, zTop);
                Point3D[] coreBottom = PlanPoints(corePlan, angleBottom, zBottom);
                Point3D[] coreTop = PlanPoints(corePlan, angleTop, zTop);

                // Four perimeter prisms: the square ring between the outer and core profiles,
                // partitioned along the corner-to-corner diagonals into one prism per base edge.
                for (int edge = 0; edge < 4; edge++)
                {
                    int next = (edge + 1) % 4;
                    List<Face3D> face3Ds = new List<Face3D>();
                    AddQuad(face3Ds, outerBottom[edge], outerBottom[next], coreBottom[next], coreBottom[edge]); // bottom
                    AddQuad(face3Ds, outerTop[edge], outerTop[next], coreTop[next], coreTop[edge]);             // top
                    AddQuad(face3Ds, outerBottom[edge], outerBottom[next], outerTop[next], outerTop[edge]);     // outer facade
                    AddQuad(face3Ds, coreBottom[edge], coreBottom[next], coreTop[next], coreTop[edge]);         // inner (core-facing) wall
                    AddQuad(face3Ds, outerBottom[edge], coreBottom[edge], coreTop[edge], outerTop[edge]);       // diagonal end wall
                    AddQuad(face3Ds, outerBottom[next], coreBottom[next], coreTop[next], outerTop[next]);       // diagonal end wall

                    result.Add(new Shell(face3Ds));
                }

                // Central core prism.
                List<Face3D> coreFace3Ds = new List<Face3D>();
                AddQuad(coreFace3Ds, coreBottom[0], coreBottom[1], coreBottom[2], coreBottom[3]); // bottom
                AddQuad(coreFace3Ds, coreTop[0], coreTop[1], coreTop[2], coreTop[3]);             // top
                for (int edge = 0; edge < 4; edge++)
                {
                    int next = (edge + 1) % 4;
                    AddQuad(coreFace3Ds, coreBottom[edge], coreBottom[next], coreTop[next], coreTop[edge]);
                }

                result.Add(new Shell(coreFace3Ds));
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

        // Splits a (possibly non-planar, when twisted) quad into two triangles along the
        // point_1-point_3 diagonal. Triangles are always planar, so the native make_face -
        // which requires a planar wire - never rejects them. Callers that share a wall pass
        // its corners in the same order, so adjacent cells split it identically and the
        // triangulated interface coincides exactly.
        private static void AddQuad(List<Face3D> face3Ds, Point3D point3D_1, Point3D point3D_2, Point3D point3D_3, Point3D point3D_4)
        {
            face3Ds.Add(Triangle(point3D_1, point3D_2, point3D_3));
            face3Ds.Add(Triangle(point3D_1, point3D_3, point3D_4));
        }

        private static Face3D Triangle(Point3D point3D_1, Point3D point3D_2, Point3D point3D_3)
        {
            return new Polygon3D(new List<Point3D> { point3D_1, point3D_2, point3D_3 }).ToFace3D();
        }
    }
}
