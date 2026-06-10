// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;

namespace SAM.Geometry.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// Decodes a persistent OCCT topology into SAM cells, shells, volumes,
        /// centers and face adjacencies. The topology handle stays valid and
        /// caller-owned. Always returns a result; check
        /// <see cref="OcctCellComplexResult.Success"/> and the diagnostics.
        /// Topology keys are only comparable within one decoded result.
        /// </summary>
        public static OcctCellComplexResult CellComplexResult(OcctTopology topology, OcctBuildOptions options = null)
        {
            OcctCellComplexResult result = new OcctCellComplexResult();

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            Native.OcctShapeBuilder.TryDecode(topology, options, result);
            return result;
        }
    }
}
