// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Formats a solve's closure state into one human-readable, multi-line report string for
    /// Grasshopper inspection (Phase 8a) - the 3D analogue of the 2D solver's per-level closure
    /// report. Pure and native-free: assembles fields already produced by <see cref="Panel3DSnapSolver"/>/
    /// <see cref="AutoTune3DSolver"/> (<see cref="ClosureSignature3D"/>, <see cref="SolverDiagnostics"/>,
    /// <see cref="LevelFrame"/>); never recomputes or reinterprets them.
    /// </summary>
    public static class ClosureReport
    {
        /// <summary>
        /// Builds the report. All parameters are optional (null/0/empty) since a caller may have a
        /// partial result (e.g. no result at all, or AutoTune-specific rounds); a missing field is
        /// reported as "n/a" rather than omitted, so the shape of the report is stable.
        /// </summary>
        /// <param name="rawAdopted">Whether the raw (L0) attempt was adopted (true) or the managed
        /// pipeline produced the adopted result (false).</param>
        /// <param name="signature">The FINAL adopted closure signature, or null when the solve produced no result.</param>
        /// <param name="rawAttemptSignature">The raw attempt's signature whether or not it was adopted, or null when raw was never attempted (<see cref="Panel3DSnapSolver.ForceManagedPipeline"/>/<c>StopAfterClean</c>/<c>StopAfterExtend</c>).</param>
        /// <param name="diagnostics">The solve's accumulated diagnostics; counted by severity.</param>
        /// <param name="rounds">AutoTune escalation rounds attempted (0 for a plain Solve3D/Clean3D/Extend3D call).</param>
        /// <param name="roundsAccepted">AutoTune escalation rounds accepted.</param>
        /// <param name="levelFrames">The managed pipeline's clustered level frames (empty when raw was adopted or no cap formed a frame).</param>
        public static string Format(
            bool rawAdopted,
            ClosureSignature3D signature,
            ClosureSignature3D rawAttemptSignature,
            SolverDiagnostics diagnostics,
            int rounds = 0,
            int roundsAccepted = 0,
            IReadOnlyList<LevelFrame> levelFrames = null)
        {
            StringBuilder stringBuilder = new StringBuilder();

            stringBuilder.AppendLine(string.Format("Adopted path: {0}", rawAdopted ? "Raw" : "Managed"));
            stringBuilder.AppendLine(string.Format("Raw attempt: {0}", rawAttemptSignature == null ? "not attempted" : rawAttemptSignature.ToString()));
            stringBuilder.AppendLine(string.Format("Final: {0}", signature == null ? "no result" : signature.ToString()));
            stringBuilder.AppendLine(string.Format("AutoTune rounds: {0} accepted of {1} attempted", roundsAccepted, rounds));

            int errorCount = diagnostics?.OfSeverity(OcctDiagnosticSeverity.Error).Count() ?? 0;
            int warningCount = diagnostics?.OfSeverity(OcctDiagnosticSeverity.Warning).Count() ?? 0;
            int infoCount = diagnostics?.OfSeverity(OcctDiagnosticSeverity.Info).Count() ?? 0;
            stringBuilder.AppendLine(string.Format("Diagnostics: {0} error(s), {1} warning(s), {2} info", errorCount, warningCount, infoCount));

            List<LevelFrame> frames = levelFrames == null ? new List<LevelFrame>() : levelFrames.ToList();
            stringBuilder.AppendLine(string.Format("Level frames: {0}{1}", frames.Count, rawAdopted ? " (n/a on the raw path)" : string.Empty));
            for (int i = 0; i < frames.Count; i++)
            {
                LevelFrame frame = frames[i];
                stringBuilder.AppendLine(string.Format(
                    "  Frame {0}: elevation {1:0.###} m, tilt {2:0.#} deg, {3} cap(s)",
                    i, frame.Elevation, frame.TiltAngle * (180.0 / System.Math.PI), frame.CapIndices?.Count ?? 0));
            }

            stringBuilder.Append("Timings: not tracked");

            return stringBuilder.ToString();
        }
    }
}
