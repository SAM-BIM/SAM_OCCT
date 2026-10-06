// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// The shared working state passed between the managed solver stages (Snap / Condition / Resolve
    /// / Heal) - the seam that lets <see cref="Panel3DSnapSolver"/> be a façade over explicit passes
    /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §D/§G, Phase 2). Holds the immutable input
    /// (faces + per-source parameters keyed by <em>source identity</em>, not list position - which is
    /// what fixes the positional MaxExtend bug), plus the accumulating <see cref="SourceMap"/>,
    /// <see cref="SolverDiagnostics"/> and <see cref="ToleranceBudget"/>.
    /// </summary>
    public class SolverContext
    {
        private readonly List<Face3D> inputFace3Ds;
        private readonly List<double> weights;
        private readonly List<double> bucketSizes;
        private readonly List<double> maxExtensions;

        /// <summary>The original input faces, indexed by source index (the identity all params key against).</summary>
        public IReadOnlyList<Face3D> InputFace3Ds
        {
            get { return inputFace3Ds; }
        }

        public SourceMap SourceMap { get; }

        public SolverDiagnostics Diagnostics { get; }

        public ToleranceBudget Tolerances { get; }

        public SolverContext(
            IEnumerable<Face3D> inputFace3Ds,
            IEnumerable<double> weights,
            IEnumerable<double> bucketSizes,
            IEnumerable<double> maxExtensions,
            SolverDiagnostics diagnostics = null,
            ToleranceBudget tolerances = null)
        {
            this.inputFace3Ds = inputFace3Ds == null ? new List<Face3D>() : inputFace3Ds.ToList();
            this.weights = weights == null ? new List<double>() : weights.ToList();
            this.bucketSizes = bucketSizes == null ? new List<double>() : bucketSizes.ToList();
            this.maxExtensions = maxExtensions == null ? new List<double>() : maxExtensions.ToList();
            SourceMap = new SourceMap();
            Diagnostics = diagnostics ?? new SolverDiagnostics();
            Tolerances = tolerances ?? new ToleranceBudget();
        }

        public int SourceCount
        {
            get { return inputFace3Ds.Count; }
        }

        /// <summary>The backer weight of source <paramref name="sourceIndex"/> (falls back to the default when out of range).</summary>
        public double WeightOf(int sourceIndex)
        {
            return sourceIndex >= 0 && sourceIndex < weights.Count ? weights[sourceIndex] : Panel3DSnapSolver.DEFAULT_Weight;
        }

        /// <summary>The capture half-width of source <paramref name="sourceIndex"/> (falls back to the default when out of range).</summary>
        public double BucketSizeOf(int sourceIndex)
        {
            return sourceIndex >= 0 && sourceIndex < bucketSizes.Count ? bucketSizes[sourceIndex] : Panel3DSnapSolver.DEFAULT_BucketSize;
        }

        /// <summary>The lateral extend reach of source <paramref name="sourceIndex"/> (falls back to the default when out of range).</summary>
        public double MaxExtensionOf(int sourceIndex)
        {
            return sourceIndex >= 0 && sourceIndex < maxExtensions.Count ? maxExtensions[sourceIndex] : Panel3DSnapSolver.DEFAULT_MaxExtension;
        }
    }
}
