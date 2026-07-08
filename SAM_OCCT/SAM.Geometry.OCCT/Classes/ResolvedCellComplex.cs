// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace SAM.Geometry.OCCT
{
    /// <summary>
    /// The cell complex a Solve3D adopted, captured as a first-class, pure-managed, serializable
    /// product (docs/CELLCOMPLEX_FIRST_HANDOVER.md, Phase P2). Projected ONCE from the same
    /// <see cref="OcctCellComplexResult"/> decode that produced the solver's adopted cells and
    /// closure signature, before that native result is disposed - so it carries no native lifetime
    /// and is safe to read, serialize, and hand across a Grasshopper boundary (P3) after the solve
    /// returns.
    /// </summary>
    /// <remarks>
    /// <para><b>Per-decode identity.</b> <see cref="ResolvedCellFace.TopologyKey"/> is a
    /// quantized-vertex signature valid ONLY within this one decode (native
    /// <c>CellComplexBuilder</c>); it is never compared across two complexes. The unique faces are
    /// deduped by that key, so a shared internal separator appears exactly once with two owner cells
    /// (a real double-owner), and an envelope face once with a single owner. Faces the native builder
    /// could not key (<c>TopologyKey == 0</c>) are excluded from <see cref="Faces"/> and counted in
    /// <see cref="TopologyKeyZeroFaceCount"/> so they are accounted for, never silently dropped.</para>
    /// <para><b>Flat ordinals.</b> Each owner records the face's position in the flattened,
    /// <c>IsValid</c>-filtered sequence of all cell faces of this decode
    /// (<c>shells.SelectMany(Face3Ds).Where(x =&gt; x != null &amp;&amp; x.IsValid())</c>) - the
    /// ordering the solver's <c>ResolvedFace3Ds</c> is built from before any coplanar post-merge. The
    /// bridge is emitted by replicating that filter, never derived arithmetically from per-cell face
    /// counts (a decoded-but-invalid face would drift the count). Mapping these ordinals onto a
    /// post-merge output face is a P5 concern.</para>
    /// </remarks>
    public class ResolvedCellComplex : IJSAMObject
    {
        /// <summary>Identifies the solve that produced this complex, so a downstream consumer can prove
        /// a set of panels came from THIS solve before consuming the complex without a rebuild (P3
        /// roster gate). A fresh GUID per solve.</summary>
        public Guid SolveId { get; private set; }

        /// <summary>Per-cell metadata (index, volume, centre), cell index aligned to the decode order.</summary>
        public IReadOnlyList<ResolvedCell> Cells { get; private set; }

        /// <summary>Unique cell faces, deduped by per-decode <see cref="ResolvedCellFace.TopologyKey"/>
        /// (excludes <c>TopologyKey == 0</c> faces). A shared face appears once with two owner cells.</summary>
        public IReadOnlyList<ResolvedCellFace> Faces { get; private set; }

        /// <summary>Shared-face adjacency pairs (per-decode key + the two owner cell indices).</summary>
        public IReadOnlyList<ResolvedFaceAdjacency> Adjacencies { get; private set; }

        /// <summary>Residual naked (free) boundary wires; empty on an adopted raw solve (watertight).</summary>
        public IReadOnlyList<OcctNakedWire> NakedWires { get; private set; }

        /// <summary>Count of decoded cell faces the native builder could not assign a per-decode key to
        /// (<c>TopologyKey == 0</c>) - excluded from <see cref="Faces"/>, surfaced here for parity
        /// accounting (they would otherwise be silent relation gaps downstream).</summary>
        public int TopologyKeyZeroFaceCount { get; private set; }

        public ResolvedCellComplex(
            Guid solveId,
            IEnumerable<ResolvedCell> cells,
            IEnumerable<ResolvedCellFace> faces,
            IEnumerable<ResolvedFaceAdjacency> adjacencies,
            IEnumerable<OcctNakedWire> nakedWires,
            int topologyKeyZeroFaceCount)
        {
            SolveId = solveId;
            Cells = (cells ?? Enumerable.Empty<ResolvedCell>()).Where(x => x != null).ToList();
            Faces = (faces ?? Enumerable.Empty<ResolvedCellFace>()).Where(x => x != null).ToList();
            Adjacencies = (adjacencies ?? Enumerable.Empty<ResolvedFaceAdjacency>()).Where(x => x != null).ToList();
            NakedWires = (nakedWires ?? Enumerable.Empty<OcctNakedWire>()).Where(x => x != null).ToList();
            TopologyKeyZeroFaceCount = topologyKeyZeroFaceCount;
        }

        public ResolvedCellComplex(JsonObject jsonObject)
        {
            FromJsonObject(jsonObject);
        }

        /// <summary>
        /// Projects the durable, pure-managed cell complex from a native decode, deduping unique faces
        /// by per-decode key and emitting the flat-ordinal bridge for each owner. Read this BEFORE the
        /// supplied result is disposed. Cells/faces/adjacencies reflect exactly the decode passed in;
        /// naked wires and the SolveId are supplied by the caller (they are known at adoption, not in
        /// the decode itself).
        /// </summary>
        public static ResolvedCellComplex Project(OcctCellComplexResult result, IEnumerable<OcctNakedWire> nakedWires, Guid solveId)
        {
            List<ResolvedCell> cells = new List<ResolvedCell>();
            Dictionary<int, FaceBuilder> faceByKey = new Dictionary<int, FaceBuilder>();
            List<FaceBuilder> orderedFaces = new List<FaceBuilder>();
            int topologyKeyZeroFaceCount = 0;
            int flatOrdinal = 0;

            IReadOnlyList<OcctCell> resultCells = result?.Cells ?? new List<OcctCell>();
            for (int cellIndex = 0; cellIndex < resultCells.Count; cellIndex++)
            {
                OcctCell cell = resultCells[cellIndex];
                cells.Add(new ResolvedCell(cellIndex, cell?.Volume ?? double.NaN, cell?.Center));

                IReadOnlyList<OcctCellFace> faces = cell?.Faces;
                if (faces == null)
                {
                    continue;
                }

                foreach (OcctCellFace face in faces)
                {
                    // Replicate the solver's flatten filter EXACTLY so the flat ordinal indexes into
                    // the same ResolvedFace3Ds ordering (see class remarks). An invalid face does not
                    // advance the ordinal - the bridge is never arithmetic from per-cell counts.
                    bool valid = face?.Face3D != null && face.Face3D.IsValid();
                    int ordinal = valid ? flatOrdinal : -1;
                    if (valid)
                    {
                        flatOrdinal++;
                    }

                    if (face == null)
                    {
                        continue;
                    }

                    if (face.TopologyKey == 0)
                    {
                        topologyKeyZeroFaceCount++;
                        continue; // no per-decode identity - not a unique shared/envelope face
                    }

                    if (!faceByKey.TryGetValue(face.TopologyKey, out FaceBuilder builder))
                    {
                        builder = new FaceBuilder(face.Face3D, face.TopologyKey);
                        faceByKey[face.TopologyKey] = builder;
                        orderedFaces.Add(builder);
                    }

                    builder.OwnerCellIndices.Add(cellIndex);
                    builder.FlatOrdinals.Add(ordinal);
                }
            }

            List<ResolvedCellFace> faceList = orderedFaces.ConvertAll(x => x.Build());

            List<ResolvedFaceAdjacency> adjacencies = (result?.FaceAdjacencies ?? new List<OcctCellFaceAdjacency>())
                .Where(x => x != null)
                .Select(x => new ResolvedFaceAdjacency(x.TopologyKey, x.CellIndex1, x.CellIndex2))
                .ToList();

            return new ResolvedCellComplex(solveId, cells, faceList, adjacencies, nakedWires, topologyKeyZeroFaceCount);
        }

        /// <summary>Returns a copy of this complex with its naked wires replaced. Used by the managed solve
        /// path, which projects the cells/faces/adjacencies from the adopted decode but only measures the
        /// naked wires afterwards (its single outward validate). Cells/faces/adjacencies are immutable, so
        /// they are shared, not copied.</summary>
        public ResolvedCellComplex WithNakedWires(IEnumerable<OcctNakedWire> nakedWires)
        {
            return new ResolvedCellComplex(SolveId, Cells, Faces, Adjacencies, nakedWires, TopologyKeyZeroFaceCount);
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jsonObject = new JsonObject
            {
                ["_type"] = GetType().FullName,
                ["SolveId"] = SolveId.ToString(),
                ["TopologyKeyZeroFaceCount"] = TopologyKeyZeroFaceCount
            };

            JsonArray cellsArray = new JsonArray();
            foreach (ResolvedCell cell in Cells)
            {
                cellsArray.Add(cell.ToJsonObject());
            }
            jsonObject["Cells"] = cellsArray;

            JsonArray facesArray = new JsonArray();
            foreach (ResolvedCellFace face in Faces)
            {
                facesArray.Add(face.ToJsonObject());
            }
            jsonObject["Faces"] = facesArray;

            JsonArray adjacenciesArray = new JsonArray();
            foreach (ResolvedFaceAdjacency adjacency in Adjacencies)
            {
                adjacenciesArray.Add(adjacency.ToJsonObject());
            }
            jsonObject["Adjacencies"] = adjacenciesArray;

            JsonArray nakedWiresArray = new JsonArray();
            foreach (OcctNakedWire nakedWire in NakedWires)
            {
                nakedWiresArray.Add(NakedWireToJson(nakedWire));
            }
            jsonObject["NakedWires"] = nakedWiresArray;

            return jsonObject;
        }

        public bool FromJsonObject(JsonObject jsonObject)
        {
            if (jsonObject == null)
            {
                return false;
            }

            SolveId = jsonObject["SolveId"] is JsonValue solveIdValue && Guid.TryParse(solveIdValue.ToString(), out Guid solveId) ? solveId : Guid.Empty;
            TopologyKeyZeroFaceCount = jsonObject["TopologyKeyZeroFaceCount"]?.GetValue<int>() ?? 0;

            List<ResolvedCell> cells = new List<ResolvedCell>();
            if (jsonObject["Cells"] is JsonArray cellsArray)
            {
                foreach (JsonNode node in cellsArray)
                {
                    if (node is JsonObject cellObject)
                    {
                        cells.Add(new ResolvedCell(cellObject));
                    }
                }
            }
            Cells = cells;

            List<ResolvedCellFace> faces = new List<ResolvedCellFace>();
            if (jsonObject["Faces"] is JsonArray facesArray)
            {
                foreach (JsonNode node in facesArray)
                {
                    if (node is JsonObject faceObject)
                    {
                        faces.Add(new ResolvedCellFace(faceObject));
                    }
                }
            }
            Faces = faces;

            List<ResolvedFaceAdjacency> adjacencies = new List<ResolvedFaceAdjacency>();
            if (jsonObject["Adjacencies"] is JsonArray adjacenciesArray)
            {
                foreach (JsonNode node in adjacenciesArray)
                {
                    if (node is JsonObject adjacencyObject)
                    {
                        adjacencies.Add(new ResolvedFaceAdjacency(adjacencyObject));
                    }
                }
            }
            Adjacencies = adjacencies;

            List<OcctNakedWire> nakedWires = new List<OcctNakedWire>();
            if (jsonObject["NakedWires"] is JsonArray nakedWiresArray)
            {
                foreach (JsonNode node in nakedWiresArray)
                {
                    if (node is JsonObject nakedWireObject)
                    {
                        OcctNakedWire nakedWire = NakedWireFromJson(nakedWireObject);
                        if (nakedWire != null)
                        {
                            nakedWires.Add(nakedWire);
                        }
                    }
                }
            }
            NakedWires = nakedWires;

            return true;
        }

        private static JsonObject NakedWireToJson(OcctNakedWire nakedWire)
        {
            JsonObject jsonObject = new JsonObject { ["IsClosed"] = nakedWire.IsClosed };

            JsonArray pointsArray = new JsonArray();
            foreach (Point3D point3D in nakedWire.Point3Ds ?? new List<Point3D>())
            {
                pointsArray.Add(point3D?.ToJsonObject());
            }
            jsonObject["Point3Ds"] = pointsArray;

            JsonArray edgeOwnersArray = new JsonArray();
            foreach (int edgeOwner in nakedWire.EdgeOwnerFaceIndices ?? new List<int>())
            {
                edgeOwnersArray.Add(edgeOwner);
            }
            jsonObject["EdgeOwnerFaceIndices"] = edgeOwnersArray;

            return jsonObject;
        }

        private static OcctNakedWire NakedWireFromJson(JsonObject jsonObject)
        {
            bool isClosed = jsonObject["IsClosed"]?.GetValue<bool>() ?? false;

            List<Point3D> point3Ds = new List<Point3D>();
            if (jsonObject["Point3Ds"] is JsonArray pointsArray)
            {
                foreach (JsonNode node in pointsArray)
                {
                    if (node is JsonObject pointObject)
                    {
                        point3Ds.Add(new Point3D(pointObject));
                    }
                }
            }

            List<int> edgeOwnerFaceIndices = new List<int>();
            if (jsonObject["EdgeOwnerFaceIndices"] is JsonArray edgeOwnersArray)
            {
                foreach (JsonNode node in edgeOwnersArray)
                {
                    edgeOwnerFaceIndices.Add(node?.GetValue<int>() ?? 0);
                }
            }

            return new OcctNakedWire(point3Ds, isClosed, edgeOwnerFaceIndices);
        }

        private sealed class FaceBuilder
        {
            public Face3D Face3D { get; }

            public int TopologyKey { get; }

            public List<int> OwnerCellIndices { get; } = new List<int>();

            public List<int> FlatOrdinals { get; } = new List<int>();

            public FaceBuilder(Face3D face3D, int topologyKey)
            {
                Face3D = face3D;
                TopologyKey = topologyKey;
            }

            public ResolvedCellFace Build()
            {
                return new ResolvedCellFace(Face3D, TopologyKey, OwnerCellIndices, FlatOrdinals);
            }
        }
    }
}
