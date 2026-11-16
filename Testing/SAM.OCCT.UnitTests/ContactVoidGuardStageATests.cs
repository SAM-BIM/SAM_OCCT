// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static SAM.OCCT.UnitTests.ContactFixtures;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Stage A with the contact-aware void guard (design rev 5, section 5 anti-parallel guard tests): the opposed pass and the weighted
    /// snap on pairs standing at the D203 offsets. A released pair reaches the weighted midpoint; everything else stays as today.
    /// Also the lifetime of the record on a <see cref="SnappedPanel"/> (conditions (c) and (d)).
    /// </summary>
    public class ContactVoidGuardStageATests
    {
        private static double Y(SnappedPanel panel)
        {
            return panel.Plane.Origin.Y;
        }

        private static void AssertAtMidpoint(SnappedPanel a, SnappedPanel b)
        {
            Assert.Equal(YMid, Y(a), 6);
            Assert.Equal(YMid, Y(b), 6);
        }

        private static void AssertUntouched(SnappedPanel thin, SnappedPanel thick)
        {
            Assert.Equal(YThin, Y(thin), 9);
            Assert.Equal(YThick, Y(thick), 9);
        }

        // ---- control A ----------------------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void StageA_ContactingAntiParallelPair_ReachesTheWeightedMidpoint(bool thinFacesPlusY)
        {
            // Arrange: D203 after junction snapping - skins 0.324 m apart (> 0.3), both with records whose slabs touch.
            (SnappedPanel thin, SnappedPanel thick) = D203Pair(thinFacesPlusY);

            // Act
            RunStageA(new List<SnappedPanel> { thin, thick }, out SolverDiagnostics diagnostics, out List<CleanRecord> records);

            // Assert: both planes at the midpoint, one SnappedToBacker record of half the separation, and no "real void" rejection.
            AssertAtMidpoint(thin, thick);
            CleanRecord moved = Assert.Single(records, x => x.Kind == CleanRecordKind.SnappedToBacker);
            Assert.Equal(0.162, moved.DistanceMoved, 6);
            Assert.DoesNotContain(diagnostics.All, x => x.Code == DiagnosticCode.RejectedCollapse);
        }

        [Fact]
        public void StageA_D203With87PercentCoverage_ReachesTheWeightedMidpoint()
        {
            // Arrange: the real D203 overlap (87 % of the larger footprint) is below the 0.97 back-to-back-partition ratio and above the 0.5
            // co-parallel ratio the weighted snap needs.
            Face3D thin = Box(YThin, false, 0.39, Run);
            Face3D thick = Box(YThick, true, 0, Run);
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick));

            // Act
            RunStageA(new List<SnappedPanel> { a, b }, out SolverDiagnostics diagnostics, out List<CleanRecord> _);

            // Assert
            AssertAtMidpoint(a, b);
            Assert.DoesNotContain(diagnostics.All, x => x.Code == DiagnosticCode.RejectedCollapse);
        }

        [Fact]
        public void Clean_ContactingAntiParallelPair_ReachesTheWeightedMidpoint()
        {
            // Arrange
            (SnappedPanel thin, SnappedPanel thick) = D203Pair();
            List<SnappedPanel> panels = new List<SnappedPanel> { thin, thick };
            SolverDiagnostics diagnostics = new SolverDiagnostics();
            List<CleanRecord> records = new List<CleanRecord>();

            // Act: the whole of Stage A, not only its two snap passes.
            SnapStage.Clean(panels, new ToleranceBudget(), 0.3, 0.3, diagnostics, null, 0.0, records);

            // Assert
            AssertAtMidpoint(thin, thick);
            Assert.DoesNotContain(diagnostics.All, x => x.Code == DiagnosticCode.RejectedCollapse);
        }

        [Fact]
        public void StageA_AntiParallelPairWithoutRecords_StaysAndReportsARealVoid()
        {
            // Arrange: the same geometry with no evidence - today's behaviour, unchanged.
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thick = Box(YThick, true, 0, Run);
            SnappedPanel a = Skin(0, thin, null);
            SnappedPanel b = Skin(1, thick, null);

            // Act
            RunStageA(new List<SnappedPanel> { a, b }, out SolverDiagnostics diagnostics, out List<CleanRecord> records);

            // Assert
            AssertUntouched(a, b);
            Assert.Empty(records);
            Assert.Contains(diagnostics.All, x => x.Code == DiagnosticCode.RejectedCollapse);
        }

        // ---- same-facing: untouched by this change (a pre-existing asymmetry, pinned) -----------------------------------------------------

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void StageA_SameFacingD203Pair_ReachesTheWeightedMidpointAsToday(bool withRecords)
        {
            // Arrange: both skins face the same way. The void guard is for anti-parallel pairs only, so the weighted snap already moves
            // them to the midpoint, records or not - a regression pin of today's behaviour.
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thick = Box(YThick, false, 0, Run);
            SnappedPanel a = Skin(0, thin, withRecords ? Evidence(thin, GThin) : null);
            SnappedPanel b = Skin(1, thick, withRecords ? Evidence(thick, GThick) : null);

            // Act
            RunStageA(new List<SnappedPanel> { a, b }, out SolverDiagnostics diagnostics, out List<CleanRecord> records);

            // Assert
            AssertAtMidpoint(a, b);
            Assert.DoesNotContain(diagnostics.All, x => x.Code == DiagnosticCode.RejectedCollapse);
        }

        // ---- control B ---------------------------------------------------------------------------------------------------------------

        [Fact]
        public void StageA_ExtensionOnlyOverlap_StaysAndReportsARealVoid()
        {
            // Arrange: the source solids never overlapped in plan (0..1.0 and 1.2..2.2); junction snapping stretched both to 0..2.2.
            Face3D thin = Box(YThin, false, 0, 2.2);
            Face3D thick = Box(YThick, true, 0, 2.2);
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin, origin: new[] { Box(YThin, false, 0, 1.0) }));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick, origin: new[] { Box(YThick, true, 1.2, 2.2) }));

            // Act
            RunStageA(new List<SnappedPanel> { a, b }, out SolverDiagnostics diagnostics, out List<CleanRecord> records);

            // Assert
            AssertUntouched(a, b);
            Assert.Empty(records);
            Assert.Contains(diagnostics.All, x => x.Code == DiagnosticCode.RejectedCollapse);
        }

        [Fact]
        public void StageA_SameFacingExtensionOnlyOverlap_MovesAsToday()
        {
            // Arrange: pin of today's behaviour for the same-facing version of control B (a known orientation asymmetry, not fixed here).
            Face3D thin = Box(YThin, false, 0, 2.2);
            Face3D thick = Box(YThick, false, 0, 2.2);
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin, origin: new[] { Box(YThin, false, 0, 1.0) }));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick, origin: new[] { Box(YThick, false, 1.2, 2.2) }));

            // Act
            RunStageA(new List<SnappedPanel> { a, b }, out SolverDiagnostics _, out List<CleanRecord> _);

            // Assert
            AssertAtMidpoint(a, b);
        }

        // ---- control C ---------------------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData(0.0, 1.2)]
        [InlineData(0.9, 2.1)]
        [InlineData(2.7, 3.0)]
        public void StageA_PartialContactVoid_StaysAndReportsARealVoid(double voidFrom, double voidTo)
        {
            // Arrange: the thick skin's certified material misses part of the overlap (60/40, 40 % in the middle, 90/10).
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thick = Box(YThick, true, 0, Run);
            List<Face3D> support = new List<Face3D>();
            if (voidFrom > 0) { support.Add(Box(YThick, true, 0, voidFrom)); }
            if (voidTo < Run) { support.Add(Box(YThick, true, voidTo, Run)); }
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick, support: support));

            // Act
            RunStageA(new List<SnappedPanel> { a, b }, out SolverDiagnostics diagnostics, out List<CleanRecord> records);

            // Assert
            AssertUntouched(a, b);
            Assert.Empty(records);
            Assert.Contains(diagnostics.All, x => x.Code == DiagnosticCode.RejectedCollapse);
        }

        [Fact]
        public void StageA_SameFacingPartialContactVoid_MovesAsToday()
        {
            // Arrange: pin of today's behaviour for the same-facing version of control C (60/40 with the void behind part of the wall).
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thick = Box(YThick, false, 0, Run);
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick, support: new[] { Box(YThick, false, 1.2, Run) }));

            // Act
            RunStageA(new List<SnappedPanel> { a, b }, out SolverDiagnostics _, out List<CleanRecord> _);

            // Assert
            AssertAtMidpoint(a, b);
        }

        // ---- controls T1, T2 ---------------------------------------------------------------------------------------------------------------

        [Fact]
        public void StageA_ContactTrimmedAway_StaysAfterTheExistingEligibility()
        {
            // Arrange (T1): the thin skin is an L whose bounding box still equals the thick skin's, so the existing eligibility passes
            // (it measures boxes); only the clipped true region (4 of 9 m2) refuses.
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thinL = Wall(YThin, false, (0, 0), (3, 0), (3, 1), (0.5, 1), (0.5, 3), (0, 3));
            Face3D thick = Box(YThick, true, 0, Run);
            SnappedPanel a = Skin(0, thinL, Evidence(thin, GThin));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick));

            // Act
            RunStageA(new List<SnappedPanel> { a, b }, out SolverDiagnostics diagnostics, out List<CleanRecord> records);

            // Assert
            AssertUntouched(a, b);
            Assert.Empty(records);
            Assert.Contains(diagnostics.All, x => x.Code == DiagnosticCode.RejectedCollapse);
        }

        [Fact]
        public void StageA_VoidTrimmedAwayFromBothFaces_ReachesTheWeightedMidpoint()
        {
            // Arrange (T2): the void (x 2..3 of the thick skin) is no longer inside either current face.
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thick = Box(YThick, true, 0, Run);
            SnappedPanel a = Skin(0, Box(YThin, false, 0, 1.8), Evidence(thin, GThin));
            SnappedPanel b = Skin(1, Box(YThick, true, 0, 1.8), Evidence(thick, GThick, support: new[] { Box(YThick, true, 0, 2.0) }));

            // Act
            RunStageA(new List<SnappedPanel> { a, b }, out SolverDiagnostics diagnostics, out List<CleanRecord> _);

            // Assert
            AssertAtMidpoint(a, b);
            Assert.DoesNotContain(diagnostics.All, x => x.Code == DiagnosticCode.RejectedCollapse);
        }

        // ---- control W ---------------------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData(0.05, false)]
        [InlineData(-0.05, false)]
        [InlineData(5e-7, true)]
        public void StageA_Wedge_ReleasedOnlyWithinTolerance(double tilt, bool released)
        {
            // Arrange: the thick skin tilted over its 3 m run; its centroid offset is the contact offset, its ends are `tilt` away.
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thick = TiltedBox(YThick, tilt, true, 0, Run);
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick));

            // Act
            RunStageA(new List<SnappedPanel> { a, b }, out SolverDiagnostics diagnostics, out List<CleanRecord> records);

            // Assert
            if (released)
            {
                Assert.Single(records, x => x.Kind == CleanRecordKind.SnappedToBacker);
                Assert.DoesNotContain(diagnostics.All, x => x.Code == DiagnosticCode.RejectedCollapse);
            }
            else
            {
                Assert.Empty(records);
                Assert.Contains(diagnostics.All, x => x.Code == DiagnosticCode.RejectedCollapse);
                Assert.Equal(YThin, Y(a), 9);
            }
        }

        // ---- existing blocks stay ---------------------------------------------------------------------------------------------------------------

        [Fact]
        public void StageA_PartialOverlapBelowTheEligibility_StaysEvenWithContactRecords()
        {
            // Arrange (#250/#247): the skins share 0.42 m of a 3 m run (about 14 % of the larger box), well under the 0.5 eligibility ratio.
            // Their records are valid and the slabs touch where they overlap; the release is for eligible pairs only.
            Face3D thin = Box(YThin, false, 2.58, Run);
            Face3D thick = Box(YThick, true, 0, Run);
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin));
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick));

            // Act
            RunStageA(new List<SnappedPanel> { a, b }, out SolverDiagnostics _, out List<CleanRecord> records);

            // Assert
            AssertUntouched(a, b);
            Assert.Empty(records);
        }

        [Theory]
        [InlineData(0.2, false)]
        [InlineData(0.4, true)]
        public void StageA_PairEligibleOnlyByAbutting_IsNeverReleased(double bucketSize, bool released)
        {
            // Arrange: the skins are 0.324 m apart with touching records. With a 0.2 m bucket the thick skin is outside the thin skin's capture
            // slab, so the overlap branch is off and only the `abut` branch (alignColinearOffset 0.5) reaches the void guard: the release is for
            // overlap-eligible pairs only, so it stays. With a 0.4 m bucket the same pair is overlap-eligible and is released.
            Face3D thin = Box(YThin, false, 0, Run);
            Face3D thick = Box(YThick, true, 0, Run);
            SnappedPanel a = Skin(0, thin, Evidence(thin, GThin), bucketSize);
            SnappedPanel b = Skin(1, thick, Evidence(thick, GThick), bucketSize);
            ToleranceBudget tolerances = new ToleranceBudget();
            List<CleanRecord> records = new List<CleanRecord>();

            // Act
            Panel3DSnapSolver.SnapOpposedPartitions(new List<SnappedPanel> { a, b }, tolerances.Angle, tolerances.Distance, null, records);
            SnapStage.SnapToFixedPoint(new List<SnappedPanel> { a, b }, tolerances, alignColinearOffset: 0.5, null, SnapStage.MaxSnapIterations, records);

            // Assert
            if (released)
            {
                AssertAtMidpoint(a, b);
            }
            else
            {
                AssertUntouched(a, b);
                Assert.Empty(records);
            }
        }

        // ---- the record on a snapped panel: conditions (c) and (d) ----------------------------------------------------------------------------------------

        private static SnappedPanel RecordedWall()
        {
            Face3D face3D = Box(YThin, false, 0, Run);
            return Skin(0, face3D, Evidence(face3D, GThin));
        }

        [Fact]
        public void LiveContactEvidence_UntouchedPanel_IsTheRecord()
        {
            Assert.NotNull(RecordedWall().LiveContactEvidence);
        }

        [Theory]
        [InlineData(0.5e-6, true)]
        [InlineData(2e-6, false)]
        public void LiveContactEvidence_AfterStageAPlaneMove_DependsOnTheToleranceDistance(double move, bool stillLive)
        {
            // Arrange
            SnappedPanel panel = RecordedWall();
            Plane moved = (Plane)panel.Plane.GetMoved(panel.Plane.Normal.Unit * move);

            // Act
            bool snapped = panel.SnapToBacker(moved);

            // Assert
            Assert.True(snapped);
            Assert.Equal(stillLive, panel.LiveContactEvidence != null);
        }

        [Fact]
        public void LiveContactEvidence_AfterAbsorb_IsNone()
        {
            // Arrange
            SnappedPanel panel = RecordedWall();
            SnappedPanel other = Skin(1, Box(YThin, false, 3, 4), null);

            // Act
            panel.Absorb(other);

            // Assert
            Assert.Null(panel.LiveContactEvidence);
        }

        [Fact]
        public void LiveContactEvidence_AfterGrowOutward_IsNone()
        {
            // Arrange
            SnappedPanel panel = RecordedWall();

            // Act
            bool grown = panel.GrowOutward(0.2, 1e-6);

            // Assert
            Assert.True(grown);
            Assert.Null(panel.LiveContactEvidence);
        }

        [Fact]
        public void LiveContactEvidence_AfterAnExtend_IsNone()
        {
            // Arrange
            SnappedPanel panel = RecordedWall();

            // Act
            bool extended = panel.ExtendTopTo(Height + 0.5, 1e-6);

            // Assert
            Assert.True(extended);
            Assert.Null(panel.LiveContactEvidence);
        }

        [Fact]
        public void LiveContactEvidence_AfterStrippingOpenings_StillTheRecord()
        {
            // Arrange: Stage A step 1 drops inner loops only; the outer boundary and plane are those of the record.
            Face3D withOpening = BoxWithOpening(YThin, false, 0, Run, 1.0, 1.5, 0.5, 1.5);
            SnappedPanel panel = Skin(0, withOpening, Evidence(Box(YThin, false, 0, Run), GThin));

            // Act
            bool stripped = panel.StripInternalEdges();

            // Assert
            Assert.True(stripped);
            Assert.NotNull(panel.LiveContactEvidence);
        }

        [Fact]
        public void LiveContactEvidence_NoRecordGiven_IsNone()
        {
            Assert.Null(Skin(0, Box(YThin, false, 0, Run), null).LiveContactEvidence);
        }
    }
}
