// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;

namespace SAM.Geometry.OCCT
{
    public class OcctCellFace
    {
        public Face3D Face3D { get; }

        public int TopologyKey { get; }

        public OcctCellFace(Face3D face3D, int topologyKey)
        {
            Face3D = face3D;
            TopologyKey = topologyKey;
        }
    }
}
