// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT
{
    /// <summary>
    /// Open Data Exchange file formats supported by the OCCT import/export
    /// bridge (issue #20, phase 1). STEP preserves BRep solids exactly; IGES is
    /// written in BRep mode so closed solids still round-trip, but it remains a
    /// surface-oriented format and may decode less precisely.
    /// </summary>
    public enum OcctExchangeFormat
    {
        Step,
        Iges
    }
}
