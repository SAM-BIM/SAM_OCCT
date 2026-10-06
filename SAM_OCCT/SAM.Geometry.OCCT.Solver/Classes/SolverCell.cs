// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Additive, disposal-safe snapshot of one resolved cell (Phase 7a): index, volume, centre, and the
    /// cell's boundary shell - copied out of the native decode (<see cref="OcctCell"/>) at the exact point
    /// <see cref="Panel3DSnapSolver.Signature"/> is produced on both the raw and managed paths, so
    /// <see cref="Panel3DSnapSolver.Cells"/> always agrees with <see cref="ClosureSignature3D.CellCount"/>/
    /// <see cref="ClosureSignature3D.CellVolumes"/>. Reuses cell metadata the native decode already exposes
    /// (<c>sam_occt_result_cell_volume</c>/<c>_center</c>) - no new native ABI. <see cref="OcctCell"/>'s own
    /// fields are plain managed data (its <see cref="Point3D"/>/<see cref="Shell"/> are private copies made
    /// at decode time, never backed by a live native handle), so referencing them here remains safe to read
    /// after the source <c>OcctCellComplexResult</c> is disposed.
    /// </summary>
    public class SolverCell
    {
        /// <summary>Index into the adopted result's cell list (stable for one solve, not across solves).</summary>
        public int Index { get; }

        /// <summary>Cell volume in cubic metres.</summary>
        public double Volume { get; }

        /// <summary>Cell centre, or null when the native decoder could not resolve one for this cell.</summary>
        public Point3D Center { get; }

        /// <summary>The cell's closed boundary shell.</summary>
        public Shell Shell { get; }

        public SolverCell(int index, double volume, Point3D center, Shell shell)
        {
            Index = index;
            Volume = volume;
            Center = center;
            Shell = shell;
        }
    }
}
