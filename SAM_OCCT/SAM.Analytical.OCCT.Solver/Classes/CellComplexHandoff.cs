// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OCCT.Solver
{
    /// <summary>
    /// The P3 handoff gate (docs/CELLCOMPLEX_FIRST_HANDOVER.md): decides whether an incoming panel set may
    /// consume a supplied <see cref="ResolvedCellComplex"/> DIRECTLY (no native rebuild) or must fall back to
    /// rebuilding with solver-matched options. A stale/edited/mismatched panel set can never be consumed
    /// silently - every refusal names why. Pure managed logic (Guid/string comparisons only), shared by the
    /// Grasshopper handoff (<c>SAMOCCTSolve3D</c> stamps, <c>SAMOCCTCreateAdjacencyCluster</c> gates) and unit-
    /// testable without the native kernel or a Grasshopper document.
    /// </summary>
    public static class CellComplexHandoff
    {
        /// <summary>
        /// Stamps <see cref="PanelProvenanceParameter.SolveId"/> on every panel so a later
        /// <see cref="TryDirectConsume"/> call can verify provenance. Mutates the supplied panels in place
        /// (mirrors the existing <c>SolverParameter</c> stamping convention); null/air-only lists are a no-op.
        /// </summary>
        public static void StampSolveId(IEnumerable<Panel> panels, Guid solveId)
        {
            string solveIdText = solveId.ToString();
            foreach (Panel panel in panels ?? Enumerable.Empty<Panel>())
            {
                panel?.SetValue(PanelProvenanceParameter.SolveId, solveIdText);
            }
        }

        /// <summary>
        /// True when <paramref name="panels"/> may consume <paramref name="resolvedCellComplex"/> directly:
        /// every panel carries a <see cref="PanelProvenanceParameter.SolveId"/> stamp matching the complex's
        /// <see cref="ResolvedCellComplex.SolveId"/>, AND the panel Guid roster equals
        /// <see cref="ResolvedCellComplex.PanelGuids"/> exactly (same count, same Guid set - order-independent).
        /// False with a stated <paramref name="reason"/> otherwise (missing stamp / roster mismatch / no
        /// complex supplied / complex has no recorded roster) - the caller's normal response is to fall back
        /// to a rebuild, not to treat this as an error.
        /// </summary>
        public static bool TryDirectConsume(IEnumerable<Panel> panels, ResolvedCellComplex resolvedCellComplex, out string reason)
        {
            if (resolvedCellComplex == null)
            {
                reason = "no CellComplex supplied";
                return false;
            }

            List<Panel> panels_Temp = (panels ?? Enumerable.Empty<Panel>()).Where(x => x != null).ToList();
            if (panels_Temp.Count == 0)
            {
                reason = "no panels supplied";
                return false;
            }

            if (resolvedCellComplex.PanelGuids == null || resolvedCellComplex.PanelGuids.Count == 0)
            {
                reason = "supplied CellComplex carries no recorded panel roster";
                return false;
            }

            string expectedSolveId = resolvedCellComplex.SolveId.ToString();
            List<Panel> missingStamp = panels_Temp.Where(x => !x.TryGetValue(PanelProvenanceParameter.SolveId, out string stamp) || stamp != expectedSolveId).ToList();
            if (missingStamp.Count > 0)
            {
                reason = string.Format("{0} of {1} panel(s) missing a matching SolveId stamp (expected {2})", missingStamp.Count, panels_Temp.Count, expectedSolveId);
                return false;
            }

            if (panels_Temp.Count != resolvedCellComplex.PanelGuids.Count)
            {
                reason = string.Format("roster count mismatch: {0} panel(s) supplied, complex roster has {1}", panels_Temp.Count, resolvedCellComplex.PanelGuids.Count);
                return false;
            }

            HashSet<Guid> incomingGuids = new HashSet<Guid>(panels_Temp.Select(x => x.Guid));
            HashSet<Guid> rosterGuids = new HashSet<Guid>(resolvedCellComplex.PanelGuids);
            if (!incomingGuids.SetEquals(rosterGuids))
            {
                reason = "roster Guid set mismatch: supplied panels do not match the complex's recorded panels (panels added, removed, or swapped)";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
