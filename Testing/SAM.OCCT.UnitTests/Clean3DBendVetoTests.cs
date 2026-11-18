// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static SAM.OCCT.UnitTests.WallFixtures;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// The rule (b) bend veto through managed <c>Modify.Clean3D</c>, with the authored geometry as the oracle and each fixture exercised for
    /// winding reversal, loop-start rotation and panel order independently (every weight and reach kept on its physical wall). A genuine
    /// bend - the 90 mm junction block and the first-floor piece over the joint - keeps its authored geometry; the controls are unchanged:
    /// a noisy straight partition still straightens onto its existing plane, and an unconnected jog still aligns.
    /// </summary>
    public class Clean3DBendVetoTests
    {
        private const double BX = 0.145;

        private static Piece[] Fixture(string name)
        {
            switch (name)
            {
                case "block": // an angled piece, a 90 mm junction block, then the straight run in two pieces
                    return new[] { Piece.Wall("angled", 0, 0, BX - 1e-4, 3.848, 0, 3, 1.0), Piece.Wall("block", BX, 3.85, BX, 3.94, 0, 3, 0.1), Piece.Wall("run1", BX, 3.94, BX, 5.7, 0, 3, 0.5), Piece.Wall("run2", BX, 5.7, BX, 10, 0, 3, 0.9) };
                case "firstfloor": // the same ground floor; on the first floor a piece over the joint, 25 mm into the angled piece's band, and its continuation
                    return new[]
                    {
                        Piece.Wall("angled", 0, 0, BX - 1e-4, 3.848, 0, 3.025, 1.0), Piece.Wall("block", BX, 3.85, BX, 3.94, 0, 3, 0.1), Piece.Wall("run1", BX, 3.94, BX, 5.7, 0, 3, 0.5),
                        Piece.Wall("over", BX, 3.49, BX, 3.94, 3, 6, 0.24), Piece.Wall("continuation", BX, 3.94, BX, 10, 3.025, 6, 0.65),
                    };
                case "skew": // a 6 m straight partition whose joint at x = 1 m is 5 mm off the axis; the last segment has the highest weight
                    return new[] { Piece.Wall("first", 0, 0, 1, 0.005, 0, 3, 0.5), Piece.Wall("middle", 1, 0.005, 3, 0, 0, 3, 0.8), Piece.Wall("last", 3, 0, 6, 0, 0, 3, 1.0) };
                case "jog": // two unconnected parallel pieces 0.1 m apart
                    return new[] { Piece.Wall("heavy", 0, 0, 0, 4, 0, 3, 1.0), Piece.Wall("jog", 0.1, 4, 0.1, 6, 0, 3, 0.5) };
                default:
                    throw new System.ArgumentException(name);
            }
        }

        private static List<string> Judge(string fixture, List<Panel> clean)
        {
            List<string> failures = new List<string>();
            List<Point3D> all = clean.SelectMany(p => Points(p.GetFace3D())).ToList();
            if (all.Count == 0)
            {
                failures.Add("empty result");
                return failures;
            }

            if (fixture == "skew")
            {
                List<(double Min, double Max)> spans = clean.Select(p => Points(p.GetFace3D())).Where(f => f.Count > 0).Select(f => (f.Min(q => q.X), f.Max(q => q.X))).OrderBy(s => s.Item1).ToList();
                double covered = 0;
                foreach ((double min, double max) in spans)
                {
                    if (min > covered + Tol)
                    {
                        failures.Add("coverage gap before x = " + F(min));
                        break;
                    }

                    covered = System.Math.Max(covered, max);
                }

                if (covered < 6 - Tol)
                {
                    failures.Add("run short at x = " + F(covered));
                }

                foreach (double x in new[] { 0.0, 6.0 })
                {
                    foreach (double z in new[] { 0.0, 3.0 })
                    {
                        if (!all.Any(q => Near(q, x, 0, z)))
                        {
                            failures.Add("exterior endpoint (" + F(x) + ",0," + F(z) + ") not retained");
                        }
                    }
                }

                failures.AddRange(all.Where(q => System.Math.Abs(q.Y) > Tol).Take(1).Select(q => "vertex off the authored axis: " + V(q)));
            }
            else if (fixture == "jog")
            {
                failures.AddRange(all.Where(q => System.Math.Abs(q.X) > Tol).Take(1).Select(q => "jog not aligned onto the heavier plane: " + V(q)));
                foreach (double y in new[] { 0.0, 6.0 })
                {
                    if (!all.Any(q => System.Math.Abs(q.X) <= Tol && System.Math.Abs(q.Y - y) <= Tol))
                    {
                        failures.Add("end (0," + F(y) + ") missing");
                    }
                }
            }
            else
            {
                List<Point3D> authored = Fixture(fixture).SelectMany(p => p.Loop()).ToList();
                failures.AddRange(authored.Where(a => !all.Any(q => Near(q, a.X, a.Y, a.Z))).Select(a => "authored vertex " + V(a) + " no longer present"));
                failures.AddRange(all.Where(q => !authored.Any(a => Near(q, a.X, a.Y, a.Z))).Select(q => "vertex " + V(q) + " is not an authored vertex"));
            }

            return failures.Distinct().ToList();
        }

        [Theory]
        [InlineData("block", "baseline")] [InlineData("block", "winding")] [InlineData("block", "rotation")] [InlineData("block", "reorder")]
        [InlineData("firstfloor", "baseline")] [InlineData("firstfloor", "winding")] [InlineData("firstfloor", "rotation")] [InlineData("firstfloor", "reorder")]
        [InlineData("skew", "baseline")] [InlineData("skew", "winding")] [InlineData("skew", "rotation")] [InlineData("skew", "reorder")]
        [InlineData("jog", "baseline")] [InlineData("jog", "winding")] [InlineData("jog", "rotation")] [InlineData("jog", "reorder")]
        public void Clean3D_WallFixtureInEveryPresentation_MeetsTheAuthoredOracle(string fixture, string axis)
        {
            // Arrange
            Piece[] pieces = Fixture(fixture);

            // Act
            List<string> failures = Presentations(axis, pieces)
                .Select(c => (c.Id, Failures: Judge(fixture, Clean(pieces, c.Order, c.Reversed, c.Start))))
                .Where(x => x.Failures.Count > 0).Select(x => x.Id + ": " + string.Join("; ", x.Failures)).ToList();

            // Assert
            Assert.True(failures.Count == 0, fixture + "/" + axis + ": " + failures.Count + " presentations failed. First: " + failures.FirstOrDefault());
        }
    }
}
