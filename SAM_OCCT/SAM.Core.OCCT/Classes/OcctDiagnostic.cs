// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OCCT
{
    public class OcctDiagnostic
    {
        public OcctDiagnosticSeverity Severity { get; }

        public string Code { get; }

        public string Message { get; }

        public int? SourceIndex { get; }

        public OcctDiagnostic(OcctDiagnosticSeverity severity, string code, string message, int? sourceIndex = null)
        {
            Severity = severity;
            Code = code;
            Message = message;
            SourceIndex = sourceIndex;
        }

        public override string ToString()
        {
            string source = SourceIndex.HasValue ? string.Format(" [{0}]", SourceIndex.Value) : string.Empty;
            return string.Format("{0}{1}: {2}", Code, source, Message);
        }
    }
}
