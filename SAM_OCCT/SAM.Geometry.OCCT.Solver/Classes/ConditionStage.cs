// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Stage B (conditioning half) - the managed fabrication passes that grow geometry so the kernel
    /// can close cells: extend walls sideways to meet each other (close the plan loop), extend walls
    /// up/down to their caps, then grow the caps out to the walls
    /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §D/§F). A thin orchestrator over the existing
    /// <see cref="Panel3DSnapSolver"/> extend/fill statics, run over a panel list whose per-panel
    /// <c>MaxExtend</c> was carried by source identity through <see cref="SnapStage"/> (so each wall's
    /// reach follows the source the caller set it on, not a list position).
    /// </summary>
    public static class ConditionStage
    {
        /// <summary>The conditioning flags/overshoots, mirroring the <see cref="Panel3DSnapSolver"/> properties.</summary>
        public class Settings
        {
            public bool ExtendWallsToWalls { get; set; } = true;

            public double WallExtendOvershoot { get; set; } = 0.05;

            public bool ExtendToCaps { get; set; } = true;

            public double ExtendOvershoot { get; set; } = 0.05;

            public double RoofOvershoot { get; set; } = 0.5;

            public bool ExtendToRoofs { get; set; } = true;

            public bool FillCapsToWalls { get; set; } = true;

            public double FillMargin { get; set; } = 0.5;

            public double FillOvershoot { get; set; } = 0.05;
        }

        /// <summary>
        /// Runs the extend/fill passes over <paramref name="panels"/> in place, in the same order and with the
        /// same semantics as the pre-Phase-2 inline Step 2 (walls-to-walls, then walls-to-caps, then
        /// caps-to-walls). Expects <paramref name="panels"/> already in the canonical Z-up frame when the level
        /// is tilted (the façade handles the transform).
        /// </summary>
        public static void Condition(List<SnappedPanel> panels, Settings settings, ToleranceBudget tolerances)
        {
            if (panels == null || panels.Count == 0)
            {
                return;
            }

            Settings s = settings ?? new Settings();
            ToleranceBudget tol = tolerances ?? new ToleranceBudget();

            // Walls first: close the plan loop.
            if (s.ExtendWallsToWalls)
            {
                Panel3DSnapSolver.ExtendWalls(panels, tol.VerticalAngle, s.WallExtendOvershoot, tol.Distance);
            }

            // Extend walls up to the floor/roof above and down to the floor below.
            if (s.ExtendToCaps)
            {
                Panel3DSnapSolver.Extend(panels, tol.VerticalAngle, s.ExtendOvershoot, tol.Distance, s.RoofOvershoot, s.ExtendToRoofs);
            }

            // Then grow the caps out to the now-closed walls.
            if (s.FillCapsToWalls)
            {
                Panel3DSnapSolver.Fill(panels, tol.VerticalAngle, s.FillMargin, tol.Distance, s.FillOvershoot);
            }
        }
    }
}
