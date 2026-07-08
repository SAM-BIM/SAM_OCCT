// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Pure-managed unit tests for the Phase 5e diagnosis-driven closure primitives
    /// (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §E/§I/§J): <see cref="AutoTune3DOptions"/> defaults,
    /// the <see cref="AutoTune3DSolver.NextLadderValue"/> / <see cref="AutoTune3DSolver.EscalateCulprits"/>
    /// escalation arithmetic, the <see cref="AutoTune3DSolver.IsAcceptableRound"/> acceptance truth table, and
    /// the <see cref="AutoTune3DSolver.CellIncreaseAdjacent"/> conservative new-cell adjacency proof. The full
    /// native re-solve loop (GappyFlat / ParallelPairWeld / ResidualDiagnostics) is exercised in the
    /// native-gated integration suite; these test the state machine and gates without any OCCT DLL.
    /// </summary>
    public class AutoTune3DTests
    {
        private static readonly List<double> DefaultLadder = new List<double> { 0.5, 0.75, 1.0, 1.5 };

        /// <summary>A closure signature with <paramref name="cells"/> equal-volume cells summing to
        /// <paramref name="totalVolume"/>, <paramref name="naked"/> naked edges and <paramref name="sliver"/> slivers.</summary>
        private static ClosureSignature3D Sig(int naked, int cells, double totalVolume, int sliver = 0)
        {
            List<double> volumes = new List<double>();
            for (int i = 0; i < cells; i++)
            {
                volumes.Add(totalVolume / cells);
            }

            return new ClosureSignature3D(cells, volumes, naked, faceCount: 10, droppedCount: 0, sliverCellCount: sliver);
        }

        /// <summary>A closed rectangular wire on z = <paramref name="z"/> spanning [x0,x1] x [y0,y1] - a stand-in
        /// closed naked loop whose bounding box lies on the boundary of the cells it closed.</summary>
        private static OcctNakedWire RectWire(double x0, double y0, double x1, double y1, double z)
        {
            List<Point3D> points = new List<Point3D>
            {
                new Point3D(x0, y0, z), new Point3D(x1, y0, z), new Point3D(x1, y1, z), new Point3D(x0, y1, z)
            };
            return new OcctNakedWire(points, isClosed: true, edgeOwnerFaceIndices: new[] { -1, -1, -1, -1 });
        }

        /// <summary>An axis-aligned bounding box centred at (cx,cy,cz) with the given half-extent - a stand-in cell box.</summary>
        private static BoundingBox3D Box(double cx, double cy, double cz, double half)
        {
            return new BoundingBox3D(new List<Point3D>
            {
                new Point3D(cx - half, cy - half, cz - half), new Point3D(cx + half, cy + half, cz + half)
            });
        }

        // ── 1. AutoTune3DOptions defaults ─────────────────────────────────────────────────────────────

        [Fact]
        public void AutoTune3DOptions_Defaults_MatchTheDesignReview()
        {
            // Arrange & Act
            AutoTune3DOptions options = new AutoTune3DOptions();

            // Assert - the §L defaults, verbatim.
            Assert.Equal(3, options.MaxRounds);
            Assert.Equal(new[] { 0.5, 0.75, 1.0, 1.5 }, options.MaxExtendLadder.ToArray());
            Assert.False(options.EscalateBucket);
            Assert.Equal(1.25, options.BucketFactor);
            Assert.True(options.PreferTrimOverExtend);
            Assert.Equal(0.5, options.SewSafetyFactor);
            Assert.True(options.ConsolidateRebuild);
        }

        [Fact]
        public void AutoTune3DOptions_CopyConstructor_DeepCopiesTheLadder()
        {
            // Arrange
            AutoTune3DOptions source = new AutoTune3DOptions { MaxRounds = 2 };
            source.MaxExtendLadder.Add(2.0);

            // Act
            AutoTune3DOptions copy = new AutoTune3DOptions(source);
            copy.MaxExtendLadder.Add(3.0);

            // Assert - the copy carries the values but its ladder is an independent list.
            Assert.Equal(2, copy.MaxRounds);
            Assert.Contains(2.0, copy.MaxExtendLadder);
            Assert.DoesNotContain(3.0, source.MaxExtendLadder);
        }

        // ── 2. MaxExtend ladder progression ───────────────────────────────────────────────────────────

        [Theory]
        [InlineData(0.4, 0.5)]   // default baseline reach -> first rung
        [InlineData(0.5, 0.75)]  // sitting exactly on a rung -> the next one (not itself)
        [InlineData(0.75, 1.0)]
        [InlineData(1.0, 1.5)]
        [InlineData(0.6, 0.75)]  // between rungs -> the next rung above
        public void NextLadderValue_BelowTop_ReturnsNextRungAbove(double current, double expected)
        {
            // Act
            double? next = AutoTune3DSolver.NextLadderValue(current, DefaultLadder);

            // Assert
            Assert.NotNull(next);
            Assert.Equal(expected, next.Value, 6);
        }

        [Theory]
        [InlineData(1.5)]   // exactly the top rung -> exhausted
        [InlineData(1.6)]   // already past the top -> exhausted (never reduces)
        [InlineData(5.0)]
        public void NextLadderValue_AtOrPastTop_ReturnsNull(double current)
        {
            // Act
            double? next = AutoTune3DSolver.NextLadderValue(current, DefaultLadder);

            // Assert - a culprit already at (or past) the top of the ladder drops out.
            Assert.Null(next);
        }

        // ── 3. Culprit-only escalation ────────────────────────────────────────────────────────────────

        [Fact]
        public void EscalateCulprits_OnlyAttributedSources_AreRaised()
        {
            // Arrange - three sources at the baseline reach; only source 1 is a culprit.
            List<double> maxExtends = new List<double> { 0.4, 0.4, 0.4 };
            List<double> buckets = new List<double> { 0.3, 0.3, 0.3 };

            // Act
            IReadOnlyList<int> escalated = AutoTune3DSolver.EscalateCulprits(
                new List<int> { 1 }, DefaultLadder, maxExtends, buckets, escalateBucket: false, bucketFactor: 1.25);

            // Assert - only source 1 moved; its neighbours are untouched.
            Assert.Equal(new[] { 1 }, escalated.ToArray());
            Assert.Equal(0.4, maxExtends[0], 6);
            Assert.Equal(0.5, maxExtends[1], 6);
            Assert.Equal(0.4, maxExtends[2], 6);
        }

        [Fact]
        public void EscalateCulprits_AcrossRounds_ClimbsTheLadderOneRungPerRound()
        {
            // Arrange - one culprit source, escalated round after round.
            List<double> maxExtends = new List<double> { 0.4, 0.4 };
            List<double> buckets = new List<double> { 0.3, 0.3 };

            // Act & Assert - 0.4 -> 0.5 -> 0.75 -> 1.0 -> 1.5, then exhausted.
            double[] expected = { 0.5, 0.75, 1.0, 1.5 };
            foreach (double rung in expected)
            {
                IReadOnlyList<int> escalated = AutoTune3DSolver.EscalateCulprits(
                    new List<int> { 0 }, DefaultLadder, maxExtends, buckets, escalateBucket: false, bucketFactor: 1.25);
                Assert.Equal(new[] { 0 }, escalated.ToArray());
                Assert.Equal(rung, maxExtends[0], 6);
            }

            // The fifth round finds source 0 at the top rung - nothing left to raise.
            IReadOnlyList<int> exhausted = AutoTune3DSolver.EscalateCulprits(
                new List<int> { 0 }, DefaultLadder, maxExtends, buckets, escalateBucket: false, bucketFactor: 1.25);
            Assert.Empty(exhausted);
            Assert.Equal(1.5, maxExtends[0], 6);
        }

        // ── 4. Exhausted ladder behaviour ─────────────────────────────────────────────────────────────

        [Fact]
        public void EscalateCulprits_SourceAtTopRung_IsNotEscalatedFurther()
        {
            // Arrange - source 0 is already at 1.5 (the top of the ladder); source 1 is fresh.
            List<double> maxExtends = new List<double> { 1.5, 0.4 };
            List<double> buckets = new List<double> { 0.3, 0.3 };

            // Act - both are culprits this round.
            IReadOnlyList<int> escalated = AutoTune3DSolver.EscalateCulprits(
                new List<int> { 0, 1 }, DefaultLadder, maxExtends, buckets, escalateBucket: false, bucketFactor: 1.25);

            // Assert - the exhausted source stays put; only the fresh one climbs.
            Assert.Equal(new[] { 1 }, escalated.ToArray());
            Assert.Equal(1.5, maxExtends[0], 6);
            Assert.Equal(0.5, maxExtends[1], 6);
        }

        [Fact]
        public void EscalateCulprits_AllCulpritsExhausted_ReturnsEmpty()
        {
            // Arrange - every culprit is already at the top rung.
            List<double> maxExtends = new List<double> { 1.5, 1.5 };
            List<double> buckets = new List<double> { 0.3, 0.3 };

            // Act
            IReadOnlyList<int> escalated = AutoTune3DSolver.EscalateCulprits(
                new List<int> { 0, 1 }, DefaultLadder, maxExtends, buckets, escalateBucket: false, bucketFactor: 1.25);

            // Assert - the loop uses this emptiness to stop (ladder exhausted).
            Assert.Empty(escalated);
        }

        // ── 5. Acceptance rule truth table ────────────────────────────────────────────────────────────

        [Fact]
        public void IsAcceptableRound_NakedDropsNoRegression_Accepts()
        {
            // Arrange - naked 4 -> 0, same cells/volume, no new sliver (gap closed without a new cell).
            ClosureSignature3D previous = Sig(naked: 4, cells: 2, totalVolume: 100);
            ClosureSignature3D candidate = Sig(naked: 0, cells: 2, totalVolume: 100);

            // Act & Assert
            Assert.True(AutoTune3DSolver.IsAcceptableRound(previous, candidate, previousFabricatedCount: 0, candidateFabricatedCount: 0, cellIncreaseAdjacent: true));
        }

        [Fact]
        public void IsAcceptableRound_NakedDropsNewCellAdjacent_Accepts()
        {
            // Arrange - naked 4 -> 0 and a new cell formed, proven adjacent to a closed loop.
            ClosureSignature3D previous = Sig(naked: 4, cells: 2, totalVolume: 100);
            ClosureSignature3D candidate = Sig(naked: 0, cells: 3, totalVolume: 130);

            // Act & Assert
            Assert.True(AutoTune3DSolver.IsAcceptableRound(previous, candidate, previousFabricatedCount: 0, candidateFabricatedCount: 0, cellIncreaseAdjacent: true));
        }

        [Fact]
        public void IsAcceptableRound_NakedDropsButCellsRegress_Rejects()
        {
            // Arrange - naked improved but a cell was lost (a partition over-merged): a signature regression.
            ClosureSignature3D previous = Sig(naked: 4, cells: 2, totalVolume: 100);
            ClosureSignature3D candidate = Sig(naked: 0, cells: 1, totalVolume: 100);

            // Act & Assert - rejected regardless of the adjacency verdict.
            Assert.False(AutoTune3DSolver.IsAcceptableRound(previous, candidate, previousFabricatedCount: 0, candidateFabricatedCount: 0, cellIncreaseAdjacent: true));
        }

        [Fact]
        public void IsAcceptableRound_SliverCountIncreases_Rejects()
        {
            // Arrange - naked improved and cells held, but a new sliver cell appeared (closed by manufacturing junk).
            ClosureSignature3D previous = Sig(naked: 4, cells: 2, totalVolume: 100, sliver: 0);
            ClosureSignature3D candidate = Sig(naked: 0, cells: 2, totalVolume: 100, sliver: 1);

            // Act & Assert
            Assert.False(AutoTune3DSolver.IsAcceptableRound(previous, candidate, previousFabricatedCount: 0, candidateFabricatedCount: 0, cellIncreaseAdjacent: true));
        }

        [Fact]
        public void IsAcceptableRound_CellAdjacencyUnproven_RejectsConservatively()
        {
            // Arrange - naked improved, no regression, no new sliver, a new cell formed - but its adjacency to a
            // closed loop could not be positively proven (owner caution 3).
            ClosureSignature3D previous = Sig(naked: 4, cells: 2, totalVolume: 100);
            ClosureSignature3D candidate = Sig(naked: 0, cells: 3, totalVolume: 130);

            // Act & Assert
            Assert.False(AutoTune3DSolver.IsAcceptableRound(previous, candidate, previousFabricatedCount: 0, candidateFabricatedCount: 0, cellIncreaseAdjacent: false));
        }

        [Fact]
        public void IsAcceptableRound_NakedNotStrictlyLower_Rejects()
        {
            // Arrange - the candidate did not strictly reduce naked edges.
            ClosureSignature3D previous = Sig(naked: 4, cells: 2, totalVolume: 100);
            ClosureSignature3D candidate = Sig(naked: 4, cells: 2, totalVolume: 100);

            // Act & Assert
            Assert.False(AutoTune3DSolver.IsAcceptableRound(previous, candidate, previousFabricatedCount: 0, candidateFabricatedCount: 0, cellIncreaseAdjacent: true));
        }

        [Fact]
        public void IsAcceptableRound_NullSignature_ReturnsFalseWithoutThrowing()
        {
            // Arrange
            ClosureSignature3D signature = Sig(naked: 0, cells: 1, totalVolume: 10);

            // Act & Assert - null-safe (best-effort: never throws), and a missing signature is never acceptable.
            Assert.False(AutoTune3DSolver.IsAcceptableRound(null, signature, previousFabricatedCount: 0, candidateFabricatedCount: 0, cellIncreaseAdjacent: true));
            Assert.False(AutoTune3DSolver.IsAcceptableRound(signature, null, previousFabricatedCount: 0, candidateFabricatedCount: 0, cellIncreaseAdjacent: true));
        }

        // Fabrication-driven acceptance (owner refinement): the baseline was watertight but closed only by
        // fabricated GapFill/HoleFill patches; a round is accepted when it keeps naked at 0 and strictly reduces
        // the fabricated patch count (measured extension replaced fabrication).

        [Fact]
        public void IsAcceptableRound_FabricationDriven_NakedHeldAndPatchesDrop_Accepts()
        {
            // Arrange - baseline watertight (naked 0) with 3 fabricated patches; the round keeps it watertight and
            // reduces the patches to 1, with no signature regression.
            ClosureSignature3D previous = Sig(naked: 0, cells: 2, totalVolume: 100);
            ClosureSignature3D candidate = Sig(naked: 0, cells: 2, totalVolume: 100);

            // Act & Assert
            Assert.True(AutoTune3DSolver.IsAcceptableRound(previous, candidate, previousFabricatedCount: 3, candidateFabricatedCount: 1, cellIncreaseAdjacent: true));
        }

        [Fact]
        public void IsAcceptableRound_FabricationDriven_PatchesNotReduced_Rejects()
        {
            // Arrange - the round left the fabricated patch count unchanged: no progress, so reject (and stop).
            ClosureSignature3D previous = Sig(naked: 0, cells: 2, totalVolume: 100);
            ClosureSignature3D candidate = Sig(naked: 0, cells: 2, totalVolume: 100);

            // Act & Assert
            Assert.False(AutoTune3DSolver.IsAcceptableRound(previous, candidate, previousFabricatedCount: 3, candidateFabricatedCount: 3, cellIncreaseAdjacent: true));
        }

        [Fact]
        public void IsAcceptableRound_FabricationDriven_ReintroducesNaked_Rejects()
        {
            // Arrange - the round reduced patches but re-opened the closure (naked went 0 -> 2): a regression.
            ClosureSignature3D previous = Sig(naked: 0, cells: 2, totalVolume: 100);
            ClosureSignature3D candidate = Sig(naked: 2, cells: 2, totalVolume: 100);

            // Act & Assert - naked rising is a ClosureSignature3D regression, so it is rejected regardless of patches.
            Assert.False(AutoTune3DSolver.IsAcceptableRound(previous, candidate, previousFabricatedCount: 3, candidateFabricatedCount: 0, cellIncreaseAdjacent: true));
        }

        // CellIncreaseAdjacent - the geometric adjacency proof feeding the gate above.

        [Fact]
        public void CellIncreaseAdjacent_NoNetIncrease_IsTrue()
        {
            // Arrange - the candidate has no more cells than the previous state: nothing new to prove.
            List<Point3D> previous = new List<Point3D> { new Point3D(2, 2, 1) };
            List<Point3D> candidate = new List<Point3D> { new Point3D(2, 2, 1) };
            List<BoundingBox3D> candidateBoxes = new List<BoundingBox3D> { Box(2, 2, 1, 1) };

            // Act
            bool adjacent = AutoTune3DSolver.CellIncreaseAdjacent(previous, candidate, candidateBoxes, new List<OcctNakedWire>(), 0.1, out string unproven);

            // Assert
            Assert.True(adjacent);
            Assert.Null(unproven);
        }

        [Fact]
        public void CellIncreaseAdjacent_ClosedLoopOnNewCellBoundary_IsTrue()
        {
            // Arrange - a new cell whose box is [6,8] x [6,8] x [0,2]; the loop that closed ([6,8] x [6,8] at z=1)
            // lies on its boundary. (A small gap can form a large room, so the loop is tested against the CELL box.)
            List<Point3D> previous = new List<Point3D> { new Point3D(2, 2, 1) };
            List<Point3D> candidate = new List<Point3D> { new Point3D(2, 2, 1), new Point3D(7, 7, 1) };
            List<BoundingBox3D> candidateBoxes = new List<BoundingBox3D> { Box(2, 2, 1, 1), Box(7, 7, 1, 1) };
            List<OcctNakedWire> closedLoops = new List<OcctNakedWire> { RectWire(6, 6, 8, 8, 1) };

            // Act
            bool adjacent = AutoTune3DSolver.CellIncreaseAdjacent(previous, candidate, candidateBoxes, closedLoops, 0.1, out string unproven);

            // Assert
            Assert.True(adjacent);
            Assert.Null(unproven);
        }

        [Fact]
        public void CellIncreaseAdjacent_NewCellButNoClosedLoop_IsFalseWithReason()
        {
            // Arrange - a new cell appeared but no naked loop closed this round to explain it.
            List<Point3D> previous = new List<Point3D> { new Point3D(2, 2, 1) };
            List<Point3D> candidate = new List<Point3D> { new Point3D(2, 2, 1), new Point3D(7, 7, 1) };
            List<BoundingBox3D> candidateBoxes = new List<BoundingBox3D> { Box(2, 2, 1, 1), Box(7, 7, 1, 1) };

            // Act
            bool adjacent = AutoTune3DSolver.CellIncreaseAdjacent(previous, candidate, candidateBoxes, new List<OcctNakedWire>(), 0.1, out string unproven);

            // Assert - unprovable, so reject conservatively and hand back a diagnostic reason.
            Assert.False(adjacent);
            Assert.False(string.IsNullOrEmpty(unproven));
        }

        [Fact]
        public void CellIncreaseAdjacent_NewCellAwayFromEveryClosedLoop_IsFalseWithReason()
        {
            // Arrange - the new cell around (50,50,50) is nowhere near the loop that closed ([6,8] x [6,8]).
            List<Point3D> previous = new List<Point3D> { new Point3D(2, 2, 1) };
            List<Point3D> candidate = new List<Point3D> { new Point3D(2, 2, 1), new Point3D(50, 50, 50) };
            List<BoundingBox3D> candidateBoxes = new List<BoundingBox3D> { Box(2, 2, 1, 1), Box(50, 50, 50, 1) };
            List<OcctNakedWire> closedLoops = new List<OcctNakedWire> { RectWire(6, 6, 8, 8, 1) };

            // Act
            bool adjacent = AutoTune3DSolver.CellIncreaseAdjacent(previous, candidate, candidateBoxes, closedLoops, 0.1, out string unproven);

            // Assert
            Assert.False(adjacent);
            Assert.False(string.IsNullOrEmpty(unproven));
        }

        // ── 6. Best-effort / no-throw behaviour on an unprovable round ────────────────────────────────

        [Fact]
        public void UnprovableRound_GateRejectsWithDiagnosticReason_AndNeverThrows()
        {
            // Arrange - a candidate that improves naked and adds a cell, but the cell cannot be tied to a closed
            // loop: the pure representation of a residual/unresolvable escalation the loop rejects (best-effort,
            // never throwing) while emitting a diagnostic (the reason string).
            ClosureSignature3D previous = Sig(naked: 6, cells: 3, totalVolume: 90);
            ClosureSignature3D candidate = Sig(naked: 2, cells: 4, totalVolume: 120);
            List<Point3D> previousCenters = new List<Point3D> { new Point3D(1, 1, 1), new Point3D(3, 1, 1), new Point3D(5, 1, 1) };
            List<Point3D> candidateCenters = new List<Point3D>(previousCenters) { new Point3D(40, 40, 40) };
            List<BoundingBox3D> candidateBoxes = new List<BoundingBox3D>
            {
                Box(1, 1, 1, 1), Box(3, 1, 1, 1), Box(5, 1, 1, 1), Box(40, 40, 40, 1)
            };

            // Act - no closed loop is supplied, so the new cell is unprovable.
            bool adjacent = AutoTune3DSolver.CellIncreaseAdjacent(previousCenters, candidateCenters, candidateBoxes, new List<OcctNakedWire>(), 0.1, out string unproven);
            bool accepted = AutoTune3DSolver.IsAcceptableRound(previous, candidate, previousFabricatedCount: 0, candidateFabricatedCount: 0, cellIncreaseAdjacent: adjacent);

            // Assert - rejected conservatively, a diagnostic reason is produced, and nothing threw.
            Assert.False(adjacent);
            Assert.False(accepted);
            Assert.False(string.IsNullOrEmpty(unproven));
        }

        // ── 7. Bucket escalation default OFF ──────────────────────────────────────────────────────────

        [Fact]
        public void EscalateCulprits_BucketEscalationOff_LeavesBucketsUnchanged()
        {
            // Arrange - default (EscalateBucket = false): reach climbs, buckets are untouched.
            List<double> maxExtends = new List<double> { 0.4, 0.4 };
            List<double> buckets = new List<double> { 0.3, 0.3 };

            // Act
            AutoTune3DSolver.EscalateCulprits(new List<int> { 0 }, DefaultLadder, maxExtends, buckets, escalateBucket: false, bucketFactor: 1.25);

            // Assert
            Assert.Equal(0.5, maxExtends[0], 6);
            Assert.Equal(0.3, buckets[0], 6);
        }

        [Fact]
        public void EscalateCulprits_BucketEscalationOn_GrowsTheCulpritBucket()
        {
            // Arrange - opt-in (EscalateBucket = true): the culprit's bucket also grows by BucketFactor.
            List<double> maxExtends = new List<double> { 0.4, 0.4 };
            List<double> buckets = new List<double> { 0.3, 0.3 };

            // Act
            AutoTune3DSolver.EscalateCulprits(new List<int> { 0 }, DefaultLadder, maxExtends, buckets, escalateBucket: true, bucketFactor: 1.25);

            // Assert - only the escalated culprit's bucket is scaled; its neighbour is untouched.
            Assert.Equal(0.3 * 1.25, buckets[0], 6);
            Assert.Equal(0.3, buckets[1], 6);
        }
    }
}
