// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Drawing;
using System.Reflection;

namespace SAM.Geometry.Grasshopper.OCCT
{
    internal static class SAMOCCTIcon
    {
        internal static Bitmap SAM_OCCT24
        {
            get
            {
                Assembly assembly = typeof(SAMOCCTIcon).Assembly;
                using (System.IO.Stream stream = assembly.GetManifestResourceStream("SAM.Geometry.Grasshopper.OCCT.Resources.SAM_OCCT24.png"))
                {
                    return stream == null ? null : new Bitmap(stream);
                }
            }
        }
    }
}
