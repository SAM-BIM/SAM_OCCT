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
        /// Imprints touching shells against one another via OCCT General Fuse
        /// (issue #27). Where one shell's boundary face only partly overlaps a
        /// neighbour's, the face is split into matching sub-faces so each side
        /// has a coincident surface - the "second-level" space boundaries energy
        /// engines (EnergyPlus, IES-VE, TAS) require. Solids stay distinct (this
        /// is not a union), so the decoded result keeps one cell per input shell
        /// and reports the matched sub-faces as FaceAdjacencies. Returns null on
        /// failure - inspect <paramref name="result"/> diagnostics.
        /// </summary>
        public static List<Shell> ShellsImprint(IEnumerable<Shell> shells, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            List<Shell> shells_Temp = shells?.Where(x => x != null).ToList();
            if (shells_Temp == null || shells_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_IMPRINT_INPUT_EMPTY", "No shells were supplied.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctShapeBuilder.TryImprintShells(shells_Temp, options, result))
            {
                return null;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_IMPRINT_SUCCESS", string.Format("Imprinted {0} shell(s) with shared boundaries split for second-level adjacency.", result.Shells.Count));
            return result.Shells?.ToList();
        }

        public static List<Shell> ShellsImprint(IEnumerable<Shell> shells, out OcctCellComplexResult result, double tolerance = Tolerance.Distance)
        {
            return ShellsImprint(shells, out result, new OcctBuildOptions { Tolerance = tolerance });
        }
    }
}
