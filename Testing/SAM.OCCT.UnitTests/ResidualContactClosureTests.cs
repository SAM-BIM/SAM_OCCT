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
    /// Unit tests for <see cref="ResidualContactClosure"/> (residual cap-edge closure, rule E): the controls C01-C31 of
    /// the towers residual-closure review, the self-overlap guard, the lineage guards fed from the real
    /// <see cref="SnapStage"/> attribution, and the one-step/uniform/untilted gate through
    /// <see cref="Panel3DSnapSolver.Execute"/>. Pure-managed: no native OCCT required. Every synthetic cap sits at z = 3
    /// with fill margin 0.4 m and overshoot 0.05 m; "current" geometry is the post-grow state the pass sees.
    /// </summary>
    public class ResidualContactClosureTests
    {
        private const double Angle = 5 * System.Math.PI / 180;
        private const double Margin = 0.4;
        private const double Overshoot = 0.05;
        private const double Tolerance = 1e-6;
        private const double Z = 3;

        // ──────────────────────────────────────────────────────────────
        // C01-C18: eligibility, coverage, blockers, holes, reach
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void Apply_NeverAuthoredInContact_KeepsMezzanineVoid()
        {
            // Arrange (C01): the cap edge y 4 was never on the wall y 4.2 (a deliberate 0.2 m void).
            Case c = new Case().Add(Rect(0, 0, 4, 4)).Add(WallY(4.2, 0, 4, 0, 6));

            // Act / Assert
            AssertNoClosure(c);
        }

        [Fact]
        public void Apply_AuthoredContactWallSnappedAway_MovesEdgeOntoWallPlusOvershoot()
        {
            // Arrange (C02, the towers analogue): authored on the wall at y 4, the wall now stands at y 4.15.
            Case c = Base();

            // Act
            List<ResidualContactClosure.Closure> closures = c.Apply();

            // Assert: one closure, the north edge moved 0.15 + 0.05 to y 4.2, nothing else changed.
            ResidualContactClosure.Closure closure = Assert.Single(closures);
            Assert.Equal(0, closure.CapIndex);
            Assert.Equal(1, closure.WallIndex);
            Assert.Equal(0.2, closure.Move, 9);
            AssertBox(c.Panels[0].Face3D, 0, 0, 4, 4.2);
            Assert.Equal(16.8, c.Panels[0].GetArea(), 9);
        }

        [Fact]
        public void Apply_StripPartlyCoveredByAnotherCap_StillCloses()
        {
            // Arrange (C03): another coplanar cap covers x 0..2 of the strip.
            Case c = Base().Add(Rect(0, 3.9, 2, 4.3));

            // Act / Assert
            Assert.Single(c.Apply());
            AssertBox(c.Panels[0].Face3D, 0, 0, 4, 4.2);
        }

        [Fact]
        public void Apply_StripFullyCoveredByAnotherCap_LeavesCap()
        {
            // Arrange (C04)
            Case c = Base().Add(Rect(-0.1, 3.9, 4.1, 4.3));

            // Act / Assert
            AssertNoClosure(c);
        }

        [Fact]
        public void Apply_CapWithHole_ClosesAndKeepsHole()
        {
            // Arrange (C05)
            Case c = Base(cap: Rect(0, 0, 4, 4, (1, 1, 2, 2)));

            // Act
            Assert.Single(c.Apply());

            // Assert: the hole is kept exactly.
            Face3D moved = c.Panels[0].Face3D;
            Assert.Single(moved.GetInternalEdge3Ds());
            Assert.Equal(15.8, moved.GetArea(), 9);
        }

        [Fact]
        public void Apply_ShaftWallAuthoredOnHoleEdge_LeavesCap()
        {
            // Arrange (C06): a shaft wall authored on the cap's HOLE edge (y 3) snapped into the hole.
            Face3D cap = Rect(0, 0, 4, 4, (1, 1, 3, 3));
            Case c = new Case().Add(cap, cap).Add(WallY(2.85, 1, 3, 0, 6), WallY(3, 1, 3, 0, 6));

            // Act / Assert
            AssertNoClosure(c);
        }

        [Fact]
        public void Apply_BlockerInsideStrip_LeavesCap()
        {
            // Arrange (C07)
            AssertNoClosure(Base().Add(WallY(4.07, 1, 3, 0, 6)));
        }

        [Fact]
        public void Apply_BlockerInsideOvershoot_LeavesCap()
        {
            // Arrange (C08): beyond the wall plane, within the 0.05 m overshoot.
            AssertNoClosure(Base().Add(WallY(4.18, 1, 3, 0, 6)));
        }

        [Fact]
        public void Apply_WallOverStripOnlyAboveCap_Closes()
        {
            // Arrange (C09): over the strip in plan, but only at z 5..6 (finite section).
            Case c = Base().Add(WallY(4.07, 1, 3, 5, 6));

            // Act / Assert
            Assert.Single(c.Apply());
        }

        [Fact]
        public void Apply_NarrowPerpendicularStub_LeavesCap()
        {
            // Arrange (C10): a 0.10 m stub between any 20 mm sampling - the test is exact.
            AssertNoClosure(Base().Add(WallX(2.013, 4.02, 4.12, 0, 6)));
        }

        [Fact]
        public void Apply_ShortfallBeyondFillMargin_LeavesCap()
        {
            // Arrange (C11): the wall moved 0.45 m (> fill margin 0.4).
            AssertNoClosure(Base(north: WallY(4.45, 0, 4, 0, 6)));
        }

        [Fact]
        public void Apply_CapAlreadyOnWallPlane_LeavesCap()
        {
            // Arrange (C12)
            AssertNoClosure(Base(north: WallY(4.0, 0, 4, 0, 6)));
        }

        [Fact]
        public void Apply_TargetWallOpenAtCapHeight_LeavesCap()
        {
            // Arrange (C13): an opening x 1..2, z 2.5..3.5 in the target wall.
            Face3D wall = Face3D.Create(new List<IClosedPlanar3D>
            {
                Loop((0, 4.15, 0), (4, 4.15, 0), (4, 4.15, 6), (0, 4.15, 6)),
                Loop((1, 4.15, 2.5), (1, 4.15, 3.5), (2, 4.15, 3.5), (2, 4.15, 2.5)),
            });
            AssertNoClosure(Base(north: wall));
        }

        [Fact]
        public void Apply_OtherWallSourceFootInStrip_LeavesCap()
        {
            // Arrange (C14): another wall's SOURCE foot (y 4.08) lies inside the strip (authored uniqueness).
            AssertNoClosure(Base().Add(WallY(6, 1, 3, 0, 6), WallY(4.08, 1, 3, 0, 6)));
        }

        [Fact]
        public void Apply_CornerPartnerCrossesSweep_ClosesWithExemption()
        {
            // Arrange (C15): the east wall now stands at x 3.98, inside the sweep, but its source bounds the cap.
            Case c = Base(east: WallX(3.98, -0.05, 4.2, 0, 6));

            // Act / Assert
            Assert.Single(c.Apply());
        }

        [Fact]
        public void Apply_NonPartnerCrossesSweep_LeavesCap()
        {
            // Arrange (C16): the same crossing wall, but its source never bounded the cap.
            AssertNoClosure(Base(east: WallX(3.98, -0.05, 4.2, 0, 6), eastSource: WallX(6, 0, 4, 0, 6)));
        }

        [Fact]
        public void Apply_SourceWallOnAnotherStorey_LeavesCap()
        {
            // Arrange (C17): the north wall's source spans z 3.5..6 only.
            AssertNoClosure(Base(northSource: WallY(4, 0, 4, 3.5, 6)));
        }

        [Fact]
        public void Apply_FacingNotchNotImageOfAuthoredEdge_LeavesCap()
        {
            // Arrange (C18): a notch edge 0.8 m from the authored contact faces a wall moved INTO the room.
            Face3D notched = Poly((0, 0), (4, 0), (4, 4), (2.5, 4), (2.5, 3.2), (1.5, 3.2), (1.5, 4), (0, 4));
            Case c = new Case().Add(notched, Rect(0, 0, 4, 4)).Add(WallY(3.5, 0, 4, 0, 6), WallY(4, 0, 4, 0, 6));
            AssertNoClosure(c);
        }

        // ──────────────────────────────────────────────────────────────
        // C19-C25: blockers of any slope, cyclic chains, partners, intervals, the cap's own geometry
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void Apply_SlopedPanelCrossesSweep_LeavesCap()
        {
            // Arrange (C19): a 45 degree panel crossing the sweep at cap height.
            Face3D sloped = Face3D.Create(new List<IClosedPlanar3D> { Loop((1, 3.97, 2.9), (3, 3.97, 2.9), (3, 4.17, 3.1), (1, 4.17, 3.1)) });
            AssertNoClosure(Base().Add(sloped));
        }

        [Fact]
        public void Apply_TiltedWallCrossesSweep_LeavesCap()
        {
            // Arrange (C20): 10 degrees off vertical - a wall to Fill (20 degrees), not a target to the rule.
            double tn = System.Math.Tan(10 * System.Math.PI / 180);
            Face3D tilted = Face3D.Create(new List<IClosedPlanar3D> { Loop((1, 4.07 - (3 * tn), 0), (3, 4.07 - (3 * tn), 0), (3, 4.07 + (3 * tn), 6), (1, 4.07 + (3 * tn), 6)) });
            AssertNoClosure(Base().Add(tilted));
        }

        [Fact]
        public void Apply_FacingChainWrapsRingStart_Closes()
        {
            // Arrange (C21): the cap ring starts mid north edge, so the facing chain wraps index 0.
            Case c = Base(cap: Poly((2, 4), (0, 4), (0, 0), (4, 0), (4, 4)), capSource: Rect(0, 0, 4, 4));

            // Act / Assert
            Assert.Single(c.Apply());
            AssertBox(c.Panels[0].Face3D, 0, 0, 4, 4.2);
        }

        [Fact]
        public void Apply_PartnerCrossesSweepMidSpan_LeavesCap()
        {
            // Arrange (C22): the partner's current stub crosses the sweep at x 2, far from its corner.
            AssertNoClosure(Base(east: WallX(2, 3.9, 4.3, 0, 6)));
        }

        [Fact]
        public void Apply_BlockerTouchesSweepBoundaryOnly_Closes()
        {
            // Arrange (C23): a wall exactly on the sweep's far boundary (y 4.2) does not block.
            Assert.Single(Base().Add(WallY(4.2, 1, 3, 0, 6)).Apply());
        }

        [Fact]
        public void Apply_TwoSeparateContactIntervals_LeavesCap()
        {
            // Arrange (C24): the north wall merged from two authored pieces (x 0..1.5 and 2.5..4).
            Case c = Base(northSources: new[] { WallY(4, 0, 1.5, 0, 6), WallY(4, 2.5, 4, 0, 6) });
            AssertNoClosure(c);
        }

        [Fact]
        public void Apply_CapArmBeyondWallBreaksChain_LeavesCap()
        {
            // Arrange (C25): the cap's own thin arm beyond the wall (y 4.17..4.19) - fails closed.
            Face3D arm = Poly((0, 0), (4.5, 0), (4.5, 4.19), (1, 4.19), (1, 4.17), (4.3, 4.17), (4.3, 4), (0, 4));
            Case c = new Case().Add(arm, Rect(0, 0, 4, 4)).Add(WallY(4.15, 0, 4, 0, 6), WallY(4, 0, 4, 0, 6)).Add(WallX(0, -0.05, 4.2, 0, 6), WallX(0, 0, 4, 0, 6));
            AssertNoClosure(c);
        }

        // ──────────────────────────────────────────────────────────────
        // C29: the self-overlap guard is load-bearing
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void Apply_SweepOverlapsCapItself_LeavesCap()
        {
            // Arrange (C29): the cap's own NE overhang lies inside the sweep; the facing chain is one contiguous edge.
            Case c = new Case().Add(HookCap(), Rect(0, 0, 4, 4)).Add(WallY(4.15, 0, 4, 0, 6), WallY(4, 0, 4, 0, 6)).Add(WallX(0, -0.05, 4.2, 0, 6), WallX(0, 0, 4, 0, 6));
            AssertNoClosure(c);
        }

        [Fact]
        public void MoveSplitBoundaryRun_SweepAcrossOwnOverhangWithoutCheck_AdoptsSelfIntersectingBoundary()
        {
            // Arrange: the C29 move the guard refuses - split at x 4 and move the north run (x 0..4) by +0.2.
            SnappedPanel cap = new SnappedPanel(0, HookCap(), 1, 0.4, 0);
            List<Point3D> split = SplitHook();

            // Act: MoveBoundaryRun's own checks (IsValid, larger) accept it...
            bool moved = cap.MoveSplitBoundaryRun(split, 6, 7, 0, 0.2, Angle, Tolerance);

            // Assert: ...although the boundary now crosses itself, which is why the pass adds its own checks.
            Assert.True(moved);
            Assert.True(cap.Face3D.IsValid());
            Assert.False(PlanIsSimple(cap.Face3D));
        }

        [Fact]
        public void MoveSplitBoundaryRun_SimplePolygonCheckRejects_LeavesCapUnchanged()
        {
            // Arrange
            SnappedPanel cap = new SnappedPanel(0, HookCap(), 1, 0.4, 0);
            double area = cap.GetArea();

            // Act
            bool moved = cap.MoveSplitBoundaryRun(SplitHook(), 6, 7, 0, 0.2, Angle, Tolerance, PlanIsSimple);

            // Assert: refused, and the cap is exactly the original (7 points, same area).
            Assert.False(moved);
            Assert.Equal(7, SnappedPanel.BoundaryPoints(cap.Face3D).Count);
            Assert.Equal(area, cap.GetArea(), 12);
        }

        // ──────────────────────────────────────────────────────────────
        // C26-C31: lineage correspondence and attribution guards
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void Apply_LineageAddsNeighbourFloorBeyondWall_LeavesVoid()
        {
            // Arrange (C26): the cap's lineage also lists the neighbour floor beyond W that touched W; that source's
            // contact-edge band lies outside the current cap (G2).
            Face3D mezzanine = Rect(0, 0, 4, 4);
            Case c = new Case().Add(mezzanine, mezzanine, Rect(0, 4.2, 4, 8)).Add(WallY(4.2, 0, 4, 0, 9));
            AssertNoClosure(c);
        }

        [Fact]
        public void Apply_NearestFallbackCapLineage_LeavesVoid()
        {
            // Arrange (C27): the storey-above floor in the cap's lineage through the nearest-panel fallback (G1).
            Face3D mezzanine = Rect(0, 0, 4, 4);
            Case c = new Case().AddFallback(mezzanine, mezzanine, Rect(0, 0, 4, 4.2, z: 6)).Add(WallY(4.2, 0, 4, 0, 9));
            AssertNoClosure(c);
        }

        [Fact]
        public void Apply_NearestFallbackWallLineage_LeavesVoid()
        {
            // Arrange (C28): an unrelated partition in the WALL's lineage through the nearest-panel fallback (G1).
            Face3D wall = WallY(4.2, 0, 4, 0, 9);
            Case c = new Case().Add(Rect(0, 0, 4, 4)).AddFallback(wall, wall, WallY(4.0, 0, 4, 0, 9));
            AssertNoClosure(c);
        }

        [Fact]
        public void Apply_SourceCapOffLevel_LeavesVoid()
        {
            // Arrange (C30): the C27 lineage claimed geometric - the level check rejects the z 6 source (G2).
            Face3D mezzanine = Rect(0, 0, 4, 4);
            Case c = new Case().Add(mezzanine, mezzanine, Rect(0, 0, 4, 4.2, z: 6)).Add(WallY(4.2, 0, 4, 0, 9));
            AssertNoClosure(c);
        }

        [Fact]
        public void Apply_UniqueGeometricWallLineage_Closes()
        {
            // Arrange (C31, documented limit): a wall source attributed geometrically and uniquely to W is, by
            // construction, a wall Stage A moved onto W's plane - the rule's intended case, so it closes.
            Face3D wall = WallY(4.2, 0, 4, 0, 9);
            Case c = new Case().Add(Rect(0, 0, 4, 4)).Add(wall, wall, WallY(4.0, 0, 4, 0, 9));

            // Act / Assert
            Assert.Single(c.Apply());
        }

        [Fact]
        public void Apply_WallSourceAttributedToTwoCleanFaces_LeavesVoid()
        {
            // Arrange (C31 through geometric attribution): the partition source is ALSO the source of its own,
            // still-standing wall - attributed to two clean faces, so it is ambiguous and creates no contact.
            Face3D wall = WallY(4.2, 0, 4, 0, 9);
            Face3D partition = WallY(4.0, 0, 4, 0, 9);
            Case c = new Case().Add(Rect(0, 0, 4, 4)).Add(wall, wall, partition).Add(WallY(4.0, 5, 9, 0, 9), partition);
            AssertNoClosure(c);
        }

        [Fact]
        public void SnapStageClean_ShallowCrossingPartition_AttributedToBothCleanFaces()
        {
            // Arrange: a partition 7 degrees off a wall's line (outside the 5 degree snap, inside the 0.99 attribution
            // cone), crossing it at its centre - the geometric path that could carry an unrelated wall into W's lineage.
            double t = System.Math.Tan(7 * System.Math.PI / 180);
            Face3D wall = WallY(4.2, 0, 8, 0, 6);
            Face3D partition = Face3D.Create(new List<IClosedPlanar3D> { Loop((2, 4.2 - (2 * t), 0), (6, 4.2 + (2 * t), 0), (6, 4.2 + (2 * t), 6), (2, 4.2 - (2 * t), 6)) });
            List<SnappedPanel> panels = new List<SnappedPanel> { new SnappedPanel(0, wall, 1, 0.4, 0), new SnappedPanel(1, partition, 1, 0.4, 0) };

            // Act
            SnapStage.Result result = SnapStage.Clean(panels, new ToleranceBudget(), 0.3, 0.3);

            // Assert: the partition is not snapped away, both faces survive, and its source index appears in two
            // clean faces' lineage - exactly what the ambiguity guard rejects.
            Assert.Equal(2, result.CleanFace3Ds.Count);
            Assert.All(result.GeometricAttributionPerFace, Assert.True);
            Assert.Equal(2, result.SourceIndicesPerFace.Count(x => x.Contains(1)));
        }

        // ──────────────────────────────────────────────────────────────
        // End to end through Panel3DSnapSolver.Execute (real Stage A attribution, Fill, gate)
        // ──────────────────────────────────────────────────────────────

        [Fact]
        public void Execute_MisattributedUnrelatedFloor_KeepsAuthoredVoid()
        {
            // Arrange (E2E): floor B is authored 0.2 m short of W and never in contact; an unrelated U floor A wraps B
            // and runs along W's far side. A's box centre lies inside B, so Stage A attributes A to B's clean face.
            Face3D b = Poly((0, 0), (5, 0), (5, 4.19), (4.3, 4.19), (4.3, 4.0), (0, 4.0));
            Face3D a = Poly((-1.1, -1.1), (-0.1, -1.1), (-0.1, 4.2), (5.2, 4.2), (5.2, -1.1), (6.2, -1.1), (6.2, 5.2), (-1.1, 5.2));
            Face3D w = WallY(4.2, -0.1, 4.1, 0, 6);

            // Act
            Panel3DSnapSolver solver = Extend(new[] { b, a, w }, directional: false, inputAlreadyClean: false);

            // Assert: no residual closure; B keeps the Fill-only growth (north edge y 4.06, not 4.2+).
            Assert.DoesNotContain(solver.ExtendRecords, x => x.TargetKind == "walls-source-contact");
            Face3D bAfter = solver.ResolvedFace3Ds.Single(x => IsFlat(x) && x.GetBoundingBox().Min.X > -0.5);
            Assert.True(bAfter.GetBoundingBox().Max.Y < 4.26 && SnappedPanel.BoundaryPoints(bAfter).Where(p => p.X < 4).Max(p => p.Y) < 4.1);
        }

        [Fact]
        public void Execute_AuthoredContactBrokenByStageA_ClosesOnUniformPathOnly()
        {
            // Arrange (E2E positive): the cap's north edge is authored on wall P (y 4.0, x 0..4); Stage A snaps P onto the
            // dominant wall W (y 4.15, x 0..6). The cap's NE protrusion (y 4.14, beyond P) makes the whole-cap Fill grow
            // only 0.06 m, leaving a 0.09 m residual against W over the authored contact x 0..4.
            Face3D cap = Poly((0, 0), (5, 0), (5, 4.14), (4.5, 4.14), (4.5, 4.0), (0, 4.0));
            Face3D p = WallY(4.0, 0, 4, 0, 6);
            Face3D w = WallY(4.15, 0, 6, 0, 6);

            // Act
            Panel3DSnapSolver uniform = Extend(new[] { cap, p, w }, directional: false, inputAlreadyClean: false);
            Panel3DSnapSolver directional = Extend(new[] { cap, p, w }, directional: true, inputAlreadyClean: false);
            Panel3DSnapSolver twoStep = Extend(uniform.CleanFace3Ds, directional: false, inputAlreadyClean: true);

            // Assert: one closure on the one-step uniform path, none on the directional or the two-step path.
            ExtendRecord closure = Assert.Single(uniform.ExtendRecords, x => x.TargetKind == "walls-source-contact");
            Assert.Contains("Residual cap-edge closure", closure.TargetDescription);
            Face3D capAfter = uniform.ResolvedFace3Ds.Single(IsFlat);
            Assert.Equal(4.2, SnappedPanel.BoundaryPoints(capAfter).Where(x => x.X < 3).Max(x => x.Y), 6);
            Assert.DoesNotContain(directional.ExtendRecords, x => x.TargetKind == "walls-source-contact");
            Assert.DoesNotContain(twoStep.ExtendRecords, x => x.TargetKind == "walls-source-contact");
        }

        // ──────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────

        /// <summary>Panels with explicit per-panel lineage; a source list defaults to the panel's own face.</summary>
        private sealed class Case
        {
            internal readonly List<SnappedPanel> Panels = new List<SnappedPanel>();
            private readonly List<Face3D> sources = new List<Face3D>();
            private readonly List<List<int>> sourceIndices = new List<List<int>>();
            private readonly List<bool> geometric = new List<bool>();

            internal Case Add(Face3D current, params Face3D[] lineage) => Add(current, true, lineage);

            internal Case AddFallback(Face3D current, params Face3D[] lineage) => Add(current, false, lineage);

            private Case Add(Face3D current, bool isGeometric, Face3D[] lineage)
            {
                Panels.Add(new SnappedPanel(Panels.Count, current, 1, 0.4, 0));
                List<int> indices = new List<int>();
                foreach (Face3D source in lineage == null || lineage.Length == 0 ? new[] { current } : lineage)
                {
                    int index = sources.IndexOf(source);
                    if (index < 0)
                    {
                        sources.Add(source);
                        index = sources.Count - 1;
                    }

                    indices.Add(index);
                }

                sourceIndices.Add(indices);
                geometric.Add(isGeometric);
                return this;
            }

            internal List<ResidualContactClosure.Closure> Apply()
            {
                ResidualContactLineage lineage = new ResidualContactLineage(sources, sourceIndices, geometric, 0.4, 0.4, Angle);
                return ResidualContactClosure.Apply(Panels, lineage, Margin, Overshoot, Tolerance);
            }
        }

        /// <summary>The C02 base: cap 0..4 x 0..4; north wall authored at y 4, now at y 4.15; west/east/south walls
        /// authored on the cap's other edges (corner partners).</summary>
        private static Case Base(Face3D cap = null, Face3D capSource = null, Face3D north = null, Face3D northSource = null, Face3D[] northSources = null, Face3D east = null, Face3D eastSource = null)
        {
            Face3D capFace = cap ?? Rect(0, 0, 4, 4);
            return new Case()
                .Add(capFace, capSource ?? (cap == null ? capFace : Rect(0, 0, 4, 4)))
                .Add(north ?? WallY(4.15, 0, 4, 0, 6), northSources ?? new[] { northSource ?? WallY(4, 0, 4, 0, 6) })
                .Add(WallX(0, -0.05, 4.2, 0, 6), WallX(0, 0, 4, 0, 6))
                .Add(east ?? WallX(4, -0.05, 4.2, 0, 6), eastSource ?? WallX(4, 0, 4, 0, 6))
                .Add(WallY(0, -0.05, 4.05, 0, 6), WallY(0, 0, 4, 0, 6));
        }

        private static void AssertNoClosure(Case c)
        {
            List<Face3D> before = c.Panels.Select(x => x.Face3D).ToList();
            Assert.Empty(c.Apply());
            Assert.True(before.Zip(c.Panels, (f, p) => ReferenceEquals(f, p.Face3D)).All(x => x), "a panel changed without a closure");
        }

        /// <summary>One-step (or condition-only) Extend3D-equivalent solver run; the LAST face is the dominant snap backer.</summary>
        private static Panel3DSnapSolver Extend(IEnumerable<Face3D> faces, bool directional, bool inputAlreadyClean)
        {
            List<Face3D> list = faces.ToList();
            Panel3DSnapSolver solver = new Panel3DSnapSolver(list, list.Select(_ => 0.4), list.Select((_, i) => i == list.Count - 1 ? 10.0 : 1.0))
            {
                StopAfterExtend = true,
                FillMargin = Margin,
                AlignColinearOffset = 0.3,
                BucketBetweenLevels = 0.4,
                DirectionalCapGrow = directional,
                InputAlreadyClean = inputAlreadyClean,
            };
            solver.Execute(null);
            return solver;
        }

        private static Face3D HookCap() => Poly((0, 0), (4.5, 0), (4.5, 4.2), (3.7, 4.2), (3.8, 4.16), (4.3, 4.0), (0, 4.0));

        /// <summary>The hook cap's ring with the split point (4, 4) inserted on its north edge: run = points 6..7.</summary>
        private static List<Point3D> SplitHook() => new List<Point3D>
        {
            new Point3D(0, 0, Z), new Point3D(4.5, 0, Z), new Point3D(4.5, 4.2, Z), new Point3D(3.7, 4.2, Z),
            new Point3D(3.8, 4.16, Z), new Point3D(4.3, 4.0, Z), new Point3D(4, 4.0, Z), new Point3D(0, 4.0, Z),
        };

        /// <summary>True when no two non-adjacent edges of the external loop touch or cross (exact plan test).</summary>
        private static bool PlanIsSimple(Face3D face3D)
        {
            List<Point3D> p = SnappedPanel.BoundaryPoints(face3D);
            int n = p.Count;
            double Cross(Point3D o, Point3D a, Point3D b) => ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
            bool OnSegment(Point3D a, Point3D b, Point3D q) => System.Math.Abs(Cross(a, b, q)) <= 1e-12
                && q.X >= System.Math.Min(a.X, b.X) - 1e-12 && q.X <= System.Math.Max(a.X, b.X) + 1e-12
                && q.Y >= System.Math.Min(a.Y, b.Y) - 1e-12 && q.Y <= System.Math.Max(a.Y, b.Y) + 1e-12;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (j == i + 1 || (i == 0 && j == n - 1))
                    {
                        continue; // adjacent edges share an end point
                    }

                    Point3D a = p[i], b = p[(i + 1) % n], c = p[j], d = p[(j + 1) % n];
                    double d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
                    bool proper = ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
                    if (proper || OnSegment(c, d, a) || OnSegment(c, d, b) || OnSegment(a, b, c) || OnSegment(a, b, d))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsFlat(Face3D face3D) => face3D.GetBoundingBox().Max.Z - face3D.GetBoundingBox().Min.Z < 1e-9;

        private static void AssertBox(Face3D face3D, double x0, double y0, double x1, double y1)
        {
            BoundingBox3D box = face3D.GetBoundingBox();
            Assert.Equal(x0, box.Min.X, 9);
            Assert.Equal(y0, box.Min.Y, 9);
            Assert.Equal(x1, box.Max.X, 9);
            Assert.Equal(y1, box.Max.Y, 9);
        }

        private static Polygon3D Loop(params (double X, double Y, double Z)[] points) => new Polygon3D(points.Select(p => new Point3D(p.X, p.Y, p.Z)).ToList());

        private static Face3D Poly(params (double X, double Y)[] points) => Face3D.Create(new List<IClosedPlanar3D> { new Polygon3D(points.Select(p => new Point3D(p.X, p.Y, Z)).ToList()) });

        private static Face3D Rect(double x0, double y0, double x1, double y1, (double, double, double, double)? hole = null, double z = Z)
        {
            List<IClosedPlanar3D> loops = new List<IClosedPlanar3D> { Loop((x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)) };
            if (hole.HasValue)
            {
                (double a, double b, double c, double d) = hole.Value;
                loops.Add(Loop((a, b, z), (a, d, z), (c, d, z), (c, b, z)));
            }

            return Face3D.Create(loops);
        }

        private static Face3D WallY(double y, double x0, double x1, double z0, double z1) => Face3D.Create(new List<IClosedPlanar3D> { Loop((x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)) });

        private static Face3D WallX(double x, double y0, double y1, double z0, double z1) => Face3D.Create(new List<IClosedPlanar3D> { Loop((x, y0, z0), (x, y1, z0), (x, y1, z1), (x, y0, z1)) });
    }
}
