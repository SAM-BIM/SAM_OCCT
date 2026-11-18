// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Keeps a genuine wall bend that <see cref="Panel3DSnapSolver.Snap(List{SnappedPanel}, double, double, double, double, double, List{CleanRecord}, Vector3D)"/>'s
    /// wall-abut rule (b) would otherwise straighten by breaking a connection. Asked only for a pair that rule (b) alone admits (not the
    /// double-wall overlap rule (a)), and only when the move turns the candidate by more than the arc tolerance: a plane translation is
    /// never refused. The move is refused when one of the candidate's run-end boundary segments (<see cref="SnappedPanel.RunEndSegments"/>)
    /// has a finite stretch of contact with a third panel that is near-parallel to it (within the angle tolerance that admits a fully
    /// captured pair), and projecting that stretch onto the move's target plane would take it off the third panel's plane by more than
    /// the distance tolerance. A coplanar continuation or junction block is then left behind by the full displacement, whereas a
    /// perpendicular return only slides along itself and is not counted.
    /// <para>
    /// Contact is the exact clip of the end segment by the third panel's face (its holes and every disconnected piece respected), kept
    /// to the connected stretches of face material that lie on that panel's plane from end to end: the segment is broken where the
    /// face's outline crosses or touches it, each piece is classified with the face's tolerant <c>Inside</c> / <c>On</c> (rounding
    /// coordinates to a grid, as a polygon overlay does, would lose an edge-on-edge contact that is not axis-aligned in the plane's
    /// frame), and consecutive pieces on the face are judged as one stretch. A segment that passes through the plane meets it at one
    /// point, however small the angle: the stretch of it within the distance tolerance of the plane (2 tol / sin of the angle) is not a
    /// contact, wherever the outlines carry vertices. Each run end is one continuous straight edge however many collinear vertices it
    /// carries (<see cref="SnappedPanel.RunEndSegments"/>), so the answer does not depend on redundant vertices of either outline.
    /// Stretches are merged and anything no longer than the distance tolerance is dropped, so a corner touch is not a contact. No
    /// constant of its own: the solver's angle, arc-angle and distance tolerances and its model-up frame are used throughout.
    /// </para>
    /// </summary>
    internal static class BendVeto
    {
        /// <summary>
        /// True when moving <paramref name="candidate"/> onto <paramref name="target"/> (the plane the existing pair move would give it:
        /// <paramref name="backer"/>'s plane, or the equal-weight midplane) would rotate it and break a finite connection with a third,
        /// near-parallel panel of <paramref name="panels"/>.
        /// </summary>
        /// <param name="reason">The connection that would break; null when the move is not refused.</param>
        internal static bool Refuses(
            SnappedPanel candidate,
            SnappedPanel backer,
            Plane target,
            IEnumerable<SnappedPanel> panels,
            double toleranceAngle,
            double toleranceArcAngle,
            double toleranceDistance,
            Vector3D up,
            out string reason)
        {
            reason = null;
            if (candidate?.Plane == null || candidate.Face3D == null || backer?.Plane == null || target == null || panels == null)
            {
                return false;
            }

            // A rotation only: the target is parallel to the backer (its own plane or a translation of it along its normal).
            if (candidate.IsParallelWith(backer, toleranceArcAngle))
            {
                return false;
            }

            List<Segment3D> ends = candidate.RunEndSegments(up, toleranceDistance);
            if (ends.Count == 0)
            {
                return false;
            }

            foreach (SnappedPanel neighbour in panels)
            {
                if (neighbour?.Plane == null || neighbour.Face3D == null || ReferenceEquals(neighbour, candidate) || ReferenceEquals(neighbour, backer))
                {
                    continue;
                }

                if (!candidate.IsParallelWith(neighbour, toleranceAngle))
                {
                    continue;
                }

                foreach (Segment3D end in ends)
                {
                    foreach ((double From, double To) contact in Contact(end, neighbour.Face3D, toleranceDistance))
                    {
                        double displacement = System.Math.Max(
                            neighbour.Plane.Distance(target.Project(PointAt(end, contact.From))),
                            neighbour.Plane.Distance(target.Project(PointAt(end, contact.To))));
                        if (displacement > toleranceDistance)
                        {
                            reason = string.Format(
                                System.Globalization.CultureInfo.InvariantCulture,
                                "run end of panel {0} meets panel {1} over {2:0.###} m and would leave its plane by {3:0.####} m",
                                string.Join("+", candidate.SourceIndices),
                                string.Join("+", neighbour.SourceIndices),
                                contact.To - contact.From,
                                displacement);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// The stretches of <paramref name="segment3D"/>, as distances from its start, that lie on <paramref name="face3D"/>: inside or on
        /// the face (its holes excluded, each disconnected piece separately) and within <paramref name="toleranceDistance"/> of the face's
        /// plane along their whole length. Each connected stretch of face material along the segment is judged whole, so an outline
        /// vertex lying on the segment does not split it. A segment that crosses the plane (both ends of a stretch not within the
        /// tolerance) meets it at a point and gives no stretch, whatever the length of its part inside the tolerance band. Normalised
        /// (<see cref="PlanIntervals.Normalise"/>): stretches within the tolerance of each other are merged, and stretches no longer than
        /// it - a touch at a corner - are dropped.
        /// </summary>
        internal static List<(double From, double To)> Contact(Segment3D segment3D, Face3D face3D, double toleranceDistance)
        {
            List<(double From, double To)> result = new List<(double, double)>();
            Plane plane = face3D?.GetPlane();
            double length = segment3D?.GetLength() ?? 0;
            if (plane == null || length <= toleranceDistance || !BoxesMeet(segment3D, face3D.GetBoundingBox(), toleranceDistance))
            {
                return result;
            }

            // The signed distance to the plane is linear along the segment, so a piece lies on the plane (within the tolerance) from end
            // to end exactly when both of its ends do. Quick rejection: the part of the segment inside the tolerance band is no longer
            // than the tolerance, so no piece can be.
            double d0 = SignedDistance(plane, segment3D[0]);
            double d1 = SignedDistance(plane, segment3D[1]);
            double DistanceAt(double distance) => d0 + ((d1 - d0) * (distance / length));
            if (System.Math.Abs(d1 - d0) <= 1e-12)
            {
                if (System.Math.Abs(d0) > toleranceDistance)
                {
                    return result;
                }
            }
            else
            {
                double a = (-toleranceDistance - d0) / (d1 - d0) * length;
                double b = (toleranceDistance - d0) / (d1 - d0) * length;
                if (System.Math.Min(length, System.Math.Max(a, b)) - System.Math.Max(0, System.Math.Min(a, b)) <= toleranceDistance)
                {
                    return result;
                }
            }

            // The whole segment, projected onto the plane: a piece crossing the plane is judged from end to end, not clipped to the band.
            Geometry.Planar.Point2D start = plane.Convert(segment3D[0]);
            Geometry.Planar.Point2D end = plane.Convert(segment3D[1]);
            Geometry.Planar.Face2D face2D = plane.Convert(face3D);
            if (start == null || end == null || face2D == null)
            {
                return result;
            }

            double dx = end.X - start.X, dy = end.Y - start.Y, lengthSquared = (dx * dx) + (dy * dy);
            if (lengthSquared <= toleranceDistance * toleranceDistance)
            {
                return result;
            }

            // Break the segment where it meets the outline of the face: where a boundary edge crosses it, and where a boundary vertex
            // lies on it (so an edge running along the segment, the usual end-to-end contact, ends where that edge ends). Between two
            // consecutive breaks the segment is on one side of the outline, decided at the midpoint with the tolerant Inside / On.
            // Consecutive pieces inside or on the face are one connected stretch of its material, however many outline vertices lie on
            // the segment; a stretch counts only if it lies on the plane from end to end.
            List<double> breaks = new List<double> { 0, 1 };
            void AddBreak(double t)
            {
                if (t > 0 && t < 1)
                {
                    breaks.Add(t);
                }
            }

            foreach (Geometry.Planar.IClosed2D loop in face2D.Edge2Ds ?? new List<Geometry.Planar.IClosed2D>())
            {
                foreach (Geometry.Planar.Segment2D edge in (loop as Geometry.Planar.ISegmentable2D)?.GetSegments() ?? new List<Geometry.Planar.Segment2D>())
                {
                    Geometry.Planar.Point2D p = edge?[0], q = edge?[1];
                    if (p == null || q == null)
                    {
                        continue;
                    }

                    foreach (Geometry.Planar.Point2D vertex in new[] { p, q })
                    {
                        double vx = vertex.X - start.X, vy = vertex.Y - start.Y;
                        if (System.Math.Abs((vx * dy) - (vy * dx)) <= toleranceDistance * System.Math.Sqrt(lengthSquared))
                        {
                            AddBreak(((vx * dx) + (vy * dy)) / lengthSquared);
                        }
                    }

                    double ex = q.X - p.X, ey = q.Y - p.Y, denominator = (dx * ey) - (dy * ex);
                    if (System.Math.Abs(denominator) > 1e-12)
                    {
                        double px = p.X - start.X, py = p.Y - start.Y;
                        double t = ((px * ey) - (py * ex)) / denominator, s = ((px * dy) - (py * dx)) / denominator;
                        if (s >= 0 && s <= 1)
                        {
                            AddBreak(t);
                        }
                    }
                }
            }

            breaks.Sort();
            List<(double From, double To)> stretches = new List<(double, double)>();
            double stretchFrom = double.NaN, stretchTo = double.NaN;
            void Close()
            {
                if (!double.IsNaN(stretchFrom)
                    && System.Math.Abs(DistanceAt(stretchFrom)) <= toleranceDistance && System.Math.Abs(DistanceAt(stretchTo)) <= toleranceDistance)
                {
                    stretches.Add((stretchFrom, stretchTo));
                }

                stretchFrom = stretchTo = double.NaN;
            }

            for (int i = 0; i < breaks.Count - 1; i++)
            {
                if (breaks[i + 1] <= breaks[i])
                {
                    continue;
                }

                double t = (breaks[i] + breaks[i + 1]) / 2.0;
                Geometry.Planar.Point2D mid = new Geometry.Planar.Point2D(start.X + (t * dx), start.Y + (t * dy));
                if (face2D.On(mid, toleranceDistance) || face2D.Inside(mid, toleranceDistance))
                {
                    if (double.IsNaN(stretchFrom))
                    {
                        stretchFrom = breaks[i] * length;
                    }

                    stretchTo = breaks[i + 1] * length;
                }
                else
                {
                    Close();
                }
            }

            Close();
            return PlanIntervals.Normalise(stretches, toleranceDistance);
        }

        private static Point3D PointAt(Segment3D segment3D, double distance)
        {
            Point3D a = segment3D[0], b = segment3D[1];
            double t = distance / segment3D.GetLength();
            return new Point3D(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t), a.Z + ((b.Z - a.Z) * t));
        }

        private static double SignedDistance(Plane plane, Point3D point3D)
        {
            Vector3D normal = plane.Normal.Unit;
            Point3D origin = plane.Origin;
            return (normal.X * (point3D.X - origin.X)) + (normal.Y * (point3D.Y - origin.Y)) + (normal.Z * (point3D.Z - origin.Z));
        }

        private static bool BoxesMeet(Segment3D segment3D, BoundingBox3D box, double tolerance)
        {
            if (box == null)
            {
                return false;
            }

            Point3D a = segment3D[0], b = segment3D[1];
            return System.Math.Min(a.X, b.X) <= box.Max.X + tolerance && System.Math.Max(a.X, b.X) >= box.Min.X - tolerance
                && System.Math.Min(a.Y, b.Y) <= box.Max.Y + tolerance && System.Math.Max(a.Y, b.Y) >= box.Min.Y - tolerance
                && System.Math.Min(a.Z, b.Z) <= box.Max.Z + tolerance && System.Math.Max(a.Z, b.Z) >= box.Min.Z - tolerance;
        }
    }
}
