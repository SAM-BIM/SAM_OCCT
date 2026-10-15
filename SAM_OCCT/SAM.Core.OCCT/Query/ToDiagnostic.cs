// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// Converts an <see cref="OcctDiagnostic"/> into the shared <see cref="Diagnostic"/>. Severity, code, message and
        /// source index are those of the original; <paramref name="detail"/> defaults to
        /// <see cref="GetDiagnosticDetail(string)"/> of the code. The original object is not modified.
        /// </summary>
        public static Diagnostic ToDiagnostic(this OcctDiagnostic occtDiagnostic, DiagnosticDetail? detail = null, string subject = null)
        {
            if (occtDiagnostic == null)
            {
                return null;
            }

            return new Diagnostic(occtDiagnostic.Severity.ToDiagnosticSeverity(), detail ?? GetDiagnosticDetail(occtDiagnostic.Code), subject, occtDiagnostic.Message, occtDiagnostic.Code, occtDiagnostic.SourceIndex);
        }

        /// <summary>
        /// The structured diagnostic paired with the original legacy text (<see cref="OcctDiagnostic.ToString"/>).
        /// </summary>
        public static DiagnosticRecord ToDiagnosticRecord(this OcctDiagnostic occtDiagnostic)
        {
            if (occtDiagnostic == null)
            {
                return null;
            }

            return new DiagnosticRecord(occtDiagnostic.ToDiagnostic(), occtDiagnostic.ToString());
        }
    }
}
