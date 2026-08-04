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
        /// List-specific OCCT panel creation overload that applies SAM's canonical
        /// mid-height plan-area calculation to every resulting space.
        /// </summary>
        public static AdjacencyCluster AdjacencyCluster(List<Space> spaces, List<Panel> panels, out OcctCellComplexResult cellComplexResult, Log log = null, OcctBuildOptions options = null, double thinnessRatio = 0.01, double minArea = Tolerance.MacroDistance, double maxDistance = 0.1, double maxAngle = 0.0872664626)
        {
            AdjacencyCluster result = AdjacencyCluster(
                (IEnumerable<Space>)spaces,
                (IEnumerable<Panel>)panels,
                out cellComplexResult,
                log,
                options,
                thinnessRatio,
                minArea,
                maxDistance,
                maxAngle);

            OcctBuildOptions effectiveOptions = options ?? new OcctBuildOptions();
            ApplySpaceAreas(result, effectiveOptions.Tolerance, effectiveOptions.FuzzyTolerance);
            return result;
        }

        /// <summary>
        /// List-specific OCCT shell creation overload used by
        /// SAMOCCT.CreateAdjacencyClusterByShells.
        /// </summary>
        public static AdjacencyCluster AdjacencyCluster(List<Shell> shells, List<Space> spaces, out OcctCellComplexResult cellComplexResult, Log log = null, OcctBuildOptions options = null, IEnumerable<string> names = null, double minArea = Tolerance.MacroDistance, double maxAngle = 0.0872664626)
        {
            AdjacencyCluster result = AdjacencyCluster(
                (IEnumerable<Shell>)shells,
                (IEnumerable<Space>)spaces,
                out cellComplexResult,
                log,
                options,
                names,
                minArea,
                maxAngle);

            OcctBuildOptions effectiveOptions = options ?? new OcctBuildOptions();
            ApplySpaceAreas(result, effectiveOptions.Tolerance, effectiveOptions.FuzzyTolerance);
            return result;
        }

        /// <summary>
        /// List-specific resolved-cell creation overload so direct solver consumers
        /// receive the same SpaceParameter.Area contract as shell-based creation.
        /// </summary>
        public static AdjacencyCluster AdjacencyCluster(List<Panel> panels, ResolvedCellComplex resolvedCellComplex, out List<string> diagnostics, double minArea = Tolerance.MacroDistance, double maxAngle = 0.0872664626, double tolerance = Tolerance.Distance, double fuzzyTolerance = Tolerance.MacroDistance, IEnumerable<int> excludeCellIndices = null)
        {
            AdjacencyCluster result = AdjacencyCluster(
                (IEnumerable<Panel>)panels,
                resolvedCellComplex,
                out diagnostics,
                minArea,
                maxAngle,
                tolerance,
                fuzzyTolerance,
                excludeCellIndices);

            ApplySpaceAreas(result, tolerance, fuzzyTolerance);
            return result;
        }

        private static void ApplySpaceAreas(AdjacencyCluster adjacencyCluster, double toleranceDistance, double toleranceSnap)
        {
            List<Space> spaces = adjacencyCluster?.GetSpaces();
            if (spaces == null)
            {
                return;
            }

            foreach (Space space in spaces)
            {
                Shell shell = space == null ? null : adjacencyCluster.Shell(space);
                BoundingBox3D boundingBox3D = shell?.GetBoundingBox();
                if (boundingBox3D == null || !boundingBox3D.IsValid())
                {
                    continue;
                }

                double height = boundingBox3D.Max.Z - boundingBox3D.Min.Z;
                if (double.IsNaN(height) || double.IsInfinity(height) || height <= toleranceDistance)
                {
                    continue;
                }

                double area = shell.Area(height / 2, Tolerance.Angle, toleranceDistance, toleranceSnap);
                if (double.IsNaN(area) || double.IsInfinity(area) || area <= 0)
                {
                    continue;
                }

                Space updated = new Space(space);
                updated.SetValue(SpaceParameter.Area, area);
                adjacencyCluster.AddObject(updated);
            }
        }
    }
}
