// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Core;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using Xunit;

using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Pure-managed tests that an OCCT-created space carries the SAME SpaceParameter.Area a SAM-created space
    /// does. Both repositories route through SAM's single shared calculation
    /// (Modify.UpdateFloorAreas -> Query.FloorArea), so equivalent geometry must produce an equal area rather
    /// than two independently-derived numbers.
    ///
    /// The ResolvedCellComplex overload is used because it is the OCCT creation path that needs no native
    /// kernel; the shell and panel overloads apply the same shared update from DirectAdjacencyCluster.
    /// </summary>
    public class SpaceFloorAreaConsistencyTests
    {
        private const double Length = 4;
        private const double Width = 3;
        private const double Height = 2;

        [Fact]
        public void AdjacencyCluster_HorizontalBox_OcctAreaMatchesSamArea()
        {
            List<Face3D> face3Ds = BoxFaces(Length, Width, Height);

            double area_Sam = SamArea(face3Ds, new Point3D(Length / 2, Width / 2, Height / 2));
            double area_Occt = OcctArea(face3Ds, new Point3D(Length / 2, Width / 2, Height / 2), out FloorAreaCalculationMethod method);

            Assert.Equal(Length * Width, area_Sam, 3);
            Assert.Equal(area_Sam, area_Occt, 3);
            Assert.Equal(FloorAreaCalculationMethod.GeometricalFloorPanels, method);
        }

        /// <summary>
        /// The ramp is the case where a plan-area shortcut and the canonical surface area diverge, so it is the
        /// real test of consistency: both repositories must report the sloped walking surface.
        /// </summary>
        [Fact]
        public void AdjacencyCluster_RampedFloor_OcctAreaMatchesSamSlopedArea()
        {
            double rise = 1;
            List<Face3D> face3Ds = RampFaces(Length, Width, rise, 3);

            double slopedArea = Width * Math.Sqrt((Length * Length) + (rise * rise));
            double projectedArea = Length * Width;

            double area_Sam = SamArea(face3Ds, new Point3D(Length / 2, Width / 2, 2));
            double area_Occt = OcctArea(face3Ds, new Point3D(Length / 2, Width / 2, 2), out FloorAreaCalculationMethod method);

            Assert.Equal(slopedArea, area_Sam, 3);
            Assert.Equal(area_Sam, area_Occt, 3);
            Assert.Equal(FloorAreaCalculationMethod.GeometricalFloorPanels, method);
            Assert.True(area_Occt > projectedArea, string.Format("OCCT ramp area {0} must exceed the projected area {1}, not collapse to it.", area_Occt, projectedArea));
        }

        [Fact]
        public void AdjacencyCluster_FromComplex_ReportsSpaceAreaDiagnostic()
        {
            List<Face3D> face3Ds = BoxFaces(Length, Width, Height);

            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.AdjacencyCluster(
                new List<Panel>(), Complex(face3Ds, new Point3D(Length / 2, Width / 2, Height / 2)), out List<string> diagnostics);

            Assert.NotNull(adjacencyCluster);
            Assert.Contains(diagnostics, x => x.Contains("SAM_OCCT_ANALYTICAL_SPACE_AREAS") && x.Contains("1 of 1 space(s)"));
        }

        // ---------------------------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------------------------

        private static double SamArea(List<Face3D> face3Ds, Point3D location)
        {
            AdjacencyCluster adjacencyCluster = global::SAM.Analytical.Create.AdjacencyCluster(
                new List<Shell> { new Shell(face3Ds) },
                new List<Space> { new Space("Test Space", location) });

            Assert.NotNull(adjacencyCluster);
            Space space = Assert.Single(adjacencyCluster.GetSpaces());
            Assert.True(space.TryGetValue(SpaceParameter.Area, out double area), "SAM did not set SpaceParameter.Area.");
            return area;
        }

        private static double OcctArea(List<Face3D> face3Ds, Point3D location, out FloorAreaCalculationMethod floorAreaCalculationMethod)
        {
            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.AdjacencyCluster(
                new List<Panel>(), Complex(face3Ds, location), out List<string> diagnostics,
                tolerance: Tolerance.Distance, fuzzyTolerance: Tolerance.MacroDistance);

            Assert.NotNull(adjacencyCluster);
            Space space = Assert.Single(adjacencyCluster.GetSpaces());
            Assert.True(space.TryGetValue(SpaceParameter.Area, out double area), "OCCT did not set SpaceParameter.Area.");

            adjacencyCluster.FloorArea(space, out floorAreaCalculationMethod);
            return area;
        }

        private static ResolvedCellComplex Complex(List<Face3D> face3Ds, Point3D centre)
        {
            List<ResolvedCellFace> faces = new List<ResolvedCellFace>();
            for (int i = 0; i < face3Ds.Count; i++)
            {
                faces.Add(new ResolvedCellFace(face3Ds[i], i + 1, new List<int> { 0 }, new List<int> { i }));
            }

            List<ResolvedCell> cells = new List<ResolvedCell> { new ResolvedCell(0, Length * Width * Height, centre) };
            return new ResolvedCellComplex(Guid.NewGuid(), cells, faces, null, null, 0);
        }

        private static List<Face3D> BoxFaces(double length, double width, double height)
        {
            return new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(length, 0, 0), new Point3D(length, width, 0), new Point3D(0, width, 0)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, height), new Point3D(length, 0, height), new Point3D(length, width, height), new Point3D(0, width, height)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(length, 0, 0), new Point3D(length, 0, height), new Point3D(0, 0, height)),
                TestGeometry.CreatePlanarFace(new Point3D(0, width, 0), new Point3D(length, width, 0), new Point3D(length, width, height), new Point3D(0, width, height)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(0, width, 0), new Point3D(0, width, height), new Point3D(0, 0, height)),
                TestGeometry.CreatePlanarFace(new Point3D(length, 0, 0), new Point3D(length, width, 0), new Point3D(length, width, height), new Point3D(length, 0, height))
            };
        }

        /// <summary>A closed prism whose lower boundary is a single tilted rectangle rising by <paramref name="rise"/> over <paramref name="length"/>.</summary>
        private static List<Face3D> RampFaces(double length, double width, double rise, double top)
        {
            return new List<Face3D>
            {
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(length, 0, rise), new Point3D(length, width, rise), new Point3D(0, width, 0)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, top), new Point3D(0, width, top), new Point3D(length, width, top), new Point3D(length, 0, top)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(0, width, 0), new Point3D(0, width, top), new Point3D(0, 0, top)),
                TestGeometry.CreatePlanarFace(new Point3D(length, 0, rise), new Point3D(length, 0, top), new Point3D(length, width, top), new Point3D(length, width, rise)),
                TestGeometry.CreatePlanarFace(new Point3D(0, 0, 0), new Point3D(0, 0, top), new Point3D(length, 0, top), new Point3D(length, 0, rise)),
                TestGeometry.CreatePlanarFace(new Point3D(0, width, 0), new Point3D(length, width, rise), new Point3D(length, width, top), new Point3D(0, width, top))
            };
        }
    }
}
