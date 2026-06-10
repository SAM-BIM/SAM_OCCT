// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;

namespace SAM.Geometry.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// Repairs every solid in the topology natively (defeaturing faces
        /// smaller than <paramref name="minArea"/> + unify same domain; solids
        /// that cannot be repaired are kept unchanged) and returns a NEW
        /// caller-owned handle; the input stays valid and caller-owned. Returns
        /// null on failure - inspect <paramref name="result"/> diagnostics.
        /// </summary>
        public static OcctTopology TopologyRepair(OcctTopology topology, out OcctCellComplexResult result, double minArea = 0, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctShapeBuilder.TryOperate(Native.OcctShapeBuilder.ShapeOperation.Repair, topology, null, options, minArea, result, out OcctTopology output))
            {
                return null;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TOPOLOGY_REPAIR_SUCCESS", string.Format("Native OCCT repair produced {0} solid(s).", output.SolidCount));
            return output;
        }
    }
}
