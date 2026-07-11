// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

using GeometryCreate = SAM.Geometry.OCCT.Create;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Diagnosis-driven closure (Phase 5e, docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §E; owner
    /// engagement refinement 2026-07-03). The 3D analogue of the 2D <c>SAM.Analytical.Solver.AutoTuneSolver</c>:
    /// a bounded, best-effort loop that wraps <see cref="Panel3DSnapSolver"/>. It runs a baseline raw-first
    /// solve and engages when the baseline leaves naked (free) boundary edges OR closes only by fabricating
    /// GapFill/HoleFill patches (a last-resort step measured extension should replace where it can). It
    /// attributes the residual naked loops (<see cref="LoopAttribution"/>) and the walls abutting each
    /// fabricated patch to their source panels, raises the <c>MaxExtend</c> permission along the ladder
    /// [0.5, 0.75, 1.0, 1.5] on ONLY those culprit sources, and re-solves through the managed pipeline
    /// (<see cref="Panel3DSnapSolver.ForceManagedPipeline"/> - the raw path is not re-tried per round).
    /// A round is adopted only when <see cref="IsAcceptableRound"/> holds - no <see cref="ClosureSignature3D"/>
    /// regression, no sliver rise, new cells proven adjacent to a loop that closed
    /// (<see cref="CellIncreaseAdjacent"/>), and progress: the naked count strictly drops (naked-driven) or
    /// stays 0 while the fabricated patch count strictly drops (fabrication-driven). Otherwise the round is
    /// rejected and the loop stops. It never throws for unresolved loops - the best-effort result is always
    /// returned with residual <see cref="NakedWires"/> / patches and diagnostics.
    /// </summary>
    /// <remarks>
    /// Operates on <see cref="Face3D"/> + per-source parameters (the <see cref="Panel3DSnapSolver"/> inputs),
    /// not on <c>Panel</c>; the analytical <c>Modify.AutoTune3D</c> entry wraps it and reconstructs panels.
    /// The ladder raises the reach <em>permission</em> only; the actual extension stays measured-to-target
    /// (the pipeline's <see cref="ConditionStage"/> grows a wall to its diagnosed meeting object), so an
    /// escalated wall cannot tunnel through a parallel room. Because an unchanged parameter set yields
    /// identical geometry (Snap is a fixed-point iteration), only the escalated culprit panels move between
    /// rounds. No native ABI is used beyond the existing wrappers.
    /// </remarks>
    public class AutoTune3DSolver
    {
        /// <summary>Epsilon (metres) for the strict "next ladder rung above the current reach" comparison, so a
        /// reach already sitting on a rung advances to the next one instead of re-selecting itself.</summary>
        private const double LadderEpsilon = 1e-6;

        /// <summary>Window (metres) for matching a before-wire to an after-wire (centroid + bbox-diagonal), and for
        /// matching a carried-over cell centre between rounds. Mirrors the <see cref="HealStage.ClassifyLoops"/> floor.</summary>
        private const double LoopMatchTolerance = 0.1;

        /// <summary>Minimum patch area (m2) to count as a fabricated GapFill/HoleFill patch - the air-panel floor
        /// (1 cm2) below which a patch is float-noise, matching the analytical air-panel emission.</summary>
        private const double MinAirArea = 1e-4;

        /// <summary>Bounding-box grow (metres) for an input face to count as adjacent to a patch (a wall abutting
        /// the gap the patch fills - the wall AutoTune raises so measured extension can close it).</summary>
        private const double PatchAdjacency = 0.1;

        private readonly List<Face3D> face3Ds;
        private readonly List<double> bucketSizes;
        private readonly List<double> weights;
        private readonly List<double> maxExtensions;

        // ---- Pass-through knobs (configured by the analytical wrapper to mirror Solve3D exactly) ----

        /// <summary>The building/level "up" axis threaded to each <see cref="Panel3DSnapSolver"/> (tilted-level frame).</summary>
        public Vector3D Up { get; set; }

        /// <summary>Threaded to each <see cref="Panel3DSnapSolver.AlignColinearOffset"/>.</summary>
        public double AlignColinearOffset { get; set; } = 0.3;

        /// <summary>Threaded to each <see cref="Panel3DSnapSolver.NormalizeCapOffset"/>.</summary>
        public double NormalizeCapOffset { get; set; } = 0.3;

        /// <summary>Threaded to each <see cref="Panel3DSnapSolver.BucketBetweenLevels"/>.</summary>
        public double BucketBetweenLevels { get; set; } = 0.0;

        /// <summary>Threaded to each <see cref="Panel3DSnapSolver.FillMargin"/>.</summary>
        public double FillMargin { get; set; } = 0.5;

        /// <summary>Threaded to each <see cref="Panel3DSnapSolver.DirectionalCapGrow"/>.</summary>
        public bool DirectionalCapGrow { get; set; } = true;

        /// <summary>Threaded to each <see cref="Panel3DSnapSolver.DoubleWallGap"/> (explicit double-wall
        /// consolidation; default 0 = off).</summary>
        public double DoubleWallGap { get; set; } = 0.0;

        // ---- Outputs (mirror Panel3DSnapSolver's public surface for the adopted best-effort result) ----

        /// <summary>The adopted resolved output faces (baseline, or the last accepted escalation round).</summary>
        public List<Face3D> ResolvedFace3Ds { get; private set; } = new List<Face3D>();

        /// <summary>Source attribution of the adopted result (composed native history / geometric fallback).</summary>
        public SourceMap SourceMap { get; private set; } = new SourceMap();

        /// <summary>Closure signature of the adopted result; null when the native kernel was unavailable.</summary>
        public ClosureSignature3D Signature { get; private set; }

        /// <summary>Naked (free) boundary wires of the adopted result - residual loops when closure did not complete.</summary>
        public List<OcctNakedWire> NakedWires { get; private set; } = new List<OcctNakedWire>();

        /// <summary>Naked (free) boundary edge points of the adopted result.</summary>
        public List<Point3D> NakedEdgePoint3Ds { get; private set; } = new List<Point3D>();

        /// <summary>Gap-fill patch faces of the adopted result (air-panel candidates).</summary>
        public List<Face3D> HoleFillFace3Ds { get; private set; } = new List<Face3D>();

        /// <summary>Escalation, rejection and residual-loop diagnostics, plus the adopted solve's own events.</summary>
        public SolverDiagnostics Diagnostics { get; private set; } = new SolverDiagnostics();

        /// <summary>Number of escalation rounds attempted (each a full managed re-solve). Bounded by <c>MaxRounds</c>.</summary>
        public int Rounds { get; private set; }

        /// <summary>Number of escalation rounds accepted (a subset of <see cref="Rounds"/>).</summary>
        public int RoundsAccepted { get; private set; }

        /// <summary>Cell count of the adopted result.</summary>
        public int ResolvedCellCount { get; private set; }

        /// <summary>Whether the native resolve ran for the adopted result.</summary>
        public bool NativeResolved { get; private set; }

        /// <summary>Final per-source <c>MaxExtend</c> reach after any accepted escalations (index-aligned to the
        /// input faces) - the reach that actually produced the adopted geometry, for downstream stamping.</summary>
        public IReadOnlyList<double> MaxExtensions { get; private set; }

        /// <summary>Final per-source <c>BucketSize</c> after any accepted bucket escalation (default OFF, so this is
        /// the input buckets unchanged) - index-aligned to the input faces.</summary>
        public IReadOnlyList<double> BucketSizes { get; private set; }

        public AutoTune3DSolver(
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

        /// <summary>
        /// Runs the baseline solve and, while naked edges remain and the round budget is not exhausted, the
        /// bounded escalation loop. Best-effort: always returns the adopted state (never throws).
        /// </summary>
        public void Execute(OcctBuildOptions options = null, AutoTune3DOptions tune = null)
        {
            tune = tune == null ? new AutoTune3DOptions() : new AutoTune3DOptions(tune);
            List<double> ladder = (tune.MaxExtendLadder ?? new List<double>()).Where(x => !double.IsNaN(x)).ToList();

            ResolvedFace3Ds = new List<Face3D>();
            SourceMap = new SourceMap();
            Signature = null;
            NakedWires = new List<OcctNakedWire>();
            NakedEdgePoint3Ds = new List<Point3D>();
            HoleFillFace3Ds = new List<Face3D>();
            Diagnostics = new SolverDiagnostics();
            Rounds = 0;
            RoundsAccepted = 0;
            ResolvedCellCount = 0;
            NativeResolved = false;

            int count = face3Ds.Count;
            List<double> currentMaxExtends = Panel3DSnapSolver.AdjustListLength(maxExtensions, count, Panel3DSnapSolver.DEFAULT_MaxExtension);
            List<double> currentBuckets = Panel3DSnapSolver.AdjustListLength(bucketSizes, count, Panel3DSnapSolver.DEFAULT_BucketSize);
            MaxExtensions = currentMaxExtends;
            BucketSizes = currentBuckets;

            OcctBuildOptions effectiveOptions = EffectiveOptions(options);

            // ---- Baseline (raw-first): the default Solve3D result. ----
            Panel3DSnapSolver baseline = NewSolver(currentBuckets, currentMaxExtends, tune, forceManaged: false);
            baseline.Execute(options);
            Panel3DSnapSolver adopted = baseline;
            Adopt(baseline, currentMaxExtends, currentBuckets);

            int baselineNaked = Signature?.NakedEdgeCount ?? -1;
            int baselineFabricated = FabricatedPatchCount(HoleFillFace3Ds);

            if (Signature == null)
            {
                // Native kernel unavailable (e.g. non-Windows agent) - nothing to tune; return the baseline.
                Diagnostics.Add(SolverStage.Heal, DiagnosticCode.AdoptedLevel, OcctDiagnosticSeverity.Info,
                    "AutoTune3D: baseline produced no signature (native kernel unavailable); returning baseline unchanged.");
            }
            else if (Signature.NakedEdgeCount == 0 && baselineFabricated == 0)
            {
                // Cleanly closed (no naked edges, and closed by real geometry - no fabricated patches): leave it.
                Diagnostics.Add(SolverStage.Heal, DiagnosticCode.AdoptedLevel, OcctDiagnosticSeverity.Info,
                    string.Format("AutoTune3D: baseline is cleanly closed ({0}); no escalation needed.", Signature));
            }
            else
            {
                // Engage when naked edges remain OR the baseline closed only by fabricating GapFill/HoleFill
                // patches - in the latter case AutoTune tries measured extension first, to replace fabrication
                // (owner decision: GapFill is a last-resort step; prefer real geometry where escalation can reach).
                adopted = RunEscalationLoop(options, effectiveOptions, tune, ladder, adopted, currentMaxExtends, currentBuckets);
            }

            EmitResidualDiagnostics(baselineNaked, baselineFabricated);

            // Surface the adopted solve's own internal events (sew, gap-fill, retain-dropped, consolidation
            // rebuild) alongside AutoTune's escalation log, so the full closure story is in one place.
            MergeDiagnostics(adopted);
        }

        /// <summary>
        /// The bounded escalation loop. Each round: attribute the residual naked loops to their source
        /// panels, raise those culprits one ladder rung, re-solve through the managed pipeline, and adopt the
        /// round only when <see cref="IsAcceptableRound"/> holds. Returns the finally-adopted solver so its
        /// internal diagnostics can be merged. Mutates <paramref name="currentMaxExtends"/> /
        /// <paramref name="currentBuckets"/> in place as rounds are accepted.
        /// </summary>
        private Panel3DSnapSolver RunEscalationLoop(
            OcctBuildOptions options,
            OcctBuildOptions effectiveOptions,
            AutoTune3DOptions tune,
            List<double> ladder,
            Panel3DSnapSolver adopted,
            List<double> currentMaxExtends,
            List<double> currentBuckets)
        {
            // The adopted state's cells (centre + bounding box), decoded lazily and reused as the next round's "previous".
            List<DecodedCell> adoptedCells = null;

            // Continue while the adopted state still has naked edges OR fabricated patches (which AutoTune tries
            // to replace with measured extension), and the round budget is not exhausted.
            while (Signature != null
                && (Signature.NakedEdgeCount > 0 || FabricatedPatchCount(HoleFillFace3Ds) > 0)
                && Rounds < tune.MaxRounds)
            {
                int previousFabricated = FabricatedPatchCount(HoleFillFace3Ds);
                int roundNumber = Rounds + 1;

                // Culprits: the sources the residual naked loops attribute to (naked-driven), plus the sources
                // whose walls are coplanar-adjacent to a fabricated patch (fabrication-driven - the wall that
                // should extend to fill the gap the patch currently covers). Union of both.
                SortedSet<int> culpritSet = new SortedSet<int>(LoopAttribution.AttributeLoopsToSources(NakedWires, ResolvedFace3Ds, SourceMap, Diagnostics));
                foreach (int source in AttributePatchesToSources(HoleFillFace3Ds, face3Ds))
                {
                    culpritSet.Add(source);
                }

                if (culpritSet.Count == 0)
                {
                    Diagnostics.Add(SolverStage.Heal, DiagnosticCode.NakedLoop, OcctDiagnosticSeverity.Warning,
                        string.Format("AutoTune3D: {0} naked edge(s) and {1} fabricated patch(es) remain but none attribute to a source panel; cannot escalate - stopping.", Signature.NakedEdgeCount, previousFabricated));
                    break;
                }

                // Escalate ONLY the attributed culprit sources, and only the reach permission (measured-to-target
                // does the actual growing). A culprit already at the top rung drops out. currentMaxExtends is left
                // intact (holding the pre-escalation reach) so the accepted-escalation diagnostic can report old->new.
                List<double> escalatedMaxExtends = currentMaxExtends.ToList();
                List<double> escalatedBuckets = currentBuckets.ToList();
                IReadOnlyList<int> escalatedSources = EscalateCulprits(culpritSet.ToList(), ladder, escalatedMaxExtends, escalatedBuckets, tune.EscalateBucket, tune.BucketFactor);

                if (escalatedSources.Count == 0)
                {
                    Diagnostics.Add(SolverStage.Heal, DiagnosticCode.EscalatedPanel, OcctDiagnosticSeverity.Warning,
                        string.Format("AutoTune3D round {0}: all attributed culprit source(s) are already at the top of the ladder; {1} naked edge(s), {2} fabricated patch(es) remain - stopping.", roundNumber, Signature.NakedEdgeCount, previousFabricated));
                    break;
                }

                Rounds++;

                // Re-run the managed pipeline (raw path skipped) with the raised reach. Deterministic: unchanged
                // parameters yield identical geometry, so only the escalated culprit panels move.
                Panel3DSnapSolver roundSolver = NewSolver(escalatedBuckets, escalatedMaxExtends, tune, forceManaged: true);
                roundSolver.Execute(options);
                ClosureSignature3D candidate = roundSolver.Signature;

                if (candidate == null)
                {
                    Diagnostics.Add(SolverStage.Heal, DiagnosticCode.EscalatedPanel, OcctDiagnosticSeverity.Warning,
                        string.Format("AutoTune3D round {0}: candidate produced no signature (native kernel unavailable); escalation not adopted - stopping.", roundNumber));
                    break;
                }

                int candidateFabricated = FabricatedPatchCount(roundSolver.HoleFillFace3Ds);

                // Signature-level checks first (cheap); the geometric adjacency proof only when they pass and the
                // cell count rose (the only case a "new cell" exists to prove).
                bool noRegression = !candidate.IsRegressionOf(Signature);
                bool sliverOk = candidate.SliverCellCount <= Signature.SliverCellCount;
                bool progresses = Signature.NakedEdgeCount > 0
                    ? candidate.NakedEdgeCount < Signature.NakedEdgeCount                      // naked-driven: naked strictly drops
                    : candidate.NakedEdgeCount == 0 && candidateFabricated < previousFabricated; // fabrication-driven: naked held at 0, fabrication drops

                bool cellIncreaseAdjacent = true;
                string unproven = null;
                if (noRegression && sliverOk && progresses && candidate.CellCount > Signature.CellCount)
                {
                    if (adoptedCells == null)
                    {
                        adoptedCells = DecodeCells(ResolvedFace3Ds, effectiveOptions);
                    }

                    List<DecodedCell> candidateCells = DecodeCells(roundSolver.ResolvedFace3Ds, effectiveOptions);
                    List<OcctNakedWire> closedLoops = ClosedLoops(NakedWires, roundSolver.NakedWires);

                    if (adoptedCells.Count != Signature.CellCount || candidateCells.Count != candidate.CellCount)
                    {
                        // The decoded cell counts do not line up with the signatures, so the new cells cannot be
                        // identified reliably: the increase is unprovable - reject conservatively (owner caution 3).
                        cellIncreaseAdjacent = false;
                        unproven = string.Format("decoded cells ({0} adopted, {1} candidate) disagree with the signatures ({2} -> {3})",
                            adoptedCells.Count, candidateCells.Count, Signature.CellCount, candidate.CellCount);
                    }
                    else
                    {
                        cellIncreaseAdjacent = CellIncreaseAdjacent(
                            adoptedCells.Select(x => x.Center).ToList(),
                            candidateCells.Select(x => x.Center).ToList(),
                            candidateCells.Select(x => x.Box).ToList(),
                            closedLoops, tune.CellAdjacencyTolerance, out unproven);
                    }
                }

                if (IsAcceptableRound(Signature, candidate, previousFabricated, candidateFabricated, cellIncreaseAdjacent))
                {
                    foreach (int source in escalatedSources)
                    {
                        Diagnostics.Add(SolverStage.Heal, DiagnosticCode.EscalatedPanel, OcctDiagnosticSeverity.Info,
                            string.Format("AutoTune3D round {0}: escalated source {1} MaxExtend {2:0.###} -> {3:0.###} (accepted).",
                                roundNumber, source, currentMaxExtends[source], escalatedMaxExtends[source]));
                    }

                    ClosureSignature3D previous = Signature;
                    Adopt(roundSolver, escalatedMaxExtends, escalatedBuckets);
                    adopted = roundSolver;
                    currentMaxExtends.Clear();
                    currentMaxExtends.AddRange(escalatedMaxExtends);
                    currentBuckets.Clear();
                    currentBuckets.AddRange(escalatedBuckets);
                    adoptedCells = null; // re-decode from the newly adopted state on the next round if needed
                    RoundsAccepted++;

                    Diagnostics.Add(SolverStage.Heal, DiagnosticCode.AdoptedLevel, OcctDiagnosticSeverity.Info,
                        string.Format("AutoTune3D round {0} accepted: naked {1} -> {2}, fabricated patches {3} -> {4}, cells {5} -> {6}.",
                            roundNumber, previous.NakedEdgeCount, candidate.NakedEdgeCount, previousFabricated, candidateFabricated, previous.CellCount, candidate.CellCount));

                    if (candidate.NakedEdgeCount == 0 && candidateFabricated == 0)
                    {
                        break; // fully and cleanly closed - the loop guard would exit anyway; stop cleanly
                    }
                }
                else
                {
                    Diagnostics.Add(SolverStage.Heal, DiagnosticCode.EscalatedPanel, OcctDiagnosticSeverity.Warning,
                        string.Format("AutoTune3D round {0} rejected ({1}); escalation discarded - stopping. [previous: {2} (fab {3}) | candidate: {4} (fab {5})]",
                            roundNumber, RejectReason(Signature, candidate, previousFabricated, candidateFabricated, noRegression, sliverOk, progresses, cellIncreaseAdjacent, unproven), Signature, previousFabricated, candidate, candidateFabricated));
                    break; // atomic reject-and-stop (§F/§E semantics)
                }
            }

            return adopted;
        }

        /// <summary>
        /// The pure, signature-level acceptance gate (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §I; owner
        /// refinement 2026-07-03). Two modes share the same guards - no <see cref="ClosureSignature3D.IsRegressionOf"/>
        /// regression (fewer cells / smaller volume / more naked), no rise in the sliver-cell count, and every new
        /// cell positively proven adjacent to a loop that closed (<paramref name="cellIncreaseAdjacent"/>; unprovable
        /// = false = reject) - and differ only in the progress metric:
        /// <list type="bullet">
        /// <item><b>Naked-driven</b> (<paramref name="previous"/> has naked edges): the naked count must strictly drop.</item>
        /// <item><b>Fabrication-driven</b> (<paramref name="previous"/> is watertight but closed only by fabricated
        /// GapFill/HoleFill patches): the naked count must stay 0 AND the fabricated patch count must strictly drop -
        /// i.e. measured extension replaced fabrication without re-opening the closure.</item>
        /// </list>
        /// </summary>
        public static bool IsAcceptableRound(ClosureSignature3D previous, ClosureSignature3D candidate, int previousFabricatedCount, int candidateFabricatedCount, bool cellIncreaseAdjacent)
        {
            if (candidate == null || previous == null)
            {
                return false;
            }

            if (candidate.IsRegressionOf(previous))
            {
                return false; // fewer cells, smaller total volume, or more naked
            }

            if (candidate.SliverCellCount > previous.SliverCellCount)
            {
                return false; // closing only by manufacturing sliver cells
            }

            if (!cellIncreaseAdjacent)
            {
                return false; // a new cell that cannot be tied to a loop that closed - reject conservatively
            }

            if (previous.NakedEdgeCount > 0)
            {
                return candidate.NakedEdgeCount < previous.NakedEdgeCount; // naked-driven: naked strictly drops
            }

            // Fabrication-driven: the baseline was closed only by fabrication - keep it closed (naked stays 0) and
            // strictly reduce the fabricated patch count (measured extension replaced a patch).
            return candidate.NakedEdgeCount == 0 && candidateFabricatedCount < previousFabricatedCount;
        }

        /// <summary>
        /// The smallest ladder rung strictly greater than <paramref name="current"/> (the reach the culprit is
        /// escalated to this round), or null when <paramref name="current"/> already reaches the top rung -
        /// the "next ladder value &gt; current" / "exhausted at 1.5 drops out" rule (§E). Order-independent:
        /// picks the minimum qualifying rung. Never reduces a reach (a value already past the top drops out).
        /// </summary>
        public static double? NextLadderValue(double current, IReadOnlyList<double> ladder)
        {
            if (ladder == null)
            {
                return null;
            }

            double? best = null;
            foreach (double rung in ladder)
            {
                if (double.IsNaN(rung) || rung <= current + LadderEpsilon)
                {
                    continue;
                }

                if (best == null || rung < best.Value)
                {
                    best = rung;
                }
            }

            return best;
        }

        /// <summary>
        /// Raises the <c>MaxExtend</c> permission of the attributed <paramref name="culprits"/> one ladder
        /// rung each, in place in <paramref name="maxExtends"/> (and their <paramref name="buckets"/> by
        /// <paramref name="bucketFactor"/> when <paramref name="escalateBucket"/> is true), and returns the
        /// distinct source indices actually raised. Culprit-only (a non-culprit source is never touched),
        /// exhaustion-aware (a culprit already at the top rung, per <see cref="NextLadderValue"/>, is skipped),
        /// and out-of-range-safe. Pure/managed - the escalation arithmetic split out so it is unit-testable
        /// without the native re-solve loop.
        /// </summary>
        public static IReadOnlyList<int> EscalateCulprits(
            IReadOnlyList<int> culprits,
            IReadOnlyList<double> ladder,
            List<double> maxExtends,
            List<double> buckets,
            bool escalateBucket,
            double bucketFactor)
        {
            List<int> escalated = new List<int>();
            if (culprits == null || maxExtends == null)
            {
                return escalated;
            }

            foreach (int source in culprits)
            {
                if (source < 0 || source >= maxExtends.Count || escalated.Contains(source))
                {
                    continue; // out of range (fabricated/unknown), or already escalated this round
                }

                double? next = NextLadderValue(maxExtends[source], ladder);
                if (next == null)
                {
                    continue; // this culprit is already at (or past) the top of the ladder - drops out
                }

                maxExtends[source] = next.Value;
                if (escalateBucket && buckets != null && source < buckets.Count)
                {
                    buckets[source] = buckets[source] * bucketFactor;
                }

                escalated.Add(source);
            }

            return escalated;
        }

        /// <summary>
        /// The conservative new-cell adjacency proof (owner caution 3). A candidate cell that is NOT carried
        /// over from the previous state (no near-coincident previous centroid) is a NEW cell; every new cell
        /// must have a loop that closed this round lying on its boundary - tested as the closed loop's bounding
        /// box overlapping the new cell's own bounding box (grown by <paramref name="adjacencyTolerance"/>).
        /// This is the correct direction: the loop that closes becomes part of the new cell's shell, so it sits
        /// inside that cell's extent (a small corner gap can legitimately form a whole room, whose centroid is
        /// far from the gap - so the loop must be checked against the CELL's box, not the reverse). When the
        /// candidate has no more cells than before there is nothing new to prove (true); a new cell with no loop
        /// closed anywhere, or one sitting away from every closed loop, is unprovable and returns false. Pure/managed.
        /// </summary>
        /// <param name="previousCellCenters">Cell centroids of the currently-adopted state (for carried-over matching).</param>
        /// <param name="candidateCellCenters">Cell centroids of the escalation candidate (index-aligned to <paramref name="candidateCellBoxes"/>).</param>
        /// <param name="candidateCellBoxes">Cell bounding boxes of the escalation candidate (index-aligned to <paramref name="candidateCellCenters"/>).</param>
        /// <param name="closedLoops">The before-wires that closed this round (no after-match) - the loops that closed.</param>
        /// <param name="adjacencyTolerance">How far a closed loop's box may sit outside a new cell's box and still count as on its boundary.</param>
        /// <param name="unproven">Set to a human-readable reason when the proof fails; null on success.</param>
        public static bool CellIncreaseAdjacent(
            IReadOnlyList<Point3D> previousCellCenters,
            IReadOnlyList<Point3D> candidateCellCenters,
            IReadOnlyList<BoundingBox3D> candidateCellBoxes,
            IReadOnlyList<OcctNakedWire> closedLoops,
            double adjacencyTolerance,
            out string unproven)
        {
            unproven = null;

            List<Point3D> previous = (previousCellCenters ?? new List<Point3D>()).Where(x => x != null).ToList();
            List<Point3D> candidateCenters = (candidateCellCenters ?? new List<Point3D>()).ToList();
            List<BoundingBox3D> candidateBoxes = (candidateCellBoxes ?? new List<BoundingBox3D>()).ToList();

            int candidateCount = System.Math.Min(candidateCenters.Count, candidateBoxes.Count);
            if (candidateCount <= previous.Count)
            {
                return true; // no net cell increase - nothing new to prove (a decrease is caught by IsRegressionOf)
            }

            // Identify the candidate cells NOT carried over from the previous state. By the determinism
            // invariant, an unescalated region's cell is byte-identical between rounds, so a carried-over cell
            // matches near-exactly; the cells left unmatched are the ones this round formed.
            bool[] previousUsed = new bool[previous.Count];
            List<BoundingBox3D> newCellBoxes = new List<BoundingBox3D>();
            for (int c = 0; c < candidateCount; c++)
            {
                Point3D center = candidateCenters[c];
                int match = -1;
                double bestDistance = double.MaxValue;
                for (int p = 0; p < previous.Count; p++)
                {
                    if (previousUsed[p] || center == null)
                    {
                        continue;
                    }

                    double distance = center.Distance(previous[p]);
                    if (distance <= LoopMatchTolerance && distance < bestDistance)
                    {
                        bestDistance = distance;
                        match = p;
                    }
                }

                if (match >= 0)
                {
                    previousUsed[match] = true;
                }
                else
                {
                    newCellBoxes.Add(candidateBoxes[c]);
                }
            }

            if (newCellBoxes.Count == 0)
            {
                return true; // count rose but every candidate cell matched a previous one (rare) - nothing unexplained
            }

            List<BoundingBox3D> closedBoxes = (closedLoops ?? new List<OcctNakedWire>())
                .Select(WireBox)
                .Where(x => x != null)
                .ToList();

            if (closedBoxes.Count == 0)
            {
                unproven = string.Format("cell count rose by {0} but no naked loop closed this round to explain it", newCellBoxes.Count);
                return false; // a new cell with no loop closing anywhere - unprovable, reject conservatively
            }

            for (int n = 0; n < newCellBoxes.Count; n++)
            {
                BoundingBox3D cellBox = newCellBoxes[n];
                bool adjacent = cellBox != null && closedBoxes.Any(loopBox => BoxesOverlap(cellBox, loopBox, adjacencyTolerance));
                if (!adjacent)
                {
                    Point3D centre = cellBox?.GetCentroid();
                    unproven = centre == null
                        ? "a new cell has no bounding box and no loop that closed this round on its boundary"
                        : string.Format("a new cell near ({0:0.##}, {1:0.##}, {2:0.##}) has no loop that closed this round on its boundary", centre.X, centre.Y, centre.Z);
                    return false;
                }
            }

            return true;
        }

        // ---- helpers ------------------------------------------------------------------------------------

        /// <summary>The effective build options the underlying solver uses when none are supplied - the standard
        /// zoned/sew-before-build cell build, matching <see cref="Panel3DSnapSolver"/>'s own default.</summary>
        private static OcctBuildOptions EffectiveOptions(OcctBuildOptions options)
        {
            return options ?? new OcctBuildOptions
            {
                AvoidInternalShapes = false,
                SewBeforeBuild = true,
                SewingTolerance = 0.01
            };
        }

        /// <summary>Builds a <see cref="Panel3DSnapSolver"/> with the AutoTune pass-through knobs applied.</summary>
        private Panel3DSnapSolver NewSolver(List<double> buckets, List<double> maxExtends, AutoTune3DOptions tune, bool forceManaged)
        {
            return new Panel3DSnapSolver(face3Ds, buckets, weights, maxExtends)
            {
                ForceManagedPipeline = forceManaged,
                Up = Up,
                AlignColinearOffset = AlignColinearOffset,
                NormalizeCapOffset = NormalizeCapOffset,
                BucketBetweenLevels = BucketBetweenLevels,
                FillMargin = FillMargin,
                DirectionalCapGrow = DirectionalCapGrow,
                DoubleWallGap = DoubleWallGap,
                MinCellVolume = tune.MinCellVolume,
                SewSafetyFactor = tune.SewSafetyFactor,
                ConsolidateRebuild = tune.ConsolidateRebuild
            };
        }

        /// <summary>Copies the adopted solver's outputs onto this solver's public surface and records the reach/bucket
        /// that produced them.</summary>
        private void Adopt(Panel3DSnapSolver solver, List<double> maxExtends, List<double> buckets)
        {
            ResolvedFace3Ds = solver.ResolvedFace3Ds ?? new List<Face3D>();
            SourceMap = solver.SourceMap ?? new SourceMap();
            Signature = solver.Signature;
            NakedWires = solver.NakedWires ?? new List<OcctNakedWire>();
            NakedEdgePoint3Ds = solver.NakedEdgePoint3Ds ?? new List<Point3D>();
            HoleFillFace3Ds = solver.HoleFillFace3Ds ?? new List<Face3D>();
            ResolvedCellCount = solver.ResolvedCellCount;
            NativeResolved = solver.NativeResolved;
            MaxExtensions = maxExtends.ToList();
            BucketSizes = buckets.ToList();
        }

        /// <summary>Appends the adopted solve's own diagnostics (sew, gap-fill, retain, rebuild) to AutoTune's log.</summary>
        private void MergeDiagnostics(Panel3DSnapSolver adopted)
        {
            if (adopted?.Diagnostics == null)
            {
                return;
            }

            foreach (SolverDiagnostic diagnostic in adopted.Diagnostics.All)
            {
                Diagnostics.Add(diagnostic);
            }
        }

        /// <summary>Emits the residual-loop diagnostics (§E/§N) when AutoTune stops with naked edges remaining, a
        /// residual-fabrication note when it stops with unreplaced GapFill patches, plus a closing summary either way.</summary>
        private void EmitResidualDiagnostics(int baselineNaked, int baselineFabricated)
        {
            int finalNaked = Signature?.NakedEdgeCount ?? -1;
            int finalFabricated = FabricatedPatchCount(HoleFillFace3Ds);

            if (Signature != null && Signature.NakedEdgeCount > 0)
            {
                Diagnostics.Add(SolverStage.Heal, DiagnosticCode.NakedLoop, OcctDiagnosticSeverity.Warning,
                    string.Format("AutoTune3D stopped with {0} naked edge(s) remaining after {1} round(s) ({2} accepted); returning best-effort result.",
                        Signature.NakedEdgeCount, Rounds, RoundsAccepted));

                foreach (OcctNakedWire wire in NakedWires ?? new List<OcctNakedWire>())
                {
                    if (wire == null)
                    {
                        continue;
                    }

                    Diagnostics.Add(SolverStage.Heal, DiagnosticCode.NakedLoop, OcctDiagnosticSeverity.Warning,
                        string.Format("AutoTune3D residual naked loop (closed={0}, {1} vertices).", wire.IsClosed, wire.Point3Ds?.Count ?? 0),
                        point3Ds: wire.Point3Ds);
                }
            }

            if (Signature != null && Signature.NakedEdgeCount == 0 && finalFabricated > 0)
            {
                // Watertight, but still closed by fabricated patches AutoTune could not replace with measured
                // extension (a genuinely fill-needing gap). Best-effort, diagnosed - never silent.
                Diagnostics.Add(SolverStage.Heal, DiagnosticCode.NakedLoop, OcctDiagnosticSeverity.Info,
                    string.Format("AutoTune3D stopped watertight with {0} fabricated patch(es) remaining after {1} round(s) ({2} accepted); measured extension could not replace them.",
                        finalFabricated, Rounds, RoundsAccepted));
            }

            Diagnostics.Add(SolverStage.Heal, DiagnosticCode.AdoptedLevel, OcctDiagnosticSeverity.Info,
                string.Format("AutoTune3D complete: {0} round(s) attempted, {1} accepted; naked {2} -> {3}; fabricated patches {4} -> {5}; {6}",
                    Rounds, RoundsAccepted,
                    baselineNaked < 0 ? "n/a" : baselineNaked.ToString(),
                    finalNaked < 0 ? "n/a" : finalNaked.ToString(),
                    baselineFabricated, finalFabricated,
                    Signature == null ? "no signature" : Signature.ToString()));
        }

        /// <summary>Number of fabricated GapFill/HoleFill patch faces (valid, above the air-panel area floor) - the
        /// fabrication metric AutoTune tries to reduce by measured extension (owner decision 2026-07-03).</summary>
        private static int FabricatedPatchCount(IReadOnlyList<Face3D> holeFillFace3Ds)
        {
            if (holeFillFace3Ds == null)
            {
                return 0;
            }

            int count = 0;
            foreach (Face3D face3D in holeFillFace3Ds)
            {
                if (face3D != null && face3D.IsValid() && face3D.GetArea() > MinAirArea)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// The input source panels bounding-box-adjacent to a fabricated patch - the walls abutting the gap the
        /// patch currently covers, whose reach AutoTune raises so measured extension can close it with real
        /// geometry instead (owner decision 2026-07-03). Proximity, not coplanarity: a residual gap is fan-patched
        /// across the floor/wall/ceiling planes (a non-planar loop), so the wall that should extend is adjacent to
        /// the patch, not coplanar with it. Over-attribution is harmless - measured-to-target only grows the walls
        /// that actually reach a diagnosed target, and a cap's <c>MaxExtend</c> is inert to the lateral extend.
        /// The INPUT faces are used directly (their indices are the <c>MaxExtend</c> the escalation raises). Pure/managed.
        /// </summary>
        private static IReadOnlyList<int> AttributePatchesToSources(IReadOnlyList<Face3D> patches, IReadOnlyList<Face3D> inputFace3Ds)
        {
            SortedSet<int> culprits = new SortedSet<int>();
            if (patches == null || inputFace3Ds == null)
            {
                return culprits.ToList();
            }

            foreach (Face3D patch in patches)
            {
                BoundingBox3D patchBox = patch?.GetBoundingBox();
                if (patchBox == null || !patch.IsValid() || patch.GetArea() <= MinAirArea)
                {
                    continue;
                }

                for (int i = 0; i < inputFace3Ds.Count; i++)
                {
                    BoundingBox3D inputBox = inputFace3Ds[i]?.GetBoundingBox();
                    if (inputBox != null && BoxesOverlap(patchBox, inputBox, PatchAdjacency))
                    {
                        culprits.Add(i);
                    }
                }
            }

            return culprits.ToList();
        }

        /// <summary>A decoded cell's centre (for carried-over matching) and bounding box (for the adjacency proof).</summary>
        private readonly struct DecodedCell
        {
            public Point3D Center { get; }

            public BoundingBox3D Box { get; }

            public DecodedCell(Point3D center, BoundingBox3D box)
            {
                Center = center;
                Box = box;
            }
        }

        /// <summary>Decodes <paramref name="face3Ds"/> into a cell complex once and returns each cell's centre (native
        /// cell centre, else its shell's bounding-box centroid) and its shell's bounding box. Empty when the native
        /// kernel is unavailable, so a caller comparing the count against a signature rejects an unprovable round.</summary>
        private static List<DecodedCell> DecodeCells(List<Face3D> face3Ds, OcctBuildOptions options)
        {
            List<DecodedCell> cells = new List<DecodedCell>();
            if (face3Ds == null || face3Ds.Count == 0)
            {
                return cells;
            }

            GeometryCreate.Shells(face3Ds, out OcctCellComplexResult result, options);
            try
            {
                if (result == null || !result.NativeAvailable || result.Cells == null)
                {
                    return cells;
                }

                foreach (OcctCell cell in result.Cells)
                {
                    BoundingBox3D box = cell?.Shell?.GetBoundingBox();
                    Point3D center = cell?.Center ?? box?.GetCentroid();
                    cells.Add(new DecodedCell(center, box));
                }
            }
            finally
            {
                result?.Dispose();
            }

            return cells;
        }

        /// <summary>The before-wires (adopted state) that closed this round: no after-wire (candidate) matches them
        /// by centroid proximity and bbox-diagonal similarity (the loops that closed - §F bookkeeping, evidence only).</summary>
        private static List<OcctNakedWire> ClosedLoops(IReadOnlyList<OcctNakedWire> before, IReadOnlyList<OcctNakedWire> after)
        {
            List<OcctNakedWire> beforeList = (before ?? new List<OcctNakedWire>()).Where(x => x != null).ToList();
            List<OcctNakedWire> afterList = (after ?? new List<OcctNakedWire>()).Where(x => x != null).ToList();

            Point3D[] afterCentroids = afterList.Select(WireCentroid).ToArray();
            double[] afterDiagonals = afterList.Select(WireDiagonal).ToArray();
            bool[] afterMatched = new bool[afterList.Count];

            List<OcctNakedWire> closed = new List<OcctNakedWire>();
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
                    if (distance <= LoopMatchTolerance && System.Math.Abs(beforeDiagonal - afterDiagonals[a]) <= LoopMatchTolerance && distance < bestDistance)
                    {
                        bestDistance = distance;
                        match = a;
                    }
                }

                if (match >= 0)
                {
                    afterMatched[match] = true; // this before-wire persists
                }
                else
                {
                    closed.Add(beforeWire); // no after-match: this loop closed
                }
            }

            return closed;
        }

        /// <summary>Bounding box of a wire's vertices, or null when it has none.</summary>
        private static BoundingBox3D WireBox(OcctNakedWire wire)
        {
            IReadOnlyList<Point3D> point3Ds = wire?.Point3Ds;
            if (point3Ds == null || point3Ds.Count == 0)
            {
                return null;
            }

            return new BoundingBox3D(new List<Point3D>(point3Ds));
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
            BoundingBox3D box = WireBox(wire);
            return box == null ? 0 : box.Min.Distance(box.Max);
        }

        /// <summary>True when box <paramref name="a"/> and box <paramref name="b"/> overlap on every axis after
        /// <paramref name="a"/> is grown by <paramref name="tolerance"/> - used to test that a loop that closed sits
        /// on (within) a new cell's extent.</summary>
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

        /// <summary>A concise reason string for a rejected round (which acceptance clause failed), for the diagnostic.</summary>
        private static string RejectReason(ClosureSignature3D previous, ClosureSignature3D candidate, int previousFabricated, int candidateFabricated, bool noRegression, bool sliverOk, bool progresses, bool cellIncreaseAdjacent, string unproven)
        {
            if (!noRegression)
            {
                return string.Format("closure regressed (cells {0} -> {1}, volume {2:0.###} -> {3:0.###}, naked {4} -> {5})", previous.CellCount, candidate.CellCount, previous.TotalVolume, candidate.TotalVolume, previous.NakedEdgeCount, candidate.NakedEdgeCount);
            }

            if (!sliverOk)
            {
                return string.Format("sliver-cell count rose ({0} -> {1})", previous.SliverCellCount, candidate.SliverCellCount);
            }

            if (!cellIncreaseAdjacent)
            {
                return "new-cell adjacency unprovable: " + (unproven ?? "a new cell could not be tied to a closed loop");
            }

            if (!progresses)
            {
                return previous.NakedEdgeCount > 0
                    ? string.Format("naked did not strictly drop ({0} -> {1})", previous.NakedEdgeCount, candidate.NakedEdgeCount)
                    : string.Format("fabricated patch count did not strictly drop with naked held at 0 (naked {0} -> {1}, patches {2} -> {3})", previous.NakedEdgeCount, candidate.NakedEdgeCount, previousFabricated, candidateFabricated);
            }

            return "unspecified";
        }
    }
}
