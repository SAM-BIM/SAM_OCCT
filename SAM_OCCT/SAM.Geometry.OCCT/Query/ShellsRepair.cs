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
        /// <paramref name="minArea"/> is greater than zero, tiny sliver faces below
        /// that area (in m²) are removed from each repaired shell and the shell is
        /// rebuilt so the gap is healed. This addresses the tiny faces (e.g. an
        /// 0.000079 m² face) that <c>SAMOCCT.ShellsSectionByPlane</c> can leave
        /// behind. See issue #11.
        /// </summary>
        /// <param name="shells">Closed shells to repair.</param>
        /// <param name="result">OCCT diagnostics for the repair.</param>
        /// <param name="options">OCCT build options (tolerance / fuzzy tolerance).</param>
        /// <param name="minArea">Minimum acceptable face area in m². Faces below this are removed and the shell is rebuilt. Pass 0 (default) to keep every face.</param>
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

            List<Shell> resultShells = new List<Shell>();
            for (int i = 0; i < shells_Temp.Count; i++)
            {
                List<Shell> repairedShells = ShellsUnion(new Shell[] { shells_Temp[i] }, out OcctCellComplexResult repairResult, options);
                if (repairResult?.Diagnostics != null)
                {
                    foreach (OcctDiagnostic diagnostic in repairResult.Diagnostics)
                    {
                        result.AddDiagnostic(diagnostic.Severity, diagnostic.Code, diagnostic.Message, i);
                    }
                }

                if (repairedShells == null)
                {
                    continue;
                }

                if (minArea <= 0)
                {
                    resultShells.AddRange(repairedShells);
                    continue;
                }

                foreach (Shell repairedShell in repairedShells)
                {
                    resultShells.Add(RemoveSmallFace3Ds(repairedShell, minArea, options, result, i));
                }
            }

            if (resultShells.Count == 0)
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
        /// Returns the supplied faces with tiny sliver faces (area below
        /// <paramref name="minArea"/> m²) removed. Pure managed geometry helper used
        /// by <see cref="ShellsRepair(IEnumerable{Shell}, out OcctCellComplexResult, OcctBuildOptions, double)"/>
        /// and unit-testable without native OCCT.
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

        /// <summary>
        /// Removes faces below <paramref name="minArea"/> from a single repaired
        /// shell and rebuilds it through OCCT so the resulting volume stays closed.
        /// Falls back to the unmodified shell when nothing tiny is found or the
        /// rebuild cannot reconstruct a closed volume.
        /// </summary>
        private static Shell RemoveSmallFace3Ds(Shell shell, double minArea, OcctBuildOptions options, OcctCellComplexResult result, int sourceIndex)
        {
            List<Face3D> face3Ds = RemoveSmallFace3Ds(shell?.Face3Ds, minArea, out int removedCount);
            if (removedCount == 0 || face3Ds == null || face3Ds.Count == 0)
            {
                return shell;
            }

            List<Shell> rebuiltShells = Create.Shells(face3Ds, out OcctCellComplexResult rebuildResult, options);
            if (rebuildResult?.Diagnostics != null)
            {
                foreach (OcctDiagnostic diagnostic in rebuildResult.Diagnostics)
                {
                    result.AddDiagnostic(diagnostic.Severity, diagnostic.Code, diagnostic.Message, sourceIndex);
                }
            }

            Shell rebuiltShell = rebuiltShells?.FirstOrDefault(x => x != null);
            if (rebuiltShell == null)
            {
                result.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_REPAIR_SMALL_FACES_KEPT", string.Format("Found {0} face(s) below {1:0.######} m² but OCCT could not rebuild the shell without them; the original shell was kept.", removedCount, minArea), sourceIndex);
                return shell;
            }

            result.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_REPAIR_SMALL_FACES_REMOVED", string.Format("Removed {0} face(s) below {1:0.######} m² and rebuilt the shell.", removedCount, minArea), sourceIndex);
            return rebuiltShell;
        }
    }
}
