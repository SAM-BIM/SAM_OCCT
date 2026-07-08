// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using System.Text.Json.Nodes;

namespace SAM.Geometry.OCCT
{
    /// <summary>A shared-face adjacency in a <see cref="ResolvedCellComplex"/>: the per-decode
    /// <see cref="TopologyKey"/> of the shared face and the two owner cell indices it separates. The
    /// managed-side view of <see cref="OcctCellFaceAdjacency"/> (face indices dropped - they are decode
    /// internals).</summary>
    public class ResolvedFaceAdjacency : IJSAMObject
    {
        public int TopologyKey { get; private set; }

        public int CellIndex1 { get; private set; }

        public int CellIndex2 { get; private set; }

        public ResolvedFaceAdjacency(int topologyKey, int cellIndex1, int cellIndex2)
        {
            TopologyKey = topologyKey;
            CellIndex1 = cellIndex1;
            CellIndex2 = cellIndex2;
        }

        public ResolvedFaceAdjacency(JsonObject jsonObject)
        {
            FromJsonObject(jsonObject);
        }

        public JsonObject ToJsonObject()
        {
            return new JsonObject
            {
                ["_type"] = GetType().FullName,
                ["TopologyKey"] = TopologyKey,
                ["CellIndex1"] = CellIndex1,
                ["CellIndex2"] = CellIndex2
            };
        }

        public bool FromJsonObject(JsonObject jsonObject)
        {
            if (jsonObject == null)
            {
                return false;
            }

            TopologyKey = jsonObject["TopologyKey"]?.GetValue<int>() ?? 0;
            CellIndex1 = jsonObject["CellIndex1"]?.GetValue<int>() ?? 0;
            CellIndex2 = jsonObject["CellIndex2"]?.GetValue<int>() ?? 0;
            return true;
        }
    }
}
