// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Native;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using Xunit;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare a static Query class.
using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Pure-managed guards, status mapping and report/summary logic for the
    /// native validation + watertightness diagnostics (issue #37 follow-on). The
    /// guards return before any native call and the rest is pure C#, so these run
    /// deterministically on any CI agent without OCCT.
    /// </summary>
    public class ValidateGuardTests
    {
        [Fact]
        public void Validate_NullFaceInput_ReturnsFalseWithEmptyDiagnostic()
        {
            bool success = GeometryQuery.Validate((IEnumerable<Face3D>)null, out OcctValidationReport report, out OcctCellComplexResult result, new OcctBuildOptions());

            Assert.False(success);
            Assert.Null(report);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VALIDATE_INPUT_EMPTY" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void Validate_EmptyShellInput_ReturnsFalseWithEmptyDiagnostic()
        {
            bool success = GeometryQuery.Validate(new List<Shell>(), out OcctValidationReport report, out OcctCellComplexResult result, new OcctBuildOptions());

            Assert.False(success);
            Assert.Null(report);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VALIDATE_INPUT_EMPTY");
        }

        [Fact]
        public void Validate_NullTopology_ReturnsFalseWithEmptyDiagnostic()
        {
            bool success = GeometryQuery.Validate((OcctTopology)null, out OcctValidationReport report, out OcctCellComplexResult result, new OcctBuildOptions());

            Assert.False(success);
            Assert.Null(report);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VALIDATE_INPUT_EMPTY");
        }

        [Theory]
        [InlineData(10, "null output pointer")]
        [InlineData(50, "shape handle was invalid")]
        [InlineData(99, "unexpected native exception")]
        public void DescribeValidateStatus_KnownStatus_ReturnsExplanation(int status, string fragment)
        {
            string message = OcctOpenShellAnalysis.DescribeValidateStatus(status);

            Assert.Contains(fragment, message);
        }

        [Fact]
        public void DescribeValidateStatus_UnknownStatus_ReturnsFallback()
        {
            Assert.Equal("unrecognised native status", OcctOpenShellAnalysis.DescribeValidateStatus(123));
        }

        [Fact]
        public void Report_ValidWatertight_IsCleanForGlue()
        {
            // Arrange & Act
            OcctValidationReport report = new OcctValidationReport(true, true, new List<OcctValidationIssue>());

            // Assert
            Assert.True(report.IsCleanForGlue);
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public void Report_InvalidOrOpen_IsNotCleanForGlue(bool isValid, bool isWatertight)
        {
            OcctValidationReport report = new OcctValidationReport(isValid, isWatertight, new List<OcctValidationIssue>());

            Assert.False(report.IsCleanForGlue);
        }

        [Fact]
        public void Report_CategoryQueries_CountAndFilterIssues()
        {
            // Arrange - two naked edges and one self-intersection.
            List<OcctValidationIssue> issues = new List<OcctValidationIssue>
            {
                new OcctValidationIssue(OcctValidationIssueCategory.NakedEdge, new Point3D(0, 0, 0), 0.5),
                new OcctValidationIssue(OcctValidationIssueCategory.NakedEdge, new Point3D(1, 0, 0), 0.25),
                new OcctValidationIssue(OcctValidationIssueCategory.SelfIntersection, new Point3D(2, 0, 0), 0.0)
            };
            OcctValidationReport report = new OcctValidationReport(false, false, issues);

            // Assert
            Assert.Equal(2, report.CountOf(OcctValidationIssueCategory.NakedEdge));
            Assert.Equal(1, report.CountOf(OcctValidationIssueCategory.SelfIntersection));
            Assert.Equal(0, report.CountOf(OcctValidationIssueCategory.SmallFace));
            Assert.Equal(2, System.Linq.Enumerable.Count(report.IssuesOf(OcctValidationIssueCategory.NakedEdge)));
        }

        [Fact]
        public void Issue_NakedEdge_ToStringDescribesLengthAndLocation()
        {
            OcctValidationIssue issue = new OcctValidationIssue(OcctValidationIssueCategory.NakedEdge, new Point3D(1, 2, 3), 0.5);

            string text = issue.ToString();

            Assert.Contains("Naked edge", text);
            Assert.Contains("0.5", text);
        }

        [Fact]
        public void Report_NullIssues_YieldsEmptyList()
        {
            OcctValidationReport report = new OcctValidationReport(true, true, null);

            Assert.NotNull(report.Issues);
            Assert.Empty(report.Issues);
        }
    }
}
