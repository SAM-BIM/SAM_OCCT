// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Closes the residual holes left in the resolved panel set - the free (naked) boundary loops where
    /// the model is not watertight - by building a new <see cref="Face3D"/> over each loop. Those faces
    /// become air panels. Free edges are found managed-side (boundary edges used by a single face) and the
    /// loops are kept only when they sit near a native naked-edge anchor, so true exterior boundaries are
    /// never mistaken for holes.
    /// </summary>
    public static class GapFill
    {
        public static List<Face3D> NakedLoopFace3Ds(IEnumerable<Face3D> face3Ds, IEnumerable<Point3D> nakedAnchors, double tolerance)
        {
            List<Face3D> result = new List<Face3D>();
            List<Face3D> faces = face3Ds?.Where(f => f != null).ToList();
            if (faces == null || faces.Count == 0)
            {
                return result;
            }

            // 1. Collect every boundary segment of every face.
            List<Segment3D> segments = new List<Segment3D>();
            foreach (Face3D face3D in faces)
            {
                AddLoop(face3D.GetExternalEdge3D() as ISegmentable3D, segments);
                List<IClosedPlanar3D> internals = face3D.GetInternalEdge3Ds();
                if (internals != null)
                {
                    foreach (IClosedPlanar3D internalEdge3D in internals)
                    {
                        AddLoop(internalEdge3D as ISegmentable3D, segments);
                    }
                }
            }

            // 2. Cluster coincident endpoints (within tolerance) into single vertex nodes, so two points
            //    that straddle a grid-rounding boundary are one node, not two (the old PointKey split them).
            List<Segment3D> longSegments = segments.Where(s => s.GetLength() > tolerance).ToList();
            VertexClusters clusters = new VertexClusters(tolerance);
            foreach (Segment3D segment3D in longSegments)
            {
                clusters.NodeId(segment3D.GetStart());
                clusters.NodeId(segment3D.GetEnd());
            }

            // An edge shared by 2 faces is interior; an edge used once is free (naked). Key by the node-id pair.
            Dictionary<long, int> count = new Dictionary<long, int>();
            Dictionary<long, Segment3D> representative = new Dictionary<long, Segment3D>();
            foreach (Segment3D segment3D in longSegments)
            {
                long key = EdgeKey(clusters.NodeId(segment3D.GetStart()), clusters.NodeId(segment3D.GetEnd()));
                count[key] = count.TryGetValue(key, out int c) ? c + 1 : 1;
                if (!representative.ContainsKey(key))
                {
                    representative[key] = segment3D;
                }
            }

            List<Segment3D> naked = count.Where(kv => kv.Value == 1).Select(kv => representative[kv.Key]).ToList();
            if (naked.Count < 3)
            {
                return result;
            }

            // 3. Walk the free edges into closed loops (least-turn at junctions).
            List<List<Point3D>> loops = AssembleLoops(naked, clusters);

            // 4. Keep only loops anchored to a native naked point, then build a face over each.
            List<Point3D> anchors = nakedAnchors?.Where(p => p != null).ToList() ?? new List<Point3D>();
            double anchorTolerance = 0.5; // a loop counts as a real hole if any vertex is within 0.5 m of a naked point
            foreach (List<Point3D> loop in loops)
            {
                if (loop.Count < 3)
                {
                    continue;
                }

                if (anchors.Count > 0 && !loop.Any(p => anchors.Any(a => p.Distance(a) <= anchorTolerance)))
                {
                    continue; // a true exterior boundary, not a hole
                }

                result.AddRange(BuildPatch(loop, tolerance));
            }

            return result;
        }

        /// <summary>
        /// Builds the patch face(s) over one closed naked loop. A planar loop yields a single
        /// <see cref="Face3D"/> (as before). A non-planar loop - the dominant floor/wall slot case, where a
        /// single planar polygon is invalid and would leave the hole open - is fan-triangulated from its
        /// centroid into planar triangles, so the warped perimeter is still closed.
        /// </summary>
        private static List<Face3D> BuildPatch(List<Point3D> loop, double tolerance)
        {
            List<Face3D> result = new List<Face3D>();
            if (loop == null || loop.Count < 3)
            {
                return result;
            }

            if (IsPlanar(loop, tolerance))
            {
                Face3D face3D = Geometry.Spatial.Create.Face3D(new Polygon3D(loop));
                if (face3D != null && face3D.IsValid())
                {
                    result.Add(face3D);
                    return result;
                }
                // planar build failed (e.g. self-touching loop) - fall through to triangulation
            }

            // Non-planar (or degenerate-planar) loop: fan-triangulate from the centroid. Each triangle is
            // planar by definition, so the hole is closed even when no single plane spans the loop.
            Point3D centroid = Centroid(loop);
            if (centroid == null)
            {
                return result;
            }

            for (int i = 0; i < loop.Count; i++)
            {
                Point3D a = loop[i];
                Point3D b = loop[(i + 1) % loop.Count];
                Triangle3D triangle3D = new Triangle3D(centroid, a, b);
                Face3D face3D = new Face3D(triangle3D);
                if (face3D != null && face3D.IsValid() && face3D.GetArea() > tolerance)
                {
                    result.Add(face3D);
                }
            }

            return result;
        }

        /// <summary>True when every loop point lies within <paramref name="tolerance"/> of the plane through
        /// the loop's first non-degenerate corner triple. Collinear/degenerate loops are reported non-planar
        /// so the triangulation fallback handles them.</summary>
        private static bool IsPlanar(List<Point3D> loop, double tolerance)
        {
            Point3D origin = loop[0];
            Vector3D normal = null;
            for (int i = 1; i < loop.Count - 1 && normal == null; i++)
            {
                Vector3D cross = new Vector3D(origin, loop[i]).CrossProduct(new Vector3D(origin, loop[i + 1]));
                if (cross.Length > tolerance)
                {
                    normal = cross.Unit;
                }
            }

            if (normal == null)
            {
                return false;
            }

            foreach (Point3D point3D in loop)
            {
                if (System.Math.Abs(new Vector3D(origin, point3D).DotProduct(normal)) > tolerance)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The arithmetic mean of the loop's points (its fan apex), or null for an empty loop.</summary>
        private static Point3D Centroid(List<Point3D> loop)
        {
            if (loop == null || loop.Count == 0)
            {
                return null;
            }

            double x = 0, y = 0, z = 0;
            foreach (Point3D point3D in loop)
            {
                x += point3D.X;
                y += point3D.Y;
                z += point3D.Z;
            }

            return new Point3D(x / loop.Count, y / loop.Count, z / loop.Count);
        }

        private static void AddLoop(ISegmentable3D loop, List<Segment3D> segments)
        {
            List<Point3D> point3Ds = loop?.GetPoints();
            if (point3Ds == null || point3Ds.Count < 2)
            {
                return;
            }

            for (int i = 0; i < point3Ds.Count; i++)
            {
                segments.Add(new Segment3D(point3Ds[i], point3Ds[(i + 1) % point3Ds.Count]));
            }
        }

        /// <summary>Packs an unordered pair of vertex-node ids into one key (order-independent).</summary>
        private static long EdgeKey(int a, int b)
        {
            int lo = System.Math.Min(a, b);
            int hi = System.Math.Max(a, b);
            return ((long)lo << 32) | (uint)hi;
        }

        /// <summary>
        /// Walks the free edges into closed loops. Vertices are identified by cluster node id (coincident
        /// points are one node). At an ordinary degree-2 vertex the single continuation is followed; at a
        /// junction (more than one unused edge) the next edge is the most-clockwise one about the loop's
        /// reference normal - the tightest face turn - which is deterministic and hugs the hole boundary,
        /// unlike the old "first unused edge" greedy pick.
        /// </summary>
        private static List<List<Point3D>> AssembleLoops(List<Segment3D> edges, VertexClusters clusters)
        {
            List<List<Point3D>> loops = new List<List<Point3D>>();

            int[] startNode = new int[edges.Count];
            int[] endNode = new int[edges.Count];
            Dictionary<int, List<int>> adjacency = new Dictionary<int, List<int>>();
            for (int i = 0; i < edges.Count; i++)
            {
                startNode[i] = clusters.NodeId(edges[i].GetStart());
                endNode[i] = clusters.NodeId(edges[i].GetEnd());
                AddIncident(adjacency, startNode[i], i);
                AddIncident(adjacency, endNode[i], i);
            }

            bool[] used = new bool[edges.Count];
            for (int i = 0; i < edges.Count; i++)
            {
                if (used[i])
                {
                    continue;
                }

                List<Point3D> loop = new List<Point3D>();
                used[i] = true;
                int startNodeId = startNode[i];
                int currentNode = endNode[i];
                loop.Add(clusters.Location(startNodeId));

                Vector3D incoming = new Vector3D(clusters.Location(startNodeId), clusters.Location(currentNode));
                Vector3D refNormal = null; // seeded from the first non-degenerate turn; null => first-unused fallback

                bool closed = false;
                int guard = 0;
                while (guard++ <= edges.Count)
                {
                    loop.Add(clusters.Location(currentNode));

                    if (currentNode == startNodeId)
                    {
                        closed = true;
                        break;
                    }

                    int next = ChooseNext(currentNode, incoming, refNormal, adjacency, used, startNode, endNode, clusters);
                    if (next < 0)
                    {
                        break;
                    }

                    used[next] = true;
                    int otherNode = startNode[next] == currentNode ? endNode[next] : startNode[next];
                    Vector3D outgoing = new Vector3D(clusters.Location(currentNode), clusters.Location(otherNode));

                    if (refNormal == null)
                    {
                        Vector3D cross = incoming.CrossProduct(outgoing);
                        if (cross.Length > 1e-9)
                        {
                            refNormal = cross.Unit;
                        }
                    }

                    incoming = outgoing;
                    currentNode = otherNode;
                }

                if (closed && loop.Count >= 3)
                {
                    loops.Add(loop);
                }
            }

            return loops;
        }

        /// <summary>
        /// Chooses the next unused edge leaving <paramref name="currentNode"/>. With one unused candidate (or
        /// no reference normal yet) the single continuation is returned; at a junction the most-clockwise edge
        /// about <paramref name="refNormal"/> - the tightest turn from the reversed incoming direction - is
        /// chosen, skipping the immediate back-edge. Returns -1 when the walk dead-ends.
        /// </summary>
        private static int ChooseNext(int currentNode, Vector3D incoming, Vector3D refNormal,
            Dictionary<int, List<int>> adjacency, bool[] used, int[] startNode, int[] endNode, VertexClusters clusters)
        {
            if (!adjacency.TryGetValue(currentNode, out List<int> candidates))
            {
                return -1;
            }

            int firstUnused = -1;
            int unusedCount = 0;
            foreach (int e in candidates)
            {
                if (used[e])
                {
                    continue;
                }
                unusedCount++;
                if (firstUnused < 0)
                {
                    firstUnused = e;
                }
            }

            if (unusedCount == 0)
            {
                return -1;
            }
            if (unusedCount == 1 || refNormal == null)
            {
                return firstUnused; // degree-2 chain, or no plane yet: keep it simple
            }

            // Junction: most-clockwise outgoing edge about the reference normal, measured from the reversed
            // incoming direction. Hugs the loop boundary deterministically.
            Vector3D from = incoming.GetNegated();
            int best = firstUnused;
            double bestAngle = double.MaxValue;
            foreach (int e in candidates)
            {
                if (used[e])
                {
                    continue;
                }

                int otherNode = startNode[e] == currentNode ? endNode[e] : startNode[e];
                Vector3D to = new Vector3D(clusters.Location(currentNode), clusters.Location(otherNode));
                double angle = SignedAngle(from, to, refNormal);
                if (angle > 1e-9 && angle < bestAngle) // skip the degenerate back-edge (angle ~ 0)
                {
                    bestAngle = angle;
                    best = e;
                }
            }

            return best;
        }

        /// <summary>The angle from <paramref name="from"/> to <paramref name="to"/> measured counter-clockwise
        /// about <paramref name="normal"/>, in [0, 2π). Degenerate inputs return 0.</summary>
        private static double SignedAngle(Vector3D from, Vector3D to, Vector3D normal)
        {
            if (from.Length <= 1e-9 || to.Length <= 1e-9)
            {
                return 0;
            }

            Vector3D f = from.Unit;
            Vector3D t = to.Unit;
            double cosA = System.Math.Max(-1.0, System.Math.Min(1.0, f.DotProduct(t)));
            double angle = System.Math.Acos(cosA); // [0, π]
            if (f.CrossProduct(t).DotProduct(normal) < 0)
            {
                angle = 2 * System.Math.PI - angle; // [0, 2π)
            }

            return angle;
        }

        private static void AddIncident(Dictionary<int, List<int>> adjacency, int node, int edge)
        {
            if (!adjacency.TryGetValue(node, out List<int> list))
            {
                adjacency[node] = list = new List<int>();
            }
            list.Add(edge);
        }

        /// <summary>
        /// Merges naked-edge endpoints that lie within <c>tolerance</c> of one another into single vertex
        /// nodes, each with an integer id. Greedy nearest clustering (first representative within tolerance
        /// wins), so coincident corners shared by several edges resolve to one node - the connectivity the
        /// loop walk relies on - without the bucket-boundary splits of a grid-rounded key.
        /// </summary>
        private sealed class VertexClusters
        {
            private readonly List<Point3D> representatives = new List<Point3D>();
            private readonly double tolerance;

            public VertexClusters(double tolerance)
            {
                this.tolerance = tolerance <= 0 ? 1e-9 : tolerance;
            }

            public int NodeId(Point3D point3D)
            {
                for (int i = 0; i < representatives.Count; i++)
                {
                    if (representatives[i].Distance(point3D) <= tolerance)
                    {
                        return i;
                    }
                }

                representatives.Add(point3D);
                return representatives.Count - 1;
            }

            public Point3D Location(int id)
            {
                return representatives[id];
            }
        }
    }
}
