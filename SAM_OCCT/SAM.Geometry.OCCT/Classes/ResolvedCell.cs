// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Geometry.Spatial;
using System.Text.Json.Nodes;

namespace SAM.Geometry.OCCT
{
    /// <summary>Per-cell metadata in a <see cref="ResolvedCellComplex"/>: the cell's decode index, its
    /// volume, and its centre. The index is the key adjacency and relation filtering address by (never
    /// a centre-distance match).</summary>
    public class ResolvedCell : IJSAMObject
    {
        public int Index { get; private set; }

        public double Volume { get; private set; }

        public Point3D Centre { get; private set; }

        public ResolvedCell(int index, double volume, Point3D centre)
        {
            Index = index;
            Volume = volume;
            Centre = centre == null ? null : new Point3D(centre);
        }

        public ResolvedCell(JsonObject jsonObject)
        {
            FromJsonObject(jsonObject);
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jsonObject = new JsonObject
            {
                ["_type"] = GetType().FullName,
                ["Index"] = Index,
                ["Volume"] = Volume
            };

            if (Centre?.ToJsonObject() is JsonObject centreJson)
            {
                jsonObject["Centre"] = centreJson;
            }

            return jsonObject;
        }

        public bool FromJsonObject(JsonObject jsonObject)
        {
            if (jsonObject == null)
            {
                return false;
            }

            Index = jsonObject["Index"]?.GetValue<int>() ?? 0;
            Volume = jsonObject["Volume"]?.GetValue<double>() ?? double.NaN;
            Centre = jsonObject["Centre"] is JsonObject centreObject ? new Point3D(centreObject) : null;
            return true;
        }
    }
}
