// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Derives <c>fillMargin</c> and <c>directionalCapGrow</c> mathematically from Face3D geometry,
    /// eliminating manual per-fixture tuning. Walls are faces with |normal.Z| ≤ sin(20°); caps are
    /// faces with |normal.Z| > sin(20°).
    /// <para>
    /// <b>fillMargin:</b> measured as the maximum gap from any cap edge to its nearest coplanar
    /// neighbouring cap, or to the nearest wall bounding box. Scaled by safety factor, clamped to
    /// [0.3, 5.0] m.
    /// </para>
    /// <para>
    /// <b>directionalCapGrow:</b> set to false when many caps share an elevation — the signature of
    /// fragmented cap strips that directional wall-based growth cannot close.
    /// </para>
    /// </summary>
    public static class FillMarginEstimator
    {
        /// <summary>Minimum fill margin (metres).</summary>
        public const double MinMargin = 0.3;

        /// <summary>Maximum fill margin (metres).</summary>
        public const double MaxMargin = 5.0;

        /// <summary>Default safety factor applied to the computed gap.</summary>
        public const double DefaultSafetyFactor = 2.0;

        /// <summary>Fraction of caps with coplanar neighbours above which directionalCapGrow is set to false.</summary>
        public const double CoplanarNeighbourThreshold = 0.3;

        /// <summary>Vertical angle (sin) for wall classification.</summary>
        private const double WallNormalZ = 0.342;

        /// <summary>
        /// Result of the fill margin estimation.
        /// </summary>
        public class Result
        {
            /// <summary>The recommended fillMargin value.</summary>
            public double FillMargin { get; set; }

            /// <summary>Whether directional cap growth should be used.</summary>
            public bool DirectionalCapGrow { get; set; } = true;

            /// <summary>Maximum inter-cap gap detected (metres).</summary>
            public double MaxInterCapGap { get; set; }

            /// <summary>Maximum cap-to-wall gap detected (metres).</summary>
            public double MaxCapWallGap { get; set; }

            /// <summary>Fraction of caps with coplanar neighbours.</summary>
            public double CoplanarNeighbourFraction { get; set; }
        }

        /// <summary>
        /// Computes optimal fill margin and directionalCapGrow from Face3D geometry.
        /// </summary>
        /// <param name="face3Ds">Input faces (post-Clean3D recommended for representative cap sizes).</param>
        /// <param name="safetyFactor">Multiplier on the largest detected gap (default 2.0).</param>
        public static Result Compute(IEnumerable<Face3D> face3Ds, double safetyFactor = DefaultSafetyFactor)
        {
            Result result = new Result();

            if (face3Ds == null)
            {
                return result;
            }

            List<Face3D> faceList = face3Ds.Where(x => x != null).ToList();
            if (faceList.Count == 0)
            {
                return result;
            }

            // Classify faces: walls (vertical) vs caps (non-vertical).
            List<Face3D> walls = new List<Face3D>();
            List<Face3D> caps = new List<Face3D>();
            foreach (Face3D face3D in faceList)
            {
                Vector3D normal = face3D.GetPlane()?.Normal?.Unit;
                if (normal == null) continue;

                if (System.Math.Abs(normal.Z) <= WallNormalZ)
                {
                    walls.Add(face3D);
                }
                else
                {
                    caps.Add(face3D);
                }
            }

            if (caps.Count == 0)
            {
                return result;
            }

            // 1. Measure inter-cap gaps.
            result.MaxInterCapGap = MeasureMaxInterCapGap(caps);
            // 2. Measure cap-to-wall gaps.
            result.MaxCapWallGap = MeasureMaxCapWallGap(caps, walls);
            // 3. Count caps with coplanar neighbours.
            result.CoplanarNeighbourFraction = CoplanarNeighbourFraction(caps);

            // The fill margin is the largest gap we need to close, scaled up.
            double maxGap = System.Math.Max(result.MaxInterCapGap, result.MaxCapWallGap);
            result.FillMargin = Clamp(maxGap * safetyFactor, MinMargin, MaxMargin);

            // Directional growth is safe unless many caps are fragmented.
            result.DirectionalCapGrow = result.CoplanarNeighbourFraction < CoplanarNeighbourThreshold;

            return result;
        }

        private static double MeasureMaxInterCapGap(List<Face3D> caps)
        {
            double maxGap = 0;

            for (int i = 0; i < caps.Count; i++)
            {
                Face3D a = caps[i];
                BoundingBox3D boxA = a?.GetBoundingBox();
                if (boxA == null) continue;

                for (int j = i + 1; j < caps.Count; j++)
                {
                    Face3D b = caps[j];
                    BoundingBox3D boxB = b?.GetBoundingBox();
                    if (boxB == null) continue;

                    double zA = 0.5 * (boxA.Min.Z + boxA.Max.Z);
                    double zB = 0.5 * (boxB.Min.Z + boxB.Max.Z);
                    if (System.Math.Abs(zA - zB) > 0.1) continue;

                    double overlapX = System.Math.Min(boxA.Max.X, boxB.Max.X) - System.Math.Max(boxA.Min.X, boxB.Min.X);
                    double overlapY = System.Math.Min(boxA.Max.Y, boxB.Max.Y) - System.Math.Max(boxA.Min.Y, boxB.Min.Y);

                    double gapX = System.Math.Max(0, System.Math.Max(boxA.Min.X - boxB.Max.X, boxB.Min.X - boxA.Max.X));
                    double gapY = System.Math.Max(0, System.Math.Max(boxA.Min.Y - boxB.Max.Y, boxB.Min.Y - boxA.Max.Y));

                    if (overlapX > 0 && gapY > 0) maxGap = System.Math.Max(maxGap, gapY);
                    if (overlapY > 0 && gapX > 0) maxGap = System.Math.Max(maxGap, gapX);
                }
            }

            return maxGap;
        }

        private static double MeasureMaxCapWallGap(List<Face3D> caps, List<Face3D> walls)
        {
            double maxGap = 0;

            foreach (Face3D cap in caps)
            {
                BoundingBox3D capBox = cap.GetBoundingBox();
                if (capBox == null) continue;

                double nearestWallDist = double.MaxValue;
                foreach (Face3D wall in walls)
                {
                    BoundingBox3D wallBox = wall.GetBoundingBox();
                    if (wallBox == null) continue;

                    double dx = System.Math.Max(0,
                        System.Math.Max(capBox.Min.X - wallBox.Max.X, wallBox.Min.X - capBox.Max.X));
                    double dy = System.Math.Max(0,
                        System.Math.Max(capBox.Min.Y - wallBox.Max.Y, wallBox.Min.Y - capBox.Max.Y));
                    double dist = System.Math.Sqrt(dx * dx + dy * dy);
                    if (dist < nearestWallDist) nearestWallDist = dist;
                }

                if (nearestWallDist < double.MaxValue && nearestWallDist > maxGap)
                {
                    maxGap = nearestWallDist;
                }
            }

            return maxGap;
        }

        private static double CoplanarNeighbourFraction(List<Face3D> caps)
        {
            if (caps.Count < 2) return 0;

            int withNeighbour = 0;
            for (int i = 0; i < caps.Count; i++)
            {
                BoundingBox3D boxA = caps[i].GetBoundingBox();
                if (boxA == null) continue;
                double zA = 0.5 * (boxA.Min.Z + boxA.Max.Z);

                bool hasNeighbour = false;
                for (int j = 0; j < caps.Count; j++)
                {
                    if (i == j) continue;
                    BoundingBox3D boxB = caps[j].GetBoundingBox();
                    if (boxB == null) continue;
                    double zB = 0.5 * (boxB.Min.Z + boxB.Max.Z);

                    if (System.Math.Abs(zA - zB) > 0.1) continue;

                    double overlapX = System.Math.Min(boxA.Max.X, boxB.Max.X) - System.Math.Max(boxA.Min.X, boxB.Min.X);
                    double overlapY = System.Math.Min(boxA.Max.Y, boxB.Max.Y) - System.Math.Max(boxA.Min.Y, boxB.Min.Y);

                    if (overlapX > 0 || overlapY > 0)
                    {
                        hasNeighbour = true;
                        break;
                    }
                }

                if (hasNeighbour) withNeighbour++;
            }

            return (double)withNeighbour / caps.Count;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value <= 0) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
