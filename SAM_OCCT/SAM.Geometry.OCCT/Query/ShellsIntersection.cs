// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    public static partial class Query
    {
        public static List<Shell> ShellsIntersection(IEnumerable<Shell> shells, IEnumerable<Shell> toolShells, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            List<Shell> shells_Temp = shells?.Where(x => x != null).ToList();
            if (shells_Temp == null || shells_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_INTERSECTION_TARGET_EMPTY", "No target shells were supplied.");
                return null;
            }

            List<Shell> toolShells_Temp = toolShells?.Where(x => x != null).ToList();
            if (toolShells_Temp == null || toolShells_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_INTERSECTION_TOOL_EMPTY", "No tool shells were supplied.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctCellComplexBuilder.TryIntersection(shells_Temp, toolShells_Temp, options, result))
            {
                return null;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_INTERSECTION_SUCCESS", string.Format("Created {0} shell(s) after OCCT intersection.", result.Shells.Count));
            return result.Shells?.ToList();
        }

        public static List<Shell> ShellsIntersection(IEnumerable<Shell> shells, Shell toolShell, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            return ShellsIntersection(shells, toolShell == null ? null : new Shell[] { toolShell }, out result, options);
        }

        public static List<Shell> ShellsIntersection(IEnumerable<Shell> shells, IEnumerable<Shell> toolShells, out OcctCellComplexResult result, double tolerance = Tolerance.Distance)
        {
            return ShellsIntersection(shells, toolShells, out result, new OcctBuildOptions { Tolerance = tolerance });
        }
    }
}
