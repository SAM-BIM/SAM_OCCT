// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Native
{
    /// <summary>
    /// Decides the sew guard's glue-band policy (<see cref="OcctUnderSplitGuard.CheckDisjoint"/>) for a common piece
    /// of two closed cells that the managed piece tests (<see cref="OcctUnderSplitGuard.ExceedsBand"/>) leave
    /// undecided: does the common region of the two cells, near the piece, hold a ball of diameter greater than the
    /// band b?
    /// <para>
    /// Proof. For a point x inside both cells, the largest ball centred at x inside their common region has radius
    /// d(x), the distance from x to the union of the two cell boundaries. An overlap is a point x inside both cells
    /// with d(x) &gt; b/2. The region around the piece is covered by boxes (in a frame taken from the piece's own
    /// faces, so the result does not depend on the pose) and refined by branch and bound:
    /// </para>
    /// <list type="number">
    /// <item>Upper bound: d(x) is at most the distance f(x) from x to any convex subset of either boundary (a convex
    /// face, or an edge of a non-convex face), and at most w f(x) + (1 - w) g(x) for two such subsets and any w in
    /// [0, 1] (between two parallel faces t apart this is t/2 everywhere). These are convex functions of x, so their
    /// maximum over a box is reached at a corner; the smallest such corner maximum bounds d on the whole box, and so
    /// does d(centre) + half the box diagonal. A box bounded by b/2 holds no centre of a band-wide ball.</item>
    /// <item>Empty box: when the box, enlarged by b/8, meets no face of one cell (its centre is farther from that
    /// boundary than half the box diagonal plus b/8, or an exact box-polygon test per face), it lies inside or
    /// outside that cell as its centre does. Outside either cell: dropped.</item>
    /// <item>Witness: a box centre inside both cells with d &gt; b/2 proves the overlap.</item>
    /// </list>
    /// <para>
    /// The answer is true (witness found), false (every box bounded by b/2: no ball wider than the band) or null
    /// (a cell not closed at b/8, geometry not usable, or the work budget spent before the boxes were resolved).
    /// Null is a failure to decide, never a seam. Distances are exact for planar polygon faces; the inside test casts
    /// rays and discards any ray that passes within b/8 of a face edge, so it holds for closed cells whose faces leave
    /// seams narrower than that. The threshold b/2 carries a relative allowance of 1e-9 against rounding (a slab
    /// exactly b thick is a seam). The proof covers the region of the piece, enlarged by <c>margin</c>; it does not
    /// search the rest of the cells. How boxes are cut (at the planes of faces perpendicular to a frame axis and b/2
    /// either side of them, else at the middle) only affects the work, not the answer.
    /// </para>
    /// </summary>
    internal static class OcctGlueBandProof
    {
        /// <summary>Boxes processed per piece before giving up (null). Measured cost: see the branch record.</summary>
        internal const int DefaultBudget = 20000;

        // Corner indices on the 3 x 3 x 3 grid of a box (index 9 i + 3 j + k, each of i, j, k: 0 low, 1 mid, 2 high)
        // for the box itself (0) and for the two halves along each axis a (1 + 2a low half, 2 + 2a high half).
        private static readonly int[][] Corners = BuildCorners();

        // Ray directions for the inside test: generic (no axis or diagonal), tried in turn until one is unambiguous.
        private static readonly double[][] Directions = BuildDirections(16);

        private sealed class Facet
        {
            public Face3D Face;
            public int Cell;
            public double[] Origin;
            public double[] Normal;
            public double[] U;
            public double[] V;
            public List<double[]> Loops2D = new List<double[]>();
            public List<double[][]> Loops3D = new List<double[][]>();
            public bool Convex;
            public double[] Min;
            public double[] Max;
        }

        // A convex subset of a boundary: a convex facet, or a segment (A, B).
        private sealed class Element
        {
            public Facet Facet;
            public double[] A;
            public double[] B;
            public double[] Min;
            public double[] Max;
        }

        /// <summary>
        /// True when the common region of <paramref name="first"/> and <paramref name="second"/> holds a ball of
        /// diameter greater than the glue band within the region of <paramref name="piece"/> enlarged by
        /// <paramref name="margin"/>; false when it provably holds none there; null when it cannot be decided.
        /// </summary>
        internal static bool? Exceeds(Shell first, Shell second, Shell piece, double margin, OcctBuildOptions options, int budget = DefaultBudget)
        {
            options = options ?? new OcctBuildOptions();
            double half = OcctUnderSplitGuard.GlueBand(options) / 2;

            // The threshold radius for both answers: b/2 widened by a relative 1e-9 (5e-13 m at the 1 mm band), so that
            // a depth of exactly b/2 reached on a whole surface (a slab exactly b thick) is not lost to rounding.
            double limit = half * (1 + 1e-9);

            // Ray parity needs closed cells: a seam between faces wider than the b/8 ray margin could let a ray through.
            double clearance = half / 4;
            List<Facet> facets = new List<Facet>();
            if (first == null || second == null || piece == null || !first.IsClosed(clearance) || !second.IsClosed(clearance) || !AddFacets(first, 0, facets) || !AddFacets(second, 1, facets))
            {
                return null;
            }

            List<Facet> pieceFacets = new List<Facet>();
            if (!AddFacets(piece, 0, pieceFacets) || pieceFacets.Count == 0)
            {
                return null;
            }

            double[][] frame = Frame(piece, pieceFacets);
            double[] lo = new double[3];
            double[] hi = new double[3];
            for (int a = 0; a < 3; a++)
            {
                lo[a] = double.MaxValue;
                hi[a] = double.MinValue;
                foreach (double[] point in pieceFacets.SelectMany(x => x.Loops3D).SelectMany(x => x))
                {
                    double value = Dot(point, frame[a]);
                    lo[a] = System.Math.Min(lo[a], value);
                    hi[a] = System.Math.Max(hi[a], value);
                }

                lo[a] -= margin;
                hi[a] += margin;
            }

            List<Element> elements = Elements(facets);
            List<double[]> ranges = facets.Select(x => FrameRange(x, frame)).ToList();
            Stack<double[][]> boxes = new Stack<double[][]>();
            boxes.Push(new[] { lo, hi });
            int processed = 0;
            while (boxes.Count != 0)
            {
                if (++processed > budget)
                {
                    return null;
                }

                double[][] box = boxes.Pop();
                lo = box[0];
                hi = box[1];
                double[] centre = Point(frame, (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2);
                double halfDiagonal = 0.5 * System.Math.Sqrt(Square(hi[0] - lo[0]) + Square(hi[1] - lo[1]) + Square(hi[2] - lo[2]));
                double[] cellDepths = { BoundaryDistance(facets, 0, centre), BoundaryDistance(facets, 1, centre) };
                double depth = System.Math.Min(cellDepths[0], cellDepths[1]);
                if (double.IsNaN(depth))
                {
                    return null;
                }

                if (depth + halfDiagonal <= limit)
                {
                    continue;
                }

                if (depth > limit && InsideBoth(facets, centre, clearance) == true)
                {
                    return true;
                }

                if (Empty(facets, ranges, frame, lo, hi, clearance, cellDepths))
                {
                    continue;
                }

                double[] bounds = Bounds(elements, frame, lo, hi);
                if (bounds[0] <= limit)
                {
                    continue;
                }

                // How a box is cut only steers the work; every box is resolved by the rules above. First at the plane
                // of a face perpendicular to a frame axis that passes through the box, so that an arrangement of such
                // faces (slab and L seams in the piece's own frame) falls into its cells.
                if (FacePlaneSplit(facets, ranges, frame, lo, hi, half, out int planeAxis, out double plane))
                {
                    double[] belowHi = (double[])hi.Clone();
                    belowHi[planeAxis] = plane;
                    double[] aboveLo = (double[])lo.Clone();
                    aboveLo[planeAxis] = plane;
                    boxes.Push(new[] { lo, belowHi });
                    boxes.Push(new[] { aboveLo, hi });
                    continue;
                }

                // Otherwise at the middle: where most halves are then resolved (bounded or empty), else across the faces
                // that cross the box, else along its longest side. As boxes shrink, the bounds approach the true depth.
                int axis = SplitAxis(facets, ranges, frame, lo, hi, bounds, limit, clearance);

                double middle = (lo[axis] + hi[axis]) / 2;
                double[] lowHi = (double[])hi.Clone();
                lowHi[axis] = middle;
                double[] highLo = (double[])lo.Clone();
                highLo[axis] = middle;
                double[][] low = { lo, lowHi };
                double[][] high = { highLo, hi };
                double lowBound = bounds[1 + 2 * axis];
                double highBound = bounds[2 + 2 * axis];

                // The child with the larger bound is searched first: it is the likelier place for a witness.
                foreach (double[][] child in lowBound > highBound ? new[] { high, low } : new[] { low, high })
                {
                    if ((child == low ? lowBound : highBound) > limit)
                    {
                        boxes.Push(child);
                    }
                }
            }

            return false;
        }

        // True when the box holds no common point: it stays b/8 (clearance) clear of one cell's faces, so it cannot
        // reach into that cell through a seam between decoded faces either, and lies inside or outside that cell as
        // its centre does, which is outside. cellDepths: the centre's distance to each cell boundary, if known.
        private static bool Empty(List<Facet> facets, List<double[]> ranges, double[][] frame, double[] lo, double[] hi, double clearance, double[] cellDepths)
        {
            double[] centre = Point(frame, (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2);
            double halfDiagonal = 0.5 * System.Math.Sqrt(Square(hi[0] - lo[0]) + Square(hi[1] - lo[1]) + Square(hi[2] - lo[2]));
            for (int cell = 0; cell < 2; cell++)
            {
                double depth = cellDepths?[cell] ?? BoundaryDistance(facets, cell, centre);
                if ((depth > halfDiagonal + clearance || Clear(facets, cell, ranges, frame, lo, hi, clearance)) && Inside(facets, cell, centre, clearance) == false)
                {
                    return true;
                }
            }

            return false;
        }

        // The largest corner distance minimised over the elements, for the box and for each half along each axis.
        private static double[] Bounds(List<Element> elements, double[][] frame, double[] lo, double[] hi)
        {
            double[][] grid = new double[27][];
            double[] boxMin = { double.MaxValue, double.MaxValue, double.MaxValue };
            double[] boxMax = { double.MinValue, double.MinValue, double.MinValue };
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        double[] point = Point(frame, Lerp(lo[0], hi[0], i), Lerp(lo[1], hi[1], j), Lerp(lo[2], hi[2], k));
                        grid[9 * i + 3 * j + k] = point;
                        for (int a = 0; a < 3; a++)
                        {
                            boxMin[a] = System.Math.Min(boxMin[a], point[a]);
                            boxMax[a] = System.Math.Max(boxMax[a], point[a]);
                        }
                    }
                }
            }

            double[] lowerBounds = elements.Select(x => BoxDistance(boxMin, boxMax, x.Min, x.Max)).ToArray();
            int[] order = Enumerable.Range(0, elements.Count).OrderBy(x => lowerBounds[x]).ToArray();
            double[] bounds = Enumerable.Repeat(double.MaxValue, Corners.Length).ToArray();
            List<double[]> nearest = new List<double[]>();
            foreach (int index in order)
            {
                // No corner of the box (or of a half) is nearer the element than the box itself. The nearest few
                // elements are kept for the pair bound below.
                if (lowerBounds[index] >= bounds.Max() && nearest.Count >= PairElements)
                {
                    break;
                }

                Element element = elements[index];
                double[] distances = new double[27];
                for (int p = 0; p < 27; p++)
                {
                    distances[p] = element.Facet != null ? FacetDistance(element.Facet, grid[p]) : SegmentDistance(element.A, element.B, grid[p]);
                }

                if (nearest.Count < PairElements)
                {
                    nearest.Add(distances);
                }

                for (int o = 0; o < Corners.Length; o++)
                {
                    double largest = 0;
                    foreach (int p in Corners[o])
                    {
                        largest = System.Math.Max(largest, distances[p]);
                    }

                    bounds[o] = System.Math.Min(bounds[o], largest);
                }
            }

            // Pair bound: min(f, g) <= w f + (1 - w) g for any w in [0, 1], again convex, so its largest corner value
            // bounds d on the box. Between two parallel faces at distance t it gives t/2 everywhere.
            for (int i = 0; i < nearest.Count - 1; i++)
            {
                for (int j = i + 1; j < nearest.Count; j++)
                {
                    for (int o = 0; o < Corners.Length; o++)
                    {
                        bounds[o] = System.Math.Min(bounds[o], PairBound(nearest[i], nearest[j], Corners[o]));
                    }
                }
            }

            return bounds;
        }

        // Elements combined pairwise in the pair bound (the nearest to the box).
        private const int PairElements = 6;

        // min over w in [0, 1] of the largest corner value of w f + (1 - w) g: a convex piecewise-linear function of
        // w, so its minimum is at w = 0, w = 1 or where two corner lines cross.
        private static double PairBound(double[] f, double[] g, int[] corners)
        {
            List<double> weights = new List<double> { 0, 1 };
            for (int i = 0; i < corners.Length - 1; i++)
            {
                for (int j = i + 1; j < corners.Length; j++)
                {
                    double slope = (f[corners[i]] - g[corners[i]]) - (f[corners[j]] - g[corners[j]]);
                    if (slope != 0)
                    {
                        double w = (g[corners[j]] - g[corners[i]]) / slope;
                        if (w > 0 && w < 1)
                        {
                            weights.Add(w);
                        }
                    }
                }
            }

            double result = double.MaxValue;
            foreach (double w in weights)
            {
                double largest = 0;
                foreach (int p in corners)
                {
                    largest = System.Math.Max(largest, w * f[p] + (1 - w) * g[p]);
                }

                result = System.Math.Min(result, largest);
            }

            return result;
        }

        // True when the box (frame coordinates lo..hi), enlarged by clearance along each axis (which contains every
        // point within clearance of the box), meets no facet of the cell.
        private static bool Clear(List<Facet> facets, int cell, List<double[]> ranges, double[][] frame, double[] lo, double[] hi, double clearance)
        {
            double[] min = { lo[0] - clearance, lo[1] - clearance, lo[2] - clearance };
            double[] max = { hi[0] + clearance, hi[1] + clearance, hi[2] + clearance };
            double[] centre = Point(frame, (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2);
            for (int f = 0; f < facets.Count; f++)
            {
                if (facets[f].Cell == cell && Crosses(facets[f], ranges[f], frame, centre, min, max))
                {
                    return false;
                }
            }

            return true;
        }

        // A face perpendicular to a frame axis (its vertices within 1e-9 m of one plane) that meets the box enlarged by
        // b/2: its plane, or a plane b/2 to either side of it (where the layer bounded by the face ends and the
        // region beyond can be dropped), passing through the box interior (not within a millionth of the box extent
        // of its sides). The candidate nearest the box middle.
        private static bool FacePlaneSplit(List<Facet> facets, List<double[]> ranges, double[][] frame, double[] lo, double[] hi, double half, out int axis, out double plane)
        {
            axis = -1;
            plane = double.NaN;
            double best = double.MaxValue;
            double[] centre = Point(frame, (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2);
            double[] min = { lo[0] - half, lo[1] - half, lo[2] - half };
            double[] max = { hi[0] + half, hi[1] + half, hi[2] + half };
            for (int f = 0; f < facets.Count; f++)
            {
                for (int a = 0; a < 3; a++)
                {
                    double extent = hi[a] - lo[a];
                    double[] range = ranges[f];
                    if (range[2 * a + 1] - range[2 * a] > 1e-9 || System.Math.Abs(Dot(facets[f].Normal, frame[a])) < 1 - 1e-12)
                    {
                        continue;
                    }

                    double value = (range[2 * a] + range[2 * a + 1]) / 2;
                    foreach (double candidate in new[] { value, value - half, value + half })
                    {
                        double offset = System.Math.Abs(candidate - (lo[a] + hi[a]) / 2) / extent;
                        if (candidate - lo[a] > 1e-6 * extent && hi[a] - candidate > 1e-6 * extent && offset < best && Crosses(facets[f], range, frame, centre, min, max))
                        {
                            best = offset;
                            axis = a;
                            plane = candidate;
                        }
                    }
                }
            }

            return axis >= 0;
        }

        // The axis to split when the bounds do not discriminate: across the faces that cross the box (largest extent
        // times normal component), so that a box beside a wall becomes a plate and an extrusion is not cut along its
        // length; the longest axis when no face crosses the box.
        private static int SplitAxis(List<Facet> facets, List<double[]> ranges, double[][] frame, double[] lo, double[] hi, double[] bounds, double limit, double clearance)
        {
            // One step ahead: the halves resolved by the rules of Exceeds (bounded by the limit, or empty).
            int[] resolved = new int[3];
            for (int a = 0; a < 3; a++)
            {
                double[] lowerHi = (double[])hi.Clone();
                lowerHi[a] = (lo[a] + hi[a]) / 2;
                double[] upperLo = (double[])lo.Clone();
                upperLo[a] = lowerHi[a];
                resolved[a] = (bounds[1 + 2 * a] <= limit || Empty(facets, ranges, frame, lo, lowerHi, clearance, null) ? 1 : 0)
                    + (bounds[2 + 2 * a] <= limit || Empty(facets, ranges, frame, upperLo, hi, clearance, null) ? 1 : 0);
            }

            double[] weights = new double[3];
            double[] centre = Point(frame, (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, (lo[2] + hi[2]) / 2);
            for (int f = 0; f < facets.Count; f++)
            {
                Facet facet = facets[f];
                double[] components = { System.Math.Abs(Dot(facet.Normal, frame[0])), System.Math.Abs(Dot(facet.Normal, frame[1])), System.Math.Abs(Dot(facet.Normal, frame[2])) };
                if (Enumerable.Range(0, 3).All(a => components[a] <= weights[a]) || !Crosses(facet, ranges[f], frame, centre, lo, hi))
                {
                    continue;
                }

                for (int a = 0; a < 3; a++)
                {
                    weights[a] = System.Math.Max(weights[a], components[a]);
                }
            }

            // Only sides at least a tenth of the longest compete, so that a vanishing side is never cut again.
            double longest = System.Math.Max(hi[0] - lo[0], System.Math.Max(hi[1] - lo[1], hi[2] - lo[2]));
            double[] scores = Enumerable.Range(0, 3).Select(a => hi[a] - lo[a] >= 0.1 * longest ? weights[a] * (hi[a] - lo[a]) : 0).ToArray();
            int axis = 0;
            for (int a = 1; a < 3; a++)
            {
                if (resolved[a] > resolved[axis] || (resolved[a] == resolved[axis] && (scores[a] > scores[axis] || (scores[a] == scores[axis] && hi[a] - lo[a] > hi[axis] - lo[axis]))))
                {
                    axis = a;
                }
            }

            return axis;
        }

        // Whether the facet meets the box min..max (frame coordinates). No: its vertices (so the facet, in their
        // convex hull) lie beyond a box face along a frame axis, or the box lies on one side of the facet plane, or
        // (exact) the section of the box by the plane shares no point with the facet polygon.
        private static bool Crosses(Facet facet, double[] range, double[][] frame, double[] centre, double[] min, double[] max)
        {
            for (int a = 0; a < 3; a++)
            {
                if (range[2 * a + 1] < min[a] || range[2 * a] > max[a])
                {
                    return false;
                }
            }

            double radius = 0;
            for (int a = 0; a < 3; a++)
            {
                radius += System.Math.Abs(Dot(facet.Normal, frame[a])) * (max[a] - min[a]) / 2;
            }

            return System.Math.Abs(Dot(Subtract(centre, facet.Origin), facet.Normal)) <= radius && SectionMeetsFacet(facet, frame, min, max);
        }

        // Whether the section of the box min..max (frame coordinates) by the facet plane meets the facet polygon.
        // In plane coordinates (s, t) the section is the convex polygon of the six constraints
        // min[a] <= e_a.origin + s e_a.U + t e_a.V <= max[a]. They meet when a facet edge crosses the section, or
        // the section lies inside the facet (a section point inside the polygon).
        private static bool SectionMeetsFacet(Facet facet, double[][] frame, double[] min, double[] max)
        {
            // Half-planes p s + q t <= r.
            List<double[]> halfPlanes = new List<double[]>();
            for (int a = 0; a < 3; a++)
            {
                double p = Dot(frame[a], facet.U);
                double q = Dot(frame[a], facet.V);
                double offset = Dot(frame[a], facet.Origin);
                halfPlanes.Add(new[] { p, q, max[a] - offset });
                halfPlanes.Add(new[] { -p, -q, offset - min[a] });
            }

            foreach (double[] loop in facet.Loops2D)
            {
                int count = loop.Length / 2;
                for (int i = 0, j = count - 1; i < count; j = i++)
                {
                    if (SegmentMeetsHalfPlanes(halfPlanes, loop[2 * j], loop[2 * j + 1], loop[2 * i], loop[2 * i + 1]))
                    {
                        return true;
                    }
                }
            }

            // No edge crosses the section: it lies wholly inside or wholly outside the facet. Test one point of it,
            // a vertex of the section polygon (the projected box corners' bounding rectangle clipped by the six
            // half-planes).
            List<double[]> polygon = SectionPolygon(facet, frame, min, max, halfPlanes);
            return polygon.Count != 0 && Inside2D(facet, polygon.Average(x => x[0]), polygon.Average(x => x[1]));
        }

        // Liang-Barsky: whether any part of the segment satisfies every half-plane.
        private static bool SegmentMeetsHalfPlanes(List<double[]> halfPlanes, double x0, double y0, double x1, double y1)
        {
            double enter = 0;
            double exit = 1;
            foreach (double[] halfPlane in halfPlanes)
            {
                double start = halfPlane[0] * x0 + halfPlane[1] * y0 - halfPlane[2];
                double change = halfPlane[0] * (x1 - x0) + halfPlane[1] * (y1 - y0);
                if (change == 0)
                {
                    if (start > 0)
                    {
                        return false;
                    }

                    continue;
                }

                double t = -start / change;
                if (change < 0)
                {
                    enter = System.Math.Max(enter, t);
                }
                else
                {
                    exit = System.Math.Min(exit, t);
                }

                if (enter > exit)
                {
                    return false;
                }
            }

            return true;
        }

        private static List<double[]> SectionPolygon(Facet facet, double[][] frame, double[] min, double[] max, List<double[]> halfPlanes)
        {
            double sMin = double.MaxValue, sMax = double.MinValue, tMin = double.MaxValue, tMax = double.MinValue;
            for (int corner = 0; corner < 8; corner++)
            {
                double[] point = Point(frame, (corner & 1) == 0 ? min[0] : max[0], (corner & 2) == 0 ? min[1] : max[1], (corner & 4) == 0 ? min[2] : max[2]);
                double[] relative = Subtract(point, facet.Origin);
                double s = Dot(relative, facet.U);
                double t = Dot(relative, facet.V);
                sMin = System.Math.Min(sMin, s);
                sMax = System.Math.Max(sMax, s);
                tMin = System.Math.Min(tMin, t);
                tMax = System.Math.Max(tMax, t);
            }

            List<double[]> polygon = new List<double[]> { new[] { sMin, tMin }, new[] { sMax, tMin }, new[] { sMax, tMax }, new[] { sMin, tMax } };
            foreach (double[] halfPlane in halfPlanes)
            {
                List<double[]> clipped = new List<double[]>();
                for (int i = 0; i < polygon.Count; i++)
                {
                    double[] current = polygon[i];
                    double[] next = polygon[(i + 1) % polygon.Count];
                    double currentValue = halfPlane[0] * current[0] + halfPlane[1] * current[1] - halfPlane[2];
                    double nextValue = halfPlane[0] * next[0] + halfPlane[1] * next[1] - halfPlane[2];
                    if (currentValue <= 0)
                    {
                        clipped.Add(current);
                    }

                    if ((currentValue <= 0) != (nextValue <= 0))
                    {
                        double t = currentValue / (currentValue - nextValue);
                        clipped.Add(new[] { current[0] + t * (next[0] - current[0]), current[1] + t * (next[1] - current[1]) });
                    }
                }

                polygon = clipped;
                if (polygon.Count == 0)
                {
                    break;
                }
            }

            return polygon;
        }

        // The extent of a facet's vertices along each frame axis: min and max per axis.
        private static double[] FrameRange(Facet facet, double[][] frame)
        {
            double[] range = new double[6];
            for (int a = 0; a < 3; a++)
            {
                range[2 * a] = double.MaxValue;
                range[2 * a + 1] = double.MinValue;
                foreach (double[] point in facet.Loops3D.SelectMany(x => x))
                {
                    double value = Dot(point, frame[a]);
                    range[2 * a] = System.Math.Min(range[2 * a], value);
                    range[2 * a + 1] = System.Math.Max(range[2 * a + 1], value);
                }
            }

            return range;
        }

        // Exact distance from point to the boundary of one cell (its facets, planar polygons), or NaN.
        private static double BoundaryDistance(List<Facet> facets, int cell, double[] point)
        {
            double result = double.MaxValue;
            foreach (Facet facet in facets)
            {
                if (facet.Cell != cell || BoxDistance(point, point, facet.Min, facet.Max) >= result)
                {
                    continue;
                }

                result = System.Math.Min(result, FacetDistance(facet, point));
            }

            return result == double.MaxValue ? double.NaN : result;
        }

        // False when point is outside either cell, true when inside both, null when a cell's test is ambiguous.
        private static bool? InsideBoth(List<Facet> facets, double[] point, double margin)
        {
            bool? first = Inside(facets, 0, point, margin);
            if (first == false)
            {
                return false;
            }

            bool? second = Inside(facets, 1, point, margin);
            if (second == false)
            {
                return false;
            }

            return first == true && second == true ? (bool?)true : null;
        }

        // Ray parity against one cell's facets. A ray that runs in a facet plane or crosses a facet within margin of
        // its edges is discarded (also catches seams between decoded faces narrower than margin); null when every
        // direction is discarded.
        private static bool? Inside(List<Facet> facets, int cell, double[] point, double margin)
        {
            foreach (double[] direction in Directions)
            {
                int crossings = 0;
                bool ambiguous = false;
                foreach (Facet facet in facets)
                {
                    if (facet.Cell != cell)
                    {
                        continue;
                    }

                    double offset = Dot(Subtract(point, facet.Origin), facet.Normal);
                    double cosine = Dot(direction, facet.Normal);
                    if (System.Math.Abs(cosine) < 1e-9)
                    {
                        if (System.Math.Abs(offset) <= margin)
                        {
                            ambiguous = true;
                            break;
                        }

                        continue;
                    }

                    double t = -offset / cosine;
                    if (t <= 0)
                    {
                        continue;
                    }

                    double[] hit = { point[0] + t * direction[0], point[1] + t * direction[1], point[2] + t * direction[2] };
                    double[] relative = Subtract(hit, facet.Origin);
                    double x = Dot(relative, facet.U);
                    double y = Dot(relative, facet.V);
                    if (EdgeDistance2D(facet, x, y) <= margin)
                    {
                        ambiguous = true;
                        break;
                    }

                    if (Inside2D(facet, x, y))
                    {
                        crossings++;
                    }
                }

                if (!ambiguous)
                {
                    return crossings % 2 == 1;
                }
            }

            return null;
        }

        // Exact distance from point to a planar polygon facet (with holes).
        private static double FacetDistance(Facet facet, double[] point)
        {
            double[] relative = Subtract(point, facet.Origin);
            double height = Dot(relative, facet.Normal);
            double x = Dot(relative, facet.U);
            double y = Dot(relative, facet.V);
            if (Inside2D(facet, x, y))
            {
                return System.Math.Abs(height);
            }

            double edge = EdgeDistance2D(facet, x, y);
            return System.Math.Sqrt(height * height + edge * edge);
        }

        // Even-odd test over all loops of the facet, in its plane.
        private static bool Inside2D(Facet facet, double x, double y)
        {
            bool inside = false;
            foreach (double[] loop in facet.Loops2D)
            {
                int count = loop.Length / 2;
                for (int i = 0, j = count - 1; i < count; j = i++)
                {
                    double xi = loop[2 * i], yi = loop[2 * i + 1], xj = loop[2 * j], yj = loop[2 * j + 1];
                    if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                    {
                        inside = !inside;
                    }
                }
            }

            return inside;
        }

        private static double EdgeDistance2D(Facet facet, double x, double y)
        {
            double result = double.MaxValue;
            foreach (double[] loop in facet.Loops2D)
            {
                int count = loop.Length / 2;
                for (int i = 0, j = count - 1; i < count; j = i++)
                {
                    double ax = loop[2 * j], ay = loop[2 * j + 1], bx = loop[2 * i], by = loop[2 * i + 1];
                    double dx = bx - ax, dy = by - ay;
                    double lengthSquared = dx * dx + dy * dy;
                    double t = lengthSquared > 0 ? System.Math.Max(0, System.Math.Min(1, ((x - ax) * dx + (y - ay) * dy) / lengthSquared)) : 0;
                    double ex = ax + t * dx - x, ey = ay + t * dy - y;
                    result = System.Math.Min(result, System.Math.Sqrt(ex * ex + ey * ey));
                }
            }

            return result;
        }

        private static double SegmentDistance(double[] a, double[] b, double[] point)
        {
            double[] ab = Subtract(b, a);
            double lengthSquared = Dot(ab, ab);
            double t = lengthSquared > 0 ? System.Math.Max(0, System.Math.Min(1, Dot(Subtract(point, a), ab) / lengthSquared)) : 0;
            double[] closest = { a[0] + t * ab[0], a[1] + t * ab[1], a[2] + t * ab[2] };
            double[] difference = Subtract(point, closest);
            return System.Math.Sqrt(Dot(difference, difference));
        }

        // Distance between two axis-aligned boxes (0 when they meet).
        private static double BoxDistance(double[] minA, double[] maxA, double[] minB, double[] maxB)
        {
            double sum = 0;
            for (int a = 0; a < 3; a++)
            {
                double gap = System.Math.Max(0, System.Math.Max(minB[a] - maxA[a], minA[a] - maxB[a]));
                sum += gap * gap;
            }

            return System.Math.Sqrt(sum);
        }

        // Each face as a planar polygon in its own plane; false when a face is not a usable planar polygon.
        private static bool AddFacets(Shell shell, int cell, List<Facet> facets)
        {
            foreach (Face3D face3D in shell.Face3Ds ?? new List<Face3D>())
            {
                Plane plane = face3D?.GetPlane();
                Vector3D normal = plane?.Normal;
                double length = normal == null ? 0 : System.Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
                if (plane?.Origin == null || !(length > 0))
                {
                    return false;
                }

                Facet facet = new Facet { Face = face3D, Cell = cell, Origin = new[] { plane.Origin.X, plane.Origin.Y, plane.Origin.Z }, Normal = new[] { normal.X / length, normal.Y / length, normal.Z / length } };
                facet.U = Perpendicular(facet.Normal);
                facet.V = Cross(facet.Normal, facet.U);
                List<IClosedPlanar3D> loops = new List<IClosedPlanar3D> { face3D.GetExternalEdge3D() };
                loops.AddRange(face3D.GetInternalEdge3Ds() ?? new List<IClosedPlanar3D>());
                foreach (IClosedPlanar3D loop in loops)
                {
                    List<Point3D> point3Ds = (loop as ISegmentable3D)?.GetPoints();
                    if (point3Ds == null || point3Ds.Count < 3)
                    {
                        return false;
                    }

                    double[] loop2D = new double[2 * point3Ds.Count];
                    double[][] loop3D = new double[point3Ds.Count][];
                    for (int i = 0; i < point3Ds.Count; i++)
                    {
                        double[] relative = { point3Ds[i].X - facet.Origin[0], point3Ds[i].Y - facet.Origin[1], point3Ds[i].Z - facet.Origin[2] };
                        loop2D[2 * i] = Dot(relative, facet.U);
                        loop2D[2 * i + 1] = Dot(relative, facet.V);

                        // The vertex projected into the plane: the facet is exactly the planar polygon.
                        loop3D[i] = Point(new[] { facet.U, facet.V, facet.Normal }, loop2D[2 * i], loop2D[2 * i + 1], 0);
                        loop3D[i] = new[] { loop3D[i][0] + facet.Origin[0], loop3D[i][1] + facet.Origin[1], loop3D[i][2] + facet.Origin[2] };
                    }

                    facet.Loops2D.Add(loop2D);
                    facet.Loops3D.Add(loop3D);
                }

                facet.Convex = facet.Loops2D.Count == 1 && IsConvex(facet.Loops2D[0]);
                facet.Min = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
                facet.Max = new[] { double.MinValue, double.MinValue, double.MinValue };
                foreach (double[] point in facet.Loops3D.SelectMany(x => x))
                {
                    for (int a = 0; a < 3; a++)
                    {
                        facet.Min[a] = System.Math.Min(facet.Min[a], point[a]);
                        facet.Max[a] = System.Math.Max(facet.Max[a], point[a]);
                    }
                }

                facets.Add(facet);
            }

            return true;
        }

        // Convex facets as they are; a non-convex facet (or one with holes) through its edges and its verified
        // triangles (see VerifiedTriangles).
        private static List<Element> Elements(List<Facet> facets)
        {
            List<Element> elements = new List<Element>();
            foreach (Facet facet in facets)
            {
                if (facet.Convex)
                {
                    elements.Add(new Element { Facet = facet, Min = facet.Min, Max = facet.Max });
                    continue;
                }

                elements.AddRange(VerifiedTriangles(facet).Select(x => new Element { Facet = x, Min = x.Min, Max = x.Max }));
                foreach (double[][] loop in facet.Loops3D)
                {
                    for (int i = 0, j = loop.Length - 1; i < loop.Length; j = i++)
                    {
                        double[] min = new double[3];
                        double[] max = new double[3];
                        for (int a = 0; a < 3; a++)
                        {
                            min[a] = System.Math.Min(loop[j][a], loop[i][a]);
                            max[a] = System.Math.Max(loop[j][a], loop[i][a]);
                        }

                        elements.Add(new Element { A = loop[j], B = loop[i], Min = min, Max = max });
                    }
                }
            }

            return elements;
        }

        // Convex pieces of a non-convex facet for the upper bound, which needs subsets of the boundary: SAM's
        // triangulation of the face, each triangle shrunk by 1e-9 m towards its centroid and kept only when its
        // centroid lies inside the facet and no facet edge meets it (its interior then lies inside the facet). A
        // triangulation error can only drop triangles (a weaker bound), never add a point off the boundary.
        private static List<Facet> VerifiedTriangles(Facet facet)
        {
            List<Facet> result = new List<Facet>();
            List<Triangle3D> triangle3Ds;
            try
            {
                triangle3Ds = facet.Face?.Triangulate(1e-9);
            }
            catch (System.Exception)
            {
                triangle3Ds = null;
            }

            foreach (Triangle3D triangle3D in triangle3Ds ?? new List<Triangle3D>())
            {
                List<Point3D> point3Ds = (triangle3D as ISegmentable3D)?.GetPoints();
                if (point3Ds == null || point3Ds.Count != 3)
                {
                    continue;
                }

                double[] loop = new double[6];
                for (int i = 0; i < 3; i++)
                {
                    double[] relative = { point3Ds[i].X - facet.Origin[0], point3Ds[i].Y - facet.Origin[1], point3Ds[i].Z - facet.Origin[2] };
                    loop[2 * i] = Dot(relative, facet.U);
                    loop[2 * i + 1] = Dot(relative, facet.V);
                }

                double cx = (loop[0] + loop[2] + loop[4]) / 3;
                double cy = (loop[1] + loop[3] + loop[5]) / 3;
                bool usable = true;
                for (int i = 0; i < 3 && usable; i++)
                {
                    double dx = cx - loop[2 * i];
                    double dy = cy - loop[2 * i + 1];
                    double length = System.Math.Sqrt(dx * dx + dy * dy);
                    usable = length > 2e-9;
                    if (usable)
                    {
                        loop[2 * i] += dx * 1e-9 / length;
                        loop[2 * i + 1] += dy * 1e-9 / length;
                    }
                }

                if (!usable || Cross2D(loop, 0, 1, 2) == 0 || !Inside2D(facet, cx, cy) || facet.Loops2D.Any(x => LoopMeetsTriangle(x, loop)))
                {
                    continue;
                }

                Facet triangle = new Facet { Face = null, Cell = facet.Cell, Origin = facet.Origin, Normal = facet.Normal, U = facet.U, V = facet.V, Convex = true };
                triangle.Loops2D.Add(loop);
                double[][] loop3D = new double[3][];
                for (int i = 0; i < 3; i++)
                {
                    loop3D[i] = new[]
                    {
                        facet.Origin[0] + loop[2 * i] * facet.U[0] + loop[2 * i + 1] * facet.V[0],
                        facet.Origin[1] + loop[2 * i] * facet.U[1] + loop[2 * i + 1] * facet.V[1],
                        facet.Origin[2] + loop[2 * i] * facet.U[2] + loop[2 * i + 1] * facet.V[2],
                    };
                }

                triangle.Loops3D.Add(loop3D);
                triangle.Min = Enumerable.Range(0, 3).Select(a => loop3D.Min(x => x[a])).ToArray();
                triangle.Max = Enumerable.Range(0, 3).Select(a => loop3D.Max(x => x[a])).ToArray();
                result.Add(triangle);
            }

            return result;
        }

        // Twice the signed area of the triangle (i, j, k) of a 2D loop.
        private static double Cross2D(double[] loop, int i, int j, int k)
        {
            return (loop[2 * j] - loop[2 * i]) * (loop[2 * k + 1] - loop[2 * i + 1]) - (loop[2 * j + 1] - loop[2 * i + 1]) * (loop[2 * k] - loop[2 * i]);
        }

        // Whether an edge of the closed loop meets the closed triangle (both 2D): an edge end inside the triangle,
        // or an edge crossing or touching a triangle side.
        private static bool LoopMeetsTriangle(double[] loop, double[] triangle)
        {
            int count = loop.Length / 2;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                double[] edge = { loop[2 * j], loop[2 * j + 1], loop[2 * i], loop[2 * i + 1] };
                if (InTriangle(triangle, edge[0], edge[1]) || InTriangle(triangle, edge[2], edge[3]))
                {
                    return true;
                }

                for (int s = 0; s < 3; s++)
                {
                    int t = (s + 1) % 3;
                    if (SegmentsMeet(edge, new[] { triangle[2 * s], triangle[2 * s + 1], triangle[2 * t], triangle[2 * t + 1] }))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool InTriangle(double[] triangle, double x, double y)
        {
            double[] points = { triangle[0], triangle[1], triangle[2], triangle[3], triangle[4], triangle[5], x, y };
            double a = Cross2D(points, 0, 1, 3);
            double b = Cross2D(points, 1, 2, 3);
            double c = Cross2D(points, 2, 0, 3);
            return (a >= 0 && b >= 0 && c >= 0) || (a <= 0 && b <= 0 && c <= 0);
        }

        // Closed segments (x0, y0, x1, y1) meet, collinear overlaps included.
        private static bool SegmentsMeet(double[] p, double[] q)
        {
            double[] points = { p[0], p[1], p[2], p[3], q[0], q[1], q[2], q[3] };
            double d1 = Cross2D(points, 2, 3, 0);
            double d2 = Cross2D(points, 2, 3, 1);
            double d3 = Cross2D(points, 0, 1, 2);
            double d4 = Cross2D(points, 0, 1, 3);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            {
                return true;
            }

            return (d1 == 0 && OnSegment(q, p[0], p[1])) || (d2 == 0 && OnSegment(q, p[2], p[3])) || (d3 == 0 && OnSegment(p, q[0], q[1])) || (d4 == 0 && OnSegment(p, q[2], q[3]));
        }

        private static bool OnSegment(double[] segment, double x, double y)
        {
            return x >= System.Math.Min(segment[0], segment[2]) && x <= System.Math.Max(segment[0], segment[2]) && y >= System.Math.Min(segment[1], segment[3]) && y <= System.Math.Max(segment[1], segment[3]);
        }

        // A simple polygon whose turns all have the sign of its area and add up to one full turn.
        private static bool IsConvex(double[] loop)
        {
            int count = loop.Length / 2;
            double area = 0;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                area += loop[2 * j] * loop[2 * i + 1] - loop[2 * i] * loop[2 * j + 1];
            }

            if (count < 3 || area == 0)
            {
                return false;
            }

            double sign = System.Math.Sign(area);
            double turning = 0;
            for (int i = 0; i < count; i++)
            {
                int previous = (i + count - 1) % count;
                int next = (i + 1) % count;
                double ex = loop[2 * i] - loop[2 * previous], ey = loop[2 * i + 1] - loop[2 * previous + 1];
                double fx = loop[2 * next] - loop[2 * i], fy = loop[2 * next + 1] - loop[2 * i + 1];
                double cross = ex * fy - ey * fx;
                double dot = ex * fx + ey * fy;
                if (cross * sign < 0)
                {
                    return false;
                }

                turning += System.Math.Atan2(cross, dot);
            }

            return System.Math.Abs(turning - sign * 2 * System.Math.PI) < 1e-6;
        }

        // An orthonormal frame from the piece's faces: the normal of the largest face, then the largest face normal
        // not nearly parallel to it, made orthogonal (a coordinate axis if there is none).
        private static double[][] Frame(Shell piece, List<Facet> pieceFacets)
        {
            List<double> areas = piece.Face3Ds.Select(x => x?.GetArea() ?? 0).ToList();
            int[] order = Enumerable.Range(0, pieceFacets.Count).OrderByDescending(x => areas[x]).ToArray();
            double[] first = pieceFacets[order[0]].Normal;
            double[] second = null;
            foreach (int index in order)
            {
                double[] normal = pieceFacets[index].Normal;
                if (System.Math.Abs(Dot(normal, first)) < 0.9)
                {
                    second = normal;
                    break;
                }
            }

            second = second ?? Perpendicular(first);
            double projection = Dot(second, first);
            second = Normalize(new[] { second[0] - projection * first[0], second[1] - projection * first[1], second[2] - projection * first[2] });
            return new[] { first, second, Cross(first, second) };
        }

        private static double[] Perpendicular(double[] normal)
        {
            double ax = System.Math.Abs(normal[0]), ay = System.Math.Abs(normal[1]), az = System.Math.Abs(normal[2]);
            double[] axis = ax <= ay && ax <= az ? new double[] { 1, 0, 0 } : ay <= az ? new double[] { 0, 1, 0 } : new double[] { 0, 0, 1 };
            return Normalize(Cross(normal, axis));
        }

        private static int[][] BuildCorners()
        {
            List<int[]> corners = new List<int[]>();
            int[][] full = { new[] { 0, 2 }, new[] { 0, 2 }, new[] { 0, 2 } };
            corners.Add(CornerIndices(full));
            for (int a = 0; a < 3; a++)
            {
                foreach (int[] range in new[] { new[] { 0, 1 }, new[] { 1, 2 } })
                {
                    int[][] ranges = (int[][])full.Clone();
                    ranges[a] = range;
                    corners.Add(CornerIndices(ranges));
                }
            }

            return corners.ToArray();
        }

        private static int[] CornerIndices(int[][] ranges)
        {
            List<int> indices = new List<int>();
            foreach (int i in ranges[0])
            {
                foreach (int j in ranges[1])
                {
                    foreach (int k in ranges[2])
                    {
                        indices.Add(9 * i + 3 * j + k);
                    }
                }
            }

            return indices.ToArray();
        }

        // Points of a Fibonacci sphere, a fixed set of well-spread generic directions.
        private static double[][] BuildDirections(int count)
        {
            double[][] directions = new double[count][];
            double golden = System.Math.PI * (3 - System.Math.Sqrt(5));
            for (int i = 0; i < count; i++)
            {
                double z = 1 - (2 * i + 1.0) / count;
                double radius = System.Math.Sqrt(1 - z * z);
                double angle = golden * i + 0.1234;
                directions[i] = new[] { radius * System.Math.Cos(angle), radius * System.Math.Sin(angle), z };
            }

            return directions;
        }

        private static double[] Point(double[][] frame, double a, double b, double c)
        {
            return new[]
            {
                a * frame[0][0] + b * frame[1][0] + c * frame[2][0],
                a * frame[0][1] + b * frame[1][1] + c * frame[2][1],
                a * frame[0][2] + b * frame[1][2] + c * frame[2][2],
            };
        }

        private static double Lerp(double lo, double hi, int step)
        {
            return step == 0 ? lo : step == 2 ? hi : (lo + hi) / 2;
        }

        private static double Square(double value)
        {
            return value * value;
        }

        private static double Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        private static double[] Subtract(double[] a, double[] b)
        {
            return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        }

        private static double[] Cross(double[] a, double[] b)
        {
            return new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        }

        private static double[] Normalize(double[] a)
        {
            double length = System.Math.Sqrt(Dot(a, a));
            return new[] { a[0] / length, a[1] / length, a[2] / length };
        }
    }
}
