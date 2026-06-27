// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    public static partial class Export
    {
        /// <summary>
        /// Exports a live OCCT topology to a STEP or IGES file (issue #20). The
        /// topology handle stays valid and caller-owned. Returns true on
        /// success; inspect <paramref name="result"/> diagnostics
        /// (SAM_OCCT_EXPORT_*) either way.
        /// </summary>
        public static bool ToFile(OcctTopology topology, string path, OcctExchangeFormat format, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            return Native.OcctShapeBuilder.TryExport(topology, path, format, result);
        }

        /// <summary>
        /// Builds a native OCCT topology from SAM Shells and exports it to a
        /// STEP or IGES file (issue #20) - the "SAM to file" direction the
        /// Grasshopper nodes use. Returns true on success; inspect
        /// <paramref name="result"/> diagnostics (SAM_OCCT_EXPORT_* /
        /// SAM_OCCT_TOPOLOGY_*) either way.
        /// </summary>
        public static bool ToFile(IEnumerable<Shell> shells, string path, OcctExchangeFormat format, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            List<Shell> shells_Temp = shells?.Where(x => x != null).ToList();
            if (shells_Temp == null || shells_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_EXPORT_INPUT", "No shells were supplied.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_EXPORT_INPUT", "No export file path was supplied.");
                return false;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctShapeBuilder.TryCreateTopology(shells_Temp, options, result, out OcctTopology topology))
            {
                return false;
            }

            using (topology)
            {
                return Native.OcctShapeBuilder.TryExport(topology, path, format, result);
            }
        }
    }
}
