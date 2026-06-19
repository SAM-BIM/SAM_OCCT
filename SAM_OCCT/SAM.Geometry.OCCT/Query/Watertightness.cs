// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Native;
using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.Geometry.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// Pure-managed watertightness summary of a face soup: welds every loop
        /// edge of the supplied faces at the build tolerance and counts how many
        /// faces use each edge. A closed manifold volume uses every edge an even
        /// number of times, so any edge used an odd number of times is <b>naked</b>
        /// (an open gap) and marks exactly where the shell fails to close. This is
        /// the managed equivalent of <c>ShapeAnalysis_FreeBounds</c> and needs no
        /// native OCCT library, so callers (e.g. the mesh-input path in
        /// SAMOCCT.CreateAdjacencyClusterByShells) can pre-check a shell before the
        /// native volume build and report the gap rather than surface an opaque
        /// MakerVolume failure.
        /// </summary>
        /// <param name="face3Ds">The faces to analyse (e.g. the faces of one shell).</param>
        /// <param name="options">Build options; <see cref="OcctBuildOptions.Tolerance"/> is the welding distance. Null uses defaults.</param>
        /// <param name="edgeCount">Number of distinct welded edges.</param>
        /// <param name="nakedEdgeCount">Edges used an odd number of times - real gaps. Zero means the faces bound a closed volume.</param>
        /// <param name="nonManifoldEdgeCount">Edges shared by more than two faces (expected between adjacent cells, not a defect on a single closed shell).</param>
        /// <returns>True when there were faces to analyse (i.e. <paramref name="edgeCount"/> &gt; 0).</returns>
        public static bool Watertightness(this IEnumerable<Face3D> face3Ds, OcctBuildOptions options, out int edgeCount, out int nakedEdgeCount, out int nonManifoldEdgeCount)
        {
            edgeCount = 0;
            nakedEdgeCount = 0;
            nonManifoldEdgeCount = 0;

            if (face3Ds == null)
            {
                return false;
            }

            OcctOpenShellAnalysis.WatertightnessSummary summary = OcctOpenShellAnalysis.AnalyzeWatertightness(face3Ds, options ?? new OcctBuildOptions());
            edgeCount = summary.EdgeCount;
            nakedEdgeCount = summary.NakedEdgeCount;
            nonManifoldEdgeCount = summary.NonManifoldEdgeCount;

            return edgeCount > 0;
        }
    }
}
