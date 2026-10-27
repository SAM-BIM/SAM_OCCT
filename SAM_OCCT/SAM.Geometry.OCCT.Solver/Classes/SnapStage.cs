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

            /// <summary>Per clean face (index-aligned to <see cref="CleanFace3Ds"/>): true when its
            /// <see cref="SourceIndicesPerFace"/> came from the geometric attribution (same plane, centre inside),
            /// false when the nearest-panel fallback supplied it. Read by the residual-contact closure only.</summary>
            internal List<bool> GeometricAttributionPerFace { get; } = new List<bool>();

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
            // A face collapsed to a line by an upstream snap (yet still IsValid) counts as degenerate: its mean width - twice
            // its area over its perimeter, a length - is within the distance tolerance. It has no internal point, and one
            // such face makes the coplanar union below return nothing, which would leave every panel of the model
            // unmerged. A small but real face is many tolerances wide and is kept.
            bool Usable(SnappedPanel x) => x?.Face3D != null && x.Face3D.IsValid() && x.Plane != null && MeanWidth(x.Face3D) > tol.Distance;
            List<SnappedPanel> postSnap = panels.Where(Usable).ToList();
            List<Face3D> face3Ds = postSnap.Select(x => x.Face3D).ToList();

            // Drops: a panel that turned invalid/degenerate during snap (no valid face/plane, or collapsed) is dropped from
            // the clean output - recorded so a vanished panel is never silent (§4.4 DroppedInvalid).
            int collapsed = panels.Count(x => x?.Face3D != null && x.Face3D.IsValid() && x.Plane != null && !Usable(x));
            if (collapsed != 0)
            {
                diagnostics?.Add(SolverStage.Snap, DiagnosticCode.SliverFace, OcctDiagnosticSeverity.Warning,
                    string.Format("Dropped {0} panel face(s) collapsed to a line (mean width within the {1:G3} m distance tolerance) before the coplanar merge.", collapsed, tol.Distance));
            }

            if (cleanRecords != null)
            {
                foreach (SnappedPanel dropped in panels.Where(x => x != null && !Usable(x)))
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

            // 3a. Coverage guard: SAM's union skips a merged face it turned invalid (a sliver hole reconverted as a 2-point
            //     loop), so a whole coplanar group can vanish while the other planes merge. Panels whose internal point no
            //     clean face covers are recovered and appended here, so the attribution below applies to them unchanged.
            //     When nothing was lost this adds nothing: same faces, same order, no diagnostic.
            cleanFace3Ds.AddRange(RecoverLostPanels(postSnap, cleanFace3Ds, tol.Distance, diagnostics));

            // Attribution: for each clean face, find the post-snap panels it covers (same plane, centroid inside),
            // carry MaxExtend = max over those sources, and record source -> face in the SourceMap. A face with no
            // geometric match (numerical edge) falls back to the nearest panel so every output keeps >= 1 source.
            for (int k = 0; k < cleanFace3Ds.Count; k++)
            {
                Face3D cleanFace3D = cleanFace3Ds[k];
                List<SnappedPanel> contributors = Contributors(cleanFace3D, postSnap);
                bool geometric = contributors.Count != 0;
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
                result.GeometricAttributionPerFace.Add(geometric);

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

        /// <summary>
        /// The faces to add to <paramref name="cleanFace3Ds"/> for the post-snap panels the coplanar merge lost (empty when
        /// none was lost). A panel is lost when its internal point lies on no clean face of its own union plane, judged with
        /// the union's own plane identity (<see cref="SamePlane"/>: SAM <c>Plane.Coplanar</c> and an offset within
        /// <paramref name="tolerance"/>) and inside/on within <paramref name="tolerance"/> - never with the broader
        /// attribution thresholds of <see cref="Contributors"/>, under which a nearby parallel face would hide the loss.
        /// Lost panels are grouped seed-first as <c>Geometry.Spatial.Query.Union</c> groups them and each group is
        /// re-unioned in its seed plane. The only loops removed are inner loops with fewer than 3 points: zero area by
        /// construction, so no opening is filled (a genuine opening, however small, keeps its loop). The re-merge is kept
        /// when every face is valid and covers the group's internal points again; otherwise, and for a single lost face,
        /// the group's faces are kept unmerged. One <see cref="DiagnosticCode.CoplanarMergeRecovered"/> Warning per group.
        /// </summary>
        /// <param name="union">The planar union; null uses <c>Geometry.Planar.Query.Union</c> (a test seam only).</param>
        internal static List<Face3D> RecoverLostPanels(
            List<SnappedPanel> postSnap,
            List<Face3D> cleanFace3Ds,
            double tolerance,
            SolverDiagnostics diagnostics,
            System.Func<List<Geometry.Planar.Face2D>, double, List<Geometry.Planar.Face2D>> union = null)
        {
            List<Face3D> result = new List<Face3D>();
            if (postSnap == null || postSnap.Count == 0)
            {
                return result;
            }

            List<CoverageFace> coverageFaces = CoverageFaces(cleanFace3Ds);
            List<SnappedPanel> lost = postSnap.Where(x => !Covered(x.Face3D, coverageFaces, tolerance)).ToList();
            if (lost.Count == 0)
            {
                return result;
            }

            union = union ?? ((face2Ds, unionTolerance) => Geometry.Planar.Query.Union(face2Ds, unionTolerance));

            while (lost.Count != 0)
            {
                SnappedPanel seed = lost[0];
                Plane plane = seed.Face3D.GetPlane();
                List<SnappedPanel> group = lost.Where(x => ReferenceEquals(x, seed) || OnSeedPlane(plane, x.Face3D, tolerance)).ToList();
                lost.RemoveAll(x => group.Contains(x));

                List<Face3D> repaired = null;
                int removedLoops = 0;
                if (group.Count > 1)
                {
                    repaired = Repair(group, plane, tolerance, union, out removedLoops);
                }

                result.AddRange(repaired ?? group.Select(x => x.Face3D));

                Vector3D normal = plane.Normal.Unit;
                diagnostics?.Add(SolverStage.Snap, DiagnosticCode.CoplanarMergeRecovered, OcctDiagnosticSeverity.Warning,
                    string.Format("Coplanar merge lost {0} panel face(s) ({1:0.###} m2) on the plane with normal ({2:0.###}, {3:0.###}, {4:0.###}) at elevation {5:0.###} m; recovered: {6}.",
                        group.Count,
                        group.Sum(x => x.Face3D.GetArea()),
                        normal.X, normal.Y, normal.Z,
                        group.Min(x => x.Face3D.GetBoundingBox().Min.Z),
                        repaired != null ? string.Format("{0} degenerate loop(s) removed", removedLoops) : "kept unmerged"));
            }

            return result;
        }

        /// <summary>A clean face prepared once for the coverage test.</summary>
        private class CoverageFace
        {
            public Plane Plane;
            public Geometry.Planar.Face2D Face2D;
        }

        private static List<CoverageFace> CoverageFaces(IEnumerable<Face3D> face3Ds)
        {
            List<CoverageFace> result = new List<CoverageFace>();
            foreach (Face3D face3D in face3Ds ?? Enumerable.Empty<Face3D>())
            {
                Plane plane = face3D?.GetPlane();
                Geometry.Planar.Face2D face2D = plane?.Convert(face3D);
                if (face2D != null)
                {
                    result.Add(new CoverageFace { Plane = plane, Face2D = face2D });
                }
            }

            return result;
        }

        /// <summary>True when the face's internal point lies inside or on (within <paramref name="tolerance"/>) one of
        /// <paramref name="coverageFaces"/> on the same union plane (<see cref="SamePlane"/>), or when the face has no internal
        /// point (it cannot be tested, so it is never reported lost).</summary>
        private static bool Covered(Face3D face3D, List<CoverageFace> coverageFaces, double tolerance)
        {
            Point3D internalPoint = face3D.InternalPoint3D(tolerance);
            if (internalPoint == null || !internalPoint.IsValid())
            {
                return true;
            }

            Plane plane = face3D.GetPlane();
            foreach (CoverageFace coverageFace in coverageFaces)
            {
                if (!SamePlane(coverageFace.Plane, plane, internalPoint, tolerance))
                {
                    continue;
                }

                Geometry.Planar.Point2D point2D = coverageFace.Plane.Convert(internalPoint);
                if (point2D != null && (Geometry.Planar.Query.Inside(coverageFace.Face2D, point2D, tolerance) || coverageFace.Face2D.On(point2D, tolerance)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The plane identity of <c>Geometry.Spatial.Query.Union</c>: <paramref name="plane"/> is parallel to
        /// <paramref name="other"/> (SAM <c>Plane.Coplanar</c>, which compares normals only) and <paramref name="point3D"/>, a
        /// point of the face on <paramref name="other"/>, lies within <paramref name="tolerance"/> of it.</summary>
        private static bool SamePlane(Plane plane, Plane other, Point3D point3D, double tolerance)
        {
            return plane.Coplanar(other, tolerance) && System.Math.Abs(plane.Distance(point3D)) <= tolerance;
        }

        /// <summary>The grouping test of <c>Geometry.Spatial.Query.Union</c>: on the seed plane by <see cref="SamePlane"/>
        /// at the face's internal point.</summary>
        private static bool OnSeedPlane(Plane plane, Face3D face3D, double tolerance)
        {
            Point3D internalPoint = face3D.InternalPoint3D(tolerance);
            return internalPoint != null && internalPoint.IsValid() && SamePlane(plane, face3D.GetPlane(), internalPoint, tolerance);
        }

        /// <summary>The group re-unioned in <paramref name="plane"/> with its degenerate inner loops (fewer than 3 points) removed,
        /// or null when the repair is not proven safe: no face, an invalid face, or a group member's internal point left
        /// uncovered.</summary>
        private static List<Face3D> Repair(
            List<SnappedPanel> group,
            Plane plane,
            double tolerance,
            System.Func<List<Geometry.Planar.Face2D>, double, List<Geometry.Planar.Face2D>> union,
            out int removedLoops)
        {
            removedLoops = 0;
            List<Geometry.Planar.Face2D> face2Ds = union(group.ConvertAll(x => plane.Convert(x.Face3D)), tolerance);
            if (face2Ds == null || face2Ds.Count == 0)
            {
                return null;
            }

            List<Face3D> result = new List<Face3D>();
            foreach (Geometry.Planar.Face2D face2D in face2Ds)
            {
                if (face2D?.ExternalEdge2D == null)
                {
                    return null;
                }

                List<Geometry.Planar.IClosed2D> internalEdge2Ds = face2D.InternalEdge2Ds ?? new List<Geometry.Planar.IClosed2D>();
                List<Geometry.Planar.IClosed2D> kept = internalEdge2Ds.Where(x => !DegenerateLoop(x)).ToList();
                removedLoops += internalEdge2Ds.Count - kept.Count;

                Geometry.Planar.Face2D rebuilt = kept.Count == internalEdge2Ds.Count ? face2D : Geometry.Planar.Create.Face2D(face2D.ExternalEdge2D, kept);
                Face3D face3D = rebuilt != null && Geometry.Planar.Query.IsValid(rebuilt) ? plane.Convert(rebuilt) : null;
                if (face3D == null || !face3D.IsValid())
                {
                    return null;
                }

                result.Add(face3D);
            }

            List<CoverageFace> coverageFaces = CoverageFaces(result);
            return group.All(x => Covered(x.Face3D, coverageFaces, tolerance)) ? result : null;
        }

        /// <summary>
        /// An inner loop that bounds no area: null, or a polyline loop with fewer than 3 points (what SAM's conversion leaves of
        /// a sliver hole whose points fall within the distance tolerance of one another, and what makes the merged face
        /// invalid). Removing it fills nothing. Any loop with 3 or more points is a genuine opening and is kept, whatever its
        /// size: no area is compared with a length.
        /// </summary>
        private static bool DegenerateLoop(Geometry.Planar.IClosed2D closed2D)
        {
            return closed2D == null || (closed2D is Geometry.Planar.ISegmentable2D segmentable2D && (segmentable2D.GetPoints()?.Count ?? 0) < 3);
        }

        /// <summary>
        /// Mean width of a face [m]: twice its area over the length of its boundary (outer and inner loops). Zero for a face
        /// collapsed to a line; positive infinity when a loop has no measurable length (never treated as collapsed).
        /// </summary>
        private static double MeanWidth(Face3D face3D)
        {
            List<IClosedPlanar3D> loops = new List<IClosedPlanar3D> { face3D.GetExternalEdge3D() };
            loops.AddRange(face3D.GetInternalEdge3Ds() ?? new List<IClosedPlanar3D>());

            double perimeter = loops.Sum(x => (x as ISegmentable3D)?.GetLength() ?? double.NaN);
            if (double.IsNaN(perimeter))
            {
                return double.PositiveInfinity;
            }

            return perimeter > 0 ? 2 * face3D.GetArea() / perimeter : 0;
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
