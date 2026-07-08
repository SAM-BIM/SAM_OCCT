// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Runtime.CompilerServices;

// Expose internal helpers (e.g. SnappedPanel's E1 extend-path census counters) to the test
// assemblies - mirrors the same InternalsVisibleTo the SAM.Geometry.OCCT project already grants.
[assembly: InternalsVisibleTo("SAM.OCCT.UnitTests")]
[assembly: InternalsVisibleTo("SAM.OCCT.IntegrationTests")]
