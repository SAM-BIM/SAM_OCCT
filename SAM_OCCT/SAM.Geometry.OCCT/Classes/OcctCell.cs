// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.Geometry.OCCT
{
    public class OcctCell
    {
        private readonly List<OcctCellFace> faces;

        public Shell Shell { get; }

        public double Volume { get; }

        public Point3D Center { get; }

        public IReadOnlyList<int> SourceFaceIndexes { get; }

        public IReadOnlyList<OcctCellFace> Faces
        {
            get { return faces; }
        }

        public OcctCell(Shell shell, double volume, IEnumerable<int> sourceFaceIndexes = null, IEnumerable<OcctCellFace> faces = null, Point3D center = null)
        {
            Shell = shell;
            Volume = volume;
            Center = center == null ? null : new Point3D(center);
            SourceFaceIndexes = sourceFaceIndexes == null ? new List<int>() : new List<int>(sourceFaceIndexes);
            this.faces = faces == null ? new List<OcctCellFace>() : new List<OcctCellFace>(faces);
        }
    }
}
