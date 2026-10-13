// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Native;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Input-face coverage (<see cref="SAM.Geometry.OCCT.Query.FaceCoverage(IEnumerable{Face3D}, IEnumerable{Shell}, double, double, double, double)"/>)
    /// and the under-split fallback criteria (<see cref="OcctUnderSplitGuard"/>) on synthetic box cells - pure
    /// managed, no native build.
    /// </summary>
    public class FaceCoverageTests
    {
        private const double Tolerance = 0.001;
        private const double Fuzzy = 0.01;

        private static Shell Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            return new Shell(new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x0, y1, z0)),
                TestGeometry.CreatePlanarFace(new Point3D(x0, y0, z1), new Point3D(x1, y0, z1), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1)),
                TestGeometry.CreatePlanarFace(new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y0, z1), new Point3D(x0, y0, z1)),
                TestGeometry.CreatePlanarFace(new Point3D(x0, y1, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1)),
                TestGeometry.CreatePlanarFace(new Point3D(x0, y0, z0), new Point3D(x0, y1, z0), new Point3D(x0, y1, z1), new Point3D(x0, y0, z1)),
                TestGeometry.CreatePlanarFace(new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x1, y0, z1)),
            });
        }

        private static OcctCell Cell(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            return new OcctCell(Box(x0, y0, z0, x1, y1, z1), (x1 - x0) * (y1 - y0) * (z1 - z0), center: new Point3D((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2));
        }

        // A vertical partition at x, spanning y0..y1 and z 0..1.
        private static Face3D PartitionAtX(double x, double y0 = 0, double y1 = 1)
        {
            return TestGeometry.CreatePlanarFace(new Point3D(x, y0, 0), new Point3D(x, y1, 0), new Point3D(x, y1, 1), new Point3D(x, y0, 1));
        }

        private static FaceCoverageReport Coverage(IEnumerable<Face3D> faces, params Shell[] cells)
        {
            return SAM.Geometry.OCCT.Query.FaceCoverage(faces, cells, 0.01, Tolerance, Fuzzy);
        }

        [Fact]
        public void FaceCoverage_PartitionBetweenTwoCells_IsCovered()
        {
            // Arrange
            List<Face3D> faces = new List<Face3D> { PartitionAtX(1) };

            // Act
            FaceCoverageReport report = Coverage(faces, Box(0, 0, 0, 1, 1, 1), Box(1, 0, 0, 2, 1, 1));

            // Assert
            Assert.Equal(FaceCoverageVerdict.Covered, report.Entries.Single().Verdict);
            Assert.Equal(1.0, report.CoveredArea, 3);
            Assert.True(report.IsClean);
        }

        [Fact]
        public void FaceCoverage_FaceSubdividedAcrossCells_IsCovered()
        {
            // Arrange: one floor face under two cells is represented by two cell faces.
            Face3D floor = TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 1, 0), new Point3D(0, 1, 0));

            // Act
            FaceCoverageReport report = Coverage(new[] { floor }, Box(0, 0, 0, 1, 1, 1), Box(1, 0, 0, 2, 1, 1));

            // Assert
            Assert.Equal(FaceCoverageVerdict.Covered, report.Entries.Single().Verdict);
            Assert.Equal(2.0, report.CoveredArea, 3);
        }

        [Fact]
        public void FaceCoverage_PartitionInsideOneCell_IsMissingWithInteriorArea()
        {
            // Arrange: the partition should split the cell but the cell runs straight through it.
            List<Face3D> faces = new List<Face3D> { PartitionAtX(1) };

            // Act
            FaceCoverageReport report = Coverage(faces, Box(0, 0, 0, 2, 1, 1));

            // Assert
            Assert.Equal(FaceCoverageVerdict.Missing, report.Entries.Single().Verdict);
            Assert.Equal(1.0, report.InteriorArea, 3);
            Assert.False(report.IsClean);
        }

        [Fact]
        public void FaceCoverage_EaveOverhangBeyondCell_IsTrimmed()
        {
            // Arrange: a roof face overhanging the cell by 0.5 on each side.
            Face3D roof = TestGeometry.CreatePlanarFace(new Point3D(-0.5, 0, 1), new Point3D(1.5, 0, 1), new Point3D(1.5, 1, 1), new Point3D(-0.5, 1, 1));

            // Act
            FaceCoverageReport report = Coverage(new[] { roof }, Box(0, 0, 0, 1, 1, 1));

            // Assert
            FaceCoverageEntry entry = report.Entries.Single();
            Assert.Equal(FaceCoverageVerdict.Trimmed, entry.Verdict);
            Assert.Equal(1.0, entry.CoveredArea, 3);
            Assert.Equal(1.0, entry.ExteriorArea, 3);
            Assert.True(report.IsClean);
        }

        [Fact]
        public void FaceCoverage_FreeStandingWallOutsideCells_IsUnused()
        {
            // Arrange
            List<Face3D> faces = new List<Face3D> { PartitionAtX(3) };

            // Act
            FaceCoverageReport report = Coverage(faces, Box(0, 0, 0, 1, 1, 1));

            // Assert
            Assert.Equal(FaceCoverageVerdict.Unused, report.Entries.Single().Verdict);
            Assert.Equal(1.0, report.UnusedArea, 3);
            Assert.Equal(0.0, report.InteriorArea, 6);
        }

        [Fact]
        public void FaceCoverage_FaceBelowMinArea_IsNotEvaluated()
        {
            // Arrange
            Face3D tiny = TestGeometry.CreatePlanarFace(new Point3D(1, 0, 0), new Point3D(1, 0.05, 0), new Point3D(1, 0.05, 0.05), new Point3D(1, 0, 0.05));

            // Act
            FaceCoverageReport report = Coverage(new[] { tiny }, Box(0, 0, 0, 2, 1, 1));

            // Assert
            Assert.Empty(report.Entries);
        }

        [Fact]
        public void IsSuspect_InteriorOrUnusedArea_ReturnsTrue()
        {
            // Arrange
            FaceCoverageReport clean = Coverage(new[] { PartitionAtX(1) }, Box(0, 0, 0, 1, 1, 1), Box(1, 0, 0, 2, 1, 1));
            FaceCoverageReport interior = Coverage(new[] { PartitionAtX(1) }, Box(0, 0, 0, 2, 1, 1));
            FaceCoverageReport unused = Coverage(new[] { PartitionAtX(3) }, Box(0, 0, 0, 1, 1, 1));

            // Act / Assert
            Assert.False(OcctUnderSplitGuard.IsSuspect(clean));
            Assert.True(OcctUnderSplitGuard.IsSuspect(interior));
            Assert.True(OcctUnderSplitGuard.IsSuspect(unused));
        }

        [Fact]
        public void ValidateCells_DisjointBoxes_HasNoFailures()
        {
            // Arrange
            List<OcctCell> cells = new List<OcctCell> { Cell(0, 0, 0, 1, 1, 1), Cell(1, 0, 0, 2, 1, 1) };

            // Act
            List<string> failures = OcctUnderSplitGuard.ValidateCells(cells, new OcctBuildOptions());

            // Assert
            Assert.Empty(failures);
        }

        [Fact]
        public void ValidateCells_OverlappingBoxes_ReportsOverlap()
        {
            // Arrange
            List<OcctCell> cells = new List<OcctCell> { Cell(0, 0, 0, 2, 1, 1), Cell(0.5, 0, 0, 1.5, 1, 1) };

            // Act
            List<string> failures = OcctUnderSplitGuard.ValidateCells(cells, new OcctBuildOptions());

            // Assert
            Assert.Contains(failures, x => x.Contains("overlap"));
        }

        [Fact]
        public void ValidateCells_SliverCell_ReportsSliver()
        {
            // Arrange: 2 x 1 x 0.0002 - mean thickness well below the default 0.001 fuzzy tolerance.
            List<OcctCell> cells = new List<OcctCell> { Cell(0, 0, 0, 2, 1, 0.0002) };

            // Act
            List<string> failures = OcctUnderSplitGuard.ValidateCells(cells, new OcctBuildOptions());

            // Assert
            Assert.True(failures.Any(x => x.Contains("sliver")), string.Join(" | ", failures));
        }

        [Fact]
        public void CheckRefinement_FineCellsPartitionCoarseCell_HasNoFailures()
        {
            // Arrange
            List<OcctCell> coarse = new List<OcctCell> { Cell(0, 0, 0, 2, 1, 1) };
            List<OcctCell> fine = new List<OcctCell> { Cell(0, 0, 0, 1, 1, 1), Cell(1, 0, 0, 2, 1, 1), Cell(0, 0, 1, 2, 1, 2) };

            // Act
            List<string> failures = OcctUnderSplitGuard.CheckRefinement(coarse, fine, new OcctBuildOptions());

            // Assert: the third fine cell sits in space the coarse build left empty, which is allowed.
            Assert.Empty(failures);
        }

        [Fact]
        public void CheckRefinement_FineCellsMissPartOfCoarseCell_ReportsFailure()
        {
            // Arrange
            List<OcctCell> coarse = new List<OcctCell> { Cell(0, 0, 0, 2, 1, 1) };
            List<OcctCell> fine = new List<OcctCell> { Cell(0, 0, 0, 1, 1, 1) };

            // Act
            List<string> failures = OcctUnderSplitGuard.CheckRefinement(coarse, fine, new OcctBuildOptions());

            // Assert
            Assert.Contains(failures, x => x.Contains("not partitioned"));
        }

        [Fact]
        public void CheckCoverageImprovement_DirectSplitsMissingPartition_HasNoFailures()
        {
            // Arrange
            List<Face3D> faces = Box(0, 0, 0, 2, 1, 1).Face3Ds.Concat(new[] { PartitionAtX(1) }).ToList();
            FaceCoverageReport before = Coverage(faces, Box(0, 0, 0, 2, 1, 1));
            FaceCoverageReport after = Coverage(faces, Box(0, 0, 0, 1, 1, 1), Box(1, 0, 0, 2, 1, 1));

            // Act
            List<string> failures = OcctUnderSplitGuard.CheckCoverageImprovement(before, after, Tolerance);

            // Assert
            Assert.Empty(failures);
        }

        [Fact]
        public void CheckCoverageImprovement_DirectLosesAFace_ReportsRegression()
        {
            // Arrange: the alternative covers the partition but drops the right-hand cell entirely.
            List<Face3D> faces = Box(0, 0, 0, 2, 1, 1).Face3Ds.Concat(new[] { PartitionAtX(1) }).ToList();
            FaceCoverageReport before = Coverage(faces, Box(0, 0, 0, 2, 1, 1));
            FaceCoverageReport after = Coverage(faces, Box(0, 0, 0, 1, 1, 1));

            // Act
            List<string> failures = OcctUnderSplitGuard.CheckCoverageImprovement(before, after, Tolerance);

            // Assert
            Assert.Contains(failures, x => x.Contains("lose coverage"));
        }

        [Fact]
        public void Evaluate_ValidRefinementThatSplitsPartition_Adopts()
        {
            // Arrange
            List<Face3D> faces = Box(0, 0, 0, 2, 1, 1).Face3Ds.Concat(new[] { PartitionAtX(1) }).ToList();
            List<OcctCell> sew = new List<OcctCell> { Cell(0, 0, 0, 2, 1, 1) };
            List<OcctCell> direct = new List<OcctCell> { Cell(0, 0, 0, 1, 1, 1), Cell(1, 0, 0, 2, 1, 1) };

            // Act
            OcctUnderSplitGuard.Evaluation evaluation = OcctUnderSplitGuard.Evaluate(faces, sew, direct, new OcctBuildOptions(), 0.01);

            // Assert
            Assert.True(evaluation.Adopt, string.Join("; ", evaluation.Failures));
        }

        [Fact]
        public void Evaluate_AlternativeWithOverlappingCells_IsRejected()
        {
            // Arrange
            List<Face3D> faces = Box(0, 0, 0, 2, 1, 1).Face3Ds.Concat(new[] { PartitionAtX(1) }).ToList();
            List<OcctCell> sew = new List<OcctCell> { Cell(0, 0, 0, 2, 1, 1) };
            List<OcctCell> direct = new List<OcctCell> { Cell(0, 0, 0, 1, 1, 1), Cell(1, 0, 0, 2, 1, 1), Cell(0.5, 0, 0, 1.5, 1, 1) };

            // Act
            OcctUnderSplitGuard.Evaluation evaluation = OcctUnderSplitGuard.Evaluate(faces, sew, direct, new OcctBuildOptions(), 0.01);

            // Assert
            Assert.False(evaluation.Adopt);
            Assert.Contains(evaluation.Failures, x => x.Contains("overlap"));
        }
    }
}
