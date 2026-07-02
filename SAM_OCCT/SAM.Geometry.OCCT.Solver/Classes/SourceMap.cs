// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Stable, opaque identifier for an output face produced by a solver stage. Face3D has no
    /// identity that survives a geometric union/boolean, so stages address their outputs by a
    /// <see cref="FaceKey"/> (a small integer id, typically the face's index in that stage's output
    /// list). <see cref="SourceMap.Compose(SourceMap)"/> chains two stages by treating the first
    /// stage's <see cref="FaceKey"/> values as the second stage's source indices.
    /// </summary>
    public readonly struct FaceKey : IEquatable<FaceKey>
    {
        public int Value { get; }

        public FaceKey(int value)
        {
            Value = value;
        }

        public bool Equals(FaceKey other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is FaceKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return "f" + Value;
        }
    }

    /// <summary>
    /// Threads provenance through the solver: which input source(s) each output face came from, and
    /// how (<see cref="Provenance"/>). Replaces the plane+centroid re-matching heuristic with an
    /// explicit, composable record (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §D/§G). In
    /// Phase 2 this is populated managed-side only (the native Resolve stage records a coarse
    /// whole-set mapping); Phase 3 composes it with the native <c>BRepTools_History</c>.
    /// </summary>
    /// <remarks>
    /// A source index of <see cref="FabricatedSource"/> (-1) marks a face with no input origin (a
    /// gap-fill patch). The map is source-keyed (source index -> the output faces it contributes to),
    /// so a merge is several sources pointing at one <see cref="FaceKey"/> and a split is one source
    /// pointing at several.
    /// </remarks>
    public class SourceMap
    {
        /// <summary>Sentinel source index for a fabricated face (no input origin).</summary>
        public const int FabricatedSource = -1;

        private readonly struct Entry : IEquatable<Entry>
        {
            public FaceKey Key { get; }

            public Provenance Provenance { get; }

            public Entry(FaceKey key, Provenance provenance)
            {
                Key = key;
                Provenance = provenance;
            }

            public bool Equals(Entry other)
            {
                return Key.Equals(other.Key) && Provenance == other.Provenance;
            }

            public override bool Equals(object obj)
            {
                return obj is Entry other && Equals(other);
            }

            public override int GetHashCode()
            {
                return unchecked(Key.GetHashCode() * 397 ^ (int)Provenance);
            }
        }

        private readonly Dictionary<int, List<Entry>> bySource = new Dictionary<int, List<Entry>>();

        /// <summary>The source indices that have at least one recorded output face.</summary>
        public IReadOnlyCollection<int> Sources
        {
            get { return bySource.Keys; }
        }

        /// <summary>Records that <paramref name="source"/> contributes to output <paramref name="key"/>.</summary>
        public void Record(int source, FaceKey key, Provenance provenance)
        {
            if (!bySource.TryGetValue(source, out List<Entry> entries))
            {
                entries = new List<Entry>();
                bySource[source] = entries;
            }

            Entry entry = new Entry(key, provenance);
            if (!entries.Contains(entry))
            {
                entries.Add(entry);
            }
        }

        /// <summary>Records a merge: several <paramref name="sources"/> collapse into one output <paramref name="key"/>.</summary>
        public void RecordMerge(IEnumerable<int> sources, FaceKey key, Provenance provenance)
        {
            if (sources == null)
            {
                return;
            }

            foreach (int source in sources)
            {
                Record(source, key, provenance);
            }
        }

        /// <summary>Records a split: one <paramref name="source"/> becomes several output <paramref name="keys"/>.</summary>
        public void RecordSplit(int source, IEnumerable<FaceKey> keys, Provenance provenance)
        {
            if (keys == null)
            {
                return;
            }

            foreach (FaceKey key in keys)
            {
                Record(source, key, provenance);
            }
        }

        /// <summary>Records a fabricated output face (no input source), e.g. a gap-fill patch.</summary>
        public void RecordFabricated(FaceKey key, Provenance provenance)
        {
            Record(FabricatedSource, key, provenance);
        }

        /// <summary>The output faces (with provenance dropped) that <paramref name="source"/> contributes to.</summary>
        public IReadOnlyList<FaceKey> FacesOf(int source)
        {
            return bySource.TryGetValue(source, out List<Entry> entries)
                ? entries.Select(x => x.Key).Distinct().ToList()
                : new List<FaceKey>();
        }

        /// <summary>The source indices that contribute to output <paramref name="key"/> (reverse lookup).</summary>
        public IReadOnlyList<int> SourcesOf(FaceKey key)
        {
            List<int> result = new List<int>();
            foreach (KeyValuePair<int, List<Entry>> pair in bySource)
            {
                if (pair.Value.Any(x => x.Key.Equals(key)))
                {
                    result.Add(pair.Key);
                }
            }

            return result;
        }

        /// <summary>True when output <paramref name="key"/> has at least one input source (not purely fabricated).</summary>
        public bool HasSource(FaceKey key)
        {
            return bySource.Any(pair => pair.Key != FabricatedSource && pair.Value.Any(x => x.Key.Equals(key)));
        }

        /// <summary>
        /// Chains this map (source -> intermediate <see cref="FaceKey"/>s) with <paramref name="next"/>
        /// (intermediate face -> output <see cref="FaceKey"/>s, keyed by the intermediate
        /// <see cref="FaceKey.Value"/> as its source index), yielding source -> output. A source that
        /// maps to intermediate face 7 which <paramref name="next"/> maps to output faces {2, 5} ends up
        /// mapping to {2, 5}. Merges and splits compose transitively. The composed provenance is
        /// <paramref name="next"/>'s (the later stage's).
        /// </summary>
        public SourceMap Compose(SourceMap next)
        {
            if (next == null)
            {
                return this;
            }

            SourceMap composed = new SourceMap();
            foreach (KeyValuePair<int, List<Entry>> pair in bySource)
            {
                int source = pair.Key;
                foreach (Entry intermediate in pair.Value)
                {
                    // The intermediate face is the "source index" that `next` is keyed against.
                    if (next.bySource.TryGetValue(intermediate.Key.Value, out List<Entry> outputs))
                    {
                        foreach (Entry output in outputs)
                        {
                            composed.Record(source, output.Key, output.Provenance);
                        }
                    }
                }
            }

            return composed;
        }
    }
}
