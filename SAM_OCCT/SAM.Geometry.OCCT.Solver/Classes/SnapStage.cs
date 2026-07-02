// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Stage A - SNAP (managed, deterministic, non-fabricating). Makes near-coincident geometry
    /// exactly coincident and never invents geometry: strip holes, collapse back-to-back partitions,
    /// bucket-snap within-bucket parallels onto one backer, normalize a level's caps, then merge
    /// coplanar overlaps (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §D). The clean-bucket
    /// geometry is byte-identical to the pre-Phase-2 <see cref="Panel3DSnapSolver.CleanBucket"/>
    /// (which now delegates here); what this stage adds is <b>source attribution</b> over the final
    /// coplanar union, so each clean output face carries the <c>MaxExtend</c> of the source panel(s)
    /// that produced it - by source identity, not list position. That fixes the positional-MaxExtend
    /// bug: the old code re-applied the input <c>maxExtensions</c> list positionally to the
    /// merged/reordered clean faces, landing the wrong reach on the wrong panel.
    /// </summary>
    public static class SnapStage
    {
        /// <summary>Outcome of <see cref="Clean"/>: the clean faces plus, index-aligned, the MaxExtend and
        /// contributing source indices attributed to each.</summary>
        public class Result
        {
            public List<Face3D> CleanFace3Ds { get; } = new List<Face3D>();

            /// <summary>MaxExtend per clean face (index-aligned to <see cref="CleanFace3Ds"/>): the max over the
            /// source panels that merged into that face, so a wall the caller marked to extend further keeps its
            /// reach through the merge. Carried by source identity, not list position.</summary>
            public List<double> CleanMaxExtensions { get; } = new List<double>();

            /// <summary>The source indices attributed to each clean face (index-aligned to <see cref="CleanFace3Ds"/>).</summary>
            public List<List<int>> SourceIndicesPerFace { get; } = new List<List<int>>();
        }

        /// <summary>
        /// Runs the four managed clean-bucket sub-passes over <paramref name="panels"/> then the mapping-preserving
        /// coplanar union. Records source -> clean-face into <paramref name="sourceMap"/> (when supplied) with
        /// <see cref="Provenance.Snapped"/>, and returns the per-face carried MaxExtend. Geometry matches the
        /// legacy CleanBucket exactly (same union call); only the attribution is new.
        /// </summary>
        public static Result Clean(
            List<SnappedPanel> panels,
            ToleranceBudget tolerances,
            double alignColinearOffset,
            double normalizeCapOffset,
            SolverDiagnostics diagnostics = null,
            SourceMap sourceMap = null)
        {
            Result result = new Result();
            if (panels == null || panels.Count == 0)
            {
                return result;
            }

            ToleranceBudget tol = tolerances ?? new ToleranceBudget();

            // 1. External shape only - drop window/door openings.
            foreach (SnappedPanel panel in panels)
            {
                panel.StripInternalEdges();
            }

            // 1b. Collapse back-to-back partitions (facing-away, congruent-footprint skins) before the weighted
            //     bucket snap can pull them onto one side. Runs in the world frame, keyed on opposing-normal
            //     geometry. Now guarded by the separation-sign + overlap-footprint gates (Phase 2).
            Panel3DSnapSolver.SnapOpposedPartitions(panels, tol.Angle, tol.Distance, diagnostics);

            // 2. Bucket snap - within-bucket near-parallel, in-plane-overlapping panels onto one backer plane,
            //    and align consecutive vertical wall segments offset by a small step jog.
            Panel3DSnapSolver.Snap(panels, tol.Angle, tol.ArcAngle, tol.Distance, tol.VerticalAngle, alignColinearOffset);

            // 2b. Normalize a level's caps onto one plane.
            Panel3DSnapSolver.NormalizeCaps(panels, tol.Angle, normalizeCapOffset, tol.Distance, tol.VerticalAngle);

            // The post-snap panels still carry their source identity (SourceIndices) and MaxExtension - the merge
            // below is the only step that collapses several into one face, so attribution is measured against them.
            List<SnappedPanel> postSnap = panels.Where(x => x?.Face3D != null && x.Face3D.IsValid() && x.Plane != null).ToList();
            List<Face3D> face3Ds = postSnap.Select(x => x.Face3D).ToList();

            // 3. Coplanar merge - identical call (and fallback) to the legacy CleanBucket, so the geometry is
            //    unchanged; the mapping is recovered afterward by attribution rather than by re-implementing the
            //    union per group (same source->output result, exact same union geometry, lower risk).
            List<Face3D> merged = Geometry.Spatial.Query.Union(face3Ds, tol.Distance);
            if (merged == null || merged.Count == 0)
            {
                merged = face3Ds;
            }

            List<Face3D> cleanFace3Ds = merged.Where(x => x != null && x.IsValid()).ToList();

            // Attribution: for each clean face, find the post-snap panels it covers (same plane, centroid inside),
            // carry MaxExtend = max over those sources, and record source -> face in the SourceMap. A face with no
            // geometric match (numerical edge) falls back to the nearest panel so every output keeps >= 1 source.
            for (int k = 0; k < cleanFace3Ds.Count; k++)
            {
                Face3D cleanFace3D = cleanFace3Ds[k];
                List<SnappedPanel> contributors = Contributors(cleanFace3D, postSnap);
                if (contributors.Count == 0)
                {
                    SnappedPanel nearest = NearestPanel(cleanFace3D, postSnap);
                    if (nearest != null)
                    {
                        contributors.Add(nearest);
                    }
                }

                List<int> sourceIndices = contributors
                    .SelectMany(x => x.SourceIndices ?? new List<int>())
                    .Distinct()
                    .ToList();

                double maxExtension = contributors.Count != 0
                    ? contributors.Max(x => x.MaxExtension)
                    : Panel3DSnapSolver.DEFAULT_MaxExtension;

                result.CleanFace3Ds.Add(cleanFace3D);
                result.CleanMaxExtensions.Add(maxExtension);
                result.SourceIndicesPerFace.Add(sourceIndices);

                if (sourceMap != null)
                {
                    if (sourceIndices.Count != 0)
                    {
                        sourceMap.RecordMerge(sourceIndices, new FaceKey(k), Provenance.Snapped);
                    }
                    else
                    {
                        sourceMap.RecordFabricated(new FaceKey(k), Provenance.Snapped);
                    }
                }
            }

            return result;
        }

        /// <summary>The post-snap panels whose (coplanar) face centre lies inside <paramref name="cleanFace3D"/>.</summary>
        private static List<SnappedPanel> Contributors(Face3D cleanFace3D, List<SnappedPanel> panels)
        {
            List<SnappedPanel> contributors = new List<SnappedPanel>();
            Plane plane = cleanFace3D?.GetPlane();
            if (plane == null)
            {
                return contributors;
            }

            Vector3D normal = plane.Normal.Unit;
            Geometry.Planar.Face2D cleanFace2D = plane.Convert(cleanFace3D);
            if (cleanFace2D == null)
            {
                return contributors;
            }

            foreach (SnappedPanel panel in panels)
            {
                Plane panelPlane = panel?.Plane;
                if (panelPlane == null)
                {
                    continue;
                }

                // Same plane: parallel normals, near-coincident offset.
                if (System.Math.Abs(normal.DotProduct(panelPlane.Normal.Unit)) < 0.99)
                {
                    continue;
                }

                Point3D centre = panel.GetBoundingBox()?.GetCentroid();
                if (centre == null || System.Math.Abs(plane.Distance(centre)) > 0.05)
                {
                    continue;
                }

                Geometry.Planar.Point2D centre2D = plane.Convert(centre);
                if (centre2D != null && (Geometry.Planar.Query.Inside(cleanFace2D, centre2D, 0.01) || cleanFace2D.On(centre2D, 0.01)))
                {
                    contributors.Add(panel);
                }
            }

            return contributors;
        }

        /// <summary>The post-snap panel whose face centroid is nearest <paramref name="cleanFace3D"/>'s centroid
        /// (attribution fallback so no clean face is left source-orphaned).</summary>
        private static SnappedPanel NearestPanel(Face3D cleanFace3D, List<SnappedPanel> panels)
        {
            Point3D cleanCentre = cleanFace3D?.GetBoundingBox()?.GetCentroid();
            if (cleanCentre == null)
            {
                return panels.FirstOrDefault();
            }

            SnappedPanel best = null;
            double bestDistance = double.MaxValue;
            foreach (SnappedPanel panel in panels)
            {
                Point3D centre = panel?.GetBoundingBox()?.GetCentroid();
                if (centre == null)
                {
                    continue;
                }

                double distance = cleanCentre.Distance(centre);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = panel;
                }
            }

            return best;
        }
    }
}
