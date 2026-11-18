// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Set operations on stretches of a wall's plan line (closed intervals of the plan parameter u). Results are
    /// normalised (sorted, overlapping or touching stretches merged), so they depend only on the set of points covered,
    /// never on how that set was split into pieces or how often a piece was listed.
    /// </summary>
    internal static class PlanIntervals
    {
        /// <summary>The union of <paramref name="intervals"/>, sorted; stretches within <paramref name="tolerance"/> of
        /// each other are merged and stretches no longer than <paramref name="tolerance"/> dropped.</summary>
        public static List<(double From, double To)> Normalise(IEnumerable<(double From, double To)> intervals, double tolerance)
        {
            List<(double From, double To)> result = new List<(double, double)>();
            if (intervals == null)
            {
                return result;
            }

            foreach ((double from, double to) in intervals.Where(x => x.To - x.From > tolerance).OrderBy(x => x.From))
            {
                if (result.Count != 0 && from <= result[result.Count - 1].To + tolerance)
                {
                    result[result.Count - 1] = (result[result.Count - 1].From, System.Math.Max(result[result.Count - 1].To, to));
                    continue;
                }

                result.Add((from, to));
            }

            return result;
        }

        public static List<(double From, double To)> Union(IEnumerable<(double From, double To)> a, IEnumerable<(double From, double To)> b, double tolerance)
        {
            return Normalise((a ?? Enumerable.Empty<(double, double)>()).Concat(b ?? Enumerable.Empty<(double, double)>()), tolerance);
        }

        public static List<(double From, double To)> Intersection(IEnumerable<(double From, double To)> a, IEnumerable<(double From, double To)> b, double tolerance)
        {
            List<(double From, double To)> left = Normalise(a, tolerance), right = Normalise(b, tolerance);
            List<(double From, double To)> result = new List<(double, double)>();
            foreach ((double From, double To) x in left)
            {
                foreach ((double From, double To) y in right)
                {
                    result.Add((System.Math.Max(x.From, y.From), System.Math.Min(x.To, y.To)));
                }
            }

            return Normalise(result, tolerance);
        }

        /// <summary>The stretches of <paramref name="a"/> not in <paramref name="b"/>.</summary>
        public static List<(double From, double To)> Difference(IEnumerable<(double From, double To)> a, IEnumerable<(double From, double To)> b, double tolerance)
        {
            List<(double From, double To)> result = new List<(double, double)>();
            List<(double From, double To)> removed = Normalise(b, tolerance);
            foreach ((double From, double To) x in Normalise(a, tolerance))
            {
                double start = x.From;
                foreach ((double From, double To) y in removed.Where(y => y.To > x.From && y.From < x.To))
                {
                    result.Add((start, System.Math.Min(y.From, x.To)));
                    start = System.Math.Max(start, y.To);
                }

                result.Add((start, x.To));
            }

            return Normalise(result, tolerance);
        }

        public static double Length(IEnumerable<(double From, double To)> intervals)
        {
            return intervals?.Sum(x => x.To - x.From) ?? 0;
        }
    }
}
