// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OCCT.Solver
{
    /// <summary>
    /// Two distinct panel-contribution checks (docs/CONTROLLED_WORKFLOW_PLAN.md §6): which GENERATED
    /// cluster panels bound no space (<see cref="OrphanClusterPanels"/>), and which SUPPLIED input panels
    /// contributed no geometry to any generated cluster face (<see cref="UnusedInputPanels"/>). The two are
    /// deliberately different tests - the builder fabricates new panel identities during the rebuild, so a
    /// supplied input panel's own Guid never appears among the cluster's panels and
    /// <c>cluster.GetSpaces(inputPanel)</c> cannot measure its contribution; <see cref="UnusedInputPanels"/>
    /// instead uses a geometric coplanar-overlap test, mirroring the P0 baseline harness's own approximation.
    /// </summary>
    public static class PanelContributionFinder
    {
        private const double DefaultCoplanarTolerance = 0.02;
        private const double DefaultBoundingBoxMargin = 0.05;

        /// <summary>Generated cluster panels related to zero spaces (<c>cluster.GetSpaces(panel)</c> empty).</summary>
        public static List<Panel> OrphanClusterPanels(AdjacencyCluster adjacencyCluster)
        {
            List<Panel> result = new List<Panel>();
            List<Panel> clusterPanels = adjacencyCluster?.GetPanels();
            if (clusterPanels == null)
            {
                return result;
            }

            foreach (Panel panel in clusterPanels)
            {
                if (panel == null)
                {
                    continue;
                }

                if ((adjacencyCluster.GetSpaces(panel)?.Count ?? 0) == 0)
                {
                    result.Add(panel);
                }
            }

            return result;
        }

        /// <summary>Supplied source panels with no coplanar overlapping contribution to any generated cluster face.</summary>
        public static List<Panel> UnusedInputPanels(IEnumerable<Panel> inputPanels, IEnumerable<Panel> clusterPanels, double coplanarTolerance = DefaultCoplanarTolerance, double boundingBoxMargin = DefaultBoundingBoxMargin)
        {
            List<Panel> result = new List<Panel>();
            List<Panel> clusterList = (clusterPanels ?? Enumerable.Empty<Panel>()).Where(x => x?.GetFace3D() != null).ToList();

            foreach (Panel panel in inputPanels ?? Enumerable.Empty<Panel>())
            {
                if (panel?.GetFace3D() == null)
                {
                    continue;
                }

                if (!clusterList.Any(clusterPanel => IsCoplanarOverlap(panel, clusterPanel, coplanarTolerance, boundingBoxMargin)))
                {
                    result.Add(panel);
                }
            }

            return result;
        }

        /// <summary>The input panel's plane coincides with the cluster panel's (within <paramref name="coplanarTolerance"/> distance / ~2 deg) and their bounding boxes overlap (expanded by <paramref name="boundingBoxMargin"/>).</summary>
        private static bool IsCoplanarOverlap(Panel inputPanel, Panel clusterPanel, double coplanarTolerance, double boundingBoxMargin)
        {
            Face3D inputFace3D = inputPanel?.GetFace3D();
            Face3D clusterFace3D = clusterPanel?.GetFace3D();
            Plane inputPlane = inputFace3D?.GetPlane();
            Plane clusterPlane = clusterFace3D?.GetPlane();
            if (inputPlane == null || clusterPlane == null)
            {
                return false;
            }

            if (System.Math.Abs(inputPlane.Normal.Unit.DotProduct(clusterPlane.Normal.Unit)) < 0.9994)
            {
                return false;
            }

            Vector3D normal = inputPlane.Normal.Unit;
            double offset = normal.X * (clusterPlane.Origin.X - inputPlane.Origin.X)
                + normal.Y * (clusterPlane.Origin.Y - inputPlane.Origin.Y)
                + normal.Z * (clusterPlane.Origin.Z - inputPlane.Origin.Z);
            if (System.Math.Abs(offset) > coplanarTolerance)
            {
                return false;
            }

            BoundingBox3D inputBox = inputFace3D.GetBoundingBox();
            BoundingBox3D clusterBox = clusterFace3D.GetBoundingBox();
            if (inputBox == null || clusterBox == null)
            {
                return false;
            }

            return inputBox.Min.X <= clusterBox.Max.X + boundingBoxMargin && clusterBox.Min.X <= inputBox.Max.X + boundingBoxMargin
                && inputBox.Min.Y <= clusterBox.Max.Y + boundingBoxMargin && clusterBox.Min.Y <= inputBox.Max.Y + boundingBoxMargin
                && inputBox.Min.Z <= clusterBox.Max.Z + boundingBoxMargin && clusterBox.Min.Z <= inputBox.Max.Z + boundingBoxMargin;
        }
    }
}
