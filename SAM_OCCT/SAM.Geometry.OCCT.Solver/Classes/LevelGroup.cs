// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// A second-stage merge of one or more raw <see cref="LevelFrame"/>s onto a single level datum
    /// (docs/CONTROLLED_WORKFLOW_PLAN.md §4.1). The raw <see cref="LevelFrame.Cluster"/> keeps its pinned
    /// 0.15 m band so a deliberate ~0.25 m split-level landing is never merged away; a <see cref="LevelGroup"/>
    /// is the OPTIONAL wider grouping over those frames, controlled by the user's <c>bucketBetweenLevels</c>
    /// band, that merges the near-coplanar slab-skin datums a single physical floor was imported as (e.g. the
    /// 12.240 m and 12.436 m frames of the controlled fixture) so cap normalization and wall-to-cap extension
    /// target one datum per storey instead of several.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built by <see cref="LevelFrame.GroupFrames"/>, which is dominant-area-first and seed-anchored exactly
    /// like <see cref="LevelFrame.Cluster"/>: the largest-area not-yet-assigned frame seeds a group (its datum
    /// becomes the group datum), then every remaining frame parallel within the normal cone and within the band
    /// perpendicular of that SEED datum joins it. Membership is tested against the seed only (never transitively),
    /// so a group is anchored by its dominant frame and cannot chain across a wide span. The grouping affects
    /// cap normalization and extend targets only - never wall bucket membership (docs plan §2 guardrail 3).
    /// </para>
    /// <para>
    /// When the band is &lt;= 0 the grouping is the identity (one group per frame), so the default
    /// (<c>bucketBetweenLevels = 0</c>) behaviour is byte-identical to the raw frames.
    /// </para>
    /// </remarks>
    public class LevelGroup
    {
        /// <summary>The group datum's up-axis: the <c>+Z</c>-hemisphere unit normal of the dominant (largest-area)
        /// member frame. World Z for a flat storey; the level normal for a tilted one.</summary>
        public Vector3D Normal { get; }

        /// <summary>A point on the group datum plane - the dominant member frame's origin.</summary>
        public Point3D Origin { get; }

        /// <summary>The canonical group datum plane (<see cref="Origin"/>, <see cref="Normal"/>) that caps are
        /// normalized onto and walls extend to.</summary>
        public Plane Plane { get; }

        /// <summary>Signed perpendicular offset of <see cref="Origin"/> from the world origin along
        /// <see cref="Normal"/> - the group's elevation (equals the dominant member frame's elevation), and the
        /// deterministic sort key <see cref="LevelFrame.GroupFrames"/> orders the returned groups by.</summary>
        public double Elevation { get; }

        /// <summary>Angle (radians) between <see cref="Normal"/> and world Z: 0 for a flat storey, the tilt for a
        /// sloped one (inherited from the dominant member frame).</summary>
        public double TiltAngle { get; }

        /// <summary>Largest member frame's dominant cap area (m²) - the frame that seeded the group and supplied
        /// its datum. The deterministic tie-break for seed order.</summary>
        public double DominantArea { get; }

        /// <summary>Indices (into the frame list handed to <see cref="LevelFrame.GroupFrames"/>) of the member
        /// frames, ascending for determinism.</summary>
        public IReadOnlyList<int> FrameIndices { get; }

        /// <summary>The member frames' elevations, ascending - the raw datums this group merged (e.g. 12.240,
        /// 12.436). Reporting aid for the SAM_OCCT_CLEAN3D_LEVELGROUP line.</summary>
        public IReadOnlyList<double> MemberElevations { get; }

        /// <summary>The perpendicular spread (m) of the member frames: <c>max - min</c> member elevation. 0 for a
        /// single-frame (identity) group; the 0.196 m of the fixture's 12.240+12.436 group at band 0.21.</summary>
        public double Spread { get; }

        /// <summary>Total member cap count - the sum of the member frames' <see cref="LevelFrame.CapIndices"/>
        /// counts. How many cap faces this datum owns.</summary>
        public int CapCount { get; }

        internal LevelGroup(LevelFrame seed, IReadOnlyList<int> frameIndices, IReadOnlyList<double> memberElevations, int capCount)
        {
            Normal = seed.Normal;
            Origin = seed.Origin;
            Plane = seed.Plane;
            Elevation = seed.Elevation;
            TiltAngle = seed.TiltAngle;
            DominantArea = seed.DominantArea;
            FrameIndices = frameIndices ?? new List<int>();
            List<double> elevations = (memberElevations ?? new List<double>()).OrderBy(x => x).ToList();
            MemberElevations = elevations;
            Spread = elevations.Count == 0 ? 0.0 : elevations[elevations.Count - 1] - elevations[0];
            CapCount = capCount;
        }

        /// <summary>
        /// A datum-only <see cref="LevelFrame"/> at this group's plane (no member caps). Fed to the frame-aware
        /// <c>Panel3DSnapSolver.NormalizeCaps</c>
        /// so a level's caps normalize onto the GROUP datum (one per storey) instead of the several raw
        /// frame datums.
        /// </summary>
        public LevelFrame ToDatumFrame()
        {
            return new LevelFrame(Normal, Origin);
        }
    }
}
