// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Metadata on an APPLIED <see cref="ExtendRecord"/> flagging something worth a reviewer's attention -
    /// distinct from <see cref="ExtendSkipReason"/> (P3, docs/CONTROLLED_WORKFLOW_PLAN.md §5.5): the panel
    /// DID move, but the move is close to a limit, reduced to a less precise fallback, or touches other
    /// geometry in a new way. Never blocks the move; purely observational.
    /// </summary>
    public enum ExtendRiskFlag
    {
        /// <summary>The applied move used at or near (&gt;=90%) all of its available reach.</summary>
        NearReachLimit,

        /// <summary>The lateral move was limited by the panel's own <c>MaxExtend</c>, not by the length-ratio cap.</summary>
        MaxExtendLimited,

        /// <summary>The lateral move was limited by the length-ratio cap (<c>EXTENSION_LIMIT_LENGTH_RATIO</c>), not by <c>MaxExtend</c>.</summary>
        LengthRatioLimited,

        /// <summary>The grown/extended panel now overlaps another panel's plane where it previously did not.</summary>
        NewCoplanarOverlap,

        /// <summary>Directional cap growth was requested but this panel fell back to the legacy uniform
        /// (measured or fixed-margin) grow.</summary>
        LegacyUniformCapGrow
    }
}
