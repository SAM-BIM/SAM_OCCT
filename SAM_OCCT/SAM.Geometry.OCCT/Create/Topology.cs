// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    public static partial class Create
    {
        /// <summary>
        /// Builds a persistent native OCCT topology (one solid per watertight
        /// shell) from the given shells. The caller owns the returned handle and
        /// should Dispose it; see <see cref="OcctTopology"/> for the lifetime
        /// and threading contract. Returns null on failure - inspect
        /// <paramref name="result"/> diagnostics.
        /// </summary>
        public static OcctTopology Topology(IEnumerable<Shell> shells, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            List<Shell> shells_Temp = shells?.Where(x => x != null).ToList();
            if (shells_Temp == null || shells_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TOPOLOGY_INPUT_EMPTY", "No shells were supplied.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctShapeBuilder.TryCreateTopology(shells_Temp, options, result, out OcctTopology topology))
            {
                return null;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TOPOLOGY_SUCCESS", string.Format("Created OCCT topology with {0} solid(s).", topology.SolidCount));
            return topology;
        }

        /// <summary>
        /// Builds a persistent native OCCT topology with cell-complex semantics
        /// (BOPAlgo_MakerVolume over independent faces) from the given faces.
        /// The caller owns the returned handle and should Dispose it. Returns
        /// null on failure - inspect <paramref name="result"/> diagnostics.
        /// </summary>
        public static OcctTopology Topology(IEnumerable<Face3D> face3Ds, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            List<Face3D> face3Ds_Temp = face3Ds?.Where(x => x != null).ToList();
            if (face3Ds_Temp == null || face3Ds_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_TOPOLOGY_INPUT_EMPTY", "No faces were supplied.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctShapeBuilder.TryCreateTopology(face3Ds_Temp, options, result, out OcctTopology topology))
            {
                return null;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TOPOLOGY_SUCCESS", string.Format("Created OCCT topology with {0} solid(s).", topology.SolidCount));
            return topology;
        }
    }
}
