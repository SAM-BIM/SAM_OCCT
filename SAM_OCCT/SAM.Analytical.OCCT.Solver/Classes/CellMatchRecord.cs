// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.OCCT.Solver
{
    /// <summary>
    /// One generated cell's <see cref="SpaceMatcher"/> result (docs/CONTROLLED_WORKFLOW_PLAN.md §6) - the
    /// cell-indexed counterpart to <see cref="SpaceMatchRecord"/>. A cell hosting one or more expected
    /// spaces carries <see cref="SpaceMatchOutcome.Matched"/> or <see cref="SpaceMatchOutcome.Merged"/>; an
    /// unclaimed cell not consumed as a <see cref="SpaceMatchOutcome.Split"/> partner carries
    /// <see cref="SpaceMatchOutcome.Extra"/>.
    /// </summary>
    public class CellMatchRecord
    {
        public int CellIndex { get; }

        public SpaceMatchOutcome Outcome { get; }

        /// <summary>Guids of the expected spaces this cell hosts (empty for Extra).</summary>
        public IReadOnlyList<Guid> SpaceGuids { get; }

        public Point3D Center { get; }

        public double Volume { get; }

        /// <summary>The cell's actual vertical span <c>[zMin, zMax]</c>.</summary>
        public double[] ActualSpan { get; }

        public CellMatchRecord(int cellIndex, SpaceMatchOutcome outcome, IReadOnlyList<Guid> spaceGuids, Point3D center, double volume, double[] actualSpan)
        {
            CellIndex = cellIndex;
            Outcome = outcome;
            SpaceGuids = spaceGuids ?? new List<Guid>();
            Center = center;
            Volume = volume;
            ActualSpan = actualSpan;
        }
    }
}
