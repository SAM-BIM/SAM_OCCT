// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Analytical.OCCT.Solver
{
    /// <summary>
    /// The GUID-based match outcome for one expected <see cref="Space"/> against the OCCT-generated
    /// cells (docs/CONTROLLED_WORKFLOW_PLAN.md §6). A cell that contains no expected location at all
    /// is reported separately, per cell, as <see cref="Extra"/> - it never appears as a
    /// <see cref="SpaceMatchRecord"/> outcome.
    /// </summary>
    public enum SpaceMatchOutcome
    {
        /// <summary>Sole occupant of one cell; the cell's vertical span matches the expected span.</summary>
        Matched,

        /// <summary>Two or more expected locations landed in the same cell.</summary>
        Merged,

        /// <summary>No cell contains the expected location.</summary>
        Missing,

        /// <summary>Sole occupant of one cell whose span falls short of the expected span, with one or
        /// more partner (otherwise-unclaimed) cells covering the rest of the expected footprint/height.</summary>
        Split,

        /// <summary>Sole occupant of one cell whose span does not match the expected span and no partner
        /// cell explains the mismatch.</summary>
        IncorrectlyBounded,

        /// <summary>A generated cell that contains no expected location and is not accounted for as a
        /// <see cref="Split"/> partner (cell-level outcome, not carried on a <see cref="SpaceMatchRecord"/>).</summary>
        Extra
    }
}
