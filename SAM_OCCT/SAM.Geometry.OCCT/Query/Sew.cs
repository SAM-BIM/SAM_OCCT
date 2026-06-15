// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// Sews a face soup into the tightest closed shell/solid OCCT can make
        /// (issue #37): BRepBuilderAPI_Sewing -> ShapeFix_Wireframe (FixSmallEdges
        /// / FixWireGaps) -> ShapeFix_Shell -> ShapeFix_Solid ->
        /// ShapeUpgrade_UnifySameDomain. Unlike <see cref="Create.Shells(System.Collections.Generic.IEnumerable{Face3D}, out OcctCellComplexResult, OcctBuildOptions)"/>
        /// this does NOT require the faces to already bound a volume - it is the
        /// closing step that makes them bound one, so it recovers the
        /// triangulated / near-touching face soups that defeat a direct
        /// BOPAlgo_MakerVolume. Returns null on failure; inspect
        /// <paramref name="result"/> diagnostics (SAM_OCCT_SEW_*).
        /// </summary>
        /// <param name="face3Ds">The face soup to sew and heal.</param>
        /// <param name="result">OCCT diagnostics for the sew-and-heal.</param>
        /// <param name="options">OCCT build options; <see cref="OcctBuildOptions.SewingTolerance"/> controls the join distance.</param>
        /// <param name="makeSolid">When true (default) each healed closed shell becomes a solid that decodes into a cell; pass false only to heal a shell for re-use.</param>
        /// <returns>The healed shells, or null when sewing produced no closed shell.</returns>
        public static List<Shell> Sew(IEnumerable<Face3D> face3Ds, out OcctCellComplexResult result, OcctBuildOptions options = null, bool makeSolid = true)
        {
            result = new OcctCellComplexResult();

            List<Face3D> face3Ds_Temp = face3Ds?.Where(x => x != null).ToList();
            if (face3Ds_Temp == null || face3Ds_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_SEW_INPUT_EMPTY", "No Face3D geometry was supplied.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctShapeBuilder.TrySew(face3Ds_Temp, makeSolid, options, result))
            {
                return null;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_SEW_SUCCESS", string.Format("Sewed {0} face(s) into {1} shell(s).", face3Ds_Temp.Count, result.Cells.Count));
            return result.Shells?.ToList();
        }

        public static List<Shell> Sew(IEnumerable<Face3D> face3Ds, out OcctCellComplexResult result, double tolerance = Tolerance.Distance, bool makeSolid = true)
        {
            return Sew(face3Ds, out result, new OcctBuildOptions { Tolerance = tolerance }, makeSolid);
        }

        /// <summary>
        /// Shell overload of <see cref="Sew(IEnumerable{Face3D}, out OcctCellComplexResult, OcctBuildOptions, bool)"/>:
        /// sews+heals the faces of the supplied shells (e.g. to close micro-gaps a
        /// boolean / offset left behind). Returns null on failure; inspect
        /// <paramref name="result"/> diagnostics (SAM_OCCT_SEW_*).
        /// </summary>
        public static List<Shell> Sew(IEnumerable<Shell> shells, out OcctCellComplexResult result, OcctBuildOptions options = null, bool makeSolid = true)
        {
            result = new OcctCellComplexResult();

            List<Shell> shells_Temp = shells?.Where(x => x != null).ToList();
            if (shells_Temp == null || shells_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_SEW_INPUT_EMPTY", "No shells were supplied.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctShapeBuilder.TrySew(shells_Temp, makeSolid, options, result))
            {
                return null;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_SEW_SUCCESS", string.Format("Sewed {0} shell(s) into {1} shell(s).", shells_Temp.Count, result.Cells.Count));
            return result.Shells?.ToList();
        }

        public static List<Shell> Sew(IEnumerable<Shell> shells, out OcctCellComplexResult result, double tolerance = Tolerance.Distance, bool makeSolid = true)
        {
            return Sew(shells, out result, new OcctBuildOptions { Tolerance = tolerance }, makeSolid);
        }
    }
}
