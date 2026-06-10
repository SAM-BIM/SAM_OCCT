// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Geometry.Spatial;
using System;

namespace SAM.Geometry.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// Classifies a point against the live OCCT topology
        /// (BRepClass3d_SolidClassifier): true when the point is inside or on
        /// the boundary (within <paramref name="tolerance"/>) of any solid,
        /// false when outside all solids, null when the topology is
        /// null/disposed, the point is null or the native library is
        /// unavailable.
        /// </summary>
        public static bool? IsPointInside(OcctTopology topology, Point3D point3D, double tolerance = Tolerance.Distance)
        {
            if (topology == null || topology.IsInvalid || topology.IsClosed || point3D == null)
            {
                return null;
            }

            try
            {
                int status = Native.OcctNativeMethods.sam_occt_shape_point_in_solid(topology, point3D.X, point3D.Y, point3D.Z, tolerance);
                if (status < 0)
                {
                    return null;
                }

                return status == 1;
            }
            catch (DllNotFoundException)
            {
                return null;
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }
    }
}
