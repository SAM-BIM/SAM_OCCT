// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace SAM.Analytical.Grasshopper.OCCT
{
    public class AssemblyInfo : GH_AssemblyInfo
    {
        public override string Name => "SAM Analytical OCCT";

        public override Bitmap Icon => null;

        public override Bitmap AssemblyIcon => null;

        public override string Description => "SAM Analytical tools backed by Open CASCADE Technology.";

        public override Guid Id => new Guid("17d67a4e-e4db-497b-926a-806c707268a0");

        public override string AuthorName => "Michal Dengusiak & Jakub Ziolkowski and contributors";

        public override string AuthorContact => "https://github.com/SAM-BIM";
    }
}
