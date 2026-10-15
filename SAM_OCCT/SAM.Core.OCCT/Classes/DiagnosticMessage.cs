// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OCCT
{
    /// <summary>
    /// One user-facing runtime message derived from <see cref="DiagnosticRecord"/>s (a Warning or an Error, or a
    /// count line that stands for further Warnings of the same code). Info diagnostics never become messages.
    /// </summary>
    public sealed class DiagnosticMessage
    {
        public DiagnosticSeverity Severity { get; }

        public string Text { get; }

        /// <summary>The diagnostic code the message is about; null for component-authored messages.</summary>
        public string Code { get; }

        /// <summary>True when the message stands for several Warnings of one code that were not shown one by one.</summary>
        public bool IsGrouped { get; }

        public DiagnosticMessage(DiagnosticSeverity severity, string text, string code = null, bool isGrouped = false)
        {
            Severity = severity;
            Text = text ?? string.Empty;
            Code = code;
            IsGrouped = isGrouped;
        }
    }
}
