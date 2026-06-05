// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    public static partial class Create
    {
        public static List<Shell> Shells(IEnumerable<Face3D> face3Ds, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            if (face3Ds == null || !face3Ds.Any())
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_INPUT_EMPTY", "No Face3D geometry was supplied.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctCellComplexBuilder.TryBuild(face3Ds, options, result))
            {
                return null;
            }

            return result.Shells?.ToList();
        }

        public static List<Shell> Shells(IEnumerable<Face3D> face3Ds, out OcctCellComplexResult result, double tolerance = Tolerance.Distance)
        {
            return Shells(face3Ds, out result, new OcctBuildOptions { Tolerance = tolerance });
        }
    }
}
