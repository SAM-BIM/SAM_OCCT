// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// Collects the bottom and top elevations (bounding box Min.Z / Max.Z) of the
        /// given Face3Ds and merges values closer than <paramref name="snapTolerance"/>
        /// into a single elevation (the cluster mean). This is how walls that do not
        /// meet at exactly the same level still produce one shared floor/roof level.
        /// Returns the clustered elevations sorted ascending; empty list for no input.
        /// </summary>
        public static List<double> ClusteredElevations(IEnumerable<Face3D> face3Ds, double snapTolerance = Core.Tolerance.MacroDistance)
        {
            List<double> values = new List<double>();
            if (face3Ds != null)
            {
                foreach (Face3D face3D in face3Ds)
                {
                    BoundingBox3D boundingBox3D = face3D?.GetBoundingBox();
                    if (boundingBox3D == null)
                    {
                        continue;
                    }

                    values.Add(boundingBox3D.Min.Z);
                    values.Add(boundingBox3D.Max.Z);
                }
            }

            return ClusteredElevations(values, snapTolerance);
        }

        /// <summary>
        /// Merges elevation values closer than <paramref name="snapTolerance"/> (gap to
        /// the previous sorted value) into single clusters represented by their mean.
        /// Returns the clustered values sorted ascending; empty list for no input.
        /// </summary>
        public static List<double> ClusteredElevations(IEnumerable<double> values, double snapTolerance = Core.Tolerance.MacroDistance)
        {
            List<double> values_Sorted = values?.ToList();
            if (values_Sorted == null || values_Sorted.Count == 0)
            {
                return new List<double>();
            }

            values_Sorted.Sort();

            List<double> result = new List<double>();
            List<double> cluster = new List<double>() { values_Sorted[0] };
            for (int i = 1; i < values_Sorted.Count; i++)
            {
                if (values_Sorted[i] - cluster[cluster.Count - 1] <= snapTolerance)
                {
                    cluster.Add(values_Sorted[i]);
                    continue;
                }

                result.Add(cluster.Average());
                cluster = new List<double>() { values_Sorted[i] };
            }

            result.Add(cluster.Average());
            return result;
        }
    }
}
