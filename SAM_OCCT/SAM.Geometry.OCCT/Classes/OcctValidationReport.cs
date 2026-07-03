// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    /// <summary>
    /// Result of a native shape validation (issue #37 follow-on): whether the
    /// shape is topologically valid and watertight, plus the located, categorised
    /// issues. Produced by <c>Query.Validate</c> and used both as the gate for
    /// the BOP-glue path (glue is only safe on a clean report) and as the teeth
    /// behind <c>OcctBuildOptions.ValidateInput</c>.
    /// </summary>
    public class OcctValidationReport
    {
        private readonly List<OcctValidationIssue> issues;
        private readonly List<OcctNakedWire> nakedWires;

        public OcctValidationReport(bool isValid, bool isWatertight, IEnumerable<OcctValidationIssue> issues)
            : this(isValid, isWatertight, issues, null)
        {
        }

        public OcctValidationReport(bool isValid, bool isWatertight, IEnumerable<OcctValidationIssue> issues, IEnumerable<OcctNakedWire> nakedWires)
        {
            IsValid = isValid;
            IsWatertight = isWatertight;
            this.issues = issues?.Where(x => x != null).ToList() ?? new List<OcctValidationIssue>();
            this.nakedWires = nakedWires?.Where(x => x != null).ToList() ?? new List<OcctNakedWire>();
        }

        /// <summary>True when BRepCheck_Analyzer and the argument analyzer found no faults.</summary>
        public bool IsValid { get; }

        /// <summary>True when no free (naked) boundary edges were found.</summary>
        public bool IsWatertight { get; }

        public IReadOnlyList<OcctValidationIssue> Issues
        {
            get { return issues; }
        }

        /// <summary>
        /// Free-boundary (naked) wires as ordered polylines with closed flags and best-effort
        /// per-edge owner faces (ABI v4, observational). Empty on a pre-v4 native build; the
        /// located <see cref="Issues"/> of category <see cref="OcctValidationIssueCategory.NakedEdge"/>
        /// remain the always-available naked-edge signal.
        /// </summary>
        public IReadOnlyList<OcctNakedWire> NakedWires
        {
            get { return nakedWires; }
        }

        /// <summary>
        /// True when the shape is both valid and watertight - the precondition
        /// the BOP-glue path is gated on (glue corrupts merely-near-coincident
        /// faces).
        /// </summary>
        public bool IsCleanForGlue
        {
            get { return IsValid && IsWatertight; }
        }

        public IEnumerable<OcctValidationIssue> IssuesOf(OcctValidationIssueCategory category)
        {
            return issues.Where(x => x.Category == category);
        }

        public int CountOf(OcctValidationIssueCategory category)
        {
            return issues.Count(x => x.Category == category);
        }
    }
}
