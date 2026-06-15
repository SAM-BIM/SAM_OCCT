// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Native
{
    /// <summary>
    /// Pure-managed watertightness check used when the native cell builder fails
    /// to form a volume (status 40). It welds every loop edge of the supplied
    /// faces at the build tolerance and counts how many faces use each edge: a
    /// closed manifold uses every edge an even number of times, so edges used an
    /// odd number of times are <b>naked</b> (open) and mark exactly where the
    /// shell fails to close. This is the managed equivalent of
    /// <c>ShapeAnalysis_FreeBounds</c> and, unlike a native diagnostic, runs
    /// without rebuilding <c>SAM.Occt.Native.dll</c>.
    /// </summary>
    internal static class OcctOpenShellAnalysis
    {
        /// <summary>
        /// Maps a non-zero <c>sam_occt_build_cell_complex</c> status to a plain
        /// explanation so the failure code is not opaque.
        /// </summary>
        public static string DescribeBuildStatus(int status)
        {
            switch (status)
            {
                case 10: return "null argument passed to the native builder";
                case 11: return "non-positive point/loop/face count passed to the native builder";
                case 20: return "no OCCT faces could be built from the supplied loops";
                case 30: return "OCCT MakerVolume reported errors while building the volume";
                case 40: return "OCCT MakerVolume produced no closed solid - the supplied faces do not bound a watertight volume";
                case 99: return "an unexpected native exception was thrown";
                default: return "unrecognised native status";
            }
        }

        /// <summary>
        /// Maps a non-zero <c>sam_occt_sew_faces</c> / <c>sam_occt_shape_sew</c>
        /// status (issue #37) to a plain explanation so the failure code is not
        /// opaque.
        /// </summary>
        public static string DescribeSewStatus(int status)
        {
            switch (status)
            {
                case 10: return "null argument passed to the native sew-and-heal entry point";
                case 11: return "non-positive point/loop/face count passed to the native sew-and-heal entry point";
                case 20: return "no OCCT faces could be built from the supplied loops";
                case 30: return "OCCT sewing/healing failed to produce a shell - the faces may be too far apart to join at the sewing tolerance (try a larger SewingTolerance)";
                case 40: return "OCCT sew-and-heal produced no closed shell - the faces do not bound a watertight volume even after healing";
                case 50: return "the OCCT shape handle was invalid";
                case 99: return "an unexpected native exception was thrown";
                default: return "unrecognised native status";
            }
        }

        /// <summary>
        /// Analyses the supplied faces for naked (open) edges and adds a
        /// diagnostic describing how many were found, their total length, and a
        /// representative location, so an open shell self-reports where the hole
        /// is. Safe to call with any face set; logs a clean result when the faces
        /// are watertight (which points the investigation away from the geometry).
        /// </summary>
        public static void Report(IEnumerable<Face3D> face3Ds, OcctBuildOptions options, OcctCellComplexResult result)
        {
            if (result == null)
            {
                return;
            }

            List<Face3D> face3DList = face3Ds?.Where(x => x != null).ToList();
            if (face3DList == null || face3DList.Count == 0)
            {
                return;
            }

            // Weld vertices at the build distance tolerance so that genuinely
            // coincident loop corners collapse to one key, while real gaps
            // larger than the tolerance stay distinct and surface as naked edges.
            double snap = options != null && options.Tolerance > 0 ? options.Tolerance : 1e-6;

            Dictionary<string, int> edgeUseCounts = new Dictionary<string, int>();
            Dictionary<string, Segment3D> edgeSamples = new Dictionary<string, Segment3D>();

            foreach (Face3D face3D in face3DList)
            {
                foreach (List<Point3D> loop in GetLoops(face3D))
                {
                    AccumulateLoop(loop, snap, edgeUseCounts, edgeSamples);
                }
            }

            if (edgeUseCounts.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_OPEN_SHELL_ANALYSIS", "Watertightness check could not extract any edges from the supplied faces.");
                return;
            }

            List<Segment3D> nakedEdges = new List<Segment3D>();
            int nonManifoldEdges = 0;
            foreach (KeyValuePair<string, int> keyValuePair in edgeUseCounts)
            {
                if (keyValuePair.Value % 2 == 1)
                {
                    if (edgeSamples.TryGetValue(keyValuePair.Key, out Segment3D segment3D) && segment3D != null)
                    {
                        nakedEdges.Add(segment3D);
                    }
                }
                else if (keyValuePair.Value > 2)
                {
                    nonManifoldEdges++;
                }
            }

            if (nakedEdges.Count == 0)
            {
                result.AddDiagnostic(
                    OcctDiagnosticSeverity.Info,
                    "SAM_OCCT_OPEN_SHELL_ANALYSIS",
                    string.Format("Watertightness check found no naked edges across {0} face(s){1}; the faces appear closed, so OCCT failed for another reason (e.g. self-intersection or a sliver below tolerance).", face3DList.Count, nonManifoldEdges > 0 ? string.Format(" but {0} non-manifold edge(s)", nonManifoldEdges) : string.Empty));
                return;
            }

            double totalLength = nakedEdges.Sum(x => x.GetLength());
            Segment3D longest = nakedEdges.OrderByDescending(x => x.GetLength()).First();
            Point3D sample = longest.Mid();

            BoundingBox3D boundingBox3D = new BoundingBox3D(nakedEdges.SelectMany(x => new Point3D[] { x[0], x[1] }).ToArray());
            Point3D min = boundingBox3D?.Min;
            Point3D max = boundingBox3D?.Max;

            result.AddDiagnostic(
                OcctDiagnosticSeverity.Warning,
                "SAM_OCCT_OPEN_SHELL_ANALYSIS",
                string.Format(
                    "Watertightness check found {0} naked (open) edge(s) totalling {1:0.####} m across {2} face(s) - the shell is NOT closed, which is why OCCT could not build a volume. Open region spans X {3:0.###}..{4:0.###}, Y {5:0.###}..{6:0.###}, Z {7:0.###}..{8:0.###} m; longest gap {9:0.####} m near ({10:0.###}, {11:0.###}, {12:0.###}).{13}",
                    nakedEdges.Count,
                    totalLength,
                    face3DList.Count,
                    min == null ? double.NaN : min.X,
                    max == null ? double.NaN : max.X,
                    min == null ? double.NaN : min.Y,
                    max == null ? double.NaN : max.Y,
                    min == null ? double.NaN : min.Z,
                    max == null ? double.NaN : max.Z,
                    longest.GetLength(),
                    sample == null ? double.NaN : sample.X,
                    sample == null ? double.NaN : sample.Y,
                    sample == null ? double.NaN : sample.Z,
                    nonManifoldEdges > 0 ? string.Format(" Also found {0} non-manifold edge(s) (shared by more than two faces).", nonManifoldEdges) : string.Empty));
        }

        private static IEnumerable<List<Point3D>> GetLoops(Face3D face3D)
        {
            if (face3D == null)
            {
                yield break;
            }

            List<Point3D> external = GetLoopPoints(face3D.GetExternalEdge3D());
            if (external != null)
            {
                yield return external;
            }

            List<IClosedPlanar3D> internalEdges = face3D.GetInternalEdge3Ds();
            if (internalEdges != null)
            {
                foreach (IClosedPlanar3D internalEdge in internalEdges)
                {
                    List<Point3D> points = GetLoopPoints(internalEdge);
                    if (points != null)
                    {
                        yield return points;
                    }
                }
            }
        }

        private static List<Point3D> GetLoopPoints(IClosedPlanar3D closedPlanar3D)
        {
            List<Point3D> points = null;

            if (closedPlanar3D is ISegmentable3D)
            {
                points = ((ISegmentable3D)closedPlanar3D).GetPoints();
            }
            else if (closedPlanar3D is ICurvable3D)
            {
                List<ICurve3D> curves = ((ICurvable3D)closedPlanar3D).GetCurves();
                points = curves?.ConvertAll(x => x?.GetStart());
            }

            points?.RemoveAll(x => x == null);
            return points != null && points.Count >= 3 ? points : null;
        }

        private static void AccumulateLoop(List<Point3D> points, double snap, Dictionary<string, int> edgeUseCounts, Dictionary<string, Segment3D> edgeSamples)
        {
            if (points == null || points.Count < 3)
            {
                return;
            }

            // Drop a duplicated closing point so the wrap-around edge is counted once.
            if (points.Count > 1 && points[0].Distance(points[points.Count - 1]) <= snap)
            {
                points = points.Take(points.Count - 1).ToList();
                if (points.Count < 3)
                {
                    return;
                }
            }

            for (int i = 0; i < points.Count; i++)
            {
                Point3D start = points[i];
                Point3D end = points[(i + 1) % points.Count];

                string startKey = VertexKey(start, snap);
                string endKey = VertexKey(end, snap);
                if (startKey == endKey)
                {
                    // Degenerate (zero-length at the weld tolerance) edge.
                    continue;
                }

                string edgeKey = string.CompareOrdinal(startKey, endKey) <= 0 ? startKey + "|" + endKey : endKey + "|" + startKey;

                if (edgeUseCounts.TryGetValue(edgeKey, out int count))
                {
                    edgeUseCounts[edgeKey] = count + 1;
                }
                else
                {
                    edgeUseCounts[edgeKey] = 1;
                    edgeSamples[edgeKey] = new Segment3D(start, end);
                }
            }
        }

        private static string VertexKey(Point3D point3D, double snap)
        {
            long x = (long)System.Math.Round(point3D.X / snap);
            long y = (long)System.Math.Round(point3D.Y / snap);
            long z = (long)System.Math.Round(point3D.Z / snap);
            return x + "_" + y + "_" + z;
        }
    }
}
