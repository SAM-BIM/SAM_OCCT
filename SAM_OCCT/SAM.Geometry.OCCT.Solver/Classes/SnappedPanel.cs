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
