// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;

namespace SAM.Geometry.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// Intersects the target solids with the tool solids natively and
        /// returns a NEW caller-owned handle; both inputs stay valid and
        /// caller-owned. Returns null on failure - inspect
        /// <paramref name="result"/> diagnostics.
        /// </summary>
        public static OcctTopology TopologyIntersection(OcctTopology target, OcctTopology tool, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctShapeBuilder.TryOperate(Native.OcctShapeBuilder.ShapeOperation.Intersection, target, tool, options, 0, result, out OcctTopology output))
            {
                return null;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TOPOLOGY_INTERSECTION_SUCCESS", string.Format("Native OCCT intersection produced {0} solid(s).", output.SolidCount));
            return output;
        }
    }
}
