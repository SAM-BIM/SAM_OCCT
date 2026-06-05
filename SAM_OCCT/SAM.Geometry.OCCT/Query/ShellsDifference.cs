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
        public static List<Shell> ShellsDifference(IEnumerable<Shell> shells, IEnumerable<Shell> cutterShells, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            List<Shell> shells_Temp = shells?.Where(x => x != null).ToList();
            if (shells_Temp == null || shells_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_DIFFERENCE_TARGET_EMPTY", "No target shells were supplied.");
                return null;
            }

            List<Shell> cutterShells_Temp = cutterShells?.Where(x => x != null).ToList();
            if (cutterShells_Temp == null || cutterShells_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_DIFFERENCE_CUTTER_EMPTY", "No cutter shells were supplied.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctCellComplexBuilder.TryDifference(shells_Temp, cutterShells_Temp, options, result))
            {
                return null;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_DIFFERENCE_SUCCESS", string.Format("Created {0} shell(s) after OCCT difference.", result.Shells.Count));
            return result.Shells?.ToList();
        }

        public static List<Shell> ShellsDifference(IEnumerable<Shell> shells, Shell cutterShell, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            return ShellsDifference(shells, cutterShell == null ? null : new Shell[] { cutterShell }, out result, options);
        }

        public static List<Shell> ShellsDifference(IEnumerable<Shell> shells, IEnumerable<Shell> cutterShells, out OcctCellComplexResult result, double tolerance = Tolerance.Distance)
        {
            return ShellsDifference(shells, cutterShells, out result, new OcctBuildOptions { Tolerance = tolerance });
        }
    }
}
