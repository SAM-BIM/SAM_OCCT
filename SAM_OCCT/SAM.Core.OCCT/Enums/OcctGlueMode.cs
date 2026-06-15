// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OCCT
{
    /// <summary>
    /// BOP "glue" mode for the cell-complex / volume builders (issue #37
    /// follow-on). Glue tells the boolean kernel that coincident faces are truly
    /// shared and lets it skip their pairwise intersection - a large throughput
    /// win on cell complexes with thousands of coincident shared walls. It
    /// CORRUPTS merely-near-coincident faces, so it is only applied to input that
    /// a watertightness check reports as clean. The integer values mirror the
    /// native <c>glue_mode</c> ABI argument exactly.
    /// </summary>
    public enum OcctGlueMode
    {
        /// <summary>No gluing (BOPAlgo_GlueOff) - the safe default.</summary>
        Off = 0,

        /// <summary>Glue by vertex shift (BOPAlgo_GlueShift): coincident vertices are merged but faces are still intersected pairwise where they overlap.</summary>
        Shift = 1,

        /// <summary>Full glue (BOPAlgo_GlueFull): coincident faces are treated as fully shared and not intersected. Fastest, but the least tolerant of near-coincidence.</summary>
        Full = 2
    }
}
