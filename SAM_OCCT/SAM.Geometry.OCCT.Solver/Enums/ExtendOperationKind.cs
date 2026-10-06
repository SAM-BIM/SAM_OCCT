// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Which managed extend/fill primitive an <see cref="ExtendRecord"/> describes (E3,
    /// docs/EXTEND3D_ROBUST_HANDOVER.md). One kind per applied mutation so a reviewer can tell, per
    /// panel, exactly which edge moved and toward what. Recording only - the geometry is produced by
    /// the E1/E2 primitives unchanged.
    /// </summary>
    public enum ExtendOperationKind
    {
        /// <summary>The wall's TOP was raised to a cap above (<c>ExtendTopTo</c>/<c>ExtendTopToPlane</c>).</summary>
        Top,

        /// <summary>The wall's BASE was lowered to a floor below (<c>ExtendBottomTo</c>/<c>ExtendBottomToPlane</c>).</summary>
        Bottom,

        /// <summary>The wall's plan foot START end was moved to close the plan loop (<c>SetVerticalFootprint</c>, lateral).</summary>
        PlanStart,

        /// <summary>The wall's plan foot END end was moved to close the plan loop (<c>SetVerticalFootprint</c>, lateral).</summary>
        PlanEnd,

        /// <summary>A floor/roof cap was grown outward to its surrounding walls (<c>GrowOutwardTo</c>/<c>GrowOutward</c>).</summary>
        CapGrow
    }
}
