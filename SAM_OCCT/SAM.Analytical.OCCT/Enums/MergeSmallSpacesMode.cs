// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Analytical.OCCT
{
    /// <summary>
    /// Controls how <see cref="Modify.MergeSmallSpaces"/> chooses the target space
    /// that a small space is merged into when several adjacent spaces are valid
    /// candidates.
    /// </summary>
    public enum MergeSmallSpacesMode
    {
        /// <summary>
        /// Merge into the neighbour that shares the largest total boundary area
        /// (the largest shared panel surface). This keeps the merged volume as
        /// closed as possible.
        /// </summary>
        LongestSharedBoundary,

        /// <summary>
        /// Merge into the neighbour with the largest floor area (falling back to
        /// the largest volume when floor area cannot be measured).
        /// </summary>
        LargestNeighbour,

        /// <summary>
        /// Prefer a neighbour with the same space type (matching internal
        /// condition). If no same-type neighbour is available, fall back to the
        /// neighbour with the largest shared boundary.
        /// </summary>
        SameTypeFirst
    }
}
