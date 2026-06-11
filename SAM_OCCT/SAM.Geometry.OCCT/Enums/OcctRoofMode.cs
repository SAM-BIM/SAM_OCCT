// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT
{
    /// <summary>
    /// Controls how <see cref="Create.ShellsFromVerticalFace3Ds"/> caps the walls on top.
    /// </summary>
    public enum OcctRoofMode
    {
        /// <summary>
        /// Horizontal floors and roofs at the clustered wall bottom/top elevations.
        /// Walls of different heights produce stacked cells per level. This is the
        /// default and matches the managed SAM CreateShells behaviour.
        /// </summary>
        Flat,

        /// <summary>
        /// Horizontal floors at the clustered wall bottom elevations, but a single
        /// sloped/pitched roof that follows the wall tops. When the wall tops are
        /// coplanar (flat, mono-pitch, or any single tilt) the roof is one planar
        /// face; when they are not (gable, hip, stepped tops) the wall-top envelope
        /// is triangulated. Targets a single roof level - use <see cref="Flat"/> for
        /// flat multi-storey stacks.
        /// </summary>
        Sloped
    }
}
