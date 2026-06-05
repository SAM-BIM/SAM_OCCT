// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT.Native
{
    internal class OcctNativeInput
    {
        public double[] Coordinates { get; set; }

        public int[] LoopPointCounts { get; set; }

        public int[] FaceLoopCounts { get; set; }

        public int FaceCount { get; set; }

        public int[] ShellFaceCounts { get; set; }

        public int ShellCount { get; set; }
    }
}
