// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT
{
    /// <summary>
    /// How an input face is represented by the faces of a built cell complex (see
    /// <see cref="Query.FaceCoverage(System.Collections.Generic.IEnumerable{Spatial.Face3D}, System.Collections.Generic.IEnumerable{Spatial.Shell}, double, double, double, double)"/>).
    /// </summary>
    public enum FaceCoverageVerdict
    {
        /// <summary>The face is fully represented by coplanar cell faces (possibly split into many pieces).</summary>
        Covered,

        /// <summary>Part of the face is represented; the rest lies outside every cell (eaves, flaps past a junction).</summary>
        Trimmed,

        /// <summary>No part of the face is represented and it lies outside every cell (e.g. a free-standing wall).</summary>
        Unused,

        /// <summary>Part of the face is represented, but some of it cuts through the interior of a single cell.</summary>
        Partial,

        /// <summary>No part of the face is represented and it cuts through the interior of a single cell: the under-split signature.</summary>
        Missing,

        /// <summary>Some of the face separates two different cells, or a cell from the outside, yet is not represented: a topology inconsistency.</summary>
        Unresolved,
    }
}
