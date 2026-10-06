// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;

// SAM.Core.OCCT and SAM.Geometry.OCCT both declare a static Query/Create class.
using GeometryQuery = SAM.Geometry.OCCT.Query;
using GeometryCreate = SAM.Geometry.OCCT.Create;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Classifies resolved cells (Phase 7b, docs/P6_ARCHITECTURE_REVIEW.md §P sub-step 7b) into
    /// <see cref="CellRole.Interior"/>/<see cref="CellRole.Exterior"/>/<see cref="CellRole.Sliver"/>/
    /// <see cref="CellRole.Unknown"/> using only managed geometric matching plus the existing native
    /// <c>Query.IsPointInside</c> verifier - no new native ABI, no solver geometry change.
    /// </summary>
    public static class CellClassifier
    {
        /// <summary>
        /// The pure classification decision, unit-testable without any native kernel: a cell whose volume
        /// falls below <paramref name="minCellVolume"/> is always <see cref="CellRole.Sliver"/> regardless of
        /// where its centre sits (a sliver is diagnosed and excluded, never spaced); otherwise the role is
        /// decided by <paramref name="insideEnvelope"/> - true -&gt; Interior, false -&gt; Exterior, null
        /// (unevaluated, e.g. native unavailable or no centre) -&gt; Unknown. Never defaults to Interior.
        /// </summary>
        public static CellRole Classify(double volume, double minCellVolume, bool? insideEnvelope)
        {
            if (volume < minCellVolume)
            {
                return CellRole.Sliver;
            }

            if (insideEnvelope == null)
            {
                return CellRole.Unknown;
            }

            return insideEnvelope.Value ? CellRole.Interior : CellRole.Exterior;
        }

        /// <summary>
        /// Classifies every entry in <paramref name="cells"/> against the model's own outer envelope: builds
        /// ONE extra <c>Create.Shells</c> decode of <paramref name="resolvedFace3Ds"/> - the same face set the
        /// cells were resolved from - with <c>AvoidInternalShapes = true</c> (collapsing internal partitions
        /// so only the true outside boundary remains) and <c>RetainTopology = true</c>, then tests each cell's
        /// <see cref="SolverCell.Center"/> against that single outer solid via the existing
        /// <c>Query.IsPointInside</c>. This is the "outer envelope" the plan's Phase 7 describes, expressed as
        /// a solid rather than a per-face adjacency count: for a normally-partitioned model every sub-cell's
        /// centre stays inside the same outer envelope it was subdivided from (all Interior); a cell whose
        /// centre falls outside it is a bounded complement/background region the resolve happened to produce
        /// alongside the real rooms, not a room itself. Reuses native metadata the kernel already exposes
        /// (cell centre, point-in-solid) - no new native entry point. Never silently defaults an
        /// unevaluated cell to Interior (native/topology unavailable, or no centre, -&gt; Unknown). Emits a
        /// <see cref="DiagnosticCode.SliverCell"/> Warning per sliver cell (never silent) when
        /// <paramref name="diagnostics"/> is supplied.
        /// </summary>
        public static IReadOnlyList<CellRole> ClassifyCells(
            IReadOnlyList<SolverCell> cells,
            List<Face3D> resolvedFace3Ds,
            double minCellVolume,
            OcctBuildOptions options,
            SolverDiagnostics diagnostics = null)
        {
            List<CellRole> roles = new List<CellRole>();
            if (cells == null || cells.Count == 0)
            {
                return roles;
            }

            // SewBeforeBuild = true is required, not just consistent with the rest of the solver's builds:
            // RetainTopology is only honoured by the native sew-then-MakerVolume path
            // (OcctCellComplexBuilder.TrySewThenMakeVolume) - the plain direct MakerVolume build discards its
            // topology handle before returning, so a build without it would always report Unknown here.
            OcctBuildOptions envelopeOptions = new OcctBuildOptions(options ?? new OcctBuildOptions())
            {
                AvoidInternalShapes = true,
                RetainTopology = true,
                SewBeforeBuild = true
            };

            OcctCellComplexResult envelopeResult = null;
            try
            {
                if (resolvedFace3Ds != null && resolvedFace3Ds.Count != 0)
                {
                    GeometryCreate.Shells(resolvedFace3Ds, out envelopeResult, envelopeOptions);
                }

                OcctTopology envelopeTopology = envelopeResult != null && envelopeResult.NativeAvailable ? envelopeResult.Topology : null;

                foreach (SolverCell cell in cells)
                {
                    double volume = cell?.Volume ?? 0;

                    // Skip the (native) IsPointInside round-trip entirely for a cell already known to be a
                    // sliver by volume alone - Classify would discard the flag anyway.
                    bool? insideEnvelope = null;
                    if (volume >= minCellVolume && cell?.Center != null && envelopeTopology != null)
                    {
                        insideEnvelope = GeometryQuery.IsPointInside(envelopeTopology, cell.Center);
                    }

                    CellRole role = Classify(volume, minCellVolume, insideEnvelope);
                    roles.Add(role);

                    if (role == CellRole.Sliver)
                    {
                        diagnostics?.Add(SolverStage.Resolve, DiagnosticCode.SliverCell, OcctDiagnosticSeverity.Warning,
                            string.Format("Cell {0}: volume {1:0.#####} m3 is below the minimum ({2} m3); classified sliver, not spaced.",
                                cell?.Index ?? -1, volume, minCellVolume));
                    }
                }
            }
            finally
            {
                envelopeResult?.Dispose();
            }

            return roles;
        }
    }
}
