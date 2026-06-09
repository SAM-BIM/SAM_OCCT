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
        /// Rebuilds and repairs each supplied closed shell through OCCT. When
        /// <paramref name="minArea"/> is greater than zero, the tiny sliver faces
        /// (surface area below <paramref name="minArea"/> m²) that
        /// <c>SAMOCCT.ShellsSectionByPlane</c> can leave behind — e.g. an
        /// 0.000079 m² face — are removed with OCCT defeaturing
        /// (<c>BRepAlgoAPI_Defeaturing</c>), which extends the neighbouring faces
        /// to fill the gap so each shell stays a closed solid. See issue #11.
        /// </summary>
        /// <param name="shells">Closed shells to repair.</param>
        /// <param name="result">OCCT diagnostics for the repair.</param>
        /// <param name="options">OCCT build options (tolerance / fuzzy tolerance).</param>
        /// <param name="minArea">Minimum acceptable face area in m². Faces below this are defeatured away. Pass 0 (default) to repair without removing any face.</param>
        /// <returns>The repaired shells, or null when no valid shells were supplied.</returns>
        public static List<Shell> ShellsRepair(IEnumerable<Shell> shells, out OcctCellComplexResult result, OcctBuildOptions options = null, double minArea = 0.0)
        {
            result = new OcctCellComplexResult();

            List<Shell> shells_Temp = shells?.Where(x => x != null).ToList();
            if (shells_Temp == null || shells_Temp.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_REPAIR_INPUT_EMPTY", "No shells were supplied.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);

            List<Shell> resultShells = minArea > 0
                ? RepairWithDefeaturing(shells_Temp, options, minArea, result)
                : RepairPerShell(shells_Temp, options, result);

            if (resultShells == null || resultShells.Count == 0)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_REPAIR_FAILED", "OCCT could not repair any supplied shells.");
                return null;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_REPAIR_SUCCESS", string.Format("Repaired {0} input shell(s) into {1} shell(s).", shells_Temp.Count, resultShells.Count));
            return resultShells;
        }

        public static List<Shell> ShellsRepair(IEnumerable<Shell> shells, out OcctCellComplexResult result, double tolerance = Tolerance.Distance, double minArea = 0.0)
        {
            return ShellsRepair(shells, out result, new OcctBuildOptions { Tolerance = tolerance }, minArea);
        }

        /// <summary>
        /// Repairs each shell independently through an OCCT rebuild (no face
        /// removal). This is the behaviour used when no minimum face area is set.
        /// </summary>
        private static List<Shell> RepairPerShell(List<Shell> shells, OcctBuildOptions options, OcctCellComplexResult result)
        {
            List<Shell> resultShells = new List<Shell>();
            for (int i = 0; i < shells.Count; i++)
            {
                List<Shell> repairedShells = ShellsUnion(new Shell[] { shells[i] }, out OcctCellComplexResult repairResult, options);
                if (repairResult?.Diagnostics != null)
                {
                    foreach (OcctDiagnostic diagnostic in repairResult.Diagnostics)
                    {
                        result.AddDiagnostic(diagnostic.Severity, diagnostic.Code, diagnostic.Message, i);
                    }
                }

                if (repairedShells != null)
                {
                    resultShells.AddRange(repairedShells);
                }
            }

            return resultShells;
        }

        /// <summary>
        /// Repairs the shells while removing faces below <paramref name="minArea"/>
        /// using OCCT defeaturing in the native layer. The neighbouring faces are
        /// extended to close the gap, so unlike a managed delete-and-rebuild the
        /// solid never opens up.
        /// </summary>
        private static List<Shell> RepairWithDefeaturing(List<Shell> shells, OcctBuildOptions options, double minArea, OcctCellComplexResult result)
        {
            int smallFaceCount = 0;
            foreach (Shell shell in shells)
            {
                RemoveSmallFace3Ds(shell?.Face3Ds, minArea, out int removed);
                smallFaceCount += removed;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_REPAIR_SMALL_FACES", string.Format("Detected {0} input face(s) below {1:0.######} m²; removing them with OCCT defeaturing so each shell stays closed.", smallFaceCount, minArea));

            if (!Native.OcctCellComplexBuilder.TryRepair(shells, options, minArea, result))
            {
                return null;
            }

            return result.Shells?.ToList();
        }

        /// <summary>
        /// Returns the supplied faces with tiny sliver faces (area below
        /// <paramref name="minArea"/> m²) filtered out. Pure managed geometry
        /// helper, used to report how many faces a repair will defeature and
        /// unit-testable without native OCCT.
        /// </summary>
        /// <param name="face3Ds">Faces to filter.</param>
        /// <param name="minArea">Minimum acceptable face area in m². Faces with a smaller area are dropped.</param>
        /// <param name="removedCount">The number of faces that were removed.</param>
        /// <returns>The retained faces, or null when no faces were supplied.</returns>
        public static List<Face3D> RemoveSmallFace3Ds(IEnumerable<Face3D> face3Ds, double minArea, out int removedCount)
        {
            removedCount = 0;

            List<Face3D> face3Ds_Temp = face3Ds?.Where(x => x != null).ToList();
            if (face3Ds_Temp == null)
            {
                return null;
            }

            if (minArea <= 0)
            {
                return face3Ds_Temp;
            }

            List<Face3D> result = new List<Face3D>();
            foreach (Face3D face3D in face3Ds_Temp)
            {
                if (face3D.GetArea() < minArea)
                {
                    removedCount++;
                    continue;
                }

                result.Add(face3D);
            }

            return result;
        }
    }
}
