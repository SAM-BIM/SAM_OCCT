// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare a static Query class.
using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Drives the real native OCCT plane sectioning: the shell faces plus a bounded
    /// plane patch go through one BOPAlgo_MakerVolume pass, replacing the managed
    /// SAM Shell.Section (issue #13). Auto-skips when the native library is absent.
    /// </summary>
    public class ShellSectionByPlanesIntegrationTests
    {
        [SkippableFact]
        public void ShellSectionByPlanes_BoxMidHeightPlane_ProducesTwoLevelsAndOneSectionFace()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - 1 x 1 x 2 box sectioned at z = 1.
            Shell shell = TestGeometry.CreateBox(0, 0, 0, 1, 1, 2);
            List<Plane> planes = new List<Plane> { new Plane(new Point3D(0.5, 0.5, 1), new Vector3D(0, 0, 1)) };

            // Act
            List<Shell> levels = GeometryQuery.ShellSectionByPlanes(shell, planes, out List<Face3D> sectionFace3Ds, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.True(result.NativeAvailable);
            Assert.True(result.Success);
            Assert.NotNull(levels);
            Assert.Equal(2, levels.Count);
            Assert.All(result.Cells, cell => Assert.Equal(1.0, cell.Volume, 2));
            Assert.NotNull(sectionFace3Ds);
            Assert.Single(sectionFace3Ds);
            Assert.Equal(1.0, sectionFace3Ds[0].GetArea(), 2);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SECTION_BUILD_SUCCESS");
        }

        [SkippableFact]
        public void ShellSectionByPlanes_TwoPlanes_ProducesThreeLevelsAndTwoSectionFaces()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - 1 x 1 x 3 box sectioned at z = 1 and z = 2.
            Shell shell = TestGeometry.CreateBox(0, 0, 0, 1, 1, 3);
            List<Plane> planes = new List<Plane>
            {
                new Plane(new Point3D(0.5, 0.5, 1), new Vector3D(0, 0, 1)),
                new Plane(new Point3D(0.5, 0.5, 2), new Vector3D(0, 0, 1))
            };

            // Act
            List<Shell> levels = GeometryQuery.ShellSectionByPlanes(shell, planes, out List<Face3D> sectionFace3Ds, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.True(result.NativeAvailable);
            Assert.NotNull(levels);
            Assert.Equal(3, levels.Count);
            Assert.NotNull(sectionFace3Ds);
            Assert.Equal(2, sectionFace3Ds.Count);
            Assert.All(sectionFace3Ds, face3D => Assert.Equal(1.0, face3D.GetArea(), 2));
        }

        [SkippableFact]
        public void ShellSectionByPlanes_PlaneOutsideShell_KeepsSingleCellWithoutSectionFaces()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - plane above the box, so nothing is cut.
            Shell shell = TestGeometry.CreateUnitBox(0, 0, 0);
            List<Plane> planes = new List<Plane> { new Plane(new Point3D(0.5, 0.5, 5), new Vector3D(0, 0, 1)) };

            // Act
            List<Shell> levels = GeometryQuery.ShellSectionByPlanes(shell, planes, out List<Face3D> sectionFace3Ds, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert
            Assert.True(result.NativeAvailable);
            Assert.NotNull(levels);
            Assert.Single(levels);
            Assert.NotNull(sectionFace3Ds);
            Assert.Empty(sectionFace3Ds);
        }
    }
}
