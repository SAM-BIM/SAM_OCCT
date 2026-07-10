// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Why an extend/fill decision point left a panel untouched (P3, docs/CONTROLLED_WORKFLOW_PLAN.md §5.4).
    /// Recorded at the REAL branch that decided not to move anything - never inferred after the fact - so a
    /// panel that stayed put is traceable to a reason instead of reading as silent success.
    /// </summary>
    public enum ExtendSkipReason
    {
        /// <summary>No candidate target (cap, wall, or facing wall plane) lies within the operation's reach.</summary>
        NoTargetWithinReach,

        /// <summary>The panel already meets the target within tolerance - there was nothing to move.</summary>
        AlreadyMeetsTarget,

        /// <summary>The panel's own length caps its lateral reach (<c>EXTENSION_LIMIT_LENGTH_RATIO</c>) at or
        /// below tolerance, so no amount of nominal <c>MaxExtend</c> could move it.</summary>
        CappedByLengthRatio,

        /// <summary>Two or more candidate targets are equally near (within tolerance of each other); rather than
        /// guess, the panel is left untouched.</summary>
        TargetAmbiguous,

        /// <summary>A target was found but the geometric construction (extend/offset/rebuild) failed or produced
        /// an invalid result.</summary>
        DegenerateGeometry,

        /// <summary>The configured margin/reach is at or below tolerance, so the fill pass has nothing
        /// meaningful to grow by.</summary>
        FillTooSmall
    }
}
