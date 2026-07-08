// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
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
    /// <para>
    /// Phase 5b adds <see cref="FromNakedWires"/>: the preferred entry that consumes the native ordered
    /// naked wires (ABI v4, <see cref="OcctNakedWire"/>) directly instead of re-walking free edges
    /// managed-side. The legacy <see cref="NakedLoopFace3Ds"/> walk is retained ONLY as the fallback for a
    /// pre-v4 native (wires empty but naked points present), and every loop outcome - patched, fan-patched,
    /// open, rejected - carries a diagnostic (never a silent fabrication or drop). See
    /// docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §G.
    /// </para>
    /// </summary>
    public static class GapFill
    {
        /// <summary>Air-panel area floor (m2): a loop enclosing less than this is a float-noise sliver, not a gap.</summary>
        public const double MinPatchArea = 1e-4;

        /// <summary>What became of one naked wire in <see cref="FromNakedWires"/> - the per-loop evidence
        /// behind the whole-set outcome (docs/P5 review §G/§I). Exactly one of the boolean states holds.</summary>
        public sealed class LoopOutcome
        {
            /// <summary>The wire closed on itself (a candidate for patching).</summary>
            public bool IsClosed { get; set; }

            /// <summary>Closed within the planarity band and patched with a single planar face.</summary>
            public bool PlanarPatched { get; set; }

            /// <summary>Non-planar (or planar build unsafe): closed with centroid-fan triangles (tagged, inspect).</summary>
            public bool FanPatched { get; set; }

            /// <summary>No patch produced (open wire, or a rejected degenerate/self-intersecting/tiny loop).</summary>
            public bool Residual { get; set; }

            /// <summary>Human-readable reason, mirrored into the emitted diagnostic.</summary>
            public string Reason { get; set; }

            /// <summary>How many patch faces this loop contributed (1 for planar, N for a fan).</summary>
            public int PatchCount { get; set; }
        }

        /// <summary>The patches <see cref="FromNakedWires"/> built plus a per-loop outcome record.</summary>
        public sealed class GapFillResult
        {
            public List<Face3D> Patches { get; } = new List<Face3D>();

            public List<LoopOutcome> LoopOutcomes { get; } = new List<LoopOutcome>();
        }

        /// <summary>
        /// Builds patch faces over the native ordered naked <paramref name="wires"/> (ABI v4) - the Phase 5b
        /// primary gap-fill path (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §G). Per closed wire
        /// (>= 3 points):
        /// <list type="bullet">
        /// <item><b>Planar</b> (max deviation from the best-fit plane &lt;= max(0.01 m, 10x tolerance)) and the
        /// single polygon is valid, non-self-intersecting and above <see cref="MinPatchArea"/> -&gt; one planar
        /// patch (<see cref="DiagnosticCode.NakedLoop"/> Info).</item>
        /// <item><b>Non-planar or planar build unsafe</b> -&gt; centroid-fan triangles, tagged
        /// <see cref="DiagnosticCode.NakedLoop"/> Warning ("fan-patched - inspect") - a last-resort close,
        /// never a silent success.</item>
        /// <item><b>Tiny / degenerate / self-intersecting with no safe fan</b> -&gt; rejected, no patch,
        /// <see cref="DiagnosticCode.NakedLoop"/> Warning.</item>
        /// </list>
        /// Open wires produce no patch and a residual <see cref="DiagnosticCode.NakedLoop"/> Warning. Nested
        /// (coplanar-containing) closed loops are NOT specially handled in P5 - a Warning is emitted (documented
        /// scope cut). The imprint of the patches into the resolved set happens in the consolidation rebuild
        /// (<c>Panel3DSnapSolver.FinalizeAndValidate</c>), not here.
        /// </summary>
        public static GapFillResult FromNakedWires(IEnumerable<OcctNakedWire> wires, SolverDiagnostics diagnostics, double tolerance)
        {
            GapFillResult result = new GapFillResult();
            List<OcctNakedWire> wireList = wires?.Where(x => x != null).ToList() ?? new List<OcctNakedWire>();
            if (wireList.Count == 0)
            {
                return result;
            }

            double planarTolerance = System.Math.Max(0.01, 10 * System.Math.Max(tolerance, 0));

            foreach (OcctNakedWire wire in wireList)
            {
                List<Point3D> points = wire.Point3Ds?.Where(x => x != null).ToList() ?? new List<Point3D>();
                LoopOutcome outcome = new LoopOutcome { IsClosed = wire.IsClosed };

                // Open or degenerate wire: no patch, residual (Phase 8 will display the polyline).
                if (!wire.IsClosed || points.Count < 3)
                {
                    outcome.Residual = true;
                    outcome.Reason = "open/degenerate naked wire; no patch (residual)";
                    Emit(diagnostics, OcctDiagnosticSeverity.Warning, outcome.Reason, points);
                    result.LoopOutcomes.Add(outcome);
                    continue;
                }

                Plane plane = PlanarFitPlane(points, planarTolerance);
                if (plane != null)
                {
                    Face3D planar = TryBuildPlanarPatch(points, plane, tolerance, out string rejectReason);
                    if (planar != null)
                    {
                        result.Patches.Add(planar);
                        outcome.PlanarPatched = true;
                        outcome.PatchCount = 1;
                        outcome.Reason = "closed planar loop patched";
                        Emit(diagnostics, OcctDiagnosticSeverity.Info, outcome.Reason, points);
                        result.LoopOutcomes.Add(outcome);
                        continue;
                    }

                    // Planar but unsafe (self-intersecting / tiny / invalid): if it is a tiny area, reject
                    // outright rather than fan a sliver into noise triangles.
                    if (rejectReason != null && rejectReason.Contains("tiny"))
                    {
                        outcome.Residual = true;
                        outcome.Reason = "planar loop rejected (" + rejectReason + ")";
                        Emit(diagnostics, OcctDiagnosticSeverity.Warning, outcome.Reason, points);
                        result.LoopOutcomes.Add(outcome);
                        continue;
                    }
                }

                // Non-planar, or planar build failed for a non-tiny reason: centroid-fan fallback (tagged).
                List<Face3D> fan = FanTriangles(points, tolerance);
                if (fan.Count != 0)
                {
                    result.Patches.AddRange(fan);
                    outcome.FanPatched = true;
                    outcome.PatchCount = fan.Count;
                    outcome.Reason = "non-planar loop; fan-patched - inspect";
                    Emit(diagnostics, OcctDiagnosticSeverity.Warning, outcome.Reason, points);
                    result.LoopOutcomes.Add(outcome);
                    continue;
                }

                outcome.Residual = true;
                outcome.Reason = "closed loop could not be patched (degenerate/self-intersecting); residual";
                Emit(diagnostics, OcctDiagnosticSeverity.Warning, outcome.Reason, points);
                result.LoopOutcomes.Add(outcome);
            }

            WarnOnNestedLoops(wireList, diagnostics);

            return result;
        }

        /// <summary>Emits a <see cref="DiagnosticCode.NakedLoop"/> diagnostic (Heal stage) for one loop outcome.</summary>
        private static void Emit(SolverDiagnostics diagnostics, OcctDiagnosticSeverity severity, string message, IEnumerable<Point3D> point3Ds)
        {
            diagnostics?.Add(SolverStage.Heal, DiagnosticCode.NakedLoop, severity, "GapFill: " + message, point3Ds);
        }

        /// <summary>
        /// The best-fit plane of a closed loop when every vertex lies within <paramref name="planarTolerance"/>
        /// of the plane through its first non-degenerate corner triple; null when the loop is non-planar or
        /// too degenerate to define a plane (so the caller falls back to the fan).
        /// </summary>
        private static Plane PlanarFitPlane(List<Point3D> loop, double planarTolerance)
        {
            Point3D origin = loop[0];
            Vector3D normal = null;
            // Seed the plane normal from the first non-degenerate corner triple. The cross-product magnitude
            // scales with triangle AREA, so it is thresholded against a tiny epsilon (a collinear/degenerate
            // triple has ~0 area) - NOT against planarTolerance, which is the point-to-plane DEVIATION bound
            // below. (Using planarTolerance here mis-read a small-but-planar loop, e.g. a tiny square, as
            // non-planar.)
            for (int i = 1; i < loop.Count - 1 && normal == null; i++)
            {
                Vector3D cross = new Vector3D(origin, loop[i]).CrossProduct(new Vector3D(origin, loop[i + 1]));
                if (cross.Length > 1e-9)
                {
                    normal = cross.Unit;
                }
            }

            if (normal == null)
            {
                return null;
            }

            foreach (Point3D point3D in loop)
            {
                if (System.Math.Abs(new Vector3D(origin, point3D).DotProduct(normal)) > planarTolerance)
                {
                    return null;
                }
            }

            return new Plane(origin, normal);
        }

        /// <summary>
        /// Builds a single planar patch and gates it: valid Face3D, area &gt;= <see cref="MinPatchArea"/>, and a
        /// simple (non-self-intersecting) boundary. Returns null with a <paramref name="rejectReason"/> when a
        /// gate fails, so the caller can reject (tiny) or fan (self-intersecting/invalid).
        /// </summary>
        /// <remarks>
        /// The self-intersection gate is a direct boundary edge-crossing test in the loop's own plane, NOT
        /// <c>Query.SelfIntersectionFace3Ds</c>: that helper decomposes a face into sub-faces and returns the
        /// face itself for a clean polygon (it is not "empty when simple"), so it cannot serve as a boolean
        /// self-intersection gate here (design-review §G names it, but its contract does not match).
        /// </remarks>
        private static Face3D TryBuildPlanarPatch(List<Point3D> loop, Plane plane, double tolerance, out string rejectReason)
        {
            rejectReason = null;

            if (LoopSelfIntersects(loop, plane, System.Math.Max(tolerance, 1e-9)))
            {
                rejectReason = "self-intersecting boundary";
                return null;
            }

            Face3D face3D = Geometry.Spatial.Create.Face3D(new Polygon3D(loop));
            if (face3D == null || !face3D.IsValid())
            {
                rejectReason = "invalid planar face";
                return null;
            }

            if (face3D.GetArea() < MinPatchArea)
            {
                rejectReason = "tiny area (< " + MinPatchArea + " m2)";
                return null;
            }

            return face3D;
        }

        /// <summary>Centroid-fan triangulation of a loop into planar triangles (each valid, above the area floor).</summary>
        private static List<Face3D> FanTriangles(List<Point3D> loop, double tolerance)
        {
            List<Face3D> result = new List<Face3D>();
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
                if (face3D != null && face3D.IsValid() && face3D.GetArea() > System.Math.Max(tolerance, 1e-9))
                {
                    result.Add(face3D);
                }
            }

            return result;
        }

        /// <summary>
        /// True when two non-adjacent boundary edges of the loop cross in its plane - a self-intersecting
        /// (bow-tie) boundary a single planar face cannot represent. O(n2) over the small loop vertex set.
        /// </summary>
        private static bool LoopSelfIntersects(List<Point3D> loop, Plane plane, double tolerance)
        {
            int n = loop.Count;
            if (n < 4)
            {
                return false; // a triangle cannot self-intersect
            }

            List<Geometry.Planar.Point2D> pts = new List<Geometry.Planar.Point2D>(n);
            foreach (Point3D point3D in loop)
            {
                Geometry.Planar.Point2D point2D = plane.Convert(point3D);
                if (point2D == null)
                {
                    return false; // cannot test - do not falsely reject
                }

                pts.Add(point2D);
            }

            for (int i = 0; i < n; i++)
            {
                Geometry.Planar.Point2D a1 = pts[i];
                Geometry.Planar.Point2D a2 = pts[(i + 1) % n];
                for (int j = i + 1; j < n; j++)
                {
                    // Skip edges that share a vertex (adjacent, or the wrap-around closing edge).
                    if (j == i || (j + 1) % n == i || (i + 1) % n == j)
                    {
                        continue;
                    }

                    if (SegmentsProperlyIntersect(a1, a2, pts[j], pts[(j + 1) % n], tolerance))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Proper 2D segment-segment crossing test (endpoints touching does not count), via orientation signs.</summary>
        private static bool SegmentsProperlyIntersect(Geometry.Planar.Point2D p1, Geometry.Planar.Point2D p2, Geometry.Planar.Point2D p3, Geometry.Planar.Point2D p4, double tolerance)
        {
            double d1 = Cross(p3, p4, p1);
            double d2 = Cross(p3, p4, p2);
            double d3 = Cross(p1, p2, p3);
            double d4 = Cross(p1, p2, p4);

            // Strict opposite signs on both segments => a proper crossing in the interior of both.
            return ((d1 > tolerance && d2 < -tolerance) || (d1 < -tolerance && d2 > tolerance))
                && ((d3 > tolerance && d4 < -tolerance) || (d3 < -tolerance && d4 > tolerance));
        }

        /// <summary>2D cross product of (b - a) x (c - a) - the orientation of c about the directed line a-&gt;b.</summary>
        private static double Cross(Geometry.Planar.Point2D a, Geometry.Planar.Point2D b, Geometry.Planar.Point2D c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        }

        /// <summary>
        /// Warns when one closed wire is coplanar with and encloses another (a nested/hole loop) - not
        /// specially handled in P5 (documented scope cut), so it is surfaced rather than silently mis-patched.
        /// </summary>
        private static void WarnOnNestedLoops(List<OcctNakedWire> wires, SolverDiagnostics diagnostics)
        {
            if (diagnostics == null)
            {
                return;
            }

            List<List<Point3D>> closed = wires
                .Where(w => w.IsClosed && (w.Point3Ds?.Count ?? 0) >= 3)
                .Select(w => w.Point3Ds.ToList())
                .ToList();

            for (int i = 0; i < closed.Count; i++)
            {
                Plane planeI = PlanarFitPlane(closed[i], 0.01);
                if (planeI == null)
                {
                    continue;
                }

                BoundingBox3D boxI = new BoundingBox3D(closed[i]);
                for (int j = 0; j < closed.Count; j++)
                {
                    if (j == i)
                    {
                        continue;
                    }

                    // j coplanar with i and fully inside i's bbox => a candidate nested loop.
                    if (closed[j].All(p => System.Math.Abs(planeI.Distance(p)) <= 0.01)
                        && closed[j].All(p => Contains(boxI, p, 0.01)))
                    {
                        diagnostics.Add(SolverStage.Heal, DiagnosticCode.NakedLoop, OcctDiagnosticSeverity.Warning,
                            "GapFill: nested/hole loop detected (coplanar loop enclosed by another); not handled in P5 - inspect.",
                            closed[j]);
                    }
                }
            }
        }

        /// <summary>True when <paramref name="point3D"/> lies inside <paramref name="box"/> grown by <paramref name="tolerance"/>.</summary>
        private static bool Contains(BoundingBox3D box, Point3D point3D, double tolerance)
        {
            Point3D min = box.Min;
            Point3D max = box.Max;
            return point3D.X >= min.X - tolerance && point3D.X <= max.X + tolerance
                && point3D.Y >= min.Y - tolerance && point3D.Y <= max.Y + tolerance
                && point3D.Z >= min.Z - tolerance && point3D.Z <= max.Z + tolerance;
        }

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
