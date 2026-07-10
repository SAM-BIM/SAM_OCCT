// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Analytical.OCCT.Solver
{
    /// <summary>
    /// Tunables for <see cref="SpaceMatcher"/>/<see cref="ExpectedSpaceSet"/>
    /// (docs/CONTROLLED_WORKFLOW_PLAN.md §6). All defaults match the plan's controlled-fixture values.
    /// </summary>
    public class SpaceMatchOptions
    {
        /// <summary>Containment tolerance, mirroring <c>FindSeedSpace</c>'s own <c>options.Tolerance</c> use of <see cref="Shell.On(Geometry.Spatial.Point3D, double)"/>.</summary>
        public double Tolerance { get; set; } = Core.Tolerance.Distance;

        /// <summary>Containment silver-spacing, mirroring <c>FindSeedSpace</c>'s own <c>options.FuzzyTolerance</c> use of <see cref="Shell.Inside(Geometry.Spatial.Point3D, double, double)"/>.</summary>
        public double SilverSpacing { get; set; } = Core.Tolerance.MacroDistance;

        /// <summary>Perpendicular band (metres) used to compare a cell's vertical span against an expected level datum pair.</summary>
        public double LevelBand { get; set; } = 0.21;

        /// <summary>Perpendicular band (metres) used to merge raw <c>LevelFrame</c> elevations (from input-panel caps) into level-group datums for <see cref="ExpectedSpaceSet"/>.</summary>
        public double LevelGroupBand { get; set; } = 0.21;

        /// <summary>Minimum fraction (of the smaller footprint) of plan-bbox overlap required for an unclaimed cell to count as a <see cref="SpaceMatchOutcome.Split"/> partner.</summary>
        public double MinSplitPlanOverlap { get; set; } = 0.5;

        /// <summary>Lateral margin (metres) used by <see cref="SeparatorPanelFinder"/> to classify a candidate separator as a full lateral match versus a "partial" near-miss.</summary>
        public double LateralMargin { get; set; } = 0.5;

        public SpaceMatchOptions()
        {
        }

        public SpaceMatchOptions(SpaceMatchOptions options)
        {
            if (options == null)
            {
                return;
            }

            Tolerance = options.Tolerance;
            SilverSpacing = options.SilverSpacing;
            LevelBand = options.LevelBand;
            LevelGroupBand = options.LevelGroupBand;
            MinSplitPlanOverlap = options.MinSplitPlanOverlap;
            LateralMargin = options.LateralMargin;
        }
    }
}
