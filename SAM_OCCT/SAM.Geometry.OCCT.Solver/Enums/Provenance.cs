// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// How an output face relates to the input it came from, recorded in a <see cref="SourceMap"/>
    /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §D/§G). Ordered by pipeline stage: an input
    /// face is <see cref="Input"/>; the managed Snap stage marks its outputs <see cref="Snapped"/>;
    /// the Condition stage <see cref="Conditioned"/>; the native cell build <see cref="Resolved"/>;
    /// a re-added dropped face <see cref="DroppedRetained"/>; a fabricated gap-fill patch
    /// <see cref="GapFill"/>.
    /// </summary>
    public enum Provenance
    {
        Input,
        Snapped,
        Conditioned,
        Resolved,
        DroppedRetained,
        GapFill
    }
}
