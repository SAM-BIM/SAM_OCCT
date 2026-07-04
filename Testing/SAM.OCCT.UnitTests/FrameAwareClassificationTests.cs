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
    /// Unit tests for Phase 6b frame-aware wall/cap/vertical classification: the frame-aware
    /// <see cref="LevelFrame.IsWall(Face3D, double)"/> / <see cref="LevelFrame.IsCap(Face3D, double)"/> /
    /// <see cref="LevelFrame.IsVertical(Face3D, double)"/> helpers, the <see cref="LevelFrame.ClassifyFace"/>
    /// multi-frame classifier, and the behaviour-preserving <see cref="SnappedPanel.IsVertical(double, Vector3D)"/>
    /// overload (docs/TRUE_3D_PANEL_SOLVER_IMPLEMENTATION_PLAN.md §E Phase 6). Pure-managed.
    /// </summary>
    public class FrameAwareClassificationTests
    {
        private const double Tilt = 25.0 * (System.Math.PI / 180.0); // past the 20° world-frame ceiling
        private static readonly double VertTol = LevelFrame.DEFAULT_VerticalAngleTolerance; // 20°

        /// <summary>A cap tile on a level tilted <see cref="Tilt"/> about X: plane normal (0, -sin, cos).</summary>
        private static Face3D TiltedCap()
        {
            double c = System.Math.Cos(Tilt);
            double s = System.Math.Sin(Tilt);
            return TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, c, s), new Point3D(0, c, s));
        }

        /// <summary>
        /// A wall of that same tilted level: it stands up along the level's up-slope direction (0,-sin,cos),
        /// so its normal is (0, cos, sin) - |normal.Z| = sin(25°) ≈ 0.42, past sin(20°) ≈ 0.34. World-Z
        /// verticality therefore MIScalls it a cap; the level frame classifies it correctly as a wall.
        /// </summary>
        private static Face3D TiltedWall()
        {
            double c = System.Math.Cos(Tilt);
            double s = System.Math.Sin(Tilt);
            return TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, 0), new Point3D(2, 0, 0), new Point3D(2, -3 * s, 3 * c), new Point3D(0, -3 * s, 3 * c));
        }

        /// <summary>A horizontal cap (normal +Z) at elevation z.</summary>
        private static Face3D FlatCap(double z)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, z), new Point3D(2, 0, z), new Point3D(2, 2, z), new Point3D(0, 2, z));
        }

        /// <summary>A world-vertical wall in the plane x = <paramref name="x"/> (normal ±X).</summary>
        private static Face3D WorldWall(double x)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(x, 0, 0), new Point3D(x, 2, 0), new Point3D(x, 2, 3), new Point3D(x, 0, 3));
        }

        private static LevelFrame TiltedFrame()
        {
            return LevelFrame.Cluster(new List<Face3D> { TiltedCap() }).Single();
        }

        private static LevelFrame FlatFrame()
        {
            return LevelFrame.Cluster(new List<Face3D> { FlatCap(0) }).Single();
        }

        [Fact]
        public void IsWall_WallOnTiltedLevel_ClassifiedAsWallInFrame()
        {
            // Arrange
            LevelFrame frame = TiltedFrame();

            // Act & Assert - the tilted-level wall reads as a wall in its own frame.
            Assert.True(frame.IsWall(TiltedWall(), VertTol));
            Assert.False(frame.IsCap(TiltedWall(), VertTol));
        }

        [Fact]
        public void IsCap_CapOnTiltedLevel_ClassifiedAsCap()
        {
            // Arrange
            LevelFrame frame = TiltedFrame();

            // Act & Assert - the tilted cap reads as a cap in its own frame.
            Assert.True(frame.IsCap(TiltedCap(), VertTol));
            Assert.False(frame.IsWall(TiltedCap(), VertTol));
        }

        [Fact]
        public void IsWall_TiltedLevelBeyond20Degrees_FixesWorldFrameMisclassification()
        {
            // Arrange - the key Phase 6 fix: a wall on a >20° level.
            LevelFrame frame = TiltedFrame();
            Face3D wall = TiltedWall();
            SnappedPanel panel = new SnappedPanel(0, wall, 1.0, 0.3, 0.4);

            // Act
            bool worldZ = panel.IsVertical(VertTol);                 // legacy world-Z test
            bool frameAware = frame.IsWall(wall, VertTol);           // frame-aware test

            // Assert - world Z misclassifies the tilted wall (not vertical), the frame gets it right.
            Assert.False(worldZ);
            Assert.True(frameAware);
            Assert.True(panel.IsVertical(VertTol, frame.Normal)); // the SnappedPanel frame overload agrees
        }

        [Fact]
        public void IsVertical_FlatFrame_MatchesLegacyWorldZ()
        {
            // Arrange - a flat frame classifies exactly as the world-Z test on a normal (flat) model.
            LevelFrame frame = FlatFrame();
            Face3D wall = WorldWall(1);
            Face3D cap = FlatCap(3);
            SnappedPanel wallPanel = new SnappedPanel(0, wall, 1.0, 0.3, 0.4);
            SnappedPanel capPanel = new SnappedPanel(1, cap, 1.0, 0.3, 0.4);

            // Act & Assert
            Assert.Equal(wallPanel.IsVertical(VertTol), frame.IsWall(wall, VertTol));
            Assert.Equal(capPanel.IsVertical(VertTol), frame.IsWall(cap, VertTol));
            Assert.True(frame.IsWall(wall, VertTol));
            Assert.True(frame.IsCap(cap, VertTol));
        }

        [Fact]
        public void IsVertical_WorldZOverload_ByteIdenticalToNoArg()
        {
            // Arrange - the behaviour-preserving refactor: the world-Z overload equals the legacy no-arg for
            // a wall, a cap and a tilted panel.
            foreach (Face3D face in new[] { WorldWall(0), FlatCap(0), TiltedWall(), TiltedCap() })
            {
                SnappedPanel panel = new SnappedPanel(0, face, 1.0, 0.3, 0.4);

                // Act & Assert
                Assert.Equal(panel.IsVertical(VertTol), panel.IsVertical(VertTol, new Vector3D(0, 0, 1)));
            }
        }

        [Fact]
        public void ClassifyFace_WallOnTiltedLevel_ReturnsWallAndNamesFrame()
        {
            // Arrange
            List<LevelFrame> frames = new List<LevelFrame> { TiltedFrame() };
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            FaceRole role = LevelFrame.ClassifyFace(TiltedWall(), frames, out int frameIndex, VertTol, diagnostics);

            // Assert - classified as a wall in frame 0, with a diagnostic naming the (tilted) frame used.
            Assert.Equal(FaceRole.Wall, role);
            Assert.Equal(0, frameIndex);
            Assert.Contains(diagnostics.All, x => x.Message.Contains("classified") && x.Message.Contains("Wall"));
        }

        [Fact]
        public void ClassifyFace_CapOnTiltedLevel_ReturnsCap()
        {
            // Arrange
            List<LevelFrame> frames = new List<LevelFrame> { TiltedFrame() };

            // Act
            FaceRole role = LevelFrame.ClassifyFace(TiltedCap(), frames, out int frameIndex, VertTol);

            // Assert
            Assert.Equal(FaceRole.Cap, role);
            Assert.Equal(0, frameIndex);
        }

        [Fact]
        public void ClassifyFace_NoFrames_FallsBackToWorldZ()
        {
            // Arrange - the fallback: with no frames, classification is the pre-Phase-6 world-Z test.
            List<LevelFrame> none = new List<LevelFrame>();

            // Act
            FaceRole wallRole = LevelFrame.ClassifyFace(WorldWall(0), none, out int wallFrame, VertTol);
            FaceRole capRole = LevelFrame.ClassifyFace(FlatCap(0), none, out int capFrame, VertTol);

            // Assert
            Assert.Equal(FaceRole.Wall, wallRole);
            Assert.Equal(FaceRole.Cap, capRole);
            Assert.Equal(-1, wallFrame);
            Assert.Equal(-1, capFrame);
        }

        [Fact]
        public void ClassifyFace_FlatModel_MatchesWorldZAndStaysQuiet()
        {
            // Arrange - a flat level; classification must match world Z and emit no tilted-frame diagnostic.
            List<LevelFrame> frames = new List<LevelFrame> { FlatFrame() };
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            FaceRole wallRole = LevelFrame.ClassifyFace(WorldWall(1), frames, out int _, VertTol, diagnostics);
            FaceRole capRole = LevelFrame.ClassifyFace(FlatCap(3), frames, out int _, VertTol, diagnostics);

            // Assert - correct roles, and no "classified ... tilted" diagnostic on a flat model.
            Assert.Equal(FaceRole.Wall, wallRole);
            Assert.Equal(FaceRole.Cap, capRole);
            Assert.DoesNotContain(diagnostics.All, x => x.Message.Contains("classified"));
        }

        [Fact]
        public void ClassifyFace_TiltedFrame_EmitsFrameUsedDiagnostic()
        {
            // Arrange
            List<LevelFrame> frames = new List<LevelFrame> { TiltedFrame() };
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            LevelFrame.ClassifyFace(TiltedWall(), frames, out int _, VertTol, diagnostics);

            // Assert - "diagnostics identify the frame used where useful" (a tilted frame).
            Assert.Contains(diagnostics.All, x => x.Code == DiagnosticCode.AdoptedLevel && x.Message.Contains("tilted level frame"));
        }
    }
}
