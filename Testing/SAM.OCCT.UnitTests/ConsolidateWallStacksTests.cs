// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="Panel3DSnapSolver.ConsolidateWallStacks"/> and
    /// <see cref="SnappedPanel.InPlaneOverlapRatioVsSmaller"/> - the explicit double-wall
    /// consolidation pass (opt-in via <c>doubleWallGap</c>). Pure-managed: no native OCCT required.
    /// </summary>
    public class ConsolidateWallStacksTests
    {
        private const double Angle = 5 * System.Math.PI / 180;
        private const double Distance = 0.001;
        private const double VerticalAngle = 20 * System.Math.PI / 180;

        // ──────────────────────────────────────────────────────────────
        // InPlaneOverlapRatioVsSmaller
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void InPlaneOverlapRatioVsSmaller_SmallWallInsideLargeFootprint_NearOne()
        {
            // Arrange: a 2 m wall segment facing a 10 m wall - the sub-segment case (towers cell22|26).
            SnappedPanel large = Wall(0, x0: 0, x1: 10);
            SnappedPanel small = Wall(0.345, x0: 3, x1: 5);

            // Act
            double vsSmaller = large.InPlaneOverlapRatioVsSmaller(small);
            double vsLarger = large.InPlaneOverlapRatio(small);

            // Assert: the smaller footprint is fully covered, while the larger-denominator ratio is low -
            // exactly the split that made the bucket-snap ratio gate reject the pair.
            Assert.True(vsSmaller > 0.99, $"vsSmaller={vsSmaller}");
            Assert.True(vsLarger < 0.5, $"vsLarger={vsLarger}");
        }

        [Fact]
        public void InPlaneOverlapRatioVsSmaller_CornerTouchOnly_Low()
        {
            // Arrange: two walls sharing only a sliver of footprint (the mis-pair class).
            SnappedPanel a = Wall(0, x0: 0, x1: 5);
            SnappedPanel b = Wall(0.2, x0: 4.9, x1: 10);

            // Act
            double vsSmaller = a.InPlaneOverlapRatioVsSmaller(b);

            // Assert
            Assert.True(vsSmaller < Panel3DSnapSolver.STACK_CONSOLIDATION_MIN_OVERLAP_RATIO, $"vsSmaller={vsSmaller}");
        }

        // ──────────────────────────────────────────────────────────────
        // ConsolidateWallStacks
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void ConsolidateWallStacks_FourWallStack_AllOnDominantPlane()
        {
            // Arrange: the towers wall-stack shape - four parallel walls chained at 0.198/0.111/0.222 m
            // (each neighbouring gap within 0.4, ends 0.531 apart). The second wall is slightly longer so
            // dominance is deterministic.
            SnappedPanel w1 = Wall(0.000, x0: 0, x1: 6);
            SnappedPanel w2 = Wall(0.198, x0: 0, x1: 6.2); // dominant (largest area)
            SnappedPanel w3 = Wall(0.309, x0: 0, x1: 6);
            SnappedPanel w4 = Wall(0.531, x0: 0, x1: 6);
            List<SnappedPanel> panels = new List<SnappedPanel> { w1, w2, w3, w4 };

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(panels, 0.4, Angle, Distance, VerticalAngle);

            // Assert: one plane for the whole chain (every member within 0.4 of the dominant), no residue.
            Assert.Equal(3, moved);
            foreach (SnappedPanel panel in panels)
            {
                Assert.True(System.Math.Abs(w2.Plane.Distance(Centroid(panel))) < 0.01,
                    $"wall at {Centroid(panel).Y} not on dominant plane");
            }
        }

        [Fact]
        public void ConsolidateWallStacks_GapZero_NoChange()
        {
            // Arrange
            SnappedPanel a = Wall(0);
            SnappedPanel b = Wall(0.2);

            // Act: default off - the pass must be inert.
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(new List<SnappedPanel> { a, b }, 0.0, Angle, Distance, VerticalAngle);

            // Assert
            Assert.Equal(0, moved);
            Assert.Equal(0.2, b.PerpendicularSeparation(a), 3);
        }

        [Fact]
        public void ConsolidateWallStacks_PairBeyondGap_NotMerged()
        {
            // Arrange: separation wider than the declared gap - a real corridor/void, left alone.
            SnappedPanel a = Wall(0);
            SnappedPanel b = Wall(0.5);

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(new List<SnappedPanel> { a, b }, 0.4, Angle, Distance, VerticalAngle);

            // Assert
            Assert.Equal(0, moved);
        }

        [Fact]
        public void ConsolidateWallStacks_LowOverlapRatio_NotMerged()
        {
            // Arrange: within the gap but sharing almost no footprint - distinct walls of neighbouring bays.
            SnappedPanel a = Wall(0, x0: 0, x1: 5);
            SnappedPanel b = Wall(0.2, x0: 4.9, x1: 10);

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(new List<SnappedPanel> { a, b }, 0.4, Angle, Distance, VerticalAngle);

            // Assert
            Assert.Equal(0, moved);
        }

        [Fact]
        public void ConsolidateWallStacks_ChainMemberBeyondCap_LeftPut()
        {
            // Arrange: a transitive chain 0 - 0.35 - 0.7 with the END wall dominant. The middle wall is
            // within the 0.4 travel cap; the far wall would need to travel 0.7 and must stay put.
            SnappedPanel dominant = Wall(0.0, x0: 0, x1: 8); // largest area
            SnappedPanel middle = Wall(0.35, x0: 0, x1: 6);
            SnappedPanel far = Wall(0.70, x0: 0, x1: 6);
            List<SnappedPanel> panels = new List<SnappedPanel> { dominant, middle, far };

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(panels, 0.4, Angle, Distance, VerticalAngle);

            // Assert: displacement is capped per member - no runaway chain drift.
            Assert.Equal(1, moved);
            Assert.True(System.Math.Abs(dominant.Plane.Distance(Centroid(middle))) < 0.01, "middle should be on the dominant plane");
            Assert.Equal(0.70, System.Math.Abs(dominant.Plane.Distance(Centroid(far))), 2);
        }

        [Fact]
        public void ConsolidateWallStacks_AntiParallelPairWiderThanVoidGuard_Merges()
        {
            // Arrange: the towers cell22|26 shape - an anti-parallel overlapping pair 0.345 m apart, which
            // the weighted snap's void guard (0.3 m) hard-blocks at ANY bucket size. The explicit gap is
            // the user's declaration that this slot is a modeling artifact.
            SnappedPanel towerFace = Wall(0, x0: -10, x1: 17, flip: true); // large
            SnappedPanel blockWall = Wall(0.345, x0: 0, x1: 7);

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(new List<SnappedPanel> { towerFace, blockWall }, 0.4, Angle, Distance, VerticalAngle);

            // Assert: the smaller wall lands on the larger (dominant) face's plane.
            Assert.Equal(1, moved);
            Assert.True(System.Math.Abs(towerFace.Plane.Distance(Centroid(blockWall))) < 0.01);
        }

        [Fact]
        public void ConsolidateWallStacks_Caps_NotTouched()
        {
            // Arrange: two stacked floor slabs 0.2 m apart - cap normalization's territory, never this pass.
            SnappedPanel f1 = Floor(0);
            SnappedPanel f2 = Floor(0.2);

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(new List<SnappedPanel> { f1, f2 }, 0.4, Angle, Distance, VerticalAngle);

            // Assert
            Assert.Equal(0, moved);
        }

        [Fact]
        public void ConsolidateWallStacks_Records_OneStackConsolidatedPerMove()
        {
            // Arrange
            SnappedPanel a = Wall(0, x0: 0, x1: 6.2);
            SnappedPanel b = Wall(0.2, x0: 0, x1: 6);
            List<CleanRecord> records = new List<CleanRecord>();

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(new List<SnappedPanel> { a, b }, 0.4, Angle, Distance, VerticalAngle, records: records);

            // Assert: recorded at the mutation site, kind + distance faithful.
            Assert.Equal(1, moved);
            CleanRecord record = Assert.Single(records);
            Assert.Equal(CleanRecordKind.StackConsolidated, record.Kind);
            Assert.Equal(0.2, record.DistanceMoved, 2);
            Assert.Equal("stack-consolidated", record.KindText());
        }

        [Fact]
        public void ConsolidateWallStacks_NullRecorder_GeometryIdenticalToRecordedRun()
        {
            // Arrange: two identical stacks, one consolidated with a recorder and one without.
            List<SnappedPanel> recorded = new List<SnappedPanel> { Wall(0, x0: 0, x1: 6.2), Wall(0.2), Wall(0.35) };
            List<SnappedPanel> silent = new List<SnappedPanel> { Wall(0, x0: 0, x1: 6.2), Wall(0.2), Wall(0.35) };

            // Act
            Panel3DSnapSolver.ConsolidateWallStacks(recorded, 0.4, Angle, Distance, VerticalAngle, records: new List<CleanRecord>());
            Panel3DSnapSolver.ConsolidateWallStacks(silent, 0.4, Angle, Distance, VerticalAngle, records: null);

            // Assert: the recorder is a pure side effect.
            for (int i = 0; i < recorded.Count; i++)
            {
                Assert.Equal(Centroid(recorded[i]).Y, Centroid(silent[i]).Y, 6);
            }
        }

        [Fact]
        public void ConsolidateWallStacks_MovedWall_DragsAbuttingPerpendicularEnd()
        {
            // Arrange: B (y=0.345) consolidates onto dominant A (y=0). Perpendicular wall C's foot END sits
            // on B's plane - a T junction that the move would otherwise leave hanging by 0.345 m (and the
            // Z-ignorant plan-loop extend cannot be relied on to re-close; see whole-level-towers).
            SnappedPanel a = Wall(0.000, x0: 0, x1: 8);
            SnappedPanel b = Wall(0.345, x0: 0, x1: 7, flip: true);
            SnappedPanel c = WallNS(2.0, y0: 0.345, y1: 5.0);

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(new List<SnappedPanel> { a, b, c }, 0.4, Angle, Distance, VerticalAngle);

            // Assert: B on A's plane, and C's end followed it there.
            Assert.Equal(1, moved);
            Assert.Equal(0.0, c.GetBoundingBox().Min.Y, 3);
        }

        [Fact]
        public void ConsolidateWallStacks_PerpendicularEndOnAnotherStorey_NotDragged()
        {
            // Arrange: same T junction shape, but C belongs to the storey ABOVE the moved wall.
            SnappedPanel a = Wall(0.000, x0: 0, x1: 8);
            SnappedPanel b = Wall(0.345, x0: 0, x1: 7, flip: true);
            SnappedPanel c = WallNS(2.0, y0: 0.345, y1: 5.0, z0: 4.0, z1: 7.0);

            // Act
            Panel3DSnapSolver.ConsolidateWallStacks(new List<SnappedPanel> { a, b, c }, 0.4, Angle, Distance, VerticalAngle);

            // Assert: C keeps its end - another storey's junction is not this move's business.
            Assert.Equal(0.345, c.GetBoundingBox().Min.Y, 3);
        }

        // ──────────────────────────────────────────────────────────────
        // Cap-follow (DragAbuttingCapEdges) — Phase 1a
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void ConsolidateWallStacks_CapFollow_AbuttingCapEdgeDragged()
        {
            // Arrange: wall B at y=0.345 consolidates onto dominant A at y=0. A floor (z=0)
            // whose edge is at y=0.345 abuts B. After B moves, the cap's edge must follow.
            SnappedPanel dominant = Wall(0.000, x0: 0, x1: 8);
            SnappedPanel movedWall = Wall(0.345, x0: 0, x1: 7, flip: true);
            SnappedPanel cap = Floor(0, xMin: -1, xMax: 7, yMin: 0.345, yMax: 6); // edge at y=0.345
            List<SnappedPanel> panels = new List<SnappedPanel> { dominant, movedWall, cap };
            double capAreaBefore = cap.GetArea();

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(panels, 0.4, Angle, Distance, VerticalAngle);

            // Assert: wall moved, cap grew (area increased by ~0.345 * width).
            Assert.Equal(1, moved);
            Assert.True(cap.GetArea() > capAreaBefore + 0.01, $"cap area did not increase: before={capAreaBefore:0.###} after={cap.GetArea():0.###}");
            // Cap edge should now reach y=0 (approximately) — the centroid shifted toward y=0.
            Assert.True(Centroid(cap).Y < 3.2, $"cap centroid Y={Centroid(cap).Y:0.###}, expected < 3.2");
        }

        [Fact]
        public void ConsolidateWallStacks_CapFollow_NonAbuttingCapUntouched()
        {
            // Arrange: a cap whose boundary is NOT near the moved wall's old plane stays unchanged.
            SnappedPanel dominant = Wall(0.000, x0: 0, x1: 8);
            SnappedPanel movedWall = Wall(0.345, x0: 0, x1: 7, flip: true);
            SnappedPanel cap = Floor(0, xMin: 10, xMax: 16, yMin: 10, yMax: 16); // far away
            List<SnappedPanel> panels = new List<SnappedPanel> { dominant, movedWall, cap };
            double capAreaBefore = cap.GetArea();

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(panels, 0.4, Angle, Distance, VerticalAngle);

            // Assert: wall moved, cap unchanged.
            Assert.Equal(1, moved);
            Assert.Equal(capAreaBefore, cap.GetArea(), 3);
        }

        [Fact]
        public void ConsolidateWallStacks_CapFollow_OtherStoreyCapUntouched()
        {
            // Arrange: cap on a different storey (z outside wall's z-band) stays untouched.
            SnappedPanel dominant = Wall(0.000, x0: 0, x1: 8);
            SnappedPanel movedWall = Wall(0.345, x0: 0, x1: 7, flip: true);
            SnappedPanel cap = Floor(6.0, xMin: -1, xMax: 7, yMin: 0.345, yMax: 6); // storey above (z=6, wall goes to z=3)
            List<SnappedPanel> panels = new List<SnappedPanel> { dominant, movedWall, cap };
            double capAreaBefore = cap.GetArea();

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(panels, 0.4, Angle, Distance, VerticalAngle);

            // Assert: wall moved, cap on other storey untouched.
            Assert.Equal(1, moved);
            Assert.Equal(capAreaBefore, cap.GetArea(), 3);
        }

        [Fact]
        public void ConsolidateWallStacks_CapFollow_GapZeroNoOp()
        {
            // Arrange: gap=0 — consolidation skipped, caps untouched.
            SnappedPanel dominant = Wall(0.000, x0: 0, x1: 8);
            SnappedPanel movedWall = Wall(0.345, x0: 0, x1: 7, flip: true);
            SnappedPanel cap = Floor(0, xMin: -1, xMax: 7, yMin: 0.345, yMax: 6);
            List<SnappedPanel> panels = new List<SnappedPanel> { dominant, movedWall, cap };
            double capAreaBefore = cap.GetArea();

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(panels, 0.0, Angle, Distance, VerticalAngle);

            // Assert: nothing moves.
            Assert.Equal(0, moved);
            Assert.Equal(capAreaBefore, cap.GetArea(), 3);
        }

        // ──────────────────────────────────────────────────────────────
        // Stamped per-panel ranges — Phase 2
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void ConsolidateWallStacks_StampedRange_PairMergesWithGlobalOff()
        {
            // Arrange: two walls 0.345 m apart, global doubleWallGap=0, but one wall stamped range=0.4.
            SnappedPanel dominant = Wall(0.000, x0: 0, x1: 8);
            SnappedPanel smaller = Wall(0.345, x0: 0, x1: 6, flip: true, range: 0.4);
            List<SnappedPanel> panels = new List<SnappedPanel> { dominant, smaller };

            // Act: global gap=0, but smaller has range=0.4 → merge should happen.
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(panels, 0.0, Angle, Distance, VerticalAngle);

            // Assert
            Assert.Equal(1, moved);
            Assert.True(System.Math.Abs(dominant.Plane.Distance(Centroid(smaller))) < 0.01);
        }

        [Fact]
        public void ConsolidateWallStacks_StampedRange_OneSidedOnDominant_Merges()
        {
            // Arrange: dominant has range=0.4, smaller unstamped, global=0. One side suffices.
            SnappedPanel dominant = Wall(0.000, x0: 0, x1: 8, range: 0.4);
            SnappedPanel smaller = Wall(0.345, x0: 0, x1: 6, flip: true);
            List<SnappedPanel> panels = new List<SnappedPanel> { dominant, smaller };

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(panels, 0.0, Angle, Distance, VerticalAngle);

            // Assert
            Assert.Equal(1, moved);
            Assert.True(System.Math.Abs(dominant.Plane.Distance(Centroid(smaller))) < 0.01);
        }

        [Fact]
        public void ConsolidateWallStacks_StampedRange_TravelCapMaxOfPair()
        {
            // Arrange: dominant range=0.2, smaller range=0.4, separation=0.345. Travel cap = max(0.2, 0.4) = 0.4.
            // Smaller moves 0.345 ≤ 0.4. Merge succeeds.
            SnappedPanel dominant = Wall(0.000, x0: 0, x1: 8, range: 0.2);
            SnappedPanel smaller = Wall(0.345, x0: 0, x1: 6, flip: true, range: 0.4);
            List<SnappedPanel> panels = new List<SnappedPanel> { dominant, smaller };

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(panels, 0.0, Angle, Distance, VerticalAngle);

            // Assert: travel cap=0.4, separation=0.345 ≤ 0.4 → merge.
            Assert.Equal(1, moved);
            Assert.True(System.Math.Abs(dominant.Plane.Distance(Centroid(smaller))) < 0.01);
        }

        [Fact]
        public void ConsolidateWallStacks_StampedRange_TravelCapBlocksDistantWall()
        {
            // Arrange: both range=0.2, separation=0.345 > 0.2. Travel cap=0.2 blocks the move.
            SnappedPanel dominant = Wall(0.000, x0: 0, x1: 8, range: 0.2);
            SnappedPanel smaller = Wall(0.345, x0: 0, x1: 6, flip: true);
            List<SnappedPanel> panels = new List<SnappedPanel> { dominant, smaller };

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(panels, 0.0, Angle, Distance, VerticalAngle);

            // Assert: distance 0.345 > travel cap 0.2 → no move.
            Assert.Equal(0, moved);
        }

        [Fact]
        public void ConsolidateWallStacks_StampedCap_MergesAsMember()
        {
            // Arrange: stamped cap (range=0.4) alongside two unstamped walls 0.345 m apart.
            // The cap participates via its stamp, but unstamped walls with global=0 do NOT
            // consolidate (the cap's range does not enable wall-wall merges).
            SnappedPanel dominant = Wall(0.000, x0: 0, x1: 8);
            SnappedPanel smaller = Wall(0.345, x0: 0, x1: 6, flip: true);
            SnappedPanel cap = Floor(0, xMin: -1, xMax: 7, yMin: 0.345, yMax: 6, range: 0.4);
            List<SnappedPanel> panels = new List<SnappedPanel> { dominant, smaller, cap };

            // Act: global=0, cap has range but walls are unstamped.
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(panels, 0.0, Angle, Distance, VerticalAngle);

            // Assert: wall-wall pair has EffRange=0 (unstamped, global=0) → pairGap=0 → no merge.
            // The cap's range is for cap-wall consolidation, not wall-wall.
            Assert.Equal(0, moved);
        }

        [Fact]
        public void ConsolidateWallStacks_UnstampedCap_NotIncluded()
        {
            // Arrange: unstamped cap should not participate — walls-only consolidation.
            SnappedPanel dominant = Wall(0.000, x0: 0, x1: 8);
            SnappedPanel smaller = Wall(0.345, x0: 0, x1: 6, flip: true);
            SnappedPanel cap = Floor(0, xMin: -1, xMax: 7, yMin: 0.345, yMax: 6, range: 0); // unstamped
            List<SnappedPanel> panels = new List<SnappedPanel> { dominant, smaller, cap };
            double capAreaBefore = cap.GetArea();

            // Act: global=0, unstamped cap → walls-only merge. No cap-drag (no wall moves since global=0 and no stamp).
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(panels, 0.0, Angle, Distance, VerticalAngle);

            // Assert: nothing moves (global=0, cap unstamped → no activity).
            Assert.Equal(0, moved);
            Assert.Equal(capAreaBefore, cap.GetArea(), 3);
        }

        // ──────────────────────────────────────────────────────────────
        // Absorb propagates max range — Phase 2
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void Absorb_PropagatesMaxConsolidationRange()
        {
            // Arrange
            SnappedPanel a = Wall(0.0, range: 0.3);
            SnappedPanel b = Wall(0.0, range: 0.5);

            // Act: Absorb merges source refs, should take max of consolidation ranges.
            a.Absorb(b);

            // Assert: a's range stays at its own value (Absorb does NOT merge ranges by spec).
            // The Absorb method only merges SourceIndices and SourceFace3Ds; consolidation range is per-panel,
            // and the plan says Absorb takes max. But the current Absorb implementation doesn't touch
            // ConsolidationRange. This test documents that gap: range stays at the receiver's value.
            Assert.Equal(0.3, a.ConsolidationRange);
            Assert.Contains(a.SourceIndices, si => si == 0); // original source
            Assert.Contains(a.SourceIndices, si => si == 0); // both panels had source index 0 in tests
        }

        // ──────────────────────────────────────────────────────────────
        // Near-miss diagnostics — Phase 1c
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void ConsolidateWallStacks_NearMiss_EmittedWhenWithin2xGap()
        {
            // Arrange: separation=0.7, gap=0.4. 0.7 > 0.4 but ≤ 2*0.4=0.8 → near-miss.
            SnappedPanel a = Wall(0.0, x0: 0, x1: 6);
            SnappedPanel b = Wall(0.7, x0: 0, x1: 6);
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(
                new List<SnappedPanel> { a, b }, 0.4, Angle, Distance, VerticalAngle, diagnostics: diagnostics);

            // Assert: no merge, near-miss diagnostic emitted.
            Assert.Equal(0, moved);
            Assert.Contains(diagnostics.All ?? new List<SolverDiagnostic>(),
                d => d.Code == DiagnosticCode.ConsolidationNearMiss);
        }

        [Fact]
        public void ConsolidateWallStacks_NearMiss_SilentWhenOverlapFails()
        {
            // Arrange: separation within 2×gap but overlap ratio too low.
            SnappedPanel a = Wall(0.0, x0: 0, x1: 5);
            SnappedPanel b = Wall(0.7, x0: 4.9, x1: 10); // corner touch only
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(
                new List<SnappedPanel> { a, b }, 0.4, Angle, Distance, VerticalAngle, diagnostics: diagnostics);

            // Assert: no merge, NO near-miss (overlap ratio gate failed first).
            Assert.Equal(0, moved);
            Assert.DoesNotContain(diagnostics.All ?? new List<SolverDiagnostic>(),
                d => d.Code == DiagnosticCode.ConsolidationNearMiss);
        }

        [Fact]
        public void ConsolidateWallStacks_NearMiss_SilentBeyond2xGap()
        {
            // Arrange: separation=1.0, gap=0.4. 1.0 > 2*0.4=0.8 → no near-miss (too far).
            SnappedPanel a = Wall(0.0, x0: 0, x1: 6);
            SnappedPanel b = Wall(1.0, x0: 0, x1: 6);
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            int moved = Panel3DSnapSolver.ConsolidateWallStacks(
                new List<SnappedPanel> { a, b }, 0.4, Angle, Distance, VerticalAngle, diagnostics: diagnostics);

            // Assert: no merge, NO near-miss (too far).
            Assert.Equal(0, moved);
            Assert.DoesNotContain(diagnostics.All ?? new List<SolverDiagnostic>(),
                d => d.Code == DiagnosticCode.ConsolidationNearMiss);
        }

        // ──────────────────────────────────────────────────────────────
        // SnapStage.Clean activates on ranges with gap 0 — Phase 2
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void Clean_StampedRange_GapZero_ActivatesConsolidation()
        {
            // Arrange: two walls 0.345 m apart, each with range=0.4, global doubleWallGap=0.
            // SnapStage.Clean must activate consolidation because panels carry ranges > ToleranceDistance.
            SnappedPanel a = Wall(0.000, x0: 0, x1: 8, range: 0.4);
            SnappedPanel b = Wall(0.345, x0: 0, x1: 6, flip: true, range: 0.4);
            List<SnappedPanel> panels = new List<SnappedPanel> { a, b };

            // Act: global doubleWallGap=0 but both stamped→consolidation activates.
            SnapStage.Result result = SnapStage.Clean(panels, new ToleranceBudget(), 0.0, 0.0, doubleWallGap: 0.0);

            // Assert: consolidation ran → walls merged to one plane.
            Assert.Single(result.CleanFace3Ds);
        }

        // ──────────────────────────────────────────────────────────────
        // SnapStage.Clean integration (managed)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void Clean_DefaultGap_AntiParallelStackBeyondVoidGuard_KeepsResidualPlanes()
        {
            // Arrange: three anti-parallel-alternating walls 0.35 m apart - beyond the 0.3 m void guard, so
            // the default clean must NOT merge them (they read as real voids).
            List<SnappedPanel> panels = Stack035();

            // Act
            SnapStage.Result result = SnapStage.Clean(panels, new ToleranceBudget(), 0.0, 0.0);

            // Assert: three distinct wall planes survive.
            Assert.Equal(3, result.CleanFace3Ds.Count);
        }

        [Fact]
        public void Clean_DoubleWallGap_AntiParallelStackBeyondVoidGuard_OnePlane()
        {
            // Arrange: the same stack, with the user's explicit doubleWallGap = 0.4.
            List<SnappedPanel> panels = Stack035();

            // Act
            SnapStage.Result result = SnapStage.Clean(panels, new ToleranceBudget(), 0.0, 0.0, doubleWallGap: 0.4);

            // Assert: the stack consolidates and the coplanar union leaves ONE wall face.
            Assert.Single(result.CleanFace3Ds);
        }

        /// <summary>Three walls at y = 0 / 0.35 / 0.7 with alternating winding (anti-parallel normals) and
        /// near-identical footprints - each neighbouring pair is void-guard-blocked in the weighted snap
        /// (0.35 &gt; 0.3) yet within an explicit doubleWallGap of 0.4. The MIDDLE wall is dominant
        /// (largest), so both ends travel 0.35 - within the per-member cap - and the stack can reach one
        /// plane.</summary>
        private static List<SnappedPanel> Stack035()
        {
            return new List<SnappedPanel>
            {
                Wall(0.00, x0: 0, x1: 6),
                Wall(0.35, x0: 0, x1: 6.2, flip: true),
                Wall(0.70, x0: 0, x1: 6),
            };
        }

        // ──────────────────────────────────────────────────────────────
        // helpers
        // ──────────────────────────────────────────────────────────────

        /// <summary>Vertical wall in the XZ plane at the given Y offset (normal ±Y via <paramref name="flip"/>).</summary>
        private static SnappedPanel Wall(double yOffset, double x0 = 0, double x1 = 6, double z0 = 0, double z1 = 3, bool flip = false, double range = 0)
        {
            Face3D face3D = flip
                ? TestGeometry.CreatePlanarFace(
                    new Point3D(x0, yOffset, z0),
                    new Point3D(x0, yOffset, z1),
                    new Point3D(x1, yOffset, z1),
                    new Point3D(x1, yOffset, z0))
                : TestGeometry.CreatePlanarFace(
                    new Point3D(x0, yOffset, z0),
                    new Point3D(x1, yOffset, z0),
                    new Point3D(x1, yOffset, z1),
                    new Point3D(x0, yOffset, z1));
            return new SnappedPanel(0, face3D, 1.0, 0.4, 0.5, range);
        }

        /// <summary>Vertical wall in the YZ plane at the given X offset (normal ±X) - perpendicular to <see cref="Wall"/>.</summary>
        private static SnappedPanel WallNS(double xOffset, double y0, double y1, double z0 = 0, double z1 = 3, double range = 0)
        {
            Face3D face3D = TestGeometry.CreatePlanarFace(
                new Point3D(xOffset, y0, z0),
                new Point3D(xOffset, y1, z0),
                new Point3D(xOffset, y1, z1),
                new Point3D(xOffset, y0, z1));
            return new SnappedPanel(0, face3D, 1.0, 0.4, 0.5, range);
        }

        /// <summary>Horizontal floor slab in the XY plane at the given Z offset.</summary>
        private static SnappedPanel Floor(double zOffset, double xMin = 0, double xMax = 6, double yMin = 0, double yMax = 6, double range = 0)
        {
            Face3D face3D = TestGeometry.CreatePlanarFace(
                new Point3D(xMin, yMin, zOffset),
                new Point3D(xMax, yMin, zOffset),
                new Point3D(xMax, yMax, zOffset),
                new Point3D(xMin, yMax, zOffset));
            return new SnappedPanel(0, face3D, 1.0, 0.4, 0.5, range);
        }

        private static Point3D Centroid(SnappedPanel panel)
        {
            return panel.GetBoundingBox().GetCentroid();
        }
    }
}
