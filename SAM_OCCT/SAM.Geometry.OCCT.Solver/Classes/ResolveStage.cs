// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

using GeometryQuery = SAM.Geometry.OCCT.Query;
using GeometryCreate = SAM.Geometry.OCCT.Create;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Stage B (native half) - RESOLVE: hand the conditioned face set to the OCCT kernel. Coplanar
    /// pre-merge → MakerVolume cell build → coplanar post-merge → adaptive residual sew → validate →
    /// gap-fill candidate loops (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §D/§F). Extracted
    /// verbatim from the pre-Phase-2 <c>Panel3DSnapSolver.Resolve</c> as a façade seam; the sew and
    /// gap-fill sub-passes stay here in Phase 2 (they are intertwined with the native resolve and its
    /// validation) and move to <see cref="HealStage"/> when Phase 5 reworks them with per-loop
    /// acceptance.
    /// </summary>
    public static class ResolveStage
    {
        /// <summary>Outputs of <see cref="Resolve"/> - what the pre-Phase-2 method wrote to instance fields.</summary>
        public class Result
        {
            public List<Face3D> ResolvedFace3Ds { get; set; } = new List<Face3D>();

            public bool NativeResolved { get; set; }

            public int ResolvedCellCount { get; set; }

            public List<Face3D> BucketMergedFace3Ds { get; set; } = new List<Face3D>();

            public List<Point3D> NakedEdgePoint3Ds { get; set; } = new List<Point3D>();

            public List<Face3D> HoleFillFace3Ds { get; set; } = new List<Face3D>();
        }

        /// <summary>
        /// Runs the native resolve over <paramref name="snappedFace3Ds"/>. Behaviour is identical to the
        /// pre-Phase-2 inline method; the read-only flags it used (tolerance angle, sew, fill-holes) are now
        /// explicit parameters and its outputs are returned rather than written to instance fields. When the
        /// native kernel is unavailable the result carries <see cref="Result.NativeResolved"/> = false and no
        /// resolved faces (the façade then keeps the managed snap result).
        /// </summary>
        public static Result Resolve(
            List<Face3D> snappedFace3Ds,
            OcctBuildOptions options,
            double toleranceAngle,
            bool sewResidualGaps,
            double sewExpandTolerance,
            bool fillHoles)
        {
            Result result = new Result();
            if (snappedFace3Ds == null || snappedFace3Ds.Count == 0)
            {
                return result;
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

            // Native coplanar pre-merge BEFORE the volume build. After Step 2's fill/extend, extended walls
            // and grown caps overlap coplanar neighbours; collapsing those overlaps (the share of
            // self-intersections MakerVolume cannot otherwise digest) is what lets the kernel form a zoned
            // cell complex instead of a single envelope cell.
            List<Face3D> buildFace3Ds = snappedFace3Ds;
            List<Face3D> preMerged = GeometryQuery.MergeCoplanarFace3Ds(snappedFace3Ds, out OcctCellComplexResult preMergeResult, toleranceAngle, options);
            preMergeResult?.Dispose();
            if (preMerged != null && preMerged.Count != 0)
            {
                buildFace3Ds = preMerged;
            }

            result.BucketMergedFace3Ds = buildFace3Ds; // expose the MakerVolume input for debugging

            // MakerVolume: split panels at mutual intersections and resolve 3-way junctions.
            List<Shell> shells = GeometryCreate.Shells(buildFace3Ds, out OcctCellComplexResult cellResult, options);
            if (cellResult == null || !cellResult.NativeAvailable)
            {
                // Native kernel not present (e.g. non-Windows agent): keep the managed snap result.
                cellResult?.Dispose();
                return result;
            }

            result.NativeResolved = true;
            result.ResolvedCellCount = cellResult.Cells?.Count ?? 0;

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
            List<Face3D> merged = GeometryQuery.MergeCoplanarFace3Ds(resolved, out OcctCellComplexResult mergeResult, toleranceAngle, options);
            mergeResult?.Dispose();
            if (merged != null && merged.Count != 0)
            {
                resolved = merged;
            }

            // ---- Adaptive native sew pass ----
            // The pre-build sew (SewingTolerance ~1 cm) only bridges sub-cm gaps; the floor/wall slot gaps
            // that survive into the resolved faces are wider. Re-sew the resolved faces at an expanded
            // tolerance to stitch the two free edges of each slot directly - no fabricated air face - and keep
            // the sewn result only when it strictly reduces the naked-edge count, so over-merging unrelated
            // near edges is rejected. GapFill below then handles only what sewing could not close.
            if (sewResidualGaps)
            {
                int nakedBefore = NakedEdgeCount(resolved, options);
                if (nakedBefore > 0)
                {
                    double sewTolerance = System.Math.Min(System.Math.Max(sewExpandTolerance, options.SewingTolerance), 0.3);
                    OcctBuildOptions sewOptions = new OcctBuildOptions(options)
                    {
                        SewBeforeBuild = true,
                        SewingTolerance = sewTolerance
                    };

                    List<Shell> sewnShells = GeometryQuery.Sew(resolved, out OcctCellComplexResult sewResult, sewOptions, false);
                    sewResult?.Dispose();

                    List<Face3D> sewn = sewnShells == null
                        ? null
                        : sewnShells.Where(x => x != null).SelectMany(x => x.Face3Ds ?? new List<Face3D>()).Where(x => x != null && x.IsValid()).ToList();

                    if (sewn != null && sewn.Count != 0 && NakedEdgeCount(sewn, options) < nakedBefore)
                    {
                        resolved = sewn;
                    }
                }
            }

            result.ResolvedFace3Ds = resolved;

            // Report naked (free) boundary edges - true boundaries vs unresolved gaps.
            GeometryQuery.Validate(resolved, out OcctValidationReport report, out OcctCellComplexResult validateResult, options, false);
            validateResult?.Dispose();
            if (report != null)
            {
                result.NakedEdgePoint3Ds = report
                    .IssuesOf(OcctValidationIssueCategory.NakedEdge)
                    .Where(x => x?.Location != null)
                    .Select(x => x.Location)
                    .ToList();
            }

            // Close the residual holes: build a Face3D over each naked-boundary loop. These are added to the
            // air-panel candidates so every space is fully enclosed.
            if (fillHoles && result.NakedEdgePoint3Ds.Count != 0)
            {
                List<Face3D> gapFace3Ds = GapFill.NakedLoopFace3Ds(resolved, result.NakedEdgePoint3Ds, 0.01);
                if (gapFace3Ds.Count != 0)
                {
                    result.HoleFillFace3Ds = gapFace3Ds;
                }
            }

            return result;
        }

        /// <summary>
        /// Counts the naked (free) boundary edges in a resolved face set via the native validator. Used by the
        /// adaptive sew pass (accept a re-sew only when it strictly reduces the count) and by the raw-first
        /// adoption gate. Returns <see cref="int.MaxValue"/> when the validator is unavailable, so a sew is
        /// never accepted on a count it could not measure.
        /// </summary>
        public static int NakedEdgeCount(List<Face3D> face3Ds, OcctBuildOptions options)
        {
            if (face3Ds == null || face3Ds.Count == 0)
            {
                return 0;
            }

            GeometryQuery.Validate(face3Ds, out OcctValidationReport report, out OcctCellComplexResult result, options, false);
            result?.Dispose();
            return report?.CountOf(OcctValidationIssueCategory.NakedEdge) ?? int.MaxValue;
        }
    }
}
