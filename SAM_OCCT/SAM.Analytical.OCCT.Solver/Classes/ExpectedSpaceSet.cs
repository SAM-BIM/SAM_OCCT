// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OCCT.Solver
{
    /// <summary>
    /// A GUID-keyed set of expected <see cref="Space"/>s plus the level-group datums inferred from a
    /// supplied set of input panels, ready to be matched against generated cells by
    /// <see cref="SpaceMatcher"/> (docs/CONTROLLED_WORKFLOW_PLAN.md §6). Identity is <see cref="Space.Guid"/>
    /// throughout; <see cref="Space.Name"/> is only ever a report label.
    /// </summary>
    /// <remarks>
    /// Level groups are inferred here independently of the (not-yet-landed) P2 <c>bucketBetweenLevels</c>
    /// plumbing: raw <see cref="LevelFrame.Cluster"/> (its pinned 0.15 m band, unchanged) followed by a
    /// self-contained 1-D dominant-first, non-transitive merge of the resulting frame elevations at
    /// <see cref="SpaceMatchOptions.LevelGroupBand"/> - the same shape of algorithm P2's
    /// <c>LevelFrame.GroupFrames</c> will formalise, but owned entirely by this class so P1 does not
    /// depend on P2 landing first.
    /// </remarks>
    public class ExpectedSpaceSet
    {
        private const double DEFAULT_CapNormalZ_ConeTolerance = 20.0 * (System.Math.PI / 180.0);

        /// <summary>Floating-point equality tolerance for "z sits on this datum" in <see cref="ComputeSpan"/>'s ordinary-space bracket search - deliberately far tighter than <see cref="SpaceMatchOptions.LevelBand"/>, which would let z borrow into the next bracket up.</summary>
        private const double DatumEqualityTolerance = 1e-6;

        private readonly Dictionary<Guid, Space> spacesByGuid;
        private readonly Dictionary<Guid, double[]> levelSpans;
        private readonly Dictionary<Guid, string> labels;

        /// <summary>The expected spaces, GUID-unique, in the order supplied.</summary>
        public IReadOnlyList<Space> Spaces { get; }

        /// <summary>Report-facing label per space Guid: the space's own <see cref="Space.Name"/>, or
        /// <c>"{Name}#{n}"</c> when two or more spaces share a name (a duplicate-name warning is also
        /// recorded in <see cref="Warnings"/>).</summary>
        public IReadOnlyDictionary<Guid, string> Labels => labels;

        /// <summary>Non-fatal findings collected while building this set (currently: duplicate-name disambiguation).</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Merged level-group datums, ascending (empty when no cap could be extracted from the supplied panels).</summary>
        public IReadOnlyList<double> LevelGroupDatums { get; }

        /// <summary>Per-space expected vertical span <c>[floor, ceiling]</c>, keyed by Guid; absent for a space with a null/invalid <see cref="Space.Location"/>.</summary>
        public IReadOnlyDictionary<Guid, double[]> LevelSpans => levelSpans;

        public SpaceMatchOptions Options { get; }

        private ExpectedSpaceSet(
            List<Space> spaces,
            Dictionary<Guid, string> labels,
            List<string> warnings,
            List<double> levelGroupDatums,
            Dictionary<Guid, double[]> levelSpans,
            SpaceMatchOptions options)
        {
            Spaces = spaces;
            this.labels = labels;
            Warnings = warnings;
            LevelGroupDatums = levelGroupDatums;
            this.levelSpans = levelSpans;
            Options = options;
            spacesByGuid = spaces.ToDictionary(x => x.Guid, x => x);
        }

        /// <summary>Look up an expected space by Guid, or null when not present.</summary>
        public Space TryGetSpace(Guid guid)
        {
            return spacesByGuid.TryGetValue(guid, out Space space) ? space : null;
        }

        /// <summary>The expected spaces as seeds for <c>SAM.Analytical.OCCT.Create.AdjacencyCluster</c>'s rebuild path.</summary>
        public List<Space> ToSeedSpaces()
        {
            return new List<Space>(Spaces);
        }

        /// <summary>
        /// Builds an <see cref="ExpectedSpaceSet"/> from expected spaces and the input panels that define
        /// the model's level datums. Hard-fails (throws <see cref="ArgumentException"/>) on a null/empty or
        /// multiline name - a multiline name breaks the single-line report grammar and cannot be
        /// disambiguated - and on a duplicate <see cref="Space.Guid"/> (a genuine identity collision, never
        /// legitimate). A duplicate NAME is not fatal: it is disambiguated into a stable <c>Name#n</c>
        /// <see cref="Labels"/> entry and recorded in <see cref="Warnings"/> (plan §6: "duplicate/multiline
        /// names -&gt; warnings + disambiguated labels in the general component"; the controlled fixture's
        /// own hard nine-distinct-name requirement is a fixture-level gate, asserted by its own test, not by
        /// this general-purpose class).
        /// </summary>
        /// <param name="spaces">The expected spaces.</param>
        /// <param name="levelSourcePanels">Input panels whose cap faces seed the level-group datums (self-contained; independent of any Clean3D/Extend3D level-group plumbing).</param>
        /// <param name="options">Matching options; a copy is taken.</param>
        /// <param name="doubleHeightGuids">Guids of spaces known to be double-height (never inferred from location).</param>
        public static ExpectedSpaceSet Create(
            IEnumerable<Space> spaces,
            IEnumerable<Panel> levelSourcePanels,
            SpaceMatchOptions options = null,
            IEnumerable<Guid> doubleHeightGuids = null)
        {
            SpaceMatchOptions resolvedOptions = new SpaceMatchOptions(options ?? new SpaceMatchOptions());

            List<Space> spaceList = (spaces ?? Enumerable.Empty<Space>()).Where(x => x != null).ToList();

            HashSet<Guid> seenGuids = new HashSet<Guid>();
            foreach (Space space in spaceList)
            {
                if (string.IsNullOrWhiteSpace(space.Name))
                {
                    throw new ArgumentException(string.Format("ExpectedSpaceSet: space {0} has an empty Name.", space.Guid));
                }

                if (space.Name.IndexOf('\n') >= 0 || space.Name.IndexOf('\r') >= 0)
                {
                    throw new ArgumentException(string.Format("ExpectedSpaceSet: space {0} name \"{1}\" is multiline - not usable as a single-line report label.", space.Guid, space.Name.Replace("\n", "\\n").Replace("\r", "\\r")));
                }

                if (!seenGuids.Add(space.Guid))
                {
                    throw new ArgumentException(string.Format("ExpectedSpaceSet: duplicate space Guid {0}.", space.Guid));
                }
            }

            List<string> warnings = new List<string>();
            Dictionary<Guid, string> labels = new Dictionary<Guid, string>();
            foreach (IGrouping<string, Space> group in spaceList.GroupBy(x => x.Name).OrderBy(x => x.Key))
            {
                List<Space> members = group.OrderBy(x => x.Guid).ToList();
                if (members.Count == 1)
                {
                    labels[members[0].Guid] = members[0].Name;
                    continue;
                }

                warnings.Add(string.Format(
                    "ExpectedSpaceSet: {0} spaces share the name \"{1}\" ({2}); disambiguated as {3}.",
                    members.Count, group.Key, string.Join(",", members.Select(x => x.Guid)),
                    string.Join(",", Enumerable.Range(1, members.Count).Select(i => i == 1 ? group.Key : string.Format("{0}#{1}", group.Key, i)))));

                for (int i = 0; i < members.Count; i++)
                {
                    labels[members[i].Guid] = i == 0 ? members[i].Name : string.Format("{0}#{1}", members[i].Name, i + 1);
                }
            }

            List<Face3D> caps = ExtractCaps(levelSourcePanels);
            List<LevelFrame> frames = LevelFrame.Cluster(caps);
            List<double> levelGroupDatums = MergeFrameElevations(frames, resolvedOptions.LevelGroupBand);

            HashSet<Guid> doubleHeightSet = new HashSet<Guid>(doubleHeightGuids ?? Enumerable.Empty<Guid>());
            Dictionary<Guid, double[]> spans = new Dictionary<Guid, double[]>();
            foreach (Space space in spaceList)
            {
                double[] span = ComputeSpan(space.Location, levelGroupDatums, doubleHeightSet.Contains(space.Guid), resolvedOptions.LevelBand);
                if (span != null)
                {
                    spans[space.Guid] = span;
                }
            }

            return new ExpectedSpaceSet(spaceList, labels, warnings, levelGroupDatums, spans, resolvedOptions);
        }

        /// <summary>Non-vertical (cap) faces of the supplied panels - the same |normal.Z (hemisphere)| >= cos(20 deg) test the rest of this codebase's diagnostics use.</summary>
        private static List<Face3D> ExtractCaps(IEnumerable<Panel> panels)
        {
            List<Face3D> result = new List<Face3D>();
            if (panels == null)
            {
                return result;
            }

            double capNormalZ = System.Math.Cos(DEFAULT_CapNormalZ_ConeTolerance);
            foreach (Panel panel in panels)
            {
                Face3D face3D = panel?.GetFace3D();
                Plane plane = face3D?.GetPlane();
                if (plane == null)
                {
                    continue;
                }

                Vector3D normal = plane.Normal.Unit;
                if (System.Math.Abs(normal.Z) >= capNormalZ)
                {
                    result.Add(face3D);
                }
            }

            return result;
        }

        /// <summary>Dominant-area-first, seed-anchored, non-transitive 1-D merge of level-frame elevations
        /// into level-group datums, ascending. Mirrors <see cref="LevelFrame.Cluster"/>'s own seeding
        /// discipline (deterministic; a datum is anchored by its dominant-area seed and does not chain
        /// across a wide span) applied to already-clustered frame elevations rather than raw caps.</summary>
        private static List<double> MergeFrameElevations(List<LevelFrame> frames, double band)
        {
            List<double> result = new List<double>();
            if (frames == null || frames.Count == 0)
            {
                return result;
            }

            List<LevelFrame> ordered = frames
                .OrderByDescending(x => x.DominantArea)
                .ThenBy(x => x.Elevation)
                .ToList();

            bool[] assigned = new bool[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                if (assigned[i])
                {
                    continue;
                }

                LevelFrame seed = ordered[i];
                assigned[i] = true;

                for (int j = i + 1; j < ordered.Count; j++)
                {
                    if (assigned[j])
                    {
                        continue;
                    }

                    if (System.Math.Abs(ordered[j].Elevation - seed.Elevation) <= band)
                    {
                        assigned[j] = true;
                    }
                }

                result.Add(seed.Elevation);
            }

            result.Sort();
            return result;
        }

        /// <summary>The expected <c>[floor, ceiling]</c> datum bracket for <paramref name="location"/>: the
        /// two consecutive datums it falls between for an ordinary space, or - when
        /// <paramref name="doubleHeight"/> is true - the bracket widened by one extra datum above (skipping
        /// the intermediate datum the double-height space's own location sits near). Null for a null
        /// location or an empty datum list.</summary>
        private static double[] ComputeSpan(Point3D location, List<double> datums, bool doubleHeight, double band)
        {
            if (location == null || datums == null || datums.Count == 0)
            {
                return null;
            }

            if (datums.Count == 1)
            {
                return new double[] { datums[0], datums[0] };
            }

            double z = location.Z;

            if (doubleHeight)
            {
                // A double-height location sits near (within band of) the intermediate datum it is meant to
                // skip - not reliably above or below it (plan §6: "ambiguous vertical assignment"). Rather
                // than bracket z directly, find the nearest datum strictly below z - band and the nearest
                // datum strictly above z + band: every datum within the band of z itself is excluded, so the
                // span always skips the intermediate datum regardless of which side z's noise lands on.
                int floorIndex = -1;
                for (int i = 0; i < datums.Count; i++)
                {
                    if (datums[i] < z - band)
                    {
                        floorIndex = i;
                    }
                }

                if (floorIndex < 0)
                {
                    floorIndex = 0;
                }

                int ceilingIndex = -1;
                for (int i = datums.Count - 1; i >= 0; i--)
                {
                    if (datums[i] > z + band)
                    {
                        ceilingIndex = i;
                    }
                }

                if (ceilingIndex < 0)
                {
                    ceilingIndex = datums.Count - 1;
                }

                if (ceilingIndex <= floorIndex)
                {
                    ceilingIndex = System.Math.Min(floorIndex + 1, datums.Count - 1);
                }

                return new double[] { datums[floorIndex], datums[ceilingIndex] };
            }

            // Ordinary space: the single consecutive datum pair z falls between (no band widening - a band
            // here would let z borrow into the NEXT bracket up, mis-bracketing a location merely close to
            // the next datum).
            int ordinaryFloorIndex = 0;
            for (int i = 0; i < datums.Count; i++)
            {
                if (datums[i] <= z + DatumEqualityTolerance)
                {
                    ordinaryFloorIndex = i;
                }
            }

            ordinaryFloorIndex = System.Math.Min(ordinaryFloorIndex, datums.Count - 2);
            return new double[] { datums[ordinaryFloorIndex], datums[ordinaryFloorIndex + 1] };
        }
    }
}
