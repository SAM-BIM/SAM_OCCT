// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

// Compiled into each OCCT Grasshopper assembly that reports OCCT diagnostics (linked file, internal on purpose:
// no new cross-plugin runtime dependency).
using global::Grasshopper.Kernel;
using SAM.Core.Grasshopper;
using SAM.Core.OCCT;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Core.Grasshopper.OCCT
{
    /// <summary>
    /// Structured severity handling shared by the OCCT components: Info is log only, Warning and Error become the
    /// matching Grasshopper runtime message level, grouped so a repetitive code does not flood the canvas. The
    /// <c>Diagnostics</c> output keeps the full legacy text of every retained diagnostic.
    /// </summary>
    internal static class OcctDiagnosticReporter
    {
        internal const string DetailInputName = "diagnosticDetail_";

        /// <summary>Info -> null (log only), Warning -> Warning, Error -> Error.</summary>
        internal static GH_RuntimeMessageLevel? ToRuntimeMessageLevel(DiagnosticSeverity diagnosticSeverity)
        {
            switch (diagnosticSeverity)
            {
                case DiagnosticSeverity.Info:
                    return null;

                case DiagnosticSeverity.Warning:
                    return GH_RuntimeMessageLevel.Warning;

                default:
                    return GH_RuntimeMessageLevel.Error;
            }
        }

        /// <summary>
        /// Raises the grouped runtime messages for <paramref name="records"/> and returns the text lines of the
        /// records that remain at <paramref name="detail"/> (Warnings and Errors always remain), in original order.
        /// </summary>
        internal static List<string> Report(GH_ActiveObject activeObject, IEnumerable<DiagnosticRecord> records, DiagnosticDetail detail)
        {
            List<DiagnosticRecord> diagnosticRecords = records?.Where(x => x != null).ToList() ?? new List<DiagnosticRecord>();

            if (activeObject != null)
            {
                foreach (DiagnosticMessage diagnosticMessage in diagnosticRecords.GetDiagnosticMessages())
                {
                    GH_RuntimeMessageLevel? level = ToRuntimeMessageLevel(diagnosticMessage.Severity);
                    if (level.HasValue)
                    {
                        activeObject.AddRuntimeMessage(level.Value, diagnosticMessage.Text);
                    }
                }
            }

            return diagnosticRecords.FilterDiagnosticRecords(detail).ConvertAll(x => x.Text);
        }

        /// <summary>
        /// Reads the optional <c>diagnosticDetail_</c> input (0 Summary, 1 Standard, 2 Detailed). A missing input (older
        /// saved definition), an unwired input or an undefined value gives Summary; an undefined value also returns a
        /// Warning record that the caller adds to the diagnostics.
        /// </summary>
        internal static DiagnosticDetail ReadDetail(GH_Component component, IGH_DataAccess dataAccess, out DiagnosticRecord invalidRecord)
        {
            invalidRecord = null;

            int index = component.Params.IndexOfInputParam(DetailInputName);
            if (index == -1)
            {
                return DiagnosticDetail.Summary;
            }

            int value = 0;
            if (!dataAccess.GetData(index, ref value))
            {
                return DiagnosticDetail.Summary;
            }

            if (SAM.Core.Query.TryGetDiagnosticDetail(value, out DiagnosticDetail diagnosticDetail))
            {
                return diagnosticDetail;
            }

            invalidRecord = SAM.Core.OCCT.Query.CreateDiagnosticRecord(string.Format("SAM_OCCT_DIAGNOSTIC_DETAIL_INVALID: Input {0} is {1}, which is not 0 (Summary), 1 (Standard) or 2 (Detailed). Summary is used.", DetailInputName, value), DiagnosticSeverity.Warning, DiagnosticDetail.Summary);
            return DiagnosticDetail.Summary;
        }
    }
}
