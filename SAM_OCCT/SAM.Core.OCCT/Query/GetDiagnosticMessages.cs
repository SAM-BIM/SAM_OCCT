// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Collections.Generic;
using System.Globalization;

namespace SAM.Core.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// The runtime messages for a set of diagnostics: Info is log only (no message). Identical texts are reported
        /// once. Errors are never merged unless the text is identical. Warnings are reported one by one up to
        /// <paramref name="maxWarningsPerCode"/> per code; further distinct Warnings of that code are replaced by one
        /// count message. Warnings with different codes always stay separate. The full individual texts remain in the
        /// diagnostics list; this only limits what is raised as a runtime message.
        /// </summary>
        public static List<DiagnosticMessage> GetDiagnosticMessages(this IEnumerable<DiagnosticRecord> diagnosticRecords, int maxWarningsPerCode = 3)
        {
            List<DiagnosticMessage> result = new List<DiagnosticMessage>();
            if (diagnosticRecords == null)
            {
                return result;
            }

            HashSet<string> seen = new HashSet<string>();
            Dictionary<string, int> shown = new Dictionary<string, int>();
            Dictionary<string, int> overflow = new Dictionary<string, int>();
            Dictionary<string, int> overflowIndex = new Dictionary<string, int>();

            foreach (DiagnosticRecord diagnosticRecord in diagnosticRecords)
            {
                Diagnostic diagnostic = diagnosticRecord?.Diagnostic;
                if (diagnostic == null || diagnostic.Severity == DiagnosticSeverity.Info)
                {
                    continue;
                }

                string text = diagnosticRecord.Text;
                if (!seen.Add(((int)diagnostic.Severity).ToString(CultureInfo.InvariantCulture) + "|" + text))
                {
                    continue;
                }

                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    result.Add(new DiagnosticMessage(DiagnosticSeverity.Error, text, diagnostic.Code));
                    continue;
                }

                string code = diagnostic.Code ?? string.Empty;
                shown.TryGetValue(code, out int count);
                if (count < maxWarningsPerCode)
                {
                    shown[code] = count + 1;
                    result.Add(new DiagnosticMessage(DiagnosticSeverity.Warning, text, diagnostic.Code));
                    continue;
                }

                overflow.TryGetValue(code, out int extra);
                overflow[code] = extra + 1;
                if (extra == 0)
                {
                    overflowIndex[code] = result.Count;
                    result.Add(null);
                }
            }

            foreach (KeyValuePair<string, int> keyValuePair in overflow)
            {
                string label = keyValuePair.Key.Length == 0 ? "uncoded" : keyValuePair.Key;
                result[overflowIndex[keyValuePair.Key]] = new DiagnosticMessage(DiagnosticSeverity.Warning, string.Format(CultureInfo.InvariantCulture, "{0}: {1} more warning(s) with this code; see Diagnostics.", label, keyValuePair.Value), keyValuePair.Key.Length == 0 ? null : keyValuePair.Key, true);
            }

            return result;
        }
    }
}
