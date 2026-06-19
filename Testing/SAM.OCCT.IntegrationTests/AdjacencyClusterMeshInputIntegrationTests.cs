// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// End-to-end proof that the mesh-input path of SAMOCCT.CreateAdjacencyClusterByShells
    /// works against the REAL native OCCT kernel. The component assembles a Rhino Mesh /
    /// SAM Mesh3D into a Shell of bare triangle faces, then sews + builds it. These tests
    /// reproduce that triangle-soup shell and drive the same native build with sewing on,
    /// asserting that MakerVolume closes the volume, that UnifySameDomain merges the
    /// triangles back into clean planar panels, and that adjacency between two mesh cells
    /// is detected. Native-gated.
    /// </summary>
    public class AdjacencyClusterMeshInputIntegrationTests
    {
        private readonly ITestOutputHelper output;

        public AdjacencyClusterMeshInputIntegrationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>
        /// Triangulates a shell and rebuilds it from the bare triangle faces - the same
        /// triangle soup the component produces from a Rhino Mesh / SAM Mesh3D.
        /// </summary>
        private static Shell TriangleSoupShell(Shell shell)
        {
            Mesh3D mesh3D = global::SAM.Geometry.Spatial.Create.Mesh3D(shell, SAM.Core.Tolerance.Distance);
            List<Triangle3D> triangle3Ds = mesh3D.GetTriangles();
            return new Shell(triangle3Ds.ConvertAll(x => new Face3D(x)));
        }

        private void WriteDiagnostics(OcctCellComplexResult result)
        {
            if (result?.Diagnostics == null)
            {
                return;
            }

            foreach (OcctDiagnostic diagnostic in result.Diagnostics)
            {
                output.WriteLine(diagnostic.ToString());
            }
        }

        [SkippableFact]
        public void AdjacencyCluster_FromTriangulatedMeshBox_BuildsSingleSpaceAndMergesPanels()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a unit box reduced to bare triangles (12), as a mesh input would be.
            Shell soupShell = TriangleSoupShell(TestGeometry.CreateUnitBox(0, 0, 0));
            Assert.True(soupShell.Face3Ds.Count >= 12, "the triangle soup should have at least 12 faces");

            // The watertightness pre-check the component runs should see a closed volume.
            bool analysed = soupShell.Face3Ds.Watertightness(new OcctBuildOptions(), out int edgeCount, out int nakedEdgeCount, out _);
            Assert.True(analysed);
            Assert.Equal(0, nakedEdgeCount);

            // Act - the mesh-input path always sews before MakerVolume.
            OcctBuildOptions options = new OcctBuildOptions { SewBeforeBuild = true };
            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.AdjacencyCluster(
                new List<Shell> { soupShell }, null, out OcctCellComplexResult result, null, options);

            // Assert
            WriteDiagnostics(result);

            Assert.NotNull(adjacencyCluster);
            Assert.Single(adjacencyCluster.GetSpaces());

            List<Panel> panels = adjacencyCluster.GetPanels();
            Assert.NotNull(panels);

            // UnifySameDomain must merge the 12 triangles back into the box's 6 planar panels;
            // otherwise the triangle soup would leak through as 12 panels.
            Assert.Equal(6, panels.Count);
        }

        [SkippableFact]
        public void AdjacencyCluster_FromTwoAdjacentTriangulatedMeshBoxes_BuildsTwoSpacesWithSharedPanel()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - two unit boxes sharing the x = 1 wall, each as its own mesh (triangle soup).
            List<Shell> shells = new List<Shell>
            {
                TriangleSoupShell(TestGeometry.CreateUnitBox(0, 0, 0)),
                TriangleSoupShell(TestGeometry.CreateUnitBox(1, 0, 0))
            };

            // Act
            OcctBuildOptions options = new OcctBuildOptions { SewBeforeBuild = true };
            AdjacencyCluster adjacencyCluster = AnalyticalOcctCreate.AdjacencyCluster(
                shells, null, out OcctCellComplexResult result, null, options);

            // Assert
            WriteDiagnostics(result);

            Assert.NotNull(adjacencyCluster);
            Assert.Equal(2, adjacencyCluster.GetSpaces().Count);

            // The shared wall must be one internal panel bordering both spaces - the core
            // adjacency relation, recovered from two independent mesh inputs.
            List<Panel> panels = adjacencyCluster.GetPanels();
            Assert.NotNull(panels);
            bool sharedPanelFound = panels.Any(panel => adjacencyCluster.GetSpaces(panel)?.Count == 2);
            Assert.True(sharedPanelFound, "expected one panel shared by both mesh-derived spaces");
        }
    }
}
