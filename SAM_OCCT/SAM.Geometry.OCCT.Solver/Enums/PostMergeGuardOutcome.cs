// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>What <see cref="PostMergeGuard"/> did with the native coplanar post-merge of the resolved faces.</summary>
    public enum PostMergeGuardOutcome
    {
        /// <summary>The merge combined no faces (or its cell set could not be checked): nothing to guard, nothing changed.</summary>
        NothingMerged,

        /// <summary>The merged faces re-decode to every cell MakerVolume had formed: the merge is kept exactly as the kernel returned it.</summary>
        Accepted,

        /// <summary>Merge groups touching the boundary of the lost cells were reverted to their pre-merge faces and every cell survives.</summary>
        Repaired,

        /// <summary>As <see cref="Repaired"/>, after widening the reverted groups to the cells adjacent to the lost ones.</summary>
        Widened,

        /// <summary>No partial repair kept every cell: all merge groups were reverted (the pre-merge faces are returned).</summary>
        FellBack
    }
}
