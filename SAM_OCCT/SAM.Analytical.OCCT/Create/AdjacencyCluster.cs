// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.Analytical.OCCT
{
    public static partial class Create
    {
        public static AdjacencyCluster AdjacencyCluster(IEnumerable<Space> spaces, IEnumerable<Panel> panels, out OcctCellComplexResult cellComplexResult, Log log = null, OcctBuildOptions options = null)
        {
            cellComplexResult = null;

            List<Shell> shells = Geometry.OCCT.Create.CellComplexByPanels(panels, out cellComplexResult, options);
            if (shells == null || shells.Count == 0)
            {
                if (log != null && cellComplexResult?.Diagnostics != null)
                {
                    foreach (OcctDiagnostic diagnostic in cellComplexResult.Diagnostics)
                    {
                        Core.Modify.Add(log, diagnostic.ToString());
                    }
                }

                return null;
            }

            if (log != null)
            {
                Core.Modify.Add(log, "OCCT created {0} closed shell(s). Analytical rebuild is pending migration from SAM_Topologic.", shells.Count);
            }

            return null;
        }
    }
}
