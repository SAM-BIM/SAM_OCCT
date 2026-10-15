// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OCCT
{
    /// <summary>
    /// A structured shared <see cref="SAM.Core.Diagnostic"/> paired with the exact legacy text the component has
    /// always shown for it. The pair is built where the diagnostic is produced, so the severity is the producer's own
    /// and is never inferred from the text. Filtering by detail keeps the original text untouched.
    /// </summary>
    public sealed class DiagnosticRecord
    {
        /// <summary>The structured diagnostic (severity, code, source index, detail).</summary>
        public Diagnostic Diagnostic { get; }

        /// <summary>The legacy single-line text, unchanged (for example <c>CODE [index]: message</c>).</summary>
        public string Text { get; }

        public DiagnosticRecord(Diagnostic diagnostic, string text)
        {
            Diagnostic = diagnostic;
            Text = text ?? string.Empty;
        }

        public override string ToString()
        {
            return Text;
        }
    }
}
