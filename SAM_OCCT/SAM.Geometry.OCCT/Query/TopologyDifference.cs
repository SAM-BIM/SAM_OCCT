// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;

namespace SAM.Geometry.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// Cuts the cutter solids from the target solids natively and returns a
        /// NEW caller-owned handle; both inputs stay valid and caller-owned.
        /// Returns null on failure - inspect <paramref name="result"/>
        /// diagnostics.
        /// </summary>
        public static OcctTopology TopologyDifference(OcctTopology target, OcctTopology cutter, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctShapeBuilder.TryOperate(Native.OcctShapeBuilder.ShapeOperation.Difference, target, cutter, options, 0, result, out OcctTopology output))
            {
                return null;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TOPOLOGY_DIFFERENCE_SUCCESS", string.Format("Native OCCT difference produced {0} solid(s).", output.SolidCount));
            return output;
        }
    }
}
