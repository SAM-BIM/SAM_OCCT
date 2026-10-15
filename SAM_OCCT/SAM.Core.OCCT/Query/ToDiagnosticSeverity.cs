// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// Maps the OCCT severity to the shared <see cref="DiagnosticSeverity"/> one to one. An undefined value maps to
        /// <see cref="DiagnosticSeverity.Error"/> so that it can never be filtered away as Info.
        /// </summary>
        public static DiagnosticSeverity ToDiagnosticSeverity(this OcctDiagnosticSeverity occtDiagnosticSeverity)
        {
            switch (occtDiagnosticSeverity)
            {
                case OcctDiagnosticSeverity.Info:
                    return DiagnosticSeverity.Info;

                case OcctDiagnosticSeverity.Warning:
                    return DiagnosticSeverity.Warning;

                default:
                    return DiagnosticSeverity.Error;
            }
        }
    }
}
