// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT
{
    /// <summary>
    /// Coverage of one input face by the faces of a built cell complex. Areas are in model units squared;
    /// <see cref="CoveredArea"/> + <see cref="InteriorArea"/> + <see cref="ExteriorArea"/> + <see cref="UnresolvedArea"/>
    /// equals <see cref="Area"/> up to the sliver threshold.
    /// </summary>
    public class FaceCoverageEntry
    {
        public FaceCoverageEntry(int faceIndex, double area, double coveredArea, double interiorArea, double exteriorArea, double unresolvedArea, FaceCoverageVerdict verdict)
        {
            FaceIndex = faceIndex;
            Area = area;
            CoveredArea = coveredArea;
            InteriorArea = interiorArea;
            ExteriorArea = exteriorArea;
            UnresolvedArea = unresolvedArea;
            Verdict = verdict;
        }

        /// <summary>Index of the face in the input sequence.</summary>
        public int FaceIndex { get; }

        public double Area { get; }

        /// <summary>Area of the face that coincides with coplanar cell faces.</summary>
        public double CoveredArea { get; }

        /// <summary>Uncovered area lying inside a single cell (the face cuts through the cell without bounding it).</summary>
        public double InteriorArea { get; }

        /// <summary>Uncovered area lying outside every cell.</summary>
        public double ExteriorArea { get; }

        /// <summary>Uncovered area separating different cells (or a cell and the outside).</summary>
        public double UnresolvedArea { get; }

        public FaceCoverageVerdict Verdict { get; }

        public double CoveredRatio
        {
            get { return Area > 0 ? CoveredArea / Area : 0; }
        }

        public override string ToString()
        {
            return string.Format("#{0} {1} {2:0.###}/{3:0.###} covered, {4:0.###} interior, {5:0.###} exterior, {6:0.###} unresolved", FaceIndex, Verdict, CoveredArea, Area, InteriorArea, ExteriorArea, UnresolvedArea);
        }
    }
}
