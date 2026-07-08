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
        private readonly List<string> extendDiagnostics = new List<string>();

        // E1 extend-path census (docs/EXTEND3D_ROBUST_HANDOVER.md). Counts, since the last
        // ResetExtendCensus(), how many extend/footprint operations took the byte-identical legacy
        // re-extrude fast path vs the profile-preserving plane-ops path, plus how many internal
        // openings a trim dropped. Pure test instrumentation - never read by production logic; the
        // golden-freeze gate asserts PlaneOpsExtendCount == 0 on the five golden fixtures (every
        // wall there is a plain vertical rectangle and must take the fast path).
        internal static int FastPathExtendCount;
        internal static int PlaneOpsExtendCount;
        internal static int HoleDroppedCount;

        internal static void ResetExtendCensus()
        {
            FastPathExtendCount = 0;
            PlaneOpsExtendCount = 0;
            HoleDroppedCount = 0;
        }

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
        /// Coded diagnostics accumulated by the profile-preserving extend/trim path - currently
        /// only <c>SAM_OCCT_EXTEND3D_HOLE_DROPPED</c>, emitted when a footprint trim clips or drops
        /// an internal opening (a window/door) rather than dropping it silently (E1 / R6). Empty on
        /// the byte-identical rectangular fast path, which never touches openings.
        /// </summary>
        public IReadOnlyList<string> ExtendDiagnostics => extendDiagnostics;

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

        /// <summary>
        /// Signed perpendicular offset from this panel's plane to <paramref name="other"/>'s centroid,
        /// measured along this plane's own normal (positive on the side the normal points to). The raw
        /// building block for <see cref="PerpendicularSeparation"/> and the equal-weight midpoint rule
        /// (<see cref="MoveToMidplaneWith"/>). <see cref="double.MaxValue"/> when undefined.
        /// </summary>
        private double SignedSeparationTo(SnappedPanel other)
        {
            if (plane == null || other == null)
            {
                return double.MaxValue;
            }

            Point3D otherCentroid = other.GetBoundingBox()?.GetCentroid();
            if (otherCentroid == null)
            {
                return double.MaxValue;
            }

            Vector3D normal = plane.Normal.Unit;
            Point3D origin = plane.Origin;
            return normal.X * (otherCentroid.X - origin.X)
                + normal.Y * (otherCentroid.Y - origin.Y)
                + normal.Z * (otherCentroid.Z - origin.Z);
        }

        /// <summary>
        /// The perpendicular separation (metres) between this panel's plane and <paramref name="other"/>'s
        /// centroid - the thickness of the gap between two near-parallel skins. Used by
        /// <see cref="Panel3DSnapSolver.SnapOpposedPartitions"/> to tell a back-to-back partition (skins within
        /// a wall thickness) from a genuine void/shaft (a wider gap that must survive). Winding-independent
        /// (a magnitude), unlike a normal-sign test - see the remarks on SnapOpposedPartitions for why the
        /// sign approach proved unreliable on real import geometry.
        /// </summary>
        public double PerpendicularSeparation(SnappedPanel other)
        {
            double signed = SignedSeparationTo(other);
            return signed == double.MaxValue ? double.MaxValue : System.Math.Abs(signed);
        }

        /// <summary>
        /// Moves this panel halfway toward <paramref name="other"/>'s plane (along this plane's own normal)
        /// and grows this panel's <see cref="BucketSize"/> by the distance moved - the 3D analogue of the 2D
        /// solver's equal-weight tie-break (average position + bucket bump,
        /// <c>SnapSolver.TryBucketSnap</c>/<c>SnappedWall.cs:417-426</c>), used when two panels of (near) equal
        /// <see cref="Weight"/> are captured together so neither is treated as unconditionally subordinate to
        /// the other. No-op (returns false) when the two are already effectively coincident.
        /// </summary>
        public bool MoveToMidplaneWith(SnappedPanel other, double tolerance)
        {
            if (plane == null || other?.plane == null)
            {
                return false;
            }

            double halfOffset = SignedSeparationTo(other) / 2.0;
            if (System.Math.Abs(halfOffset) <= tolerance)
            {
                return false; // already effectively coincident - nothing to move
            }

            Plane midPlane = (Plane)plane.GetMoved(plane.Normal.Unit * halfOffset);
            if (!SnapToBacker(midPlane))
            {
                return false;
            }

            GrowBucket(System.Math.Abs(halfOffset));
            return true;
        }

        /// <summary>Widens the capture slab by <paramref name="amount"/> (never negative) - the bucket-growth
        /// half of the equal-weight midpoint rule (<see cref="MoveToMidplaneWith"/>).</summary>
        public void GrowBucket(double amount)
        {
            if (amount > 0)
            {
                BucketSize += amount;
            }
        }

        /// <summary>
        /// Ratio of the in-plane overlap footprint to the larger of the two panels' footprints, both
        /// measured in this panel's plane frame (so a near-coplanar/anti-parallel partner is projected onto
        /// this plane first). 1.0 when the two occupy the same footprint - one partition's two skins, even
        /// when one carries a door notch that cuts its <em>area</em> but not its bounding footprint - and
        /// small when one footprint is much larger than the other (a long shared wall caught against a short
        /// partition skin, or two distinct walls). Used by <see cref="Panel3DSnapSolver.SnapOpposedPartitions"/>
        /// to tell a real back-to-back partition from a mis-pair without being fooled by a door cut (which the
        /// old full-<em>area</em> ratio was). 0 when either footprint is degenerate or they do not overlap.
        /// </summary>
        public double InPlaneOverlapRatio(SnappedPanel other)
        {
            if (plane == null || face3D == null || other?.face3D == null)
            {
                return 0;
            }

            if (!FootprintBounds(plane, face3D, out double aMinU, out double aMaxU, out double aMinV, out double aMaxV))
            {
                return 0;
            }

            if (!FootprintBounds(plane, other.face3D, out double bMinU, out double bMaxU, out double bMinV, out double bMaxV))
            {
                return 0;
            }

            double overlapU = System.Math.Min(aMaxU, bMaxU) - System.Math.Max(aMinU, bMinU);
            double overlapV = System.Math.Min(aMaxV, bMaxV) - System.Math.Max(aMinV, bMinV);
            if (overlapU <= 0 || overlapV <= 0)
            {
                return 0;
            }

            double overlapArea = overlapU * overlapV;
            double areaA = (aMaxU - aMinU) * (aMaxV - aMinV);
            double areaB = (bMaxU - bMinU) * (bMaxV - bMinV);
            double larger = System.Math.Max(areaA, areaB);
            return larger <= 0 ? 0 : overlapArea / larger;
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
        /// Smart analogue of <see cref="GrowOutward"/>: instead of growing this cap by a blind fixed
        /// margin, grows it by the <em>measured</em> gap to the walls it is short of. For each in-reach
        /// vertical wall, the smallest perpendicular distance from this cap's boundary to the wall plane is
        /// how far the cap falls short on that side; the cap is offset outward by the largest such gap
        /// (clamped to <paramref name="maxReach"/>) plus a small <paramref name="overshoot"/> the native
        /// kernel trims back at the walls. This reaches every wall in range without the blanket overshoot of
        /// the fixed margin, and never grows further than that margin. No-op (returns false) when no wall
        /// borders this cap, so the caller can fall back to the fixed-margin <see cref="GrowOutward"/>.
        /// </summary>
        /// <param name="walls">Candidate (vertical) wall panels that may bound this cap.</param>
        /// <param name="maxReach">Upper bound on how far the cap may grow (the fill margin / panel reach).</param>
        /// <param name="overshoot">Small extra growth past the wall so the kernel trims a clean edge.</param>
        /// <returns>True when the cap was grown toward its walls; false when no wall is in reach.</returns>
        public bool GrowOutwardTo(IEnumerable<SnappedPanel> walls, double maxReach, double overshoot, double tolerance)
        {
            if (face3D == null || plane == null || walls == null || maxReach <= tolerance)
            {
                return false;
            }

            BoundingBox3D capBox = face3D.GetBoundingBox();
            List<Point3D> capPoints = BoundaryPoints(face3D);
            if (capBox == null || capPoints == null || capPoints.Count == 0)
            {
                return false;
            }

            bool anyWallInReach = false;
            double maxGap = 0;
            foreach (SnappedPanel wall in walls)
            {
                Plane wallPlane = wall?.Plane;
                BoundingBox3D wallBox = wall?.GetBoundingBox();
                if (wallPlane == null || wallBox == null)
                {
                    continue;
                }

                // Locality: the wall must border this cap in plan (within reach), not be a parallel wall
                // elsewhere that merely shares a near perpendicular offset with the cap's plane.
                if (!OverlapsInPlanExpanded(capBox, wallBox, maxReach))
                {
                    continue;
                }

                // How far this cap currently falls short of the wall plane: the nearest its boundary gets.
                double gap = double.MaxValue;
                foreach (Point3D capPoint in capPoints)
                {
                    double distance = System.Math.Abs(wallPlane.Distance(capPoint));
                    if (distance < gap)
                    {
                        gap = distance;
                    }
                }

                if (gap > maxReach + tolerance)
                {
                    continue; // wall is out of reach - growing to it would overshoot past the cap
                }

                anyWallInReach = true;
                if (gap > maxGap)
                {
                    maxGap = gap;
                }
            }

            if (!anyWallInReach)
            {
                return false; // no wall borders this cap - let the caller fall back to the fixed margin
            }

            double reach = System.Math.Min(maxGap, maxReach) + System.Math.Max(overshoot, 0);
            return GrowOutward(reach, tolerance);
        }

        /// <summary>
        /// True when the supporting plane is (near) vertical - the normal lies (near) horizontal,
        /// i.e. |normal.Z| is within <paramref name="angleTolerance"/> of zero. Walls are vertical;
        /// floors and roofs are not. Used to decide which panels are extended up to a cap.
        /// <para>
        /// This is the world-Z special case of the frame-aware
        /// <see cref="IsVertical(double, Vector3D)"/> (Phase 6): it delegates with the world up-axis, so
        /// every existing caller is byte-identical. A caller with a <see cref="LevelFrame"/> passes the
        /// level's up-axis instead, so a wall on a tilted level is still recognised as a wall past the 20°
        /// world-frame ceiling.
        /// </para>
        /// </summary>
        public bool IsVertical(double angleTolerance)
        {
            return IsVertical(angleTolerance, new Vector3D(0, 0, 1));
        }

        /// <summary>
        /// Frame-aware verticality: true when the supporting plane's normal is (near) perpendicular to
        /// <paramref name="upAxis"/> - i.e. within <paramref name="angleTolerance"/> of the plane through the
        /// origin normal to <paramref name="upAxis"/> (the level's "horizontal"). Reduces exactly to the
        /// world-Z <see cref="IsVertical(double)"/> when <paramref name="upAxis"/> is world Z: the dot product
        /// with (0,0,1) is just the normal's Z. Phase 6 removes the hard world-Z assumption here without
        /// changing any existing (world-Z) result. A null/degenerate up-axis falls back to world Z.
        /// </summary>
        public bool IsVertical(double angleTolerance, Vector3D upAxis)
        {
            if (plane == null)
            {
                return false;
            }

            Vector3D up = upAxis == null || upAxis.Length <= Core.Tolerance.Distance ? new Vector3D(0, 0, 1) : upAxis.Unit;
            return System.Math.Abs(plane.Normal.Unit.DotProduct(up)) <= System.Math.Sin(angleTolerance);
        }

        /// <summary>
        /// Lengthens this (vertical) panel upward so its top reaches <paramref name="targetZ"/>. The 3D
        /// analogue of the 2D solver's extend-to-junction: a wall that stops short of the floor/roof above
        /// is grown so the native kernel can trim it against that cap. A plain vertical rectangle takes the
        /// byte-identical legacy re-extrude; any other profile (sloped/shifted/gable/M-top, or a wall with a
        /// window) is extended to the horizontal plane at <paramref name="targetZ"/> via
        /// <see cref="Geometry.Spatial.Query.Extend(Face3D, Plane, double, double)"/>, which preserves the
        /// base profile, the openings, and the supporting plane while giving a flat top at the target (the
        /// kernel re-cuts the true roofline). The E1 fix: the legacy path collapsed non-rectangular walls to
        /// a degenerate sliver ("walls disappear after Extend3D").
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

            if (targetZ <= boundingBox3D.Max.Z + tolerance)
            {
                return false; // already tall enough
            }

            if (IsRectangularHoleFreeVertical(face3D, boundingBox3D, tolerance))
            {
                return LegacyExtendTop(targetZ, boundingBox3D, tolerance);
            }

            return ExtendToPlane(Geometry.Spatial.Create.Plane(targetZ), tolerance);
        }

        /// <summary>
        /// Lengthens this (vertical) panel downward so its base reaches <paramref name="targetZ"/>. The
        /// mirror of <see cref="ExtendTopTo"/>: a rectangle re-extrudes byte-identically, any other profile
        /// is extended to the horizontal plane at <paramref name="targetZ"/> (base flattened to the target,
        /// top profile and openings preserved).
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

            if (targetZ >= boundingBox3D.Min.Z - tolerance)
            {
                return false; // already low enough
            }

            if (IsRectangularHoleFreeVertical(face3D, boundingBox3D, tolerance))
            {
                return LegacyExtendBottom(targetZ, boundingBox3D, tolerance);
            }

            return ExtendToPlane(Geometry.Spatial.Create.Plane(targetZ), tolerance);
        }

        /// <summary>
        /// E2 (docs/EXTEND3D_ROBUST_HANDOVER.md): lengthen this (vertical) panel UP so its top reaches the
        /// ACTUAL cap plane <paramref name="capPlane"/> (a sloped roof or a flat ceiling/floor) offset by
        /// <paramref name="overshoot"/>, so the native kernel trims the wall cleanly along the real surface.
        /// Where the scalar <see cref="ExtendTopTo(double, double)"/> grows the wall to a FLAT top at a single
        /// Z, this follows the cap's true (possibly sloped) surface: a wall under a pitched roof gains a sloped
        /// top matching the roof. The extension is COLUMN-WISE within the wall's own plan extent (E2 review
        /// finding: the generic <c>Query.Extend</c> extreme-projection construction is horizontal-target-only -
        /// on an inclined line it spills sideways in plan, the room-merge vector, and under-covers the high
        /// side as pitch grows), and the target is CLAMPED at <paramref name="capTopZ"/> +
        /// <paramref name="overshoot"/> - the cap's real top - so a cap plane extrapolated far beyond the cap's
        /// physical extent (a small angled shed clipping a long wall's bbox) can never drag the wall past what
        /// the cap could actually trim (the pre-E2 scalar bound). A (near) horizontal cap routes to the scalar
        /// path (byte-identical for a plain rectangle); a cap plane parallel to the wall, diving below the wall
        /// base, or with no usable slope across the wall falls back to the scalar path - never a throw, never
        /// material below the base.
        /// </summary>
        /// <returns>True when the panel was grown to a valid taller face; false on a no-op or a hard failure.</returns>
        public bool ExtendTopToPlane(Plane capPlane, double capTopZ, double overshoot, double tolerance)
        {
            return ExtendToCapPlane(capPlane, capTopZ, overshoot, true, tolerance);
        }

        /// <summary>
        /// The mirror of <see cref="ExtendTopToPlane"/>: lengthen this (vertical) panel DOWN so its base
        /// reaches the cap plane below (a sloped or flat floor), offset by <paramref name="overshoot"/> and
        /// clamped at <paramref name="capBottomZ"/> - <paramref name="overshoot"/> (the floor's real bottom).
        /// Same construction (column-wise within the wall's plan extent) and the mirrored guards (parallel /
        /// rising-above-the-top / no-slope fall back to the scalar path; never material above the top).
        /// </summary>
        /// <returns>True when the panel was grown to a valid lower face; false on a no-op or a hard failure.</returns>
        public bool ExtendBottomToPlane(Plane capPlane, double capBottomZ, double overshoot, double tolerance)
        {
            return ExtendToCapPlane(capPlane, capBottomZ, overshoot, false, tolerance);
        }

        /// <summary>
        /// The plan foot of a (vertical) wall: a horizontal segment at the wall's base elevation whose
        /// direction is the wall's longest external edge projected into plan, and whose two endpoints are
        /// the FULL plan extent of the wall's boundary along that direction. Feeds the plan-loop solver
        /// (<c>Panel3DSnapSolver.ExtendWalls</c>/<c>OpenWallEnds</c>). The E1 fix (R7): the old horizontal
        /// cut just above the base under-measured a wall with a door notch or a stepped foot to the notched
        /// width; the extent-of-all-boundary-points span is the wall's true plan length. The direction
        /// tie-breaks longer edge, then lower mean Z, then lower index, so a parallelogram's equal-length
        /// base/top no longer resolves by point order. For a vertical wall every boundary point projects
        /// onto one plan line, so the direction choice is safe. Null for a degenerate face.
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

            List<Point3D> point3Ds = BoundaryPoints(face3D);
            if (point3Ds == null || point3Ds.Count < 2)
            {
                return null;
            }

            double toleranceSquared = tolerance * tolerance;
            bool found = false;
            double bestLengthSquared = -1, bestMeanZ = double.MaxValue;
            double ux = 0, uy = 0;
            for (int i = 0; i < point3Ds.Count; i++)
            {
                Point3D a = point3Ds[i];
                Point3D b = point3Ds[(i + 1) % point3Ds.Count];
                if (a == null || b == null)
                {
                    continue;
                }

                double dx = b.X - a.X, dy = b.Y - a.Y;
                double lengthSquared = dx * dx + dy * dy;
                if (lengthSquared <= toleranceSquared)
                {
                    continue; // vertical (in-plan degenerate) edge - no plan direction
                }

                double meanZ = 0.5 * (a.Z + b.Z);
                bool better = lengthSquared > bestLengthSquared + toleranceSquared
                    || (System.Math.Abs(lengthSquared - bestLengthSquared) <= toleranceSquared && meanZ < bestMeanZ - tolerance);
                if (!found || better)
                {
                    found = true;
                    bestLengthSquared = lengthSquared;
                    bestMeanZ = meanZ;
                    double length = System.Math.Sqrt(lengthSquared);
                    ux = dx / length;
                    uy = dy / length;
                }
            }

            if (!found)
            {
                return null;
            }

            double minParameter = double.MaxValue, maxParameter = double.MinValue;
            double minX = 0, minY = 0, maxX = 0, maxY = 0;
            foreach (Point3D point3D in point3Ds)
            {
                if (point3D == null)
                {
                    continue;
                }

                double parameter = point3D.X * ux + point3D.Y * uy;
                if (parameter < minParameter) { minParameter = parameter; minX = point3D.X; minY = point3D.Y; }
                if (parameter > maxParameter) { maxParameter = parameter; maxX = point3D.X; maxY = point3D.Y; }
            }

            if (maxParameter - minParameter <= tolerance)
            {
                return null;
            }

            double baseZ = boundingBox3D.Min.Z;
            return new Segment3D(new Point3D(minX, minY, baseZ), new Point3D(maxX, maxY, baseZ));
        }

        /// <summary>
        /// Moves this (vertical) wall's two plan ends onto a new plan foot (the extended/trimmed segment the
        /// plan-loop solver resolved), so a wall can be both lengthened and shortened to meet its junctions.
        /// A plain vertical rectangle is re-extruded onto the new foot byte-identically; any other profile
        /// moves each plan end independently along the wall's plan axis - lengthening via
        /// <see cref="Geometry.Spatial.Query.Extend(Face3D, Plane, double, double)"/> and shortening via
        /// <see cref="Geometry.Spatial.Query.Cut(Face3D, Plane, out List{Face3D}, out List{Face3D}, double)"/>
        /// (keeping the wall-body side) - preserving the top/base profile and the openings. A trim that clips
        /// or drops an opening emits <c>SAM_OCCT_EXTEND3D_HOLE_DROPPED</c> (never silent - R6). No-op when
        /// the new foot is degenerate.
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

            if (IsRectangularHoleFreeVertical(face3D, boundingBox3D, tolerance))
            {
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

                Adopt(extended);
                FastPathExtendCount++;
                return true;
            }

            return SetVerticalFootprintPlaneOps(newStart, newEnd, tolerance);
        }

        // ── E1 plane-ops helpers ─────────────────────────────────────────────────────────────

        /// <summary>Fast-path gate: a plain vertical rectangle (exactly 4 corners, a horizontal bottom edge
        /// at bbox Min.Z, a horizontal top edge at bbox Max.Z, no openings, AND vertical sides - each bottom
        /// corner sits directly under a top corner in plan) is exactly the prismatic wall the legacy
        /// straight-up re-extrude reproduces perfectly, so it takes the byte-identical fast path and the
        /// golden fixtures (whose walls are all such rectangles in the solver's canonical frame) stay frozen.
        /// The vertical-sides requirement is what keeps a TILTED rectangle (whose re-extrude would verticalize
        /// it, dropping its plane - R5) and a shifted-top/sloped parallelogram off the fast path and onto the
        /// profile-preserving plane-ops path.</summary>
        private static bool IsRectangularHoleFreeVertical(Face3D face3D, BoundingBox3D boundingBox3D, double tolerance)
        {
            if ((face3D.GetInternalEdge3Ds()?.Count ?? 0) > 0)
            {
                return false;
            }

            List<Point3D> point3Ds = BoundaryPoints(face3D);
            if (point3Ds == null || point3Ds.Count != 4)
            {
                return false;
            }

            double minZ = boundingBox3D.Min.Z, maxZ = boundingBox3D.Max.Z;
            if (maxZ - minZ <= tolerance)
            {
                return false; // flat - not a wall
            }

            List<Point3D> bottom = new List<Point3D>();
            List<Point3D> top = new List<Point3D>();
            foreach (Point3D point3D in point3Ds)
            {
                if (System.Math.Abs(point3D.Z - minZ) <= tolerance)
                {
                    bottom.Add(point3D);
                }
                else if (System.Math.Abs(point3D.Z - maxZ) <= tolerance)
                {
                    top.Add(point3D);
                }
                else
                {
                    return false; // a corner off the top/bottom rails - not a plain rectangle
                }
            }

            if (bottom.Count != 2 || top.Count != 2)
            {
                return false;
            }

            // Vertical sides: the two bottom plan positions equal the two top plan positions as a set.
            return (PlanClose(bottom[0], top[0], tolerance) && PlanClose(bottom[1], top[1], tolerance))
                || (PlanClose(bottom[0], top[1], tolerance) && PlanClose(bottom[1], top[0], tolerance));
        }

        private static bool PlanClose(Point3D a, Point3D b, double tolerance)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy <= tolerance * tolerance;
        }

        private bool LegacyExtendTop(double targetZ, BoundingBox3D boundingBox3D, double tolerance)
        {
            Segment3D baseSegment3D = LegacyBaseSegment(tolerance);
            if (baseSegment3D == null)
            {
                return false;
            }

            Face3D extended = Geometry.Spatial.Create.Face3D(baseSegment3D, new Vector3D(0, 0, targetZ - boundingBox3D.Min.Z));
            if (extended == null || !extended.IsValid())
            {
                return false;
            }

            Adopt(extended);
            FastPathExtendCount++;
            return true;
        }

        private bool LegacyExtendBottom(double targetZ, BoundingBox3D boundingBox3D, double tolerance)
        {
            Segment3D baseSegment3D = LegacyBaseSegment(tolerance);
            if (baseSegment3D == null)
            {
                return false;
            }

            Point3D start = baseSegment3D.GetStart();
            Point3D end = baseSegment3D.GetEnd();
            Segment3D loweredSegment3D = new Segment3D(
                new Point3D(start.X, start.Y, targetZ),
                new Point3D(end.X, end.Y, targetZ));

            Face3D extended = Geometry.Spatial.Create.Face3D(loweredSegment3D, new Vector3D(0, 0, boundingBox3D.Max.Z - targetZ));
            if (extended == null || !extended.IsValid())
            {
                return false;
            }

            Adopt(extended);
            FastPathExtendCount++;
            return true;
        }

        /// <summary>The frozen legacy foot recovery (a horizontal cut just above the base) used ONLY by the
        /// rectangular fast path, so its byte-identical output is insulated from the public
        /// <see cref="GetBaseSegment"/>'s R7 change.</summary>
        private Segment3D LegacyBaseSegment(double tolerance)
        {
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

        /// <summary>Extends the face to <paramref name="targetPlane"/> in its own plane, preserving profile,
        /// openings and supporting plane. Records a hole-drop diagnostic if an opening is lost (never happens
        /// for a union-only extend, but checked for safety).</summary>
        private bool ExtendToPlane(Plane targetPlane, double tolerance)
        {
            int holesBefore = face3D.GetInternalEdge3Ds()?.Count ?? 0;
            Face3D extended = face3D.Extend(targetPlane, Core.Tolerance.Angle, tolerance);
            if (extended == null || !extended.IsValid())
            {
                return false;
            }

            RecordHoleDrop(holesBefore, extended, "extend");
            Adopt(extended);
            PlaneOpsExtendCount++;
            return true;
        }

        /// <summary>
        /// E2 core (shared by <see cref="ExtendTopToPlane"/>/<see cref="ExtendBottomToPlane"/>).
        /// <paramref name="up"/> selects the grow direction: true extends the top upward to a roof/ceiling,
        /// false extends the base downward to a floor. <paramref name="capExtremeZ"/> is the cap's REAL
        /// extreme elevation (bbox Max.Z above / Min.Z below), the clamp that bounds the sloped target.
        /// </summary>
        /// <remarks>
        /// E2 review rewrite: the first implementation reused <c>Query.Extend</c> (extend to the offset
        /// plane's intersection line via extreme perpendicular projections). That construction is correct
        /// ONLY for a horizontal target line (E1's use): on an INCLINED line the perpendicular is oblique, so
        /// the union (a) spills sideways in plan past the wall's ends - lengthening the wall into
        /// neighbouring rooms, the room-merge vector - and (b) under-covers the high side as pitch grows (at
        /// 70 degrees the "extension" barely rose above the original top while reporting success). This
        /// version builds the extension explicitly COLUMN-WISE: a polygon spanning exactly the wall's own
        /// plan extent [uMin, uMax], from the anchor edge to the intersection line, with the line CLAMPED at
        /// the cap's real extreme +/- overshoot (so an extrapolated plane from a cap that only clips the
        /// wall's bbox corner cannot drag the wall past what the cap can trim - the pre-E2 scalar bound).
        /// No plan growth, full-span coverage at any pitch, holes preserved.
        /// </remarks>
        private bool ExtendToCapPlane(Plane capPlane, double capExtremeZ, double overshoot, bool up, double tolerance)
        {
            if (face3D == null || plane == null || capPlane == null)
            {
                return false;
            }

            Vector3D capNormal = capPlane.Normal?.Unit;
            Point3D capOrigin = capPlane.Origin;
            if (capNormal == null || capOrigin == null)
            {
                return false;
            }

            BoundingBox3D boundingBox3D = face3D.GetBoundingBox();
            if (boundingBox3D == null)
            {
                return false;
            }

            // A (near) horizontal cap is a flat floor/ceiling: route to the scalar path so a plain rectangular
            // wall keeps the byte-identical rectangular fast path (the golden fixtures' flat levels) and any
            // other profile takes E1's extend-to-horizontal-plane. capOrigin.Z is the flat cap's elevation, so
            // the scalar target reproduces E1 (nearestCap.Max.Z +/- overshoot) exactly.
            double absNz = System.Math.Abs(capNormal.Z);
            if (absNz >= 1.0 - 1e-6)
            {
                double horizontalTargetZ = up ? capOrigin.Z + overshoot : capOrigin.Z - overshoot;
                return up ? ExtendTopTo(horizontalTargetZ, tolerance) : ExtendBottomTo(horizontalTargetZ, tolerance);
            }

            // A (near) vertical cap plane has no meaningful surface directly above/below the wall and gives a
            // near-parallel, unstable intersection with a vertical wall: nothing to extend to.
            if (absNz <= 1e-6)
            {
                return false;
            }

            // The wall's in-plane axes: u (horizontal, along the wall in plan) and w (in-plane up). Degenerate
            // u (a horizontal wall - not a wall) or degenerate w falls back to the scalar path.
            Vector3D wallNormal = plane.Normal?.Unit;
            if (wallNormal == null)
            {
                return false;
            }

            Vector3D uAxis = new Vector3D(-wallNormal.Y, wallNormal.X, 0);
            if (uAxis.Length <= 1e-9)
            {
                return ExtendToCapScalarFallback(capNormal, capOrigin, boundingBox3D, overshoot, up, tolerance);
            }

            uAxis = uAxis.Unit;
            Vector3D wAxis = wallNormal.CrossProduct(uAxis).Unit;
            if (System.Math.Abs(wAxis.Z) <= 1e-9)
            {
                return ExtendToCapScalarFallback(capNormal, capOrigin, boundingBox3D, overshoot, up, tolerance);
            }

            // Offset the cap plane along its own normal so its SURFACE moves in the grow direction (up in
            // world Z for a roof, down for a floor); the vertical shift over a fixed plan point is
            // overshoot/|normal.Z| - the wall overshoots the cap and the native kernel trims it back along
            // the true surface. The clamp below caps the total reach at the cap's real extreme + overshoot.
            double sign = capNormal.Z >= 0 ? 1.0 : -1.0;
            double delta = (up ? overshoot : -overshoot) * sign;
            Point3D offsetOrigin = capOrigin.GetMoved(capNormal * delta) as Point3D;
            if (offsetOrigin == null)
            {
                return false;
            }

            Plane offsetPlane = new Plane(offsetOrigin, capPlane.Normal);

            // Intersection line of the offset cap plane with the wall plane. Parallel (no line) -> scalar.
            PlanarIntersectionResult planarIntersectionResult = Geometry.Spatial.Create.PlanarIntersectionResult(plane, offsetPlane);
            Line3D line3D = planarIntersectionResult == null || !planarIntersectionResult.Intersecting ? null : planarIntersectionResult.GetGeometry3D<Line3D>();
            if (line3D?.Origin == null || line3D.Direction == null)
            {
                return ExtendToCapScalarFallback(capNormal, capOrigin, boundingBox3D, overshoot, up, tolerance);
            }

            // z(u): the line's height over plan-parameter u (u = p . uAxis). A line (near) vertical in the
            // wall plane (cap sloping along the wall's own direction ~ wall-parallel slope) has no usable
            // z-per-u - scalar fallback.
            Vector3D lineDirection = line3D.Direction.Unit;
            double dU = lineDirection.DotProduct(uAxis);
            if (System.Math.Abs(dU) <= 1e-9)
            {
                return ExtendToCapScalarFallback(capNormal, capOrigin, boundingBox3D, overshoot, up, tolerance);
            }

            Point3D lineOrigin = line3D.Origin;
            double lineOriginU = lineOrigin.X * uAxis.X + lineOrigin.Y * uAxis.Y;
            double slope = lineDirection.Z / dU;

            List<Point3D> boundaryPoint3Ds = BoundaryPoints(face3D);
            if (boundaryPoint3Ds == null || boundaryPoint3Ds.Count < 3)
            {
                return false;
            }

            double uMin = double.MaxValue, uMax = double.MinValue;
            foreach (Point3D point3D in boundaryPoint3Ds)
            {
                double u = point3D.X * uAxis.X + point3D.Y * uAxis.Y;
                if (u < uMin) uMin = u;
                if (u > uMax) uMax = u;
            }

            if (uMax - uMin <= tolerance)
            {
                return false;
            }

            double zA = lineOrigin.Z + (uMin - lineOriginU) * slope;
            double zB = lineOrigin.Z + (uMax - lineOriginU) * slope;

            // Diving guard: a cap plane crossing past the wall's OPPOSITE extreme within the wall's own span
            // (a steep roof passing below the base at one end, a floor rising above the top) cannot be a
            // whole-wall sloped target - scalar fallback (grows in the intended direction only).
            if (up ? System.Math.Min(zA, zB) < boundingBox3D.Min.Z + tolerance
                   : System.Math.Max(zA, zB) > boundingBox3D.Max.Z - tolerance)
            {
                return ExtendToCapScalarFallback(capNormal, capOrigin, boundingBox3D, overshoot, up, tolerance);
            }

            // Clamp at the cap's REAL extreme + overshoot: beyond it there is no cap surface to trim against,
            // so following the extrapolated plane further would build a phantom wall (E2 review finding A1).
            double clampZ = up ? capExtremeZ + overshoot : capExtremeZ - overshoot;
            double zAClamped = up ? System.Math.Min(zA, clampZ) : System.Math.Max(zA, clampZ);
            double zBClamped = up ? System.Math.Min(zB, clampZ) : System.Math.Max(zB, clampZ);

            // No-grow: the (clamped) target does not clear the wall's extreme anywhere on the span - the
            // conforming case, or a failed/afield target. The scalar path decides (it no-ops when the cap
            // surface over the wall centre does not clear the extreme either) - never a silent swallow.
            if (up ? System.Math.Max(zAClamped, zBClamped) <= boundingBox3D.Max.Z + tolerance
                   : System.Math.Min(zAClamped, zBClamped) >= boundingBox3D.Min.Z - tolerance)
            {
                return ExtendToCapScalarFallback(capNormal, capOrigin, boundingBox3D, overshoot, up, tolerance);
            }

            // The extension polygon, column-wise over exactly [uMin, uMax]: anchored at the wall's opposite
            // extreme, rising (dropping) to the clamped line - with the clamp crossing vertex when the line
            // pierces the clamp inside the span.
            double anchorZ = up ? boundingBox3D.Min.Z : boundingBox3D.Max.Z;
            List<Point3D> extension3Ds = new List<Point3D>
            {
                InPlanePoint(uAxis, wAxis, uMin, anchorZ),
                InPlanePoint(uAxis, wAxis, uMax, anchorZ),
                InPlanePoint(uAxis, wAxis, uMax, zBClamped)
            };

            bool crossesClamp = (zA - clampZ) * (zB - clampZ) < 0 && System.Math.Abs(zB - zA) > 1e-12;
            if (crossesClamp)
            {
                double uStar = uMin + (clampZ - zA) * (uMax - uMin) / (zB - zA);
                extension3Ds.Add(InPlanePoint(uAxis, wAxis, uStar, clampZ));
            }

            extension3Ds.Add(InPlanePoint(uAxis, wAxis, uMin, zAClamped));

            // Union the extension with the existing boundary in the wall plane (holes carried through).
            List<Geometry.Planar.Point2D> extension2Ds = extension3Ds.ConvertAll(x => plane.Convert(x));
            Geometry.Planar.ISegmentable2D externalEdge2D = face3D.ExternalEdge2D as Geometry.Planar.ISegmentable2D;
            if (externalEdge2D == null || extension2Ds.Any(x => x == null))
            {
                return ExtendToCapScalarFallback(capNormal, capOrigin, boundingBox3D, overshoot, up, tolerance);
            }

            List<Geometry.Planar.Polygon2D> polygon2Ds = Geometry.Planar.Query.Union(new List<Geometry.Planar.Polygon2D>
            {
                new Geometry.Planar.Polygon2D(externalEdge2D.GetPoints()),
                new Geometry.Planar.Polygon2D(extension2Ds)
            }, tolerance);

            if (polygon2Ds == null || polygon2Ds.Count == 0)
            {
                return ExtendToCapScalarFallback(capNormal, capOrigin, boundingBox3D, overshoot, up, tolerance);
            }

            if (polygon2Ds.Count > 1)
            {
                polygon2Ds.Sort((x, y) => y.GetArea().CompareTo(x.GetArea()));
            }

            Geometry.Planar.Polygon2D polygon2D = polygon2Ds[0];
            polygon2D.SetOrientation(Geometry.Planar.Query.Orientation(externalEdge2D.GetPoints()));

            int holesBefore = face3D.GetInternalEdge3Ds()?.Count ?? 0;
            Face3D extended = Face3D.Create(plane, polygon2D, face3D.InternalEdge2Ds);
            if (extended == null || !extended.IsValid())
            {
                return ExtendToCapScalarFallback(capNormal, capOrigin, boundingBox3D, overshoot, up, tolerance);
            }

            BoundingBox3D extendedBox = extended.GetBoundingBox();
            if (extendedBox == null)
            {
                return false;
            }

            // Belt-and-braces: by construction the extension spans only [uMin, uMax] from the anchor to the
            // clamped line, so the opposite extreme and the plan extent are preserved; verify and refuse a
            // degenerate union rather than adopting it.
            if (up ? extendedBox.Min.Z < boundingBox3D.Min.Z - tolerance : extendedBox.Max.Z > boundingBox3D.Max.Z + tolerance)
            {
                return ExtendToCapScalarFallback(capNormal, capOrigin, boundingBox3D, overshoot, up, tolerance);
            }

            double grewTo = up ? extendedBox.Max.Z : extendedBox.Min.Z;
            double from = up ? boundingBox3D.Max.Z : boundingBox3D.Min.Z;
            if (up ? grewTo <= from + tolerance : grewTo >= from - tolerance)
            {
                return ExtendToCapScalarFallback(capNormal, capOrigin, boundingBox3D, overshoot, up, tolerance);
            }

            RecordHoleDrop(holesBefore, extended, up ? "extend to roof plane" : "extend to floor plane");
            Adopt(extended);
            PlaneOpsExtendCount++;
            return true;
        }

        /// <summary>A point in this panel's plane with plan-parameter <paramref name="u"/> (its projection on
        /// <paramref name="uAxis"/>) and world elevation <paramref name="z"/> - the column-wise coordinates
        /// <see cref="ExtendToCapPlane"/> builds its extension polygon from. <paramref name="wAxis"/> is the
        /// in-plane up axis (non-degenerate Z-component guaranteed by the caller).</summary>
        private Point3D InPlanePoint(Vector3D uAxis, Vector3D wAxis, double u, double z)
        {
            Point3D origin = plane.Origin;
            double a = u - (origin.X * uAxis.X + origin.Y * uAxis.Y);
            double b = (z - origin.Z) / wAxis.Z;
            return new Point3D(
                origin.X + uAxis.X * a + wAxis.X * b,
                origin.Y + uAxis.Y * a + wAxis.Y * b,
                origin.Z + wAxis.Z * b);
        }

        /// <summary>Scalar-Z fallback shared by the parallel-cap and base/top-preservation guards of
        /// <see cref="ExtendToCapPlane"/>: extend to the cap surface directly over the wall's plan centre
        /// (offset by <paramref name="overshoot"/> in the grow direction), which grows the wall in that
        /// direction only. Mirrors E1's flat-target behaviour for a cap that cannot be used as a sloped plane
        /// target here. Returns false when the cap surface does not actually clear the wall's current extreme.</summary>
        private bool ExtendToCapScalarFallback(Vector3D capNormal, Point3D capOrigin, BoundingBox3D boundingBox3D, double overshoot, bool up, double tolerance)
        {
            if (System.Math.Abs(capNormal.Z) <= 1e-9)
            {
                return false;
            }

            double centreX = 0.5 * (boundingBox3D.Min.X + boundingBox3D.Max.X);
            double centreY = 0.5 * (boundingBox3D.Min.Y + boundingBox3D.Max.Y);
            double capZ = capOrigin.Z - ((capNormal.X * (centreX - capOrigin.X)) + (capNormal.Y * (centreY - capOrigin.Y))) / capNormal.Z;
            double targetZ = up ? capZ + overshoot : capZ - overshoot;
            return up ? ExtendTopTo(targetZ, tolerance) : ExtendBottomTo(targetZ, tolerance);
        }

        private bool SetVerticalFootprintPlaneOps(Geometry.Planar.Point2D newStart, Geometry.Planar.Point2D newEnd, double tolerance)
        {
            double dx = newEnd.X - newStart.X, dy = newEnd.Y - newStart.Y;
            double length = System.Math.Sqrt(dx * dx + dy * dy);
            if (length <= tolerance)
            {
                return false;
            }

            double ux = dx / length, uy = dy / length;
            double tStart = newStart.X * ux + newStart.Y * uy;
            double tEnd = newEnd.X * ux + newEnd.Y * uy;
            double tMin = System.Math.Min(tStart, tEnd), tMax = System.Math.Max(tStart, tEnd);

            bool changed = false;
            if (!MovePlanEnd(ux, uy, tMin, true, tolerance, ref changed))
            {
                return false;
            }

            if (!MovePlanEnd(ux, uy, tMax, false, tolerance, ref changed))
            {
                return false;
            }

            return changed;
        }

        /// <summary>Moves the wall's boundary at one plan end onto plan-parameter <paramref name="targetParameter"/>
        /// along axis (<paramref name="ux"/>, <paramref name="uy"/>). <paramref name="keepGreater"/> is true
        /// for the min end (the retained body lies at parameters &gt; target), false for the max end. Lengthens
        /// via extend-to-plane, shortens via cut-keep-body-side. Returns false only on a hard failure (the
        /// caller then leaves the wall untouched); a successful move sets <paramref name="changed"/>.</summary>
        private bool MovePlanEnd(double ux, double uy, double targetParameter, bool keepGreater, double tolerance, ref bool changed)
        {
            List<Point3D> point3Ds = BoundaryPoints(face3D);
            if (point3Ds == null || point3Ds.Count < 3)
            {
                return false;
            }

            double currentMin = double.MaxValue, currentMax = double.MinValue;
            foreach (Point3D point3D in point3Ds)
            {
                if (point3D == null)
                {
                    continue;
                }

                double parameter = point3D.X * ux + point3D.Y * uy;
                if (parameter < currentMin) currentMin = parameter;
                if (parameter > currentMax) currentMax = parameter;
            }

            double edge = keepGreater ? currentMin : currentMax;
            if (System.Math.Abs(targetParameter - edge) <= tolerance)
            {
                return true; // this end already at the target
            }

            // Vertical plane {p : p·u == targetParameter}; normal is the (horizontal) plan axis.
            Plane plane_Cut = new Plane(new Point3D(ux * targetParameter, uy * targetParameter, 0), new Vector3D(ux, uy, 0));

            bool lengthen = keepGreater ? targetParameter < currentMin : targetParameter > currentMax;
            int holesBefore = face3D.GetInternalEdge3Ds()?.Count ?? 0;

            if (lengthen)
            {
                Face3D extended = face3D.Extend(plane_Cut, Core.Tolerance.Angle, tolerance);
                if (extended == null || !extended.IsValid())
                {
                    return false;
                }

                RecordHoleDrop(holesBefore, extended, "footprint extend");
                Adopt(extended);
                PlaneOpsExtendCount++;
                changed = true;
                return true;
            }

            List<Face3D> pieces = face3D.Cut(plane_Cut, out List<Face3D> face3Ds_Above, out List<Face3D> face3Ds_Below, tolerance);
            if (pieces == null)
            {
                return false;
            }

            // above = +normal (=+u) side = parameters > target = the body for the min end; below for the max end.
            Face3D best = LargestValid(keepGreater ? face3Ds_Above : face3Ds_Below);
            if (best == null)
            {
                return false;
            }

            RecordHoleDrop(holesBefore, best, "footprint trim");
            Adopt(best);
            PlaneOpsExtendCount++;
            changed = true;
            return true;
        }

        private static Face3D LargestValid(List<Face3D> face3Ds)
        {
            Face3D best = null;
            double bestArea = double.MinValue;
            foreach (Face3D face3D in face3Ds ?? new List<Face3D>())
            {
                if (face3D == null || !face3D.IsValid())
                {
                    continue;
                }

                double area = face3D.GetArea();
                if (area > bestArea)
                {
                    bestArea = area;
                    best = face3D;
                }
            }

            return best;
        }

        private void Adopt(Face3D newFace3D)
        {
            face3D = newFace3D;
            plane = newFace3D.GetPlane();
        }

        private void RecordHoleDrop(int holesBefore, Face3D result, string operation)
        {
            int holesAfter = result.GetInternalEdge3Ds()?.Count ?? 0;
            if (holesAfter >= holesBefore)
            {
                return;
            }

            HoleDroppedCount += holesBefore - holesAfter;
            extendDiagnostics.Add(string.Format(
                "SAM_OCCT_EXTEND3D_HOLE_DROPPED: {0} of {1} internal opening(s) dropped or clipped to the boundary during {2}.",
                holesBefore - holesAfter, holesBefore, operation));
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

        /// <summary>True when the two boxes overlap in the XY (plan) projection after both are grown by
        /// <paramref name="expand"/> - used to decide whether a wall borders a cap in plan within reach.</summary>
        private static bool OverlapsInPlanExpanded(BoundingBox3D a, BoundingBox3D b, double expand)
        {
            if (a == null || b == null)
            {
                return false;
            }

            return a.Min.X - expand <= b.Max.X && a.Max.X + expand >= b.Min.X
                && a.Min.Y - expand <= b.Max.Y && a.Max.Y + expand >= b.Min.Y;
        }
    }
}
