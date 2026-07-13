// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
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

            /// <summary>How many <see cref="Panel3DSnapSolver.Snap"/> passes <see cref="SnapToFixedPoint"/> ran
            /// before no panel changed further (1 when a single pass already converged).</summary>
            public int SnapIterationCount { get; set; }

            /// <summary>
            /// The level datums (Phase 6a) clustered from this clean pass's caps before cap normalization
            /// (Phase 8 reporting capture) - empty when no cap formed a frame (the legacy world-frame
            /// normalization fallback ran instead). Read-only; does not affect the returned geometry.
            /// </summary>
            public List<LevelFrame> LevelFrames { get; set; } = new List<LevelFrame>();

            /// <summary>
            /// The level GROUPS (P2, docs/CONTROLLED_WORKFLOW_PLAN.md §4.1) formed by merging
            /// <see cref="LevelFrames"/> over <c>bucketBetweenLevels</c> - the storey datums caps normalize onto
            /// when the merge band is set. With the default <c>bucketBetweenLevels = 0</c> this is the identity of
            /// <see cref="LevelFrames"/> (one group per frame). Read-only reporting capture; does not affect the
            /// returned geometry beyond selecting the group datum for cap normalization.
            /// </summary>
            public List<LevelGroup> LevelGroups { get; set; } = new List<LevelGroup>();
        }

        /// <summary>
        /// Upper bound on <see cref="SnapToFixedPoint"/>'s passes (Phase 2b, 2D parity -
        /// <c>SnapSolver.MAX_SNAP_ADJUST_ITERATIONS</c>) - a safety backstop, not an expected count: real
        /// fixtures converge in a handful of passes. Hitting it emits a <see cref="DiagnosticCode.BudgetExceeded"/>
        /// warning rather than looping forever on a pathological/oscillating input.
        /// </summary>
        public const int MaxSnapIterations = 1000;

        /// <summary>
        /// Runs <see cref="Panel3DSnapSolver.Snap"/> repeatedly until no panel changes further (a fixed point)
        /// or <paramref name="maxIterations"/> is reached (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md
        /// §E Phase 2b - the 2D <c>SnapAndAdjustWalls</c> do-while, lifted to 3D). A single greedy pass can
        /// miss a chained relationship - panel C only comes within panel A's reach after A's bucket grows from
        /// merging with B earlier in the SAME pass, but C was already checked (and rejected) before that
        /// growth happened; the next pass re-evaluates everyone against the now-current geometry and catches
        /// it. Returns the number of passes run.
        /// </summary>
        public static int SnapToFixedPoint(
            List<SnappedPanel> panels,
            ToleranceBudget tolerances,
            double alignColinearOffset,
            SolverDiagnostics diagnostics = null,
            int maxIterations = MaxSnapIterations,
            List<CleanRecord> records = null)
        {
            if (panels == null || panels.Count < 2)
            {
                return 0;
            }

            ToleranceBudget tol = tolerances ?? new ToleranceBudget();

            int iteration = 0;
            bool anyChanged;
            do
            {
                anyChanged = Panel3DSnapSolver.Snap(panels, tol.Angle, tol.ArcAngle, tol.Distance, tol.VerticalAngle, alignColinearOffset, records);
                iteration++;
            }
            while (anyChanged && iteration < maxIterations);

            if (anyChanged && iteration >= maxIterations)
            {
                diagnostics?.Add(SolverStage.Snap, DiagnosticCode.BudgetExceeded, OcctDiagnosticSeverity.Warning,
                    string.Format("Snap fixed-point iteration reached the cap ({0}); the snapped model may be incomplete.", maxIterations));
            }

            return iteration;
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
            SourceMap sourceMap = null,
            double bucketBetweenLevels = 0.0,
            List<CleanRecord> cleanRecords = null,
            double doubleWallGap = 0.0)
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
            Panel3DSnapSolver.SnapOpposedPartitions(panels, tol.Angle, tol.Distance, diagnostics, cleanRecords);

            // 2. Bucket snap to a FIXED POINT (Phase 2b) - within-bucket near-parallel, in-plane-overlapping
            //    panels onto one backer plane, and align consecutive vertical wall segments offset by a small
            //    step jog, repeated until nothing changes (see SnapToFixedPoint).
            result.SnapIterationCount = SnapToFixedPoint(panels, tol, alignColinearOffset, diagnostics, MaxSnapIterations, cleanRecords);

            // 2a. OPT-IN explicit double-wall consolidation (default doubleWallGap = 0 -> skipped, byte-identical):
            //     collapse each residual chain of near-parallel, overlapping vertical walls within the user's
            //     declared gap onto its dominant plane in one deterministic pass. Runs AFTER the fixed point
            //     because it exists to finish what the pairwise snap cannot - the Snapped-frozen residue planes
            //     of a 3+ wall stack, and anti-parallel pairs beyond the void guard the user declares artifacts.
            //     Also activates when any panel carries a stamped ConsolidationRange > 0 (per-panel override).
            if (doubleWallGap > tol.Distance || panels.Any(x => x?.ConsolidationRange > tol.Distance))
            {
                Panel3DSnapSolver.ConsolidateWallStacks(panels, doubleWallGap, tol.Angle, tol.Distance, tol.VerticalAngle, diagnostics, cleanRecords);
            }

            // 2b. Normalize each level's caps onto that level's own datum plane (Phase 6c). Cluster the current
            //     cap faces into RAW level frames (the pinned 0.15 m band - UNCHANGED), then optionally merge
            //     those frames into LevelGroups over the user's bucketBetweenLevels band (P2, §4.1). A cap
            //     normalizes onto its GROUP datum (one per storey) when the merge band is set, or onto its raw
            //     frame datum with the default bucketBetweenLevels = 0 (identity groups). Falls back to the
            //     legacy world-frame band when no cap forms a frame (e.g. a wall-only bucket).
            List<LevelFrame> capFrames = LevelFrame.Cluster(
                panels.Where(x => x?.Face3D != null && x.Face3D.IsValid() && x.Plane != null && !x.IsVertical(tol.VerticalAngle))
                    .Select(x => x.Face3D)
                    .ToList(),
                LevelFrame.DEFAULT_NormalConeTolerance,
                LevelFrame.DEFAULT_ElevationBand);
            result.LevelFrames = capFrames;
            result.LevelGroups = LevelFrame.GroupFrames(capFrames, bucketBetweenLevels, LevelFrame.DEFAULT_NormalConeTolerance, diagnostics);

            if (capFrames.Count != 0)
            {
                if (bucketBetweenLevels > tol.Distance)
                {
                    // Feature ON: normalize caps onto the GROUP datums (§4.2). effectiveBand widens the cap->datum
                    // membership to max(0.15, bucketBetweenLevels) so a cap up to bucketBetweenLevels from a merged
                    // group datum is claimed by that datum (not the nearest-datum ambiguity fallback).
                    double effectiveBand = System.Math.Max(LevelFrame.DEFAULT_ElevationBand, bucketBetweenLevels);
                    List<LevelFrame> groupDatums = result.LevelGroups.Select(x => x.ToDatumFrame()).ToList();
                    Panel3DSnapSolver.NormalizeCaps(panels, groupDatums, tol.Angle, tol.Distance, tol.VerticalAngle, diagnostics, effectiveBand, cleanRecords, normalizeToFrameDatum: true);
                }
                else
                {
                    // Feature OFF (default): the EXACT pre-P2 frame-aware normalization call - byte-identical.
                    Panel3DSnapSolver.NormalizeCaps(panels, capFrames, tol.Angle, tol.Distance, tol.VerticalAngle, diagnostics, LevelFrame.DEFAULT_ElevationBand, cleanRecords);
                }
            }
            else
            {
                diagnostics?.Add(SolverStage.Snap, DiagnosticCode.FrameNormalization, OcctDiagnosticSeverity.Info,
                    string.Format("Frame-aware cap normalization: no cap formed a level frame; falling back to the legacy world-frame band ({0:0.###} m).", normalizeCapOffset));
                Panel3DSnapSolver.NormalizeCaps(panels, tol.Angle, normalizeCapOffset, tol.Distance, tol.VerticalAngle);
            }

            // The post-snap panels still carry their source identity (SourceIndices) and MaxExtension - the merge
            // below is the only step that collapses several into one face, so attribution is measured against them.
            List<SnappedPanel> postSnap = panels.Where(x => x?.Face3D != null && x.Face3D.IsValid() && x.Plane != null).ToList();
            List<Face3D> face3Ds = postSnap.Select(x => x.Face3D).ToList();

            // Drops: a panel that turned invalid/degenerate during snap (no valid face/plane) is dropped from the
            // clean output - recorded so a vanished panel is never silent (§4.4 DroppedInvalid).
            if (cleanRecords != null)
            {
                foreach (SnappedPanel dropped in panels.Where(x => x != null && (x.Face3D == null || !x.Face3D.IsValid() || x.Plane == null)))
                {
                    Panel3DSnapSolver.RecordClean(cleanRecords, dropped, null, CleanRecordKind.DroppedInvalid, 0.0);
                }
            }

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

                // Coplanar merge (§4.4 CoplanarMerged): several post-snap panels absorbed into one clean face.
                // The largest-area contributor is the surviving backer; the rest are recorded as merged onto it.
                if (cleanRecords != null && contributors.Count > 1)
                {
                    SnappedPanel dominant = contributors.OrderByDescending(x => x.GetArea()).First();
                    foreach (SnappedPanel merged2 in contributors)
                    {
                        if (!ReferenceEquals(merged2, dominant))
                        {
                            Panel3DSnapSolver.RecordClean(cleanRecords, merged2, dominant, CleanRecordKind.CoplanarMerged, 0.0);
                        }
                    }
                }

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
