// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Planar;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Decides whether an anti-parallel pair wider than <see cref="Panel3DSnapSolver.OPPOSED_PARTITION_MAX_SEPARATION"/> is two skins of
    /// one solid wall that touch - not the two sides of a real void - from what each source element was proved to contain
    /// (<see cref="ContactEvidence"/>). Design rev 5, section 3 (Softhouse D203): the existing eligibility of the caller (bucket reach,
    /// in-plane overlap, the 0.5 ratio on the snapped panels) runs first and is not relaxed; this only lifts the 0.3 m void guard, never
    /// the 0.3 constant itself.
    /// <para>
    /// Everything is measured in the first panel's record frame with true polygons: the clipped common region
    /// <c>P' = OriginA n OriginB n currentFaceA n currentFaceB</c> (current faces, so material trimmed away by junction snapping is not
    /// counted), its coverage of the larger bounding box (at least <see cref="Panel3DSnapSolver.COPARALLEL_SNAP_MIN_OVERLAP_RATIO"/>),
    /// no certified-thickness gap inside it, and the separation bound at every vertex of it. The separation is a convex function of the
    /// position, so the vertices bound the whole region. Any region operation that cannot be decided, an empty region, or planes that are
    /// not opposed, is a refusal.
    /// </para>
    /// </summary>
    internal static class ContactVoidGuard
    {
        /// <summary>Smallest <c>|nA . nB|</c> for a pair to be treated as two faces of one wall; below it the thickness projected along A's
        /// normal (g / (2|c|)) is meaningless.</summary>
        internal const double MIN_ABSOLUTE_COSINE = 0.1;

        /// <summary>
        /// True when the pair's records and geometry prove contact, so the void guard must not hold it back.
        /// </summary>
        /// <param name="a">First panel; its record's plane is the frame of the measurement.</param>
        /// <param name="b">Second panel.</param>
        /// <param name="reason">Why the pair is not released; null when it is.</param>
        internal static bool Releases(SnappedPanel a, SnappedPanel b, out string reason)
        {
            reason = null;

            ContactEvidence evidenceA = a?.LiveContactEvidence;
            ContactEvidence evidenceB = b?.LiveContactEvidence;
            if (evidenceA == null || evidenceB == null)
            {
                reason = "no live source evidence on both panels";
                return false;
            }

            Func<IEnumerable<Face2D>, IEnumerable<Face2D>, double, List<Face2D>> intersect = evidenceA.RegionIntersection;
            if (intersect == null || evidenceB.RegionIntersection == null)
            {
                reason = "no definite region intersection supplied";
                return false;
            }

            double tolerance = Core.Tolerance.Distance;
            Plane frame = evidenceA.Plane;
            Vector3D normalA = frame.Normal.Unit;
            Vector3D normalB = evidenceB.Plane.Normal.Unit;
            double c = normalB.DotProduct(normalA);
            if (c >= 0 || System.Math.Abs(c) < MIN_ABSOLUTE_COSINE)
            {
                reason = "the planes are not opposed";
                return false;
            }

            // Everything below in the frame of A's record.
            List<Face2D> originA = evidenceA.Origin;
            List<Face2D> supportA = evidenceA.Support;
            List<Face2D> originB = FromFrame(evidenceB.Plane, evidenceB.Origin, frame);
            List<Face2D> supportB = FromFrame(evidenceB.Plane, evidenceB.Support, frame);
            List<Face2D> currentA = Project(frame, a.Face3D);
            List<Face2D> currentB = Project(frame, b.Face3D);
            if (originB == null || supportB == null || currentA == null || currentB == null)
            {
                reason = "a footprint could not be expressed in the frame of the record";
                return false;
            }

            // 2. The clipped common region.
            List<Face2D> region = intersect(originA, originB, tolerance);
            region = region == null ? null : intersect(region, currentA, tolerance);
            region = region == null ? null : intersect(region, currentB, tolerance);
            if (region == null)
            {
                reason = "the common region could not be decided";
                return false;
            }

            double regionArea = Area(region);
            if (region.Count == 0 || regionArea <= tolerance)
            {
                reason = "the clipped common region is empty";
                return false;
            }

            // 3. Coverage of the larger bounding box.
            double larger = System.Math.Max(BoundingArea(currentA), BoundingArea(currentB));
            if (!(larger > 0) || regionArea / larger < Panel3DSnapSolver.COPARALLEL_SNAP_MIN_OVERLAP_RATIO)
            {
                reason = string.Format(System.Globalization.CultureInfo.InvariantCulture, "the clipped common region covers {0:P1} of the larger footprint", larger > 0 ? regionArea / larger : 0);
                return false;
            }

            // 4. No gap of certified thickness inside the region.
            List<Face2D> supported = intersect(region, supportA, tolerance);
            supported = supported == null ? null : intersect(supported, supportB, tolerance);
            if (supported == null)
            {
                reason = "the supported part of the region could not be decided";
                return false;
            }

            double gapArea = regionArea - Area(supported);
            double allowed = tolerance * Perimeter(region);
            if (!(gapArea <= allowed))
            {
                reason = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:G4} m2 of the region has no certified thickness on both sides (allowed {1:G3} m2)", gapArea, allowed);
                return false;
            }

            // 5. The slabs touch at every vertex of the region.
            double gA = evidenceA.GuaranteedThickness;
            double gB = evidenceB.GuaranteedThickness;
            Point3D originOfB = evidenceB.Plane.Origin;
            foreach (Face2D face2D in region)
            {
                foreach (IClosed2D loop in Loops(face2D))
                {
                    List<Point2D> point2Ds = (loop as ISegmentable2D)?.GetPoints();
                    if (point2Ds == null)
                    {
                        reason = "the region has a curved boundary";
                        return false;
                    }

                    foreach (Point2D point2D in point2Ds)
                    {
                        Point3D p = frame.Convert(point2D);
                        double tB = normalB.DotProduct(new Vector3D(originOfB.X - p.X, originOfB.Y - p.Y, originOfB.Z - p.Z)) / c;
                        double gap = System.Math.Abs(tB) - gB / (2 * System.Math.Abs(c)) - gA / 2;
                        if (!(gap <= tolerance))
                        {
                            reason = string.Format(System.Globalization.CultureInfo.InvariantCulture, "the certified slabs are {0:G4} m apart at ({1:0.###}, {2:0.###}, {3:0.###})", gap, p.X, p.Y, p.Z);
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>Regions of <paramref name="from"/> expressed in <paramref name="to"/>: through space and orthogonally onto the plane.</summary>
        private static List<Face2D> FromFrame(Plane from, IEnumerable<Face2D> face2Ds, Plane to)
        {
            List<Face2D> result = new List<Face2D>();
            foreach (Face2D face2D in face2Ds)
            {
                Face3D face3D = from.Convert(face2D);
                List<Face2D> projected = Project(to, face3D);
                if (projected == null)
                {
                    return null;
                }

                result.AddRange(projected);
            }

            return result;
        }

        /// <summary>A face's boundary projected orthogonally onto <paramref name="plane"/> (the perpendicular offset dropped). Inner loops are
        /// kept only when <paramref name="externalOnly"/> is false. Null when the boundary cannot be expressed.</summary>
        private static List<Face2D> Project(Plane plane, Face3D face3D, bool externalOnly = true)
        {
            if (face3D == null)
            {
                return null;
            }

            Polygon2D external = ProjectLoop(plane, face3D.GetExternalEdge3D());
            if (external == null)
            {
                return null;
            }

            List<IClosed2D> internals = new List<IClosed2D>();
            if (!externalOnly)
            {
                foreach (IClosedPlanar3D internalEdge3D in face3D.GetInternalEdge3Ds() ?? new List<IClosedPlanar3D>())
                {
                    Polygon2D inner = ProjectLoop(plane, internalEdge3D);
                    if (inner == null)
                    {
                        return null;
                    }

                    internals.Add(inner);
                }
            }

            Face2D result = Planar.Create.Face2D(external, internals);
            return result == null ? null : new List<Face2D> { result };
        }

        private static Polygon2D ProjectLoop(Plane plane, IClosedPlanar3D loop)
        {
            List<Point3D> point3Ds = (loop as ISegmentable3D)?.GetPoints();
            if (point3Ds == null || point3Ds.Count < 3)
            {
                return null;
            }

            List<Point2D> point2Ds = point3Ds.ConvertAll(x => plane.Convert(x));
            return point2Ds.Any(x => x == null) ? null : new Polygon2D(point2Ds);
        }

        private static IEnumerable<IClosed2D> Loops(Face2D face2D)
        {
            yield return face2D.ExternalEdge2D;
            foreach (IClosed2D internalEdge2D in face2D.InternalEdge2Ds ?? new List<IClosed2D>())
            {
                yield return internalEdge2D;
            }
        }

        private static double Area(IEnumerable<Face2D> face2Ds)
        {
            return face2Ds.Where(x => x != null).Sum(x => x.GetArea());
        }

        private static double Perimeter(IEnumerable<Face2D> face2Ds)
        {
            double result = 0;
            foreach (Face2D face2D in face2Ds.Where(x => x != null))
            {
                foreach (IClosed2D loop in Loops(face2D))
                {
                    result += loop?.Perimeter() ?? 0;
                }
            }

            return result;
        }

        /// <summary>Area of the axis-aligned bounding box (in the record frame) of the regions' external boundaries.</summary>
        private static double BoundingArea(IEnumerable<Face2D> face2Ds)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (Face2D face2D in face2Ds)
            {
                BoundingBox2D boundingBox2D = face2D?.GetBoundingBox();
                if (boundingBox2D == null)
                {
                    continue;
                }

                minX = System.Math.Min(minX, boundingBox2D.Min.X);
                minY = System.Math.Min(minY, boundingBox2D.Min.Y);
                maxX = System.Math.Max(maxX, boundingBox2D.Max.X);
                maxY = System.Math.Max(maxY, boundingBox2D.Max.Y);
            }

            return maxX < minX || maxY < minY ? 0 : (maxX - minX) * (maxY - minY);
        }
    }
}
