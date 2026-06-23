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

        /// <summary>Step 1: drop coincident coplanar duplicate panels before the resolve. Default true.</summary>
        public bool DedupCoincident { get; set; } = true;

        /// <summary>Step 2: grow floors/roofs out to the surrounding walls (close floor-to-wall gaps). Default true.</summary>
        public bool FillCapsToWalls { get; set; } = true;

        /// <summary>How far a floor/roof is grown outward so it overshoots the walls and trims cleanly (metres).</summary>
        public double FillMargin { get; set; } = 0.3;

        /// <summary>Create a new Face3D over every hole (internal opening) so the model closes; these become air panels. Default true.</summary>
        public bool FillHoles { get; set; } = true;

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

        /// <summary>Number of closed cells (rooms/levels) the native MakerVolume formed. 1 = single space; 0 = none.</summary>
        public int ResolvedCellCount { get; private set; }

        /// <summary>The face set fed to the native MakerVolume - after snap, dedup, fill, extend and the
        /// coplanar pre-merge ("after bucket merge"). Exposed for visual debugging of the pre-resolve state.</summary>
        public List<Face3D> BucketMergedFace3Ds { get; private set; } = new List<Face3D>();

        /// <summary>New faces created to close holes (internal openings) in the input panels. These are the
        /// air-panel candidates: the analytical wrapper turns them into <c>PanelType.Air</c> panels.</summary>
        public List<Face3D> HoleFillFace3Ds { get; private set; } = new List<Face3D>();

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

            // Step 0: bucket/snap - project lower-weight panels onto higher-weight backer planes.
            Snap(SnappedPanels, ToleranceAngle, ToleranceArcAngle);

            // Step 1: 3D bucket dedup - drop coincident coplanar duplicates (a major self-intersection source).
            if (DedupCoincident)
            {
                SnappedPanels = Dedup(SnappedPanels, ToleranceAngle, ToleranceDistance);
            }

            // Step 2: fill floors/roofs out to the walls so the floor/roof-to-wall gaps close.
            if (FillCapsToWalls)
            {
                Fill(SnappedPanels, VerticalAngleTolerance, FillMargin, ToleranceDistance);
            }

            // Step 3: extend walls up to the floor/roof above so the kernel can trim them and close the volume.
            if (ExtendToCaps)
            {
                Extend(SnappedPanels, VerticalAngleTolerance, ExtendOvershoot, ToleranceDistance);
            }

            List<Face3D> snappedFace3Ds = SnappedPanels.Select(x => x.Face3D).Where(x => x != null && x.IsValid()).ToList();

            // Step 4: close holes (openings) with a new coplanar Face3D each, so the model is watertight.
            // These are the air-panel candidates; they are fed to the build (to seal the wall) and exposed.
            HoleFillFace3Ds = new List<Face3D>();
            if (FillHoles)
            {
                HoleFillFace3Ds = CreateHoleFillFace3Ds(SnappedPanels);
                if (HoleFillFace3Ds.Count != 0)
                {
                    snappedFace3Ds = snappedFace3Ds.Concat(HoleFillFace3Ds).ToList();
                }
            }

            ResolvedFace3Ds = snappedFace3Ds;

            Resolve(snappedFace3Ds, options);
        }

        /// <summary>
        /// Creates a new Face3D over each hole (internal opening loop) of every panel. The opening is closed
        /// by a coplanar face that the analytical wrapper later tags as an air panel. Sealing the openings is
        /// what makes the panel set watertight for the volume build.
        /// </summary>
        public static List<Face3D> CreateHoleFillFace3Ds(List<SnappedPanel> panels)
        {
            List<Face3D> result = new List<Face3D>();
            if (panels == null)
            {
                return result;
            }

            foreach (SnappedPanel panel in panels)
            {
                List<IClosedPlanar3D> holes = panel?.Face3D?.GetInternalEdge3Ds();
                if (holes == null)
                {
                    continue;
                }

                foreach (IClosedPlanar3D hole in holes)
                {
                    if (hole == null)
                    {
                        continue;
                    }

                    Face3D holeFace3D = Geometry.Spatial.Create.Face3D(hole);
                    if (holeFace3D != null && holeFace3D.IsValid())
                    {
                        result.Add(holeFace3D);
                    }
                }
            }

            return result;
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
                else if (boundingBox3D.Max.Z - boundingBox3D.Min.Z <= toleranceDistance + 0.1)
                {
                    // Only (near) horizontal caps - floors/flat ceilings - drive wall extension. A sloped
                    // roof's ridge is far above its eave; extending walls to the ridge would bury the roof
                    // inside a tall box and the kernel would drop it. Sloped-roof wall-trim is a later step.
                    caps.Add(boundingBox3D);
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

        /// <summary>
        /// Step 1 - 3D bucket dedup: collapse coincident coplanar duplicate panels into one (keeping the
        /// highest-weight, absorbing the rest's source references). Layered/doubled Revit faces project
        /// onto the same plane during Snap and then self-intersect; removing the duplicates is the cheapest
        /// way to cut the coplanar share of MakerVolume's self-intersection count.
        /// </summary>
        public static List<SnappedPanel> Dedup(List<SnappedPanel> panels, double angleTolerance, double distanceTolerance)
        {
            if (panels == null || panels.Count < 2)
            {
                return panels;
            }

            List<SnappedPanel> kept = new List<SnappedPanel>();
            foreach (SnappedPanel panel in panels.OrderByDescending(x => x.Weight))
            {
                SnappedPanel duplicate = kept.FirstOrDefault(k =>
                    k.IsCoplanarWith(panel, angleTolerance, distanceTolerance) && k.IsNearDuplicateOf(panel, distanceTolerance));

                if (duplicate != null)
                {
                    duplicate.Absorb(panel);
                }
                else
                {
                    kept.Add(panel);
                }
            }

            return kept;
        }

        /// <summary>
        /// Step 2 - fill floors/roofs to walls: grow each (non-vertical) cap outward in its plane so it
        /// overshoots the surrounding walls, closing the floor/roof-to-wall gaps that otherwise leave naked
        /// edges and prevent any cell from closing. The native resolve trims the overshoot back at the walls.
        /// </summary>
        public static void Fill(List<SnappedPanel> panels, double verticalAngleTolerance, double margin, double toleranceDistance)
        {
            if (panels == null || panels.Count == 0 || margin <= toleranceDistance)
            {
                return;
            }

            foreach (SnappedPanel panel in panels)
            {
                if (!panel.IsVertical(verticalAngleTolerance)) // floors and roofs are the caps
                {
                    panel.GrowOutward(margin, toleranceDistance);
                }
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

            // Healing defaults for the solver use-case: sew near-touching faces before the volume build
            // (bridges residual sub-mm gaps the managed fill leaves) and keep internal floors/partitions as
            // shared cell faces (a zoned complex, not just the outer envelope). Callers can override.
            if (options == null)
            {
                options = new OcctBuildOptions
                {
                    AvoidInternalShapes = false,
                    SewBeforeBuild = true,
                    SewingTolerance = 0.01 // 1 cm: bridges the cm-scale floor/wall gaps typical of Revit exports
                };
            }

            // Step 1 (native): coplanar pre-merge BEFORE the volume build. Collapsing coplanar overlaps
            // (the share of self-intersections MakerVolume cannot otherwise digest) is what lets the kernel
            // form a zoned cell complex instead of a single envelope cell - the actual "3D bucket".
            List<Face3D> buildFace3Ds = snappedFace3Ds;
            if (DedupCoincident)
            {
                List<Face3D> preMerged = GeometryQuery.MergeCoplanarFace3Ds(snappedFace3Ds, out OcctCellComplexResult preMergeResult, ToleranceAngle, options);
                preMergeResult?.Dispose();
                if (preMerged != null && preMerged.Count != 0)
                {
                    buildFace3Ds = preMerged;
                }
            }

            BucketMergedFace3Ds = buildFace3Ds; // expose the MakerVolume input for debugging

            // MakerVolume: split panels at mutual intersections and resolve 3-way junctions.
            List<Shell> shells = GeometryCreate.Shells(buildFace3Ds, out OcctCellComplexResult cellResult, options);
            if (cellResult == null || !cellResult.NativeAvailable)
            {
                // Native kernel not present (e.g. non-Windows agent): keep the managed snap result.
                cellResult?.Dispose();
                return;
            }

            NativeResolved = true;
            ResolvedCellCount = cellResult.Cells?.Count ?? 0;

            List<Face3D> resolved = shells == null
                ? new List<Face3D>()
                : shells.Where(x => x != null).SelectMany(x => x.Face3Ds ?? new List<Face3D>()).Where(x => x != null).ToList();
            cellResult.Dispose();

            if (resolved.Count == 0)
            {
                // No closed cells formed (open wall soup): fall back to the pre-merged faces.
                resolved = buildFace3Ds;
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

            // Close the residual holes: build a Face3D over each naked-boundary loop. These are added to the
            // air-panel candidates so every space is fully enclosed.
            if (FillHoles && NakedEdgePoint3Ds.Count != 0)
            {
                List<Face3D> gapFace3Ds = GapFill.NakedLoopFace3Ds(resolved, NakedEdgePoint3Ds, 0.01);
                if (gapFace3Ds.Count != 0)
                {
                    HoleFillFace3Ds = (HoleFillFace3Ds ?? new List<Face3D>()).Concat(gapFace3Ds).ToList();
                }
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
