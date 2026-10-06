// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Derives <c>bucketBetweenLevels</c> mathematically from cap elevations.
    /// The algorithm:
    /// <list type="number">
    /// <item>Extract cap elevations from non-vertical faces.</item>
    /// <item>Cluster elevations into frames using a default narrow band (0.15 m).</item>
    /// <item>Sort frame datums by elevation, compute inter-frame gaps.</item>
    /// <item>Identify natural level boundaries: large gaps = inter-storey, small gaps = intra-storey.</item>
    /// <item>The optimal band is the maximum intra-storey gap × safety factor (default 1.5),
    /// clamped to [0.15, 1.0] m.</item>
    /// </list>
    /// </summary>
    public static class BucketSizeEstimator
    {
        public const double MinBand = 0.15;
        public const double MaxBand = 1.0;
        public const double DefaultSafetyFactor = 1.5;
        public const double FrameBand = 0.15; // narrow band for initial clustering
        public const int MinElevations = 4;

        /// <summary>
        /// Computes the optimal <c>bucketBetweenLevels</c> from cap Face3Ds.
        /// Returns 0 when the input is too small.
        /// </summary>
        public static double Compute(IEnumerable<Face3D> face3Ds, double safetyFactor = DefaultSafetyFactor)
        {
            List<double> elevations = ExtractCapElevations(face3Ds);
            if (elevations.Count < MinElevations) return 0;
            return Compute(elevations, safetyFactor);
        }

        /// <summary>
        /// Computes the optimal band directly from a list of cap elevations.
        /// </summary>
        public static double Compute(IReadOnlyList<double> elevations, double safetyFactor = DefaultSafetyFactor)
        {
            if (elevations == null || elevations.Count < MinElevations) return 0;

            // 1. Cluster into narrow frames (0.15 m band).
            List<double> sorted = elevations.OrderBy(x => x).ToList();
            List<FrameCluster> frames = ClusterIntoFrames(sorted, FrameBand);

            if (frames.Count < 2) return 0;

            // 2. Compute gaps between consecutive frame datums.
            List<double> interFrameGaps = new List<double>();
            for (int i = 1; i < frames.Count; i++)
            {
                interFrameGaps.Add(frames[i].Datum - frames[i - 1].Datum);
            }

            // 3. Find the natural threshold separating intra-storey from inter-storey gaps.
            double threshold = FindSeparationThreshold(interFrameGaps);

            // 4. Max intra-storey gap × safety factor.
            List<double> intraGaps;
            if (threshold > 0)
            {
                intraGaps = interFrameGaps.Where(g => g <= threshold).ToList();
            }
            else
            {
                // No clear separation — use the median gap (assume single storey with variation).
                intraGaps = interFrameGaps;
            }

            if (intraGaps.Count == 0)
            {
                // All gaps are large (single storey). Use the minimum gap.
                return Clamp(interFrameGaps.Min() * 0.5, MinBand, MaxBand);
            }

            double maxIntraGap = intraGaps.Max();
            return Clamp(maxIntraGap * safetyFactor, MinBand, MaxBand);
        }

        /// <summary>
        /// Extracts cap elevations from Face3Ds. Non-vertical faces (|normal.Z| > sin(20°))
        /// contribute their bounding-box mid-Z.
        /// </summary>
        public static List<double> ExtractCapElevations(IEnumerable<Face3D> face3Ds)
        {
            List<double> elevations = new List<double>();
            if (face3Ds == null) return elevations;

            foreach (Face3D face3D in face3Ds)
            {
                if (face3D == null) continue;
                Vector3D normal = face3D.GetPlane()?.Normal?.Unit;
                if (normal == null || System.Math.Abs(normal.Z) <= 0.342) continue;
                BoundingBox3D box = face3D.GetBoundingBox();
                if (box == null) continue;
                elevations.Add(0.5 * (box.Min.Z + box.Max.Z));
            }

            return elevations;
        }

        /// <summary>
        /// Clusters sorted elevations into frames within a band. Each frame's datum is the
        /// mean elevation of its members.
        /// </summary>
        private static List<FrameCluster> ClusterIntoFrames(List<double> sorted, double band)
        {
            List<FrameCluster> frames = new List<FrameCluster>();
            if (sorted.Count == 0) return frames;

            FrameCluster current = new FrameCluster { Datum = sorted[0], MinZ = sorted[0], MaxZ = sorted[0], Count = 1, Sum = sorted[0] };

            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i] - current.MaxZ <= band)
                {
                    // Within the band — extend the current frame.
                    current.MaxZ = sorted[i];
                    current.Count++;
                    current.Sum += sorted[i];
                    current.Datum = current.Sum / current.Count;
                }
                else
                {
                    // New frame starts.
                    frames.Add(current);
                    current = new FrameCluster { Datum = sorted[i], MinZ = sorted[i], MaxZ = sorted[i], Count = 1, Sum = sorted[i] };
                }
            }

            frames.Add(current);
            return frames;
        }

        /// <summary>
        /// Finds the threshold that best separates small gaps (intra-storey) from large gaps
        /// (inter-storey). Uses between-group variance maximization.
        /// </summary>
        private static double FindSeparationThreshold(List<double> gaps)
        {
            if (gaps.Count < 2) return -1;

            List<double> sorted = gaps.OrderBy(x => x).ToList();
            double bestThreshold = -1;
            double bestVariance = 0;

            for (int t = 0; t < sorted.Count - 1; t++)
            {
                double candidate = 0.5 * (sorted[t] + sorted[t + 1]);

                double sum1 = 0, sum2 = 0;
                int count1 = 0, count2 = 0;
                foreach (double g in gaps)
                {
                    if (g <= candidate) { sum1 += g; count1++; }
                    else { sum2 += g; count2++; }
                }

                if (count1 == 0 || count2 == 0) continue;

                double mean1 = sum1 / count1;
                double mean2 = sum2 / count2;
                double betweenVar = (double)count1 * count2 * (mean1 - mean2) * (mean1 - mean2) / (count1 + count2);

                if (betweenVar > bestVariance)
                {
                    bestVariance = betweenVar;
                    bestThreshold = candidate;
                }
            }

            return bestThreshold;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value <= 0) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private class FrameCluster
        {
            public double Datum;
            public double MinZ;
            public double MaxZ;
            public int Count;
            public double Sum;
        }
    }
}
