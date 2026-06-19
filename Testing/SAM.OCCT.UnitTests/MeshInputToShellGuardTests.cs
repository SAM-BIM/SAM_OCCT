// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT.Native;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Guards the mesh-input path added to SAMOCCT.CreateAdjacencyClusterByShells:
    /// a Rhino Mesh / SAM Mesh3D is not a closed Brep, so the component pulls its
    /// triangle Face3Ds and assembles them into a single SAM Shell. These pure-managed
    /// tests reproduce that assembly (triangulated box -> triangle Face3Ds -> Shell)
    /// and assert the result is a watertight closed volume the OCCT path can consume.
    /// </summary>
    public class MeshInputToShellGuardTests
    {
        /// <summary>
        /// Mimics the Face3D extraction the component performs on a mesh input: every
        /// mesh triangle becomes a Face3D.
        /// </summary>
        private static List<Face3D> TriangleFace3DsFromBoxMesh()
        {
            Shell boxShell = new Shell(TestGeometry.CreateClosedBoxFaces());
            Mesh3D mesh3D = global::SAM.Geometry.Spatial.Create.Mesh3D(boxShell, Core.Tolerance.Distance);
            Assert.NotNull(mesh3D);

            List<Triangle3D> triangle3Ds = mesh3D.GetTriangles();
            Assert.NotNull(triangle3Ds);
            Assert.True(triangle3Ds.Count >= 12, "A triangulated box should have at least 12 triangles.");

            return triangle3Ds.ConvertAll(x => new Face3D(x));
        }

        [Fact]
        public void RawTriangleFaces_FromBoxMesh_AreWatertight()
        {
            // Arrange - the raw mesh triangle faces (the new Shell(face3Ds) fallback).
            List<Face3D> face3Ds = TriangleFace3DsFromBoxMesh();

            // Act
            OcctOpenShellAnalysis.WatertightnessSummary summary = OcctOpenShellAnalysis.AnalyzeWatertightness(face3Ds, new OcctBuildOptions());

            // Assert - a closed mesh stays closed when fed face-by-face to the OCCT path.
            Assert.True(summary.EdgeCount > 0);
            Assert.Equal(0, summary.NakedEdgeCount);
            Assert.Equal(0, summary.NonManifoldEdgeCount);
            Assert.True(summary.IsCleanForGlue);
        }

        [Fact]
        public void CreateShell_FromBoxMeshTriangles_ReturnsWatertightShell()
        {
            // Arrange
            List<Face3D> face3Ds = TriangleFace3DsFromBoxMesh();

            // Act - the preferred path the component uses for a mesh input.
            Shell shell = global::SAM.Geometry.Spatial.Create.Shell(face3Ds, Core.Tolerance.MacroDistance, Core.Tolerance.Distance);

            // Assert - a closed volume is produced. (Coplanar triangle merging is left to
            // the OCCT UnifySameDomain pass downstream, so the face count is not asserted.)
            Assert.NotNull(shell);

            List<Face3D> shellFace3Ds = shell.Face3Ds;
            Assert.NotNull(shellFace3Ds);
            Assert.True(shellFace3Ds.Count >= 6, "A box shell needs at least its 6 planar faces.");

            OcctOpenShellAnalysis.WatertightnessSummary summary = OcctOpenShellAnalysis.AnalyzeWatertightness(shellFace3Ds, new OcctBuildOptions());
            Assert.Equal(0, summary.NakedEdgeCount);
            Assert.True(summary.IsCleanForGlue);
        }

        [Fact]
        public void CreateShell_FromEmptyFaces_ReturnsNull()
        {
            // The component falls back to new Shell(face3Ds) when Create.Shell yields null,
            // so document that Create.Shell returns null (not throw) on empty input.
            Shell shell = global::SAM.Geometry.Spatial.Create.Shell(new List<Face3D>(), Core.Tolerance.MacroDistance, Core.Tolerance.Distance);
            Assert.Null(shell);
        }
    }
}
