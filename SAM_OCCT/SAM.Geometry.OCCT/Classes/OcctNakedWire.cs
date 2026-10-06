// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    /// <summary>
    /// A free-boundary (naked) wire grouped from the <c>ShapeAnalysis_FreeBounds</c> pass
    /// <c>Query.Validate</c> already runs (ABI v4, observational). An ordered polyline of the
    /// wire's vertices (the closing vertex is NOT duplicated), whether it closes on itself, and
    /// the best-effort owning face of each edge. Feeds diagnostics, gap-fill loops and display
    /// without a second free-bounds computation (docs/P3_ABI_V4_NATIVE_HISTORY_DESIGN_REVIEW.md §I).
    /// </summary>
    public sealed class OcctNakedWire
    {
        private readonly List<Point3D> point3Ds;
        private readonly List<int> edgeOwnerFaceIndices;

        public OcctNakedWire(IEnumerable<Point3D> point3Ds, bool isClosed, IEnumerable<int> edgeOwnerFaceIndices)
        {
            this.point3Ds = point3Ds?.Where(x => x != null).ToList() ?? new List<Point3D>();
            IsClosed = isClosed;
            this.edgeOwnerFaceIndices = edgeOwnerFaceIndices?.ToList() ?? new List<int>();
        }

        /// <summary>Ordered polyline vertices; the closing vertex of a closed wire is not repeated.</summary>
        public IReadOnlyList<Point3D> Point3Ds
        {
            get { return point3Ds; }
        }

        /// <summary>True when the wire closes on itself (edge count == point count).</summary>
        public bool IsClosed { get; }

        /// <summary>
        /// Owning face index of each edge (its position in the validated shape's face
        /// enumeration order), or -1 when unknown. Best-effort - callers must tolerate -1.
        /// </summary>
        public IReadOnlyList<int> EdgeOwnerFaceIndices
        {
            get { return edgeOwnerFaceIndices; }
        }
    }
}
