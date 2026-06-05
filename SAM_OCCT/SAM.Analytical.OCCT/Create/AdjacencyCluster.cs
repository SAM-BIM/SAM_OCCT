// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Linq;

namespace SAM.Analytical.OCCT
{
    public static partial class Create
    {
        public static AdjacencyCluster AdjacencyCluster(IEnumerable<Space> spaces, IEnumerable<Panel> panels, out OcctCellComplexResult cellComplexResult, Log log = null, OcctBuildOptions options = null, double thinnessRatio = 0.01, double minArea = Tolerance.MacroDistance, double maxDistance = 0.1, double maxAngle = 0.0872664626)
        {
            cellComplexResult = null;

            List<Panel> panels_Temp = panels?.Where(x => x != null).ToList();
            if (panels_Temp == null || panels_Temp.Count == 0)
            {
                cellComplexResult = new OcctCellComplexResult();
                cellComplexResult.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_ANALYTICAL_INPUT_EMPTY", "No panels were supplied for adjacency cluster creation.");
                return null;
            }

            options = options == null ? new OcctBuildOptions() : new OcctBuildOptions(options);
            List<Space> spaces_Temp = spaces?.Where(x => x != null).ToList();

            List<Shell> shells = Geometry.OCCT.Create.CellComplexByPanels(panels_Temp, out cellComplexResult, options);
            if (shells == null || shells.Count == 0)
            {
                if (log != null && cellComplexResult?.Diagnostics != null)
                {
                    foreach (OcctDiagnostic diagnostic in cellComplexResult.Diagnostics)
                    {
                        Core.Modify.Add(log, diagnostic.ToString());
                    }
                }

                return null;
            }

            cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_ANALYTICAL_INPUT", string.Format("Received {0} panel(s) and {1} seed space(s) for OCCT adjacency creation.", panels_Temp.Count, spaces_Temp?.Count ?? 0));

            if (log != null)
            {
                Core.Modify.Add(log, "OCCT created {0} closed shell(s). Rebuilding SAM adjacency cluster.", shells.Count);
            }

            cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_ANALYTICAL_REBUILD", string.Format("Rebuilding SAM adjacency cluster from {0} OCCT shell(s).", shells.Count));
            if (spaces_Temp != null && spaces_Temp.Count != 0 && spaces_Temp.Count != shells.Count)
            {
                cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_ANALYTICAL_CELL_SPACE_DELTA", string.Format("Seed space count ({0}) differs from OCCT cell count ({1}). This usually means OCCT merged, rejected, or could not close at least one intended cell.", spaces_Temp.Count, shells.Count));
            }

            AdjacencyCluster adjacencyCluster = global::SAM.Analytical.Create.AdjacencyCluster(
                shells,
                spaces_Temp,
                panels_Temp,
                addMissingSpaces: true,
                addMissingPanels: true,
                thinnessRatio: thinnessRatio,
                minArea: minArea,
                maxDistance: maxDistance,
                maxAngle: maxAngle,
                silverSpacing: options.FuzzyTolerance,
                tolerance_Distance: options.Tolerance);

            if (log != null)
            {
                if (adjacencyCluster == null)
                {
                    Core.Modify.Add(log, "SAM adjacency cluster rebuild failed.");
                }
                else
                {
                    Core.Modify.Add(log, "SAM adjacency cluster created with {0} space(s) and {1} panel(s).", adjacencyCluster.GetSpaces()?.Count ?? 0, adjacencyCluster.GetPanels()?.Count ?? 0);
                }
            }

            if (adjacencyCluster == null)
            {
                cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_ANALYTICAL_REBUILD_FAILED", "OCCT produced closed shells, but SAM could not rebuild an adjacency cluster from them.");
            }
            else
            {
                int adjacencySpaceCount = adjacencyCluster.GetSpaces()?.Count ?? 0;
                int adjacencyPanelCount = adjacencyCluster.GetPanels()?.Count ?? 0;
                cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Info, "SAM_OCCT_ANALYTICAL_SUCCESS", string.Format("Created SAM adjacency cluster with {0} space(s) and {1} panel(s).", adjacencySpaceCount, adjacencyPanelCount));

                if (adjacencySpaceCount != shells.Count)
                {
                    cellComplexResult?.AddDiagnostic(OcctDiagnosticSeverity.Warning, "SAM_OCCT_ANALYTICAL_REBUILD_SPACE_DELTA", string.Format("SAM adjacency space count ({0}) differs from OCCT shell count ({1}). This usually means SAM merged, rejected, or could not assign one or more rebuilt cells.", adjacencySpaceCount, shells.Count));
                }
            }

            return adjacencyCluster;
        }
    }
}
