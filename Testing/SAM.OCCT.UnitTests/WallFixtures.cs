// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AnalyticalCreate = SAM.Analytical.Create;
using SolverModify = SAM.Analytical.OCCT.Solver.Modify;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Geometry-only wall fixtures for the Clean3D rule (b) tests: walls given by their plan end points and elevations (optionally an
    /// explicit outline in the wall's (along, height) frame), the presentations of one wall (winding reversal, loop-start rotation, panel
    /// order) and a managed Clean3D run that keeps every weight and reach on its physical wall whatever the order the panels are given in.
    /// </summary>
    internal static class WallFixtures
    {
        internal const double Tol = 1e-6;

        private static readonly Construction WallConstruction = new Construction("wall");

        internal sealed class Piece
        {
            public string Name;
            public double X0, Y0, X1, Y1, Z0, Z1, Weight, MaxExt;

            /// <summary>Optional outline as (distance along the wall from (X0, Y0), height); null for the rectangle Z0..Z1.</summary>
            public (double S, double Z)[] Outline;

            /// <summary>A wall from plan point (x0, y0) to (x1, y1), z0..z1 m high.</summary>
            public static Piece Wall(string name, double x0, double y0, double x1, double y1, double z0, double z1, double weight, double maxExt = 0.4)
            {
                return new Piece { Name = name, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Z0 = z0, Z1 = z1, Weight = weight, MaxExt = maxExt };
            }

            /// <summary>A wall on the plan line (x0, y0)-(x1, y1) whose outline is given as (distance along the line, height) pairs.</summary>
            public static Piece Shaped(string name, double x0, double y0, double x1, double y1, double weight, params (double S, double Z)[] outline)
            {
                return new Piece { Name = name, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Z0 = outline.Min(p => p.Z), Z1 = outline.Max(p => p.Z), Weight = weight, MaxExt = 0.4, Outline = outline };
            }

            public Point3D[] Loop()
            {
                if (Outline == null)
                {
                    return new[] { new Point3D(X0, Y0, Z0), new Point3D(X1, Y1, Z0), new Point3D(X1, Y1, Z1), new Point3D(X0, Y0, Z1) };
                }

                double length = System.Math.Sqrt(((X1 - X0) * (X1 - X0)) + ((Y1 - Y0) * (Y1 - Y0)));
                double ux = (X1 - X0) / length, uy = (Y1 - Y0) / length;
                return Outline.Select(p => new Point3D(X0 + (ux * p.S), Y0 + (uy * p.S), p.Z)).ToArray();
            }
        }

        /// <summary>One presentation of a loop: winding reversal keeps the first vertex, loop-start rotation keeps the winding.</summary>
        internal static Point3D[] Present(Point3D[] loop, bool reversed, int start)
        {
            List<Point3D> list = loop.ToList();
            if (reversed)
            {
                list = new[] { list[0] }.Concat(list.Skip(1).Reverse()).ToList();
            }

            start %= list.Count;
            return list.Skip(start).Concat(list.Take(start)).ToArray();
        }

        /// <summary>The point turned by <paramref name="degrees"/> about the world X axis (a tilted level).</summary>
        internal static Point3D Tilt(Point3D point3D, double degrees)
        {
            double a = degrees * (System.Math.PI / 180), c = System.Math.Cos(a), s = System.Math.Sin(a);
            return new Point3D(point3D.X, (point3D.Y * c) - (point3D.Z * s), (point3D.Y * s) + (point3D.Z * c));
        }

        /// <summary>The model-up axis of a level tilted by <paramref name="degrees"/> about X; null (world Z) for no tilt.</summary>
        internal static Vector3D TiltedUp(double degrees)
        {
            if (degrees == 0)
            {
                return null;
            }

            double a = degrees * (System.Math.PI / 180);
            return new Vector3D(0, -System.Math.Sin(a), System.Math.Cos(a));
        }

        internal static List<Point3D> Points(Face3D face3D)
        {
            return (face3D?.GetExternalEdge3D() as ISegmentable3D)?.GetPoints() ?? new List<Point3D>();
        }

        internal static bool Near(Point3D q, double x, double y, double z)
        {
            return System.Math.Abs(q.X - x) <= Tol && System.Math.Abs(q.Y - y) <= Tol && System.Math.Abs(q.Z - z) <= Tol;
        }

        internal static string F(double v) => v.ToString("0.########", CultureInfo.InvariantCulture);

        internal static string V(Point3D q) => "(" + F(q.X) + "," + F(q.Y) + "," + F(q.Z) + ")";

        internal static IEnumerable<int[]> Permutations(int[] items)
        {
            if (items.Length <= 1)
            {
                yield return items;
                yield break;
            }

            for (int i = 0; i < items.Length; i++)
            {
                int[] rest = items.Where((_, j) => j != i).ToArray();
                foreach (int[] permutation in Permutations(rest))
                {
                    yield return new[] { items[i] }.Concat(permutation).ToArray();
                }
            }
        }

        /// <summary>
        /// The presentations of the pieces for one axis, each independent of the others: "baseline" (as authored), "winding" (every
        /// winding subset), "rotation" (each piece's loop start alone, then all together) and "reorder" (every panel order). Each case is
        /// (order, reversed per piece, loop start per piece).
        /// </summary>
        internal static IEnumerable<(string Id, int[] Order, bool[] Reversed, int[] Start)> Presentations(string axis, Piece[] pieces)
        {
            int count = pieces.Length;
            int[] id = Enumerable.Range(0, count).ToArray();
            bool[] none = new bool[count];
            int[] zero = new int[count];
            switch (axis)
            {
                case "baseline":
                    yield return ("authored", id, none, zero);
                    break;
                case "winding":
                    for (int m = 1; m < (1 << count); m++)
                    {
                        bool[] reversed = Enumerable.Range(0, count).Select(i => (m & (1 << i)) != 0).ToArray();
                        yield return ("reversed=" + string.Join("", reversed.Select(b => b ? "1" : "0")), id, reversed, zero);
                    }

                    break;
                case "rotation":
                    for (int i = 0; i < count; i++)
                    {
                        for (int s = 1; s < pieces[i].Loop().Length; s++)
                        {
                            int[] start = new int[count];
                            start[i] = s;
                            yield return ("wall" + i + "start=" + s, id, none, start);
                        }
                    }

                    for (int s = 1; s < 4; s++)
                    {
                        yield return ("allstart=" + s, id, none, Enumerable.Repeat(s, count).ToArray());
                    }

                    break;
                case "reorder":
                    foreach (int[] permutation in Permutations(id))
                    {
                        yield return ("order=" + string.Join("", permutation), permutation, none, zero);
                    }

                    break;
                default:
                    throw new ArgumentException(axis);
            }
        }

        /// <summary>Managed Clean3D of the pieces in the given order and presentation; the weight and the reach follow the physical wall.</summary>
        internal static List<Panel> Clean(Piece[] pieces, int[] order, bool[] reversed, int[] start)
        {
            List<Panel> panels = new List<Panel>();
            List<double> weights = new List<double>();
            List<double> reach = new List<double>();
            foreach (int i in order)
            {
                panels.Add(AnalyticalCreate.Panel(WallConstruction, PanelType.Wall, TestGeometry.CreatePlanarFace(Present(pieces[i].Loop(), reversed[i], start[i]))));
                weights.Add(pieces[i].Weight);
                reach.Add(pieces[i].MaxExt);
            }

            return SolverModify.Clean3D(panels, out List<string> _, weights: weights, maxExtends: reach) ?? new List<Panel>();
        }

        internal static List<Panel> Clean(Piece[] pieces)
        {
            int[] order = Enumerable.Range(0, pieces.Length).ToArray();
            return Clean(pieces, order, new bool[pieces.Length], new int[pieces.Length]);
        }
    }
}
