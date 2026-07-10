// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.OCCT.Solver
{
    /// <summary>
    /// One expected <see cref="Space"/>'s <see cref="SpaceMatcher"/> result
    /// (docs/CONTROLLED_WORKFLOW_PLAN.md §6). Identity is <see cref="Guid"/>; <see cref="Name"/>/<see cref="Label"/>
    /// are report-facing only.
    /// </summary>
    public class SpaceMatchRecord
    {
        /// <summary>The expected space's Guid.</summary>
        public Guid Guid { get; }

        /// <summary>The expected space's own (possibly duplicated) name.</summary>
        public string Name { get; }

        /// <summary>The disambiguated report label (<see cref="ExpectedSpaceSet.Labels"/>).</summary>
        public string Label { get; }

        public SpaceMatchOutcome Outcome { get; }

        /// <summary>The expected location; null when the space carries no valid location (always <see cref="SpaceMatchOutcome.Missing"/>).</summary>
        public Point3D Location { get; }

        /// <summary>The matched cell index/indices: one for Matched/IncorrectlyBounded, one shared index for Merged, two-or-more for Split, empty for Missing.</summary>
        public IReadOnlyList<int> CellIndices { get; }

        /// <summary>Merged: the Guids of the other expected spaces sharing the cell. Empty otherwise.</summary>
        public IReadOnlyList<Guid> PartnerGuids { get; }

        /// <summary>Missing: index of the nearest cell by centre distance; -1 when no cell exists at all.</summary>
        public int NearestCellIndex { get; }

        /// <summary>Missing: distance from the expected location to the nearest cell's centre; <see cref="double.NaN"/> otherwise.</summary>
        public double NearestDistance { get; }

        /// <summary>The expected vertical span <c>[floor, ceiling]</c> from <see cref="ExpectedSpaceSet.LevelSpans"/>; null when unavailable.</summary>
        public double[] ExpectedSpan { get; }

        /// <summary>The matched cell's actual vertical span <c>[zMin, zMax]</c>; null for Missing.</summary>
        public double[] ActualSpan { get; }

        /// <summary>Split: the datum elevation the cell unexpectedly split at (snapped to the nearest level-group datum). <see cref="double.NaN"/> otherwise.</summary>
        public double SplitElevation { get; }

        /// <summary>True when the expected location sat on more than one cell's boundary and was assigned to the nearest centre.</summary>
        public bool Boundary { get; }

        /// <summary>When <see cref="Boundary"/> is true: every cell index the expected location was found inside/on, ascending. Empty otherwise.</summary>
        public IReadOnlyList<int> BoundaryCellIndices { get; }

        /// <summary>Free-form detail text for the report line (e.g. "2 expected locations in one cell").</summary>
        public string Detail { get; }

        public SpaceMatchRecord(
            Guid guid,
            string name,
            string label,
            SpaceMatchOutcome outcome,
            Point3D location,
            IReadOnlyList<int> cellIndices,
            IReadOnlyList<Guid> partnerGuids,
            int nearestCellIndex,
            double nearestDistance,
            double[] expectedSpan,
            double[] actualSpan,
            double splitElevation,
            bool boundary,
            IReadOnlyList<int> boundaryCellIndices,
            string detail)
        {
            Guid = guid;
            Name = name;
            Label = label;
            Outcome = outcome;
            Location = location;
            CellIndices = cellIndices ?? new List<int>();
            PartnerGuids = partnerGuids ?? new List<Guid>();
            NearestCellIndex = nearestCellIndex;
            NearestDistance = nearestDistance;
            ExpectedSpan = expectedSpan;
            ActualSpan = actualSpan;
            SplitElevation = splitElevation;
            Boundary = boundary;
            BoundaryCellIndices = boundaryCellIndices ?? new List<int>();
            Detail = detail ?? string.Empty;
        }
    }
}
