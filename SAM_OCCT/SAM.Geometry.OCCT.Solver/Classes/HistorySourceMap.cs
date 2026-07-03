// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using System.Collections.Generic;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Adapts a native <see cref="OcctHistory"/> snapshot into a <see cref="SourceMap"/> hop so it
    /// composes with the managed pipeline's maps (docs/P3_ABI_V4_NATIVE_HISTORY_DESIGN_REVIEW.md
    /// §D.5/§G/§H). Source index = the input face's flattened-array ordinal; <see cref="FaceKey"/> =
    /// the output flat ordinal (cell-major, face-minor - identical to the managed decode order,
    /// which is what makes <see cref="SourceMap.Compose(SourceMap)"/> valid across the hop). A 1->N
    /// split fans out; an N->1 merge points several sources at one key; a deleted input is excluded.
    /// </summary>
    public static class HistorySourceMap
    {
        /// <summary>
        /// Converts <paramref name="history"/> into a <see cref="SourceMap"/> keyed by input ordinal.
        /// Deleted inputs are excluded (recorded nowhere). Any input that is neither mapped nor
        /// deleted is a history gap: it is emitted as a <see cref="DiagnosticCode.HistoryGap"/>
        /// warning (never silent) so that face falls back to the geometric heuristic downstream.
        /// Returns null when <paramref name="history"/> is null (pre-v4 native / no capture), so the
        /// caller skips the hop entirely rather than composing an identity that would lie.
        /// </summary>
        public static SourceMap ToSourceMap(
            OcctHistory history,
            Provenance provenance = Provenance.Resolved,
            SolverDiagnostics diagnostics = null)
        {
            if (history == null)
            {
                return null;
            }

            SourceMap map = new SourceMap();
            for (int input = 0; input < history.InputCount; input++)
            {
                if (history.IsDeleted(input))
                {
                    continue;
                }

                bool mapped = false;

                foreach (int ordinal in history.ModifiedOrdinals(input))
                {
                    map.Record(input, new FaceKey(ordinal), provenance);
                    mapped = true;
                }

                foreach (int ordinal in history.GeneratedOrdinals(input))
                {
                    map.Record(input, new FaceKey(ordinal), provenance);
                    mapped = true;
                }

                if (!mapped)
                {
                    // Neither mapped nor deleted - a genuine gap. Record it (the face
                    // falls back to NearestSourceIndex) instead of silently dropping it.
                    diagnostics?.Add(
                        SolverStage.Resolve,
                        DiagnosticCode.HistoryGap,
                        OcctDiagnosticSeverity.Warning,
                        string.Format("Native history has no output and no deletion record for input face {0}; falling back to the geometric heuristic for it.", input));
                }
            }

            return map;
        }

        /// <summary>
        /// Reverse-gap check over a composed map: every output <see cref="FaceKey"/> in
        /// [0, <paramref name="outputCount"/>) that no source maps to is emitted as a
        /// <see cref="DiagnosticCode.HistoryGap"/> warning (it will take the geometric fallback in
        /// panel reconstruction). Runs once against the final resolved face count, where the total
        /// output cardinality is known.
        /// </summary>
        public static void ReportReverseGaps(SourceMap map, int outputCount, SolverDiagnostics diagnostics)
        {
            if (map == null || diagnostics == null || outputCount <= 0)
            {
                return;
            }

            for (int ordinal = 0; ordinal < outputCount; ordinal++)
            {
                if (map.SourcesOf(new FaceKey(ordinal)).Count == 0)
                {
                    diagnostics.Add(
                        SolverStage.Resolve,
                        DiagnosticCode.HistoryGap,
                        OcctDiagnosticSeverity.Warning,
                        string.Format("Resolved output face {0} has no native-history source; falling back to the geometric heuristic for it.", ordinal));
                }
            }
        }
    }
}
