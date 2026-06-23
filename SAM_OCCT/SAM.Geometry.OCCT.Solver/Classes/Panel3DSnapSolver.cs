// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

// SAM.Core.OCCT and SAM.Geometry.OCCT both declare a static Query class.
using GeometryQuery = SAM.Geometry.OCCT.Query;
using GeometryCreate = SAM.Geometry.OCCT.Create;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// True 3D analogue of <c>SAM.Geometry.Solver.SnapSolver</c>. Reuses the proven
    /// backer (<c>Weight</c>) + width (<c>BucketSize</c>) semantics, but operates on panels
    /// as faces in full 3D space instead of per-level 2D slices.
    /// </summary>
    /// <remarks>
    /// Pipeline:
    /// <list type="number">
    /// <item>Register panels as <see cref="SnappedPanel"/>s (pad parameter lists).</item>
    /// <item><b>Snap (managed):</b> within near-parallel clusters, project each lower-<c>Weight</c>
    /// panel that lies inside a higher-<c>Weight</c> backer's <c>BucketSize</c> slab onto the backer plane.</item>
    /// <item><b>Resolve (native):</b> hand the snapped face set to the OCCT kernel - MakerVolume
    /// (<c>Create.Shells</c>) splits mutual intersections and resolves junctions, then
    /// <c>MergeCoplanarFace3Ds</c> unifies resolved coplanar faces.</item>
    /// <item><b>Report:</b> <c>Validate</c> flags naked (free) boundary edges.</item>
    /// </list>
    /// When the native kernel is unavailable the managed snap result is returned unresolved and
    /// <see cref="NativeResolved"/> is false.
    /// </remarks>
    public class Panel3DSnapSolver
    {
        public const double DEFAULT_BucketSize = 0.3;
        public const double DEFAULT_Weight = 1.0;
        public const double DEFAULT_MaxExtension = 0.5;

        private readonly List<Face3D> face3Ds;
        private readonly List<double> bucketSizes;
        private readonly List<double> weights;
        private readonly List<double> maxExtensions;

        public double ToleranceAngle { get; set; } = 5 * (System.Math.PI / 180);
        public double ToleranceArcAngle { get; set; } = 0.3 * (System.Math.PI / 180);
        public double ToleranceDistance { get; set; } = Tolerance.Distance;

        /// <summary>
        /// When true, walls that stop short of the floor/roof above are extended up to that cap before
        /// the native resolve, so the kernel can trim them (e.g. split a gable wall at the roof pitch)
        /// and close the under-roof volume. The floor/roof elevations a wall is extended to become the
        /// implicit levels. Default true.
        /// </summary>
        public bool ExtendToCaps { get; set; } = true;

        /// <summary>How far past the cap a wall is over-extended so the native trim cuts cleanly (metres).</summary>
        public double ExtendOvershoot { get; set; } = 0.05;

        /// <summary>Angle within which a panel's normal counts as horizontal, so the panel is "vertical" (a wall).</summary>
        public double VerticalAngleTolerance { get; set; } = 20 * (System.Math.PI / 180);

        /// <summary>The registered panels after the managed snap stage. Carries source mapping.</summary>
        public List<SnappedPanel> SnappedPanels { get; private set; } = new List<SnappedPanel>();

        /// <summary>The resolved faces. After native resolve these are split/merged; otherwise the snapped faces.</summary>
        public List<Face3D> ResolvedFace3Ds { get; private set; } = new List<Face3D>();

        /// <summary>Locations of naked (free) boundary edges reported by the native validator.</summary>
        public List<Point3D> NakedEdgePoint3Ds { get; private set; } = new List<Point3D>();

        /// <summary>True when the native OCCT kernel ran the resolve stage; false for a managed-only result.</summary>
        public bool NativeResolved { get; private set; }

        public Panel3DSnapSolver(
            IEnumerable<Face3D> face3Ds,
            IEnumerable<double> bucketSizes = null,
            IEnumerable<double> weights = null,
            IEnumerable<double> maxExtensions = null)
        {
            this.face3Ds = face3Ds == null ? new List<Face3D>() : face3Ds.ToList();
            this.bucketSizes = bucketSizes?.ToList();
            this.weights = weights?.ToList();
            this.maxExtensions = maxExtensions?.ToList();
        }

        public void Execute(OcctBuildOptions options = null)
        {
            SnappedPanels = new List<SnappedPanel>();
            ResolvedFace3Ds = new List<Face3D>();
            NakedEdgePoint3Ds = new List<Point3D>();
            NativeResolved = false;

            if (face3Ds == null || face3Ds.Count == 0)
            {
                return;
            }

            List<double> bucketSizes_Adjusted = AdjustListLength(bucketSizes, face3Ds.Count, DEFAULT_BucketSize);
            List<double> weights_Adjusted = AdjustListLength(weights, face3Ds.Count, DEFAULT_Weight);
            List<double> maxExtensions_Adjusted = AdjustListLength(maxExtensions, face3Ds.Count, DEFAULT_MaxExtension);

            SnappedPanels = Register(face3Ds, bucketSizes_Adjusted, weights_Adjusted, maxExtensions_Adjusted);

            Snap(SnappedPanels, ToleranceAngle, ToleranceArcAngle);

            if (ExtendToCaps)
            {
                Extend(SnappedPanels, VerticalAngleTolerance, ExtendOvershoot, ToleranceDistance);
            }

            List<Face3D> snappedFace3Ds = SnappedPanels.Select(x => x.Face3D).Where(x => x != null && x.IsValid()).ToList();
            ResolvedFace3Ds = snappedFace3Ds;

            Resolve(snappedFace3Ds, options);
        }

        /// <summary>
        /// Managed extend: grow each (vertical) wall up to the nearest cap - the floor or roof that
        /// sits above it and covers it in plan - so the native resolve can trim the wall against that
        /// cap and close the volume. The cap a wall reaches defines its implicit upper level; a wall
        /// under a pitched roof is over-extended past the ridge so the roof faces split it at the pitch.
        /// Walls with no cap above (true parapets/outer tops) are left untouched.
        /// </summary>
        public static void Extend(List<SnappedPanel> panels, double verticalAngleTolerance, double overshoot, double toleranceDistance)
        {
            if (panels == null || panels.Count < 2)
            {
                return;
            }

            List<SnappedPanel> walls = new List<SnappedPanel>();
            List<BoundingBox3D> caps = new List<BoundingBox3D>();
            foreach (SnappedPanel panel in panels)
            {
                BoundingBox3D boundingBox3D = panel.GetBoundingBox();
                if (boundingBox3D == null)
                {
                    continue;
                }

                if (panel.IsVertical(verticalAngleTolerance))
                {
                    walls.Add(panel);
                }
                else
                {
                    caps.Add(boundingBox3D); // floors and roofs are the caps walls extend to
                }
            }

            foreach (SnappedPanel wall in walls)
            {
                BoundingBox3D wallBox = wall.GetBoundingBox();
                if (wallBox == null)
                {
                    continue;
                }

                double wallTopZ = wallBox.Max.Z;

                // The nearest cap that starts above the wall top and covers it in plan.
                BoundingBox3D nearestCap = null;
                double nearestStartZ = double.MaxValue;
                foreach (BoundingBox3D cap in caps)
                {
                    if (cap.Min.Z < wallTopZ - toleranceDistance)
                    {
                        continue; // not above the wall
                    }

                    if (!OverlapsInPlan(cap, wallBox, toleranceDistance))
                    {
                        continue;
                    }

                    if (cap.Min.Z < nearestStartZ)
                    {
                        nearestStartZ = cap.Min.Z;
                        nearestCap = cap;
                    }
                }

                if (nearestCap == null)
                {
                    continue;
                }

                // Extend past the highest point of that cap (the ridge, for a pitched roof) so the
                // native trim cuts the wall cleanly along the cap.
                wall.ExtendTopTo(nearestCap.Max.Z + overshoot, toleranceDistance);
            }
        }

        /// <summary>True when the two boxes overlap in the XY (plan) projection within a tolerance.</summary>
        private static bool OverlapsInPlan(BoundingBox3D a, BoundingBox3D b, double tolerance)
        {
            return a.Min.X <= b.Max.X + tolerance && a.Max.X >= b.Min.X - tolerance
                && a.Min.Y <= b.Max.Y + tolerance && a.Max.Y >= b.Min.Y - tolerance;
        }

        /// <summary>
        /// Managed snap: sort by <c>Weight</c> descending so backers are processed first, then
        /// project each not-yet-snapped lower-weight panel that lies within a backer's slab and is
        /// near-parallel to it onto the backer plane. Equal-weight near-coincident panels are absorbed.
        /// </summary>
        public static void Snap(List<SnappedPanel> panels, double toleranceAngle, double toleranceArcAngle)
        {
            if (panels == null || panels.Count < 2)
            {
                return;
            }

            List<SnappedPanel> ordered = panels.OrderByDescending(x => x.Weight).ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                SnappedPanel backer = ordered[i];
                if (backer.Plane == null)
                {
                    continue;
                }

                for (int j = i + 1; j < ordered.Count; j++)
                {
                    SnappedPanel candidate = ordered[j];
                    if (candidate.Snapped || candidate.Plane == null)
                    {
                        continue;
                    }

                    // Equal-weight panels do not dominate one another; lower-weight panels snap.
                    if (candidate.Weight >= backer.Weight)
                    {
                        continue;
                    }

                    if (!backer.BucketContains(candidate, out bool fully))
                    {
                        continue;
                    }

                    // A fully captured neighbour tolerates a larger angle (it is clearly the same
                    // surface); a partially captured one must be near-parallel. Mirrors the 2D solver.
                    double angleTolerance = fully ? toleranceAngle : toleranceArcAngle;
                    if (!backer.IsParallelWith(candidate, angleTolerance))
                    {
                        continue;
                    }

                    candidate.SnapToBacker(backer.Plane);
                }
            }
        }

        private void Resolve(List<Face3D> snappedFace3Ds, OcctBuildOptions options)
        {
            if (snappedFace3Ds == null || snappedFace3Ds.Count == 0)
            {
                return;
            }

            // MakerVolume: split panels at mutual intersections and resolve 3-way junctions.
            List<Shell> shells = GeometryCreate.Shells(snappedFace3Ds, out OcctCellComplexResult cellResult, options);
            if (cellResult == null || !cellResult.NativeAvailable)
            {
                // Native kernel not present (e.g. non-Windows agent): keep the managed snap result.
                cellResult?.Dispose();
                return;
            }

            NativeResolved = true;

            List<Face3D> resolved = shells == null
                ? new List<Face3D>()
                : shells.Where(x => x != null).SelectMany(x => x.Face3Ds ?? new List<Face3D>()).Where(x => x != null).ToList();
            cellResult.Dispose();

            if (resolved.Count == 0)
            {
                // No closed cells formed (open wall soup): fall back to imprinting the raw faces.
                resolved = snappedFace3Ds;
            }

            // Merge resolved coplanar neighbours (the colinear-merge analogue).
            List<Face3D> merged = GeometryQuery.MergeCoplanarFace3Ds(resolved, out OcctCellComplexResult mergeResult, ToleranceAngle, options);
            mergeResult?.Dispose();
            if (merged != null && merged.Count != 0)
            {
                resolved = merged;
            }

            ResolvedFace3Ds = resolved;

            // Report naked (free) boundary edges - true boundaries vs unresolved gaps.
            GeometryQuery.Validate(resolved, out OcctValidationReport report, out OcctCellComplexResult validateResult, options, false);
            validateResult?.Dispose();
            if (report != null)
            {
                NakedEdgePoint3Ds = report
                    .IssuesOf(OcctValidationIssueCategory.NakedEdge)
                    .Where(x => x?.Location != null)
                    .Select(x => x.Location)
                    .ToList();
            }
        }

        private static List<SnappedPanel> Register(List<Face3D> face3Ds, List<double> bucketSizes, List<double> weights, List<double> maxExtensions)
        {
            List<SnappedPanel> panels = new List<SnappedPanel>();
            for (int i = 0; i < face3Ds.Count; i++)
            {
                Face3D face3D = face3Ds[i];
                if (face3D == null || !face3D.IsValid() || face3D.GetPlane() == null)
                {
                    continue;
                }

                panels.Add(new SnappedPanel(i, face3D, weights[i], bucketSizes[i], maxExtensions[i]));
            }

            return panels;
        }

        /// <summary>
        /// Pads or trims <paramref name="values"/> to <paramref name="targetCount"/>, filling the
        /// shortfall with <paramref name="defaultValue"/>. Same pad pattern as the 2D <c>SnapSolver</c>.
        /// </summary>
        public static List<double> AdjustListLength(List<double> values, int targetCount, double defaultValue)
        {
            List<double> adjusted = new List<double>(targetCount);
            int existing = values?.Count ?? 0;
            for (int i = 0; i < targetCount; i++)
            {
                adjusted.Add(i < existing ? values[i] : defaultValue);
            }

            return adjusted;
        }
    }
}
