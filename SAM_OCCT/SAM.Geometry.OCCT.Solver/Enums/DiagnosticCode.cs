// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// The full <see cref="SolverDiagnostic"/> code taxonomy
    /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §N) every later phase emits into. Phase 1
    /// only produces a subset (<see cref="SliverCell"/>, <see cref="DroppedFace"/>,
    /// <see cref="NakedEdge"/>, <see cref="AdoptedLevel"/>) - the rest are reserved for the stages
    /// that measure them (Phase 3 history/naked-loop export, Phase 5 escalation/sew, Phase 9 budget).
    /// </summary>
    public enum DiagnosticCode
    {
        /// <summary>A single naked (free) boundary edge - a validated gap.</summary>
        NakedEdge,

        /// <summary>A naked boundary resolved to a closed wire loop (native free-bounds).</summary>
        NakedLoop,

        /// <summary>A measured gap between near, non-touching faces.</summary>
        Gap,

        /// <summary>Two faces overlapping where they should abut.</summary>
        Overlap,

        /// <summary>Two faces occupying (near-)identical geometry.</summary>
        DuplicateFace,

        /// <summary>A resolved face below the minimum meaningful area.</summary>
        SliverFace,

        /// <summary>A resolved cell below <see cref="Panel3DSnapSolver.MinCellVolume"/>.</summary>
        SliverCell,

        /// <summary>An edge shared by more than two faces.</summary>
        NonManifoldEdge,

        /// <summary>Native shape tolerance grew beyond the expected budget for a stage.</summary>
        ToleranceDrift,

        /// <summary>An input face has no surviving representation in the resolved output.</summary>
        DroppedFace,

        /// <summary>
        /// A native-history composition gap: an input face that the history neither mapped to an
        /// output nor recorded as deleted, or an output face no input maps to (reverse gap). The
        /// affected face falls back to the geometric <c>NearestSourceIndex</c> heuristic - never silent.
        /// </summary>
        HistoryGap,

        /// <summary>An adaptive/expanded sew was attempted and rejected (would fuse distinct faces).</summary>
        RejectedSew,

        /// <summary>An opposed-partition collapse was attempted and rejected (gate failed).</summary>
        RejectedCollapse,

        /// <summary>A wall stack was consolidated onto one plane by the explicit double-wall pass
        /// (<see cref="Panel3DSnapSolver.ConsolidateWallStacks"/>, opt-in via <c>doubleWallGap</c>).</summary>
        ConsolidatedStack,

        /// <summary>Records which resolve level (raw/snapped/conditioned) was adopted, and why.</summary>
        AdoptedLevel,

        /// <summary>A panel's reach (MaxExtend/bucket) was raised during diagnosis-driven closure.</summary>
        EscalatedPanel,

        /// <summary>A configured cost/time/round budget was exceeded.</summary>
        BudgetExceeded,

        /// <summary>
        /// A cap or wall face could not be assigned to a single <see cref="LevelFrame"/> unambiguously -
        /// it lies within the elevation band of more than one level frame, or (a wall) spans none of the
        /// clustered datums (Phase 6). Never silent: the face is assigned to a deterministic fallback frame
        /// (the nearest datum, or for a wall the nearest by centroid) and this diagnostic records the choice.
        /// </summary>
        AmbiguousLevelFrame,

        /// <summary>
        /// Reports how frame-aware cap normalization (Phase 6c) grouped a level's caps: the number of
        /// level frames formed, the cap count normalized within each, or that no cap formed a frame and
        /// the legacy world-frame <see cref="Panel3DSnapSolver.NormalizeCapOffset"/> band was used instead
        /// (Phase 7-pre diagnostics parity - geometry is unchanged by this diagnostic).
        /// </summary>
        FrameNormalization,

        /// <summary>
        /// Phase 7c closure gate: spaces were NOT created because the resolved geometry is not fully
        /// closed (naked edges remain) or the adjacency/space construction failed. A degraded solve (e.g.
        /// a multi-level managed result that falls short of closure) returns this diagnostic and no
        /// <c>AdjacencyCluster</c>, rather than building spaces on an incomplete cell complex.
        /// </summary>
        SpacesRefused,

        /// <summary>
        /// Phase 7c: a classified cell (<see cref="CellRole.Sliver"/>, <see cref="CellRole.Exterior"/>, or
        /// <see cref="CellRole.Unknown"/>) did not become a <c>Space</c>. Complements the classifier's own
        /// per-role diagnostic (e.g. <see cref="SliverCell"/>) with the space-layer consequence - never
        /// silent, so a room that unexpectedly did not get a Space is traceable to its cause.
        /// </summary>
        CellExcludedFromSpaces,

        /// <summary>Raw adoption rejected because a dropped room-dividing partition sits strictly inside an
        /// adopted cell (rooms merged watertight-but-wrong) - the under-split gate (codex #7, P4). Appended at
        /// the end of the enum so existing members keep their ordinal values (public-enum binary contract).</summary>
        UnderSplit,

        /// <summary>Level-group near-miss (P2, docs/CONTROLLED_WORKFLOW_PLAN.md §4.5): a raw <see cref="LevelFrame"/>
        /// datum sits just OUTSIDE the <c>bucketBetweenLevels</c> band of a group datum (band &lt; distance &lt;=
        /// 2×band), so it was NOT merged. Never silent: the diagnostic states exactly what <c>bucketBetweenLevels</c>
        /// value would merge it, so a user can see a near-miss split without guessing. Appended at the end of the
        /// enum so existing members keep their ordinal values (public-enum binary contract).</summary>
        LevelBandNearMiss,

        /// <summary>
        /// P3 (docs/CONTROLLED_WORKFLOW_PLAN.md §5, risk #3): a <see cref="LevelGroup"/>'s member frames span
        /// more than <see cref="LevelFrame.OverMergeSpreadWarning"/> - wide enough that <c>bucketBetweenLevels</c>
        /// may be merging a genuine split-level landing rather than a single physical floor's slab-skin noise.
        /// Never blocks the merge (the user's chosen band is honoured); this is visibility, not a rejection.
        /// Appended at the end of the enum so existing members keep their ordinal values (public-enum binary
        /// contract).
        /// </summary>
        LevelGroupOverMerge
    }
}
