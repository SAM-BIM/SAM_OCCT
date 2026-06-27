// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare a static Query class.
using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Detects, once per test run, whether the native OCCT library (SAM.Occt.Native)
    /// can be loaded. Integration tests use this to skip - rather than fail - when the
    /// native layer has not been built (e.g. on a CI agent without vcpkg/OCCT).
    /// </summary>
    internal static class NativeProbe
    {
        private static readonly object syncRoot = new object();
        private static bool? available;

        public static bool Available
        {
            get
            {
                if (available.HasValue)
                {
                    return available.Value;
                }

                lock (syncRoot)
                {
                    if (!available.HasValue)
                    {
                        available = Probe();
                    }
                }

                return available.Value;
            }
        }

        private static bool Probe()
        {
            List<Shell> shells = new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) };
            GeometryQuery.ShellsUnion(shells, out OcctCellComplexResult result, new OcctBuildOptions());
            return result != null && result.NativeAvailable;
        }
    }
}
