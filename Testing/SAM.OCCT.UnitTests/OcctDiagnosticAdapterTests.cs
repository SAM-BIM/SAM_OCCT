// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Solver;
using System;
using System.Collections.Generic;
using System.Linq;
using OcctQuery = SAM.Core.OCCT.Query;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Phase 3 (unified Diagnostics): the additive adapters from <see cref="OcctDiagnostic"/> and
    /// <see cref="SolverDiagnostic"/> to <see cref="Diagnostic"/>, and the detail filtering and runtime-message
    /// grouping built on them. Pure managed; the original diagnostic types are only read.
    /// </summary>
    public class OcctDiagnosticAdapterTests
    {
        // ── Severity mapping ─────────────────────────────────────────────

        [Theory]
        [InlineData(OcctDiagnosticSeverity.Info, DiagnosticSeverity.Info)]
        [InlineData(OcctDiagnosticSeverity.Warning, DiagnosticSeverity.Warning)]
        [InlineData(OcctDiagnosticSeverity.Error, DiagnosticSeverity.Error)]
        public void ToDiagnosticSeverity_EveryDefinedValue_MapsOneToOne(OcctDiagnosticSeverity input, DiagnosticSeverity expected)
        {
            // Arrange / Act
            DiagnosticSeverity actual = input.ToDiagnosticSeverity();

            // Assert
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ToDiagnosticSeverity_EveryEnumValue_IsCovered()
        {
            // Arrange: enum-driven, so a value added later is covered without a registry entry.
            foreach (OcctDiagnosticSeverity severity in Enum.GetValues(typeof(OcctDiagnosticSeverity)))
            {
                // Act
                DiagnosticSeverity mapped = severity.ToDiagnosticSeverity();

                // Assert: the name is the same, i.e. 1:1 by name.
                Assert.Equal(severity.ToString(), mapped.ToString());
            }
        }

        [Fact]
        public void ToDiagnosticSeverity_UndefinedValue_IsErrorNeverInfo()
        {
            // Arrange / Act
            DiagnosticSeverity mapped = ((OcctDiagnosticSeverity)99).ToDiagnosticSeverity();

            // Assert
            Assert.Equal(DiagnosticSeverity.Error, mapped);
        }

        [Fact]
        public void ToDiagnosticSeverity_EverySolverCodeAndSeverity_FollowsTheOriginalSeverity()
        {
            foreach (DiagnosticCode code in Enum.GetValues(typeof(DiagnosticCode)))
            {
                foreach (OcctDiagnosticSeverity severity in Enum.GetValues(typeof(OcctDiagnosticSeverity)))
                {
                    // Arrange
                    SolverDiagnostic solverDiagnostic = new SolverDiagnostic(SolverStage.Resolve, code, severity, "m");

                    // Act
                    Diagnostic diagnostic = solverDiagnostic.ToDiagnostic();

                    // Assert: severity depends on the original severity only, never on the code.
                    Assert.Equal(severity.ToDiagnosticSeverity(), diagnostic.Severity);
                    Assert.Equal(code.ToString(), diagnostic.Code);
                }
            }
        }

        // ── Adapter fidelity ─────────────────────────────────────────────

        [Fact]
        public void ToDiagnostic_OcctDiagnostic_PreservesCodeMessageSeverityAndSourceIndex()
        {
            // Arrange
            OcctDiagnostic original = new OcctDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_BUILD_UNRELIABLE", "the build is unreliable", 7);

            // Act
            Diagnostic diagnostic = original.ToDiagnostic();

            // Assert
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Equal("SAM_OCCT_BUILD_UNRELIABLE", diagnostic.Code);
            Assert.Equal("the build is unreliable", diagnostic.Message);
            Assert.Equal(7, diagnostic.SourceIndex);
            Assert.Equal("SAM_OCCT_BUILD_UNRELIABLE [7]: the build is unreliable", original.ToString());
        }

        [Fact]
        public void ToDiagnostic_Null_ReturnsNull()
        {
            Assert.Null(((OcctDiagnostic)null).ToDiagnostic());
            Assert.Null(((SolverDiagnostic)null).ToDiagnostic());
            Assert.Null(((OcctDiagnostic)null).ToDiagnosticRecord());
        }

        [Fact]
        public void ToDiagnosticRecord_OcctDiagnostic_KeepsTheLegacyText()
        {
            // Arrange
            OcctDiagnostic original = new OcctDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_NATIVE_FAILED", "status 40", 2);

            // Act
            DiagnosticRecord record = original.ToDiagnosticRecord();

            // Assert
            Assert.Equal(original.ToString(), record.Text);
            Assert.Equal(DiagnosticSeverity.Error, record.Diagnostic.Severity);
        }

        [Fact]
        public void ToDiagnostic_SolverDiagnostic_PreservesStageCodeSeverityAndTolerance()
        {
            // Arrange
            SolverDiagnostic original = new SolverDiagnostic(SolverStage.Heal, DiagnosticCode.NakedLoop, OcctDiagnosticSeverity.Warning, "naked loop", toleranceUsed: 0.01);

            // Act
            Diagnostic diagnostic = original.ToDiagnostic();

            // Assert
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Equal("NakedLoop", diagnostic.Code);
            Assert.Equal("Heal", diagnostic.Subject);
            Assert.Equal(0.01, diagnostic.Value);
            Assert.Equal("m", diagnostic.Unit);
            Assert.Equal("[Heal/NakedLoop/Warning] naked loop", original.ToString());
        }

        [Fact]
        public void ToDiagnostic_SolverDiagnosticWithoutTolerance_HasNoValue()
        {
            // Arrange / Act
            Diagnostic diagnostic = new SolverDiagnostic(SolverStage.Snap, DiagnosticCode.Gap, OcctDiagnosticSeverity.Info, "gap").ToDiagnostic();

            // Assert
            Assert.Null(diagnostic.Value);
            Assert.Null(diagnostic.Unit);
        }

        // ── Detail classification: a few families, unknown is Standard, never decides severity ──

        [Theory]
        [InlineData("SAM_OCCT_ANALYTICAL_PANEL_SUCCESS", DiagnosticDetail.Summary)]
        [InlineData("SAM_OCCT_SOLVE3D_RESULT", DiagnosticDetail.Summary)]
        [InlineData("SAM_OCCT_SOLVE3D_INPUT_EMPTY", DiagnosticDetail.Summary)]
        [InlineData("SAM_OCCT_ANALYTICAL_COMPLEX_CONSUMED", DiagnosticDetail.Summary)]
        [InlineData("SAM_OCCT_OPEN_SHELL_ANALYSIS", DiagnosticDetail.Summary)]
        [InlineData("SAM_OCCT_EXTEND3D_HOLE_DROPPED", DiagnosticDetail.Summary)]
        [InlineData("SAM_OCCT_ANALYTICAL_PANEL_METADATA", DiagnosticDetail.Detailed)]
        [InlineData("SAM_OCCT_EXTEND3D_PANEL", DiagnosticDetail.Detailed)]
        [InlineData("SAM_OCCT_TIMING_TOTAL", DiagnosticDetail.Standard)]
        [InlineData("SAM_OCCT_SOME_FUTURE_CODE", DiagnosticDetail.Standard)]
        [InlineData("", DiagnosticDetail.Standard)]
        [InlineData(null, DiagnosticDetail.Standard)]
        public void GetDiagnosticDetail_CodeFamilies_AreClassified(string code, DiagnosticDetail expected)
        {
            Assert.Equal(expected, OcctQuery.GetDiagnosticDetail(code));
        }

        [Theory]
        [InlineData("SAM_OCCT_SOME_FUTURE_CODE")]
        [InlineData("SAM_OCCT_ANALYTICAL_PANEL_METADATA")]
        [InlineData("SAM_OCCT_TIMING_TOTAL")]
        public void ToDiagnostic_AnyCode_NeverChangesTheSeverity(string code)
        {
            foreach (OcctDiagnosticSeverity severity in Enum.GetValues(typeof(OcctDiagnosticSeverity)))
            {
                Diagnostic diagnostic = new OcctDiagnostic(severity, code, "m").ToDiagnostic();
                Assert.Equal(severity.ToDiagnosticSeverity(), diagnostic.Severity);
            }
        }

        [Fact]
        public void GetDiagnosticDetail_EverySolverCode_HasADefinedLevel()
        {
            foreach (DiagnosticCode code in Enum.GetValues(typeof(DiagnosticCode)))
            {
                Assert.True(Enum.IsDefined(typeof(DiagnosticDetail), SolverDiagnosticExtensions.GetDiagnosticDetail(code)));
            }
        }

        // ── Filtering at Summary / Standard / Detailed ───────────────────

        private static List<DiagnosticRecord> MixedRecords()
        {
            string[] codes =
            {
                "SAM_OCCT_ANALYTICAL_PANEL_SUCCESS", "SAM_OCCT_TIMING_TOTAL", "SAM_OCCT_ANALYTICAL_PANEL_METADATA",
                "SAM_OCCT_BUILD_UNRELIABLE", "SAM_OCCT_NATIVE_FAILED", "SAM_OCCT_FUTURE_UNKNOWN_1", "SAM_OCCT_FUTURE_UNKNOWN_2", string.Empty, null,
                "SAM_OCCT_EXTEND3D_PANEL", "SAM_OCCT_SOLVE3D_RESULT"
            };

            List<DiagnosticRecord> result = new List<DiagnosticRecord>();
            int index = 0;
            foreach (OcctDiagnosticSeverity severity in Enum.GetValues(typeof(OcctDiagnosticSeverity)))
            {
                foreach (string code in codes)
                {
                    result.Add(new OcctDiagnostic(severity, code, "message " + index, index).ToDiagnosticRecord());
                    index++;
                }
            }

            return result;
        }

        [Theory]
        [InlineData(DiagnosticDetail.Summary)]
        [InlineData(DiagnosticDetail.Standard)]
        [InlineData(DiagnosticDetail.Detailed)]
        public void FilterDiagnosticRecords_AnyLevel_KeepsEveryWarningAndErrorInOrderWithTheirText(DiagnosticDetail detail)
        {
            // Arrange
            List<DiagnosticRecord> records = MixedRecords();
            List<DiagnosticRecord> expected = records.Where(x => x.Diagnostic.Severity != DiagnosticSeverity.Info).ToList();

            // Act
            List<DiagnosticRecord> filtered = records.FilterDiagnosticRecords(detail);

            // Assert
            List<DiagnosticRecord> kept = filtered.Where(x => x.Diagnostic.Severity != DiagnosticSeverity.Info).ToList();
            Assert.Equal(expected.Count, kept.Count);
            Assert.True(expected.SequenceEqual(kept), "Warnings/Errors must be the same instances in the same order.");
            Assert.Equal(expected.Select(x => x.Text), kept.Select(x => x.Text));
        }

        [Fact]
        public void FilterDiagnosticRecords_InfoCount_IsMonotonicAcrossLevels()
        {
            // Arrange
            List<DiagnosticRecord> records = MixedRecords();

            // Act
            int summary = records.FilterDiagnosticRecords(DiagnosticDetail.Summary).Count(x => x.Diagnostic.Severity == DiagnosticSeverity.Info);
            int standard = records.FilterDiagnosticRecords(DiagnosticDetail.Standard).Count(x => x.Diagnostic.Severity == DiagnosticSeverity.Info);
            int detailed = records.FilterDiagnosticRecords(DiagnosticDetail.Detailed).Count(x => x.Diagnostic.Severity == DiagnosticSeverity.Info);

            // Assert
            Assert.True(summary < standard);
            Assert.True(standard < detailed);
            Assert.Equal(records.Count(x => x.Diagnostic.Severity == DiagnosticSeverity.Info), detailed);
        }

        [Fact]
        public void FilterDiagnosticRecords_Detailed_ReturnsEveryRecordUnchanged()
        {
            // Arrange
            List<DiagnosticRecord> records = MixedRecords();

            // Act
            List<DiagnosticRecord> filtered = records.FilterDiagnosticRecords(DiagnosticDetail.Detailed);

            // Assert: the legacy Diagnostics output at Detailed equals the pre-change list.
            Assert.True(records.SequenceEqual(filtered));
        }

        [Fact]
        public void FilterDiagnosticRecords_UnknownCodeInfo_AppearsAtStandardNotSummary()
        {
            // Arrange
            List<DiagnosticRecord> records = new List<DiagnosticRecord> { new OcctDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_FUTURE_UNKNOWN_1", "m").ToDiagnosticRecord() };

            // Act / Assert
            Assert.Empty(records.FilterDiagnosticRecords(DiagnosticDetail.Summary));
            Assert.Single(records.FilterDiagnosticRecords(DiagnosticDetail.Standard));
        }

        [Fact]
        public void FilterDiagnosticRecords_NullAndNullEntries_AreSkipped()
        {
            Assert.Empty(((IEnumerable<DiagnosticRecord>)null).FilterDiagnosticRecords(DiagnosticDetail.Detailed));
            Assert.Empty(new DiagnosticRecord[] { null, new DiagnosticRecord(null, "x") }.FilterDiagnosticRecords(DiagnosticDetail.Detailed));
        }

        // ── No severity inference from text ─────────────────────────────

        [Fact]
        public void CreateDiagnosticRecord_TextThatLooksLikeAWarning_KeepsTheSeverityGiven()
        {
            // Arrange / Act
            DiagnosticRecord info = OcctQuery.CreateDiagnosticRecord("SAM_OCCT_X_WARNING: Warning: failed Error", DiagnosticSeverity.Info);
            DiagnosticRecord error = OcctQuery.CreateDiagnosticRecord("SAM_OCCT_X_INFO: fine", DiagnosticSeverity.Error);

            // Assert
            Assert.Equal(DiagnosticSeverity.Info, info.Diagnostic.Severity);
            Assert.Equal("SAM_OCCT_X_WARNING", info.Diagnostic.Code);
            Assert.Equal(DiagnosticSeverity.Error, error.Diagnostic.Severity);
            Assert.Equal("SAM_OCCT_X_WARNING: Warning: failed Error", info.Text);
        }

        [Fact]
        public void CreateDiagnosticRecord_NoColon_HasNoCodeAndStandardDetail()
        {
            DiagnosticRecord record = OcctQuery.CreateDiagnosticRecord("plain text", DiagnosticSeverity.Info);

            Assert.Null(record.Diagnostic.Code);
            Assert.Equal(DiagnosticDetail.Standard, record.Diagnostic.Detail);
        }

        // ── Solver records: same lines as FormatDiagnostics, real severity ──

        [Fact]
        public void FormatDiagnosticRecords_EverySeverity_TextEqualsFormatDiagnosticsAndSeverityIsTheSolvers()
        {
            // Arrange
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            diagnostics.Add(SolverStage.Resolve, DiagnosticCode.AdoptedLevel, OcctDiagnosticSeverity.Info, "Warning: looks bad but is Info");
            diagnostics.Add(SolverStage.Heal, DiagnosticCode.NakedLoop, OcctDiagnosticSeverity.Warning, "an open loop");
            diagnostics.Add(SolverStage.Heal, DiagnosticCode.SpacesRefused, OcctDiagnosticSeverity.Error, "refused");

            // Act
            List<DiagnosticRecord> records = Analytical.OCCT.Solver.SolverReportFormat.FormatDiagnosticRecords(diagnostics, "SAM_OCCT_SOLVE3D");
            List<string> lines = Analytical.OCCT.Solver.SolverReportFormat.FormatDiagnostics(diagnostics, "SAM_OCCT_SOLVE3D");

            // Assert
            Assert.Equal(lines, records.Select(x => x.Text).ToList());
            Assert.Equal(new[] { DiagnosticSeverity.Info, DiagnosticSeverity.Warning, DiagnosticSeverity.Error }, records.Select(x => x.Diagnostic.Severity).ToArray());
            Assert.Equal("SAM_OCCT_SOLVE3D_DIAGNOSTIC: [Resolve/AdoptedLevel/Info] Warning: looks bad but is Info", lines[0]);
        }

        [Fact]
        public void FormatDiagnosticRecords_NullDiagnostics_ReturnsEmpty()
        {
            Assert.Empty(Analytical.OCCT.Solver.SolverReportFormat.FormatDiagnosticRecords(null, "P"));
        }

        // ── Runtime message grouping ─────────────────────────────────────

        private static DiagnosticRecord Record(DiagnosticSeverity severity, string code, string text)
        {
            return new DiagnosticRecord(new Diagnostic(severity, DiagnosticDetail.Summary, null, text, code), text);
        }

        [Fact]
        public void GetDiagnosticMessages_InfoOnly_RaisesNothing()
        {
            // Arrange
            List<DiagnosticRecord> records = new List<DiagnosticRecord> { Record(DiagnosticSeverity.Info, "A", "a"), Record(DiagnosticSeverity.Info, "B", "b") };

            // Act / Assert
            Assert.Empty(records.GetDiagnosticMessages());
        }

        [Fact]
        public void GetDiagnosticMessages_WarningAndError_KeepTheirSeverityAndText()
        {
            // Arrange
            List<DiagnosticRecord> records = new List<DiagnosticRecord> { Record(DiagnosticSeverity.Warning, "W", "w text"), Record(DiagnosticSeverity.Error, "E", "e text") };

            // Act
            List<DiagnosticMessage> messages = records.GetDiagnosticMessages();

            // Assert
            Assert.Equal(2, messages.Count);
            Assert.Equal(DiagnosticSeverity.Warning, messages[0].Severity);
            Assert.Equal("w text", messages[0].Text);
            Assert.Equal(DiagnosticSeverity.Error, messages[1].Severity);
            Assert.Equal("e text", messages[1].Text);
        }

        [Fact]
        public void GetDiagnosticMessages_ExactDuplicates_AreReportedOnce()
        {
            // Arrange
            List<DiagnosticRecord> records = new List<DiagnosticRecord> { Record(DiagnosticSeverity.Warning, "W", "same"), Record(DiagnosticSeverity.Warning, "W", "same"), Record(DiagnosticSeverity.Error, "E", "boom"), Record(DiagnosticSeverity.Error, "E", "boom") };

            // Act / Assert
            Assert.Equal(2, records.GetDiagnosticMessages().Count);
        }

        [Fact]
        public void GetDiagnosticMessages_ErrorsWithDifferentText_AreNeverMerged()
        {
            // Arrange
            List<DiagnosticRecord> records = Enumerable.Range(0, 10).Select(i => Record(DiagnosticSeverity.Error, "SAME_CODE", "failure " + i)).ToList();

            // Act
            List<DiagnosticMessage> messages = records.GetDiagnosticMessages();

            // Assert
            Assert.Equal(10, messages.Count);
            Assert.All(messages, x => Assert.False(x.IsGrouped));
        }

        [Fact]
        public void GetDiagnosticMessages_ManyWarningsOfOneCode_CollapseIntoOneCountMessageAfterThree()
        {
            // Arrange
            List<DiagnosticRecord> records = Enumerable.Range(0, 10).Select(i => Record(DiagnosticSeverity.Warning, "SAM_OCCT_NAKED", "naked " + i)).ToList();

            // Act
            List<DiagnosticMessage> messages = records.GetDiagnosticMessages();

            // Assert: three individual texts, then one count line for the other seven.
            Assert.Equal(4, messages.Count);
            Assert.Equal(new[] { "naked 0", "naked 1", "naked 2" }, messages.Take(3).Select(x => x.Text).ToArray());
            Assert.True(messages[3].IsGrouped);
            Assert.Contains("7 more", messages[3].Text);
            Assert.Contains("SAM_OCCT_NAKED", messages[3].Text);
        }

        [Fact]
        public void GetDiagnosticMessages_WarningsOfDifferentCodes_StayDistinct()
        {
            // Arrange
            List<DiagnosticRecord> records = new List<DiagnosticRecord>();
            for (int i = 0; i < 5; i++)
            {
                records.Add(Record(DiagnosticSeverity.Warning, "CODE_A", "a" + i));
                records.Add(Record(DiagnosticSeverity.Warning, "CODE_B", "b" + i));
            }

            // Act
            List<DiagnosticMessage> messages = records.GetDiagnosticMessages();

            // Assert: 3 + 3 individual, plus one count line per code.
            Assert.Equal(8, messages.Count);
            Assert.Equal(2, messages.Count(x => x.IsGrouped));
            Assert.Contains(messages, x => x.IsGrouped && x.Code == "CODE_A");
            Assert.Contains(messages, x => x.IsGrouped && x.Code == "CODE_B");
        }

        [Fact]
        public void GetDiagnosticMessages_GroupingNeverChangesTheDiagnosticsText()
        {
            // Arrange
            List<DiagnosticRecord> records = Enumerable.Range(0, 10).Select(i => Record(DiagnosticSeverity.Warning, "SAME", "text " + i)).ToList();

            // Act
            records.GetDiagnosticMessages();
            List<string> lines = records.FilterDiagnosticRecords(DiagnosticDetail.Summary).Select(x => x.Text).ToList();

            // Assert: the full individual text of every Warning remains in the Diagnostics list.
            Assert.Equal(10, lines.Count);
            Assert.Equal("text 9", lines[9]);
        }

        [Fact]
        public void GetDiagnosticMessages_NullRecords_ReturnsEmpty()
        {
            Assert.Empty(((IEnumerable<DiagnosticRecord>)null).GetDiagnosticMessages());
            Assert.Empty(new DiagnosticRecord[] { null }.GetDiagnosticMessages());
        }
    }
}
