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

            // 2. An edge shared by 2 faces is interior; an edge used once is free (naked).
            Dictionary<string, int> count = new Dictionary<string, int>();
            Dictionary<string, Segment3D> representative = new Dictionary<string, Segment3D>();
            foreach (Segment3D segment3D in segments)
            {
                if (segment3D.GetLength() <= tolerance)
                {
                    continue;
                }

                string key = EdgeKey(segment3D, tolerance);
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

            // 3. Walk the free edges into closed loops.
            List<List<Point3D>> loops = AssembleLoops(naked, tolerance);

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

                Face3D face3D = Geometry.Spatial.Create.Face3D(new Polygon3D(loop));
                if (face3D != null && face3D.IsValid())
                {
                    result.Add(face3D);
                }
            }

            return result;
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

        private static string PointKey(Point3D point3D, double tolerance)
        {
            return string.Format("{0}_{1}_{2}",
                System.Math.Round(point3D.X / tolerance),
                System.Math.Round(point3D.Y / tolerance),
                System.Math.Round(point3D.Z / tolerance));
        }

        private static string EdgeKey(Segment3D segment3D, double tolerance)
        {
            string a = PointKey(segment3D.GetStart(), tolerance);
            string b = PointKey(segment3D.GetEnd(), tolerance);
            return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
        }

        private static List<List<Point3D>> AssembleLoops(List<Segment3D> edges, double tolerance)
        {
            List<List<Point3D>> loops = new List<List<Point3D>>();

            Dictionary<string, List<int>> adjacency = new Dictionary<string, List<int>>();
            for (int i = 0; i < edges.Count; i++)
            {
                foreach (string key in new[] { PointKey(edges[i].GetStart(), tolerance), PointKey(edges[i].GetEnd(), tolerance) })
                {
                    if (!adjacency.TryGetValue(key, out List<int> list))
                    {
                        adjacency[key] = list = new List<int>();
                    }
                    list.Add(i);
                }
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
                Point3D start = edges[i].GetStart();
                Point3D current = edges[i].GetEnd();
                string startKey = PointKey(start, tolerance);
                loop.Add(start);

                bool closed = false;
                int guard = 0;
                while (guard++ <= edges.Count)
                {
                    loop.Add(current);
                    string currentKey = PointKey(current, tolerance);

                    int next = -1;
                    if (adjacency.TryGetValue(currentKey, out List<int> candidates))
                    {
                        foreach (int e in candidates)
                        {
                            if (!used[e])
                            {
                                next = e;
                                break;
                            }
                        }
                    }

                    if (next < 0)
                    {
                        break;
                    }

                    used[next] = true;
                    Point3D nextStart = edges[next].GetStart();
                    Point3D nextEnd = edges[next].GetEnd();
                    current = PointKey(nextStart, tolerance) == currentKey ? nextEnd : nextStart;

                    if (PointKey(current, tolerance) == startKey)
                    {
                        closed = true;
                        break;
                    }
                }

                if (closed && loop.Count >= 3)
                {
                    loops.Add(loop);
                }
            }

            return loops;
        }
    }
}
