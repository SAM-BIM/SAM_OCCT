// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>Whether an <see cref="ExtendRecord"/> describes a mutation that actually happened, or a
    /// decision point where the extend/fill primitive found no safe target and left the panel untouched
    /// (P3, docs/CONTROLLED_WORKFLOW_PLAN.md §5.4).</summary>
    public enum ExtendOutcome
    {
        /// <summary>The panel was moved; <see cref="ExtendRecord.Describe"/> reports what changed.</summary>
        Applied,

        /// <summary>Nothing moved; <see cref="ExtendRecord.SkipReason"/> reports why.</summary>
        Skipped
    }
}
