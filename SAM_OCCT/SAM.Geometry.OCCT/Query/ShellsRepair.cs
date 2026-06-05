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
        public static List<Shell> ShellsRepair(IEnumerable<Shell> shells, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            List<Shell> shells_Temp = shells?.Where(x => x != null).ToList();
            if (shells_Temp == null || shells_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_REPAIR_INPUT_EMPTY", "No shells were supplied.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            List<Shell> resultShells = new List<Shell>();
            for (int i = 0; i < shells_Temp.Count; i++)
            {
                List<Shell> repairedShells = ShellsUnion(new Shell[] { shells_Temp[i] }, out OcctCellComplexResult repairResult, options);
                if (repairResult?.Diagnostics != null)
                {
                    foreach (OcctDiagnostic diagnostic in repairResult.Diagnostics)
                    {
                        result.AddDiagnostic(diagnostic.Severity, diagnostic.Code, diagnostic.Message, i);
                    }
                }

                if (repairedShells != null)
                {
                    resultShells.AddRange(repairedShells);
                }
            }

            if (resultShells.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_REPAIR_FAILED", "OCCT could not repair any supplied shells.");
                return null;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_REPAIR_SUCCESS", string.Format("Repaired {0} input shell(s) into {1} shell(s).", shells_Temp.Count, resultShells.Count));
            return resultShells;
        }

        public static List<Shell> ShellsRepair(IEnumerable<Shell> shells, out OcctCellComplexResult result, double tolerance = Tolerance.Distance)
        {
            return ShellsRepair(shells, out result, new OcctBuildOptions { Tolerance = tolerance });
        }
    }
}
