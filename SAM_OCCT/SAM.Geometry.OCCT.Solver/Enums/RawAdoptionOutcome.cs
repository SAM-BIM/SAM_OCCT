// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Outcome of the raw-first (L0) adoption gate
    /// (<see cref="Panel3DSnapSolver.EvaluateRawAdoption"/>): whether a raw resolve is trusted as-is,
    /// and if not, which check rejected it.
    /// </summary>
    public enum RawAdoptionOutcome
    {
        Adopted,

        /// <summary>No closed cell formed at all.</summary>
        RejectedNoCells,

        /// <summary>The resolved envelope has one or more naked (free) boundary edges - a gappy solve.</summary>
        RejectedNakedEdges,

        /// <summary>A resolved cell is smaller than <see cref="Panel3DSnapSolver.MinCellVolume"/> - a
        /// watertight-but-degenerate artifact, not a genuine room.</summary>
        RejectedSliverCell,

        /// <summary>More than <see cref="Panel3DSnapSolver.MaxDroppedRatio"/> of input faces bound no
        /// closed cell - watertight-but-wrong (e.g. a partition that fails to split the rooms it should).</summary>
        RejectedDroppedRatio,

        /// <summary>At least one dropped wall-like input face lies strictly INTERIOR to a single adopted cell,
        /// spanning a real fraction of its height - a room-dividing partition the raw build failed to imprint,
        /// so two (or more) rooms silently merged into one cell. Watertight-but-wrong, and invisible to the
        /// dropped-RATIO check when only a few partitions are dropped out of many faces (codex #7).</summary>
        RejectedUnderSplit
    }
}
