// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT
{
    public class OcctCellFaceAdjacency
    {
        public int TopologyKey { get; }

        public int CellIndex1 { get; }

        public int FaceIndex1 { get; }

        public int CellIndex2 { get; }

        public int FaceIndex2 { get; }

        public OcctCellFaceAdjacency(int topologyKey, int cellIndex1, int faceIndex1, int cellIndex2, int faceIndex2)
        {
            TopologyKey = topologyKey;
            CellIndex1 = cellIndex1;
            FaceIndex1 = faceIndex1;
            CellIndex2 = cellIndex2;
            FaceIndex2 = faceIndex2;
        }
    }
}
