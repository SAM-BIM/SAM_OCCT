// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SAM.OCCT.GrasshopperTests
{
    /// <summary>
    /// The native OCCT runtime for the component behaviour tests, as in the integration project
    /// (NativeRuntimeBootstrap / NativeProbe): build-native.ps1 puts SAM.Occt.Native.dll and its dependencies in
    /// &lt;repoRoot&gt;/build, so that directory is prepended to PATH once, before any test. Only PATH is widened; no
    /// file is copied. Tests that need the native layer skip when <see cref="Available"/> is false.
    /// </summary>
    internal static class NativeRuntime
    {
        private const string NativeLibraryFileName = "SAM.Occt.Native.dll";
        private const int MaxParentLevels = 10;

        private static readonly Lazy<bool> available = new Lazy<bool>(Probe);

        public static bool Available => available.Value;

        [ModuleInitializer]
        internal static void Initialize()
        {
            DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; directory != null && i < MaxParentLevels; i++, directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, "build");
                if (!File.Exists(Path.Combine(candidate, NativeLibraryFileName)))
                {
                    continue;
                }

                string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                if (!path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Contains(candidate, StringComparer.OrdinalIgnoreCase))
                {
                    Environment.SetEnvironmentVariable("PATH", candidate + Path.PathSeparator + path);
                }

                return;
            }
        }

        private static bool Probe()
        {
            Shell box = new Shell(new List<Face3D>
            {
                Quad(0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0), Quad(0, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1, 1),
                Quad(0, 0, 0, 1, 0, 0, 1, 0, 1, 0, 0, 1), Quad(0, 1, 0, 1, 1, 0, 1, 1, 1, 0, 1, 1),
                Quad(0, 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1), Quad(1, 0, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1),
            });

            SAM.Geometry.OCCT.Query.ShellsUnion(new List<Shell> { box }, out OcctCellComplexResult result, new OcctBuildOptions());
            using (result)
            {
                return result != null && result.NativeAvailable;
            }
        }

        internal static Face3D Quad(params double[] c)
        {
            return new Face3D(new Polygon3D(new List<Point3D> { new Point3D(c[0], c[1], c[2]), new Point3D(c[3], c[4], c[5]), new Point3D(c[6], c[7], c[8]), new Point3D(c[9], c[10], c[11]) }));
        }
    }
}
