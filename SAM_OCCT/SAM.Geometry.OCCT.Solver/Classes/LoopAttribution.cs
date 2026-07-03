// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Attributes the naked (free-boundary) wires of a resolved cell complex back to the input source
    /// panel(s) responsible for them - the "which panels do I escalate?" step of diagnosis-driven closure
    /// (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §E). Each naked edge's owning resolved face is
    /// read from the native per-edge owner index (<see cref="OcctNakedWire.EdgeOwnerFaceIndices"/>, whose
    /// order aligns with the face list handed to <c>Query.Validate</c>); an unknown (-1) or out-of-range
    /// owner falls back to the nearest coplanar resolved face by the edge midpoint. The owning face index
    /// is then mapped to its source panel(s) through the <see cref="SourceMap"/>, excluding the fabricated
    /// sentinel. Wholly managed and native-free: it consumes the wires/faces/map the solver already holds.
    /// </summary>
    public static class LoopAttribution
    {
        /// <summary>|n·n| floor for the midpoint fallback to treat a resolved face as coplanar with an edge.</summary>
        private const double ParallelDot = 0.99;

        /// <summary>
        /// The set of source panel indices the naked <paramref name="wires"/> attribute to (the culprits to
        /// escalate), sorted ascending for determinism and excluding <see cref="SourceMap.FabricatedSource"/>.
        /// A naked edge whose owning face cannot be resolved to any real source is counted and reported (per
        /// wire) as an ambiguous-attribution <see cref="DiagnosticCode.NakedLoop"/> warning - never silent.
        /// </summary>
        public static IReadOnlyList<int> AttributeLoopsToSources(
            IEnumerable<OcctNakedWire> wires,
            IReadOnlyList<Face3D> resolvedFace3Ds,
            SourceMap sourceMap,
            SolverDiagnostics diagnostics = null)
        {
            SortedSet<int> culprits = new SortedSet<int>();
            if (wires == null || sourceMap == null)
            {
                return culprits.ToList();
            }

            List<Face3D> faces = resolvedFace3Ds as List<Face3D> ?? resolvedFace3Ds?.ToList() ?? new List<Face3D>();

            foreach (OcctNakedWire wire in wires)
            {
                if (wire == null)
                {
                    continue;
                }

                IReadOnlyList<Point3D> points = wire.Point3Ds;
                IReadOnlyList<int> owners = wire.EdgeOwnerFaceIndices;
                int pointCount = points?.Count ?? 0;
                if (pointCount < 2)
                {
                    continue; // a single vertex has no edge to attribute
                }

                int edgeCount = wire.IsClosed ? pointCount : pointCount - 1;
                int unattributed = 0;

                for (int i = 0; i < edgeCount; i++)
                {
                    Point3D a = points[i];
                    Point3D b = points[wire.IsClosed ? (i + 1) % pointCount : i + 1];
                    Point3D midpoint = Midpoint(a, b);

                    int owner = (owners != null && i < owners.Count) ? owners[i] : -1;
                    int faceIndex = owner >= 0 && owner < faces.Count
                        ? owner
                        : NearestCoplanarFaceIndex(midpoint, faces);

                    if (faceIndex < 0 || faceIndex >= faces.Count)
                    {
                        unattributed++;
                        continue;
                    }

                    IReadOnlyList<int> sources = sourceMap.SourcesOf(new FaceKey(faceIndex));
                    bool attributed = false;
                    foreach (int source in sources)
                    {
                        if (source == SourceMap.FabricatedSource)
                        {
                            continue; // a gap-fill patch is not a culprit to escalate
                        }

                        culprits.Add(source);
                        attributed = true;
                    }

                    if (!attributed)
                    {
                        unattributed++;
                    }
                }

                if (unattributed > 0 && diagnostics != null)
                {
                    diagnostics.Add(
                        SolverStage.Heal,
                        DiagnosticCode.NakedLoop,
                        OcctDiagnosticSeverity.Warning,
                        string.Format("Naked loop: {0} of {1} edge(s) could not be attributed to a source panel.", unattributed, edgeCount),
                        point3Ds: points);
                }
            }

            return culprits.ToList();
        }

        private static Point3D Midpoint(Point3D a, Point3D b)
        {
            if (a == null)
            {
                return b;
            }

            if (b == null)
            {
                return a;
            }

            return new Point3D((a.X + b.X) / 2, (a.Y + b.Y) / 2, (a.Z + b.Z) / 2);
        }

        /// <summary>
        /// Index of the resolved face that best owns <paramref name="point"/>: the coplanar face (near-zero
        /// plane distance) whose boundary contains the point, else the coplanar face nearest in plane
        /// distance, else -1. The -1 owner fallback for a naked edge with no native owner index.
        /// </summary>
        private static int NearestCoplanarFaceIndex(Point3D point, List<Face3D> faces)
        {
            if (point == null || faces == null || faces.Count == 0)
            {
                return -1;
            }

            // Track the best containing face and, separately, the nearest-plane fallback; a containing
            // face always wins when one exists (the edge midpoint lies on its owning face's plane).
            int bestInside = -1;
            double bestInsideDistance = double.MaxValue;
            int bestAny = -1;
            double bestAnyDistance = double.MaxValue;

            for (int k = 0; k < faces.Count; k++)
            {
                Plane plane = faces[k]?.GetPlane();
                if (plane == null)
                {
                    continue;
                }

                double planeDistance = System.Math.Abs(plane.Distance(point));
                if (planeDistance < bestAnyDistance)
                {
                    bestAnyDistance = planeDistance;
                    bestAny = k;
                }

                Geometry.Planar.Face2D face2D = plane.Convert(faces[k]);
                Geometry.Planar.Point2D point2D = plane.Convert(point);
                bool inside = face2D != null && point2D != null
                    && (Geometry.Planar.Query.Inside(face2D, point2D, 0.01) || face2D.On(point2D, 0.01));

                if (inside && planeDistance < bestInsideDistance)
                {
                    bestInsideDistance = planeDistance;
                    bestInside = k;
                }
            }

            return bestInside >= 0 ? bestInside : bestAny;
        }
    }
}
