// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Analytical.OCCT.Solver
{
    /// <summary>
    /// Where a resolved solver parameter value (BucketSize / Weight / MaxExtend) came from
    /// (docs/CONTROLLED_WORKFLOW_PLAN.md §3). A valid per-panel stamp always wins; otherwise the value is
    /// derived; otherwise the solver default. Carried on the CleanReport's <c>SAM_OCCT_CLEAN3D_PANEL</c> line
    /// so the user can see whether a value they see is their own stamp, a derived value, or a floor/default.
    /// </summary>
    public enum ParameterProvenance
    {
        /// <summary>A valid per-panel <c>SolverParameter</c> stamp was present and used (it wins).</summary>
        Stamped,

        /// <summary>Derived from the panel's in-plane length (Weight's <c>SetWeights</c> remap, MaxExtend's
        /// <c>SetMaxExtends</c> reach).</summary>
        DerivedLength,

        /// <summary>Derived from construction thickness (BucketSize = thickness × thicknessFactor).</summary>
        DerivedThickness,

        /// <summary>Clamped to the minimum floor (BucketSize floored at minBucketSize).</summary>
        MinFloor,

        /// <summary>The solver default (no stamp, no derivation available).</summary>
        Default
    }

    /// <summary>Text-tag helpers for <see cref="ParameterProvenance"/> - the kebab-case tags used in the
    /// <c>SAM_OCCT_CLEAN3D_PANEL</c> line (<c>stamped</c>, <c>derived-length</c>, <c>derived-thickness</c>,
    /// <c>min-floor</c>, <c>default</c>).</summary>
    public static class ParameterProvenanceExtensions
    {
        public static string ToTag(this ParameterProvenance provenance)
        {
            switch (provenance)
            {
                case ParameterProvenance.Stamped: return "stamped";
                case ParameterProvenance.DerivedLength: return "derived-length";
                case ParameterProvenance.DerivedThickness: return "derived-thickness";
                case ParameterProvenance.MinFloor: return "min-floor";
                case ParameterProvenance.Default: return "default";
                default: return provenance.ToString();
            }
        }
    }
}
