// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;

namespace SAM.Core.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// Keeps the records the shared <see cref="Diagnostics"/> rules keep at <paramref name="detail"/>: Info above the
        /// level is dropped, Warnings and Errors are always kept. The original records are returned in their original
        /// order, so the legacy text is untouched.
        /// </summary>
        public static List<DiagnosticRecord> FilterDiagnosticRecords(this IEnumerable<DiagnosticRecord> diagnosticRecords, DiagnosticDetail detail)
        {
            List<DiagnosticRecord> result = new List<DiagnosticRecord>();
            if (diagnosticRecords == null)
            {
                return result;
            }

            Diagnostics diagnostics = new Diagnostics(detail);
            foreach (DiagnosticRecord diagnosticRecord in diagnosticRecords)
            {
                if (diagnosticRecord?.Diagnostic != null && diagnostics.Add(diagnosticRecord.Diagnostic))
                {
                    result.Add(diagnosticRecord);
                }
            }

            return result;
        }
    }
}
