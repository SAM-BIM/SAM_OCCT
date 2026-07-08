// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// The explicit per-stage tolerance bands the solver consumes
    /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §M). Each stage reads only its own band;
    /// the invariant is that snapping makes geometry exact so the kernel's fuzzy band only absorbs
    /// residual noise - never widen fuzzy/sew to compensate for un-snapped input.
    /// </summary>
    /// <remarks>
    /// Phase 2 introduces this as the carrier for the values the managed stages already use (the
    /// full drift-monitoring / adaptive-sew bands are populated by later phases). Defaults mirror the
    /// existing <see cref="Panel3DSnapSolver"/> defaults so behaviour is unchanged.
    /// </remarks>
    public class ToleranceBudget
    {
        /// <summary>SAM geometric distance tolerance (metres) - used everywhere managed.</summary>
        public double Distance { get; set; } = Tolerance.Distance;

        /// <summary>Angular tolerance (radians) for the fully-contained cluster / parallelism test (5 deg default).</summary>
        public double Angle { get; set; } = 5 * (System.Math.PI / 180);

        /// <summary>Angular tolerance (radians) for the partial-capture / arc test (0.3 deg default).</summary>
        public double ArcAngle { get; set; } = 0.3 * (System.Math.PI / 180);

        /// <summary>Angle (radians) within which a panel normal counts as horizontal, so the panel is a wall (20 deg default).</summary>
        public double VerticalAngle { get; set; } = 20 * (System.Math.PI / 180);
    }
}
