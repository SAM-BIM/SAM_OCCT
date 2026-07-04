// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// One detected inter-storey stacked-slab interface (Phase 6d): a pair of near-congruent, opposite-facing
    /// cap faces - the floor of level N and the ceiling of level N-1 - that represent the same physical
    /// inter-storey boundary and are (or are treated as) one geometric interface for the cell build. Produced
    /// by <see cref="StackedSlabInterfaceDetector.DetectStackedInterfaces"/>; a pure, immutable record with no
    /// geometry mutation of its own (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §E Phase 6).
    /// </summary>
    /// <remarks>
    /// The two skins are addressed by their INPUT face index (which is also their <see cref="SourceMap"/> source
    /// index), so a caller can verify both analytical source panels survive into the output. <see cref="LowerSourceIndex"/>
    /// / <see cref="UpperSourceIndex"/> are ordered by elevation (lower skin first) purely for determinism.
    /// </remarks>
    public sealed class StackedSlabInterface
    {
        /// <summary>Input face index of the lower (smaller-elevation centroid) skin - also its <see cref="SourceMap"/> source index.</summary>
        public int LowerSourceIndex { get; }

        /// <summary>Input face index of the upper skin - also its <see cref="SourceMap"/> source index.</summary>
        public int UpperSourceIndex { get; }

        /// <summary>Level-frame the lower skin was assigned to (<see cref="LevelFrame"/> cluster index), or -1.</summary>
        public int LowerFrameIndex { get; }

        /// <summary>Level-frame the upper skin was assigned to (<see cref="LevelFrame"/> cluster index), or -1.</summary>
        public int UpperFrameIndex { get; }

        /// <summary>Perpendicular separation (metres) between the two skins - the slab thickness. ~0 for coincident
        /// skins; up to the configured slab band for a modelled slab gap.</summary>
        public double Separation { get; }

        /// <summary>In-plane overlap ratio of the two skins' footprints (1.0 when congruent) - the congruence
        /// evidence that this is one interface, not two unrelated caps.</summary>
        public double OverlapRatio { get; }

        /// <summary>The interface's mid elevation (world Z of the two skins' centroid midpoint) - the deterministic
        /// sort key <see cref="StackedSlabInterfaceDetector.DetectStackedInterfaces"/> orders interfaces by.</summary>
        public double Elevation { get; }

        public StackedSlabInterface(int lowerSourceIndex, int upperSourceIndex, int lowerFrameIndex, int upperFrameIndex, double separation, double overlapRatio, double elevation)
        {
            LowerSourceIndex = lowerSourceIndex;
            UpperSourceIndex = upperSourceIndex;
            LowerFrameIndex = lowerFrameIndex;
            UpperFrameIndex = upperFrameIndex;
            Separation = separation;
            OverlapRatio = overlapRatio;
            Elevation = elevation;
        }

        public override string ToString()
        {
            return string.Format(
                "StackedSlabInterface[{0}/{1} frames {2}/{3} sep={4:0.###}m overlap={5:P0} z={6:0.###}]",
                LowerSourceIndex, UpperSourceIndex, LowerFrameIndex, UpperFrameIndex, Separation, OverlapRatio, Elevation);
        }
    }
}
