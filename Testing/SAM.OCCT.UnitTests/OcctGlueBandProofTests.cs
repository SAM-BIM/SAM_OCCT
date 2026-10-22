// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Native;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// R4: the glue-band decision for a common piece that the piece tests leave undecided, made on the two cells by
    /// <see cref="OcctGlueBandProof"/> (managed). Two cells overlap when their common region holds a ball of diameter
    /// greater than the band b (1 mm here). Thresholds, from the analytic inradius:
    /// <list type="bullet">
    /// <item>a right isosceles triangular prism with legs s holds a ball of diameter 2s/(2 + sqrt 2) = 0.586 s, so the
    /// corner prism of the review (s = 2 mm, 1.17 mm ball) is an overlap; s* = 1.7071 mm;</item>
    /// <item>an L-shaped seam of width t holds a ball of diameter 2 sqrt 2 t/(1 + sqrt 2) = 1.1716 t at its reflex
    /// corner, so an L seam is an overlap once t &gt; t* = 0.8536 mm, below the band itself.</item>
    /// </list>
    /// </summary>
    public class OcctGlueBandProofTests
    {
        private const double Band = 1e-3;

        // The region margin CommonVolume passes: the fine kernel pass (b/10) plus Tolerance.
        private const double Margin = 1e-4 + 1e-6;

        private static OcctBuildOptions Options()
        {
            return new OcctBuildOptions { Tolerance = 1e-6, FuzzyTolerance = Band, SewBeforeBuild = true, ValidateSewCoverage = true };
        }

        public static IEnumerable<object[]> RigidMotions()
        {
            yield return new object[] { "identity" };
            yield return new object[] { "rotate Z 45" };
            yield return new object[] { "rotate oblique" };
            yield return new object[] { "rotate oblique, translate" };
        }

        private static Shell Moved(Shell shell, string motion)
        {
            // Oblique: about Z, then X, then Y (each elementary rotation is orthonormal).
            Transform3D[] oblique = { Transform3D.GetRotationZ(0.6), Transform3D.GetRotationX(0.5), Transform3D.GetRotationY(0.4) };
            List<Transform3D> transforms = motion == "identity" ? new List<Transform3D>()
                : motion == "rotate Z 45" ? new List<Transform3D> { Transform3D.GetRotationZ(Math.PI / 4) }
                : motion == "rotate oblique" ? oblique.ToList()
                : oblique.Append(Transform3D.GetTranslation(100, -50, 7)).ToList();
            foreach (Transform3D transform3D in transforms)
            {
                shell = (Shell)shell.GetTransformed(transform3D);
            }

            return shell;
        }

        // A vertical prism over the footprint, from z0 to z1.
        private static Shell Prism(Point3D[] footprint, double z0, double z1)
        {
            List<Face3D> faces = new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(footprint.Select(x => new Point3D(x.X, x.Y, z0)).ToArray()),
                TestGeometry.CreatePlanarFace(footprint.Select(x => new Point3D(x.X, x.Y, z1)).ToArray()),
            };
            for (int i = 0; i < footprint.Length; i++)
            {
                Point3D a = footprint[i];
                Point3D b = footprint[(i + 1) % footprint.Length];
                faces.Add(TestGeometry.CreatePlanarFace(new Point3D(a.X, a.Y, z0), new Point3D(b.X, b.Y, z0), new Point3D(b.X, b.Y, z1), new Point3D(a.X, a.Y, z1)));
            }

            return new Shell(faces);
        }

        private static Shell Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            return Prism(new[] { new Point3D(x0, y0, 0), new Point3D(x1, y0, 0), new Point3D(x1, y1, 0), new Point3D(x0, y1, 0) }, z0, z1);
        }

        // The review's R4 pair: the unit cube and the prism over (1 - s, 1), (1, 1 - s), (2, 2) from z = 0.998 to 2;
        // their common region is the corner prism over (1 - s, 1), (1, 1 - s), (1, 1), z 0.998..1.
        private static Shell[] CornerTriangle(double side)
        {
            return new[]
            {
                Box(0, 0, 0, 1, 1, 1),
                Prism(new[] { new Point3D(1 - side, 1, 0), new Point3D(1, 1 - side, 0), new Point3D(2, 2, 0) }, 0.998, 2),
                Prism(new[] { new Point3D(1 - side, 1, 0), new Point3D(1, 1 - side, 0), new Point3D(1, 1, 0) }, 0.998, 1),
            };
        }

        // A unit-height L (x 0..2 by y 0..1 plus x 0..1 by y 1..2) and a box filling its notch shifted by -t in x and
        // y: their common region is the L-shaped seam of width t.
        private static Shell[] LNotch(double width)
        {
            Shell lShape = Prism(new[] { new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, 1, 0), new Point3D(1, 1, 0), new Point3D(1, 2, 0), new Point3D(0, 2, 0) }, 0, 1);
            Shell piece = Prism(new[] { new Point3D(1 - width, 1 - width, 0), new Point3D(2, 1 - width, 0), new Point3D(2, 1, 0), new Point3D(1, 1, 0), new Point3D(1, 2, 0), new Point3D(1 - width, 2, 0) }, 0, 1);
            return new[] { lShape, Box(1 - width, 1 - width, 0, 2, 2, 1), piece };
        }

        private static bool? Exceeds(Shell[] shells, string motion, int budget = OcctGlueBandProof.DefaultBudget)
        {
            return OcctGlueBandProof.Exceeds(Moved(shells[0], motion), Moved(shells[1], motion), Moved(shells[2], motion), Margin, Options(), budget);
        }

        public static IEnumerable<object[]> CornerTriangles()
        {
            foreach (object[] motion in RigidMotions())
            {
                yield return new object[] { motion[0], 1.6e-3, false };
                yield return new object[] { motion[0], 1.7e-3, false };
                yield return new object[] { motion[0], 1.75e-3, true };
                yield return new object[] { motion[0], 1.8e-3, true };
                yield return new object[] { motion[0], 2e-3, true };
                yield return new object[] { motion[0], 3e-3, true };
            }
        }

        [Theory]
        [MemberData(nameof(CornerTriangles))]
        public void Exceeds_CornerTrianglePrismUnderRigidMotion_DecidedByBallDiameter(string motion, double side, bool expected)
        {
            // Arrange: below s* the inscribed ball is at most the band; above it, wider (2 mm: the R4 reproduction).
            Shell[] shells = CornerTriangle(side);

            // Act
            bool? exceeds = Exceeds(shells, motion);

            // Assert: a sound decision either way, in every pose; never undecided.
            Assert.True(exceeds == expected, string.Format(System.Globalization.CultureInfo.InvariantCulture, "side {0} ({1}): {2}", side, motion, exceeds));
        }

        public static IEnumerable<object[]> LNotchSeams()
        {
            foreach (object[] motion in RigidMotions())
            {
                yield return new object[] { motion[0], 0.2e-3, false };
                yield return new object[] { motion[0], 0.5e-3, false };
                yield return new object[] { motion[0], 0.8e-3, false };
                yield return new object[] { motion[0], 0.85e-3, false };
                yield return new object[] { motion[0], 0.86e-3, true };
                yield return new object[] { motion[0], 0.9e-3, true };
                yield return new object[] { motion[0], 1e-3, true };
                yield return new object[] { motion[0], 1.5e-3, true };
            }
        }

        [Theory]
        [MemberData(nameof(LNotchSeams))]
        public void Exceeds_LShapedSeamUnderRigidMotion_DecidedByBallDiameter(string motion, double width, bool expected)
        {
            // Arrange: an L seam of width t holds a 1.17 t ball at its reflex corner. Before R4 the kernel at the band
            // glued L seams of up to 1 mm away and they were accepted; 0.86 to 1 mm are overlaps by the criterion.
            Shell[] shells = LNotch(width);

            // Act
            bool? exceeds = Exceeds(shells, motion);

            // Assert
            Assert.True(exceeds == expected, string.Format(System.Globalization.CultureInfo.InvariantCulture, "width {0} ({1}): {2}", width, motion, exceeds));
        }

        public static IEnumerable<object[]> TiltedLNotchSeams()
        {
            // Notch box turned about the reflex edge (1, 1): the seam widens by the angle times 1 m at one leg end.
            foreach (string motion in new[] { "identity", "rotate oblique, translate" })
            {
                yield return new object[] { motion, 1e-6, 0.5e-3, false };
                yield return new object[] { motion, 1e-4, 0.8e-3, false };
                yield return new object[] { motion, 1e-4, 0.9e-3, true };
                yield return new object[] { motion, 3e-4, 0.8e-3, true };
            }
        }

        [Theory]
        [MemberData(nameof(TiltedLNotchSeams))]
        public void Exceeds_TiltedLShapedSeam_DecidedByBallDiameter(string motion, double angle, double width, bool expected)
        {
            // Arrange: no face of the notch box is parallel to the L (decoded geometry is never exactly aligned).
            // 1e-4 rad and 0.8 mm: at most 0.9 mm wide (seam); 3e-4 rad: 1.1 mm at one end (overlap). The piece
            // passed is a covering L region, 0.4 mm wider than the seam.
            Func<double, double, Point3D> turned = (x, y) => new Point3D(1 + (x - 1) * Math.Cos(angle) - (y - 1) * Math.Sin(angle), 1 + (x - 1) * Math.Sin(angle) + (y - 1) * Math.Cos(angle), 0);
            Shell notchBox = Prism(new[] { turned(1 - width, 1 - width), turned(2, 1 - width), turned(2, 2), turned(1 - width, 2) }, 0, 1);
            double w = width + 0.4e-3;
            Shell region = Prism(new[] { new Point3D(1 - w, 1 - w, 0), new Point3D(2, 1 - w, 0), new Point3D(2, 1, 0), new Point3D(1, 1, 0), new Point3D(1, 2, 0), new Point3D(1 - w, 2, 0) }, 0, 1);
            Shell[] shells = { LNotch(width)[0], notchBox, region };

            // Act
            bool? exceeds = Exceeds(shells, motion);

            // Assert
            Assert.True(exceeds == expected, string.Format(System.Globalization.CultureInfo.InvariantCulture, "angle {0}, width {1} ({2}): {3}", angle, width, motion, exceeds));
        }

        [Fact]
        public void CornerTriangle_ReviewWitness_LiesDeeperThanHalfBandInBothCells()
        {
            // Arrange (independent of the proof): the analytic witness of the review, the incentre of the 2 mm
            // corner prism, (1 - r, 1 - r, 0.999) with r = 0.002 / (2 + sqrt 2) = 0.5858 mm.
            Shell[] shells = CornerTriangle(2e-3);
            double radius = 2e-3 / (2 + Math.Sqrt(2));
            Point3D witness = new Point3D(1 - radius, 1 - radius, 0.999);

            // Act
            double[] depths = shells.Take(2).Select(x => x.Face3Ds.Min(y => y.Distance(witness))).ToArray();

            // Assert: inside both cells and more than b/2 from both boundaries (review: 0.58578643762686 mm each).
            Assert.All(shells.Take(2), x => Assert.True(x.Inside(witness, 1e-6, 1e-9)));
            Assert.All(depths, x => Assert.Equal(radius, x, 12));
            Assert.All(depths, x => Assert.True(x > Band / 2));
        }

        [Fact]
        public void Exceeds_SlabExactlyOneBandThick_IsSeam()
        {
            // Arrange: x 0..1 and 0.999..2 (a slab exactly b thick, up to rounding: a ball of diameter b, not more).
            Shell[] shells = { Box(0, 0, 0, 1, 1, 1), Box(1 - Band, 0, 0, 2, 1, 1), Box(1 - Band, 0, 0, 1, 1, 1) };

            // Act
            bool? exceeds = Exceeds(shells, "rotate oblique");

            // Assert
            Assert.False(exceeds);
        }

        [Fact]
        public void Exceeds_BudgetSpent_ReturnsNullNotSeam()
        {
            // Arrange: the 0.5 mm L seam needs more than one box.
            Shell[] shells = LNotch(0.5e-3);

            // Act
            bool? exceeds = Exceeds(shells, "identity", budget: 1);

            // Assert: undecided, which the guard treats as a failure.
            Assert.Null(exceeds);
        }

        [Fact]
        public void Exceeds_OpenCell_ReturnsNull()
        {
            // Arrange: the corner-prism pair with the cube's top face removed (ray parity needs closed cells).
            Shell[] shells = CornerTriangle(1.6e-3);
            Shell open = new Shell(shells[0].Face3Ds.Where(x => x.GetPlane().Origin.Z < 1 - 1e-9 || System.Math.Abs(x.GetPlane().Normal.Z) < 0.5).ToList());
            Assert.Equal(5, open.Face3Ds.Count);

            // Act
            bool? exceeds = OcctGlueBandProof.Exceeds(open, shells[1], shells[2], Margin, Options());

            // Assert
            Assert.Null(exceeds);
        }
    }
}
