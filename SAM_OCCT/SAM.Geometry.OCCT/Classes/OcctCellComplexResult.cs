// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    public class OcctCellComplexResult
    {
        private readonly List<OcctCell> cells = new List<OcctCell>();
        private readonly List<OcctDiagnostic> diagnostics = new List<OcctDiagnostic>();

        public bool NativeAvailable { get; internal set; }

        public string NativeVersion { get; internal set; }

        public bool Success
        {
            get
            {
                return cells.Count != 0 && !diagnostics.Any(x => x.Severity == OcctDiagnosticSeverity.Error);
            }
        }

        public IReadOnlyList<OcctCell> Cells
        {
            get { return cells; }
        }

        public IReadOnlyList<Shell> Shells
        {
            get { return cells.ConvertAll(x => x.Shell); }
        }

        public IReadOnlyList<OcctDiagnostic> Diagnostics
        {
            get { return diagnostics; }
        }

        public void AddCell(OcctCell cell)
        {
            if (cell?.Shell != null)
            {
                cells.Add(cell);
            }
        }

        public void AddDiagnostic(OcctDiagnosticSeverity severity, string code, string message, int? sourceIndex = null)
        {
            diagnostics.Add(new OcctDiagnostic(severity, code, message, sourceIndex));
        }
    }
}
