// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.Analytical.OCCT.Solver
{
    /// <summary>
    /// Additive, Grasshopper-facing snapshot of a <c>Modify.Solve3D</c>/<c>Modify.AutoTune3D</c> solve
    /// (Phase 8a): every staged field <see cref="Geometry.OCCT.Solver.Panel3DSnapSolver"/> (or
    /// <see cref="Geometry.OCCT.Solver.AutoTune3DSolver"/>) already produces, bundled into one out-parameter
    /// so a caller does not need a growing list of individual out-parameters. Plain data - no native
    /// lifetime, nothing to dispose, safe to read after the solve returns.
    /// </summary>
    public class Solve3DReport
    {
        /// <summary>True when the raw (L0) attempt was adopted; false when the managed pipeline produced the adopted result.</summary>
        public bool RawAdopted { get; }

        /// <summary>The FINAL adopted closure signature, or null when the solve produced no result.</summary>
        public ClosureSignature3D Signature { get; }

        /// <summary>The raw attempt's closure signature whether or not it was adopted; null when raw was never attempted.</summary>
        public ClosureSignature3D RawAttemptSignature { get; }

        /// <summary>The solve's accumulated machine-readable diagnostics.</summary>
        public SolverDiagnostics Diagnostics { get; }

        /// <summary>The exact per-output-face provenance (composed native history where available, geometric fallback otherwise).</summary>
        public SourceMap SourceMap { get; }

        /// <summary>The source panels the solve consumed, index-aligned to <see cref="SourceMap"/>'s source indices.</summary>
        public IReadOnlyList<Panel> Sources { get; }

        /// <summary>Per-cell metadata (index, volume, centre, boundary shell) for the adopted resolve.</summary>
        public IReadOnlyList<SolverCell> Cells { get; }

        /// <summary>Per-cell classification, index-aligned to <see cref="Cells"/>; empty when classification was not requested.</summary>
        public IReadOnlyList<CellRole> CellRoles { get; }

        /// <summary>Residual naked (free) boundary wires, grouped and ordered.</summary>
        public IReadOnlyList<OcctNakedWire> NakedWires { get; }

        /// <summary>Stage A (clean bucket) output faces; empty when <see cref="RawAdopted"/> is true (the raw path never runs Stage A).</summary>
        public IReadOnlyList<Face3D> CleanFace3Ds { get; }

        /// <summary>The managed pipeline's clustered level datums; empty when <see cref="RawAdopted"/> is true or no cap formed a frame.</summary>
        public IReadOnlyList<LevelFrame> LevelFrames { get; }

        /// <summary>True when the native OCCT kernel ran the resolve stage.</summary>
        public bool NativeResolved { get; }

        /// <summary>Number of closed cells the adopted resolve formed.</summary>
        public int ResolvedCellCount { get; }

        /// <summary>AutoTune escalation rounds attempted (0 for a plain Solve3D/Clean3D/Extend3D call).</summary>
        public int Rounds { get; }

        /// <summary>AutoTune escalation rounds accepted.</summary>
        public int RoundsAccepted { get; }

        /// <summary>The formatted, human-readable closure report (<see cref="Geometry.OCCT.Solver.ClosureReport.Format"/>).</summary>
        public string ClosureReportText { get; }

        public Solve3DReport(
            bool rawAdopted,
            ClosureSignature3D signature,
            ClosureSignature3D rawAttemptSignature,
            SolverDiagnostics diagnostics,
            SourceMap sourceMap,
            IReadOnlyList<Panel> sources,
            IReadOnlyList<SolverCell> cells,
            IReadOnlyList<CellRole> cellRoles,
            IReadOnlyList<OcctNakedWire> nakedWires,
            IReadOnlyList<Face3D> cleanFace3Ds,
            IReadOnlyList<LevelFrame> levelFrames,
            bool nativeResolved,
            int resolvedCellCount,
            int rounds = 0,
            int roundsAccepted = 0)
        {
            RawAdopted = rawAdopted;
            Signature = signature;
            RawAttemptSignature = rawAttemptSignature;
            Diagnostics = diagnostics ?? new SolverDiagnostics();
            SourceMap = sourceMap ?? new SourceMap();
            Sources = sources ?? new List<Panel>();
            Cells = cells ?? new List<SolverCell>();
            CellRoles = cellRoles ?? new List<CellRole>();
            NakedWires = nakedWires ?? new List<OcctNakedWire>();
            CleanFace3Ds = cleanFace3Ds ?? new List<Face3D>();
            LevelFrames = levelFrames ?? new List<LevelFrame>();
            NativeResolved = nativeResolved;
            ResolvedCellCount = resolvedCellCount;
            Rounds = rounds;
            RoundsAccepted = roundsAccepted;

            ClosureReportText = ClosureReport.Format(rawAdopted, signature, rawAttemptSignature, Diagnostics, rounds, roundsAccepted, LevelFrames);
        }

        /// <summary>Convenience wrapper over <see cref="SolverReportFormat.FormatSourceMap"/> with this report's own <see cref="SourceMap"/>/<see cref="Sources"/>.</summary>
        public List<string> FormatSourceMap()
        {
            return SolverReportFormat.FormatSourceMap(SourceMap, Sources);
        }

        /// <summary>Convenience wrapper over <see cref="SolverReportFormat.FormatCells"/> with this report's own <see cref="Cells"/>/<see cref="CellRoles"/>.</summary>
        public List<string> FormatCells()
        {
            return SolverReportFormat.FormatCells(Cells, CellRoles);
        }
    }
}
