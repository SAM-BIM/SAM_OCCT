// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.Analytical.OCCT.Solver
{
    /// <summary>
    /// A uniform, anonymous per-cell geometry view for <see cref="SpaceMatcher"/>
    /// (docs/CONTROLLED_WORKFLOW_PLAN.md §6): index, boundary <see cref="Shell"/>, centre and volume,
    /// regardless of whether it came from a finished <see cref="AdjacencyCluster"/> (<see cref="FromCluster"/>)
    /// or directly from an <see cref="OcctCellComplexResult"/> (<see cref="FromComplex"/>). Deliberately
    /// carries no Space/name identity: <see cref="SAMOCCTCreateAdjacencyCluster"/>'s builder-side space
    /// matching (<c>FindSeedSpace</c>, greedy first-fit) is not the validation reference (plan §0.3) - the
    /// matcher re-derives containment itself against this anonymous geometry.
    /// </summary>
    public class CellGeometry
    {
        /// <summary>Position in the source cell list (<see cref="AdjacencyCluster.GetShells"/> order, or <see cref="OcctCellComplexResult.Cells"/> order).</summary>
        public int Index { get; }

        /// <summary>The cell's boundary shell.</summary>
        public Shell Shell { get; }

        /// <summary>The cell's centre point; bounding-box centroid for a cluster-derived cell (no volumetric centroid is exposed at that layer), the OCCT-computed centre for a complex-derived cell.</summary>
        public Point3D Center { get; }

        /// <summary>The cell's volume (m³).</summary>
        public double Volume { get; }

        public CellGeometry(int index, Shell shell, Point3D center, double volume)
        {
            Index = index;
            Shell = shell;
            Center = center;
            Volume = volume;
        }

        /// <summary>
        /// One <see cref="CellGeometry"/> per space-shell a finished <see cref="AdjacencyCluster"/> already
        /// carries (<see cref="AdjacencyCluster.GetShells"/>), in that same order. Used as the matcher's
        /// cell-geometry source for the <c>SAMOCCT.ValidateSpaces</c> component, which only ever receives a
        /// built <see cref="AdjacencyCluster"/>, not the raw <see cref="OcctCellComplexResult"/>.
        /// </summary>
        public static List<CellGeometry> FromCluster(AdjacencyCluster adjacencyCluster, SpaceMatchOptions options = null)
        {
            List<CellGeometry> result = new List<CellGeometry>();
            List<Shell> shells = adjacencyCluster?.GetShells();
            if (shells == null)
            {
                return result;
            }

            SpaceMatchOptions resolvedOptions = options ?? new SpaceMatchOptions();
            for (int i = 0; i < shells.Count; i++)
            {
                Shell shell = shells[i];
                if (shell == null)
                {
                    continue;
                }

                BoundingBox3D box = shell.GetBoundingBox();
                Point3D center = box?.GetCentroid();
                double volume = shell.Volume(resolvedOptions.SilverSpacing, resolvedOptions.Tolerance);
                result.Add(new CellGeometry(i, shell, center, volume));
            }

            return result;
        }

        /// <summary>
        /// One <see cref="CellGeometry"/> per <see cref="OcctCell"/> in an <see cref="OcctCellComplexResult"/>
        /// (its own already-computed centre/volume), in <see cref="OcctCellComplexResult.Cells"/> order. Used
        /// by tests/diagnostics that have the raw build result on hand (e.g. the P0 baseline harness) rather
        /// than a finished <see cref="AdjacencyCluster"/>.
        /// </summary>
        public static List<CellGeometry> FromComplex(OcctCellComplexResult result)
        {
            List<CellGeometry> list = new List<CellGeometry>();
            IReadOnlyList<OcctCell> cells = result?.Cells;
            if (cells == null)
            {
                return list;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                OcctCell cell = cells[i];
                if (cell?.Shell == null)
                {
                    continue;
                }

                list.Add(new CellGeometry(i, cell.Shell, cell.Center, cell.Volume));
            }

            return list;
        }
    }
}
