// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;

namespace SAM.Analytical.OCCT.Solver
{
    /// <summary>
    /// An aperture that could not be re-hosted on any resolved output panel derived from its source
    /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md Phase 4, orphan policy): every output piece
    /// the source contributed to was tried (via <c>Analytical.Create.Panel</c>'s own bounding-box +
    /// <c>maxDistance</c> gate) and none was close enough to host it. Carries the aperture's original
    /// world-space geometry (<see cref="Aperture.GetFace3D"/>) and the source panel's Guid so a caller
    /// can re-host it manually. Apertures are never attached to Spaces (not a SAM concept) and never
    /// silently dropped - every orphan surfaces here.
    /// </summary>
    public sealed class OrphanedAperture
    {
        public OrphanedAperture(Aperture aperture, Guid sourceGuid)
        {
            Aperture = aperture;
            SourceGuid = sourceGuid;
        }

        /// <summary>The aperture's own original geometry/construction, unmodified.</summary>
        public Aperture Aperture { get; }

        /// <summary>Guid of the source panel the aperture was hosted on before the solve.</summary>
        public Guid SourceGuid { get; }
    }
}
