// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// The 3D analogue of <c>SAM.Geometry.Solver.SnappedWall</c>. Where the 2D solver
    /// modelled a panel by its projected <c>Segment2D</c> axis on a level, a
    /// <see cref="SnappedPanel"/> models the panel by its supporting <see cref="Plane"/>
    /// and planar boundary (<see cref="Face3D"/>) in full 3D, carrying the same
    /// backer (<see cref="Weight"/>) and width (<see cref="BucketSize"/>) semantics.
    /// </summary>
    /// <remarks>
    /// The geometric predicates here are pure managed code (no native OCCT dependency)
    /// so they are deterministic and unit-testable on any platform. Junction splitting,
    /// coplanar merge and naked-edge detection are delegated to the native kernel by
    /// <see cref="Panel3DSnapSolver"/>.
    /// </remarks>
    public class SnappedPanel
    {
        private Face3D face3D;
        private Plane plane;

        /// <summary>Indices of the source panels represented by this snapped panel.</summary>
        public List<int> SourceIndices { get; private set; }

        /// <summary>The original (pre-snap) source faces represented by this snapped panel.</summary>
        public List<Face3D> SourceFace3Ds { get; private set; }

        /// <summary>Priority / dominance ("backer"): higher-weight panels win; lower ones snap onto them.</summary>
        public double Weight { get; private set; }

        /// <summary>Tolerance band ("width") around the supporting plane: half-width of the capture slab.</summary>
        public double BucketSize { get; private set; }

        /// <summary>Trim/extend reach (carried for parity with the 2D solver; reserved for junction logic).</summary>
        public double MaxExtension { get; private set; }

        /// <summary>True once this panel's boundary has been projected onto a higher-weight backer plane.</summary>
        public bool Snapped { get; private set; }

        public SnappedPanel(int sourceIndex, Face3D face3D, double weight, double bucketSize, double maxExtension)
        {
            this.face3D = face3D ?? throw new System.ArgumentNullException(nameof(face3D));
            plane = face3D.GetPlane();
            Weight = weight;
            BucketSize = bucketSize;
            MaxExtension = maxExtension;
            Snapped = false;
            SourceIndices = new List<int> { sourceIndex };
            SourceFace3Ds = new List<Face3D> { face3D };
        }

        /// <summary>The current (possibly snapped) planar boundary.</summary>
        public Face3D Face3D => face3D;

        /// <summary>The supporting plane of the current boundary. Null when the source face is degenerate.</summary>
        public Plane Plane => plane;

        /// <summary>
        /// Coplanar test: the two supporting planes are parallel within
        /// <paramref name="angleTolerance"/> (radians) AND coincident within
        /// <paramref name="distanceTolerance"/>. Mirrors the colinear test of the 2D solver,
        /// lifted from a <c>Segment2D</c> to a <see cref="Plane"/>.
        /// </summary>
        public bool IsCoplanarWith(SnappedPanel other, double angleTolerance, double distanceTolerance)
        {
            if (other?.plane == null || plane == null)
            {
                return false;
            }

            double minDotProduct = System.Math.Cos(angleTolerance);
            if (System.Math.Abs(plane.Normal.Unit.DotProduct(other.plane.Normal.Unit)) < minDotProduct)
            {
                return false;
            }

            return plane.Distance(other.plane.Origin) <= distanceTolerance;
        }

        /// <summary>
        /// True when the two supporting planes are parallel within <paramref name="angleTolerance"/>
        /// (radians), irrespective of offset. Offset is governed separately by the capture slab
        /// (<see cref="BucketContains"/>), which carries the width semantics.
        /// </summary>
        public bool IsParallelWith(SnappedPanel other, double angleTolerance)
        {
            if (other?.plane == null || plane == null)
            {
                return false;
            }

            double minDotProduct = System.Math.Cos(angleTolerance);
            return System.Math.Abs(plane.Normal.Unit.DotProduct(other.plane.Normal.Unit)) >= minDotProduct;
        }

        /// <summary>
        /// True when <paramref name="other"/>'s boundary lies within this panel's capture
        /// slab - the band of half-width <see cref="BucketSize"/> either side of this plane.
        /// The 3D analogue of <c>SnappedWall.BucketContains</c> (band around an axis).
        /// </summary>
        /// <param name="fully">True when the whole of <paramref name="other"/>'s boundary is inside the slab.</param>
        public bool BucketContains(SnappedPanel other, out bool fully)
        {
            fully = false;
            if (plane == null || other?.face3D == null)
            {
                return false;
            }

            List<Point3D> point3Ds = BoundaryPoints(other.face3D);
            if (point3Ds == null || point3Ds.Count == 0)
            {
                return false;
            }

            bool anyInside = false;
            bool allInside = true;
            foreach (Point3D point3D in point3Ds)
            {
                if (System.Math.Abs(plane.Distance(point3D)) <= BucketSize)
                {
                    anyInside = true;
                }
                else
                {
                    allInside = false;
                }
            }

            fully = allInside;
            return anyInside;
        }

        /// <summary>
        /// True when <paramref name="other"/>, projected onto this panel's plane (dropping the
        /// perpendicular bucket offset), actually shares surface area with this panel's face in-plane -
        /// i.e. the two are the same wall seen twice (a genuine double-wall), not two distinct parallel
        /// walls that merely pass through each other's capture slab. <see cref="BucketContains"/> decides
        /// nearness (the perpendicular band); this decides whether a snap would collapse a real duplicate
        /// or wrongly drag a separate wall (e.g. the next bay's wall, colinear but offset along the plane)
        /// onto its neighbour. The 3D analogue of the 2D solver's along-axis overlap requirement, lifted
        /// from a <c>Segment2D</c> to a <see cref="Face3D"/>: both footprints are compared in this plane's
        /// own 2D frame, so a real overlap in both in-plane directions (not just a touching end) is
        /// required. Conservative - never blocks a true double-wall, only the unnecessary moves.
        /// </summary>
        public bool OverlapsInPlane(SnappedPanel other, double tolerance)
        {
            if (plane == null || face3D == null || other?.face3D == null)
            {
                return false;
            }

            if (!FootprintBounds(plane, face3D, out double thisMinU, out double thisMaxU, out double thisMinV, out double thisMaxV))
            {
                return false;
            }

            if (!FootprintBounds(plane, other.face3D, out double otherMinU, out double otherMaxU, out double otherMinV, out double otherMaxV))
            {
                return false;
            }

            double overlapU = System.Math.Min(thisMaxU, otherMaxU) - System.Math.Max(thisMinU, otherMinU);
            double overlapV = System.Math.Min(thisMaxV, otherMaxV) - System.Math.Max(thisMinV, otherMinV);
            return overlapU > tolerance && overlapV > tolerance;
        }

        /// <summary>
        /// Orthographically projects <paramref name="face3D"/>'s external boundary onto
        /// <paramref name="plane"/> and returns its 2D bounding extents in that plane's (AxisX, AxisY)
        /// frame. Used by <see cref="OverlapsInPlane"/> to compare two parallel panels' footprints in a
        /// common frame after the perpendicular offset is dropped.
        /// </summary>
        private static bool FootprintBounds(Plane plane, Face3D face3D, out double minU, out double maxU, out double minV, out double maxV)
        {
            minU = minV = double.MaxValue;
            maxU = maxV = double.MinValue;

            List<Point3D> point3Ds = BoundaryPoints(face3D);
            if (point3Ds == null || point3Ds.Count == 0)
            {
                return false;
            }

            foreach (Point3D point3D in point3Ds)
            {
                Geometry.Planar.Point2D point2D = plane.Convert(point3D);
                if (point2D == null)
                {
                    continue;
                }

                if (point2D.X < minU) { minU = point2D.X; }
                if (point2D.X > maxU) { maxU = point2D.X; }
                if (point2D.Y < minV) { minV = point2D.Y; }
                if (point2D.Y > maxV) { maxV = point2D.Y; }
            }

            return maxU >= minU && maxV >= minV;
        }

        /// <summary>
        /// True when <paramref name="other"/> is a near-colinear continuation of this panel offset by no
        /// more than <paramref name="maxOffset"/> perpendicular: parallel (the caller checks that), their
        /// height (V) bands overlap, and their along-run (U) extents overlap or abut within
        /// <paramref name="maxOffset"/>. This distinguishes consecutive segments of one stepped wall - a
        /// small perpendicular jog at a step, which should align onto one plane - from genuinely separate
        /// parallel walls (a larger offset, or segments far apart along the run, which stay put). Where
        /// <see cref="OverlapsInPlane"/> collapses a true overlapping double-wall, this closes the end-to-end
        /// jog the overlap test deliberately leaves alone. Measured in this panel's own plane frame.
        /// </summary>
        public bool AbutsColinearWithin(SnappedPanel other, double maxOffset, double tolerance)
        {
            if (plane == null || face3D == null || other?.plane == null || other.face3D == null)
            {
                return false;
            }

            if (System.Math.Abs(plane.Distance(other.plane.Origin)) > maxOffset)
            {
                return false; // offset too large to be the same wall
            }

            if (!FootprintBounds(plane, face3D, out double aMinU, out double aMaxU, out double aMinV, out double aMaxV))
            {
                return false;
            }

            if (!FootprintBounds(plane, other.face3D, out double bMinU, out double bMaxU, out double bMinV, out double bMaxV))
            {
                return false;
            }

            double overlapV = System.Math.Min(aMaxV, bMaxV) - System.Math.Max(aMinV, bMinV);
            if (overlapV <= tolerance)
            {
                return false; // not in the same height band - not the same run of wall
            }

            // Along-run gap: negative when the U-extents overlap, positive (and small) when they abut.
            double gapU = System.Math.Max(aMinU, bMinU) - System.Math.Min(aMaxU, bMaxU);
            return gapU <= maxOffset;
        }

        /// <summary>
        /// Projects this panel's boundary onto <paramref name="backerPlane"/> (the analogue of
        /// snapping a lower-weight axis onto a higher-weight one) and records the snap. Every
        /// boundary loop - external and internal (holes) - is projected so the face stays valid.
        /// </summary>
        /// <returns>True when the boundary was re-projected onto a valid face.</returns>
        public bool SnapToBacker(Plane backerPlane)
        {
            if (backerPlane == null || face3D == null)
            {
                return false;
            }

            List<IClosedPlanar3D> loops = new List<IClosedPlanar3D>();

            IClosedPlanar3D external = ProjectLoop(face3D.GetExternalEdge3D(), backerPlane);
            if (external == null)
            {
                return false;
            }
            loops.Add(external);

            List<IClosedPlanar3D> internalEdge3Ds = face3D.GetInternalEdge3Ds();
            if (internalEdge3Ds != null)
            {
                foreach (IClosedPlanar3D internalEdge3D in internalEdge3Ds)
                {
                    IClosedPlanar3D projected = ProjectLoop(internalEdge3D, backerPlane);
                    if (projected != null)
                    {
                        loops.Add(projected);
                    }
                }
            }

            Face3D snappedFace3D = Geometry.Spatial.Face3D.Create(loops);
            if (snappedFace3D == null || !snappedFace3D.IsValid())
            {
                return false;
            }

            face3D = snappedFace3D;
            plane = snappedFace3D.GetPlane();
            Snapped = true;
            return true;
        }

        /// <summary>Merges another panel's source references into this one (used when two equal-weight panels collapse).</summary>
        public void Absorb(SnappedPanel other)
        {
            if (other == null)
            {
                return;
            }

            SourceIndices.AddRange(other.SourceIndices);
            SourceFace3Ds.AddRange(other.SourceFace3Ds);
        }

        /// <summary>The axis-aligned bounds of the current boundary, or null when degenerate.</summary>
        public BoundingBox3D GetBoundingBox()
        {
            return face3D?.GetBoundingBox();
        }

        /// <summary>Planar area of the current boundary (0 when degenerate).</summary>
        public double GetArea()
        {
            return face3D?.GetArea() ?? 0;
        }

        /// <summary>
        /// Discards any internal edges (holes / openings) so only the external shape of the panel remains.
        /// Step 1 keeps clean single-loop panels; window/door openings are not carried into the bucket/merge.
        /// </summary>
        /// <returns>True when holes were present and removed.</returns>
        public bool StripInternalEdges()
        {
            if (face3D == null)
            {
                return false;
            }

            List<IClosedPlanar3D> internalEdge3Ds = face3D.GetInternalEdge3Ds();
            if (internalEdge3Ds == null || internalEdge3Ds.Count == 0)
            {
                return false; // already a clean external shape
            }

            IClosedPlanar3D external = face3D.GetExternalEdge3D();
            if (external == null)
            {
                return false;
            }

            Face3D stripped = Geometry.Spatial.Face3D.Create(new List<IClosedPlanar3D> { external });
            if (stripped == null || !stripped.IsValid())
            {
                return false;
            }

            face3D = stripped;
            plane = stripped.GetPlane();
            return true;
        }

        /// <summary>
        /// Grows a (horizontal/sloped) cap - a floor or roof - outward in its own plane by
        /// <paramref name="margin"/> metres, so it overshoots the surrounding walls and the native kernel
        /// can trim it back at them (closing the floor/roof-to-wall gap). Only the external boundary is
        /// offset; holes are preserved. No-op when the offset fails or shrinks the face.
        /// </summary>
        /// <returns>True when the cap was grown to a valid larger face.</returns>
        public bool GrowOutward(double margin, double tolerance)
        {
            if (face3D == null || plane == null || margin <= tolerance)
            {
                return false;
            }

            Geometry.Planar.Face2D face2D = plane.Convert(face3D);
            if (face2D == null)
            {
                return false;
            }

            List<Geometry.Planar.Face2D> offset = Geometry.Planar.Query.Offset(face2D, margin, true, true, tolerance);
            Geometry.Planar.Face2D grown = offset?.OrderByDescending(x => x.GetArea()).FirstOrDefault();
            if (grown == null || grown.GetArea() <= face2D.GetArea())
            {
                return false; // offset went inward or failed
            }

            Face3D grown3D = plane.Convert(grown);
            if (grown3D == null || !grown3D.IsValid())
            {
                return false;
            }

            face3D = grown3D;
            plane = grown3D.GetPlane();
            return true;
        }

        /// <summary>
        /// True when the supporting plane is (near) vertical - the normal lies (near) horizontal,
        /// i.e. |normal.Z| is within <paramref name="angleTolerance"/> of zero. Walls are vertical;
        /// floors and roofs are not. Used to decide which panels are extended up to a cap.
        /// </summary>
        public bool IsVertical(double angleTolerance)
        {
            if (plane == null)
            {
                return false;
            }

            return System.Math.Abs(plane.Normal.Unit.Z) <= System.Math.Sin(angleTolerance);
        }

        /// <summary>
        /// Lengthens this (vertical) panel upward so its top reaches <paramref name="targetZ"/>, by
        /// re-extruding its base edge to the new height. The 3D analogue of the 2D solver's
        /// extend-to-junction: a wall that stops short of the floor/roof above is grown so the native
        /// kernel can trim it against that cap (e.g. split a gable wall at the roof pitch). Only the
        /// top moves; the base footprint and supporting plane are preserved.
        /// </summary>
        /// <returns>True when the panel was extended to a valid taller face.</returns>
        public bool ExtendTopTo(double targetZ, double tolerance)
        {
            if (face3D == null || plane == null)
            {
                return false;
            }

            BoundingBox3D boundingBox3D = face3D.GetBoundingBox();
            if (boundingBox3D == null)
            {
                return false;
            }

            double baseZ = boundingBox3D.Min.Z;
            double topZ = boundingBox3D.Max.Z;
            if (targetZ <= topZ + tolerance)
            {
                return false; // already tall enough
            }

            // Recover the horizontal base edge: the panel cut just above its foot.
            Segment3D baseSegment3D = GetBaseSegment(tolerance);
            if (baseSegment3D == null)
            {
                return false;
            }

            Face3D extended = Geometry.Spatial.Create.Face3D(baseSegment3D, new Vector3D(0, 0, targetZ - baseZ));
            if (extended == null || !extended.IsValid())
            {
                return false;
            }

            face3D = extended;
            plane = extended.GetPlane();
            return true;
        }

        /// <summary>
        /// Lengthens this (vertical) panel downward so its base reaches <paramref name="targetZ"/>, by
        /// re-extruding its top edge down to the new height. The mirror of <see cref="ExtendTopTo"/>: a wall
        /// that stops short of the floor below is grown down so the native kernel can trim it against that
        /// floor and close the room. Only the base moves; the top and supporting plane are preserved.
        /// </summary>
        /// <returns>True when the panel was extended to a valid taller face.</returns>
        public bool ExtendBottomTo(double targetZ, double tolerance)
        {
            if (face3D == null || plane == null)
            {
                return false;
            }

            BoundingBox3D boundingBox3D = face3D.GetBoundingBox();
            if (boundingBox3D == null)
            {
                return false;
            }

            double baseZ = boundingBox3D.Min.Z;
            double topZ = boundingBox3D.Max.Z;
            if (targetZ >= baseZ - tolerance)
            {
                return false; // already low enough
            }

            // Recover the horizontal base edge, then drop it to the target elevation.
            Segment3D baseSegment3D = GetBaseSegment(tolerance);
            if (baseSegment3D == null)
            {
                return false;
            }

            Point3D start = baseSegment3D.GetStart();
            Point3D end = baseSegment3D.GetEnd();
            Segment3D loweredSegment3D = new Segment3D(
                new Point3D(start.X, start.Y, targetZ),
                new Point3D(end.X, end.Y, targetZ));

            Face3D extended = Geometry.Spatial.Create.Face3D(loweredSegment3D, new Vector3D(0, 0, topZ - targetZ));
            if (extended == null || !extended.IsValid())
            {
                return false;
            }

            face3D = extended;
            plane = extended.GetPlane();
            return true;
        }

        /// <summary>
        /// The horizontal foot of a (vertical) wall: the panel cut by a horizontal plane just above its
        /// base. Its direction is the wall's in-plan axis - the X/Y direction the wall runs along - and its
        /// endpoints are the wall's two ends in plan. The basis for both the vertical re-extrude
        /// (<see cref="ExtendTopTo"/>) and the lateral one (<see cref="ExtendHorizontal"/>). Null for a
        /// degenerate face or a cut that yields no segment.
        /// </summary>
        public Segment3D GetBaseSegment(double tolerance)
        {
            if (face3D == null)
            {
                return null;
            }

            BoundingBox3D boundingBox3D = face3D.GetBoundingBox();
            if (boundingBox3D == null)
            {
                return null;
            }

            Plane basePlane = Geometry.Spatial.Create.Plane(boundingBox3D.Min.Z + tolerance);
            Segment3D baseSegment3D = Geometry.Spatial.Query.MaxIntersectionSegment3D(basePlane, face3D);
            if (baseSegment3D == null || baseSegment3D.GetLength() <= tolerance)
            {
                return null;
            }

            return baseSegment3D;
        }

        /// <summary>
        /// Lengthens this (vertical) wall sideways along its own in-plan axis - the horizontal direction it
        /// runs along - growing it <paramref name="startReach"/> metres past its start end and
        /// <paramref name="endReach"/> metres past its end end, then re-extruding the widened foot to the
        /// wall's height. The lateral analogue of <see cref="ExtendTopTo"/>: a wall whose end stops short of
        /// the next wall is grown sideways so the native kernel can trim it at that wall and close the plan
        /// loop. Each end grows independently (a different reach per direction); a non-positive reach leaves
        /// that end where it is. Assumes a prismatic (vertical-rectangular) wall - the height profile is
        /// rebuilt flat between base and top, matching <see cref="ExtendTopTo"/>'s own model.
        /// </summary>
        /// <returns>True when the wall was re-extruded to a valid wider face.</returns>
        public bool ExtendHorizontal(double startReach, double endReach, double tolerance)
        {
            if (face3D == null || plane == null)
            {
                return false;
            }

            if (startReach <= tolerance && endReach <= tolerance)
            {
                return false; // nothing to grow
            }

            BoundingBox3D boundingBox3D = face3D.GetBoundingBox();
            if (boundingBox3D == null)
            {
                return false;
            }

            double baseZ = boundingBox3D.Min.Z;
            double topZ = boundingBox3D.Max.Z;

            Segment3D baseSegment3D = GetBaseSegment(tolerance);
            if (baseSegment3D == null)
            {
                return false;
            }

            Point3D start = baseSegment3D.GetStart();
            Point3D end = baseSegment3D.GetEnd();
            double length = baseSegment3D.GetLength();

            // Unit axis (start -> end) in plan; the wall is vertical so Z plays no part.
            double dx = (end.X - start.X) / length;
            double dy = (end.Y - start.Y) / length;

            Point3D widenedStart = startReach > tolerance
                ? new Point3D(start.X - dx * startReach, start.Y - dy * startReach, baseZ)
                : new Point3D(start.X, start.Y, baseZ);
            Point3D widenedEnd = endReach > tolerance
                ? new Point3D(end.X + dx * endReach, end.Y + dy * endReach, baseZ)
                : new Point3D(end.X, end.Y, baseZ);

            Segment3D widenedSegment3D = new Segment3D(widenedStart, widenedEnd);
            Face3D extended = Geometry.Spatial.Create.Face3D(widenedSegment3D, new Vector3D(0, 0, topZ - baseZ));
            if (extended == null || !extended.IsValid())
            {
                return false;
            }

            face3D = extended;
            plane = extended.GetPlane();
            return true;
        }

        /// <summary>
        /// Re-extrudes this (vertical) wall onto a new plan foot, given the foot's two ends in plan (X, Y).
        /// The wall's base and top elevations are preserved; the foot is rebuilt at the base Z and extruded
        /// up. Unlike <see cref="ExtendHorizontal"/> (which only grows along the existing axis), this accepts
        /// an arbitrary new foot - the extended/trimmed segment the plan-loop solver resolved - so a wall can
        /// be both lengthened and shortened to meet its junctions. No-op when the new foot is degenerate.
        /// </summary>
        public bool SetVerticalFootprint(Geometry.Planar.Point2D newStart, Geometry.Planar.Point2D newEnd, double tolerance)
        {
            if (face3D == null || plane == null || newStart == null || newEnd == null)
            {
                return false;
            }

            BoundingBox3D boundingBox3D = face3D.GetBoundingBox();
            if (boundingBox3D == null)
            {
                return false;
            }

            double baseZ = boundingBox3D.Min.Z;
            double topZ = boundingBox3D.Max.Z;
            if (topZ - baseZ <= tolerance)
            {
                return false;
            }

            Segment3D foot = new Segment3D(
                new Point3D(newStart.X, newStart.Y, baseZ),
                new Point3D(newEnd.X, newEnd.Y, baseZ));
            if (foot.GetLength() <= tolerance)
            {
                return false;
            }

            Face3D extended = Geometry.Spatial.Create.Face3D(foot, new Vector3D(0, 0, topZ - baseZ));
            if (extended == null || !extended.IsValid())
            {
                return false;
            }

            face3D = extended;
            plane = extended.GetPlane();
            return true;
        }

        private static IClosedPlanar3D ProjectLoop(IClosedPlanar3D loop, Plane backerPlane)
        {
            List<Point3D> point3Ds = (loop as ISegmentable3D)?.GetPoints();
            if (point3Ds == null || point3Ds.Count < 3)
            {
                return null;
            }

            List<Point3D> projected = point3Ds.Select(backerPlane.Project).ToList();
            return new Polygon3D(projected);
        }

        private static List<Point3D> BoundaryPoints(Face3D face3D)
        {
            return (face3D?.GetExternalEdge3D() as ISegmentable3D)?.GetPoints();
        }
    }
}
