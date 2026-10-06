// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Parameter discovery solver: sweeps <c>bucketBetweenLevels</c>, <c>fillMargin</c>, and
    /// <c>directionalCapGrow</c> over plausible ranges, runs the full managed pipeline for each
    /// combination, scores by closure quality (cell count / naked edges / volume), and
    /// returns the best configuration with full documentation of every combination tried.
    /// <para>
    /// <see cref="WorkflowMode"/> controls which pipeline is tested:
    /// <b>Solve3D</b> (default): the full Panel3DSnapSolver pipeline (raw-first gate + managed).
    /// <b>Extend3D</b>: stops after conditioning (StopAfterExtend) and scores via a separate
    /// Create.AdjacencyCluster call — matches the Extend3D → AdjacencyCluster GH chain.
    /// </para>
    /// </summary>
    public class ParameterDiscoverySolver
    {
        /// <summary>Which pipeline the discovery sweep tests.</summary>
        public enum WorkflowMode
        {
            /// <summary>Full Solve3D pipeline (default).</summary>
            Solve3D,
            /// <summary>Extend3D → Create.AdjacencyCluster chain (matches the GH workflow).</summary>
            Extend3D,
        }

        /// <summary>A single parameter combination and its result.</summary>
        public class Trial
        {
            public double BucketBetweenLevels { get; set; }
            public double FillMargin { get; set; }
            public bool DirectionalCapGrow { get; set; }
            public int CellCount { get; set; }
            public int NakedEdgeCount { get; set; }
            public double TotalVolume { get; set; }
            public int FaceCount { get; set; }
            public bool Adopted { get; set; }
            public string Notes { get; set; }

            public override string ToString()
            {
                return string.Format("band={0:0.###} fill={1:0.###} dir={2} -> cells={3} naked={4} vol={5:0.#} faces={6}{7}",
                    BucketBetweenLevels, FillMargin, DirectionalCapGrow,
                    CellCount, NakedEdgeCount, TotalVolume, FaceCount,
                    Adopted ? "" : " REJECTED");
            }
        }

        /// <summary>All trials executed (including rejected ones).</summary>
        public List<Trial> Trials { get; } = new List<Trial>();

        /// <summary>The best trial found (highest cell count, then lowest naked, then highest volume).</summary>
        public Trial BestTrial { get; private set; }

        /// <summary>Baseline trial (default parameters).</summary>
        public Trial Baseline { get; private set; }

        /// <summary>Diagnostic output for each trial.</summary>
        public List<string> Diagnostics { get; } = new List<string>();

        private readonly List<Face3D> face3Ds;
        private readonly List<double> bucketSizes;
        private readonly List<double> weights;
        private readonly List<double> maxExtensions;

        public Vector3D Up { get; set; }
        public double AlignColinearOffset { get; set; } = 0.3;
        public double NormalizeCapOffset { get; set; } = 0.3;

        /// <summary>Which pipeline the sweep tests. Extend3D matches the GH Extend3D→AdjacencyCluster chain.</summary>
        public WorkflowMode Mode { get; set; } = WorkflowMode.Solve3D;

        public ParameterDiscoverySolver(
            IEnumerable<Face3D> face3Ds,
            IEnumerable<double> bucketSizes = null,
            IEnumerable<double> weights = null,
            IEnumerable<double> maxExtensions = null)
        {
            this.face3Ds = face3Ds?.ToList() ?? new List<Face3D>();
            this.bucketSizes = bucketSizes?.ToList();
            this.weights = weights?.ToList();
            this.maxExtensions = maxExtensions?.ToList();
        }

        /// <summary>
        /// Runs the parameter sweep. Estimates starting ranges from the input geometry,
        /// then sweeps combinations and selects the best.
        /// </summary>
        /// <param name="options">Native build options (shared across all trials).</param>
        /// <param name="sweepBands">Bucket band values to try. If null, derived from the model.</param>
        /// <param name="sweepMargins">Fill margin values to try. If null, derived from the model.</param>
        public void Execute(OcctBuildOptions options = null, IEnumerable<double> sweepBands = null, IEnumerable<double> sweepMargins = null)
        {
            Trials.Clear();
            Diagnostics.Clear();

            // Estimate plausible ranges from the input geometry.
            double estimatedBand = BucketSizeEstimator.Compute(face3Ds);
            double[] bands = sweepBands?.ToArray() ?? DeriveBandRange(estimatedBand);
            double[] margins = sweepMargins?.ToArray() ?? DeriveMarginRange();
            bool[] dirValues = { true, false };

            Diagnostics.Add(string.Format("ParameterDiscovery: {0} face(s), estimated band ~{1:0.###} m",
                face3Ds.Count, estimatedBand));
            Diagnostics.Add(string.Format("Sweeping bands: [{0}]", string.Join(", ", bands.Select(b => b.ToString("0.###")))));
            Diagnostics.Add(string.Format("Sweeping margins: [{0}]", string.Join(", ", margins.Select(m => m.ToString("0.###")))));

            // --- Run the baseline with default parameters first ---
            Baseline = RunTrial(options, 0.0, 0.5, true, "baseline (defaults)");
            Diagnostics.Add(string.Format("Baseline: {0}", Baseline));

            // --- Sweep ---
            foreach (double band in bands)
            {
                foreach (double margin in margins)
                {
                    foreach (bool dir in dirValues)
                    {
                        string note = string.Format("sweep band={0:0.###} margin={1:0.###} dir={2}", band, margin, dir);
                        Trial trial = RunTrial(options, band, margin, dir, note);
                        Trials.Add(trial);
                        Diagnostics.Add(trial.ToString());
                    }
                }
            }

            // Select best: highest cell count, then lowest naked, then highest volume.
            BestTrial = Trials
                .OrderByDescending(t => t.CellCount)
                .ThenBy(t => t.NakedEdgeCount)
                .ThenByDescending(t => t.TotalVolume)
                .FirstOrDefault();

            Diagnostics.Add("");
            Diagnostics.Add(string.Format("Best: {0}", BestTrial));
            int improvement = BestTrial != null && Baseline != null
                ? BestTrial.CellCount - Baseline.CellCount
                : 0;
            Diagnostics.Add(string.Format("Improvement over baseline: {0:+0;-#} cells", improvement));

            // --- Re-run the best config to adopt it ---
            if (BestTrial != null && BestTrial.Adopted)
            {
                Panel3DSnapSolver solver = CreateSolver(options, BestTrial.BucketBetweenLevels, BestTrial.FillMargin, BestTrial.DirectionalCapGrow);
                solver.Execute(options);
                Adopt(solver);
            }
        }

        // ---- Outputs (mirror Panel3DSnapSolver) ----

        public List<Face3D> ResolvedFace3Ds { get; private set; } = new List<Face3D>();
        public SourceMap SourceMap { get; private set; } = new SourceMap();
        public ClosureSignature3D Signature { get; private set; }
        public List<OcctNakedWire> NakedWires { get; private set; } = new List<OcctNakedWire>();
        public List<Point3D> NakedEdgePoint3Ds { get; private set; } = new List<Point3D>();
        public int ResolvedCellCount { get; private set; }

        // ---- Private helpers ----

        private Panel3DSnapSolver CreateSolver(OcctBuildOptions options, double band, double margin, bool dir)
        {
            int count = face3Ds.Count;
            return new Panel3DSnapSolver(
                face3Ds,
                Panel3DSnapSolver.AdjustListLength(bucketSizes, count, Panel3DSnapSolver.DEFAULT_BucketSize),
                Panel3DSnapSolver.AdjustListLength(weights, count, Panel3DSnapSolver.DEFAULT_Weight),
                Panel3DSnapSolver.AdjustListLength(maxExtensions, count, Panel3DSnapSolver.DEFAULT_MaxExtension))
            {
                BucketBetweenLevels = band,
                FillMargin = margin,
                DirectionalCapGrow = dir,
                ForceManagedPipeline = true,
                Up = Up,
                AlignColinearOffset = AlignColinearOffset,
                NormalizeCapOffset = NormalizeCapOffset,
                StopAfterExtend = (Mode == WorkflowMode.Extend3D),
            };
        }

        private Trial RunTrial(OcctBuildOptions options, double band, double margin, bool dir, string note)
        {
            Trial trial = new Trial
            {
                BucketBetweenLevels = band,
                FillMargin = margin,
                DirectionalCapGrow = dir,
                Notes = note,
            };

            try
            {
                Panel3DSnapSolver solver = CreateSolver(options, band, margin, dir);
                solver.Execute(options);

                if (Mode == WorkflowMode.Extend3D)
                {
                    // Extend3D chain: build adjacency cluster from conditioned (overshooting) faces.
                    var buildOptions = new OcctBuildOptions
                    {
                        AvoidInternalShapes = false,
                        SewBeforeBuild = true,
                        SewingTolerance = 0.01,
                    };
                    List<Shell> shells = Geometry.OCCT.Create.Shells(
                        solver.ResolvedFace3Ds, out OcctCellComplexResult cellResult, buildOptions);
                    trial.CellCount = cellResult?.Cells?.Count ?? 0;
                    trial.NakedEdgeCount = -1;
                    trial.TotalVolume = solver.Signature?.TotalVolume ?? 0;
                    trial.FaceCount = solver.ResolvedFace3Ds?.Count ?? 0;
                    trial.Adopted = (cellResult?.Cells?.Count ?? 0) > 0;
                    cellResult?.Dispose();
                }
                else if (solver.Signature != null)
                {
                    trial.CellCount = solver.Signature.CellCount;
                    trial.NakedEdgeCount = solver.Signature.NakedEdgeCount;
                    trial.TotalVolume = solver.Signature.TotalVolume;
                    trial.FaceCount = solver.Signature.FaceCount;
                    trial.Adopted = true;
                }
                else
                {
                    trial.Adopted = false;
                    trial.Notes += " (no signature)";
                }
            }
            catch (Exception ex)
            {
                trial.Adopted = false;
                trial.Notes += string.Format(" (error: {0})", ex.Message);
            }

            return trial;
        }

        private void Adopt(Panel3DSnapSolver solver)
        {
            ResolvedFace3Ds = solver.ResolvedFace3Ds ?? new List<Face3D>();
            SourceMap = solver.SourceMap ?? new SourceMap();
            Signature = solver.Signature;
            NakedWires = solver.NakedWires ?? new List<OcctNakedWire>();
            NakedEdgePoint3Ds = solver.NakedEdgePoint3Ds ?? new List<Point3D>();
            ResolvedCellCount = solver.ResolvedCellCount;
        }

        private static double[] DeriveBandRange(double estimated)
        {
            if (estimated > 0)
            {
                // Sweep around the estimate: 0.5×, 0.75×, 1.0×, 1.5×, 2.0×, clamped to [0.15, 1.0].
                double[] around = {
                    Clamp(estimated * 0.5, 0.15, 1.0),
                    Clamp(estimated * 0.75, 0.15, 1.0),
                    Clamp(estimated, 0.15, 1.0),
                    Clamp(estimated * 1.5, 0.15, 1.0),
                    Clamp(estimated * 2.0, 0.15, 1.0),
                };
                return around.Distinct().OrderBy(x => x).ToArray();
            }

            // Default: sweep common bands.
            return new double[] { 0.15, 0.21, 0.3, 0.4, 0.5, 0.7 };
        }

        private static double[] DeriveMarginRange()
        {
            return new double[] { 0.3, 0.5, 0.7, 1.0 };
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value <= 0) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
