// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.Attributes;
using System.ComponentModel;

namespace SAM.Analytical.OCCT.Solver
{
    /// <summary>
    /// Panel-reconstruction bookkeeping stamped by <see cref="PanelReconstruction.Build"/>
    /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md Phase 4) so a split/merge is still legible
    /// after the fact, alongside the geometry-only <c>SAM.Analytical.Solver.SolverParameter</c> stamps
    /// (BucketSize/Weight/MaxExtend). String-valued - mirrors <c>SolverParameter</c>'s enum/attribute
    /// pattern but targets <see cref="IPanel"/> directly since these values are never numeric.
    /// </summary>
    [AssociatedTypes(typeof(IPanel)), Description("OCCT Solve3D Panel Provenance Parameter")]
    public enum PanelProvenanceParameter
    {
        /// <summary>
        /// On a split piece (new Guid): the original source panel's Guid, as a string. Absent on a
        /// 1:1-mapped panel (which keeps the source's own Guid directly) and on a merge's dominant panel.
        /// </summary>
        [ParameterProperties("Source Guid", "The original source panel's Guid, for a split piece that was given a new Guid.")]
        SourceGuid,

        /// <summary>
        /// On a merge's dominant panel (which keeps its own Guid): the other contributing sources'
        /// Guids, comma-separated. Absent on a 1:1-mapped panel and on a non-dominant split piece.
        /// </summary>
        [ParameterProperties("Merged Source Guids", "Comma-separated Guids of the other source panels folded into this one by the native resolve.")]
        MergedSourceGuids,

        /// <summary>Free-form provenance tag, e.g. "GapFill" on an air panel built from a closed naked-boundary loop.</summary>
        [ParameterProperties("Provenance", "How this panel came to exist (e.g. GapFill for a closed-gap air panel).")]
        Provenance
    }
}
