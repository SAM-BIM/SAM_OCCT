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
        /// Merges adjacent coplanar Face3Ds into fewer, larger Face3Ds using OCCT
        /// (ShapeUpgrade_UnifySameDomain). Input faces are sewn so coincident edges become
        /// shared topology, then same-plane neighbours are unified and redundant edges removed.
        /// </summary>
        /// <param name="face3Ds">Faces to merge. Disjoint faces and faces on different planes are kept separate.</param>
        /// <param name="result">OCCT diagnostics and native availability.</param>
        /// <param name="angularTolerance">Maximum angle (radians) between face normals still treated as coplanar.</param>
        /// <param name="options">OCCT build options; Tolerance is used as the sewing/linear tolerance.</param>
        public static List<Face3D> MergeCoplanarFace3Ds(IEnumerable<Face3D> face3Ds, out OcctCellComplexResult result, double angularTolerance = Tolerance.Angle, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            List<Face3D> face3Ds_Temp = face3Ds?.Where(x => x != null).ToList();
            if (face3Ds_Temp == null || face3Ds_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_INPUT_EMPTY", "No Face3D geometry was supplied.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctCellComplexBuilder.TryMergeCoplanar(face3Ds_Temp, options, angularTolerance, result, out List<Face3D> merged))
            {
                return null;
            }

            return merged;
        }

        /// <summary>
        /// Merges adjacent coplanar faces of a Shell into fewer, larger faces using OCCT and
        /// returns a rebuilt Shell. The closed volume is preserved; only the face count changes.
        /// </summary>
        public static Shell MergeCoplanar(Shell shell, out OcctCellComplexResult result, double angularTolerance = Tolerance.Angle, OcctBuildOptions options = null)
        {
            List<Face3D> merged = MergeCoplanarFace3Ds(shell?.Face3Ds, out result, angularTolerance, options);
            if (merged == null || merged.Count == 0)
            {
                return null;
            }

            return new Shell(merged);
        }
    }
}
