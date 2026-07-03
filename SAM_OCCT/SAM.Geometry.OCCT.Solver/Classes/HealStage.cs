// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Stage C - HEAL: re-attach input faces the native MakerVolume dropped. The kernel returns only
    /// faces that bound a closed cell, so a face whose cell fails to form (a stepped/tilted region it
    /// cannot close, or a lid the cells cap off) is silently discarded, leaving a hole
    /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §D/§F).
    /// <para>
    /// Phase 5c adds <see cref="RetainDroppedV2"/>: the managed-pipeline entry that re-adds the ORIGINAL
    /// CLEAN geometry (the SnapStage output) for every input source the native resolve genuinely dropped -
    /// detected map-side (<c>SourceMap.FacesOf(source).Count == 0</c>), not by a geometric nearest-source
    /// heuristic - behind duplicate/degenerate/area filters, tagging each re-added face
    /// <see cref="Provenance.DroppedRetained"/> and emitting a <see cref="DiagnosticCode.DroppedFace"/>
    /// diagnostic (never a silent re-add). The pre-Phase-5 <see cref="RetainDropped"/> - a filter-only
    /// variant that re-adds a supplied candidate list verbatim - is kept for callers that already hold the
    /// exact face set to filter. See docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §H.
    /// </para>
    /// </summary>
    public static class HealStage
    {
        /// <summary>Outcome of <see cref="RetainDropped"/>: the augmented face set plus the faces that were re-added.</summary>
        public class Result
        {
            public List<Face3D> ResolvedFace3Ds { get; set; } = new List<Face3D>();

            public List<Face3D> RetainedFace3Ds { get; } = new List<Face3D>();
        }

        /// <summary>
        /// Outcome of <see cref="RetainDroppedV2"/>: the clean faces re-added (in retain order) plus, index-aligned,
        /// the dropped source index/indices each one recovers - so the caller can record
        /// <see cref="Provenance.DroppedRetained"/> at the retained face's eventual output ordinal (which
        /// <see cref="RetainDroppedV2"/> cannot know, since the final append order is decided downstream).
        /// </summary>
        public sealed class RetainDroppedResult
        {
            public List<Face3D> RetainedFace3Ds { get; } = new List<Face3D>();

            public List<List<int>> RetainedSourceIndices { get; } = new List<List<int>>();
        }

        /// <summary>
        /// RetainDropped v2 (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §H) - the managed-pipeline heal.
        /// For every input source the native resolve produced no output face for
        /// (<c><paramref name="sourceMap"/>.FacesOf(source).Count == 0</c> - map-driven, not a geometric
        /// nearest-source guess), re-adds the ORIGINAL CLEAN geometry (the SnapStage clean face carrying that
        /// source), NOT the conditioned/extended face the resolve saw. Each candidate must pass the safety
        /// filters before it is retained: a valid face, area &gt;= <paramref name="minArea"/>, and not already
        /// represented in <paramref name="resolvedFace3Ds"/> or an earlier-retained face (kills
        /// duplicate/degenerate/double-cover). Every retained face is tagged for
        /// <see cref="Provenance.DroppedRetained"/> (the caller records it at the output ordinal) and a
        /// <see cref="DiagnosticCode.DroppedFace"/> Info diagnostic names the recovered source(s) - never a
        /// silent re-add.
        /// </summary>
        /// <param name="resolvedFace3Ds">The native resolve's output faces (the "represented" set to dedup against).</param>
        /// <param name="cleanFace3Ds">The SnapStage clean faces (the "original clean geometry" source, world frame).</param>
        /// <param name="sourceIndicesPerCleanFace">Per clean face, the input source indices it carries (index-aligned to <paramref name="cleanFace3Ds"/>).</param>
        /// <param name="sourceMap">Source -&gt; resolved-output attribution; a source with an empty <c>FacesOf</c> was dropped.</param>
        /// <param name="diagnostics">Accumulates a <see cref="DiagnosticCode.DroppedFace"/> Info per retained face.</param>
        /// <param name="minArea">Minimum retained-face area (m2); defaults to the air-panel floor (1e-4 m2).</param>
        public static RetainDroppedResult RetainDroppedV2(
            List<Face3D> resolvedFace3Ds,
            List<Face3D> cleanFace3Ds,
            List<List<int>> sourceIndicesPerCleanFace,
            SourceMap sourceMap,
            SolverDiagnostics diagnostics = null,
            double minArea = GapFill.MinPatchArea)
        {
            RetainDroppedResult result = new RetainDroppedResult();
            if (cleanFace3Ds == null || cleanFace3Ds.Count == 0 || sourceMap == null)
            {
                return result;
            }

            // resolved ∪ already-retained: IsRepresented is tested against BOTH, so a dropped source whose
            // clean geometry a resolved face (or an earlier retained face) already covers is not re-added twice.
            List<Face3D> represented = resolvedFace3Ds == null ? new List<Face3D>() : resolvedFace3Ds.ToList();

            for (int k = 0; k < cleanFace3Ds.Count; k++)
            {
                List<int> sources = sourceIndicesPerCleanFace != null && k < sourceIndicesPerCleanFace.Count
                    ? sourceIndicesPerCleanFace[k]
                    : null;
                if (sources == null || sources.Count == 0)
                {
                    continue; // a fabricated clean face (no source identity) is not an input the resolve dropped
                }

                // The sources of THIS clean face the resolve dropped (produced no output face for). A clean face
                // mixing a dropped and a represented source records only the dropped one(s), so a still-represented
                // source is never perturbed into a spurious split.
                List<int> droppedSources = sources
                    .Where(s => s >= 0 && sourceMap.FacesOf(s).Count == 0)
                    .Distinct()
                    .ToList();
                if (droppedSources.Count == 0)
                {
                    continue; // every source of this clean face is represented - nothing to retain
                }

                Face3D cleanFace3D = cleanFace3Ds[k];
                if (cleanFace3D == null || !cleanFace3D.IsValid())
                {
                    continue; // safety: invalid geometry
                }

                if (cleanFace3D.GetArea() < minArea)
                {
                    continue; // safety: degenerate/sliver face below the area floor
                }

                if (Panel3DSnapSolver.IsRepresented(cleanFace3D, represented))
                {
                    continue; // safety: already covered by a resolved or earlier-retained face (duplicate/double-cover)
                }

                result.RetainedFace3Ds.Add(cleanFace3D);
                result.RetainedSourceIndices.Add(droppedSources);
                represented.Add(cleanFace3D); // dedup subsequent candidates against this retained face too

                diagnostics?.Add(SolverStage.Heal, DiagnosticCode.DroppedFace, OcctDiagnosticSeverity.Info,
                    string.Format("RetainDropped: re-added original clean geometry for dropped source(s) {0}.", string.Join(", ", droppedSources)),
                    face3D: cleanFace3D);
            }

            return result;
        }

        /// <summary>
        /// Filter-only RetainDropped (pre-Phase-5 contract, kept for callers that already hold the exact face
        /// set to filter): re-adds every face in <paramref name="candidateFace3Ds"/> that is not represented in
        /// <paramref name="resolvedFace3Ds"/>, using the supplied geometry verbatim. Records the re-added faces
        /// (and, when a <paramref name="sourceMap"/> is supplied, tags them <see cref="Provenance.DroppedRetained"/>
        /// keyed by their output index). Prefer <see cref="RetainDroppedV2"/> in the managed pipeline: it re-adds
        /// the ORIGINAL CLEAN geometry (not the conditioned/extended candidate), detects drops map-side, and adds
        /// area/dedup safety filters.
        /// </summary>
        public static Result RetainDropped(
            List<Face3D> resolvedFace3Ds,
            List<Face3D> candidateFace3Ds,
            SourceMap sourceMap = null)
        {
            Result result = new Result();
            List<Face3D> resolved = resolvedFace3Ds == null ? new List<Face3D>() : resolvedFace3Ds.ToList();
            result.ResolvedFace3Ds = resolved;

            if (candidateFace3Ds == null || candidateFace3Ds.Count == 0)
            {
                return result;
            }

            foreach (Face3D face3D in candidateFace3Ds)
            {
                if (face3D != null && face3D.IsValid() && !Panel3DSnapSolver.IsRepresented(face3D, resolved))
                {
                    result.RetainedFace3Ds.Add(face3D);
                }
            }

            if (result.RetainedFace3Ds.Count != 0)
            {
                int baseIndex = resolved.Count;
                result.ResolvedFace3Ds = resolved.Concat(result.RetainedFace3Ds).ToList();

                for (int i = 0; i < result.RetainedFace3Ds.Count; i++)
                {
                    sourceMap?.RecordFabricated(new FaceKey(baseIndex + i), Provenance.DroppedRetained);
                }
            }

            return result;
        }
    }
}
