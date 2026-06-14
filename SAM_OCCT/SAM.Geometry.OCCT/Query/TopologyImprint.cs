// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;

namespace SAM.Geometry.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// Imprints every solid in the topology against the others via OCCT
        /// General Fuse (issue #27) and returns a NEW caller-owned handle;
        /// the input stays valid and caller-owned. Coincident boundary regions
        /// are split into matching sub-faces (second-level space boundaries)
        /// while the solids stay distinct. Returns null on failure - inspect
        /// <paramref name="result"/> diagnostics.
        /// </summary>
        public static OcctTopology TopologyImprint(OcctTopology topology, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctShapeBuilder.TryOperate(Native.OcctShapeBuilder.ShapeOperation.Imprint, topology, null, options, 0, result, out OcctTopology output))
            {
                return null;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_TOPOLOGY_IMPRINT_SUCCESS", string.Format("Native OCCT imprint produced {0} solid(s).", output.SolidCount));
            return output;
        }
    }
}
