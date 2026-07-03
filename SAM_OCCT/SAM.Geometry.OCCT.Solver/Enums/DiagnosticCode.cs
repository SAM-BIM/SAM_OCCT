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

        /// <summary>Records which resolve level (raw/snapped/conditioned) was adopted, and why.</summary>
        AdoptedLevel,

        /// <summary>A panel's reach (MaxExtend/bucket) was raised during diagnosis-driven closure.</summary>
        EscalatedPanel,

        /// <summary>A configured cost/time/round budget was exceeded.</summary>
        BudgetExceeded
    }
}
