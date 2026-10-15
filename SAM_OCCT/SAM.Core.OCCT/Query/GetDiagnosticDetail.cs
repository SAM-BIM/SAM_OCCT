// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;

namespace SAM.Core.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// The detail level at which an Info diagnostic with this code is shown. This only decides when an Info line
        /// appears: a Warning or an Error is never filtered by it. A few important codes and code families are
        /// classified; every other (including future) code is <see cref="DiagnosticDetail.Standard"/>.
        /// </summary>
        public static DiagnosticDetail GetDiagnosticDetail(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return DiagnosticDetail.Standard;
            }

            // Outcomes and explanations the user needs to see even at the default level.
            if (code == "SAM_OCCT_OPEN_SHELL_ANALYSIS" ||
                code == "SAM_OCCT_SEW_UNDER_SPLIT_SUSPECTED" ||
                code == "SAM_OCCT_EXTEND3D_HOLE_DROPPED" ||
                code.StartsWith("SAM_OCCT_ANALYTICAL_COMPLEX_", StringComparison.Ordinal) ||
                code.StartsWith("SAM_OCCT_NATIVE_", StringComparison.Ordinal) ||
                code.EndsWith("_SUCCESS", StringComparison.Ordinal) ||
                code.EndsWith("_RESULT", StringComparison.Ordinal) ||
                code.EndsWith("_INPUT_EMPTY", StringComparison.Ordinal) ||
                code.EndsWith("_NO_RESULT", StringComparison.Ordinal) ||
                code.EndsWith("_FAILED", StringComparison.Ordinal))
            {
                return DiagnosticDetail.Summary;
            }

            // Per-item and bookkeeping lines.
            if (code == "SAM_OCCT_ANALYTICAL_PANEL_METADATA" ||
                code == "SAM_OCCT_ANALYTICAL_SHELL_FACES" ||
                code == "SAM_OCCT_ANALYTICAL_SPACE_LOCATIONS" ||
                code == "SAM_OCCT_CELL_CENTERS" ||
                code.EndsWith("_SMALL_FACES_KEPT", StringComparison.Ordinal) ||
                code.EndsWith("_PANEL", StringComparison.Ordinal))
            {
                return DiagnosticDetail.Detailed;
            }

            return DiagnosticDetail.Standard;
        }
    }
}
