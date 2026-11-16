// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Planar;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// What was proved about the source element of one panel, as the geometry data the contact-aware void guard
    /// (<see cref="ContactVoidGuard"/>) needs: the frame the element was extracted in, the thickness certified as solid
    /// everywhere inside <see cref="Support"/>, and the face the panel had at extraction (<see cref="Origin"/>, openings and
    /// notches filled).
    /// <para>
    /// The solver never reads or validates a panel's own record: the analytical adapter (<c>SAM.Analytical.OCCT.Solver</c>)
    /// accepts a <c>Panel.SourceEvidence</c> only through <c>SAM.Analytical.Query.ValidSourceEvidence</c> and hands over
    /// this plain carrier, together with <see cref="RegionIntersection"/> (<c>SAM.Analytical.Query.DefiniteIntersection</c>),
    /// the one region operation the guard may use. A carrier without a <see cref="RegionIntersection"/> never releases a guard.
    /// </para>
    /// </summary>
    public class ContactEvidence
    {
        /// <param name="plane">The frame of the record: the plane the source element was extracted on.</param>
        /// <param name="guaranteedThickness">Thickness [m] certified as solid at every point of <paramref name="support"/> (g).</param>
        /// <param name="support">Regions of <paramref name="plane"/> where that thickness is certified.</param>
        /// <param name="origin">The panel's face at extraction, in <paramref name="plane"/>.</param>
        /// <param name="regionIntersection">Definite intersection of two region lists in one frame: an empty list means certainly
        /// empty, null means the kernel could not decide.</param>
        public ContactEvidence(Plane plane, double guaranteedThickness, IEnumerable<Face2D> support, IEnumerable<Face2D> origin, Func<IEnumerable<Face2D>, IEnumerable<Face2D>, double, List<Face2D>> regionIntersection)
        {
            Plane = plane;
            GuaranteedThickness = guaranteedThickness;
            Support = support?.Where(x => x != null).ToList() ?? new List<Face2D>();
            Origin = origin?.Where(x => x != null).ToList() ?? new List<Face2D>();
            RegionIntersection = regionIntersection;
        }

        /// <summary>The frame of the record.</summary>
        public Plane Plane { get; }

        /// <summary>Thickness [m] certified as solid at every point of <see cref="Support"/>.</summary>
        public double GuaranteedThickness { get; }

        /// <summary>Regions of <see cref="Plane"/> where <see cref="GuaranteedThickness"/> is certified.</summary>
        public List<Face2D> Support { get; }

        /// <summary>The panel's face at extraction, in <see cref="Plane"/>.</summary>
        public List<Face2D> Origin { get; }

        /// <summary>The definite region intersection the guard uses for every region operation; null means no release.</summary>
        public Func<IEnumerable<Face2D>, IEnumerable<Face2D>, double, List<Face2D>> RegionIntersection { get; }

        /// <summary>True when the record can be used: a plane, a positive finite thickness, and non-empty support and origin.</summary>
        public bool IsComplete => Plane != null && !double.IsNaN(GuaranteedThickness) && !double.IsInfinity(GuaranteedThickness)
            && GuaranteedThickness > 0 && Support.Count != 0 && Origin.Count != 0;
    }
}
