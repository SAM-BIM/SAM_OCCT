// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT
{
    /// <summary>
    /// Category of a watertightness / validity issue located by the native
    /// validator (issue #37 follow-on). The integer values mirror the native
    /// <c>SamOcctValidationCategory</c> enum one-for-one, so an unrecognised
    /// future value decodes as <see cref="Unknown"/> rather than throwing.
    /// </summary>
    public enum OcctValidationIssueCategory
    {
        /// <summary>An issue the native layer could not categorise (or a value newer than this build).</summary>
        Unknown = 0,

        /// <summary>A free (naked) boundary edge - an edge bounding only one face, so the shell is not watertight. <c>Size</c> is the edge length.</summary>
        NakedEdge = 1,

        /// <summary>A self-intersection located by BOPAlgo_ArgumentAnalyzer.</summary>
        SelfIntersection = 2,

        /// <summary>A topologically invalid face (BRepCheck_Analyzer). <c>Size</c> is the face area.</summary>
        InvalidFace = 3,

        /// <summary>A sliver face whose area is below the tolerance-derived threshold. <c>Size</c> is the face area.</summary>
        SmallFace = 4,

        /// <summary>A degenerate / too-small edge (BOPAlgo_ArgumentAnalyzer).</summary>
        SmallEdge = 5,

        /// <summary>Another invalid sub-shape reported by the kernel analysers.</summary>
        InvalidShape = 6
    }
}
