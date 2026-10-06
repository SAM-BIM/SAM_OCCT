// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SAM.OCCT.GrasshopperTests
{
    /// <summary>
    /// Module initializer that satisfies Grasshopper/Rhino assembly dependencies at JIT
    /// time when a real Rhino 8 installation is not present, by loading the real managed
    /// DLLs from the NuGet cache.  Native DLLs that RhinoCommon P/Invokes are not
    /// resolved (the resolver returns IntPtr.Zero) so that the test process does not
    /// show error popups; the tests never call native code paths.
    /// </summary>
    internal static class GrasshopperTestAssembly
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        public static void Initialize()
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            NativeLibrary.SetDllImportResolver(typeof(GrasshopperTestAssembly).Assembly, OnDllImportResolve);
        }

        private static IntPtr OnDllImportResolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            return IntPtr.Zero;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name);
            string simple = name.Name ?? "";

            if (simple == "Grasshopper" || simple == "GH_IO")
            {
                string path = NuGetPath("grasshopper", simple + ".dll");
                if (File.Exists(path))
                    return Assembly.LoadFrom(path);
            }
            if (simple == "RhinoCommon" || simple == "Rhino.UI" || simple == "Eto" || simple == "Ed.Eto")
            {
                string path = NuGetPath("rhinocommon", simple + ".dll", "net48");
                if (File.Exists(path))
                    return Assembly.LoadFrom(path);
            }
            return null;
        }

        private static string NuGetPath(string package, string file, string tfm = "net7.0")
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "packages", package, "8.21.25188.17001",
                "lib", tfm, file);
        }
    }
}

