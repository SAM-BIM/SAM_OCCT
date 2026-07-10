// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// One applied managed extend/fill operation, captured for observability (E3,
    /// docs/EXTEND3D_ROBUST_HANDOVER.md). Recording only: the E1/E2 primitives produce the geometry
    /// unchanged; the solver measures each mutation (which panel, which edge, from where to where,
    /// toward what target, with what overshoot) and hands the record to the report/Grasshopper layer.
    /// Surfaced as a coded <c>SAM_OCCT_EXTEND3D_PANEL:</c> line (via
    /// <see cref="SAM.Geometry.OCCT.Solver"/> consumers) and a moved-edge preview segment.
    /// <para>Identity is solver-side: <see cref="PanelIndex"/> is the panel's position in the
    /// conditioned <see cref="Panel3DSnapSolver.SnappedPanels"/> list and <see cref="SourceIndex"/> is
    /// its representative source-face index. The analytical layer resolves those to the source panel's
    /// Guid when it formats the line (the geometry solver has no Guids).</para>
    /// </summary>
    public class ExtendRecord
    {
        /// <summary>Position of the mutated panel in the conditioned <see cref="Panel3DSnapSolver.SnappedPanels"/> list.</summary>
        public int PanelIndex { get; }

        /// <summary>Representative source-face index of the mutated panel. Emitted as the panel's clean-face
        /// ordinal (its first <see cref="SnappedPanel.SourceIndices"/>), then remapped by the solver to the
        /// ORIGINAL input source index (via the snap stage's per-clean-face attribution) before the analytical
        /// layer resolves it to a source panel's Guid; -1 when the panel carries no source.</summary>
        public int SourceIndex { get; internal set; }

        /// <summary>Which primitive was applied.</summary>
        public ExtendOperationKind Kind { get; }

        /// <summary>The measured quantity BEFORE the move (metres/parameter, per <see cref="MeasureUnit"/>).</summary>
        public double FromValue { get; }

        /// <summary>The measured quantity AFTER the move.</summary>
        public double ToValue { get; }

        /// <summary>What <see cref="FromValue"/>/<see cref="ToValue"/> measure: <c>elevation</c> and
        /// <c>distance-to-plane</c> for vertical extends, <c>plan</c> for a lateral foot move, <c>area</c>
        /// for a cap grow.</summary>
        public string MeasureUnit { get; }

        /// <summary>A representative point on the moved edge BEFORE the move (world frame); null for a cap grow
        /// (no single edge). Paired with <see cref="To"/> as the moved-edge preview segment.</summary>
        public Point3D From { get; internal set; }

        /// <summary>A representative point on the moved edge AFTER the move (world frame); null for a cap grow.</summary>
        public Point3D To { get; internal set; }

        /// <summary>Position of the target cap in the conditioned panel list (-1 for a wall-to-wall or fixed-margin move).</summary>
        public int TargetPanelIndex { get; }

        /// <summary>Representative source-face index of the target cap (-1 when there is no cap target).
        /// Emitted as the cap's clean-face ordinal, then remapped by the solver to the ORIGINAL input source
        /// index, exactly like <see cref="SourceIndex"/>.</summary>
        public int TargetSourceIndex { get; internal set; }

        /// <summary>Which target rule was taken: <c>cap-scalar</c> (E1 flat-Z), <c>cap-plane</c> (E2 sloped plane),
        /// <c>walls</c> (the 2D plan-loop solver), <c>walls-measured</c> or <c>fixed-margin</c> (cap grow).</summary>
        public string TargetKind { get; }

        /// <summary>Human-readable target detail (e.g. the target elevation, or the cap plane normal for the sloped branch).</summary>
        public string TargetDescription { get; }

        /// <summary>The overshoot (metres) applied past the target so the native kernel gets a clean crossing.</summary>
        public double Overshoot { get; }

        /// <summary>True when a LATERAL move reached the panel's extension cap (<c>min(MaxExtend, 0.49 * length)</c>);
        /// always false for the MaxExtend-uncapped vertical extends.</summary>
        public bool MaxExtendCapped { get; }

        /// <summary>Applied (something moved) or Skipped (a decision point found no safe target and left the
        /// panel untouched) - P3, docs/CONTROLLED_WORKFLOW_PLAN.md §5.4. Defaults to <see cref="ExtendOutcome.Applied"/>
        /// for every record built through the (frozen) primary constructor; only <see cref="Skip"/> produces a
        /// <see cref="ExtendOutcome.Skipped"/> record.</summary>
        public ExtendOutcome Outcome { get; private set; } = ExtendOutcome.Applied;

        /// <summary>Why a <see cref="ExtendOutcome.Skipped"/> record's panel was left untouched; null for an
        /// Applied record.</summary>
        public ExtendSkipReason? SkipReason { get; private set; }

        /// <summary>Human-readable detail for a <see cref="ExtendOutcome.Skipped"/> record (e.g. the measured
        /// gap and the reach that fell short); empty for an Applied record.</summary>
        public string SkipDetail { get; private set; } = string.Empty;

        private readonly List<ExtendRiskFlag> riskFlags = new List<ExtendRiskFlag>();

        /// <summary>Metadata flags on an APPLIED record worth a reviewer's attention (P3 §5.5) - near a reach
        /// limit, a fallback to a less precise primitive, a new coplanar overlap. Never populated on a Skipped
        /// record (a skip already explains itself via <see cref="SkipReason"/>). Empty by default.</summary>
        public IReadOnlyList<ExtendRiskFlag> RiskFlags => riskFlags;

        /// <summary>Appends a risk flag (P3 §5.5); metadata only - never changes <see cref="Outcome"/>.</summary>
        public void AddRisk(ExtendRiskFlag riskFlag)
        {
            riskFlags.Add(riskFlag);
        }

        public ExtendRecord(
            int panelIndex,
            int sourceIndex,
            ExtendOperationKind kind,
            double fromValue,
            double toValue,
            string measureUnit,
            Point3D from,
            Point3D to,
            int targetPanelIndex,
            int targetSourceIndex,
            string targetKind,
            string targetDescription,
            double overshoot,
            bool maxExtendCapped)
        {
            PanelIndex = panelIndex;
            SourceIndex = sourceIndex;
            Kind = kind;
            FromValue = fromValue;
            ToValue = toValue;
            MeasureUnit = measureUnit ?? string.Empty;
            From = from;
            To = to;
            TargetPanelIndex = targetPanelIndex;
            TargetSourceIndex = targetSourceIndex;
            TargetKind = targetKind ?? string.Empty;
            TargetDescription = targetDescription ?? string.Empty;
            Overshoot = overshoot;
            MaxExtendCapped = maxExtendCapped;
        }

        /// <summary>
        /// Builds a <see cref="ExtendOutcome.Skipped"/> record (P3 §5.4): a real decision point that found no
        /// safe target and left the panel exactly where it was. Reuses the frozen primary constructor (so its
        /// field wiring never drifts from the Applied path) with zeroed measurements, then marks the result
        /// Skipped - formatters route a Skipped record to <see cref="DescribeSkip"/> instead of <see cref="Describe"/>.
        /// </summary>
        public static ExtendRecord Skip(
            int panelIndex,
            int sourceIndex,
            ExtendOperationKind kind,
            ExtendSkipReason reason,
            string detail,
            Point3D at = null)
        {
            ExtendRecord record = new ExtendRecord(
                panelIndex, sourceIndex, kind,
                0, 0, string.Empty,
                at, at,
                -1, -1, string.Empty, string.Empty,
                0, false)
            {
                Outcome = ExtendOutcome.Skipped,
                SkipReason = reason,
                SkipDetail = detail ?? string.Empty
            };
            return record;
        }

        /// <summary>The body of the <c>SAM_OCCT_EXTEND3D_SKIP:</c> line AFTER the panel identity - the edge/
        /// operation kind, the skip reason and its detail. Only meaningful when <see cref="Outcome"/> is
        /// <see cref="ExtendOutcome.Skipped"/>.</summary>
        public string DescribeSkip()
        {
            return string.Format("{0}: {1} ({2})", KindText(Kind), SkipReason, SkipDetail);
        }

        /// <summary>The moved-edge preview segment (<see cref="From"/> -&gt; <see cref="To"/>), or null when this
        /// operation has no single edge (a cap grow) or did not actually move a distinct edge.</summary>
        public Segment3D PreviewSegment3D()
        {
            if (From == null || To == null || From.Distance(To) <= Core.Tolerance.Distance)
            {
                return null;
            }

            return new Segment3D(From, To);
        }

        /// <summary>The body of the <c>SAM_OCCT_EXTEND3D_PANEL:</c> line AFTER the panel identity - the operation,
        /// the measured from -&gt; to, the target, the overshoot and the lateral-cap flag. The analytical
        /// formatter prepends the panel/cap Guids (which this geometry-layer record does not carry).</summary>
        public string Describe()
        {
            string target = TargetPanelIndex >= 0
                ? string.Format("cap #{0} {1}", TargetPanelIndex, TargetKind)
                : TargetKind;

            if (!string.IsNullOrEmpty(TargetDescription))
            {
                target = string.Format("{0} [{1}]", target, TargetDescription);
            }

            return string.Format(
                "{0} {1:0.###} -> {2:0.###} ({3}); target {4}; overshoot {5:0.###}; lateral-capped {6}",
                KindText(Kind), FromValue, ToValue, MeasureUnit, target, Overshoot, MaxExtendCapped);
        }

        private static string KindText(ExtendOperationKind kind)
        {
            switch (kind)
            {
                case ExtendOperationKind.Top: return "top";
                case ExtendOperationKind.Bottom: return "bottom";
                case ExtendOperationKind.PlanStart: return "plan-start";
                case ExtendOperationKind.PlanEnd: return "plan-end";
                case ExtendOperationKind.CapGrow: return "cap-grow";
                default: return kind.ToString();
            }
        }
    }
}
