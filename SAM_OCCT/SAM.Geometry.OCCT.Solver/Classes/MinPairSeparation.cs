// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Managed measurement of the smallest gap between any pair of near-parallel, laterally-overlapping
    /// faces in a resolved set - the input to the Phase 5d adaptive-sew tolerance cap
    /// (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §A.3/§F). Capping the sew tolerance below this
    /// separation is what stops a global sew from fusing a genuine double wall while it closes an unrelated
    /// residual gap.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT <c>Query.ShellsDistance</c>: that takes shells only and costs a native round-trip
    /// plus two topology builds per call - far too heavy for this hot-path measurement. This is a pure
    /// managed O(n²) scan with a bounding-box prefilter, exact enough for a tolerance cap. Only pairs whose
    /// unit normals are near-(anti-)parallel (|n·n| ≥ threshold) and whose bounding boxes overlap (grown by
    /// the max separation) are measured; the separation is the distance between the two parallel planes.
    /// Non-parallel pairs are ignored; a set with no qualifying pair returns null.
    /// </remarks>
    public static class MinPairSeparation
    {
        /// <summary>Default lower bound (metres): a plane distance below this is coincident, not a gap.</summary>
        public const double DEFAULT_MinSeparation = Core.Tolerance.Distance;

        /// <summary>Default upper bound (metres): parallel faces farther apart than this are separate walls,
        /// not two skins of one slot the sew should consider - the §F [Tolerance.Distance, 0.5] window.</summary>
        public const double DEFAULT_MaxSeparation = 0.5;

        /// <summary>Default |n·n| floor for two faces to count as parallel (anti-parallel included).</summary>
        public const double DEFAULT_ParallelDot = 0.99;

        /// <summary>
        /// The smallest plane-to-plane separation over all near-parallel, bbox-overlapping face pairs whose
        /// separation lies in [<paramref name="minSeparation"/>, <paramref name="maxSeparation"/>], or null
        /// when no such pair exists. O(n²) with a bounding-box prefilter.
        /// </summary>
        public static double? Compute(
            IReadOnlyList<Face3D> face3Ds,
            double minSeparation = DEFAULT_MinSeparation,
            double maxSeparation = DEFAULT_MaxSeparation,
            double parallelDot = DEFAULT_ParallelDot)
        {
            if (face3Ds == null || face3Ds.Count < 2)
            {
                return null;
            }

            // Snapshot planes and boxes once (O(n)); a null plane/box drops the face from consideration.
            int count = face3Ds.Count;
            Plane[] planes = new Plane[count];
            BoundingBox3D[] boxes = new BoundingBox3D[count];
            Vector3D[] normals = new Vector3D[count];
            for (int i = 0; i < count; i++)
            {
                Face3D face3D = face3Ds[i];
                Plane plane = face3D?.GetPlane();
                BoundingBox3D box = face3D?.GetBoundingBox();
                if (plane == null || box == null)
                {
                    continue;
                }

                planes[i] = plane;
                boxes[i] = box;
                normals[i] = plane.Normal.Unit;
            }

            double? best = null;
            for (int i = 0; i < count; i++)
            {
                if (planes[i] == null)
                {
                    continue;
                }

                for (int j = i + 1; j < count; j++)
                {
                    if (planes[j] == null)
                    {
                        continue;
                    }

                    // Near-(anti-)parallel only; a differently-oriented face is not a slot skin.
                    if (System.Math.Abs(normals[i].DotProduct(normals[j])) < parallelDot)
                    {
                        continue;
                    }

                    // Bbox prefilter: the two faces must share a lateral region (grown by the max
                    // separation so a genuinely-close pair is never hidden - the pair sits within
                    // maxSeparation along its normal, so their raw boxes may just miss overlapping).
                    if (!BoxesOverlap(boxes[i], boxes[j], maxSeparation))
                    {
                        continue;
                    }

                    // Separation = distance from one plane to the other's origin (they are parallel).
                    double separation = System.Math.Abs(planes[i].Distance(planes[j].Origin));
                    if (separation < minSeparation || separation > maxSeparation)
                    {
                        continue;
                    }

                    if (best == null || separation < best.Value)
                    {
                        best = separation;
                    }
                }
            }

            return best;
        }

        /// <summary>True when the two boxes overlap on every axis after being grown by
        /// <paramref name="tolerance"/> (the prefilter that keeps close-but-just-missing pairs in play).</summary>
        private static bool BoxesOverlap(BoundingBox3D a, BoundingBox3D b, double tolerance)
        {
            if (a == null || b == null)
            {
                return false;
            }

            Point3D aMin = a.Min, aMax = a.Max, bMin = b.Min, bMax = b.Max;
            return aMin.X - tolerance <= bMax.X && aMax.X + tolerance >= bMin.X
                && aMin.Y - tolerance <= bMax.Y && aMax.Y + tolerance >= bMin.Y
                && aMin.Z - tolerance <= bMax.Z && aMax.Z + tolerance >= bMin.Z;
        }
    }
}
