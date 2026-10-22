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
        public void CheckOverlap_PartialOverlapMissedByOwnPoints_FoundByProbe()
        {
            // Arrange: neither cell's own centre (x 1.0 and 2.5) lies in the other; a probe at x 1.75 lies in both.
            List<OcctCell> cells = new List<OcctCell> { Cell(0, 2), Cell(1.5, 3.5) };
            List<Point3D> probes = new List<Point3D> { new Point3D(1.75, 0.5, 0.5) };

            // Act
            List<string> withoutProbes = OcctUnderSplitGuard.CheckOverlap(cells, null, Options(), "sew cells");
            List<string> withProbes = OcctUnderSplitGuard.CheckOverlap(cells, probes, Options(), "sew cells");

            // Assert
            Assert.Empty(withoutProbes);
            Assert.Contains(withProbes, x => x.Contains("sew cells 0 and 1 overlap"));
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
