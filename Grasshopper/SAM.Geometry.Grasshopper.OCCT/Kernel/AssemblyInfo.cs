// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Grasshopper.Kernel;
using System;
using System.Drawing;

namespace SAM.Geometry.Grasshopper.OCCT
{
    public class AssemblyInfo : GH_AssemblyInfo
    {
        public override string Name => "SAM Geometry OCCT";

        public override Bitmap Icon => null;

        public override Bitmap AssemblyIcon => null;

        public override string Description => "SAM Geometry tools backed by Open CASCADE Technology.";

        public override Guid Id => new Guid("82f8cb69-5abd-492d-b90a-f1042d1efbb7");

        public override string AuthorName => "Michal Dengusiak & Jakub Ziolkowski and contributors";

        public override string AuthorContact => "https://github.com/SAM-BIM";
    }
}
