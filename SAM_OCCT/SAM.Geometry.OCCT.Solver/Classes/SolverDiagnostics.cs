// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Ordered collection of <see cref="SolverDiagnostic"/> a solve accumulates across every stage.
    /// The single contract every later phase's diagnostics feed into
    /// (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §N); everything user-facing (Grasshopper
    /// diagnostics tree, closure report string) derives from this.
    /// </summary>
    public class SolverDiagnostics
    {
        private readonly List<SolverDiagnostic> diagnostics = new List<SolverDiagnostic>();

        public IReadOnlyList<SolverDiagnostic> All
        {
            get { return diagnostics; }
        }

        public void Add(SolverDiagnostic diagnostic)
        {
            if (diagnostic != null)
            {
                diagnostics.Add(diagnostic);
            }
        }

        public SolverDiagnostic Add(
            SolverStage stage,
            DiagnosticCode code,
            OcctDiagnosticSeverity severity,
            string message,
            IEnumerable<Point3D> point3Ds = null,
            Face3D face3D = null,
            double toleranceUsed = 0,
            long elapsedMs = 0)
        {
            SolverDiagnostic diagnostic = new SolverDiagnostic(stage, code, severity, message, point3Ds, face3D, toleranceUsed, elapsedMs);
            diagnostics.Add(diagnostic);
            return diagnostic;
        }

        public IEnumerable<SolverDiagnostic> OfCode(DiagnosticCode code)
        {
            return diagnostics.Where(x => x.Code == code);
        }

        public IEnumerable<SolverDiagnostic> OfStage(SolverStage stage)
        {
            return diagnostics.Where(x => x.Stage == stage);
        }

        public IEnumerable<SolverDiagnostic> OfSeverity(OcctDiagnosticSeverity severity)
        {
            return diagnostics.Where(x => x.Severity == severity);
        }

        public bool HasErrors
        {
            get { return diagnostics.Any(x => x.Severity == OcctDiagnosticSeverity.Error); }
        }

        public void Clear()
        {
            diagnostics.Clear();
        }
    }
}
