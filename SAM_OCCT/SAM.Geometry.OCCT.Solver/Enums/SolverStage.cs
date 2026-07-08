// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// The three-stage true-3D pipeline a <see cref="SolverDiagnostic"/> is attributed to
    /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §D): Snap (managed, non-fabricating),
    /// Resolve (native cell build, adoption levels raw/snapped/conditioned), Heal (diagnosis-driven
    /// closure - escalation, sew, gap-fill).
    /// </summary>
    public enum SolverStage
    {
        Snap,
        Resolve,
        Heal
    }
}
