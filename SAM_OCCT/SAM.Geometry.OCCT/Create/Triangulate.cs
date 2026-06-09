// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    public static partial class Create
    {
        /// <summary>
        /// Triangulates the boundary of each input face into planar SAM Triangle3Ds using OCCT.
        /// Coplanar boundaries are meshed as a plane; non-planar (warped) boundaries are spanned
        /// with an OCCT filling surface and meshed into planar triangles. Each Triangle3D is planar
        /// by definition and can be turned into a Face3D with <c>new Face3D(triangle3D)</c>.
        /// </summary>
        /// <param name="face3Ds">Source faces. Their boundary loops may be non-planar.</param>
        /// <param name="result">OCCT diagnostics and native availability.</param>
        /// <param name="linearDeflection">Maximum chord deviation from the true surface; larger values give fewer, bigger planar panels.</param>
        /// <param name="angularDeflection">Maximum angular deviation (radians) used along curved boundaries.</param>
        /// <param name="relativeDeflection">When true, OCCT treats <paramref name="linearDeflection"/> as relative to each face size.</param>
        /// <param name="options">OCCT build options (tolerance is reused for triangle de-duplication keys).</param>
        public static List<Triangle3D> Triangulate(IEnumerable<Face3D> face3Ds, out OcctCellComplexResult result, double linearDeflection = 0.1, double angularDeflection = 0.5, bool relativeDeflection = false, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            List<Face3D> face3Ds_Temp = face3Ds?.Where(x => x != null).ToList();
            if (face3Ds_Temp == null || face3Ds_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_INPUT_EMPTY", "No Face3D geometry was supplied.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctCellComplexBuilder.TryTriangulate(face3Ds_Temp, options, linearDeflection, angularDeflection, relativeDeflection, result, out List<Triangle3D> triangles))
            {
                return null;
            }

            return triangles;
        }

        public static List<Triangle3D> Triangulate(IEnumerable<Face3D> face3Ds, out OcctCellComplexResult result, double linearDeflection, double tolerance)
        {
            return Triangulate(face3Ds, out result, linearDeflection, 0.5, false, new OcctBuildOptions { Tolerance = tolerance });
        }

        /// <summary>
        /// Triangulates raw (possibly non-planar) boundary loops into planar SAM Triangle3Ds using OCCT.
        /// Each loop is the outer boundary of one surface; the points are passed to OCCT unchanged, so a
        /// warped boundary keeps its true corners. Coplanar loops are meshed as a plane and non-planar loops
        /// are spanned by an OCCT filling surface before meshing. Convert with <c>new Face3D(triangle3D)</c>.
        /// </summary>
        /// <param name="boundaryLoops">Outer boundary loops, one per surface. Points may be non-planar.</param>
        /// <param name="result">OCCT diagnostics and native availability.</param>
        /// <param name="linearDeflection">Maximum chord deviation from the true surface; larger values give fewer, bigger planar panels.</param>
        /// <param name="angularDeflection">Maximum angular deviation (radians) used along curved boundaries.</param>
        /// <param name="relativeDeflection">When true, OCCT treats <paramref name="linearDeflection"/> as relative to each face size.</param>
        /// <param name="options">OCCT build options (tolerance is reused for loop point de-duplication).</param>
        public static List<Triangle3D> Triangulate(IEnumerable<IReadOnlyList<Point3D>> boundaryLoops, out OcctCellComplexResult result, double linearDeflection = 0.1, double angularDeflection = 0.5, bool relativeDeflection = false, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            List<IReadOnlyList<Point3D>> boundaryLoops_Temp = boundaryLoops?.Where(x => x != null && x.Count >= 3).ToList();
            if (boundaryLoops_Temp == null || boundaryLoops_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_INPUT_EMPTY", "No boundary loops were supplied.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctCellComplexBuilder.TryTriangulate(boundaryLoops_Temp, options, linearDeflection, angularDeflection, relativeDeflection, result, out List<Triangle3D> triangles))
            {
                return null;
            }

            return triangles;
        }
    }
}
