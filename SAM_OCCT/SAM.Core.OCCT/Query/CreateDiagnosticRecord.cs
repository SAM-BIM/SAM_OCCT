// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// A record for a coded text line (<c>CODE: message</c>) whose severity the caller knows at the place where the
        /// line is produced. The leading <c>CODE</c> token is read only to look up the Info detail level
        /// (<see cref="GetDiagnosticDetail(string)"/>); the severity is never derived from the text.
        /// </summary>
        public static DiagnosticRecord CreateDiagnosticRecord(string text, DiagnosticSeverity severity, DiagnosticDetail? detail = null)
        {
            text = text ?? string.Empty;

            int index = text.IndexOf(':');
            string code = index > 0 ? text.Substring(0, index) : null;

            return new DiagnosticRecord(new Diagnostic(severity, detail ?? GetDiagnosticDetail(code), null, text, code), text);
        }
    }
}
