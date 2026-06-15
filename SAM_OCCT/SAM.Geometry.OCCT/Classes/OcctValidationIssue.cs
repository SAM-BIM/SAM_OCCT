// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;

namespace SAM.Geometry.OCCT
{
    /// <summary>
    /// A single located, categorised validity / watertightness issue from the
    /// native validator (issue #37 follow-on) - e.g. a naked edge with its
    /// midpoint and gap length, or a self-intersection with its location. Unlike
    /// an opaque status code this points the investigation at the exact geometry.
    /// </summary>
    public class OcctValidationIssue
    {
        public OcctValidationIssueCategory Category { get; }

        /// <summary>
        /// A representative location of the issue (a naked edge midpoint, a face
        /// centroid, or the bounding-box centre of a self-intersection).
        /// </summary>
        public Point3D Location { get; }

        /// <summary>
        /// A size metric whose meaning depends on <see cref="Category"/>: the gap
        /// length for <see cref="OcctValidationIssueCategory.NakedEdge"/>, the
        /// face area for <see cref="OcctValidationIssueCategory.InvalidFace"/> /
        /// <see cref="OcctValidationIssueCategory.SmallFace"/>, otherwise 0.
        /// </summary>
        public double Size { get; }

        public OcctValidationIssue(OcctValidationIssueCategory category, Point3D location, double size)
        {
            Category = category;
            Location = location;
            Size = size;
        }

        public override string ToString()
        {
            string location = Location == null
                ? "(unknown)"
                : string.Format("({0:0.###}, {1:0.###}, {2:0.###})", Location.X, Location.Y, Location.Z);

            switch (Category)
            {
                case OcctValidationIssueCategory.NakedEdge:
                    return string.Format("Naked edge of length {0:0.####} m at {1}", Size, location);
                case OcctValidationIssueCategory.SelfIntersection:
                    return string.Format("Self-intersection near {0}", location);
                case OcctValidationIssueCategory.InvalidFace:
                    return string.Format("Invalid face of area {0:0.######} m² at {1}", Size, location);
                case OcctValidationIssueCategory.SmallFace:
                    return string.Format("Small (sliver) face of area {0:0.######} m² at {1}", Size, location);
                case OcctValidationIssueCategory.SmallEdge:
                    return string.Format("Small / degenerate edge near {0}", location);
                default:
                    return string.Format("{0} near {1}", Category, location);
            }
        }
    }
}
