// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Pure-managed unit tests for the Phase P2 <see cref="ResolvedCellComplex"/> DTO and its projection
    /// from a decoded cell complex. Native-free: a synthetic <see cref="OcctCellComplexResult"/> is built
    /// by hand (public AddCell / BuildFaceAdjacencies), so the projection, dedup, flat-ordinal bridge and
    /// serialization are exercised without a kernel.
    /// </summary>
    public class ResolvedCellComplexTests
    {
        private static Face3D Quad(double x)
        {
            // A distinct valid unit quad per call (geometry is immaterial to the projection logic; only
            // validity, per-cell order and TopologyKey matter).
            return TestGeometry.CreatePlanarFace(
                new Point3D(x, 0, 0), new Point3D(x + 1, 0, 0), new Point3D(x + 1, 1, 0), new Point3D(x, 1, 0));
        }

        private static OcctCell Cell(double volume, Point3D centre, params OcctCellFace[] faces)
        {
            List<Face3D> face3Ds = faces.Where(f => f?.Face3D != null).Select(f => f.Face3D).ToList();
            Shell shell = new Shell(face3Ds.Count == 0 ? new List<Face3D> { Quad(0) } : face3Ds);
            return new OcctCell(shell, volume, null, faces, centre);
        }

        [Fact]
        public void Project_SharedFaceBetweenTwoCells_OneUniqueFaceWithTwoOwners()
        {
            // Two cells share a face (same TopologyKey 100); each also has a private envelope face.
            OcctCellComplexResult result = new OcctCellComplexResult();
            result.AddCell(Cell(10, new Point3D(0, 0, 0), new OcctCellFace(Quad(0), 100), new OcctCellFace(Quad(2), 1)));
            result.AddCell(Cell(20, new Point3D(5, 0, 0), new OcctCellFace(Quad(4), 100), new OcctCellFace(Quad(6), 2)));
            result.BuildFaceAdjacencies();

            ResolvedCellComplex complex = ResolvedCellComplex.Project(result, null, Guid.NewGuid());

            Assert.Equal(2, complex.Cells.Count);
            // The shared face appears exactly once, with both cells as owners.
            List<ResolvedCellFace> shared = complex.Faces.Where(f => f.TopologyKey == 100).ToList();
            Assert.Single(shared);
            Assert.Equal(new[] { 0, 1 }, shared[0].OwnerCellIndices.OrderBy(x => x).ToArray());
            // Two envelope faces, one owner each.
            Assert.Equal(3, complex.Faces.Count);
            Assert.All(complex.Faces.Where(f => f.TopologyKey != 100), f => Assert.Single(f.OwnerCellIndices));
            // The adjacency pair is decoded.
            Assert.Contains(complex.Adjacencies, a => a.TopologyKey == 100
                && ((a.CellIndex1 == 0 && a.CellIndex2 == 1) || (a.CellIndex1 == 1 && a.CellIndex2 == 0)));
        }

        [Fact]
        public void Project_FlatOrdinals_CountValidFacesInFlattenOrder()
        {
            OcctCellComplexResult result = new OcctCellComplexResult();
            result.AddCell(Cell(10, new Point3D(0, 0, 0), new OcctCellFace(Quad(0), 10), new OcctCellFace(Quad(2), 20)));
            result.AddCell(Cell(20, new Point3D(5, 0, 0), new OcctCellFace(Quad(4), 30)));
            result.BuildFaceAdjacencies();

            ResolvedCellComplex complex = ResolvedCellComplex.Project(result, null, Guid.NewGuid());

            // Flatten order is cell 0 [key10, key20], cell 1 [key30] -> ordinals 0, 1, 2.
            Assert.Equal(0, complex.Faces.First(f => f.TopologyKey == 10).FlatOrdinals[0]);
            Assert.Equal(1, complex.Faces.First(f => f.TopologyKey == 20).FlatOrdinals[0]);
            Assert.Equal(2, complex.Faces.First(f => f.TopologyKey == 30).FlatOrdinals[0]);
        }

        [Fact]
        public void Project_InvalidFaceInCell_DoesNotAdvanceFlatOrdinal()
        {
            // Filter drift: a decoded-but-invalid face (null Face3D here) must NOT advance the flat ordinal,
            // so the bridge is never derived arithmetically from per-cell face counts. The face AFTER it must
            // land at ordinal 1, not 2.
            OcctCellComplexResult result = new OcctCellComplexResult();
            result.AddCell(Cell(10, new Point3D(0, 0, 0),
                new OcctCellFace(Quad(0), 10),         // valid   -> ordinal 0
                new OcctCellFace(null, 20),            // invalid -> ordinal -1 (no advance)
                new OcctCellFace(Quad(2), 30)));       // valid   -> ordinal 1 (NOT 2)
            result.BuildFaceAdjacencies();

            ResolvedCellComplex complex = ResolvedCellComplex.Project(result, null, Guid.NewGuid());

            Assert.Equal(0, complex.Faces.First(f => f.TopologyKey == 10).FlatOrdinals[0]);
            Assert.Equal(-1, complex.Faces.First(f => f.TopologyKey == 20).FlatOrdinals[0]);
            Assert.Equal(1, complex.Faces.First(f => f.TopologyKey == 30).FlatOrdinals[0]);
        }

        [Fact]
        public void Project_TopologyKeyZeroFaces_ExcludedFromFacesButCounted()
        {
            OcctCellComplexResult result = new OcctCellComplexResult();
            result.AddCell(Cell(10, new Point3D(0, 0, 0),
                new OcctCellFace(Quad(0), 10),
                new OcctCellFace(Quad(2), 0),   // no per-decode identity
                new OcctCellFace(Quad(4), 0)));
            result.BuildFaceAdjacencies();

            ResolvedCellComplex complex = ResolvedCellComplex.Project(result, null, Guid.NewGuid());

            Assert.Single(complex.Faces); // only the key-10 face
            Assert.Equal(2, complex.TopologyKeyZeroFaceCount);
            // A valid key-0 face still advances the ordinal (it is a real face in the flatten): the key-10
            // face is at 0, and the two key-0 faces occupied ordinals 1 and 2 even though they are excluded.
            Assert.Equal(0, complex.Faces[0].FlatOrdinals[0]);
        }

        [Fact]
        public void ToJsonObject_RoundTrip_PreservesFacesOwnersOrdinalsAndAdjacencies()
        {
            OcctCellComplexResult result = new OcctCellComplexResult();
            result.AddCell(Cell(10.5, new Point3D(0, 0, 0), new OcctCellFace(Quad(0), 100), new OcctCellFace(Quad(2), 1)));
            result.AddCell(Cell(20.25, new Point3D(5, 0, 0), new OcctCellFace(Quad(4), 100), new OcctCellFace(Quad(6), 2)));
            result.BuildFaceAdjacencies();
            Guid solveId = Guid.NewGuid();
            ResolvedCellComplex original = ResolvedCellComplex.Project(result, null, solveId);

            ResolvedCellComplex roundTripped = new ResolvedCellComplex(original.ToJsonObject());

            Assert.Equal(solveId, roundTripped.SolveId);
            Assert.Equal(original.Cells.Count, roundTripped.Cells.Count);
            Assert.Equal(original.Faces.Count, roundTripped.Faces.Count);
            Assert.Equal(original.Adjacencies.Count, roundTripped.Adjacencies.Count);
            Assert.Equal(original.TopologyKeyZeroFaceCount, roundTripped.TopologyKeyZeroFaceCount);

            ResolvedCellFace originalShared = original.Faces.First(f => f.TopologyKey == 100);
            ResolvedCellFace roundTrippedShared = roundTripped.Faces.First(f => f.TopologyKey == 100);
            Assert.Equal(originalShared.OwnerCellIndices, roundTrippedShared.OwnerCellIndices);
            Assert.Equal(originalShared.FlatOrdinals, roundTrippedShared.FlatOrdinals);
            Assert.NotNull(roundTrippedShared.Face3D);
            Assert.Equal(originalShared.Face3D.GetArea(), roundTrippedShared.Face3D.GetArea(), 6);
            Assert.Equal(10.5, roundTripped.Cells[0].Volume, 6);
        }
    }
}
