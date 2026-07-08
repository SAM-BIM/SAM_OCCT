// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.OCCT.Solver;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="LoopAttribution.AttributeLoopsToSources"/> - mapping a resolved cell
    /// complex's naked wires back to the input source panels to escalate
    /// (docs/P5_DIAGNOSIS_DRIVEN_CLOSURE_DESIGN_REVIEW.md §E/§J.1). Hand-built wires + <see cref="SourceMap"/>,
    /// pure-managed: no native OCCT DLL required.
    /// </summary>
    public class LoopAttributionTests
    {
        /// <summary>A unit quad in the plane z = <paramref name="z"/>, [0,1]x[0,1] - a stand-in resolved face.</summary>
        private static Face3D Quad(double z)
        {
            return TestGeometry.CreatePlanarFace(
                new Point3D(0, 0, z), new Point3D(1, 0, z), new Point3D(1, 1, z), new Point3D(0, 1, z));
        }

        /// <summary>A closed 4-edge wire around the [0,1]x[0,1] rim at z, with the given per-edge owner indices.</summary>
        private static OcctNakedWire ClosedRimWire(double z, params int[] edgeOwners)
        {
            List<Point3D> points = new List<Point3D>
            {
                new Point3D(0, 0, z), new Point3D(1, 0, z), new Point3D(1, 1, z), new Point3D(0, 1, z)
            };
            return new OcctNakedWire(points, isClosed: true, edgeOwnerFaceIndices: edgeOwners);
        }

        [Fact]
        public void AttributeLoopsToSources_OwnerIndexMapsToSource_ReturnsThatSource()
        {
            // Arrange - resolved face 0 came from source panel 5; the wire's edges all own face 0.
            List<Face3D> resolved = new List<Face3D> { Quad(0) };
            SourceMap map = new SourceMap();
            map.Record(5, new FaceKey(0), Provenance.Resolved);
            List<OcctNakedWire> wires = new List<OcctNakedWire> { ClosedRimWire(0, 0, 0, 0, 0) };

            // Act
            IReadOnlyList<int> culprits = LoopAttribution.AttributeLoopsToSources(wires, resolved, map);

            // Assert
            Assert.Equal(new[] { 5 }, culprits.ToArray());
        }

        [Fact]
        public void AttributeLoopsToSources_OwnerMinusOne_FallsBackToNearestCoplanarFaceByMidpoint()
        {
            // Arrange - no native owner (-1) on any edge; the edge midpoints lie on resolved face 0's
            // boundary, so the midpoint fallback must resolve to face 0 -> source 3.
            List<Face3D> resolved = new List<Face3D> { Quad(0) };
            SourceMap map = new SourceMap();
            map.Record(3, new FaceKey(0), Provenance.Resolved);
            List<OcctNakedWire> wires = new List<OcctNakedWire> { ClosedRimWire(0, -1, -1, -1, -1) };

            // Act
            IReadOnlyList<int> culprits = LoopAttribution.AttributeLoopsToSources(wires, resolved, map);

            // Assert
            Assert.Equal(new[] { 3 }, culprits.ToArray());
        }

        [Fact]
        public void AttributeLoopsToSources_MergedSourceFace_ReturnsAllContributingSources()
        {
            // Arrange - resolved face 0 is a merge of sources 2 and 7.
            List<Face3D> resolved = new List<Face3D> { Quad(0) };
            SourceMap map = new SourceMap();
            map.RecordMerge(new[] { 2, 7 }, new FaceKey(0), Provenance.Resolved);
            List<OcctNakedWire> wires = new List<OcctNakedWire> { ClosedRimWire(0, 0, 0, 0, 0) };

            // Act
            IReadOnlyList<int> culprits = LoopAttribution.AttributeLoopsToSources(wires, resolved, map);

            // Assert - both sources are escalation culprits, returned sorted.
            Assert.Equal(new[] { 2, 7 }, culprits.ToArray());
        }

        [Fact]
        public void AttributeLoopsToSources_FabricatedOwnerFace_ExcludedAndDiagnosed()
        {
            // Arrange - resolved face 0 is a fabricated gap-fill patch (no real source); the naked wire
            // owns only that face.
            List<Face3D> resolved = new List<Face3D> { Quad(0) };
            SourceMap map = new SourceMap();
            map.RecordFabricated(new FaceKey(0), Provenance.GapFill);
            List<OcctNakedWire> wires = new List<OcctNakedWire> { ClosedRimWire(0, 0, 0, 0, 0) };
            SolverDiagnostics diagnostics = new SolverDiagnostics();

            // Act
            IReadOnlyList<int> culprits = LoopAttribution.AttributeLoopsToSources(wires, resolved, map, diagnostics);

            // Assert - the fabricated sentinel is never a culprit, and the unattributable loop is reported.
            Assert.Empty(culprits);
            Assert.DoesNotContain(SourceMap.FabricatedSource, culprits);
            Assert.Contains(diagnostics.OfCode(DiagnosticCode.NakedLoop),
                d => d.Severity == OcctDiagnosticSeverity.Warning);
        }

        [Fact]
        public void AttributeLoopsToSources_MixedFabricatedAndRealFaces_KeepsRealSourceOnly()
        {
            // Arrange - face 0 fabricated, face 1 from source 4; two wires, one over each.
            List<Face3D> resolved = new List<Face3D> { Quad(0), Quad(1) };
            SourceMap map = new SourceMap();
            map.RecordFabricated(new FaceKey(0), Provenance.GapFill);
            map.Record(4, new FaceKey(1), Provenance.Resolved);
            List<OcctNakedWire> wires = new List<OcctNakedWire>
            {
                ClosedRimWire(0, 0, 0, 0, 0), // owns fabricated face 0
                ClosedRimWire(1, 1, 1, 1, 1)  // owns real face 1
            };

            // Act
            IReadOnlyList<int> culprits = LoopAttribution.AttributeLoopsToSources(wires, resolved, map);

            // Assert
            Assert.Equal(new[] { 4 }, culprits.ToArray());
        }

        [Fact]
        public void AttributeLoopsToSources_NullWires_ReturnsEmpty()
        {
            // Arrange
            SourceMap map = new SourceMap();
            map.Record(1, new FaceKey(0), Provenance.Resolved);

            // Act
            IReadOnlyList<int> culprits = LoopAttribution.AttributeLoopsToSources(null, new List<Face3D> { Quad(0) }, map);

            // Assert
            Assert.Empty(culprits);
        }

        [Fact]
        public void AttributeLoopsToSources_OwnerOutOfRange_FallsBackToMidpointResolution()
        {
            // Arrange - owner index points past the resolved list; the midpoint fallback resolves it to
            // the only coplanar face (0) -> source 9.
            List<Face3D> resolved = new List<Face3D> { Quad(0) };
            SourceMap map = new SourceMap();
            map.Record(9, new FaceKey(0), Provenance.Resolved);
            List<OcctNakedWire> wires = new List<OcctNakedWire> { ClosedRimWire(0, 99, 99, 99, 99) };

            // Act
            IReadOnlyList<int> culprits = LoopAttribution.AttributeLoopsToSources(wires, resolved, map);

            // Assert
            Assert.Equal(new[] { 9 }, culprits.ToArray());
        }
    }
}
