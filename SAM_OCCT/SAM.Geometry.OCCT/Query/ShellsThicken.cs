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
        /// Hollows each shell's solid into a genuine wall of the given
        /// <paramref name="thickness"/> (issue #29) - e.g. turning a zone volume
        /// into its enclosing construction / plenum shell. The wall is the
        /// material between the original boundary and a parallel offset surface,
        /// built as (outer solid) - (inner solid) so the result keeps both an
        /// outer face set and an inner cavity (it is NOT just an offset solid).
        /// A positive <paramref name="thickness"/> builds the wall outward from
        /// the boundary, a negative one inward. Returns null on failure - inspect
        /// <paramref name="result"/> diagnostics.
        /// </summary>
        public static List<Shell> ShellsThicken(IEnumerable<Shell> shells, double thickness, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            List<Shell> shells_Temp = shells?.Where(x => x != null).ToList();
            if (shells_Temp == null || shells_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_THICKEN_INPUT_EMPTY", "No shells were supplied.");
                return null;
            }

            if (thickness == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_THICKEN_ZERO", "The wall thickness must be non-zero.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctShapeBuilder.TryOffsetShells(shells_Temp, thickness, true, options, result))
            {
                return null;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_THICKEN_SUCCESS", string.Format("Thickened {0} shell(s) by {1}.", shells_Temp.Count, thickness.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            return result.Shells?.ToList();
        }

        public static List<Shell> ShellsThicken(IEnumerable<Shell> shells, double thickness, out OcctCellComplexResult result, double tolerance = Tolerance.Distance)
        {
            return ShellsThicken(shells, thickness, out result, new OcctBuildOptions { Tolerance = tolerance });
        }
    }
}
