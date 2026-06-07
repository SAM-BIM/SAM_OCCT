// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Makes the native OCCT runtime - built by build-native.ps1 into &lt;repoRoot&gt;/build,
    /// alongside SAM.Occt.Native.dll and the OCCT/vcpkg runtime DLLs it depends on -
    /// discoverable by the P/Invoke loader when the testhost runs from its own bin output.
    ///
    /// Without this, DllImport("SAM.Occt.Native") cannot resolve even on a machine where
    /// the native library has been built, so NativeProbe would always report unavailable
    /// and the integration suite would always skip. Runs once, before any test, via a
    /// module initializer, and only ever widens the process PATH - it never copies files,
    /// so it cannot shadow the test's own managed assemblies.
    /// </summary>
    internal static class NativeRuntimeBootstrap
    {
        private const string NativeLibraryFileName = "SAM.Occt.Native.dll";
        private const int MaxParentLevels = 10;

        [ModuleInitializer]
        internal static void Initialize()
        {
            string directory = FindNativeRuntimeDirectory(AppContext.BaseDirectory);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            // Prepending the directory to PATH lets the Windows loader resolve both
            // SAM.Occt.Native.dll and its colocated dependency DLLs (the loader searches
            // a loaded module's own directory first when resolving its dependencies).
            string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            string[] entries = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            if (!entries.Contains(directory, StringComparer.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + path);
            }
        }

        private static string FindNativeRuntimeDirectory(string startDirectory)
        {
            DirectoryInfo directory = string.IsNullOrEmpty(startDirectory) ? null : new DirectoryInfo(startDirectory);

            for (int i = 0; directory != null && i < MaxParentLevels; i++, directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, "build");
                if (File.Exists(Path.Combine(candidate, NativeLibraryFileName)))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
