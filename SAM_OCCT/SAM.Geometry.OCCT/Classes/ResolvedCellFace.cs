// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace SAM.Geometry.OCCT
{
    /// <summary>
    /// A unique cell face in a <see cref="ResolvedCellComplex"/>: one entry per distinct per-decode
    /// <see cref="TopologyKey"/>. A shared internal separator has two <see cref="OwnerCellIndices"/>
    /// (the two rooms it divides); an envelope face has one. <see cref="FlatOrdinals"/> is aligned to
    /// <see cref="OwnerCellIndices"/> - entry <c>k</c> is this face's flat ordinal as it appears in
    /// owner cell <c>OwnerCellIndices[k]</c>'s block of the decode flatten (a shared face is duplicated
    /// once per owner in that flatten). See <see cref="ResolvedCellComplex"/> remarks.
    /// </summary>
    public class ResolvedCellFace : IJSAMObject
    {
        public Face3D Face3D { get; private set; }

        public int TopologyKey { get; private set; }

        public IReadOnlyList<int> OwnerCellIndices { get; private set; }

        public IReadOnlyList<int> FlatOrdinals { get; private set; }

        public ResolvedCellFace(Face3D face3D, int topologyKey, IEnumerable<int> ownerCellIndices, IEnumerable<int> flatOrdinals)
        {
            Face3D = face3D == null ? null : new Face3D(face3D);
            TopologyKey = topologyKey;
            OwnerCellIndices = (ownerCellIndices ?? Enumerable.Empty<int>()).ToList();
            FlatOrdinals = (flatOrdinals ?? Enumerable.Empty<int>()).ToList();
        }

        public ResolvedCellFace(JsonObject jsonObject)
        {
            FromJsonObject(jsonObject);
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jsonObject = new JsonObject
            {
                ["_type"] = GetType().FullName,
                ["TopologyKey"] = TopologyKey
            };

            if (Face3D?.ToJsonObject() is JsonObject face3DJson)
            {
                jsonObject["Face3D"] = face3DJson;
            }

            JsonArray ownerCellIndicesArray = new JsonArray();
            foreach (int ownerCellIndex in OwnerCellIndices)
            {
                ownerCellIndicesArray.Add(ownerCellIndex);
            }
            jsonObject["OwnerCellIndices"] = ownerCellIndicesArray;

            JsonArray flatOrdinalsArray = new JsonArray();
            foreach (int flatOrdinal in FlatOrdinals)
            {
                flatOrdinalsArray.Add(flatOrdinal);
            }
            jsonObject["FlatOrdinals"] = flatOrdinalsArray;

            return jsonObject;
        }

        public bool FromJsonObject(JsonObject jsonObject)
        {
            if (jsonObject == null)
            {
                return false;
            }

            TopologyKey = jsonObject["TopologyKey"]?.GetValue<int>() ?? 0;
            Face3D = jsonObject["Face3D"] is JsonObject face3DObject ? new Face3D(face3DObject) : null;

            List<int> ownerCellIndices = new List<int>();
            if (jsonObject["OwnerCellIndices"] is JsonArray ownerCellIndicesArray)
            {
                foreach (JsonNode node in ownerCellIndicesArray)
                {
                    ownerCellIndices.Add(node?.GetValue<int>() ?? 0);
                }
            }
            OwnerCellIndices = ownerCellIndices;

            List<int> flatOrdinals = new List<int>();
            if (jsonObject["FlatOrdinals"] is JsonArray flatOrdinalsArray)
            {
                foreach (JsonNode node in flatOrdinalsArray)
                {
                    flatOrdinals.Add(node?.GetValue<int>() ?? -1);
                }
            }
            FlatOrdinals = flatOrdinals;

            return true;
        }
    }
}
