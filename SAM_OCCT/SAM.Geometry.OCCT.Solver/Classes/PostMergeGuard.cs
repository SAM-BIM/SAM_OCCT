// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using GeometryCreate = SAM.Geometry.OCCT.Create;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// A cell's identity for before/after comparison: volume and axis-aligned bounds. Bounds make the signature
    /// independent of how the cell's boundary is split into faces, which is exactly what a coplanar merge changes.
    /// </summary>
    public sealed class CellSignature
    {
        /// <summary>Index of the cell in the complex it was read from.</summary>
        public int Index { get; }

        /// <summary>Cell volume (m3).</summary>
        public double Volume { get; }

        /// <summary>Lower bounds (x, y, z).</summary>
        public double[] Min { get; }

        /// <summary>Upper bounds (x, y, z).</summary>
        public double[] Max { get; }

        public CellSignature(int index, double volume, double[] min, double[] max)
        {
            Index = index;
            Volume = volume;
            Min = min;
            Max = max;
        }

        /// <summary>Bounding-box midpoint (the native cell centre is the same quantity).</summary>
        public Point3D Midpoint
        {
            get { return new Point3D((Min[0] + Max[0]) / 2, (Min[1] + Max[1]) / 2, (Min[2] + Max[2]) / 2); }
        }

        /// <summary>Signatures of every cell of <paramref name="complex"/> that owns at least one face (bounds come from those faces).</summary>
        public static List<CellSignature> From(ResolvedCellComplex complex)
        {
            List<CellSignature> result = new List<CellSignature>();
            if (complex == null)
            {
                return result;
            }

            Dictionary<int, double[]> min = new Dictionary<int, double[]>();
            Dictionary<int, double[]> max = new Dictionary<int, double[]>();
            foreach (ResolvedCellFace face in complex.Faces)
            {
                BoundingBox3D bounds = face?.Face3D?.GetBoundingBox();
                if (bounds == null)
                {
                    continue;
                }

                foreach (int owner in face.OwnerCellIndices)
                {
                    if (!min.ContainsKey(owner))
                    {
                        min[owner] = new[] { bounds.Min.X, bounds.Min.Y, bounds.Min.Z };
                        max[owner] = new[] { bounds.Max.X, bounds.Max.Y, bounds.Max.Z };
                        continue;
                    }

                    min[owner][0] = System.Math.Min(min[owner][0], bounds.Min.X);
                    min[owner][1] = System.Math.Min(min[owner][1], bounds.Min.Y);
                    min[owner][2] = System.Math.Min(min[owner][2], bounds.Min.Z);
                    max[owner][0] = System.Math.Max(max[owner][0], bounds.Max.X);
                    max[owner][1] = System.Math.Max(max[owner][1], bounds.Max.Y);
                    max[owner][2] = System.Math.Max(max[owner][2], bounds.Max.Z);
                }
            }

            foreach (ResolvedCell cell in complex.Cells)
            {
                if (min.ContainsKey(cell.Index))
                {
                    result.Add(new CellSignature(cell.Index, cell.Volume, min[cell.Index], max[cell.Index]));
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Outcome gate around the native coplanar post-merge of the resolved faces (<see cref="ResolveStage"/>). The merge
    /// unifies coplanar neighbours, but it is global and can change which faces share a boundary: on a tilted two-level
    /// model the merged faces re-decoded to fewer cells than MakerVolume had formed (two rooms vanished, or two rooms
    /// fused into one cell) although the pre-merge faces decode to every cell. The guard re-decodes the merged faces
    /// (the consolidation rebuild's direct path), requires every non-sliver cell of the MakerVolume result to survive
    /// (<see cref="FindLostCells"/>), and otherwise reverts merge groups - those touching the lost cells' boundary
    /// first, then those touching the neighbouring cells, finally all of them - re-verifying after each step. An
    /// accepted merge is returned exactly as the kernel produced it; the guard never merges anything itself.
    /// </summary>
    public static class PostMergeGuard
    {
        /// <summary>Cells below this volume (m3) may vanish when faces merge; same default as <c>Panel3DSnapSolver.MinCellVolume</c>.</summary>
        public const double DefaultMinCellVolume = 0.05;

        /// <summary>Absolute volume tolerance (m3) for matching a cell before and after the merge.</summary>
        public const double VolumeTolerance = 1e-3;

        /// <summary>Relative volume tolerance for matching a cell before and after the merge.</summary>
        public const double RelativeVolumeTolerance = 1e-4;

        /// <summary>Bounds tolerance (m) for matching a cell before and after the merge.</summary>
        public const double BoundsTolerance = 1e-3;

        /// <summary>The guarded merge: the faces to continue with, how they relate to the pre-merge faces, and what was done.</summary>
        public class Result
        {
            /// <summary>Faces to use from here on (the kernel's merge when accepted, otherwise the repaired list or the pre-merge faces).</summary>
            public List<Face3D> Faces { get; set; }

            /// <summary>Pre-merge ordinal to <see cref="Faces"/> ordinal map of a repaired or fully reverted list; null when the kernel's own merge history applies unchanged.</summary>
            public SourceMap RepairedHop { get; set; }

            public PostMergeGuardOutcome Outcome { get; set; }

            /// <summary>Merge groups that combined more than one pre-merge face.</summary>
            public int GroupCount { get; set; }

            /// <summary>Merge groups reverted to their pre-merge faces.</summary>
            public int RevertedGroupCount { get; set; }

            /// <summary>Cells the unrepaired merge would have lost.</summary>
            public List<CellSignature> LostCells { get; set; } = new List<CellSignature>();

            /// <summary>Verification decodes run.</summary>
            public int Decodes { get; set; }
        }

        // ---- Public entry: native verification decode ----

        /// <summary>
        /// Guards the post-merge. <paramref name="hop"/> is the kernel's pre-merge to merged map (null when the history was
        /// unavailable); <paramref name="initialCells"/> is the MakerVolume complex the pre-merge faces came from.
        /// </summary>
        public static Result Evaluate(
            IList<Face3D> preMerge,
            IList<Face3D> merged,
            SourceMap hop,
            ResolvedCellComplex initialCells,
            OcctBuildOptions options,
            SolverDiagnostics diagnostics,
            double minCellVolume = DefaultMinCellVolume)
        {
            OcctBuildOptions verifyOptions = new OcctBuildOptions(options ?? new OcctBuildOptions())
            {
                SewBeforeBuild = false,
                AvoidInternalShapes = false,
                MergeCoplanarBeforeBuild = false
            };

            return Evaluate(preMerge, merged, hop, initialCells, faces => DecodeSignatures(faces, verifyOptions), diagnostics, minCellVolume);
        }

        // ---- Orchestration (decoder injectable so the decision logic is unit-testable without the native kernel) ----

        internal static Result Evaluate(
            IList<Face3D> preMerge,
            IList<Face3D> merged,
            SourceMap hop,
            ResolvedCellComplex initialCells,
            Func<IList<Face3D>, List<CellSignature>> decode,
            SolverDiagnostics diagnostics,
            double minCellVolume)
        {
            Result result = new Result { Faces = merged == null ? null : merged.ToList(), Outcome = PostMergeGuardOutcome.NothingMerged };
            if (preMerge == null || merged == null || merged.Count == 0 || initialCells == null || decode == null)
            {
                return result;
            }

            Dictionary<int, List<int>> inputsOf = GroupsOf(hop);
            result.GroupCount = inputsOf.Count(x => x.Value.Count > 1);
            bool merging = hop == null ? merged.Count != preMerge.Count : result.GroupCount > 0;
            if (!merging)
            {
                return result;
            }

            List<CellSignature> initial = CellSignature.From(initialCells);
            List<CellSignature> after = decode(merged);
            result.Decodes++;
            if (after == null)
            {
                return result; // the kernel could not verify: keep the merge exactly as before
            }

            List<CellSignature> lost = FindLostCells(initial, after, minCellVolume);
            if (lost.Count == 0)
            {
                result.Outcome = PostMergeGuardOutcome.Accepted;
                return result;
            }

            result.LostCells = lost;
            List<int> lostIndices = lost.Select(x => x.Index).ToList();

            HashSet<int> boundary = null;
            bool lineageUsable = hop != null && TryBoundaryOrdinals(initialCells, lostIndices, preMerge.Count, out boundary);
            if (lineageUsable)
            {
                Dictionary<int, List<int>> forward = ForwardOf(hop);

                // Repair 1: groups with a face on the boundary of a lost cell.
                if (TryRepair(preMerge, merged, hop, inputsOf, forward, LineageGroups(inputsOf, boundary), decode, initial, minCellVolume, result, PostMergeGuardOutcome.Repaired))
                {
                    Report(diagnostics, result);
                    return result;
                }

                // Repair 2: widen to the cells adjacent to the lost ones.
                List<int> widened = lostIndices.Concat(AdjacentCells(initialCells, lostIndices)).Distinct().ToList();
                if (TryBoundaryOrdinals(initialCells, widened, preMerge.Count, out HashSet<int> wideBoundary)
                    && TryRepair(preMerge, merged, hop, inputsOf, forward, LineageGroups(inputsOf, wideBoundary), decode, initial, minCellVolume, result, PostMergeGuardOutcome.Widened))
                {
                    Report(diagnostics, result);
                    return result;
                }
            }

            // Fall back: every group reverted (the pre-merge faces decode to every initial cell by construction).
            result.Faces = preMerge.ToList();
            result.RepairedHop = IdentityHop(preMerge.Count);
            result.RevertedGroupCount = result.GroupCount;
            result.Outcome = PostMergeGuardOutcome.FellBack;
            Report(diagnostics, result);
            return result;
        }

        private static bool TryRepair(
            IList<Face3D> preMerge,
            IList<Face3D> merged,
            SourceMap hop,
            Dictionary<int, List<int>> inputsOf,
            Dictionary<int, List<int>> forward,
            HashSet<int> revert,
            Func<IList<Face3D>, List<CellSignature>> decode,
            List<CellSignature> initial,
            double minCellVolume,
            Result result,
            PostMergeGuardOutcome success)
        {
            if (revert.Count == 0)
            {
                return false;
            }

            List<Face3D> repaired = RevertGroups(preMerge, merged, hop, inputsOf, forward, revert, out SourceMap repairedHop);
            if (repaired == null)
            {
                return false; // a restored input also feeds a merged face that stays: would duplicate coverage
            }

            List<CellSignature> after = decode(repaired);
            result.Decodes++;
            if (after == null || FindLostCells(initial, after, minCellVolume).Count != 0)
            {
                return false;
            }

            result.Faces = repaired;
            result.RepairedHop = repairedHop;
            result.RevertedGroupCount = revert.Count;
            result.Outcome = success;
            return true;
        }

        // ---- Pure helpers (unit-tested) ----

        /// <summary>
        /// Initial cells (volume at least <paramref name="minCellVolume"/>) with no one-to-one counterpart in <paramref name="after"/>
        /// within the volume and bounds tolerances. Smaller cells may vanish when faces merge and are not reported.
        /// </summary>
        public static List<CellSignature> FindLostCells(IList<CellSignature> initial, IList<CellSignature> after, double minCellVolume = DefaultMinCellVolume)
        {
            List<CellSignature> lost = new List<CellSignature>();
            if (initial == null)
            {
                return lost;
            }

            bool[] used = new bool[after?.Count ?? 0];
            foreach (CellSignature cell in initial)
            {
                if (cell.Volume < minCellVolume)
                {
                    continue;
                }

                int best = -1;
                double bestDifference = double.MaxValue;
                for (int i = 0; i < used.Length; i++)
                {
                    if (used[i] || !Matches(cell, after[i]))
                    {
                        continue;
                    }

                    double difference = System.Math.Abs(cell.Volume - after[i].Volume);
                    if (difference < bestDifference)
                    {
                        bestDifference = difference;
                        best = i;
                    }
                }

                if (best < 0)
                {
                    lost.Add(cell);
                    continue;
                }

                used[best] = true;
            }

            return lost;
        }

        private static bool Matches(CellSignature a, CellSignature b)
        {
            if (System.Math.Abs(a.Volume - b.Volume) > System.Math.Max(VolumeTolerance, RelativeVolumeTolerance * System.Math.Abs(a.Volume)))
            {
                return false;
            }

            for (int k = 0; k < 3; k++)
            {
                if (System.Math.Abs(a.Min[k] - b.Min[k]) > BoundsTolerance || System.Math.Abs(a.Max[k] - b.Max[k]) > BoundsTolerance)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Merged ordinal to the (sorted) pre-merge ordinals it was built from, read from the kernel's history map. Empty when <paramref name="hop"/> is null.</summary>
        public static Dictionary<int, List<int>> GroupsOf(SourceMap hop)
        {
            Dictionary<int, List<int>> inputsOf = new Dictionary<int, List<int>>();
            if (hop == null)
            {
                return inputsOf;
            }

            foreach (int source in hop.Sources.OrderBy(x => x))
            {
                foreach (FaceKey key in hop.FacesOf(source))
                {
                    if (!inputsOf.TryGetValue(key.Value, out List<int> inputs))
                    {
                        inputs = new List<int>();
                        inputsOf[key.Value] = inputs;
                    }

                    inputs.Add(source);
                }
            }

            return inputsOf;
        }

        /// <summary>Pre-merge ordinal to the merged ordinals it feeds.</summary>
        public static Dictionary<int, List<int>> ForwardOf(SourceMap hop)
        {
            Dictionary<int, List<int>> forward = new Dictionary<int, List<int>>();
            if (hop == null)
            {
                return forward;
            }

            foreach (int source in hop.Sources)
            {
                forward[source] = hop.FacesOf(source).Select(x => x.Value).ToList();
            }

            return forward;
        }

        /// <summary>
        /// Pre-merge ordinals that lie on the boundary of the given cells, from the complex's face owners and flat ordinals
        /// (<c>FlatOrdinals[k]</c> belongs to <c>OwnerCellIndices[k]</c>). False when the flat ordinals do not address exactly the
        /// <paramref name="preMergeCount"/> pre-merge faces (an invalid face was filtered), so no lineage is claimed.
        /// </summary>
        public static bool TryBoundaryOrdinals(ResolvedCellComplex complex, IEnumerable<int> cellIndices, int preMergeCount, out HashSet<int> ordinals)
        {
            ordinals = new HashSet<int>();
            if (complex == null)
            {
                return false;
            }

            HashSet<int> cells = new HashSet<int>(cellIndices ?? Enumerable.Empty<int>());
            HashSet<int> seen = new HashSet<int>();
            foreach (ResolvedCellFace face in complex.Faces)
            {
                if (face.OwnerCellIndices.Count != face.FlatOrdinals.Count)
                {
                    return false;
                }

                for (int k = 0; k < face.FlatOrdinals.Count; k++)
                {
                    int ordinal = face.FlatOrdinals[k];
                    if (ordinal < 0 || ordinal >= preMergeCount || !seen.Add(ordinal))
                    {
                        return false;
                    }

                    if (cells.Contains(face.OwnerCellIndices[k]))
                    {
                        ordinals.Add(ordinal);
                    }
                }
            }

            return seen.Count == preMergeCount && complex.TopologyKeyZeroFaceCount == 0;
        }

        /// <summary>Cells that share a face with any of <paramref name="cellIndices"/>, excluding those cells themselves.</summary>
        public static List<int> AdjacentCells(ResolvedCellComplex complex, IEnumerable<int> cellIndices)
        {
            HashSet<int> cells = new HashSet<int>(cellIndices ?? Enumerable.Empty<int>());
            HashSet<int> neighbours = new HashSet<int>();
            foreach (ResolvedFaceAdjacency adjacency in complex?.Adjacencies ?? new List<ResolvedFaceAdjacency>())
            {
                bool first = cells.Contains(adjacency.CellIndex1);
                bool second = cells.Contains(adjacency.CellIndex2);
                if (first && !second)
                {
                    neighbours.Add(adjacency.CellIndex2);
                }
                else if (second && !first)
                {
                    neighbours.Add(adjacency.CellIndex1);
                }
            }

            return neighbours.OrderBy(x => x).ToList();
        }

        /// <summary>Merged ordinals of the groups that combined more than one face and include a pre-merge face from <paramref name="boundaryOrdinals"/>.</summary>
        public static HashSet<int> LineageGroups(Dictionary<int, List<int>> inputsOf, ISet<int> boundaryOrdinals)
        {
            HashSet<int> groups = new HashSet<int>();
            foreach (KeyValuePair<int, List<int>> group in inputsOf)
            {
                if (group.Value.Count > 1 && group.Value.Any(boundaryOrdinals.Contains))
                {
                    groups.Add(group.Key);
                }
            }

            return groups;
        }

        /// <summary>
        /// The merged list with each group in <paramref name="revert"/> replaced, at its own position, by its pre-merge faces
        /// (ascending ordinal); all other merged faces keep their order. <paramref name="repairedHop"/> maps every covered pre-merge
        /// ordinal to its face in the returned list. Null when a restored face also feeds a merged face that stays in the list
        /// (the substitution would cover that area twice).
        /// </summary>
        public static List<Face3D> RevertGroups(
            IList<Face3D> preMerge,
            IList<Face3D> merged,
            SourceMap hop,
            Dictionary<int, List<int>> inputsOf,
            Dictionary<int, List<int>> forward,
            ISet<int> revert,
            out SourceMap repairedHop)
        {
            repairedHop = null;
            foreach (int group in revert)
            {
                if (!inputsOf.TryGetValue(group, out List<int> inputs))
                {
                    return null;
                }

                if (inputs.Any(u => u < 0 || u >= preMerge.Count || (forward.TryGetValue(u, out List<int> images) && images.Any(m => !revert.Contains(m)))))
                {
                    return null;
                }
            }

            List<Face3D> result = new List<Face3D>();
            Dictionary<int, int> newIndexOfMerged = new Dictionary<int, int>();
            Dictionary<int, int> newIndexOfRestored = new Dictionary<int, int>();
            for (int i = 0; i < merged.Count; i++)
            {
                if (!revert.Contains(i))
                {
                    newIndexOfMerged[i] = result.Count;
                    result.Add(merged[i]);
                    continue;
                }

                foreach (int u in inputsOf[i].OrderBy(x => x))
                {
                    newIndexOfRestored[u] = result.Count;
                    result.Add(preMerge[u]);
                }
            }

            SourceMap map = new SourceMap();
            foreach (int source in hop.Sources)
            {
                foreach (FaceKey key in hop.FacesOf(source))
                {
                    if (newIndexOfMerged.TryGetValue(key.Value, out int index))
                    {
                        map.Record(source, new FaceKey(index), Provenance.Resolved);
                    }
                }
            }

            foreach (KeyValuePair<int, int> restored in newIndexOfRestored)
            {
                map.Record(restored.Key, new FaceKey(restored.Value), Provenance.Resolved);
            }

            repairedHop = map;
            return result;
        }

        /// <summary>The identity map over <paramref name="count"/> pre-merge faces (every group reverted).</summary>
        public static SourceMap IdentityHop(int count)
        {
            SourceMap map = new SourceMap();
            for (int i = 0; i < count; i++)
            {
                map.Record(i, new FaceKey(i), Provenance.Resolved);
            }

            return map;
        }

        // ---- Native verification decode ----

        private static List<CellSignature> DecodeSignatures(IList<Face3D> faces, OcctBuildOptions options)
        {
            List<Face3D> valid = faces?.Where(x => x != null && x.IsValid()).ToList();
            if (valid == null || valid.Count == 0)
            {
                return null;
            }

            GeometryCreate.Shells(valid, out OcctCellComplexResult native, options);
            if (native == null)
            {
                return null;
            }

            try
            {
                if (!native.NativeAvailable)
                {
                    return null;
                }

                return CellSignature.From(ResolvedCellComplex.Project(native, null, Guid.Empty));
            }
            finally
            {
                native.Dispose();
            }
        }

        // ---- Diagnostics ----

        private static void Report(SolverDiagnostics diagnostics, Result result)
        {
            if (diagnostics == null || result.LostCells.Count == 0)
            {
                return;
            }

            string lostText = string.Join("; ", result.LostCells.Take(5).Select(x =>
            {
                Point3D centre = x.Midpoint;
                return string.Format(CultureInfo.InvariantCulture, "{0:0.##} m3 at ({1:0.##}, {2:0.##}, {3:0.##}) m", x.Volume, centre.X, centre.Y, centre.Z);
            }));
            string more = result.LostCells.Count > 5 ? string.Format(CultureInfo.InvariantCulture, " and {0} more", result.LostCells.Count - 5) : string.Empty;
            string action = result.Outcome == PostMergeGuardOutcome.FellBack
                ? "All coplanar merge groups were reverted"
                : string.Format(CultureInfo.InvariantCulture, "{0} of {1} coplanar merge group(s) were reverted ({2})", result.RevertedGroupCount, result.GroupCount, result.Outcome == PostMergeGuardOutcome.Widened ? "widened to the neighbouring cells" : "groups on the lost cells' boundary");

            diagnostics.Add(SolverStage.Resolve, DiagnosticCode.RejectedMerge, OcctDiagnosticSeverity.Warning,
                string.Format(CultureInfo.InvariantCulture, "{0} because merging the resolved faces would have lost {1} cell(s): {2}{3}. The pre-merge faces are used for those groups.", action, result.LostCells.Count, lostText, more));
        }
    }
}
