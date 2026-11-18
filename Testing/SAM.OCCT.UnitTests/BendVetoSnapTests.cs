// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static SAM.OCCT.UnitTests.WallFixtures;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// The Clean3D rule (b) bend veto at the snap pass (public API only). Each fixture is a genuine bend: a heavier angled piece (2.16
    /// degrees, the Softhouse A/B joint) and, beyond its end, straight pieces of the other wall line. A straight piece whose run end holds a
    /// finite stretch of a near-parallel third piece is kept on its plane; one that only touches a third piece at a corner is still rotated
    /// onto the angled piece, as before. Every fixture is exercised for winding, loop start and panel order independently, on a level and on
    /// a level tilted 10 degrees with the snap reading the model-up axis.
    /// </summary>
    public class BendVetoSnapTests
    {
        private const double BX = 0.145;
        private static readonly ToleranceBudget Tolerances = new ToleranceBudget();

        /// <summary>The heavier angled piece: from the origin to 2 mm short of the joint at (BX, 3.85).</summary>
        private static Piece Angled(double z1 = 3)
        {
            return Piece.Wall("angled", 0, 0, BX - 1e-4, 3.848, 0, z1, 1.0);
        }

        /// <summary>Fixture pieces, the indices that must keep their authored geometry and the indices that must move onto the angled piece.</summary>
        private static (Piece[] Pieces, int[] Keep, int[] Move) Fixture(string name)
        {
            switch (name)
            {
                case "block": // the Softhouse 90 mm junction block between the bend and a straight run of two pieces
                    return (new[] { Angled(), Piece.Wall("block", BX, 3.85, BX, 3.94, 0, 3, 0.1), Piece.Wall("run1", BX, 3.94, BX, 5.7, 0, 3, 0.5), Piece.Wall("run2", BX, 5.7, BX, 10, 0, 3, 0.9) }, new[] { 0, 1, 2, 3 }, new int[0]);
                case "firstfloor": // a first-floor piece over the joint (25 mm into the ground-floor band) and its first-floor continuation
                    return (new[] { Angled(3.025), Piece.Wall("over", BX, 3.49, BX, 3.94, 3, 6, 0.24), Piece.Wall("continuation", BX, 3.94, BX, 10, 3.025, 6, 0.65) }, new[] { 0, 1, 2 }, new int[0]);
                case "short": // the run end meets an 80 mm strip at 1.21..1.29 m, between the 0.15 m sample heights of the prototype
                    return (new[] { Angled(), Piece.Wall("run", BX, 3.85, BX, 5.7, 0, 3, 0.5), Piece.Wall("strip", BX, 5.7, BX, 7, 1.21, 1.29, 0.3) }, new[] { 0, 1, 2 }, new int[0]);
                case "corner": // the next piece sits on top of the run: it touches the run end at its top corner only
                    return (new[] { Angled(), Piece.Wall("run", BX, 3.85, BX, 5.7, 0, 3, 0.5), Piece.Wall("above", BX, 5.7, BX, 7, 3, 6, 0.3) }, new[] { 0, 2 }, new[] { 1 });
                case "notch": // a door-height run whose next piece is notched 0..2.1 m at the joint: they share the notch corner only
                    return (new[] { Angled(), Piece.Wall("run", BX, 3.85, BX, 5.7, 0, 2.1, 0.5), Piece.Shaped("notched", BX, 5.7, BX, 8, 0.3, (0, 2.1), (0.5, 2.1), (0.5, 0), (2.3, 0), (2.3, 3), (0, 3)) }, new[] { 0 }, new[] { 1 });
                case "notch-finite": // the same with a 1 m notch: 1.1 m of the run end is held
                    return (new[] { Angled(), Piece.Wall("run", BX, 3.85, BX, 5.7, 0, 2.1, 0.5), Piece.Shaped("notched", BX, 5.7, BX, 8, 0.3, (0, 1.0), (0.5, 1.0), (0.5, 0), (2.3, 0), (2.3, 3), (0, 3)) }, new[] { 0, 1, 2 }, new int[0]);
                case "lean": // the next piece leans 1 degree about the horizontal line through the run end's mid-height: they cross at z = 1.5 only
                    return (new[] { Angled(), Piece.Wall("run", BX, 3.85, BX, 5.7, 0, 3, 0.5), Piece.Leaning("leaning", BX, 5.7, 8, 0, 3, 1, 0.3) }, new[] { 0 }, new[] { 1 });
                default:
                    throw new System.ArgumentException(name);
            }
        }

        private static List<SnappedPanel> Snap(Piece[] pieces, int[] order, bool[] reversed, int[] start, double tilt)
        {
            List<SnappedPanel> panels = order.Select(i => new SnappedPanel(i, TestGeometry.CreatePlanarFace(Present(pieces[i].Loop(), reversed[i], start[i]).Select(p => Tilt(p, tilt)).ToArray()), pieces[i].Weight, 0.4, pieces[i].MaxExt)).ToList();
            SnapStage.SnapToFixedPoint(panels, Tolerances, 0.3, null, 20, null, TiltedUp(tilt));
            return panels;
        }

        private static List<string> Judge(Piece[] pieces, int[] keep, int[] move, List<SnappedPanel> panels, double tilt)
        {
            List<string> failures = new List<string>();
            Plane angled = TestGeometry.CreatePlanarFace(pieces[0].Loop().Select(p => Tilt(p, tilt)).ToArray()).GetPlane();
            foreach (SnappedPanel panel in panels)
            {
                int index = panel.SourceIndices[0];
                List<Point3D> points = Points(panel.Face3D);
                if (keep.Contains(index))
                {
                    List<Point3D> authored = pieces[index].Loop().Select(p => Tilt(p, tilt)).ToList();
                    foreach (Point3D q in points.Where(q => !authored.Any(a => Near(q, a.X, a.Y, a.Z))).Take(1))
                    {
                        failures.Add(pieces[index].Name + " left its authored geometry: " + V(q));
                    }
                }

                if (move.Contains(index))
                {
                    foreach (Point3D q in points.Where(q => angled.Distance(q) > Tol).Take(1))
                    {
                        failures.Add(pieces[index].Name + " not moved onto the angled piece: " + V(q) + " is " + F(angled.Distance(q)) + " m off");
                    }
                }
            }

            return failures;
        }

        [Theory]
        [InlineData("block", "baseline", 0)] [InlineData("block", "winding", 0)] [InlineData("block", "rotation", 0)] [InlineData("block", "reorder", 0)]
        [InlineData("block", "baseline", 10)] [InlineData("block", "winding", 10)] [InlineData("block", "rotation", 10)] [InlineData("block", "reorder", 10)]
        [InlineData("firstfloor", "baseline", 0)] [InlineData("firstfloor", "winding", 0)] [InlineData("firstfloor", "rotation", 0)] [InlineData("firstfloor", "reorder", 0)]
        [InlineData("firstfloor", "baseline", 10)] [InlineData("firstfloor", "winding", 10)] [InlineData("firstfloor", "rotation", 10)] [InlineData("firstfloor", "reorder", 10)]
        [InlineData("short", "baseline", 0)] [InlineData("short", "winding", 0)] [InlineData("short", "rotation", 0)] [InlineData("short", "reorder", 0)]
        [InlineData("short", "baseline", 10)] [InlineData("short", "winding", 10)] [InlineData("short", "rotation", 10)] [InlineData("short", "reorder", 10)]
        [InlineData("corner", "baseline", 0)] [InlineData("corner", "winding", 0)] [InlineData("corner", "rotation", 0)] [InlineData("corner", "reorder", 0)]
        [InlineData("corner", "baseline", 10)] [InlineData("corner", "winding", 10)] [InlineData("corner", "rotation", 10)] [InlineData("corner", "reorder", 10)]
        [InlineData("notch", "baseline", 0)] [InlineData("notch", "winding", 0)] [InlineData("notch", "rotation", 0)] [InlineData("notch", "reorder", 0)]
        [InlineData("notch", "baseline", 10)] [InlineData("notch", "winding", 10)] [InlineData("notch", "rotation", 10)] [InlineData("notch", "reorder", 10)]
        [InlineData("notch-finite", "baseline", 0)] [InlineData("notch-finite", "winding", 0)] [InlineData("notch-finite", "rotation", 0)] [InlineData("notch-finite", "reorder", 0)]
        [InlineData("notch-finite", "baseline", 10)] [InlineData("notch-finite", "winding", 10)] [InlineData("notch-finite", "rotation", 10)] [InlineData("notch-finite", "reorder", 10)]
        [InlineData("lean", "baseline", 0)] [InlineData("lean", "winding", 0)] [InlineData("lean", "rotation", 0)] [InlineData("lean", "reorder", 0)]
        [InlineData("lean", "baseline", 10)] [InlineData("lean", "winding", 10)] [InlineData("lean", "rotation", 10)] [InlineData("lean", "reorder", 10)]
        public void Snap_BendFixtureInEveryPresentation_KeepsHeldPiecesAndMovesFreeOnes(string fixture, string axis, double tilt)
        {
            // Arrange
            (Piece[] pieces, int[] keep, int[] move) = Fixture(fixture);

            // Act
            List<string> failures = Presentations(axis, pieces)
                .Select(c => (c.Id, Failures: Judge(pieces, keep, move, Snap(pieces, c.Order, c.Reversed, c.Start, tilt), tilt)))
                .Where(x => x.Failures.Count > 0).Select(x => x.Id + ": " + string.Join("; ", x.Failures)).ToList();

            // Assert
            Assert.True(failures.Count == 0, fixture + "/" + axis + "/tilt " + tilt + ": " + failures.Count + " presentations failed. First: " + failures.FirstOrDefault());
        }

        [Fact]
        public void Snap_RunEndCrossedByALeaningWallAtOneHeight_StillMovesTheRunOntoTheAngledPiece()
        {
            // Arrange: the reported case, through the snap pass itself (5 degrees, 0.3 degree arc, 1 um, 0.3 m alignment, 0.4 m bucket
            // and reach). The leaning wall touches the run end at z = 1.5 only, so nothing holds the run: it is free to snap, as on #84.
            Piece[] pieces = Fixture("lean").Pieces;
            List<SnappedPanel> panels = pieces.Select((p, i) => new SnappedPanel(i, TestGeometry.CreatePlanarFace(p.Loop()), p.Weight, 0.4, p.MaxExt)).ToList();
            Plane angled = panels[0].Plane;

            // Act: passes to the fixed point
            int passes = 0;
            while (passes < 20 && Panel3DSnapSolver.Snap(panels, 5 * (System.Math.PI / 180), 0.3 * (System.Math.PI / 180), 1e-6, 20 * (System.Math.PI / 180), 0.3, null))
            {
                passes++;
            }

            // Assert
            Assert.All(Points(panels[1].Face3D), q => Assert.True(angled.Distance(q) <= Tol, "run point " + V(q) + " is " + F(angled.Distance(q)) + " m off the angled piece"));
        }

        [Fact]
        public void Snap_EqualWeightBendPair_RefusesTheMidplaneMoveOfBoth()
        {
            // Arrange: the block fixture's run1 as heavy as the angled piece, so the pair would move to their midplane
            Piece[] pieces = Fixture("block").Pieces;
            pieces[2].Weight = pieces[0].Weight;
            int[] order = { 0, 1, 2, 3 };

            // Act
            List<SnappedPanel> panels = Snap(pieces, order, new bool[4], new int[4], 0);

            // Assert: neither the angled piece nor the run is translated or turned
            Assert.Empty(Judge(pieces, order, new int[0], panels, 0));
        }

        [Fact]
        public void Snap_EqualWeightPairWhoseHeldPieceIsTheBacker_StillMovesBoth()
        {
            // Arrange: the straight run (larger area, so the backer of the equal-weight pair) carries the continuation; the angled
            // candidate's run ends meet nothing but the run itself. The run is only translated (never refused) and the angled piece turns.
            Piece[] pieces =
            {
                Piece.Wall("angled", 0, 0, BX - 1e-4, 3.848, 0, 3, 1.0), Piece.Wall("run", BX, 3.85, BX, 9, 0, 3, 1.0),
                Piece.Wall("continuation", BX, 9, BX, 12, 0, 3, 0.5),
            };

            // Act
            List<SnappedPanel> panels = Snap(pieces, new[] { 0, 1, 2 }, new bool[3], new int[3], 0);

            // Assert: the run left x = BX (translated to the midplane) and the angled piece lies on the run's new plane
            Plane run = panels.Single(p => p.SourceIndices[0] == 1).Plane;
            Assert.True(run.Distance(new Point3D(BX, 5, 1)) > 1e-3, "the run was not translated");
            Assert.All(Points(panels.Single(p => p.SourceIndices[0] == 0).Face3D), q => Assert.True(run.Distance(q) <= Tol, V(q) + " is off the run's plane"));
        }

        [Fact]
        public void Snap_ParallelJogWithAContinuation_StillAligns()
        {
            // Arrange: a 0.1 m jog (no turn) whose far end meets a continuation: a translation is never refused
            Piece[] pieces = { Piece.Wall("heavy", 0, 0, 0, 4, 0, 3, 1.0), Piece.Wall("jog", 0.1, 4, 0.1, 6, 0, 3, 0.5), Piece.Wall("continuation", 0.1, 6, 0.1, 8, 0, 3, 0.3) };

            // Act
            List<SnappedPanel> panels = Snap(pieces, new[] { 0, 1, 2 }, new bool[3], new int[3], 0);

            // Assert
            Assert.All(Points(panels.Single(p => p.SourceIndices[0] == 1).Face3D), q => Assert.Equal(0, q.X, 6));
        }

        [Fact]
        public void Snap_OverlappingDoubleWallTurnedOneDegree_IsStillCollapsedByRuleA()
        {
            // Arrange: a second skin over the same 4 m, turned 1 degree, whose end meets a near-parallel continuation
            double dx = 4 * System.Math.Tan(System.Math.PI / 180);
            Piece[] pieces =
            {
                Piece.Wall("backer", 0, 0, 0, 4, 0, 3, 1.0), Piece.Wall("skin", 0.02, 0, 0.02 + dx, 4, 0, 3, 0.5),
                Piece.Wall("continuation", 0.02 + dx, 4, 0.02 + (1.5 * dx), 6, 0, 3, 0.3),
            };

            // Act
            List<SnappedPanel> panels = Snap(pieces, new[] { 0, 1, 2 }, new bool[3], new int[3], 0);

            // Assert
            Assert.All(Points(panels.Single(p => p.SourceIndices[0] == 1).Face3D), q => Assert.Equal(0, q.X, 6));
        }
    }
}
