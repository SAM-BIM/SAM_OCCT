// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.OCCT.Solver
{
    public enum SeparatorFindingKind
    {
        /// <summary>A full-height, laterally-covering vertical panel sits strictly between the two locations but did not contribute to the adjacency.</summary>
        Candidate,

        /// <summary>A vertical panel sits strictly between the two locations but only nearly covers the pair laterally (a near-miss).</summary>
        Partial,

        /// <summary>No candidate separator panel was found in the input at all.</summary>
        Absent
    }

    /// <summary>One missing-separator finding for a merged expected pair (docs/CONTROLLED_WORKFLOW_PLAN.md §6).</summary>
    public class SeparatorFinding
    {
        public Guid GuidA { get; }

        public Guid GuidB { get; }

        public string NameA { get; }

        public string NameB { get; }

        /// <summary>The candidate panel's Guid; null for <see cref="SeparatorFindingKind.Absent"/>.</summary>
        public Guid? PanelGuid { get; }

        public SeparatorFindingKind Kind { get; }

        public string Detail { get; }

        public SeparatorFinding(Guid guidA, Guid guidB, string nameA, string nameB, Guid? panelGuid, SeparatorFindingKind kind, string detail)
        {
            GuidA = guidA;
            GuidB = guidB;
            NameA = nameA;
            NameB = nameB;
            PanelGuid = panelGuid;
            Kind = kind;
            Detail = detail ?? string.Empty;
        }
    }

    /// <summary>
    /// For a merged expected pair, scans a set of source panels for a vertical separator strictly between
    /// the two locations (docs/CONTROLLED_WORKFLOW_PLAN.md §6 "Missing-separator analysis"). Mirrors the
    /// P0 baseline harness's <c>ScanSeparator</c> scan, generalised: plane strictly between (signed offsets
    /// opposite), vertical coverage of the pair's combined expected floor-to-ceiling span (not just a tight
    /// band around the two seed elevations - a partial-height fragment cannot bound a room), and lateral
    /// coverage (the pair's midpoint projects inside the face for a full <see cref="SeparatorFindingKind.Candidate"/>,
    /// or within <see cref="SpaceMatchOptions.LateralMargin"/> of the face's plan bounds for a
    /// <see cref="SeparatorFindingKind.Partial"/> near-miss).
    /// </summary>
    public static class SeparatorPanelFinder
    {
        private const double VerticalNormalZ = 0.342; // sin(20 deg) - a wall's |normal.Z| stays below this

        /// <summary>Scans <paramref name="panels"/> for a separator between <paramref name="a"/> and <paramref name="b"/>, using their expected vertical spans (from <see cref="ExpectedSpaceSet.LevelSpans"/>) for the full-height coverage test.</summary>
        public static List<SeparatorFinding> Find(Space a, double[] spanA, Space b, double[] spanB, IReadOnlyList<Panel> panels, SpaceMatchOptions options)
        {
            List<SeparatorFinding> result = new List<SeparatorFinding>();
            Point3D locationA = a?.Location;
            Point3D locationB = b?.Location;
            if (locationA == null || locationB == null || panels == null)
            {
                return result;
            }

            double band = options?.LevelBand ?? 0.21;
            double lateralMargin = options?.LateralMargin ?? 0.5;

            double[] fa = spanA ?? new double[] { locationA.Z - band, locationA.Z + band };
            double[] fb = spanB ?? new double[] { locationB.Z - band, locationB.Z + band };
            double zMin = System.Math.Min(fa[0], fb[0]);
            double zMax = System.Math.Max(fa[1], fb[1]);
            Point3D midpoint = new Point3D((locationA.X + locationB.X) / 2, (locationA.Y + locationB.Y) / 2, (locationA.Z + locationB.Z) / 2);

            List<Panel> candidates = new List<Panel>();
            List<Panel> partials = new List<Panel>();

            foreach (Panel panel in panels)
            {
                Face3D face3D = panel?.GetFace3D();
                Plane plane = face3D?.GetPlane();
                if (plane == null)
                {
                    continue;
                }

                Vector3D normal = plane.Normal.Unit;
                if (System.Math.Abs(normal.Z) > VerticalNormalZ)
                {
                    continue; // not a wall
                }

                double offsetA = SignedOffset(plane, locationA);
                double offsetB = SignedOffset(plane, locationB);
                if (offsetA * offsetB >= -1e-9)
                {
                    continue; // not strictly between the two locations
                }

                BoundingBox3D box = face3D.GetBoundingBox();
                if (box == null || box.Min.Z > zMin + band || box.Max.Z < zMax - band)
                {
                    continue; // does not cover the pair's full expected height
                }

                Point3D projected = plane.Project(midpoint);
                if (projected == null)
                {
                    continue;
                }

                if (face3D.Inside(projected))
                {
                    candidates.Add(panel);
                }
                else if (projected.X >= box.Min.X - lateralMargin && projected.X <= box.Max.X + lateralMargin
                    && projected.Y >= box.Min.Y - lateralMargin && projected.Y <= box.Max.Y + lateralMargin)
                {
                    partials.Add(panel);
                }
            }

            if (candidates.Count == 0 && partials.Count == 0)
            {
                result.Add(new SeparatorFinding(a.Guid, b.Guid, a.Name, b.Name, null, SeparatorFindingKind.Absent,
                    string.Format("Panel should separate {0}|{1} but no candidate panel was found in the input - wall genuinely missing.", a.Name, b.Name)));
            }

            foreach (Panel panel in candidates)
            {
                result.Add(new SeparatorFinding(a.Guid, b.Guid, a.Name, b.Name, panel.Guid, SeparatorFindingKind.Candidate,
                    string.Format("Panel should separate {0}|{1} but did not contribute to adjacency.", a.Name, b.Name)));
            }

            foreach (Panel panel in partials)
            {
                result.Add(new SeparatorFinding(a.Guid, b.Guid, a.Name, b.Name, panel.Guid, SeparatorFindingKind.Partial,
                    string.Format("Partial candidate separator for {0}|{1} (lateral near-miss).", a.Name, b.Name)));
            }

            return result;
        }

        private static double SignedOffset(Plane plane, Point3D point3D)
        {
            Vector3D normal = plane.Normal.Unit;
            return normal.X * (point3D.X - plane.Origin.X) + normal.Y * (point3D.Y - plane.Origin.Y) + normal.Z * (point3D.Z - plane.Origin.Z);
        }
    }
}
