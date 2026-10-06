// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    public class OcctNativeImportTests
    {
        [Fact]
        public void NativeImports_WhenInspected_UseExplicitWindowsFileName()
        {
            // Arrange
            Type nativeMethods = typeof(global::SAM.Geometry.OCCT.Query).Assembly.GetType(
                "SAM.Geometry.OCCT.Native.OcctNativeMethods",
                throwOnError: true);

            DllImportAttribute[] imports = nativeMethods
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Select(method => method.GetCustomAttribute<DllImportAttribute>())
                .Where(attribute => attribute != null && attribute.Value.StartsWith("SAM.Occt.Native", StringComparison.Ordinal))
                .ToArray();

            // Act & Assert
            Assert.NotEmpty(imports);
            Assert.All(imports, attribute => Assert.Equal("SAM.Occt.Native.dll", attribute.Value));
        }
    }
}
