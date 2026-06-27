// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// Minimum distance between two sets of shells via OCCT
        /// BRepExtrema_DistShapeShape (issue #28), with the closest point on
        /// each side. A tolerance-true alternative to bounding-box heuristics
        /// for adjacency detection, watertightness QA (where exactly is the
        /// gap) and clash reporting. A returned distance of 0 means the shells
        /// touch or overlap. Returns null on failure (or when the native library
        /// is unavailable) - inspect <paramref name="result"/> diagnostics.
        /// </summary>
        public static double? ShellsDistance(IEnumerable<Shell> shells, IEnumerable<Shell> otherShells, out OcctCellComplexResult result, out Point3D closestPointOnShells, out Point3D closestPointOnOtherShells, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();
            closestPointOnShells = null;
            closestPointOnOtherShells = null;

            List<Shell> shells_Temp = shells?.Where(x => x != null).ToList();
            if (shells_Temp == null || shells_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_DISTANCE_INPUT_EMPTY", "No shells were supplied.");
                return null;
            }

            List<Shell> otherShells_Temp = otherShells?.Where(x => x != null).ToList();
            if (otherShells_Temp == null || otherShells_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_DISTANCE_OTHER_EMPTY", "No second-set shells were supplied.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctShapeBuilder.TryDistance(shells_Temp, otherShells_Temp, options, result, out double distance, out Point3D pointA, out Point3D pointB))
            {
                return null;
            }

            closestPointOnShells = pointA;
            closestPointOnOtherShells = pointB;
            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_DISTANCE_SUCCESS", string.Format("Minimum OCCT distance between the two shell sets is {0}.", distance.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return distance;
        }

        public static double? ShellsDistance(Shell shell, Shell otherShell, out OcctCellComplexResult result, out Point3D closestPointOnShell, out Point3D closestPointOnOtherShell, OcctBuildOptions options = null)
        {
            return ShellsDistance(
                shell == null ? null : new Shell[] { shell },
                otherShell == null ? null : new Shell[] { otherShell },
                out result,
                out closestPointOnShell,
                out closestPointOnOtherShell,
                options);
        }

        public static double? ShellsDistance(IEnumerable<Shell> shells, IEnumerable<Shell> otherShells, out OcctCellComplexResult result, double tolerance = Tolerance.Distance)
        {
            return ShellsDistance(shells, otherShells, out result, out Point3D _, out Point3D _, new OcctBuildOptions { Tolerance = tolerance });
        }
    }
}
