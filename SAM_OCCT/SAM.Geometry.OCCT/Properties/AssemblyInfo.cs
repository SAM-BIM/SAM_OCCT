// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// Expose internal helpers (e.g. OcctNativeInputBuilder) to the unit test assembly.
[assembly: InternalsVisibleTo("SAM.OCCT.UnitTests")]
[assembly: AssemblyTitle("SAM.Geometry.OCCT")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("SAM_OCCT")]
[assembly: AssemblyCopyright("Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: Guid("7d5909a0-bd80-47bc-935d-f0f412d9b8a7")]
[assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyFileVersion("1.0.*")]
