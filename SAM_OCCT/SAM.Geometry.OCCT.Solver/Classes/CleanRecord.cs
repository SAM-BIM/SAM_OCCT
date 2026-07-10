// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// One observed Stage A clean decision, captured at its REAL mutation site (opposed collapse, bucket snap,
    /// cap normalization, coplanar merge, drop) rather than inferred from a before/after geometry diff
    /// (docs/CONTROLLED_WORKFLOW_PLAN.md §4.4). The analogue of <see cref="ExtendRecord"/> for the clean stage:
    /// recording only - a null recorder is byte-identical to the geometry (proven by
    /// <c>CleanRecordTests</c>). Multiple records per source are allowed (a panel can be snapped in one pass and
    /// merged in another).
    /// <para>Identity is solver-side (source-face indices); the analytical layer resolves them to the source
    /// panel's Guid and joins the resolved BucketSize/Weight/MaxExtend and their provenance tags when it formats
    /// the <c>SAM_OCCT_CLEAN3D_PANEL:</c> line (the geometry solver carries no Guids or parameter provenance).</para>
    /// </summary>
    public class CleanRecord
    {
        /// <summary>Representative source-face index of the panel this record is about (its first
        /// <see cref="SnappedPanel.SourceIndices"/>); -1 when the panel carries no source.</summary>
        public int SourceIndex { get; }

        /// <summary>Representative source-face index of the backer/datum this panel was snapped onto (-1 for a
        /// drop, a coplanar-merge dominant that is itself, or where there is no distinct backer).</summary>
        public int BackerSourceIndex { get; }

        /// <summary>Which clean decision this record captured.</summary>
        public CleanRecordKind Kind { get; }

        /// <summary>The perpendicular distance (metres) the panel moved onto its backer/datum (0 for a
        /// coplanar merge / drop, where nothing was translated).</summary>
        public double DistanceMoved { get; }

        /// <summary>Index of the <see cref="LevelGroup"/> the cap normalized against, for
        /// <see cref="CleanRecordKind.CapNormalized"/>; -1 for the other kinds (and when no group applies).</summary>
        public int LevelGroupIndex { get; }

        public CleanRecord(int sourceIndex, int backerSourceIndex, CleanRecordKind kind, double distanceMoved, int levelGroupIndex = -1)
        {
            SourceIndex = sourceIndex;
            BackerSourceIndex = backerSourceIndex;
            Kind = kind;
            DistanceMoved = distanceMoved;
            LevelGroupIndex = levelGroupIndex;
        }

        /// <summary>The kebab-case action text used in the <c>SAM_OCCT_CLEAN3D_PANEL:</c> line
        /// (<c>opposed-collapsed</c>, <c>snapped-to-backer</c>, <c>cap-normalized</c>, <c>coplanar-merged</c>,
        /// <c>dropped-invalid</c>, <c>preserved</c>).</summary>
        public string KindText()
        {
            switch (Kind)
            {
                case CleanRecordKind.OpposedCollapsed: return "opposed-collapsed";
                case CleanRecordKind.SnappedToBacker: return "snapped-to-backer";
                case CleanRecordKind.CapNormalized: return "cap-normalized";
                case CleanRecordKind.CoplanarMerged: return "coplanar-merged";
                case CleanRecordKind.DroppedInvalid: return "dropped-invalid";
                case CleanRecordKind.Preserved: return "preserved";
                default: return Kind.ToString();
            }
        }
    }
}
