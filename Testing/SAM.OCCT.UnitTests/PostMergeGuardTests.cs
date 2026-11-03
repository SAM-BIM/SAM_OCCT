// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using OcctDiagnosticSeverity = SAM.Core.OCCT.OcctDiagnosticSeverity;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="PostMergeGuard"/> - the pure/managed logic (cell matching, merge-group derivation,
    /// lineage selection, order-preserving revert and its hop) and the orchestration through an injected decoder. The
    /// native verification decode itself is exercised end to end by <c>PostMergeGuardIntegrationTests</c>.
    /// <para>
    /// The synthetic model is a row of three unit cubes A|B|C (x 0..3). Pre-merge faces: floors 0-2, roofs 3-5, the A|B wall
    /// as 6 (A's side) and 7 (B's side), the B|C wall as 8 (B's side) and 9 (C's side). The kernel's merge fused the floors of
    /// A and B (merged 0) and the roofs of A and B (merged 2); everything else is a single-input group.
    /// </para>
    /// </summary>
    public class PostMergeGuardTests
    {
        private static Face3D Quad(Point3D a, Point3D b, Point3D c, Point3D d)
        {
            return TestGeometry.CreatePlanarFace(a, b, c, d);
        }

        private static Face3D Floor(double x0, double x1) { return Quad(new Point3D(x0, 0, 0), new Point3D(x1, 0, 0), new Point3D(x1, 1, 0), new Point3D(x0, 1, 0)); }

        private static Face3D Roof(double x0, double x1) { return Quad(new Point3D(x0, 0, 1), new Point3D(x1, 0, 1), new Point3D(x1, 1, 1), new Point3D(x0, 1, 1)); }

        private static Face3D Wall(double x) { return Quad(new Point3D(x, 0, 0), new Point3D(x, 1, 0), new Point3D(x, 1, 1), new Point3D(x, 0, 1)); }

        private sealed class Row
        {
            public List<Face3D> Pre;
            public List<Face3D> Merged;
            public SourceMap Hop;
            public ResolvedCellComplex Cells;
        }

        private static Row BuildRow()
        {
            Row row = new Row();
            row.Pre = new List<Face3D>
            {
                Floor(0, 1), Floor(1, 2), Floor(2, 3),
                Roof(0, 1), Roof(1, 2), Roof(2, 3),
                Wall(1), Wall(1), Wall(2), Wall(2)
            };

            // Kernel merge: floors 0+1 -> merged 0, floor 2 -> 1, roofs 3+4 -> 2, roof 5 -> 3, walls 6..9 -> 4..7.
            row.Merged = new List<Face3D> { Floor(0, 2), row.Pre[2], Roof(0, 2), row.Pre[5], row.Pre[6], row.Pre[7], row.Pre[8], row.Pre[9] };
            row.Hop = new SourceMap();
            int[] output = { 0, 0, 1, 2, 2, 3, 4, 5, 6, 7 };
            for (int i = 0; i < output.Length; i++)
            {
                row.Hop.Record(i, new FaceKey(output[i]), Provenance.Resolved);
            }

            ResolvedCellFace face(int key, Face3D geometry, int[] owners, int[] flat) { return new ResolvedCellFace(geometry, key, owners, flat); }
            row.Cells = new ResolvedCellComplex(
                Guid.Empty,
                new[] { new ResolvedCell(0, 1, new Point3D(0.5, 0.5, 0.5)), new ResolvedCell(1, 1, new Point3D(1.5, 0.5, 0.5)), new ResolvedCell(2, 1, new Point3D(2.5, 0.5, 0.5)) },
                new[]
                {
                    face(1, row.Pre[0], new[] { 0 }, new[] { 0 }),
                    face(2, row.Pre[1], new[] { 1 }, new[] { 1 }),
                    face(3, row.Pre[2], new[] { 2 }, new[] { 2 }),
                    face(4, row.Pre[3], new[] { 0 }, new[] { 3 }),
                    face(5, row.Pre[4], new[] { 1 }, new[] { 4 }),
                    face(6, row.Pre[5], new[] { 2 }, new[] { 5 }),
                    face(7, row.Pre[6], new[] { 0, 1 }, new[] { 6, 7 }),
                    face(8, row.Pre[8], new[] { 1, 2 }, new[] { 8, 9 })
                },
                new[] { new ResolvedFaceAdjacency(7, 0, 1), new ResolvedFaceAdjacency(8, 1, 2) },
                null,
                0);
            return row;
        }

        private static CellSignature Sig(int index, double volume, double x0, double x1)
        {
            return new CellSignature(index, volume, new[] { x0, 0, 0 }, new[] { x1, 1, 1 });
        }

        private static List<CellSignature> AllCells(Row row) { return CellSignature.From(row.Cells); }

        // ---- Cell matching ----

        [Fact]
        public void FindLostCells_IdenticalCells_ReturnsNone()
        {
            // Arrange
            List<CellSignature> cells = new List<CellSignature> { Sig(0, 1, 0, 1), Sig(1, 1, 1, 2) };

            // Act
            List<CellSignature> lost = PostMergeGuard.FindLostCells(cells, cells);

            // Assert
            Assert.Empty(lost);
        }

        [Fact]
        public void FindLostCells_CellVanishes_ReportsIt()
        {
            // Arrange
            List<CellSignature> before = new List<CellSignature> { Sig(0, 1, 0, 1), Sig(1, 1, 1, 2) };
            List<CellSignature> after = new List<CellSignature> { Sig(0, 1, 0, 1) };

            // Act
            List<CellSignature> lost = PostMergeGuard.FindLostCells(before, after);

            // Assert
            Assert.Single(lost);
            Assert.Equal(1, lost[0].Index);
        }

        [Fact]
        public void FindLostCells_TwoCellsFuseIntoOne_ReportsBothParts()
        {
            // Arrange - the 283.20 m3 failure shape: two rooms with the wall between them removed.
            List<CellSignature> before = new List<CellSignature> { Sig(0, 1, 0, 1), Sig(1, 1, 1, 2) };
            List<CellSignature> after = new List<CellSignature> { Sig(0, 2, 0, 2) };

            // Act
            List<CellSignature> lost = PostMergeGuard.FindLostCells(before, after);

            // Assert
            Assert.Equal(2, lost.Count);
        }

        [Fact]
        public void FindLostCells_SliverBelowMinimumVolume_MayVanish()
        {
            // Arrange
            List<CellSignature> before = new List<CellSignature> { Sig(0, 1, 0, 1), Sig(1, 0.01, 1, 1.01) };
            List<CellSignature> after = new List<CellSignature> { Sig(0, 1, 0, 1) };

            // Act
            List<CellSignature> lost = PostMergeGuard.FindLostCells(before, after);

            // Assert
            Assert.Empty(lost);
        }

        [Fact]
        public void FindLostCells_VolumeWithinToleranceAndBoundsWithin_Matches()
        {
            // Arrange
            List<CellSignature> before = new List<CellSignature> { Sig(0, 100, 0, 1) };
            List<CellSignature> after = new List<CellSignature> { Sig(0, 100.005, 0.0005, 1.0005) };

            // Act & Assert
            Assert.Empty(PostMergeGuard.FindLostCells(before, after));
        }

        [Fact]
        public void FindLostCells_BoundsMovedBeyondTolerance_IsLost()
        {
            // Arrange - same volume, shifted 1 cm: a different cell.
            List<CellSignature> before = new List<CellSignature> { Sig(0, 1, 0, 1) };
            List<CellSignature> after = new List<CellSignature> { Sig(0, 1, 0.01, 1.01) };

            // Act & Assert
            Assert.Single(PostMergeGuard.FindLostCells(before, after));
        }

        [Fact]
        public void FindLostCells_OneCellAfterCannotMatchTwoBefore_CountsOnce()
        {
            // Arrange - two identical-looking initial cells but only one counterpart: one is lost.
            List<CellSignature> before = new List<CellSignature> { Sig(0, 1, 0, 1), Sig(1, 1, 0, 1) };
            List<CellSignature> after = new List<CellSignature> { Sig(0, 1, 0, 1) };

            // Act & Assert
            Assert.Single(PostMergeGuard.FindLostCells(before, after));
        }

        // ---- Merge groups and lineage ----

        [Fact]
        public void GroupsOf_MergedFaces_ListsSortedInputsPerOutput()
        {
            // Arrange
            Row row = BuildRow();

            // Act
            Dictionary<int, List<int>> groups = PostMergeGuard.GroupsOf(row.Hop);

            // Assert
            Assert.Equal(new[] { 0, 1 }, groups[0]);
            Assert.Equal(new[] { 3, 4 }, groups[2]);
            Assert.Equal(new[] { 2 }, groups[1]);
            Assert.Equal(8, groups.Count);
        }

        [Fact]
        public void GroupsOf_NullHop_IsEmpty()
        {
            Assert.Empty(PostMergeGuard.GroupsOf(null));
        }

        [Fact]
        public void TryBoundaryOrdinals_CellOwnership_ReturnsThatCellsOrdinals()
        {
            // Arrange
            Row row = BuildRow();

            // Act
            bool usable = PostMergeGuard.TryBoundaryOrdinals(row.Cells, new[] { 2 }, row.Pre.Count, out HashSet<int> ordinals);

            // Assert - cell C: floor 2, roof 5, and its side of the B|C wall (9).
            Assert.True(usable);
            Assert.Equal(new[] { 2, 5, 9 }, ordinals.OrderBy(x => x));
        }

        [Fact]
        public void TryBoundaryOrdinals_FlatOrdinalsDoNotCoverThePreMergeFaces_ClaimsNoLineage()
        {
            // Arrange - one more pre-merge face than the complex addresses (an invalid face was filtered).
            Row row = BuildRow();

            // Act
            bool usable = PostMergeGuard.TryBoundaryOrdinals(row.Cells, new[] { 2 }, row.Pre.Count + 1, out _);

            // Assert
            Assert.False(usable);
        }

        [Fact]
        public void AdjacentCells_SharedWalls_ReturnsTheNeighboursOnly()
        {
            // Arrange
            Row row = BuildRow();

            // Act & Assert
            Assert.Equal(new[] { 1 }, PostMergeGuard.AdjacentCells(row.Cells, new[] { 2 }));
            Assert.Equal(new[] { 0, 2 }, PostMergeGuard.AdjacentCells(row.Cells, new[] { 1 }));
        }

        [Fact]
        public void LineageGroups_OnlyMultiInputGroupsOnTheBoundary_AreSelected()
        {
            // Arrange
            Row row = BuildRow();
            Dictionary<int, List<int>> groups = PostMergeGuard.GroupsOf(row.Hop);

            // Act
            HashSet<int> forB = PostMergeGuard.LineageGroups(groups, new HashSet<int> { 1, 4, 7, 8 });
            HashSet<int> forC = PostMergeGuard.LineageGroups(groups, new HashSet<int> { 2, 5, 9 });

            // Assert - B's floor and roof sit in merged groups 0 and 2; C's faces are all single-input groups.
            Assert.Equal(new[] { 0, 2 }, forB.OrderBy(x => x));
            Assert.Empty(forC);
        }

        // ---- Revert ----

        [Fact]
        public void RevertGroups_OneGroup_RestoresItsFacesInPlaceAndKeepsTheRestInOrder()
        {
            // Arrange
            Row row = BuildRow();
            Dictionary<int, List<int>> groups = PostMergeGuard.GroupsOf(row.Hop);

            // Act
            List<Face3D> repaired = PostMergeGuard.RevertGroups(row.Pre, row.Merged, row.Hop, groups, PostMergeGuard.ForwardOf(row.Hop), new HashSet<int> { 0 }, out SourceMap hop);

            // Assert - floors 0 and 1 replace merged 0 at its position; the rest keeps its order.
            Assert.Equal(9, repaired.Count);
            Assert.Same(row.Pre[0], repaired[0]);
            Assert.Same(row.Pre[1], repaired[1]);
            Assert.Same(row.Merged[1], repaired[2]);
            Assert.Same(row.Merged[2], repaired[3]);
            Assert.Same(row.Merged[7], repaired[8]);
        }

        [Fact]
        public void RevertGroups_TwoGroups_EachIsInsertedAtItsOwnPosition()
        {
            // Arrange
            Row row = BuildRow();
            Dictionary<int, List<int>> groups = PostMergeGuard.GroupsOf(row.Hop);

            // Act
            List<Face3D> repaired = PostMergeGuard.RevertGroups(row.Pre, row.Merged, row.Hop, groups, PostMergeGuard.ForwardOf(row.Hop), new HashSet<int> { 0, 2 }, out _);

            // Assert - [floor0, floor1, floor2, roof0, roof1, roof2, walls...] i.e. the pre-merge order, not both groups at the first position.
            Assert.Equal(10, repaired.Count);
            for (int i = 0; i < repaired.Count; i++)
            {
                Assert.Same(row.Pre[i], repaired[i]);
            }
        }

        [Fact]
        public void RevertGroups_RepairedHop_MapsEveryCoveredPreMergeFaceExactlyOnceToItsFace()
        {
            // Arrange
            Row row = BuildRow();
            Dictionary<int, List<int>> groups = PostMergeGuard.GroupsOf(row.Hop);

            // Act - revert the roofs only: the floor group keeps its merged face.
            List<Face3D> repaired = PostMergeGuard.RevertGroups(row.Pre, row.Merged, row.Hop, groups, PostMergeGuard.ForwardOf(row.Hop), new HashSet<int> { 2 }, out SourceMap hop);

            // Assert
            for (int u = 0; u < row.Pre.Count; u++)
            {
                IReadOnlyList<FaceKey> images = hop.FacesOf(u);
                Assert.Single(images);
            }

            Assert.Same(row.Merged[0], repaired[hop.FacesOf(0)[0].Value]);
            Assert.Same(row.Merged[0], repaired[hop.FacesOf(1)[0].Value]);
            Assert.Same(row.Pre[3], repaired[hop.FacesOf(3)[0].Value]);
            Assert.Same(row.Pre[4], repaired[hop.FacesOf(4)[0].Value]);
            Assert.Same(row.Pre[9], repaired[hop.FacesOf(9)[0].Value]);
        }

        [Fact]
        public void RevertGroups_RestoredFaceAlsoFeedsAGroupThatStays_IsRefused()
        {
            // Arrange - pre-merge face 3 feeds merged 2 and merged 3: reverting only merged 2 would cover face 3's area twice.
            Row row = BuildRow();
            SourceMap hop = new SourceMap();
            int[][] images = { new[] { 0 }, new[] { 0 }, new[] { 1 }, new[] { 2, 3 }, new[] { 2 }, new[] { 3 }, new[] { 4 }, new[] { 5 }, new[] { 6 }, new[] { 7 } };
            for (int i = 0; i < images.Length; i++)
            {
                foreach (int image in images[i])
                {
                    hop.Record(i, new FaceKey(image), Provenance.Resolved);
                }
            }

            // Act
            List<Face3D> repaired = PostMergeGuard.RevertGroups(row.Pre, row.Merged, hop, PostMergeGuard.GroupsOf(hop), PostMergeGuard.ForwardOf(hop), new HashSet<int> { 2 }, out _);

            // Assert
            Assert.Null(repaired);
        }

        [Fact]
        public void RevertGroups_RestoredFaceFeedsTwoRevertedGroups_RestoresItOnceAtTheFirstPosition()
        {
            // Arrange
            Row row = BuildRow();
            row.Hop.Record(3, new FaceKey(3), Provenance.Resolved);

            // Act
            List<Face3D> repaired = PostMergeGuard.RevertGroups(row.Pre, row.Merged, row.Hop, PostMergeGuard.GroupsOf(row.Hop), PostMergeGuard.ForwardOf(row.Hop), new HashSet<int> { 2, 3 }, out SourceMap hop);

            // Assert
            Assert.Single(repaired, face => ReferenceEquals(face, row.Pre[3]));
            Assert.Equal(2, Assert.Single(hop.FacesOf(3)).Value);
            Assert.Equal(new[] { row.Merged[0], row.Merged[1] }.Concat(row.Pre.Skip(3)), repaired);
            for (int source = 3; source < row.Pre.Count; source++)
            {
                Assert.Same(row.Pre[source], repaired[Assert.Single(hop.FacesOf(source)).Value]);
            }
        }

        [Fact]
        public void IdentityHop_MapsEachOrdinalToItself()
        {
            // Act
            SourceMap hop = PostMergeGuard.IdentityHop(4);

            // Assert
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(i, hop.FacesOf(i).Single().Value);
            }
        }

        // ---- Orchestration through an injected decoder ----

        /// <summary>A decoder that returns every cell when the list holds the pre-merge floors 0,1 and roofs 3,4, else loses the given cell.</summary>
        private static Func<IList<Face3D>, List<CellSignature>> DecoderNeedingPreMergeFloorsAndRoofs(Row row, int cellLostOtherwise, List<int> calls)
        {
            return faces =>
            {
                calls?.Add(faces.Count);
                List<CellSignature> all = AllCells(row);
                bool restored = new[] { 0, 1, 3, 4 }.All(i => faces.Any(f => ReferenceEquals(f, row.Pre[i])));
                return restored ? all : all.Where(c => c.Index != cellLostOtherwise).ToList();
            };
        }

        [Fact]
        public void Evaluate_MergedFacesKeepEveryCell_IsAcceptedUnchanged()
        {
            // Arrange
            Row row = BuildRow();
            List<int> calls = new List<int>();

            // Act
            PostMergeGuard.Result result = PostMergeGuard.Evaluate(row.Pre, row.Merged, row.Hop, row.Cells, faces => { calls.Add(faces.Count); return AllCells(row); }, new SolverDiagnostics(), 0.05);

            // Assert
            Assert.Equal(PostMergeGuardOutcome.Accepted, result.Outcome);
            Assert.Equal(row.Merged, result.Faces);
            Assert.Null(result.RepairedHop);
            Assert.Single(calls);
            Assert.Empty(result.LostCells);
        }

        [Fact]
        public void Evaluate_NothingMerged_SkipsTheVerificationDecode()
        {
            // Arrange - an identity history: every group has one input.
            Row row = BuildRow();
            SourceMap identity = PostMergeGuard.IdentityHop(row.Pre.Count);
            int calls = 0;

            // Act
            PostMergeGuard.Result result = PostMergeGuard.Evaluate(row.Pre, row.Pre, identity, row.Cells, faces => { calls++; return AllCells(row); }, new SolverDiagnostics(), 0.05);

            // Assert
            Assert.Equal(PostMergeGuardOutcome.NothingMerged, result.Outcome);
            Assert.Equal(0, calls);
        }

        [Fact]
        public void Evaluate_DecoderCannotVerify_RestoresPreMergeFacesAndWarns()
        {
            // Arrange
            Row row = BuildRow();
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            PostMergeGuard.Result result = PostMergeGuard.Evaluate(row.Pre, row.Merged, row.Hop, row.Cells, faces => null, diagnostics, 0.05);

            // Assert
            Assert.Equal(PostMergeGuardOutcome.FellBack, result.Outcome);
            Assert.Equal(row.Pre, result.Faces);
            Assert.Equal(result.GroupCount, result.RevertedGroupCount);
            Assert.Equal(1, result.Decodes);
            Assert.Empty(result.LostCells);
            for (int source = 0; source < row.Pre.Count; source++)
            {
                Assert.Equal(source, Assert.Single(result.RepairedHop.FacesOf(source)).Value);
            }

            SolverDiagnostic warning = Assert.Single(diagnostics.OfCode(DiagnosticCode.RejectedMerge));
            Assert.Equal(OcctDiagnosticSeverity.Warning, warning.Severity);
            Assert.Contains("could not be verified", warning.Message);
        }

        [Fact]
        public void Evaluate_CellLostAndLineageGroupsRestoreIt_IsRepairedAndWarned()
        {
            // Arrange - cell B is lost by the merge; reverting the floor and roof groups on B's boundary restores it.
            Row row = BuildRow();
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            List<int> calls = new List<int>();

            // Act
            PostMergeGuard.Result result = PostMergeGuard.Evaluate(row.Pre, row.Merged, row.Hop, row.Cells, DecoderNeedingPreMergeFloorsAndRoofs(row, 1, calls), diagnostics, 0.05);

            // Assert
            Assert.Equal(PostMergeGuardOutcome.Repaired, result.Outcome);
            Assert.Equal(2, result.RevertedGroupCount);
            Assert.Equal(2, result.Decodes);
            Assert.Equal(row.Pre.Count, result.Faces.Count);
            Assert.NotNull(result.RepairedHop);
            SolverDiagnostic warning = Assert.Single(diagnostics.OfCode(DiagnosticCode.RejectedMerge));
            Assert.Equal(OcctDiagnosticSeverity.Warning, warning.Severity);
            Assert.Equal(SolverStage.Resolve, warning.Stage);
            Assert.Contains("1 cell(s)", warning.Message);
        }

        [Fact]
        public void Evaluate_LostCellHasNoMultiInputGroupOnItsBoundary_WidensToTheNeighbours()
        {
            // Arrange - cell C is lost but all of C's faces are single-input groups; its neighbour B owns the merged floor and roof.
            Row row = BuildRow();

            // Act
            PostMergeGuard.Result result = PostMergeGuard.Evaluate(row.Pre, row.Merged, row.Hop, row.Cells, DecoderNeedingPreMergeFloorsAndRoofs(row, 2, null), new SolverDiagnostics(), 0.05);

            // Assert
            Assert.Equal(PostMergeGuardOutcome.Widened, result.Outcome);
            Assert.Equal(2, result.RevertedGroupCount);
        }

        [Fact]
        public void Evaluate_NoPartialRepairKeepsTheCells_FallsBackToThePreMergeFaces()
        {
            // Arrange - the decoder never recovers the lost cell.
            Row row = BuildRow();

            // Act
            PostMergeGuard.Result result = PostMergeGuard.Evaluate(row.Pre, row.Merged, row.Hop, row.Cells, faces => AllCells(row).Where(c => c.Index != 1).ToList(), new SolverDiagnostics(), 0.05);

            // Assert
            Assert.Equal(PostMergeGuardOutcome.FellBack, result.Outcome);
            Assert.Equal(row.Pre, result.Faces);
            Assert.Equal(result.GroupCount, result.RevertedGroupCount);
            for (int i = 0; i < row.Pre.Count; i++)
            {
                Assert.Equal(i, result.RepairedHop.FacesOf(i).Single().Value);
            }
        }

        [Fact]
        public void Evaluate_CellLostWithoutAHistoryMap_FallsBackBecauseNoLineageIsClaimed()
        {
            // Arrange
            Row row = BuildRow();

            // Act
            PostMergeGuard.Result result = PostMergeGuard.Evaluate(row.Pre, row.Merged, null, row.Cells, faces => AllCells(row).Where(c => c.Index != 1).ToList(), new SolverDiagnostics(), 0.05);

            // Assert
            Assert.Equal(PostMergeGuardOutcome.FellBack, result.Outcome);
            Assert.Equal(1, result.Decodes);
        }

        [Fact]
        public void Evaluate_AcceptedMerge_AddsNoDiagnostic()
        {
            // Arrange
            Row row = BuildRow();
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            PostMergeGuard.Evaluate(row.Pre, row.Merged, row.Hop, row.Cells, faces => AllCells(row), diagnostics, 0.05);

            // Assert
            Assert.Empty(diagnostics.OfCode(DiagnosticCode.RejectedMerge));
        }

        [Fact]
        public void DiagnosticCode_RejectedMerge_IsAppendedAfterTheExistingMembers()
        {
            // Assert - the published enum values are never renumbered: the new member is the highest.
            int highest = Enum.GetValues(typeof(DiagnosticCode)).Cast<int>().Max();
            Assert.Equal((int)DiagnosticCode.RejectedMerge, highest);
            Assert.True((int)DiagnosticCode.RejectedMerge > (int)DiagnosticCode.CoplanarMergeRecovered);
        }
    }
}
