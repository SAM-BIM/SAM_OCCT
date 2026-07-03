// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare static Create/Query classes.
using GeometryCreate = SAM.Geometry.OCCT.Create;
using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// End-to-end tests for the persistent OCCT topology handle (issue #14).
    /// Each test auto-skips (via SkippableFact) when the native library is absent.
    /// </summary>
    public class OcctTopologyIntegrationTests
    {
        [SkippableFact]
        public void AbiVersion_NativeAvailable_ReportsVersion2()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - any tiny native round trip populates NativeVersion.
            List<Shell> shells = new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) };

            // Act
            GeometryQuery.ShellsUnion(shells, out OcctCellComplexResult result, new OcctBuildOptions());

            // Assert - the committed native build must expose ABI revision 4
            // (sam_occt_abi_version) so the shape-handle, validation, BOP-glue (_ex)
            // and Phase-3 history/naked-wire/tolerance entry points all exist.
            Assert.True(result.NativeAvailable);
            Assert.Equal("4", result.NativeVersion);
        }

        [SkippableFact]
        public void Topology_UnitBox_ReturnsValidHandleWithOneSolid()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            List<Shell> shells = new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) };

            // Act
            using (OcctTopology topology = GeometryCreate.Topology(shells, out OcctCellComplexResult result))
            {
                // Assert
                Assert.True(result.NativeAvailable);
                Assert.NotNull(topology);
                Assert.False(topology.IsInvalid);
                Assert.Equal(1, topology.SolidCount);
                Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_TOPOLOGY_SUCCESS");
            }
        }

        [SkippableFact]
        public void CellComplexResult_TopologyPath_MatchesArrayPath_CellsVolumesCentersAdjacencies()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - two adjacent boxes; build via the legacy array path and
            // via the persistent handle, then compare the decoded complexes.
            List<Face3D> face3Ds = new List<Face3D>();
            face3Ds.AddRange(TestGeometry.CreateUnitBox(0, 0, 0).Face3Ds);
            face3Ds.AddRange(TestGeometry.CreateUnitBox(1, 0, 0).Face3Ds);

            // Act - legacy array path (build -> decode -> free inside the call).
            List<Shell> shells_Array = GeometryCreate.Shells(face3Ds, out OcctCellComplexResult result_Array, new OcctBuildOptions());

            // Act - persistent handle path (create -> decode bridge).
            using (OcctTopology topology = GeometryCreate.Topology(face3Ds, out OcctCellComplexResult result_Create))
            {
                Assert.NotNull(topology);
                OcctCellComplexResult result_Topology = GeometryQuery.CellComplexResult(topology);

                // Assert - identical cell complexes from both paths.
                Assert.True(result_Array.Success);
                Assert.True(result_Topology.Success);
                Assert.NotNull(shells_Array);
                Assert.Equal(result_Array.Cells.Count, result_Topology.Cells.Count);
                Assert.Equal(result_Array.FaceAdjacencies.Count, result_Topology.FaceAdjacencies.Count);

                List<double> volumes_Array = result_Array.Cells.Select(x => x.Volume).OrderBy(x => x).ToList();
                List<double> volumes_Topology = result_Topology.Cells.Select(x => x.Volume).OrderBy(x => x).ToList();
                for (int i = 0; i < volumes_Array.Count; i++)
                {
                    Assert.Equal(volumes_Array[i], volumes_Topology[i], 9);
                }

                Assert.Equal(result_Array.Cells.Count(x => x.Center != null), result_Topology.Cells.Count(x => x.Center != null));
            }
        }

        [SkippableFact]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            List<Shell> shells = new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) };
            OcctTopology topology = GeometryCreate.Topology(shells, out _);
            Assert.NotNull(topology);

            // Act + Assert - SafeHandle.Dispose is idempotent.
            topology.Dispose();
            topology.Dispose();
            Assert.True(topology.IsClosed);
            Assert.Equal(-1, topology.SolidCount);
        }

        [SkippableFact]
        public void Finalizer_UndisposedTopology_ReleasesWithoutCrash()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - create a topology and drop the reference without Dispose.
            CreateAndAbandonTopology();

            // Act - force finalization of the abandoned SafeHandle.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Assert - the native library still works after finalizer release.
            List<Shell> shells = new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) };
            GeometryQuery.ShellsUnion(shells, out OcctCellComplexResult result, new OcctBuildOptions());
            Assert.True(result.NativeAvailable);
            Assert.True(result.Success);
        }

        [SkippableFact]
        public void IsPointInside_CenterOfUnitBox_ReturnsTrue()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            using (OcctTopology topology = GeometryCreate.Topology(new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) }, out _))
            {
                Assert.NotNull(topology);

                // Act + Assert
                Assert.True(GeometryQuery.IsPointInside(topology, new Point3D(0.5, 0.5, 0.5)));
            }
        }

        [SkippableFact]
        public void IsPointInside_PointOutsideBox_ReturnsFalse()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            using (OcctTopology topology = GeometryCreate.Topology(new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) }, out _))
            {
                Assert.NotNull(topology);

                // Act + Assert
                Assert.False(GeometryQuery.IsPointInside(topology, new Point3D(5, 5, 5)));
            }
        }

        [SkippableFact]
        public void IsPointInside_PointOnFaceWithinTolerance_ReturnsTrue()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            using (OcctTopology topology = GeometryCreate.Topology(new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) }, out _))
            {
                Assert.NotNull(topology);

                // Act + Assert - a point on the x = 1 face classifies as ON.
                Assert.True(GeometryQuery.IsPointInside(topology, new Point3D(1.0, 0.5, 0.5)));
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void CreateAndAbandonTopology()
        {
            List<Shell> shells = new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) };
            OcctTopology topology = GeometryCreate.Topology(shells, out _);
            Assert.NotNull(topology);
        }
    }
}
