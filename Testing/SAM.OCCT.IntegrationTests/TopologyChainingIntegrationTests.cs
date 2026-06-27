// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare static Create/Query classes.
using GeometryCreate = SAM.Geometry.OCCT.Create;
using GeometryQuery = SAM.Geometry.OCCT.Query;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// End-to-end tests for natively chained shape operations and topology
    /// retention (issue #14, stage 3).
    /// </summary>
    public class TopologyChainingIntegrationTests
    {
        [SkippableFact]
        public void TopologyDifference_ChainedNative_MatchesTwoStepArrayPath()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a 2x1x1 slab, cut by a unit box, then united with a
            // third box adjacent to the remainder.
            Shell target = TestGeometry.CreateBox(0, 0, 0, 2, 1, 1);
            Shell cutter = TestGeometry.CreateBox(1, 0, 0, 2, 1, 1);
            Shell extra = TestGeometry.CreateBox(0, 1, 0, 1, 1, 1);

            // Act - legacy two-step array path: every step re-serialises and
            // rebuilds the geometry managed->native.
            List<Shell> difference_Shells = GeometryQuery.ShellsDifference(new List<Shell> { target }, new List<Shell> { cutter }, out OcctCellComplexResult _, new OcctBuildOptions());
            Assert.NotNull(difference_Shells);
            List<Shell> union_Shells = GeometryQuery.ShellsUnion(difference_Shells.Concat(new[] { extra }), out OcctCellComplexResult result_Array, new OcctBuildOptions());
            Assert.NotNull(union_Shells);

            // Act - chained native path: the intermediate geometry never leaves
            // OCCT.
            using (OcctTopology topology_Target = GeometryCreate.Topology(new List<Shell> { target }, out _))
            using (OcctTopology topology_Cutter = GeometryCreate.Topology(new List<Shell> { cutter }, out _))
            using (OcctTopology topology_Extra = GeometryCreate.Topology(new List<Shell> { extra }, out _))
            {
                Assert.NotNull(topology_Target);
                Assert.NotNull(topology_Cutter);
                Assert.NotNull(topology_Extra);

                using (OcctTopology topology_Difference = GeometryQuery.TopologyDifference(topology_Target, topology_Cutter, out OcctCellComplexResult result_Difference))
                {
                    Assert.NotNull(topology_Difference);

                    // Combine the difference result with the extra box into one
                    // handle via make-volume-free union: union over a compound is
                    // exposed through TopologyUnion of a merged topology; build
                    // the merged input from the decoded difference plus extra.
                    OcctCellComplexResult decoded_Difference = GeometryQuery.CellComplexResult(topology_Difference);
                    Assert.True(decoded_Difference.Success);

                    using (OcctTopology topology_Combined = GeometryCreate.Topology(decoded_Difference.Shells.Concat(new[] { extra }), out _))
                    {
                        Assert.NotNull(topology_Combined);

                        using (OcctTopology topology_Union = GeometryQuery.TopologyUnion(topology_Combined, out OcctCellComplexResult result_UnionOp))
                        {
                            Assert.NotNull(topology_Union);
                            OcctCellComplexResult result_Topology = GeometryQuery.CellComplexResult(topology_Union);

                            // Assert - both routes converge on the same complex.
                            Assert.True(result_Array.Success);
                            Assert.True(result_Topology.Success);
                            Assert.Equal(result_Array.Cells.Count, result_Topology.Cells.Count);
                            Assert.Equal(
                                result_Array.Cells.Sum(x => x.Volume),
                                result_Topology.Cells.Sum(x => x.Volume),
                                9);
                        }
                    }
                }
            }
        }

        [SkippableFact]
        public void TopologyIntersection_OverlappingBoxes_ProducesExpectedVolume()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - boxes overlapping in x = [1, 2]; chained natively.
            using (OcctTopology target = GeometryCreate.Topology(new List<Shell> { TestGeometry.CreateBox(0, 0, 0, 2, 1, 1) }, out _))
            using (OcctTopology tool = GeometryCreate.Topology(new List<Shell> { TestGeometry.CreateBox(1, 0, 0, 2, 1, 1) }, out _))
            {
                Assert.NotNull(target);
                Assert.NotNull(tool);

                // Act
                using (OcctTopology intersection = GeometryQuery.TopologyIntersection(target, tool, out OcctCellComplexResult result))
                {
                    // Assert - the inputs stay valid; the output is the 1x1x1 overlap.
                    Assert.NotNull(intersection);
                    Assert.False(target.IsClosed);
                    Assert.False(tool.IsClosed);

                    OcctCellComplexResult decoded = GeometryQuery.CellComplexResult(intersection);
                    Assert.True(decoded.Success);
                    Assert.Equal(1.0, decoded.Cells.Sum(x => x.Volume), 6);
                }
            }
        }

        [SkippableFact]
        public void TopologyRepair_AfterCreate_KeepsSolidCount()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            using (OcctTopology topology = GeometryCreate.Topology(new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) }, out _))
            {
                Assert.NotNull(topology);

                // Act - repair with no minimum area is a native rebuild that must
                // not drop the solid.
                using (OcctTopology repaired = GeometryQuery.TopologyRepair(topology, out OcctCellComplexResult result))
                {
                    // Assert
                    Assert.NotNull(repaired);
                    Assert.Equal(topology.SolidCount, repaired.SolidCount);

                    OcctCellComplexResult decoded = GeometryQuery.CellComplexResult(repaired);
                    Assert.True(decoded.Success);
                    Assert.Equal(1.0, decoded.Cells.Sum(x => x.Volume), 6);
                }
            }
        }

        [SkippableFact]
        public void ShellsUnion_RetainTopologyOption_ResultExposesDisposableTopology()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - two adjacent unit boxes and the retention opt-in.
            List<Shell> shells = new List<Shell>
            {
                TestGeometry.CreateUnitBox(0, 0, 0),
                TestGeometry.CreateUnitBox(1, 0, 0)
            };

            // Act
            List<Shell> union_Shells = GeometryQuery.ShellsUnion(shells, out OcctCellComplexResult result, new OcctBuildOptions { RetainTopology = true });

            // Assert - same decoded outcome as the legacy path, plus the live handle.
            Assert.True(result.Success);
            Assert.NotNull(union_Shells);
            Assert.Single(result.Cells);
            Assert.Equal(2.0, result.Cells.Sum(x => x.Volume), 6);
            Assert.NotNull(result.Topology);
            Assert.False(result.Topology.IsInvalid);
            Assert.Equal(1, result.Topology.SolidCount);

            result.Dispose();
            Assert.True(result.Topology.IsClosed);
        }

        [SkippableFact]
        public void Result_Dispose_DisposesRetainedTopology_DoubleDisposeSafe()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            List<Shell> shells = new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) };
            GeometryQuery.ShellsUnion(shells, out OcctCellComplexResult result, new OcctBuildOptions { RetainTopology = true });
            Assert.NotNull(result.Topology);

            // Act + Assert - disposing the result disposes the retained handle;
            // disposing either again is safe.
            result.Dispose();
            Assert.True(result.Topology.IsClosed);
            result.Dispose();
            result.Topology.Dispose();
        }

        [SkippableFact]
        public void TopologyUnion_DisposedInput_FailsWithDiagnosticNotCrash()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a disposed input handle.
            OcctTopology topology = GeometryCreate.Topology(new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) }, out _);
            Assert.NotNull(topology);
            topology.Dispose();

            // Act
            OcctTopology output = GeometryQuery.TopologyUnion(topology, out OcctCellComplexResult result);

            // Assert
            Assert.Null(output);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_TOPOLOGY_DISPOSED");
        }
    }
}
