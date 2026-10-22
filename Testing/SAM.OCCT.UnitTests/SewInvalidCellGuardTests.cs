// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Native;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// The sew guard against demonstrably invalid sew builds (inverted or duplicated envelope cells, observed on
    /// translated Revit-home panels). Synthetic box cells and a stand-in for the direct native build, so every
    /// decision path, including fail-closed, runs without OCCT.
    /// </summary>
    public class SewInvalidCellGuardTests
    {
        private const double Tolerance = 0.001;
        private const double Fuzzy = 0.01;

        private static OcctBuildOptions Options(bool retainTopology = false)
        {
            return new OcctBuildOptions { Tolerance = Tolerance, FuzzyTolerance = Fuzzy, SewBeforeBuild = true, ValidateSewCoverage = true, RetainTopology = retainTopology };
        }

        private static Shell Box(double x0, double x1)
        {
            return new Shell(new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(x0, 0, 0), new Point3D(x1, 0, 0), new Point3D(x1, 1, 0), new Point3D(x0, 1, 0)),
                TestGeometry.CreatePlanarFace(new Point3D(x0, 0, 1), new Point3D(x1, 0, 1), new Point3D(x1, 1, 1), new Point3D(x0, 1, 1)),
                TestGeometry.CreatePlanarFace(new Point3D(x0, 0, 0), new Point3D(x1, 0, 0), new Point3D(x1, 0, 1), new Point3D(x0, 0, 1)),
                TestGeometry.CreatePlanarFace(new Point3D(x0, 1, 0), new Point3D(x1, 1, 0), new Point3D(x1, 1, 1), new Point3D(x0, 1, 1)),
                TestGeometry.CreatePlanarFace(new Point3D(x0, 0, 0), new Point3D(x0, 1, 0), new Point3D(x0, 1, 1), new Point3D(x0, 0, 1)),
                TestGeometry.CreatePlanarFace(new Point3D(x1, 0, 0), new Point3D(x1, 1, 0), new Point3D(x1, 1, 1), new Point3D(x1, 0, 1)),
            });
        }

        // A 1 x 1 x (x1 - x0) box cell; volume overrides the true volume (negative = inverted).
        private static OcctCell Cell(double x0, double x1, double? volume = null)
        {
            return new OcctCell(Box(x0, x1), volume ?? (x1 - x0), center: new Point3D((x0 + x1) / 2, 0.5, 0.5));
        }

        // Two rooms (x 0..1 and 1..2) separated by a partition at x = 1: the outer box faces plus the partition.
        private static List<Face3D> TwoRoomFaces()
        {
            List<Face3D> faces = Box(0, 2).Face3Ds.ToList();
            faces.Add(TestGeometry.CreatePlanarFace(new Point3D(1, 0, 0), new Point3D(1, 1, 0), new Point3D(1, 1, 1), new Point3D(1, 0, 1)));
            return faces;
        }

        private static OcctCellComplexResult Candidate(params OcctCell[] cells)
        {
            OcctCellComplexResult result = new OcctCellComplexResult { NativeAvailable = true };
            foreach (OcctCell cell in cells)
            {
                result.AddCell(cell);
            }

            return result;
        }

        // Stand-in for the isolated direct MakerVolume build: returns the status and fills the candidate on 0.
        private static OcctCellComplexBuilder.DirectBuild Direct(int status, params OcctCell[] cells)
        {
            return (OcctCellComplexResult candidate, out bool decoded) =>
            {
                decoded = status == 0;
                if (status == 0)
                {
                    foreach (OcctCell cell in cells)
                    {
                        candidate.AddCell(cell);
                    }
                }

                return status;
            };
        }

        private static OcctTopology FakeTopology()
        {
            // An invalid (zero) handle: disposing it closes the SafeHandle without a native call.
            return (OcctTopology)Activator.CreateInstance(typeof(OcctTopology), nonPublic: true);
        }

        private static bool IsCleanSewCoverage(List<Face3D> faces, OcctCellComplexResult sew)
        {
            return SAM.Geometry.OCCT.Query.FaceCoverage(faces, sew.Cells.Select(x => x.Shell), 0.01, Tolerance, Fuzzy).IsClean;
        }

        private static void AssertFailedClosed(bool adopted, OcctCellComplexResult result)
        {
            Assert.False(adopted);
            Assert.Empty(result.Cells);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_BUILD_INVALID" && x.Severity == OcctDiagnosticSeverity.Warning);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_REJECTED" && x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Theory]
        [InlineData(-1.0, "non-positive")]
        [InlineData(0.0, "non-positive")]
        [InlineData(double.NaN, "non-finite")]
        [InlineData(double.PositiveInfinity, "non-finite")]
        [InlineData(double.NegativeInfinity, "non-finite")]
        public void CheckSignedVolumes_InvalidSignedVolume_ReportsCell(double volume, string expected)
        {
            // Arrange
            List<OcctCell> cells = new List<OcctCell> { Cell(0, 1), Cell(1, 2, volume) };

            // Act
            List<string> failures = OcctUnderSplitGuard.CheckSignedVolumes(cells, "sew cell");

            // Assert
            string failure = Assert.Single(failures);
            Assert.Contains("sew cell 1", failure);
            Assert.Contains(expected, failure);
        }

        [Fact]
        public void CheckSignedVolumes_PositiveCells_HasNoFailures()
        {
            // Arrange
            List<OcctCell> cells = new List<OcctCell> { Cell(0, 1), Cell(1, 2) };

            // Act
            List<string> failures = OcctUnderSplitGuard.CheckSignedVolumes(cells, "sew cell");

            // Assert
            Assert.Empty(failures);
        }

        [Theory]
        [InlineData(-1.0, "negative signed volume")]
        [InlineData(double.NaN, "non-finite")]
        [InlineData(double.PositiveInfinity, "non-finite")]
        [InlineData(0.0, "no volume")]
        public void ValidateCells_InvalidSignedVolume_RejectsBeforeAbsoluteValue(double volume, string expected)
        {
            // Arrange: before the fix |-1| passed as a valid 1 m3 cell.
            List<OcctCell> cells = new List<OcctCell> { Cell(0, 1, volume) };

            // Act
            List<string> failures = OcctUnderSplitGuard.ValidateCells(cells, Options());

            // Assert
            Assert.Contains(failures, x => x.Contains(expected));
        }

        [Fact]
        public void CheckVolumeAgreement_EqualTotals_ReturnsNull()
        {
            // Arrange
            List<OcctCell> sew = new List<OcctCell> { Cell(0, 2) };
            List<OcctCell> direct = new List<OcctCell> { Cell(0, 1), Cell(1, 2) };

            // Act
            string conflict = OcctUnderSplitGuard.CheckVolumeAgreement(sew, direct, 11, Options());

            // Assert
            Assert.Null(conflict);
        }

        [Fact]
        public void CheckVolumeAgreement_DuplicatedEnvelope_ReportsBothTotals()
        {
            // Arrange: like the 431.985 m3 (= 2 x 215.993) Revit-home sew build.
            List<OcctCell> sew = new List<OcctCell> { Cell(0, 1), Cell(1, 2), Cell(0, 2) };
            List<OcctCell> direct = new List<OcctCell> { Cell(0, 1), Cell(1, 2) };

            // Act
            string conflict = OcctUnderSplitGuard.CheckVolumeAgreement(sew, direct, 11, Options());

            // Assert
            Assert.NotNull(conflict);
            Assert.Contains("4 m3", conflict);
            Assert.Contains("2 m3", conflict);
        }

        [Fact]
        public void CheckVolumeAgreement_DifferenceWithinTolerance_ReturnsNull()
        {
            // Arrange: 0.05 % difference, below the 0.1 % allowance.
            List<OcctCell> sew = new List<OcctCell> { Cell(0, 2, 2.001) };
            List<OcctCell> direct = new List<OcctCell> { Cell(0, 2) };

            // Act
            string conflict = OcctUnderSplitGuard.CheckVolumeAgreement(sew, direct, 0, Options());

            // Assert
            Assert.Null(conflict);
        }

        [Fact]
        public void CheckOverlap_DuplicatedEnvelope_ReportsOverlap()
        {
            // Arrange
            List<OcctCell> cells = new List<OcctCell> { Cell(0, 1), Cell(1, 2), Cell(0, 2) };

            // Act
            List<string> failures = OcctUnderSplitGuard.CheckOverlap(cells, null, Options(), "sew cells");

            // Assert
            Assert.Contains(failures, x => x.Contains("sew cells 0 and 2 overlap"));
            Assert.Contains(failures, x => x.Contains("sew cells 1 and 2 overlap"));
        }

        [Fact]
        public void CheckOverlap_DisjointCells_HasNoFailures()
        {
            // Arrange
            List<OcctCell> cells = new List<OcctCell> { Cell(0, 1), Cell(1, 2) };

            // Act
            List<string> failures = OcctUnderSplitGuard.CheckOverlap(cells, null, Options(), "sew cells");

            // Assert
            Assert.Empty(failures);
        }

        [Fact]
        public void CheckOverlap_PartialOverlapMissedByOwnPoints_ReportedWithoutProbes()
        {
            // Arrange: neither cell's own centre (x 1.0 and 2.5) lies in the other; they share 0.5 m3. Before the fix
            // only an outside probe (such as x 1.75) could find this, so it passed without one.
            List<OcctCell> cells = new List<OcctCell> { Cell(0, 2), Cell(1.5, 3.5) };

            // Act
            List<string> failures = OcctUnderSplitGuard.CheckOverlap(cells, null, Options(), "sew cells");

            // Assert
            Assert.Contains(failures, x => x.Contains("sew cells 0 and 1 overlap"));
        }

        // Unit-height L-shaped cell: x 0..2 by y 0..1 plus x 0..1 by y 1..2 (3 m3).
        private static OcctCell LCell()
        {
            Point3D[] footprint = { new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 1, 0), new Point3D(1, 1, 0), new Point3D(1, 2, 0), new Point3D(0, 2, 0) };
            List<Face3D> faces = new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(footprint),
                TestGeometry.CreatePlanarFace(footprint.Select(x => new Point3D(x.X, x.Y, 1)).ToArray()),
            };
            for (int i = 0; i < footprint.Length; i++)
            {
                Point3D a = footprint[i];
                Point3D b = footprint[(i + 1) % footprint.Length];
                faces.Add(TestGeometry.CreatePlanarFace(a, b, new Point3D(b.X, b.Y, 1), new Point3D(a.X, a.Y, 1)));
            }

            return new OcctCell(new Shell(faces), 3, center: new Point3D(0.5, 0.5, 0.5));
        }

        // Unit-height box cell over x0..x1 by y0..y1.
        private static OcctCell BoxCell(double x0, double x1, double y0, double y1)
        {
            Shell shell = new Shell(new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(x0, y0, 0), new Point3D(x1, y0, 0), new Point3D(x1, y1, 0), new Point3D(x0, y1, 0)),
                TestGeometry.CreatePlanarFace(new Point3D(x0, y0, 1), new Point3D(x1, y0, 1), new Point3D(x1, y1, 1), new Point3D(x0, y1, 1)),
                TestGeometry.CreatePlanarFace(new Point3D(x0, y0, 0), new Point3D(x1, y0, 0), new Point3D(x1, y0, 1), new Point3D(x0, y0, 1)),
                TestGeometry.CreatePlanarFace(new Point3D(x0, y1, 0), new Point3D(x1, y1, 0), new Point3D(x1, y1, 1), new Point3D(x0, y1, 1)),
                TestGeometry.CreatePlanarFace(new Point3D(x0, y0, 0), new Point3D(x0, y1, 0), new Point3D(x0, y1, 1), new Point3D(x0, y0, 1)),
                TestGeometry.CreatePlanarFace(new Point3D(x1, y0, 0), new Point3D(x1, y1, 0), new Point3D(x1, y1, 1), new Point3D(x1, y0, 1)),
            });
            return new OcctCell(shell, (x1 - x0) * (y1 - y0), center: new Point3D((x0 + x1) / 2, (y0 + y1) / 2, 0.5));
        }

        public static IEnumerable<object[]> OverlapControls()
        {
            // Partial overlap, own centres outside each other (0.5 m3).
            yield return new object[] { 0.0, 2.0, 1.5, 3.5 };
            // Duplicates.
            yield return new object[] { 0.0, 1.0, 0.0, 1.0 };
            // Containment, strictly inside.
            yield return new object[] { 0.0, 3.0, 1.0, 2.0 };
            // Containment sharing faces.
            yield return new object[] { 0.0, 2.0, 0.0, 1.0 };
            // Overlap thicker than the band only just (0.02 m against a 0.01 m band).
            yield return new object[] { 0.0, 1.0, 0.98, 2.0 };
        }

        [Theory]
        [MemberData(nameof(OverlapControls))]
        public void CheckDisjoint_OverlappingBoxes_ReportOverlapWithoutExactTest(double a0, double a1, double b0, double b1)
        {
            // Arrange
            List<OcctCell> cells = new List<OcctCell> { Cell(a0, a1), Cell(b0, b1) };

            // Act
            List<string> failures = OcctUnderSplitGuard.CheckDisjoint(cells, null, Options(), "cells", OcctUnderSplitGuard.NoExactTest);

            // Assert: proven by a sample point inside both, no exact test needed.
            Assert.Equal("cells 0 and 1 overlap", Assert.Single(failures));
        }

        [Theory]
        [InlineData(0.0, 1.0, 2.0, 3.0)]
        [InlineData(0.0, 1.0, 1.0, 2.0)]
        [InlineData(0.0, 1.0, 0.995, 2.0)]
        public void CheckDisjoint_DisjointOrFaceTouchingBoxes_PassWithoutExactTest(double a0, double a1, double b0, double b1)
        {
            // Arrange: apart; sharing a face; and overlapping by 0.005 m, within the 0.01 m glue band.
            List<OcctCell> cells = new List<OcctCell> { Cell(a0, a1), Cell(b0, b1) };
            int exactCalls = 0;

            // Act
            List<string> failures = OcctUnderSplitGuard.CheckDisjoint(cells, null, Options(), "cells", (first, second) => { exactCalls++; return null; });

            // Assert
            Assert.Empty(failures);
            Assert.Equal(0, exactCalls);
        }

        // Unit-height triangular prism over the footprint a, b, c (counter-clockwise), volume 0.5.
        private static OcctCell Wedge(Point3D a, Point3D b, Point3D c)
        {
            Point3D[] footprint = { a, b, c };
            List<Face3D> faces = new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(footprint),
                TestGeometry.CreatePlanarFace(footprint.Select(x => new Point3D(x.X, x.Y, 1)).ToArray()),
            };
            for (int i = 0; i < footprint.Length; i++)
            {
                Point3D p = footprint[i];
                Point3D q = footprint[(i + 1) % footprint.Length];
                faces.Add(TestGeometry.CreatePlanarFace(p, q, new Point3D(q.X, q.Y, 1), new Point3D(p.X, p.Y, 1)));
            }

            return new OcctCell(new Shell(faces), 0.5, center: new Point3D((a.X + b.X + c.X) / 3, (a.Y + b.Y + c.Y) / 3, 0.5));
        }

        [Fact]
        public void CheckDisjoint_WedgesSharingDiagonalFace_SeparatedByFacePlane()
        {
            // Arrange: the unit square split along x + y = 1. The bounding boxes coincide, and their centre lies on
            // the shared face, so only the face-plane test can show the cells disjoint.
            List<OcctCell> cells = new List<OcctCell>
            {
                Wedge(new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(0, 1, 0)),
                Wedge(new Point3D(1, 0, 0), new Point3D(1, 1, 0), new Point3D(0, 1, 0)),
            };
            int exactCalls = 0;

            // Act
            List<string> failures = OcctUnderSplitGuard.CheckDisjoint(cells, null, Options(), "cells", (first, second) => { exactCalls++; return null; });

            // Assert
            Assert.Empty(failures);
            Assert.Equal(0, exactCalls);
        }

        [Fact]
        public void CheckDisjoint_CellInNotchOfLShape_NeedsExactTest()
        {
            // Arrange: disjoint, face-touching on two faces, but no face plane separates them and no sample proves
            // an overlap.
            List<OcctCell> cells = new List<OcctCell> { LCell(), BoxCell(1, 2, 1, 2) };

            // Act
            List<string> withoutExact = OcctUnderSplitGuard.CheckDisjoint(cells, null, Options(), "cells", OcctUnderSplitGuard.NoExactTest);
            List<string> exactThrows = OcctUnderSplitGuard.CheckDisjoint(cells, null, Options(), "cells", (first, second) => throw new InvalidOperationException("kernel"));
            List<string> exactEmpty = OcctUnderSplitGuard.CheckDisjoint(cells, null, Options(), "cells", (first, second) => 0);

            // Assert: undecidable fails closed; the exact test decides.
            Assert.Contains("could not be established", Assert.Single(withoutExact));
            Assert.Contains("could not be established", Assert.Single(exactThrows));
            Assert.Empty(exactEmpty);
        }

        [Fact]
        public void CheckDisjoint_BoxIntrudingIntoLShape_OverlapFoundOnlyByExactTest()
        {
            // Arrange: box x,y 0.8..2 shares 0.44 m3 with the L, yet no sample point lies in both.
            List<OcctCell> cells = new List<OcctCell> { LCell(), BoxCell(0.8, 2, 0.8, 2) };

            // Act
            List<string> withoutExact = OcctUnderSplitGuard.CheckDisjoint(cells, null, Options(), "cells", OcctUnderSplitGuard.NoExactTest);
            List<string> withExact = OcctUnderSplitGuard.CheckDisjoint(cells, null, Options(), "cells", (first, second) => 0.44);

            // Assert
            Assert.Contains("could not be established", Assert.Single(withoutExact));
            Assert.Contains("cells 0 and 1 overlap (common volume 0.44 m3)", Assert.Single(withExact));
        }

        [Fact]
        public void ValidateCells_CellInNotchOfLShape_UsesExactTest()
        {
            // Arrange
            List<OcctCell> cells = new List<OcctCell> { LCell(), BoxCell(1, 2, 1, 2) };

            // Act
            List<string> withoutExact = OcctUnderSplitGuard.ValidateCells(cells, Options(), null, OcctUnderSplitGuard.NoExactTest);
            List<string> withExact = OcctUnderSplitGuard.ValidateCells(cells, Options(), null, (first, second) => 0);

            // Assert
            Assert.Contains(withoutExact, x => x.Contains("could not be established"));
            Assert.Empty(withExact);
        }

        [Fact]
        public void AdoptSewCandidate_DuplicateCellsInBothCandidates_FailsClosed()
        {
            // Arrange (R1): both builds hold the same two identical unit cubes. Coverage is clean and the totals
            // agree (2 m3 each), which used to skip the overlap check and return Success with only SEW_SUCCESS.
            List<Face3D> faces = Box(0, 1).Face3Ds.ToList();
            OcctCellComplexResult sew = Candidate(Cell(0, 1), Cell(0, 1));
            Assert.True(IsCleanSewCoverage(faces, sew));
            Assert.Null(OcctUnderSplitGuard.CheckVolumeAgreement(sew.Cells, new List<OcctCell> { Cell(0, 1), Cell(0, 1) }, 6, Options()));
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(faces, Options(), sew, Direct(0, Cell(0, 1), Cell(0, 1)), result);

            // Assert
            AssertFailedClosed(adopted, result);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_BUILD_INVALID" && x.Message.Contains("sew cells 0 and 1 overlap"));
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_REJECTED" && x.Message.Contains("cells 0 and 1 overlap"));
            Assert.DoesNotContain(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_SUCCESS");
        }

        [Fact]
        public void AdoptSewCandidate_DuplicateSewCellsWithValidDirect_AdoptsDirect()
        {
            // Arrange: the same duplicated sew build, recovered by an independently acceptable direct build.
            List<Face3D> faces = Box(0, 1).Face3Ds.ToList();
            OcctCellComplexResult sew = Candidate(Cell(0, 1), Cell(0, 1));
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(faces, Options(), sew, Direct(0, Cell(0, 1)), result);

            // Assert
            Assert.True(adopted);
            Assert.True(result.Success);
            Assert.Equal(1, Assert.Single(result.Cells).Volume, 6);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_BUILD_INVALID" && x.Message.Contains("overlap"));
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_FALLBACK_DIRECT");
        }

        // Faces of every given box cell (complete coverage for exactly those cells).
        private static List<Face3D> FacesOf(params OcctCell[] cells)
        {
            return cells.SelectMany(x => x.Shell.Face3Ds).ToList();
        }

        [Fact]
        public void EvaluateReplacement_PartialOverlapCentresOutside_Rejects()
        {
            // Arrange (R2): boxes x 0..2 and 1.5..3.5 overlap by 0.5 m3; their own faces give clean coverage.
            List<OcctCell> direct = new List<OcctCell> { Cell(0, 2), Cell(1.5, 3.5) };

            // Act
            OcctUnderSplitGuard.Evaluation evaluation = OcctUnderSplitGuard.EvaluateReplacement(FacesOf(direct.ToArray()), direct, Options(), 0.01);

            // Assert
            Assert.False(evaluation.Adopt);
            Assert.True(evaluation.DirectCoverage.IsClean);
            Assert.Contains(evaluation.Failures, x => x.Contains("cells 0 and 1 overlap"));
        }

        public static IEnumerable<object[]> OverlappingReplacements()
        {
            yield return new object[] { 0.0, 2.0, 1.5, 3.5 };
            yield return new object[] { 0.0, 1.0, 0.0, 1.0 };
            yield return new object[] { 0.0, 3.0, 1.0, 2.0 };
        }

        [Theory]
        [MemberData(nameof(OverlappingReplacements))]
        public void AdoptSewCandidate_InvalidSewOverlappingDirect_FailsClosed(double a0, double a1, double b0, double b1)
        {
            // Arrange (R2, end to end): an inverted sew envelope, and a direct build whose cells overlap (partial,
            // duplicate, containment) but cover every input face.
            OcctCell first = Cell(a0, a1);
            OcctCell second = Cell(b0, b1);
            List<Face3D> faces = FacesOf(first, second);
            double extent = System.Math.Max(a1, b1);
            OcctCellComplexResult sew = Candidate(Cell(0, extent, -extent));
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(faces, Options(), sew, Direct(0, first, second), result);

            // Assert
            AssertFailedClosed(adopted, result);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_REJECTED" && x.Message.Contains("cells 0 and 1 overlap"));
            Assert.DoesNotContain(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_FALLBACK_DIRECT");
        }

        [Theory]
        [InlineData(2.0)]
        [InlineData(1.0)]
        public void AdoptSewCandidate_InvalidSewDisjointOrTouchingDirect_AdoptsDirect(double secondStart)
        {
            // Arrange: control for the case above - apart, or sharing a face.
            OcctCell first = Cell(0, 1);
            OcctCell second = Cell(secondStart, secondStart + 1);
            OcctCellComplexResult sew = Candidate(Cell(0, secondStart + 1, -2));
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(FacesOf(first, second), Options(), sew, Direct(0, first, second), result);

            // Assert
            Assert.True(adopted);
            Assert.True(result.Success);
            Assert.Equal(2, result.Cells.Count);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_FALLBACK_DIRECT");
            Assert.DoesNotContain(result.Diagnostics, x => x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void AdoptSewCandidate_UnderSplitSewOverlappingDirect_KeepsSewWithUnreliable()
        {
            // Arrange: a valid sew cell (x 0..3.5) with the direct cells' end faces cutting through it, and a direct
            // build whose two cells cover every face but overlap by 0.5 m3 (total 4 against 3.5: a volume conflict).
            // Neither the #76 refinement route nor the conflict route may adopt it.
            OcctCell first = Cell(0, 2);
            OcctCell second = Cell(1.5, 3.5);
            OcctCellComplexResult sew = Candidate(Cell(0, 3.5));
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(FacesOf(first, second), Options(), sew, Direct(0, first, second), result);

            // Assert: the valid sew build is kept, flagged; the overlapping direct build is never adopted.
            Assert.True(adopted);
            OcctCell kept = Assert.Single(result.Cells);
            Assert.Equal(3.5, kept.Volume, 6);
            Assert.DoesNotContain(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_FALLBACK_DIRECT");
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_UNRELIABLE" && x.Message.Contains("cells 0 and 1 overlap"));
        }

        [Fact]
        public void CheckCoverageComplete_InteriorFace_ReportsIncomplete()
        {
            // Arrange: the partition cuts through a single cell.
            FaceCoverageReport coverage = SAM.Geometry.OCCT.Query.FaceCoverage(TwoRoomFaces(), new[] { Box(0, 2) }, 0.01, Tolerance, Fuzzy);

            // Act
            List<string> failures = OcctUnderSplitGuard.CheckCoverageComplete(coverage);

            // Assert
            Assert.Contains(failures, x => x.Contains("incomplete"));
            Assert.Contains(OcctUnderSplitGuard.CheckCoverageComplete(null), x => x.Contains("unavailable"));
        }

        [Fact]
        public void EvaluateReplacement_ValidCompleteDirect_AdoptsWithoutRefinementOrGain()
        {
            // Arrange: no sew reference at all; the candidate stands on its own.
            List<OcctCell> direct = new List<OcctCell> { Cell(0, 1), Cell(1, 2) };

            // Act
            OcctUnderSplitGuard.Evaluation evaluation = OcctUnderSplitGuard.EvaluateReplacement(TwoRoomFaces(), direct, Options(), 0.01);

            // Assert
            Assert.True(evaluation.Adopt, string.Join("; ", evaluation.Failures));
            Assert.True(evaluation.DirectCoverage.IsClean);
        }

        [Fact]
        public void EvaluateReplacement_DirectMissingPartition_Rejects()
        {
            // Arrange
            List<OcctCell> direct = new List<OcctCell> { Cell(0, 2) };

            // Act
            OcctUnderSplitGuard.Evaluation evaluation = OcctUnderSplitGuard.EvaluateReplacement(TwoRoomFaces(), direct, Options(), 0.01);

            // Assert
            Assert.False(evaluation.Adopt);
            Assert.Contains(evaluation.Failures, x => x.Contains("incomplete"));
        }

        [Fact]
        public void AdoptSewCandidate_InvertedSewCellWithCleanCoverage_AdoptsValidDirect()
        {
            // Arrange: the two rooms plus an inverted envelope cell (the -191.97 m3 shape). Its coverage is clean,
            // so the coverage early return alone kept it silently before.
            List<Face3D> faces = TwoRoomFaces();
            OcctCellComplexResult sew = Candidate(Cell(0, 1), Cell(1, 2), Cell(0, 2, -2));
            Assert.True(IsCleanSewCoverage(faces, sew));
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(faces, Options(), sew, Direct(0, Cell(0, 1), Cell(1, 2)), result);

            // Assert
            Assert.True(adopted);
            Assert.True(result.Success);
            Assert.Equal(2, result.Cells.Count);
            Assert.All(result.Cells, x => Assert.True(x.Volume > 0));
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_BUILD_INVALID" && x.Severity == OcctDiagnosticSeverity.Warning && x.Message.Contains("non-positive signed volume"));
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_FALLBACK_DIRECT" && x.Severity == OcctDiagnosticSeverity.Warning);
            Assert.DoesNotContain(result.Diagnostics, x => x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void AdoptSewCandidate_DuplicatedEnvelopeWithCleanCoverage_DetectsVolumeConflictAndAdoptsDirect()
        {
            // Arrange: all volumes positive and coverage clean; only the comparison with the direct build (twice the
            // volume) and the overlap check reveal the duplicated envelope cell.
            List<Face3D> faces = TwoRoomFaces();
            OcctCellComplexResult sew = Candidate(Cell(0, 1), Cell(1, 2), Cell(0, 2));
            Assert.True(IsCleanSewCoverage(faces, sew));
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(faces, Options(), sew, Direct(0, Cell(0, 1), Cell(1, 2)), result);

            // Assert
            Assert.True(adopted);
            Assert.Equal(2, result.Cells.Count);
            Assert.Equal(2, result.Cells.Sum(x => x.Volume), 6);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_BUILD_INVALID" && x.Message.Contains("overlap"));
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_FALLBACK_DIRECT");
        }

        [Fact]
        public void AdoptSewCandidate_InvalidSewWithEqualCoverage_AdoptsWithoutCoverageGain()
        {
            // Arrange: an inverted envelope covers every face, so the replacement cannot strictly improve coverage;
            // that criterion must not apply to an invalid reference.
            List<Face3D> faces = TwoRoomFaces();
            OcctCellComplexResult sew = Candidate(Cell(0, 1), Cell(1, 2), Cell(0, 2, -2));
            FaceCoverageReport sewCoverage = SAM.Geometry.OCCT.Query.FaceCoverage(faces, sew.Cells.Select(x => x.Shell), 0.01, Tolerance, Fuzzy);
            FaceCoverageReport directCoverage = SAM.Geometry.OCCT.Query.FaceCoverage(faces, new[] { Box(0, 1), Box(1, 2) }, 0.01, Tolerance, Fuzzy);
            Assert.Equal(sewCoverage.CoveredArea, directCoverage.CoveredArea, 6);
            Assert.NotEmpty(OcctUnderSplitGuard.CheckCoverageImprovement(sewCoverage, directCoverage, Fuzzy));
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(faces, Options(), sew, Direct(0, Cell(0, 1), Cell(1, 2)), result);

            // Assert
            Assert.True(adopted);
            Assert.Equal(2, result.Cells.Count);
        }

        [Fact]
        public void AdoptSewCandidate_InvalidSewDirectBuildFails_FailsClosed()
        {
            // Arrange
            OcctCellComplexResult sew = Candidate(Cell(0, 1), Cell(1, 2), Cell(0, 2, -2));
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(TwoRoomFaces(), Options(), sew, Direct(40), result);

            // Assert
            AssertFailedClosed(adopted, result);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_REJECTED" && x.Message.Contains("status 40"));
        }

        [Fact]
        public void AdoptSewCandidate_InvalidSewDirectBuildUnavailable_FailsClosed()
        {
            // Arrange
            OcctCellComplexResult sew = Candidate(Cell(0, 1), Cell(1, 2), Cell(0, 2, -2));
            OcctCellComplexResult result = new OcctCellComplexResult();
            OcctCellComplexBuilder.DirectBuild missing = (OcctCellComplexResult candidate, out bool decoded) => throw new DllNotFoundException("SAM.Occt.Native");

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(TwoRoomFaces(), Options(), sew, missing, result);

            // Assert
            AssertFailedClosed(adopted, result);
        }

        [Fact]
        public void AdoptSewCandidate_InvalidSewDirectCoverageIncomplete_FailsClosed()
        {
            // Arrange: the direct build is valid but leaves the partition cutting through its single cell.
            OcctCellComplexResult sew = Candidate(Cell(0, 1), Cell(1, 2), Cell(0, 2, -2));
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(TwoRoomFaces(), Options(), sew, Direct(0, Cell(0, 2)), result);

            // Assert
            AssertFailedClosed(adopted, result);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_REJECTED" && x.Message.Contains("incomplete"));
        }

        [Fact]
        public void AdoptSewCandidate_InvalidSewDirectHasInvertedCell_FailsClosed()
        {
            // Arrange: the alternative has the same defect; it must not be adopted either.
            OcctCellComplexResult sew = Candidate(Cell(0, 1), Cell(1, 2), Cell(0, 2, -2));
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(TwoRoomFaces(), Options(), sew, Direct(0, Cell(0, 1), Cell(1, 2, -1)), result);

            // Assert
            AssertFailedClosed(adopted, result);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_REJECTED" && x.Message.Contains("direct cell 1"));
        }

        [Fact]
        public void AdoptSewCandidate_InvalidSewDirectCellsOverlap_FailsClosed()
        {
            // Arrange
            OcctCellComplexResult sew = Candidate(Cell(0, 1), Cell(1, 2), Cell(0, 2, -2));
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(TwoRoomFaces(), Options(), sew, Direct(0, Cell(0, 1), Cell(1, 2), Cell(0.5, 1.5)), result);

            // Assert
            AssertFailedClosed(adopted, result);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_REJECTED" && x.Message.Contains("overlap"));
        }

        [Fact]
        public void AdoptSewCandidate_InvalidSewWithRetainTopology_FailsClosedAndReleasesTopology()
        {
            // Arrange: a valid direct build exists, but it cannot carry the retained topology handle.
            OcctCellComplexResult sew = Candidate(Cell(0, 1), Cell(1, 2), Cell(0, 2, -2));
            OcctTopology topology = FakeTopology();
            sew.Topology = topology;
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(TwoRoomFaces(), Options(true), sew, Direct(0, Cell(0, 1), Cell(1, 2)), result);

            // Assert
            AssertFailedClosed(adopted, result);
            Assert.Null(result.Topology);
            Assert.True(topology.IsClosed);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_REJECTED" && x.Message.Contains("RetainTopology"));
        }

        [Fact]
        public void AdoptSewCandidate_ValidSewWithRetainTopology_KeepsSewAndOwnsTopology()
        {
            // Arrange
            OcctCellComplexResult sew = Candidate(Cell(0, 1), Cell(1, 2));
            OcctTopology topology = FakeTopology();
            sew.Topology = topology;
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(TwoRoomFaces(), Options(true), sew, Direct(0, Cell(0, 1), Cell(1, 2)), result);

            // Assert
            Assert.True(adopted);
            Assert.True(result.Success);
            Assert.Same(topology, result.Topology);
            Assert.False(topology.IsClosed);
            Assert.Null(sew.Topology);
            Assert.DoesNotContain(result.Diagnostics, x => x.Severity != OcctDiagnosticSeverity.Info);
            result.Dispose();
            Assert.True(topology.IsClosed);
        }

        [Fact]
        public void AdoptSewCandidate_ValidSewAgreeingWithDirect_KeepsSewSilently()
        {
            // Arrange
            int directBuilds = 0;
            OcctCellComplexBuilder.DirectBuild direct = (OcctCellComplexResult candidate, out bool decoded) =>
            {
                directBuilds++;
                return Direct(0, Cell(0, 1), Cell(1, 2))(candidate, out decoded);
            };
            OcctCellComplexResult sew = Candidate(Cell(0, 1), Cell(1, 2));
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(TwoRoomFaces(), Options(), sew, direct, result);

            // Assert: the comparison build runs on every validated call, even with clean coverage.
            Assert.True(adopted);
            Assert.Equal(1, directBuilds);
            Assert.Equal(2, result.Cells.Count);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_SUCCESS");
            Assert.DoesNotContain(result.Diagnostics, x => x.Severity != OcctDiagnosticSeverity.Info);
        }

        [Fact]
        public void AdoptSewCandidate_ValidSewVolumeConflictOnly_KeepsSewWithConflictWarning()
        {
            // Arrange: a valid, fully covered sew build; the direct build lost a room (as on gappy input a direct
            // build can). The disagreement is not proof that the direct build is right.
            OcctCellComplexResult sew = Candidate(Cell(0, 1), Cell(1, 2));
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(TwoRoomFaces(), Options(), sew, Direct(0, Cell(0, 1)), result);

            // Assert
            Assert.True(adopted);
            Assert.True(result.Success);
            Assert.Equal(2, result.Cells.Count);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_VOLUME_CONFLICT" && x.Severity == OcctDiagnosticSeverity.Warning);
            Assert.DoesNotContain(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_BUILD_INVALID" || x.Code == "SAM_OCCT_BUILD_FALLBACK_DIRECT");
        }

        [Fact]
        public void AdoptSewCandidate_ValidUnderSplitSew_KeepsExistingRefinementRoute()
        {
            // Arrange: one positive envelope cell with the partition cutting through it (the #76 under-split).
            OcctCellComplexResult sew = Candidate(Cell(0, 2));
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(TwoRoomFaces(), Options(), sew, Direct(0, Cell(0, 1), Cell(1, 2)), result);

            // Assert
            Assert.True(adopted);
            Assert.Equal(2, result.Cells.Count);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_UNDER_SPLIT_SUSPECTED");
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_FALLBACK_DIRECT" && x.Message.Contains("refinement"));
            Assert.DoesNotContain(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_BUILD_INVALID");
        }

        [Fact]
        public void AdoptSewCandidate_SuspectSewWithVolumeConflict_AdoptsIndependentlyValidDirect()
        {
            // Arrange: a positive sew cell (x 0..1.5) that stops short of the outer wall: the partition cuts through it
            // and its volume (1.5) disagrees with the direct build (2). The refinement check fails (the second room's
            // internal point lies on the sew cell's boundary), but the sew cell is no under-split to refine.
            OcctCellComplexResult sew = Candidate(Cell(0, 1.5));
            OcctCellComplexResult result = new OcctCellComplexResult();
            Assert.NotEmpty(OcctUnderSplitGuard.CheckRefinement(sew.Cells, new List<OcctCell> { Cell(0, 1), Cell(1, 2) }, Options()));

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(TwoRoomFaces(), Options(), sew, Direct(0, Cell(0, 1), Cell(1, 2)), result);

            // Assert
            Assert.True(adopted);
            Assert.Equal(2, result.Cells.Count);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_FALLBACK_DIRECT" && x.Message.Contains("volume disagrees"));
            Assert.DoesNotContain(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_BUILD_INVALID" || x.Code == "SAM_OCCT_BUILD_UNRELIABLE");
        }

        [Fact]
        public void InternalPoints_RoofWedgeDefeatingCentreAndInternalPoint3D_UsesVerifiedVertexCentroid()
        {
            // Arrange: direct cell 5 of the current Revit-home panels with Air (a 0.053 m3 roof wedge), exact
            // coordinates. Neither its native centre nor Shell.InternalPoint3D gives an interior point, so before the
            // fallback the guard rejected a valid direct build as "cell 5 has no internal point".
            Shell wedge = new Shell(new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(8.13525801529758, 2.620280179563709, 0.4372656035465036), new Point3D(7.718648016953255, 2.6202801795637107, 0.43726560765449074), new Point3D(8.13525801529758, 2.620280179563708, 0.044999999999999984)),
                TestGeometry.CreatePlanarFace(new Point3D(7.718648016953255, 2.6202801795637103, 0.43726560765449085), new Point3D(8.13525801529758, 2.620280179563709, 0.4372656035465037), new Point3D(8.135257634074765, 4.574729613216517, 0.04500000000000004)),
                TestGeometry.CreatePlanarFace(new Point3D(8.135257634074765, 4.574729613216517, 0.04499999999999996), new Point3D(8.13525801529758, 2.6202801795637085, 0.43726560354650357), new Point3D(8.13525801529758, 2.6202801795637076, 0.04499999999999996)),
                TestGeometry.CreatePlanarFace(new Point3D(8.13525801529758, 2.620280179563706, 0.04500000000000026), new Point3D(7.718648016953255, 2.6202801795637103, 0.43726560765449096), new Point3D(8.135257634074765, 4.574729613216517, 0.045000000000000207)),
            });
            OcctCell cell = new OcctCell(wedge, 0.05323326510040709, center: new Point3D(7.926953016125418, 3.597504896390112, 0.24113280382724533));
            OcctBuildOptions options = new OcctBuildOptions { Tolerance = 1e-6, FuzzyTolerance = 1e-3 };
            Assert.False(wedge.Inside(cell.Center, options.FuzzyTolerance, options.Tolerance));
            Assert.Null(wedge.InternalPoint3D(options.FuzzyTolerance, options.Tolerance));

            // Act
            List<Point3D> points = OcctUnderSplitGuard.InternalPoints(new List<OcctCell> { cell }, options);

            // Assert
            Point3D point = Assert.Single(points);
            Assert.True(wedge.Inside(point, options.FuzzyTolerance, options.Tolerance));
            Assert.Empty(OcctUnderSplitGuard.ValidateCells(new List<OcctCell> { cell }, options));
        }

        [Fact]
        public void EvaluateReplacement_SeamBelowFuzzyTolerance_JudgesClosureAtGlueBand()
        {
            // Arrange: a unit box whose lid sits 5e-5 m above its walls, the size of the decoding seams seen on
            // direct AdjacencyCluster-home and Face3D-home cells (1e-6 to 1e-4 m, below the 1e-3 m fuzzy tolerance).
            const double seam = 5e-5;
            OcctBuildOptions options = new OcctBuildOptions { Tolerance = 1e-6, FuzzyTolerance = 1e-3 };
            List<Face3D> faces = Box(0, 1).Face3Ds.ToList();
            List<Face3D> cellFaces = faces.Take(1)
                .Concat(new[] { TestGeometry.CreatePlanarFace(new Point3D(0, 0, 1 + seam), new Point3D(1, 0, 1 + seam), new Point3D(1, 1, 1 + seam), new Point3D(0, 1, 1 + seam)) })
                .Concat(faces.Skip(2)).ToList();
            List<OcctCell> cells = new List<OcctCell> { new OcctCell(new Shell(cellFaces), 1, center: new Point3D(0.5, 0.5, 0.5)) };

            // Act
            List<string> strict = OcctUnderSplitGuard.ValidateCells(cells, options);
            OcctUnderSplitGuard.Evaluation replacement = OcctUnderSplitGuard.EvaluateReplacement(faces, cells, options, 0.01);

            // Assert: the under-split route keeps its strict closure test; the replacement route uses the glue band.
            Assert.Contains(strict, x => x.Contains("not closed"));
            Assert.True(replacement.Adopt, string.Join("; ", replacement.Failures));
        }

        [Fact]
        public void AdoptSewCandidate_ValidUnderSplitSewDirectNotRefinement_KeepsSewWithUnreliable()
        {
            // Arrange: the alternative covers only one room, so it does not partition the sew cell.
            OcctCellComplexResult sew = Candidate(Cell(0, 2));
            OcctCellComplexResult result = new OcctCellComplexResult();

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(TwoRoomFaces(), Options(), sew, Direct(0, Cell(0, 1)), result);

            // Assert: existing behaviour for a valid sew build suspected only of under-splitting.
            Assert.True(adopted);
            Assert.Single(result.Cells);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_BUILD_UNRELIABLE" && x.Severity == OcctDiagnosticSeverity.Warning);
            Assert.DoesNotContain(result.Diagnostics, x => x.Severity == OcctDiagnosticSeverity.Error);
        }

        [Fact]
        public void AdoptSewCandidate_ValidationOff_KeepsSewWithoutDirectBuild()
        {
            // Arrange: the opt-in is unchanged; legacy callers (the solver) keep the sew build as before.
            int directBuilds = 0;
            OcctCellComplexBuilder.DirectBuild direct = (OcctCellComplexResult candidate, out bool decoded) => { directBuilds++; decoded = false; return 40; };
            OcctCellComplexResult sew = Candidate(Cell(0, 1), Cell(1, 2), Cell(0, 2, -2));
            OcctCellComplexResult result = new OcctCellComplexResult();
            OcctBuildOptions options = Options();
            options.ValidateSewCoverage = false;

            // Act
            bool adopted = OcctCellComplexBuilder.AdoptSewCandidate(TwoRoomFaces(), options, sew, direct, result);

            // Assert
            Assert.True(adopted);
            Assert.Equal(0, directBuilds);
            Assert.Equal(3, result.Cells.Count);
            Assert.DoesNotContain(result.Diagnostics, x => x.Severity != OcctDiagnosticSeverity.Info);
        }
    }
}
