// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System;

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

        /// <summary>
        /// Surface area of the decoded face (issue #31), driving U&middot;A heat-loss
        /// terms in energy models. Computed from the planar <see cref="Face3D"/>,
        /// so it is exact for SAM's planar faces. NaN when the face is null.
        /// </summary>
        public double Area
        {
            get { return Face3D?.GetArea() ?? double.NaN; }
        }

        /// <summary>
        /// Outward normal of the decoded face (issue #31), taken from the face
        /// plane. Null when the face has no resolvable plane.
        /// </summary>
        public Vector3D Normal
        {
            get { return Face3D?.GetPlane()?.Normal; }
        }

        /// <summary>
        /// Tilt of the face in degrees [0, 180] (issue #31): the angle of the
        /// outward <see cref="Normal"/> from vertical (world +Z). 0 = a face
        /// pointing straight up (floor slab top), 90 = a vertical wall,
        /// 180 = a face pointing straight down (soffit / ceiling underside).
        /// Drives roof/floor/wall classification. NaN when the normal is
        /// unavailable or degenerate.
        /// </summary>
        public double Tilt
        {
            get
            {
                Vector3D normal = Normal;
                if (normal == null)
                {
                    return double.NaN;
                }

                double magnitude = Math.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y) + (normal.Z * normal.Z));
                if (magnitude <= 0)
                {
                    return double.NaN;
                }

                double z = normal.Z / magnitude;
                z = Math.Max(-1.0, Math.Min(1.0, z));
                return Math.Acos(z) * (180.0 / Math.PI);
            }
        }

        /// <summary>
        /// Azimuth of the face in degrees [0, 360) (issue #31): the compass
        /// bearing of the outward <see cref="Normal"/> measured clockwise from
        /// North (world +Y), driving orientation-based solar gains. Returns 0
        /// for a horizontal face (no meaningful azimuth) and NaN when the normal
        /// is unavailable.
        /// </summary>
        public double Azimuth
        {
            get
            {
                Vector3D normal = Normal;
                if (normal == null)
                {
                    return double.NaN;
                }

                double horizontal = Math.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y));
                if (horizontal <= 1e-9)
                {
                    return 0.0;
                }

                double azimuth = Math.Atan2(normal.X, normal.Y) * (180.0 / Math.PI);
                if (azimuth < 0)
                {
                    azimuth += 360.0;
                }

                return azimuth;
            }
        }
    }
}
