// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OCCT.Solver
{
    /// <summary>
    /// Rebuilds solved output faces into Panels that keep 2D output parity - Guid, parameters,
    /// construction, and re-hosted trimmed apertures (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md
    /// Phase 4). Consumes a <see cref="SourceMap"/> (the solver's always-populated per-output-face
    /// attribution - exact via composed native history when available, geometric otherwise, see
    /// <c>Panel3DSnapSolver.SourceMap</c>) rather than a single nearest-source index, so a genuine
    /// split or merge is handled as such instead of collapsing to one arbitrary winner.
    /// </summary>
    /// <remarks>
    /// Policy per output face, keyed by its recorded source set:
    /// <list type="bullet">
    /// <item>Exactly one source that itself maps to exactly one output face (a clean 1:1 mapping):
    /// the source's own Guid is kept via <c>Create.Panel(source.Guid, source, face3D, ...)</c>.</item>
    /// <item>Exactly one source that maps to more than one output face (a split): this output is one
    /// piece of the split, so it gets a fresh Guid and a <see cref="PanelProvenanceParameter.SourceGuid"/>
    /// stamp naming the original source.</item>
    /// <item>More than one source (a merge): the dominant (largest-area) source keeps its own Guid;
    /// the others are stamped as <see cref="PanelProvenanceParameter.MergedSourceGuids"/>.</item>
    /// </list>
    /// Apertures are never matched by hand: every contributing source's own apertures are handed to
    /// <c>Create.Panel</c>, whose ctor already re-hosts each one only if it lands within the caller's
    /// max distance of that particular output face (trimmed to fit) - trying an aperture against every
    /// piece a split produced is exactly "assigned to the piece that geometrically contains it". An
    /// aperture that fits no produced piece is collected into <see cref="Build"/>'s
    /// <c>orphanedApertures</c> out-parameter (never silently dropped).
    /// </remarks>
    public static class PanelReconstruction
    {
        /// <summary>
        /// Rebuilds <paramref name="resolvedFace3Ds"/> into Panels per the Guid/aperture policy above.
        /// </summary>
        /// <param name="resolvedFace3Ds">The solver's resolved output faces, in flat-ordinal order
        /// (the same order <paramref name="sourceMap"/>'s <see cref="FaceKey"/>s address).</param>
        /// <param name="sources">The original input Panels, index-aligned with <paramref name="sourceMap"/>'s source indices.</param>
        /// <param name="sourceMap">Per-output-face source attribution; never null in practice (the solver always
        /// populates at least the geometric fallback), but a face with an empty source set here still
        /// falls back to <see cref="Modify.NearestSourceIndex"/> so no output is ever left unbuilt.</param>
        /// <param name="bucketSizes">Per-source capture half-width, stamped from the dominant source (index-aligned with <paramref name="sources"/>).</param>
        /// <param name="weights">Per-source backer weight, stamped from the dominant source (index-aligned with <paramref name="sources"/>).</param>
        /// <param name="maxExtends">Per-source lateral extend reach, stamped from the dominant source (index-aligned with <paramref name="sources"/>).</param>
        /// <param name="tolerance">Distance tolerance for the <see cref="Modify.NearestSourceIndex"/> fallback.</param>
        /// <param name="orphanedApertures">Apertures that could not be re-hosted on any output piece their source contributed to.</param>
        /// <param name="minArea">Minimum aperture area to re-host (Panel ctor's own gate).</param>
        /// <param name="maxDistance">Max distance between an output face and an aperture for it to be re-hosted onto that face.</param>
        public static List<Panel> Build(
            List<Face3D> resolvedFace3Ds,
            List<Panel> sources,
            SourceMap sourceMap,
            List<double> bucketSizes,
            List<double> weights,
            List<double> maxExtends,
            double tolerance,
            out List<OrphanedAperture> orphanedApertures,
            double minArea = Tolerance.MacroDistance,
            double maxDistance = Tolerance.MacroDistance)
        {
            orphanedApertures = new List<OrphanedAperture>();

            List<Panel> result = new List<Panel>();
            if (resolvedFace3Ds == null || sources == null || sources.Count == 0)
            {
                return result;
            }

            sourceMap = sourceMap ?? new SourceMap();

            // Which of each source's own aperture Guids actually landed on a built output panel -
            // the orphan sweep below is everything left over.
            Dictionary<int, HashSet<Guid>> attachedBySource = new Dictionary<int, HashSet<Guid>>();

            for (int faceIndex = 0; faceIndex < resolvedFace3Ds.Count; faceIndex++)
            {
                Face3D face3D = resolvedFace3Ds[faceIndex];
                if (face3D == null || !face3D.IsValid())
                {
                    continue;
                }

                // Phase 5b: a fabricated gap-fill patch folded into the resolved set by the consolidation
                // rebuild is emitted as an air panel elsewhere (from the solver's HoleFillFace3Ds, stamped
                // Provenance=GapFill), not as a solid here - skip it so it is not double-built. This guard is
                // inert on every pre-5b path (no GapFill-provenance face ever reached ResolvedFace3Ds before),
                // so it changes no existing (raw-path) output.
                if (sourceMap.ProvenancesOf(new FaceKey(faceIndex)).Contains(Provenance.GapFill)
                    && !sourceMap.HasSource(new FaceKey(faceIndex)))
                {
                    continue;
                }

                List<int> sourceIndices = sourceMap.SourcesOf(new FaceKey(faceIndex))
                    .Where(i => i >= 0 && i < sources.Count && sources[i] != null)
                    .Distinct()
                    .ToList();

                if (sourceIndices.Count == 0)
                {
                    // No attribution at all for this face (should be rare - the solver's own SourceMap
                    // already backfills geometrically): fall back to the plane+centroid heuristic so the
                    // face still gets a Panel rather than being silently dropped.
                    int fallback = Modify.NearestSourceIndex(face3D, sources, tolerance);
                    if (fallback < 0)
                    {
                        continue;
                    }

                    sourceIndices = new List<int> { fallback };
                }

                int dominant = Dominant(sourceIndices, sources);
                Panel dominantSource = sources[dominant];

                bool isMerge = sourceIndices.Count > 1;

                // The dominant source needs a fresh Guid whenever IT ALSO maps to more than one output face -
                // regardless of whether THIS face is additionally a merge with other sources. Without the
                // isMerge exclusion, a dominant source contributing to several distinct merged output faces
                // (e.g. a long wall split into pieces, each piece separately merging with a different
                // neighbour) would stamp the SAME dominantSource.Guid onto multiple Panels - a Guid collision
                // that breaks any Guid-keyed downstream lookup.
                bool isSplitPiece = sourceMap.FacesOf(dominant).Count > 1;

                // A merge keeps the dominant source's own Guid ("wins Guid" - the plan's merge policy);
                // only a split piece needs a fresh Guid, since the source's original Guid can address at
                // most one of its resulting pieces.
                Guid guid = isSplitPiece ? Guid.NewGuid() : dominantSource.Guid;

                // The dominant source's own apertures are re-tried automatically by Create.Panel (it
                // merges panel.Apertures into the candidate list internally); only the OTHER
                // contributing sources' apertures need to be passed explicitly here.
                List<Aperture> extraApertures = new List<Aperture>();
                foreach (int source in sourceIndices)
                {
                    if (source == dominant)
                    {
                        continue;
                    }

                    List<Aperture> apertures = sources[source]?.Apertures;
                    if (apertures != null)
                    {
                        extraApertures.AddRange(apertures);
                    }
                }

                Panel panel = global::SAM.Analytical.Create.Panel(guid, dominantSource, face3D, extraApertures, true, minArea, maxDistance);
                if (panel == null)
                {
                    continue;
                }

                if (isSplitPiece)
                {
                    panel.SetValue(PanelProvenanceParameter.SourceGuid, dominantSource.Guid.ToString());
                }

                if (isMerge)
                {
                    string mergedGuids = string.Join(",", sourceIndices.Where(x => x != dominant).Select(x => sources[x].Guid.ToString()));
                    if (!string.IsNullOrEmpty(mergedGuids))
                    {
                        panel.SetValue(PanelProvenanceParameter.MergedSourceGuids, mergedGuids);
                    }
                }

                StampParameters(panel, dominant, bucketSizes, weights, maxExtends);

                RecordAttached(panel, sourceIndices, attachedBySource);

                result.Add(panel);
            }

            CollectOrphans(sources, attachedBySource, orphanedApertures);

            return result;
        }

        /// <summary>Largest-area source among <paramref name="sourceIndices"/>; ties keep the lowest index (deterministic).</summary>
        private static int Dominant(List<int> sourceIndices, List<Panel> sources)
        {
            int dominant = sourceIndices[0];
            double dominantArea = sources[dominant]?.GetFace3D()?.GetArea() ?? 0;

            foreach (int source in sourceIndices.Skip(1))
            {
                double area = sources[source]?.GetFace3D()?.GetArea() ?? 0;
                if (area > dominantArea)
                {
                    dominant = source;
                    dominantArea = area;
                }
            }

            return dominant;
        }

        private static void StampParameters(Panel panel, int dominant, List<double> bucketSizes, List<double> weights, List<double> maxExtends)
        {
            if (bucketSizes != null && dominant < bucketSizes.Count)
            {
                panel.SetValue(global::SAM.Analytical.Solver.SolverParameter.BucketSize, bucketSizes[dominant]);
            }

            if (weights != null && dominant < weights.Count)
            {
                panel.SetValue(global::SAM.Analytical.Solver.SolverParameter.Weight, weights[dominant]);
            }

            if (maxExtends != null && dominant < maxExtends.Count)
            {
                panel.SetValue(global::SAM.Analytical.Solver.SolverParameter.MaxExtend, maxExtends[dominant]);
            }
        }

        private static void RecordAttached(Panel panel, List<int> sourceIndices, Dictionary<int, HashSet<Guid>> attachedBySource)
        {
            List<Aperture> attached = panel.Apertures ?? new List<Aperture>();
            if (attached.Count == 0)
            {
                return;
            }

            foreach (int source in sourceIndices)
            {
                if (!attachedBySource.TryGetValue(source, out HashSet<Guid> set))
                {
                    set = new HashSet<Guid>();
                    attachedBySource[source] = set;
                }

                foreach (Aperture aperture in attached)
                {
                    if (aperture != null)
                    {
                        set.Add(aperture.Guid);
                    }
                }
            }
        }

        private static void CollectOrphans(List<Panel> sources, Dictionary<int, HashSet<Guid>> attachedBySource, List<OrphanedAperture> orphanedApertures)
        {
            for (int source = 0; source < sources.Count; source++)
            {
                List<Aperture> apertures = sources[source]?.Apertures;
                if (apertures == null || apertures.Count == 0)
                {
                    continue;
                }

                attachedBySource.TryGetValue(source, out HashSet<Guid> attachedGuids);
                foreach (Aperture aperture in apertures)
                {
                    if (aperture == null)
                    {
                        continue;
                    }

                    if (attachedGuids == null || !attachedGuids.Contains(aperture.Guid))
                    {
                        orphanedApertures.Add(new OrphanedAperture(aperture, sources[source].Guid));
                    }
                }
            }
        }
    }
}
