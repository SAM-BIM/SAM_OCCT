// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Runtime.CompilerServices;

// Expose internal helpers (e.g. the parameter-precedence resolvers ResolveWeights/ResolveMaxExtends/
// BucketSize + their provenance) to the test assemblies - mirrors the InternalsVisibleTo the sibling
// SAM.Geometry.OCCT.Solver project already grants (this project has GenerateAssemblyInfo=false, so the
// attributes live here rather than in a generated file).
[assembly: InternalsVisibleTo("SAM.OCCT.UnitTests")]
[assembly: InternalsVisibleTo("SAM.OCCT.IntegrationTests")]
[assembly: InternalsVisibleTo("SAM.Analytical.Grasshopper.OCCT")]
