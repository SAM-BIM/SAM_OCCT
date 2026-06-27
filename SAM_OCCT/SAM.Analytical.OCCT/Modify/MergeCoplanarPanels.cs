// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OCCT
{
    public static partial class Modify
    {
        /// <summary>
        /// Merges adjacent coplanar <see cref="Panel"/>s into fewer, larger panels using the OCCT
        /// engine (<c>ShapeUpgrade_UnifySameDomain</c>). Only panels that share the same panel type
        /// and construction are eligible to merge; apertures (windows/doors) are re-hosted onto the
        /// merged panel. Panels on different planes, of different type/construction, or that are not
        /// adjacent are left unchanged.
        /// </summary>
        /// <param name="panels">Panels to merge. Not modified; new merged panels are returned.</param>
        /// <param name="diagnostics">Coded diagnostics describing the merge.</param>
        /// <param name="tolerance">Sewing/linear tolerance.</param>
        /// <param name="angleTolerance">Maximum angle (radians) between face normals still treated as coplanar.</param>
        /// <param name="options">OCCT build options; Tolerance defaults to <paramref name="tolerance"/>.</param>
        /// <returns>The merged panels, or null when no panels were supplied.</returns>
        public static List<Panel> MergeCoplanarPanels(this IEnumerable<Panel> panels, out List<string> diagnostics, double tolerance = Tolerance.Distance, double angleTolerance = Tolerance.Angle, OcctBuildOptions options = null)
        {
            diagnostics = new List<string>();

            List<Panel> panels_Temp = panels?.Where(x => x != null).ToList();
            if (panels_Temp == null || panels_Temp.Count == 0)
            {
                diagnostics.Add("SAM_OCCT_MERGE_PANELS_INPUT_EMPTY: No panels were supplied.");
                return null;
            }

            options = options == null ? new OcctBuildOptions { Tolerance = tolerance } : new OcctBuildOptions(options);

            // Group by panel type + construction only; standalone panels have no space adjacency.
            Dictionary<string, List<Panel>> groups = GroupPanels(panels_Temp, x => TypeConstructionKey(x));

            List<Panel> result = new List<Panel>();
            foreach (KeyValuePair<string, List<Panel>> group in groups)
            {
                result.AddRange(MergeGroup(group.Value, tolerance, angleTolerance, options));
            }

            diagnostics.Add(string.Format("SAM_OCCT_MERGE_PANELS_RESULT: Merged {0} panel(s) into {1} panel(s) across {2} type/construction group(s).", panels_Temp.Count, result.Count, groups.Count));
            return result;
        }

        /// <summary>
        /// Merges adjacent coplanar panels of an <see cref="AdjacencyCluster"/> into fewer, larger
        /// panels using the OCCT engine. Only panels that share the same panel type, construction,
        /// and the same space adjacency are eligible to merge, so the analytical topology is
        /// preserved. Apertures are re-hosted onto the merged panels.
        /// </summary>
        /// <param name="adjacencyCluster">Cluster to clean. Not modified; a new cluster is returned.</param>
        /// <param name="diagnostics">Coded diagnostics describing the merge.</param>
        /// <param name="tolerance">Sewing/linear tolerance.</param>
        /// <param name="angleTolerance">Maximum angle (radians) between face normals still treated as coplanar.</param>
        /// <param name="options">OCCT build options; Tolerance defaults to <paramref name="tolerance"/>.</param>
        /// <returns>A new cleaned cluster, or null when no cluster was supplied.</returns>
        public static AdjacencyCluster MergeCoplanarPanels(this AdjacencyCluster adjacencyCluster, out List<string> diagnostics, double tolerance = Tolerance.Distance, double angleTolerance = Tolerance.Angle, OcctBuildOptions options = null)
        {
            diagnostics = new List<string>();

            if (adjacencyCluster == null)
            {
                diagnostics.Add("SAM_OCCT_MERGE_PANELS_INPUT_NULL: No AdjacencyCluster was supplied.");
                return null;
            }

            List<Panel> panels = adjacencyCluster.GetPanels();
            List<Space> spaces = adjacencyCluster.GetSpaces();
            if (panels == null || panels.Count == 0)
            {
                diagnostics.Add("SAM_OCCT_MERGE_PANELS_INPUT_EMPTY: AdjacencyCluster contains no panels.");
                return new AdjacencyCluster(adjacencyCluster);
            }

            options = options == null ? new OcctBuildOptions { Tolerance = tolerance } : new OcctBuildOptions(options);

            // Group by panel type + construction + the set of spaces the panel separates, so two
            // panels only merge when they are the same construction AND bound the same spaces.
            Dictionary<string, List<Panel>> groups = new Dictionary<string, List<Panel>>();
            Dictionary<string, List<Guid>> groupSpaceGuids = new Dictionary<string, List<Guid>>();
            foreach (Panel panel in panels.Where(x => x != null))
            {
                List<Space> panelSpaces = adjacencyCluster.GetSpaces(panel) ?? new List<Space>();
                List<Guid> spaceGuids = panelSpaces.Where(x => x != null).Select(x => x.Guid).Distinct().OrderBy(x => x).ToList();
                string key = TypeConstructionKey(panel) + "|" + string.Join(",", spaceGuids);

                if (!groups.TryGetValue(key, out List<Panel> list))
                {
                    list = new List<Panel>();
                    groups[key] = list;
                    groupSpaceGuids[key] = spaceGuids;
                }

                list.Add(panel);
            }

            // Rebuild the cluster: copy spaces, then add merged panels related to the same spaces.
            AdjacencyCluster result = new AdjacencyCluster();
            Dictionary<Guid, Space> spaceByGuid = new Dictionary<Guid, Space>();
            foreach (Space space in (spaces ?? new List<Space>()).Where(x => x != null))
            {
                Space newSpace = new Space(space, space.Name, space.Location);
                if (space.TryGetValue(SpaceParameter.Volume, out double volume) && !double.IsNaN(volume))
                {
                    newSpace.SetValue(SpaceParameter.Volume, volume);
                }

                result.AddObject(newSpace);
                spaceByGuid[space.Guid] = newSpace;
            }

            int mergedPanelCount = 0;
            foreach (KeyValuePair<string, List<Panel>> group in groups)
            {
                List<Panel> mergedPanels = MergeGroup(group.Value, tolerance, angleTolerance, options);
                List<Guid> spaceGuids = groupSpaceGuids[group.Key];

                foreach (Panel panel in mergedPanels)
                {
                    result.AddObject(panel);
                    mergedPanelCount++;

                    foreach (Guid spaceGuid in spaceGuids)
                    {
                        if (spaceByGuid.TryGetValue(spaceGuid, out Space newSpace))
                        {
                            result.AddRelation(newSpace, panel);
                        }
                    }
                }
            }

            // Keep normals consistent with the rebuilt relations; do NOT reset panel types or
            // constructions, so the merged panels keep the type/construction they were grouped by.
            result = result.UpdateNormals(false, true, false, options.FuzzyTolerance, options.Tolerance);
            result.Normalize(false);

            diagnostics.Add(string.Format(
                "SAM_OCCT_MERGE_PANELS_RESULT: Merged {0} panel(s) into {1} panel(s) across {2} type/construction/adjacency group(s); {3} space(s).",
                panels.Count,
                mergedPanelCount,
                groups.Count,
                result.GetSpaces()?.Count ?? 0));

            return result;
        }

        private static Dictionary<string, List<Panel>> GroupPanels(IEnumerable<Panel> panels, Func<Panel, string> keySelector)
        {
            Dictionary<string, List<Panel>> result = new Dictionary<string, List<Panel>>();
            foreach (Panel panel in panels.Where(x => x != null))
            {
                string key = keySelector(panel);
                if (!result.TryGetValue(key, out List<Panel> list))
                {
                    list = new List<Panel>();
                    result[key] = list;
                }

                list.Add(panel);
            }

            return result;
        }

        private static string TypeConstructionKey(Panel panel)
        {
            string panelType = panel.PanelType.ToString();
            string construction = panel.Construction?.Name ?? "<none>";
            return panelType + "|" + construction;
        }

        private static List<Panel> MergeGroup(List<Panel> groupPanels, double tolerance, double angleTolerance, OcctBuildOptions options)
        {
            // Nothing to merge across a single panel; keep the original (with its apertures) intact.
            List<Face3D> face3Ds = groupPanels.Select(x => x?.GetFace3D()).Where(x => x != null).ToList();
            if (face3Ds.Count < 2)
            {
                return new List<Panel>(groupPanels);
            }

            List<Face3D> mergedFace3Ds = Geometry.OCCT.Query.MergeCoplanarFace3Ds(face3Ds, out OcctCellComplexResult result, angleTolerance, options);

            // No merge happened (or native OCCT unavailable): keep the original panels untouched.
            if (mergedFace3Ds == null || mergedFace3Ds.Count == 0 || mergedFace3Ds.Count >= face3Ds.Count)
            {
                return new List<Panel>(groupPanels);
            }

            PanelType panelType = groupPanels[0].PanelType;
            Construction construction = groupPanels[0].Construction;

            List<Panel> mergedPanels = new List<Panel>();
            foreach (Face3D mergedFace3D in mergedFace3Ds)
            {
                Panel panel = global::SAM.Analytical.Create.Panel(construction, panelType, mergedFace3D);
                if (panel == null)
                {
                    continue;
                }

                // Re-host apertures from the original panels that fall on this merged face.
                foreach (Panel original in groupPanels)
                {
                    Face3D originalFace3D = original?.GetFace3D();
                    if (originalFace3D == null || !MapsTo(originalFace3D, mergedFace3D, tolerance))
                    {
                        continue;
                    }

                    List<Aperture> apertures = original.Apertures;
                    if (apertures == null)
                    {
                        continue;
                    }

                    foreach (Aperture aperture in apertures.Where(x => x != null))
                    {
                        panel.AddAperture(aperture);
                    }
                }

                mergedPanels.Add(panel);
            }

            return mergedPanels.Count == 0 ? new List<Panel>(groupPanels) : mergedPanels;
        }

        private static bool MapsTo(Face3D originalFace3D, Face3D mergedFace3D, double tolerance)
        {
            Plane planeA = originalFace3D.GetPlane();
            Plane planeB = mergedFace3D.GetPlane();
            if (planeA == null || planeB == null)
            {
                return false;
            }

            Vector3D normalA = planeA.Normal;
            Vector3D normalB = planeB.Normal;
            if (normalA == null || normalB == null)
            {
                return false;
            }

            // Parallel planes only (coplanar group members can include parallel offsets; the
            // bounding-box test below separates those by their differing offset).
            double dot = Math.Abs((normalA.X * normalB.X) + (normalA.Y * normalB.Y) + (normalA.Z * normalB.Z));
            if (dot < 0.99)
            {
                return false;
            }

            BoundingBox3D boundingBox3D = mergedFace3D.GetBoundingBox();
            Point3D centroid = originalFace3D.GetBoundingBox()?.GetCentroid();
            if (boundingBox3D == null || centroid == null)
            {
                return false;
            }

            Point3D min = boundingBox3D.Min;
            Point3D max = boundingBox3D.Max;
            return centroid.X >= min.X - tolerance && centroid.X <= max.X + tolerance
                && centroid.Y >= min.Y - tolerance && centroid.Y <= max.Y + tolerance
                && centroid.Z >= min.Z - tolerance && centroid.Z <= max.Z + tolerance;
        }
    }
}
