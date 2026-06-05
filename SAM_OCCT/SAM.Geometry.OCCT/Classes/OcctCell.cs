// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.Geometry.OCCT
{
    public class OcctCell
    {
        public Shell Shell { get; }

        public double Volume { get; }

        public IReadOnlyList<int> SourceFaceIndexes { get; }

        public OcctCell(Shell shell, double volume, IEnumerable<int> sourceFaceIndexes = null)
        {
            Shell = shell;
            Volume = volume;
            SourceFaceIndexes = sourceFaceIndexes == null ? new List<int>() : new List<int>(sourceFaceIndexes);
        }
    }
}
