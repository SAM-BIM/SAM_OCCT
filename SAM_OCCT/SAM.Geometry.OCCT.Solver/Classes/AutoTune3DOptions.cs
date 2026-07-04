// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Tuning knobs for <see cref="AutoTune3DSolver"/> - the bounded, diagnosis-driven closure loop
    /// (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §E/§L). Mirrors the 2D
    /// <c>SAM.Analytical.Solver.AutoTuneSolver</c> contract (ladder, max rounds) but escalates the
    /// <c>MaxExtend</c> permission only on the source panels a naked loop attributes to, re-solving
    /// through the managed pipeline and accepting a round only when the closure signature strictly
    /// improves without regressing.
    /// </summary>
    /// <remarks>
    /// The ladder raises the <em>permission</em> (reach cap); the actual extension stays
    /// measured-to-target (the pipeline's <see cref="ConditionStage"/> grows a wall to its diagnosed
    /// meeting object, not by the blind ladder length), which is the anti-tunneling guarantee. Bucket
    /// escalation is a partition-collapse hazard and is OFF by default (§A.4).
    /// </remarks>
    public class AutoTune3DOptions
    {
        /// <summary>
        /// Hard cap on escalation rounds (each round is one full managed re-solve). Three, not the 2D
        /// solver's six: MakerVolume is the cost driver at 2k panels, so the round budget is tighter (§C/§M).
        /// </summary>
        public int MaxRounds { get; set; } = 3;

        /// <summary>
        /// Absolute <c>MaxExtend</c> permission targets (metres). Each escalated culprit source is raised to
        /// the smallest rung strictly greater than its current reach; a source already at (or above) the top
        /// rung drops out. Absolute (not a multiplier) because the per-wall baseline reach is capped near
        /// 0.49x the wall length, so a short stub could never reach across a gap with a multiplier. Mirrors
        /// the proven 2D ladder [0.5, 0.75, 1.0, 1.5].
        /// </summary>
        public List<double> MaxExtendLadder { get; set; } = new List<double> { 0.5, 0.75, 1.0, 1.5 };

        /// <summary>
        /// When true, an escalated culprit's <c>BucketSize</c> is also grown by <see cref="BucketFactor"/>.
        /// Default OFF (§A.4): a bucket change alters Stage-A snapping globally for that panel and can merge a
        /// genuine double wall (the weld/shaft fixtures guard this when it is enabled).
        /// </summary>
        public bool EscalateBucket { get; set; } = false;

        /// <summary>
        /// Multiplier applied to an escalated culprit's <c>BucketSize</c> - used ONLY when
        /// <see cref="EscalateBucket"/> is explicitly true; ignored otherwise.
        /// </summary>
        public double BucketFactor { get; set; } = 1.25;

        /// <summary>
        /// Prefer shrinking a face to its diagnosed target over blindly extending it. Realized by the
        /// pipeline's measured-to-target conditioning (§D.4/§E): measured-to-target inherently trims (a target
        /// closer than the current reach shrinks the footprint), and there is no separate trim planner in
        /// Phase 5, so this flag is advisory - the round re-solve is always measured-to-target.
        /// </summary>
        public bool PreferTrimOverExtend { get; set; } = true;

        /// <summary>
        /// Fraction of the closest near-parallel gap the adaptive residual sew tolerance is capped at
        /// (<see cref="HealStage.SewV2"/>), passed through to every round's <see cref="Panel3DSnapSolver"/>
        /// so the sew cap that stops a global sew fusing a double wall is consistent across rounds.
        /// </summary>
        public double SewSafetyFactor { get; set; } = HealStage.DEFAULT_SewSafetyFactor;

        /// <summary>
        /// Run the signature-gated consolidation rebuild in each round's <c>FinalizeAndValidate</c> (§H).
        /// Passed through to every round's <see cref="Panel3DSnapSolver.ConsolidateRebuild"/>.
        /// </summary>
        public bool ConsolidateRebuild { get; set; } = true;

        /// <summary>
        /// Minimum cell volume (m3) below which a cell is a sliver artifact rather than a room. Threaded to
        /// every round's <see cref="Panel3DSnapSolver.MinCellVolume"/> so the <see cref="ClosureSignature3D"/>
        /// sliver term is measured on the same threshold and stays comparable across rounds. Mirrors the
        /// solver default.
        /// </summary>
        public double MinCellVolume { get; set; } = 0.05;

        /// <summary>
        /// How far (metres) a new cell's centroid may sit outside a closed loop's bounding box and still count
        /// as adjacent to it, in the conservative <see cref="AutoTune3DSolver.CellIncreaseAdjacent"/> proof
        /// (owner caution 3). Small: the closed loops are room-perimeter-sized, so their boxes already contain
        /// a legitimately-formed cell's centroid; the tolerance only absorbs extension overshoot.
        /// </summary>
        public double CellAdjacencyTolerance { get; set; } = 0.1;

        public AutoTune3DOptions()
        {
        }

        /// <summary>Copy constructor - deep-copies the ladder so a mutated clone never aliases the source list.</summary>
        public AutoTune3DOptions(AutoTune3DOptions autoTune3DOptions)
        {
            if (autoTune3DOptions == null)
            {
                return;
            }

            MaxRounds = autoTune3DOptions.MaxRounds;
            MaxExtendLadder = autoTune3DOptions.MaxExtendLadder == null ? null : autoTune3DOptions.MaxExtendLadder.ToList();
            EscalateBucket = autoTune3DOptions.EscalateBucket;
            BucketFactor = autoTune3DOptions.BucketFactor;
            PreferTrimOverExtend = autoTune3DOptions.PreferTrimOverExtend;
            SewSafetyFactor = autoTune3DOptions.SewSafetyFactor;
            ConsolidateRebuild = autoTune3DOptions.ConsolidateRebuild;
            MinCellVolume = autoTune3DOptions.MinCellVolume;
            CellAdjacencyTolerance = autoTune3DOptions.CellAdjacencyTolerance;
        }
    }
}
