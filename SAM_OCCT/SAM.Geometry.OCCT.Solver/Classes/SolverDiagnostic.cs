// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// A single machine-readable event from the true-3D solver pipeline: which stage produced it,
    /// its taxonomy code, severity, a human-readable message, and the geometry/measurements that
    /// located it. Every rejection (a gate that refused to adopt/accept something) is recorded as
    /// one of these with its reason, never silently dropped
    /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §N).
    /// </summary>
    public class SolverDiagnostic
    {
        public SolverStage Stage { get; }

        public DiagnosticCode Code { get; }

        public OcctDiagnosticSeverity Severity { get; }

        public string Message { get; }

        /// <summary>Locations the diagnostic refers to (e.g. naked-edge points, a gap's endpoints).</summary>
        public IReadOnlyList<Point3D> Point3Ds { get; }

        /// <summary>The face the diagnostic is about, when it concerns a single face (may be null).</summary>
        public Face3D Face3D { get; }

        /// <summary>The tolerance value that was in effect when this diagnostic was raised.</summary>
        public double ToleranceUsed { get; }

        /// <summary>Wall-clock time (milliseconds) the measured operation took, when timed; 0 otherwise.</summary>
        public long ElapsedMs { get; }

        public SolverDiagnostic(
            SolverStage stage,
            DiagnosticCode code,
            OcctDiagnosticSeverity severity,
            string message,
            IEnumerable<Point3D> point3Ds = null,
            Face3D face3D = null,
            double toleranceUsed = 0,
            long elapsedMs = 0)
        {
            Stage = stage;
            Code = code;
            Severity = severity;
            Message = message ?? string.Empty;
            Point3Ds = point3Ds == null ? new List<Point3D>() : point3Ds.Where(x => x != null).ToList();
            Face3D = face3D;
            ToleranceUsed = toleranceUsed;
            ElapsedMs = elapsedMs;
        }

        public override string ToString()
        {
            return string.Format("[{0}/{1}/{2}] {3}", Stage, Code, Severity, Message);
        }
    }
}
