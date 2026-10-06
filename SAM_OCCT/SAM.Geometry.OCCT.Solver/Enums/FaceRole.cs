// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// The role a panel face plays relative to a <see cref="LevelFrame"/>: the frame-aware analogue of the
    /// solver's binary world-Z wall/cap split (Phase 6). A face is a <see cref="Wall"/> when its normal is
    /// (near) perpendicular to the frame's up-axis, and a <see cref="Cap"/> (floor/roof) otherwise - the same
    /// partition the pipeline draws with <c>IsVertical</c>, but measured against the level's own up-axis rather
    /// than world Z, so a tilted level's walls stay walls past the 20° world-frame ceiling.
    /// </summary>
    public enum FaceRole
    {
        /// <summary>A cap (floor/roof): normal (near) parallel to the level frame's up-axis.</summary>
        Cap,

        /// <summary>A wall: normal (near) perpendicular to the level frame's up-axis.</summary>
        Wall
    }
}
