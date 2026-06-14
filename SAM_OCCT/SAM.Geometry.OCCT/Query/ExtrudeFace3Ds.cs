// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Geometry.OCCT
{
    public static partial class Query
    {
        /// <summary>
        /// Extrudes planar footprint <see cref="Face3D"/>s along a direction
        /// vector via OCCT BRepPrimAPI_MakePrism (issue #30), returning one
        /// closed <see cref="Shell"/> per footprint. Built for the early-design
        /// "draw the floor outline, give it a storey height" workflow: the
        /// resulting shells feed straight into CreateShells / MergeSmallShells /
        /// CreateAdjacencyCluster. Returns null on failure - inspect
        /// <paramref name="result"/> diagnostics.
        /// </summary>
        public static List<Shell> ExtrudeFace3Ds(IEnumerable<Face3D> face3Ds, Vector3D direction, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            result = new OcctCellComplexResult();

            List<Face3D> face3Ds_Temp = face3Ds?.Where(x => x != null).ToList();
            if (face3Ds_Temp == null || face3Ds_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_EXTRUDE_INPUT_EMPTY", "No footprint Face3Ds were supplied.");
                return null;
            }

            if (direction == null || (direction.X == 0 && direction.Y == 0 && direction.Z == 0))
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_EXTRUDE_DIRECTION_ZERO", "The extrusion direction must be a non-zero vector.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            if (!Native.OcctShapeBuilder.TryExtrude(face3Ds_Temp, direction, options, result))
            {
                return null;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_EXTRUDE_SUCCESS", string.Format("Extruded {0} footprint(s) into {1} shell(s).", face3Ds_Temp.Count, result.Shells.Count));
            return result.Shells?.ToList();
        }

        /// <summary>
        /// Extrudes footprints vertically (along +Z) by <paramref name="height"/>.
        /// </summary>
        public static List<Shell> ExtrudeFace3Ds(IEnumerable<Face3D> face3Ds, double height, out OcctCellComplexResult result, OcctBuildOptions options = null)
        {
            return ExtrudeFace3Ds(face3Ds, new Vector3D(0, 0, height), out result, options);
        }
    }
}
