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
        public static List<Shell> ShellsUnion(IEnumerable<Shell> shells, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            List<Shell> shells_Temp = shells?.Where(x => x != null).ToList();
            if (shells_Temp == null || shells_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_UNION_INPUT_EMPTY", "No shells were supplied.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            // RetainTopology routes through the persistent shape handle so the
            // result keeps the live OCCT topology; default keeps the legacy
            // decode-and-free native path.
            bool built = options.RetainTopology
                ? Native.OcctShapeBuilder.TryOperateRetained(Native.OcctShapeBuilder.ShapeOperation.Union, shells_Temp, null, options, 0, result)
                : Native.OcctCellComplexBuilder.TryUnion(shells_Temp, options, result);

            if (!built)
            {
                return null;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_UNION_SUCCESS", string.Format("Created {0} shell(s) after OCCT union.", result.Shells.Count));
            return result.Shells?.ToList();
        }

        public static List<Shell> ShellsUnion(IEnumerable<Shell> shells, out OcctCellComplexResult result, double tolerance = Tolerance.Distance)
        {
            return ShellsUnion(shells, out result, new OcctBuildOptions { Tolerance = tolerance });
        }
    }
}
