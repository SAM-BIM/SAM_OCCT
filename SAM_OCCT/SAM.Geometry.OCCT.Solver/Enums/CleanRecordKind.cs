// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// What a <see cref="CleanRecord"/> observed happening to a source panel during Stage A clean
    /// (docs/CONTROLLED_WORKFLOW_PLAN.md §4.4). Recorded at the REAL mutation site - never inferred
    /// post-hoc from a geometry diff - so a null recorder is byte-identical to the geometry.
    /// </summary>
    public enum CleanRecordKind
    {
        /// <summary>A back-to-back partition skin collapsed onto its opposite skin's plane
        /// (<see cref="Panel3DSnapSolver.SnapOpposedPartitions"/>).</summary>
        OpposedCollapsed,

        /// <summary>A lower-priority panel snapped onto a higher-priority (or equal-weight midpoint) backer plane
        /// during the weighted bucket snap (<see cref="Panel3DSnapSolver.Snap"/>).</summary>
        SnappedToBacker,

        /// <summary>A cap projected onto its level datum during cap normalization
        /// (<c>Panel3DSnapSolver.NormalizeCaps</c>).</summary>
        CapNormalized,

        /// <summary>A source face was absorbed into another during the final coplanar union (the coplanar merge
        /// that collapses overlapping/contained clean faces into one).</summary>
        CoplanarMerged,

        /// <summary>A wall projected onto its stack's dominant plane during the explicit double-wall
        /// consolidation pass (<see cref="Panel3DSnapSolver.ConsolidateWallStacks"/>, active only when the
        /// caller sets <c>doubleWallGap</c> &gt; 0).</summary>
        StackConsolidated,

        /// <summary>A source panel became invalid/degenerate during clean and was dropped from the output.</summary>
        DroppedInvalid,

        /// <summary>A source panel passed through clean with no snap/collapse/normalize/merge (reserved -
        /// emitted only where a caller explicitly records the untouched set; the action kinds above are the
        /// routine output).</summary>
        Preserved
    }
}
