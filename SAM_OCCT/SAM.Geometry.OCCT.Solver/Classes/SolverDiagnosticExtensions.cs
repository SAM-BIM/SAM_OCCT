// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;

namespace SAM.Geometry.OCCT.Solver
{
    /// <summary>
    /// Additive adapters from the solver's own <see cref="SolverDiagnostic"/> to the shared
    /// <see cref="SAM.Core.Diagnostic"/>. The original object keeps the stage, face and points; only the
    /// severity, code, stage (as subject) and tolerance are carried over.
    /// </summary>
    public static class SolverDiagnosticExtensions
    {
        /// <summary>
        /// Severity comes from <see cref="SolverDiagnostic.Severity"/>, Code is the <see cref="DiagnosticCode"/> name,
        /// Subject is the <see cref="SolverStage"/> name and Value/Unit carry <see cref="SolverDiagnostic.ToleranceUsed"/>
        /// (metres) when it is positive. <paramref name="detail"/> defaults to <see cref="GetDiagnosticDetail"/>.
        /// </summary>
        public static Diagnostic ToDiagnostic(this SolverDiagnostic solverDiagnostic, DiagnosticDetail? detail = null)
        {
            if (solverDiagnostic == null)
            {
                return null;
            }

            double? value = solverDiagnostic.ToleranceUsed > 0 ? solverDiagnostic.ToleranceUsed : (double?)null;
            return new Diagnostic(solverDiagnostic.Severity.ToDiagnosticSeverity(), detail ?? GetDiagnosticDetail(solverDiagnostic.Code), solverDiagnostic.Stage.ToString(), solverDiagnostic.Message, solverDiagnostic.Code.ToString(), null, value, null, value.HasValue ? "m" : null);
        }

        /// <summary>
        /// The structured diagnostic paired with the given legacy text. The text is supplied by the caller (the
        /// formatter that already produces it) so it is not re-derived here.
        /// </summary>
        public static DiagnosticRecord ToDiagnosticRecord(this SolverDiagnostic solverDiagnostic, string text)
        {
            if (solverDiagnostic == null)
            {
                return null;
            }

            return new DiagnosticRecord(solverDiagnostic.ToDiagnostic(), text);
        }

        /// <summary>
        /// The detail at which an Info solver diagnostic is shown. Only Info is affected; Warnings and Errors are never
        /// filtered. Unclassified (including future) codes are Standard.
        /// </summary>
        public static DiagnosticDetail GetDiagnosticDetail(DiagnosticCode diagnosticCode)
        {
            switch (diagnosticCode)
            {
                case DiagnosticCode.NakedEdge:
                case DiagnosticCode.NakedLoop:
                case DiagnosticCode.DroppedFace:
                case DiagnosticCode.EscalatedPanel:
                    return DiagnosticDetail.Detailed;

                default:
                    return DiagnosticDetail.Standard;
            }
        }
    }
}
