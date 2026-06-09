// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare a static Query class.
using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// End-to-end tests for the native ShellsRepair tiny-face clean-up (issue #11).
    /// They drive the real OCCT defeaturing path and auto-skip (via SkippableFact)
    /// when the native library is absent.
    /// </summary>
    public class ShellsRepairIntegrationTests
    {
        /// <summary>
        /// A unit box whose top (z = 1) is split into a large face and a thin strip
        /// of width <paramref name="stripWidth"/> (so a 0.01 m strip => 0.01 m² face).
        /// </summary>
        private static Shell CreateUnitBoxWithSplitTop(double stripWidth)
        {
            double yStrip = 1.0 - stripWidth;

            List<Face3D> face3Ds = new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(1, 1, 0), new Point3D(0, 1, 0)), // bottom
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 1), new Point3D(1, 0, 1), new Point3D(1, yStrip, 1), new Point3D(0, yStrip, 1)), // top (large)
                TestGeometry.CreatePlanarFace(new Point3D(0, yStrip, 1), new Point3D(1, yStrip, 1), new Point3D(1, 1, 1), new Point3D(0, 1, 1)), // top (thin strip)
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(1, 0, 0), new Point3D(1, 0, 1), new Point3D(0, 0, 1)), // front (y0)
                TestGeometry.CreatePlanarFace(new Point3D(0, 1, 0), new Point3D(1, 1, 0), new Point3D(1, 1, 1), new Point3D(0, 1, 1)), // back (y1)
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(0, 1, 0), new Point3D(0, 1, 1), new Point3D(0, 0, 1)), // left (x0)
                TestGeometry.CreatePlanarFace(new Point3D(1, 0, 0), new Point3D(1, 1, 0), new Point3D(1, 1, 1), new Point3D(1, 0, 1))  // right (x1)
            };

            return new Shell(face3Ds);
        }

        [SkippableFact]
        public void ShellsRepair_CleanBox_KeepsClosedShellWithoutTinyFaces()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a clean unit box; every face is 1 m².
            List<Shell> shells = new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) };

            // Act - repair with a 0.01 m² minimum face area.
            List<Shell> result_Shells = GeometryQuery.ShellsRepair(shells, out OcctCellComplexResult result, new OcctBuildOptions(), 0.01);

            // Assert - the box survives unchanged: one closed cell, unit volume, no tiny faces.
            Assert.True(result.NativeAvailable);
            Assert.True(result.Success);
            Assert.NotNull(result_Shells);
            Assert.Single(result_Shells);
            Assert.True(Math.Abs(result.Cells.Sum(x => Math.Abs(x.Volume)) - 1.0) < 1e-3);
            Assert.All(result_Shells, shell => Assert.All(shell.Face3Ds, face => Assert.True(face.GetArea() >= 0.01)));
        }

        [SkippableFact]
        public void ShellsRepair_BoxWithSliverTopFace_RemovesSliverAndPreservesVolume()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a unit box carrying a 0.01 m² sliver strip on its top face.
            List<Shell> shells = new List<Shell> { CreateUnitBoxWithSplitTop(0.01) };

            // Act - minArea above the strip so it must be defeatured away.
            List<Shell> result_Shells = GeometryQuery.ShellsRepair(shells, out OcctCellComplexResult result, new OcctBuildOptions(), 0.02);

            // Assert - the shell stays closed (volume preserved) and no sub-threshold face remains.
            Assert.True(result.Success);
            Assert.NotNull(result_Shells);
            Assert.Single(result_Shells);

            double volume = result.Cells.Sum(x => Math.Abs(x.Volume));
            Assert.True(Math.Abs(volume - 1.0) < 1e-3, string.Format("Volume should be preserved (~1.0), was {0}.", volume));

            Assert.All(result_Shells, shell => Assert.All(shell.Face3Ds, face => Assert.True(face.GetArea() >= 0.02, string.Format("Face area {0} should be >= 0.02 after defeaturing.", face.GetArea()))));
        }
    }
}
