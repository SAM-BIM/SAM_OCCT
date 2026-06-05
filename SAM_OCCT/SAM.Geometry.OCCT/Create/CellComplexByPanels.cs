// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    public static partial class Create
    {
        public static List<Shell> CellComplexByPanels(IEnumerable<Panel> panels, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            if (panels == null || !panels.Any())
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_INPUT_EMPTY", "No SAM panels were supplied.");
                return null;
            }

            List<Face3D> face3Ds = new List<Face3D>();
            int index = 0;
            foreach (Panel panel in panels)
            {
                Face3D face3D = panel?.GetFace3D();
                if (face3D == null)
                {
                    result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_PANEL_NO_FACE", "Panel has no Face3D geometry.", index);
                }
                else
                {
                    face3Ds.Add(face3D);
                }

                index++;
            }

            if (face3Ds.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_INPUT_EMPTY", "No panel Face3D geometry could be extracted.");
                return null;
            }

            return Shells(face3Ds, out result, options);
        }
    }
}
