// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Analytical.OCCT
{
    public static partial class Create
    {
        /// <summary>
        /// Builds a watertight, multi-level <see cref="SAM.Analytical.AdjacencyCluster"/> for a zoned, optionally
        /// twisted tower (issue #12). Each storey contributes four perimeter zones plus a central
        /// core; the storeys are rotated progressively about the vertical axis to form the twist.
        /// The closed tower shells are generated with <c>SAM.Geometry.OCCT.Create.Tower</c> and then
        /// driven through the existing OCCT cell-complex pipeline
        /// (the shell overload of <c>AdjacencyCluster</c>),
        /// which runs <c>BOPAlgo_MakerVolume</c> over the combined faces, detects the shared internal
        /// boundaries, and rebuilds SAM spaces, panels, and space-panel relations. Each space is named
        /// <c>Floor_{level}_Zone_{orientation}</c> with orientation identified from its centroid
        /// (NORTH / EAST / SOUTH / WEST / CORE), so the twist re-labels perimeter zones as they rotate.
        /// </summary>
        /// <param name="height">Total tower height (m).</param>
        /// <param name="twistAngle">Total twist over the full height (radians); 0 gives an untwisted tower.</param>
        /// <param name="floors">Number of storeys (at least 1).</param>
        /// <param name="cellComplexResult">OCCT build result carrying diagnostics, cells, and shared-face relations.</param>
        /// <param name="log">Optional log to receive human-readable progress messages.</param>
        /// <param name="options">OCCT build options; defaults are used when null.</param>
        /// <param name="width">Outer square plan dimension (m). Defaults to 20 m.</param>
        /// <param name="coreInset">Inward offset of the core from the perimeter (m). Defaults to 5 m.</param>
        /// <returns>The tower adjacency cluster, or <c>null</c> when generation or the OCCT build fails (see <paramref name="cellComplexResult"/>).</returns>
        public static AdjacencyCluster Tower(double height, double twistAngle, int floors, out OcctCellComplexResult cellComplexResult, Log log = null, OcctBuildOptions options = null, double width = 20.0, double coreInset = 5.0)
        {
            cellComplexResult = null;

            List<Shell> shells = Geometry.OCCT.Create.Tower(height, twistAngle, floors, width, coreInset);
            if (shells == null || shells.Count == 0)
            {
                cellComplexResult = new OcctCellComplexResult();
                cellComplexResult.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TOWER_INPUT_INVALID", string.Format("Could not generate tower shells for height={0}, twistAngle={1}, floors={2}, width={3}, coreInset={4}.", height, twistAngle, floors, width, coreInset));
                return null;
            }

            double floorHeight = height / floors;
            double coreThreshold = (width * 0.5) - coreInset; // perimeter centroids sit well beyond this; the core centroid is on the axis.

            List<Space> spaces = new List<Space>(shells.Count);
            foreach (Shell shell in shells)
            {
                Point3D location = shell?.GetBoundingBox()?.GetCentroid();
                if (location == null)
                {
                    continue;
                }

                int floorLevel = IdentifyFloorLevel(location, floorHeight, floors);
                string orientation = IdentifyZoneOrientation(location, coreThreshold);
                spaces.Add(new Space(string.Format("Floor_{0}_Zone_{1}", floorLevel, orientation), location));
            }

            return AdjacencyCluster(shells, spaces, out cellComplexResult, log, options);
        }

        /// <summary>
        /// Classifies a zone by the plan-quadrant of its centroid, mirroring issue #12's
        /// <c>IdentifyZoneOrientation</c>. A centroid on the vertical axis is the core; the
        /// surrounding zones are NORTH / EAST / SOUTH / WEST. Because classification is by the
        /// (twisted) centroid, a perimeter zone is re-labelled as the tower rotates.
        /// </summary>
        private static string IdentifyZoneOrientation(Point3D centroid, double coreThreshold)
        {
            double x = centroid.X;
            double y = centroid.Y;

            if (Math.Abs(x) < coreThreshold && Math.Abs(y) < coreThreshold)
            {
                return "CORE";
            }

            if (y >= Math.Abs(x))
            {
                return "NORTH";
            }

            if (x >= Math.Abs(y))
            {
                return "EAST";
            }

            if (y <= -Math.Abs(x))
            {
                return "SOUTH";
            }

            return "WEST";
        }

        /// <summary>
        /// Resolves the storey index from a centroid's height, mirroring issue #12's
        /// <c>IdentifyFloorLevel</c>, clamped to the valid storey range.
        /// </summary>
        private static int IdentifyFloorLevel(Point3D centroid, double floorHeight, int floors)
        {
            if (floorHeight <= 0)
            {
                return 0;
            }

            int level = (int)Math.Floor(centroid.Z / floorHeight);
            if (level < 0)
            {
                return 0;
            }

            return level >= floors ? floors - 1 : level;
        }
    }
}
