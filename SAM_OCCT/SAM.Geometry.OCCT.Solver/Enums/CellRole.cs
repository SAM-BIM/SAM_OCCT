// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// The analytical role of a resolved <see cref="SolverCell"/> (Phase 7b): whether it is a genuine,
    /// space-worthy room, a sliver artifact, or - on the rare model where the resolve produced a bounded
    /// complement/outside region alongside the real rooms - the exterior. <see cref="CellClassifier"/> is
    /// the sole producer; never fabricated, never silently defaulted to <see cref="Interior"/>.
    /// </summary>
    public enum CellRole
    {
        /// <summary>A genuine enclosed room: volume at or above the minimum and its centre lies inside the
        /// model's own outer envelope. The only role Phase 7c turns into a <c>Space</c>.</summary>
        Interior,

        /// <summary>The cell's centre lies OUTSIDE the model's own outer envelope - a bounded complement/
        /// background region the resolve produced alongside the real rooms, not a room itself.</summary>
        Exterior,

        /// <summary>Volume below <see cref="Panel3DSnapSolver.MinCellVolume"/> - a hair's-width artifact
        /// (e.g. a gap the kernel closed into its own micro-cell), not a genuine room. Takes precedence over
        /// Interior/Exterior: a sliver is diagnosed and excluded regardless of where its centre sits.</summary>
        Sliver,

        /// <summary>The inside/outside test could not be evaluated (native kernel unavailable, or no cell
        /// centre was decoded) - never silently treated as Interior.</summary>
        Unknown
    }
}
