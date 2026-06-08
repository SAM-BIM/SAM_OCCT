// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT
{
    /// <summary>
    /// Controls how <see cref="Modify.MergeSmallShells"/> chooses the larger shell
    /// that a small shell is merged (unioned) into when several adjacent shells are
    /// valid candidates.
    /// </summary>
    public enum MergeShellsMode
    {
        /// <summary>
        /// Merge into the neighbour with the largest estimated shared contact area
        /// (the largest touching/overlapping boundary). Keeps the merged volume as
        /// closed as possible.
        /// </summary>
        LongestSharedBoundary,

        /// <summary>
        /// Merge into the neighbour with the largest floor footprint (falling back
        /// to the largest bounding-box volume when footprint cannot be measured).
        /// </summary>
        LargestNeighbour
    }
}
