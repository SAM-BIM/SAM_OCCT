// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Stage C - HEAL: re-attach input faces the native MakerVolume dropped and stitch the residual free
    /// boundaries the volume build left open. The kernel returns only faces that bound a closed cell, so a
    /// face whose cell fails to form (a stepped/tilted region it cannot close, or a lid the cells cap off)
    /// is silently discarded, leaving a hole (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §D/§F).
    /// <para>
    /// Phase 5c adds <see cref="RetainDroppedV2"/>: the managed-pipeline entry that re-adds the ORIGINAL
    /// CLEAN geometry (the SnapStage output) for every input source the native resolve genuinely dropped -
    /// detected map-side (<c>SourceMap.FacesOf(source).Count == 0</c>), not by a geometric nearest-source
    /// heuristic - behind duplicate/degenerate/area filters, tagging each re-added face
    /// <see cref="Provenance.DroppedRetained"/> and emitting a <see cref="DiagnosticCode.DroppedFace"/>
    /// diagnostic (never a silent re-add). The pre-Phase-5 <see cref="RetainDropped"/> - a filter-only
    /// variant that re-adds a supplied candidate list verbatim - is kept for callers that already hold the
    /// exact face set to filter. See docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §H.
    /// </para>
    /// <para>
    /// Phase 5d adds <see cref="SewV2"/>: the capped adaptive residual sew extracted from
    /// <see cref="ResolveStage"/>. The native sew stays GLOBAL (there is no per-loop native sew); per-loop
    /// logic is acceptance EVIDENCE only. The sew tolerance is capped below half the closest near-parallel
    /// gap (<see cref="MinPairSeparation"/>) so a global sew cannot fuse a genuine double wall while closing
    /// an unrelated slot, before/after naked wires are matched into Closed/Persisting/New loops, and a
    /// fusion veto rejects the whole sew if any near-parallel pair fused. The sew is adopted only when the
    /// naked count strictly drops with no veto (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §F/§I).
    /// </para>
    /// </summary>
    public static class HealStage
    {
        /// <summary>Default fraction of the closest near-parallel gap the sew tolerance is capped at (§F).</summary>
        public const double DEFAULT_SewSafetyFactor = 0.5;

        /// <summary>Default hard upper clamp (metres) on the adaptive sew tolerance, independent of the gap cap (§F).</summary>
        public const double DEFAULT_SewHardClamp = 0.3;

        /// <summary>Outcome of <see cref="RetainDropped"/>: the augmented face set plus the faces that were re-added.</summary>
        public class Result
        {
            public List<Face3D> ResolvedFace3Ds { get; set; } = new List<Face3D>();

            public List<Face3D> RetainedFace3Ds { get; } = new List<Face3D>();
        }

        /// <summary>
        /// Outcome of <see cref="RetainDroppedV2"/>: the clean faces re-added (in retain order) plus, index-aligned,
        /// the dropped source index/indices each one recovers - so the caller can record
        /// <see cref="Provenance.DroppedRetained"/> at the retained face's eventual output ordinal (which
        /// <see cref="RetainDroppedV2"/> cannot know, since the final append order is decided downstream).
        /// </summary>
        public sealed class RetainDroppedResult
        {
            public List<Face3D> RetainedFace3Ds { get; } = new List<Face3D>();

            public List<List<int>> RetainedSourceIndices { get; } = new List<List<int>>();
        }

        /// <summary>
        /// RetainDropped v2 (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §H) - the managed-pipeline heal.
        /// For every input source the native resolve produced no output face for
        /// (<c><paramref name="sourceMap"/>.FacesOf(source).Count == 0</c> - map-driven, not a geometric
        /// nearest-source guess), re-adds the ORIGINAL CLEAN geometry (the SnapStage clean face carrying that
        /// source), NOT the conditioned/extended face the resolve saw. Each candidate must pass the safety
        /// filters before it is retained: a valid face, area &gt;= <paramref name="minArea"/>, and not already
        /// represented in <paramref name="resolvedFace3Ds"/> or an earlier-retained face (kills
        /// duplicate/degenerate/double-cover). Every retained face is tagged for
        /// <see cref="Provenance.DroppedRetained"/> (the caller records it at the output ordinal) and a
        /// <see cref="DiagnosticCode.DroppedFace"/> Info diagnostic names the recovered source(s) - never a
        /// silent re-add.
        /// </summary>
        /// <param name="resolvedFace3Ds">The native resolve's output faces (the "represented" set to dedup against).</param>
        /// <param name="cleanFace3Ds">The SnapStage clean faces (the "original clean geometry" source, world frame).</param>
        /// <param name="sourceIndicesPerCleanFace">Per clean face, the input source indices it carries (index-aligned to <paramref name="cleanFace3Ds"/>).</param>
        /// <param name="sourceMap">Source -&gt; resolved-output attribution; a source with an empty <c>FacesOf</c> was dropped.</param>
        /// <param name="diagnostics">Accumulates a <see cref="DiagnosticCode.DroppedFace"/> Info per retained face.</param>
        /// <param name="minArea">Minimum retained-face area (m2); defaults to the air-panel floor (1e-4 m2).</param>
        public static RetainDroppedResult RetainDroppedV2(
            List<Face3D> resolvedFace3Ds,
            List<Face3D> cleanFace3Ds,
            List<List<int>> sourceIndicesPerCleanFace,
            SourceMap sourceMap,
            SolverDiagnostics diagnostics = null,
            double minArea = GapFill.MinPatchArea)
        {
            RetainDroppedResult result = new RetainDroppedResult();
            if (cleanFace3Ds == null || cleanFace3Ds.Count == 0 || sourceMap == null)
            {
                return result;
            }

            // resolved ∪ already-retained: IsRepresented is tested against BOTH, so a dropped source whose
            // clean geometry a resolved face (or an earlier retained face) already covers is not re-added twice.
            List<Face3D> represented = resolvedFace3Ds == null ? new List<Face3D>() : resolvedFace3Ds.ToList();

            for (int k = 0; k < cleanFace3Ds.Count; k++)
            {
                List<int> sources = sourceIndicesPerCleanFace != null && k < sourceIndicesPerCleanFace.Count
                    ? sourceIndicesPerCleanFace[k]
                    : null;
                if (sources == null || sources.Count == 0)
                {
                    continue; // a fabricated clean face (no source identity) is not an input the resolve dropped
                }

                // The sources of THIS clean face the resolve dropped (produced no output face for). A clean face
                // mixing a dropped and a represented source records only the dropped one(s), so a still-represented
                // source is never perturbed into a spurious split.
                List<int> droppedSources = sources
                    .Where(s => s >= 0 && sourceMap.FacesOf(s).Count == 0)
                    .Distinct()
                    .ToList();
                if (droppedSources.Count == 0)
                {
                    continue; // every source of this clean face is represented - nothing to retain
                }

                Face3D cleanFace3D = cleanFace3Ds[k];
                if (cleanFace3D == null || !cleanFace3D.IsValid())
                {
                    continue; // safety: invalid geometry
                }

                if (cleanFace3D.GetArea() < minArea)
                {
                    continue; // safety: degenerate/sliver face below the area floor
                }

                if (Panel3DSnapSolver.IsRepresented(cleanFace3D, represented))
                {
                    continue; // safety: already covered by a resolved or earlier-retained face (duplicate/double-cover)
                }

                result.RetainedFace3Ds.Add(cleanFace3D);
                result.RetainedSourceIndices.Add(droppedSources);
                represented.Add(cleanFace3D); // dedup subsequent candidates against this retained face too

                diagnostics?.Add(SolverStage.Heal, DiagnosticCode.DroppedFace, OcctDiagnosticSeverity.Info,
                    string.Format("RetainDropped: re-added original clean geometry for dropped source(s) {0}.", string.Join(", ", droppedSources)),
                    face3D: cleanFace3D);
            }

            return result;
        }

        /// <summary>
        /// Filter-only RetainDropped (pre-Phase-5 contract, kept for callers that already hold the exact face
        /// set to filter): re-adds every face in <paramref name="candidateFace3Ds"/> that is not represented in
        /// <paramref name="resolvedFace3Ds"/>, using the supplied geometry verbatim. Records the re-added faces
        /// (and, when a <paramref name="sourceMap"/> is supplied, tags them <see cref="Provenance.DroppedRetained"/>
        /// keyed by their output index). Prefer <see cref="RetainDroppedV2"/> in the managed pipeline: it re-adds
        /// the ORIGINAL CLEAN geometry (not the conditioned/extended candidate), detects drops map-side, and adds
        /// area/dedup safety filters.
        /// </summary>
        public static Result RetainDropped(
            List<Face3D> resolvedFace3Ds,
            List<Face3D> candidateFace3Ds,
            SourceMap sourceMap = null)
        {
            Result result = new Result();
            List<Face3D> resolved = resolvedFace3Ds == null ? new List<Face3D>() : resolvedFace3Ds.ToList();
            result.ResolvedFace3Ds = resolved;

            if (candidateFace3Ds == null || candidateFace3Ds.Count == 0)
            {
                return result;
            }

            foreach (Face3D face3D in candidateFace3Ds)
            {
                if (face3D != null && face3D.IsValid() && !Panel3DSnapSolver.IsRepresented(face3D, resolved))
                {
                    result.RetainedFace3Ds.Add(face3D);
                }
            }

            if (result.RetainedFace3Ds.Count != 0)
            {
                int baseIndex = resolved.Count;
                result.ResolvedFace3Ds = resolved.Concat(result.RetainedFace3Ds).ToList();

                for (int i = 0; i < result.RetainedFace3Ds.Count; i++)
                {
                    sourceMap?.RecordFabricated(new FaceKey(baseIndex + i), Provenance.DroppedRetained);
                }
            }

            return result;
        }

        // ---- Phase 5d: capped adaptive residual sew (SewV2) --------------------------------------------

        /// <summary>Outcome of <see cref="SewV2"/>: the (possibly sewn) faces plus the acceptance evidence.</summary>
        public sealed class SewResult
        {
            /// <summary>The faces after the pass: the sewn set when <see cref="Adopted"/>, else the pre-sew input.</summary>
            public List<Face3D> ResolvedFace3Ds { get; set; } = new List<Face3D>();

            /// <summary>True when the capped sew was accepted (naked strictly dropped and no fusion veto).</summary>
            public bool Adopted { get; set; }

            /// <summary>True when the sew was not attempted (no naked edges, empty input, or the cap fell to/below the pre-build floor).</summary>
            public bool Skipped { get; set; }

            /// <summary>True when the fusion veto rejected the sew (a near-parallel pair fused).</summary>
            public bool FusionVetoed { get; set; }

            /// <summary>The capped sewing tolerance used (0 when skipped before the sew ran).</summary>
            public double SewTolerance { get; set; }

            /// <summary>The measured closest near-parallel separation that drove the cap (null when there was none).</summary>
            public double? MinSeparation { get; set; }

            public int NakedBefore { get; set; }

            public int NakedAfter { get; set; }

            public int ClosedLoopCount { get; set; }

            public int PersistingLoopCount { get; set; }

            public int NewLoopCount { get; set; }
        }

        /// <summary>Per-loop bookkeeping from <see cref="ClassifyLoops"/> - acceptance EVIDENCE, not a per-loop native op.</summary>
        public sealed class LoopBookkeeping
        {
            /// <summary>Before-wires with no after-match: the sew closed them.</summary>
            public int Closed { get; set; }

            /// <summary>Before-wires still present after the sew (matched to an after-wire).</summary>
            public int Persisting { get; set; }

            /// <summary>After-wires the sew introduced (no before-match).</summary>
            public List<OcctNakedWire> NewLoops { get; } = new List<OcctNakedWire>();

            /// <summary>Convenience: <c>NewLoops.Count</c>.</summary>
            public int New
            {
                get { return NewLoops.Count; }
            }
        }

        /// <summary>
        /// The capped adaptive residual sew (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §F), extracted
        /// from <see cref="ResolveStage"/>. The native sew stays GLOBAL; per-loop logic is acceptance evidence
        /// only (there is no per-loop native sew). Steps:
        /// <list type="number">
        /// <item>Validate before: naked count + ordered free-boundary wires. No naked edges =&gt; nothing to sew.</item>
        /// <item>Cap the tolerance at min(<paramref name="sewExpandTolerance"/>, <paramref name="hardClamp"/>,
        /// minPairSeparation × <paramref name="sewSafetyFactor"/>). If the cap is at/below the pre-build sew
        /// floor (<c>occtOptions.SewingTolerance</c>), skip - an expanded sew could only risk fusing the close
        /// pair that drove the cap, with nothing left to gain.</item>
        /// <item>Global sew at the capped tolerance; validate after.</item>
        /// <item>Match before↔after naked wires into Closed / Persisting / New loops (evidence + diagnostics).</item>
        /// <item>Fusion veto: every pre-sew near-parallel pair within 2× the tolerance must survive; any fusion
        /// rejects the whole sew.</item>
        /// <item>Accept iff the naked count strictly drops AND no fusion veto (New loops are justified only by
        /// that strict net drop). Otherwise keep the pre-sew faces and emit a <see cref="DiagnosticCode.RejectedSew"/>.</item>
        /// </list>
        /// </summary>
        public static SewResult SewV2(
            List<Face3D> resolvedFace3Ds,
            OcctBuildOptions occtOptions,
            double sewExpandTolerance,
            double sewSafetyFactor = DEFAULT_SewSafetyFactor,
            SolverDiagnostics diagnostics = null,
            double hardClamp = DEFAULT_SewHardClamp)
        {
            SewResult result = new SewResult();
            List<Face3D> resolved = resolvedFace3Ds == null ? new List<Face3D>() : resolvedFace3Ds.ToList();
            result.ResolvedFace3Ds = resolved;

            if (resolved.Count == 0 || occtOptions == null)
            {
                result.Skipped = true;
                return result;
            }

            // 1. Validate before: naked count + the ordered free-boundary wires (the "before" bookkeeping side).
            int nakedBefore = ValidateWires(resolved, occtOptions, out List<OcctNakedWire> wiresBefore);
            result.NakedBefore = nakedBefore;
            result.NakedAfter = nakedBefore;
            if (nakedBefore <= 0)
            {
                result.Skipped = true; // watertight already - nothing to sew
                return result;
            }

            // 2. Tolerance cap: never bridge more than half the closest near-parallel gap, so a global sew
            //    cannot fuse a genuine double wall while it closes an unrelated residual slot (§A.3/§F).
            double preBuildFloor = occtOptions.SewingTolerance; // what the pre-build sew already closed (~1 cm)
            double? minSep = MinPairSeparation.Compute(resolved);
            double sewTol = SewToleranceCap(sewExpandTolerance, sewSafetyFactor, minSep, hardClamp, preBuildFloor);
            result.SewTolerance = sewTol;
            result.MinSeparation = minSep;

            if (sewTol <= preBuildFloor)
            {
                result.Skipped = true;
                diagnostics?.Add(SolverStage.Heal, DiagnosticCode.RejectedSew, OcctDiagnosticSeverity.Info,
                    string.Format("SewV2 skipped: capped tolerance {0:0.####} m at/below the pre-build sew floor {1:0.####} m (min pair separation {2}).",
                        sewTol, preBuildFloor, minSep == null ? "none" : minSep.Value.ToString("0.####")));
                return result;
            }

            // 3. Global sew at the capped tolerance (the native sew is global - the same call ResolveStage made).
            OcctBuildOptions sewOptions = new OcctBuildOptions(occtOptions)
            {
                SewBeforeBuild = true,
                SewingTolerance = sewTol
            };

            List<Shell> sewnShells = GeometryQuery.Sew(resolved, out OcctCellComplexResult sewResult, sewOptions, false);
            sewResult?.Dispose();

            List<Face3D> sewn = sewnShells == null
                ? null
                : sewnShells.Where(x => x != null).SelectMany(x => x.Face3Ds ?? new List<Face3D>()).Where(x => x != null && x.IsValid()).ToList();

            if (sewn == null || sewn.Count == 0)
            {
                return result; // sew produced nothing usable - keep pre-sew (not adopted)
            }

            // 4. Validate after; per-loop bookkeeping (evidence, not a per-loop native operation).
            int nakedAfter = ValidateWires(sewn, occtOptions, out List<OcctNakedWire> wiresAfter);
            result.NakedAfter = nakedAfter;

            LoopBookkeeping loops = ClassifyLoops(wiresBefore, wiresAfter, sewTol);
            result.ClosedLoopCount = loops.Closed;
            result.PersistingLoopCount = loops.Persisting;
            result.NewLoopCount = loops.New;

            // 5. Fusion veto: any pre-sew near-parallel pair that did not survive the sew rejects the whole sew.
            if (DetectFusion(resolved, sewn, sewTol, out MinPairSeparation.Pair fusedPair))
            {
                result.FusionVetoed = true;
                diagnostics?.Add(SolverStage.Heal, DiagnosticCode.RejectedSew, OcctDiagnosticSeverity.Warning,
                    string.Format("SewV2 rejected: sew fused distinct near-parallel faces {0} m apart (tolerance {1:0.####} m); pre-sew faces kept.",
                        fusedPair.Separation.ToString("0.####"), sewTol));
                return result; // keep pre-sew
            }

            // 6. Whole-result acceptance: the naked count must strictly drop (which alone justifies any New loops).
            if (nakedAfter < nakedBefore)
            {
                result.ResolvedFace3Ds = sewn;
                result.Adopted = true;

                foreach (OcctNakedWire newLoop in loops.NewLoops)
                {
                    diagnostics?.Add(SolverStage.Heal, DiagnosticCode.NakedLoop, OcctDiagnosticSeverity.Warning,
                        "SewV2: sew introduced a new naked loop (net naked count still strictly dropped; inspect).",
                        newLoop.Point3Ds);
                }

                diagnostics?.Add(SolverStage.Heal, DiagnosticCode.NakedLoop, OcctDiagnosticSeverity.Info,
                    string.Format("SewV2 adopted at {0:0.####} m: naked {1}->{2}; {3} closed, {4} persisting, {5} new loop(s).",
                        sewTol, nakedBefore, nakedAfter, loops.Closed, loops.Persisting, loops.New));
            }
            else
            {
                diagnostics?.Add(SolverStage.Heal, DiagnosticCode.RejectedSew, OcctDiagnosticSeverity.Info,
                    string.Format("SewV2 rejected at {0:0.####} m: naked {1}->{2} (not reduced); pre-sew faces kept.",
                        sewTol, nakedBefore, nakedAfter));
            }

            return result;
        }

        /// <summary>
        /// The Phase 5d sew-tolerance cap (pure, unit-testable): the base tolerance is
        /// <paramref name="sewExpandTolerance"/> floored at the pre-build sew (<paramref name="preBuildFloor"/>)
        /// and clamped at <paramref name="hardClamp"/>, then - when a near-parallel pair exists - capped at
        /// <paramref name="minSeparation"/> × <paramref name="sewSafetyFactor"/>. The result may fall BELOW the
        /// floor (when the gap cap is tighter than the pre-build sew); the caller treats
        /// <c>cap &lt;= preBuildFloor</c> as "skip the sew" (docs/P5 review §F).
        /// </summary>
        public static double SewToleranceCap(double sewExpandTolerance, double sewSafetyFactor, double? minSeparation, double hardClamp = DEFAULT_SewHardClamp, double preBuildFloor = 0.01)
        {
            double cap = System.Math.Min(System.Math.Max(sewExpandTolerance, preBuildFloor), hardClamp);
            if (minSeparation != null)
            {
                cap = System.Math.Min(cap, minSeparation.Value * sewSafetyFactor);
            }

            return cap;
        }

        /// <summary>
        /// Matches before↔after naked wires by centroid proximity and bounding-box-diagonal similarity into
        /// Closed (before wire the sew removed), Persisting (before wire still present) and New (after wire the
        /// sew introduced) - the Phase 5d per-loop acceptance EVIDENCE (§F). Pure/managed and greedy: each
        /// after-wire matches at most one before-wire, nearest first. Match window = max(2× the sew tolerance, 0.1 m).
        /// </summary>
        public static LoopBookkeeping ClassifyLoops(IReadOnlyList<OcctNakedWire> before, IReadOnlyList<OcctNakedWire> after, double sewTolerance)
        {
            LoopBookkeeping result = new LoopBookkeeping();
            List<OcctNakedWire> beforeList = before?.Where(x => x != null).ToList() ?? new List<OcctNakedWire>();
            List<OcctNakedWire> afterList = after?.Where(x => x != null).ToList() ?? new List<OcctNakedWire>();

            double matchTol = System.Math.Max(2 * sewTolerance, 0.1);

            Point3D[] afterCentroids = afterList.Select(WireCentroid).ToArray();
            double[] afterDiagonals = afterList.Select(WireDiagonal).ToArray();
            bool[] afterMatched = new bool[afterList.Count];

            foreach (OcctNakedWire beforeWire in beforeList)
            {
                Point3D beforeCentroid = WireCentroid(beforeWire);
                double beforeDiagonal = WireDiagonal(beforeWire);

                int match = -1;
                double bestDistance = double.MaxValue;
                for (int a = 0; a < afterList.Count; a++)
                {
                    if (afterMatched[a] || afterCentroids[a] == null || beforeCentroid == null)
                    {
                        continue;
                    }

                    double distance = beforeCentroid.Distance(afterCentroids[a]);
                    if (distance <= matchTol && System.Math.Abs(beforeDiagonal - afterDiagonals[a]) <= matchTol && distance < bestDistance)
                    {
                        bestDistance = distance;
                        match = a;
                    }
                }

                if (match >= 0)
                {
                    afterMatched[match] = true;
                    result.Persisting++;
                }
                else
                {
                    result.Closed++;
                }
            }

            for (int a = 0; a < afterList.Count; a++)
            {
                if (!afterMatched[a])
                {
                    result.NewLoops.Add(afterList[a]);
                }
            }

            return result;
        }

        /// <summary>
        /// The Phase 5d fusion veto (pure/managed, unit-testable): true when any pre-sew near-parallel pair
        /// within 2× <paramref name="sewTolerance"/> did NOT survive the sew - either face no longer represented
        /// in <paramref name="sewnFace3Ds"/>, or the pair's local area was not conserved (±5%), i.e. two distinct
        /// parallel faces fused into one. <paramref name="fusedPair"/> reports the first offending pair (§F).
        /// </summary>
        public static bool DetectFusion(IReadOnlyList<Face3D> preSewFace3Ds, IReadOnlyList<Face3D> sewnFace3Ds, double sewTolerance, out MinPairSeparation.Pair fusedPair)
        {
            fusedPair = default;
            if (preSewFace3Ds == null || sewnFace3Ds == null)
            {
                return false;
            }

            List<Face3D> sewn = sewnFace3Ds.Where(x => x != null).ToList();
            foreach (MinPairSeparation.Pair pair in MinPairSeparation.Pairs(preSewFace3Ds, MinPairSeparation.DEFAULT_MinSeparation, 2 * sewTolerance))
            {
                Face3D a = pair.IndexA < preSewFace3Ds.Count ? preSewFace3Ds[pair.IndexA] : null;
                Face3D b = pair.IndexB < preSewFace3Ds.Count ? preSewFace3Ds[pair.IndexB] : null;

                bool survivesA = a != null && Panel3DSnapSolver.IsRepresented(a, sewn);
                bool survivesB = b != null && Panel3DSnapSolver.IsRepresented(b, sewn);

                double preArea = (a?.GetArea() ?? 0) + (b?.GetArea() ?? 0);
                double postArea = LocalArea(a, b, sewn, sewTolerance);
                bool areaConserved = preArea <= 0 || postArea >= 0.95 * preArea;

                if (!(survivesA && survivesB && areaConserved))
                {
                    fusedPair = pair;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Validates <paramref name="face3Ds"/>, returning the naked-edge count and the ordered free-boundary
        /// wires. Returns <see cref="int.MaxValue"/> when the validator is unavailable, so a sew is never accepted
        /// on a count it could not measure.</summary>
        private static int ValidateWires(List<Face3D> face3Ds, OcctBuildOptions options, out List<OcctNakedWire> wires)
        {
            wires = new List<OcctNakedWire>();
            if (face3Ds == null || face3Ds.Count == 0)
            {
                return 0;
            }

            GeometryQuery.Validate(face3Ds, out OcctValidationReport report, out OcctCellComplexResult result, options, false);
            result?.Dispose();
            wires = report?.NakedWires?.ToList() ?? new List<OcctNakedWire>();
            return report?.CountOf(OcctValidationIssueCategory.NakedEdge) ?? int.MaxValue;
        }

        /// <summary>Total area of the sewn faces occupying the local A∪B slab (near-parallel to A, centroid inside the
        /// pair's combined bounding box grown by 2× the tolerance) - the "area conserved" side of the fusion veto.</summary>
        private static double LocalArea(Face3D a, Face3D b, List<Face3D> sewn, double sewTolerance)
        {
            Plane planeA = a?.GetPlane();
            BoundingBox3D region = CombinedBox(a, b);
            if (planeA == null || region == null)
            {
                return 0;
            }

            Vector3D normalA = planeA.Normal.Unit;
            double grow = System.Math.Max(2 * sewTolerance, 0.05);
            double total = 0;
            foreach (Face3D face3D in sewn)
            {
                Plane planeF = face3D?.GetPlane();
                if (planeF == null || System.Math.Abs(normalA.DotProduct(planeF.Normal.Unit)) < 0.99)
                {
                    continue; // not a skin of this near-parallel pair
                }

                Point3D centre = face3D.GetBoundingBox()?.GetCentroid();
                if (centre != null && InBox(region, centre, grow))
                {
                    total += face3D.GetArea();
                }
            }

            return total;
        }

        /// <summary>The bounding box enclosing both faces (the pair's local region), or null when neither has a box.</summary>
        private static BoundingBox3D CombinedBox(Face3D a, Face3D b)
        {
            List<Point3D> corners = new List<Point3D>();
            BoundingBox3D boxA = a?.GetBoundingBox();
            BoundingBox3D boxB = b?.GetBoundingBox();
            if (boxA != null)
            {
                corners.Add(boxA.Min);
                corners.Add(boxA.Max);
            }

            if (boxB != null)
            {
                corners.Add(boxB.Min);
                corners.Add(boxB.Max);
            }

            return corners.Count == 0 ? null : new BoundingBox3D(corners);
        }

        /// <summary>True when <paramref name="point3D"/> lies inside <paramref name="box"/> grown by <paramref name="tolerance"/>.</summary>
        private static bool InBox(BoundingBox3D box, Point3D point3D, double tolerance)
        {
            Point3D min = box.Min;
            Point3D max = box.Max;
            return point3D.X >= min.X - tolerance && point3D.X <= max.X + tolerance
                && point3D.Y >= min.Y - tolerance && point3D.Y <= max.Y + tolerance
                && point3D.Z >= min.Z - tolerance && point3D.Z <= max.Z + tolerance;
        }

        /// <summary>Arithmetic mean of a wire's vertices, or null for an empty wire.</summary>
        private static Point3D WireCentroid(OcctNakedWire wire)
        {
            IReadOnlyList<Point3D> point3Ds = wire?.Point3Ds;
            if (point3Ds == null || point3Ds.Count == 0)
            {
                return null;
            }

            double x = 0, y = 0, z = 0;
            foreach (Point3D point3D in point3Ds)
            {
                x += point3D.X;
                y += point3D.Y;
                z += point3D.Z;
            }

            return new Point3D(x / point3Ds.Count, y / point3Ds.Count, z / point3Ds.Count);
        }

        /// <summary>Diagonal length of a wire's bounding box (its size signature for loop matching), or 0 when empty.</summary>
        private static double WireDiagonal(OcctNakedWire wire)
        {
            IReadOnlyList<Point3D> point3Ds = wire?.Point3Ds;
            if (point3Ds == null || point3Ds.Count == 0)
            {
                return 0;
            }

            BoundingBox3D box = new BoundingBox3D(new List<Point3D>(point3Ds));
            return box.Min.Distance(box.Max);
        }
    }
}
