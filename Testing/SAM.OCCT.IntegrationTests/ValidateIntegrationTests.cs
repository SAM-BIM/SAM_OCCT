// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare a static Query class.
using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// End-to-end tests for the native validation + watertightness diagnostics
    /// (issue #37 follow-on). They drive the real BRepCheck_Analyzer /
    /// ShapeAnalysis_FreeBounds / BOPAlgo_ArgumentAnalyzer and auto-skip when the
    /// native library is absent.
    /// </summary>
    public class ValidateIntegrationTests
    {
        /// <summary>A unit box face soup missing its top face - five faces, an open rim.</summary>
        private static List<Face3D> CreateOpenBoxFaceSoup()
        {
            Point3D b00 = new Point3D(0, 0, 0), b10 = new Point3D(1, 0, 0), b11 = new Point3D(1, 1, 0), b01 = new Point3D(0, 1, 0);
            Point3D t00 = new Point3D(0, 0, 1), t10 = new Point3D(1, 0, 1), t11 = new Point3D(1, 1, 1), t01 = new Point3D(0, 1, 1);

            return new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(b00, b10, b11, b01), // bottom
                TestGeometry.CreatePlanarFace(b00, b10, t10, t00), // front
                TestGeometry.CreatePlanarFace(b01, b11, t11, t01), // back
                TestGeometry.CreatePlanarFace(b00, b01, t01, t00), // left
                TestGeometry.CreatePlanarFace(b10, b11, t11, t10)  // right (no top)
            };
        }

        [SkippableFact]
        public void Validate_ClosedBox_ReportsValidAndWatertight()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            List<Shell> shells = new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) };

            // Act
            bool success = GeometryQuery.Validate(shells, out OcctValidationReport report, out OcctCellComplexResult result);

            // Assert - a closed box is valid, watertight and has no naked edges.
            Assert.True(success);
            Assert.True(result.NativeAvailable);
            Assert.NotNull(report);
            Assert.True(report.IsValid);
            Assert.True(report.IsWatertight);
            Assert.True(report.IsCleanForGlue);
            Assert.Equal(0, report.CountOf(OcctValidationIssueCategory.NakedEdge));
        }

        [SkippableFact]
        public void Validate_OpenFaceSoup_ReportsNakedEdgesWithLocations()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - five faces of a box, top left open.
            List<Face3D> face3Ds = CreateOpenBoxFaceSoup();

            // Act
            bool success = GeometryQuery.Validate(face3Ds, out OcctValidationReport report, out OcctCellComplexResult result);

            // Assert - a report is produced and it flags the shape as not watertight.
            Assert.True(success);
            Assert.NotNull(report);
            Assert.False(report.IsWatertight);
            Assert.False(report.IsCleanForGlue);

            List<OcctValidationIssue> nakedEdges = report.IssuesOf(OcctValidationIssueCategory.NakedEdge).ToList();
            Assert.NotEmpty(nakedEdges);
            Assert.All(nakedEdges, x => Assert.NotNull(x.Location));
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_VALIDATE_NOT_WATERTIGHT");
        }
    }
}
