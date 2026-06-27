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
        /// <summary>
        /// Offsets each shell's skin outward (positive <paramref name="offset"/>)
        /// or inward (negative) via OCCT BRepOffsetAPI_MakeOffsetShape (issue
        /// #29) - the centre-line-to-physical-face move for energy models.
        /// Offsetting is the most failure-prone OCCT operation; on failure the
        /// result carries a diagnostic and null is returned. Inspect
        /// <paramref name="result"/> diagnostics.
        /// </summary>
        public static List<Shell> ShellsOffset(IEnumerable<Shell> shells, double offset, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            List<Shell> shells_Temp = shells?.Where(x => x != null).ToList();
            if (shells_Temp == null || shells_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_OFFSET_INPUT_EMPTY", "No shells were supplied.");
                return null;
            }

            if (offset == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_OFFSET_ZERO", "The offset distance must be non-zero.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctShapeBuilder.TryOffsetShells(shells_Temp, offset, false, options, result))
            {
                return null;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_OFFSET_SUCCESS", string.Format("Offset {0} shell(s) by {1}.", shells_Temp.Count, offset.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return result.Shells?.ToList();
        }

        public static List<Shell> ShellsOffset(IEnumerable<Shell> shells, double offset, out OcctCellComplexResult result, double tolerance = Tolerance.Distance)
        {
            return ShellsOffset(shells, offset, out result, new OcctBuildOptions { Tolerance = tolerance });
        }
    }
}
