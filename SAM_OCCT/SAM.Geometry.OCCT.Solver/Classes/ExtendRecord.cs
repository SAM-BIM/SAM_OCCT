// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;

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

        /// <summary>Representative source-face index of the mutated panel (its first <see cref="SnappedPanel.SourceIndices"/>);
        /// -1 when the panel carries no source. The analytical layer maps this to the source panel's Guid.</summary>
        public int SourceIndex { get; }

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

        /// <summary>Representative source-face index of the target cap (-1 when there is no cap target).</summary>
        public int TargetSourceIndex { get; }

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
