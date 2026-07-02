// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// The 3D analogue of the 2D solver's closure signature (loop count + area): a compact,
    /// comparable fingerprint of a resolved cell complex - how many cells closed, how much volume
    /// they enclose, how many naked (unresolved) boundary edges remain, how many faces the resolve
    /// produced, and how many source faces have no surviving representation in the output.
    /// </summary>
    /// <remarks>
    /// Two signatures captured from the same fixture on the same OCCT build should be identical;
    /// this is the golden-master tripwire (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md, Phase
    /// 0) later phases assert against so a refactor's effect on closure is measured, not guessed.
    /// <para>
    /// <see cref="DroppedCount"/> is intentionally a plain caller-supplied count in this phase: the
    /// plan's mapping-derived definition (sources with no surviving representation, via
    /// <c>SourceMap</c>) is introduced in Phase 2/3. Until then, pass 0 (unknown) or a
    /// caller-computed geometric estimate - never a value implying an authoritative mapping.
    /// </para>
    /// </remarks>
    public class ClosureSignature3D
    {
        /// <summary>Number of closed cells (rooms/levels/zones) the resolve formed.</summary>
        public int CellCount { get; }

        /// <summary>Volume of each closed cell, in the same order the resolve reported them.</summary>
        public IReadOnlyList<double> CellVolumes { get; }

        /// <summary>Sum of <see cref="CellVolumes"/>.</summary>
        public double TotalVolume { get; }

        /// <summary>Number of naked (free) boundary edges the native validator reported.</summary>
        public int NakedEdgeCount { get; }

        /// <summary>Number of faces in the resolved geometry this signature was captured from.</summary>
        public int FaceCount { get; }

        /// <summary>Number of source faces with no surviving representation in the resolved output.</summary>
        public int DroppedCount { get; }

        public ClosureSignature3D(int cellCount, IEnumerable<double> cellVolumes, int nakedEdgeCount, int faceCount, int droppedCount)
        {
            CellCount = cellCount;

            List<double> cellVolumes_Temp = cellVolumes == null ? new List<double>() : cellVolumes.ToList();
            CellVolumes = cellVolumes_Temp;
            TotalVolume = cellVolumes_Temp.Sum();

            NakedEdgeCount = nakedEdgeCount;
            FaceCount = faceCount;
            DroppedCount = droppedCount;
        }

        /// <summary>
        /// True when this signature is worse than <paramref name="other"/>: more naked edges, fewer
        /// closed cells, or a shrunken total volume beyond <paramref name="volumeTolerance"/>. Used
        /// to gate acceptance of a pipeline change (an adopted resolve level, an escalation round, a
        /// re-sew) - the change is kept only when it does not regress the signature it is compared
        /// against. A null <paramref name="other"/> has nothing to regress against.
        /// </summary>
        public bool IsRegressionOf(ClosureSignature3D other, double volumeTolerance = Core.Tolerance.MacroDistance)
        {
            if (other == null)
            {
                return false;
            }

            if (NakedEdgeCount > other.NakedEdgeCount)
            {
                return true;
            }

            if (CellCount < other.CellCount)
            {
                return true;
            }

            if (other.TotalVolume - TotalVolume > System.Math.Max(volumeTolerance, 0))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Builds a signature from a decoded <see cref="OcctCellComplexResult"/> - the cell count and
        /// per-cell volumes it already exposes - plus the naked-edge/face/dropped counts measured
        /// alongside it. Reuses the existing cell metadata (<c>sam_occt_result_cell_count/_volume</c>);
        /// no new native operation.
        /// </summary>
        public static ClosureSignature3D FromCellComplexResult(OcctCellComplexResult result, int nakedEdgeCount, int faceCount, int droppedCount = 0)
        {
            IEnumerable<double> cellVolumes = result?.Cells == null ? Enumerable.Empty<double>() : result.Cells.Select(x => x.Volume);
            int cellCount = result?.Cells?.Count ?? 0;
            return new ClosureSignature3D(cellCount, cellVolumes, nakedEdgeCount, faceCount, droppedCount);
        }

        public override string ToString()
        {
            return string.Format(
                "{0} cell(s); {1:0.###} m3 total volume; {2} naked edge(s); {3} face(s); {4} dropped",
                CellCount, TotalVolume, NakedEdgeCount, FaceCount, DroppedCount);
        }
    }
}
