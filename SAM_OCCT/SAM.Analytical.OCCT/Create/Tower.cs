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
        /// Builds a watertight, multi-level <see cref="SAM.Analytical.AdjacencyCluster"/> for a zoned,
        /// optionally twisted tower (issue #12). Each storey contributes four perimeter zones plus a
        /// central core. Only the facade twists; the core walls and the diagonal partition walls are
        /// strictly vertical (fixed at each floor's bottom rotation), so the internal separations come
        /// out as plumb <c>WallInternal</c> panels the way a BEMS model expects. The bounding faces are
        /// generated with <c>SAM.Geometry.OCCT.Create.Tower</c>, wrapped into panels, and driven through
        /// the existing OCCT pipeline (the panel overload of <c>AdjacencyCluster</c>), where
        /// <c>BOPAlgo_MakerVolume</c> carves the cells: the vertical partitions seal at each floor's
        /// bottom profile and poke through the receding twisted facade above it, and MakerVolume trims
        /// the excess. Spaces are seeded per zone as <c>Floor_{level}_Zone_{orientation}</c>
        /// (NORTH / EAST / SOUTH / WEST / CORE), with perimeter orientation taken from the seed
        /// centroid's plan quadrant so the twist re-labels zones as they rotate.
        /// </summary>
        /// <param name="height">Total tower height (m).</param>
        /// <param name="twistAngle">Total facade twist over the full height (radians); 0 gives an untwisted tower.</param>
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

            List<Face3D> face3Ds = Geometry.OCCT.Create.Tower(height, twistAngle, floors, width, coreInset);
            if (face3Ds == null || face3Ds.Count == 0)
            {
                cellComplexResult = new OcctCellComplexResult();
                cellComplexResult.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TOWER_INPUT_INVALID", string.Format("Could not generate tower geometry for height={0}, twistAngle={1}, floors={2}, width={3}, coreInset={4}.", height, twistAngle, floors, width, coreInset));
                return null;
            }

            const double toleranceAngle = 0.0872664626;

            List<Panel> panels = new List<Panel>();
            foreach (Face3D face3D in face3Ds)
            {
                PanelType panelType = Query.PanelType(face3D?.GetPlane()?.Normal, toleranceAngle);
                if (panelType == PanelType.Undefined)
                {
                    panelType = PanelType.Air;
                }

                Panel panel = global::SAM.Analytical.Create.Panel(Query.DefaultConstruction(panelType), panelType, face3D);
                if (panel != null)
                {
                    panels.Add(panel);
                }
            }

            return AdjacencyCluster(SeedSpaces(height, twistAngle, floors, width, coreInset), panels, out cellComplexResult, log, options);
        }

        /// <summary>
        /// One seed space per intended zone, located safely inside its cell for any twist: the
        /// perimeter seeds sit mid-ring at each side's centre (rotated with the floor's bottom
        /// rotation, matching the vertical partitions), which stays inside the rotated facade
        /// because it is within the outer square's apothem; the core seed sits on the axis.
        /// </summary>
        private static List<Space> SeedSpaces(double height, double twistAngle, int floors, double width, double coreInset)
        {
            double heightStep = height / floors;
            double angleStep = twistAngle / floors;
            double halfWidth = width * 0.5;
            double seedRadius = (halfWidth + (halfWidth - coreInset)) * 0.5; // mid-ring, between core and facade

            // Side centres in the same order as the plan edges: NORTH, EAST, SOUTH, WEST at zero twist.
            double[][] sideDirections = { new[] { 0.0, 1.0 }, new[] { 1.0, 0.0 }, new[] { 0.0, -1.0 }, new[] { -1.0, 0.0 } };

            List<Space> result = new List<Space>();
            for (int floor = 0; floor < floors; floor++)
            {
                double z = (floor + 0.5) * heightStep;
                double angle = floor * angleStep; // the floor's bottom rotation - the one its vertical internals use
                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);

                result.Add(new Space(string.Format("Floor_{0}_Zone_CORE", floor), new Point3D(0, 0, z)));

                foreach (double[] direction in sideDirections)
                {
                    double x = (direction[0] * cos - direction[1] * sin) * seedRadius;
                    double y = (direction[0] * sin + direction[1] * cos) * seedRadius;
                    Point3D location = new Point3D(x, y, z);
                    result.Add(new Space(string.Format("Floor_{0}_Zone_{1}", floor, QuadrantOrientation(location)), location));
                }
            }

            return result;
        }

        /// <summary>
        /// Compass quadrant of a perimeter zone centroid, mirroring issue #12's
        /// <c>IdentifyZoneOrientation</c>. Because classification is by the (rotated) centroid,
        /// a perimeter zone is re-labelled as the tower twists.
        /// </summary>
        private static string QuadrantOrientation(Point3D centroid)
        {
            double x = centroid.X;
            double y = centroid.Y;

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
    }
}
