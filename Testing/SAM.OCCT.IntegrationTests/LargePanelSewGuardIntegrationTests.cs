// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Core;
using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;

namespace SAM.OCCT.IntegrationTests
{
    public class LargePanelSewGuardIntegrationTests
    {
        private const string FixtureEnvironmentVariable = "SAM_OCCT_LARGE_PANEL_FIXTURE";

        [SkippableFact]
        public void AdjacencyCluster_LargePanelSoupWithSewBeforeBuild_SkipsNativeSew()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            string path = Environment.GetEnvironmentVariable(FixtureEnvironmentVariable);
            Skip.If(string.IsNullOrWhiteSpace(path) || !File.Exists(path), FixtureEnvironmentVariable + " was not set to an existing .sam file.");

            List<Panel> panels = LoadPanels(path);
            OcctBuildOptions options = new OcctBuildOptions
            {
                Tolerance = Tolerance.Distance,
                FuzzyTolerance = Tolerance.MacroDistance,
                AvoidInternalShapes = false,
                SewBeforeBuild = true,
                SewingTolerance = 0.01
            };

            Skip.If(panels.Count <= options.MaxSewFaceCount, "Fixture is not large enough to exercise the sew guard.");

            AnalyticalOcctCreate.AdjacencyCluster(null, panels, out OcctCellComplexResult result, null, options);

            Assert.NotNull(result);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_SEW_SKIPPED_LARGE_INPUT");
        }

        private static List<Panel> LoadPanels(string path)
        {
            List<IJSAMObject> objects = SAM.Core.Convert.ToSAM(path);
            List<Panel> result = new List<Panel>();

            foreach (IJSAMObject sAMObject in objects ?? new List<IJSAMObject>())
            {
                switch (sAMObject)
                {
                    case AnalyticalModel analyticalModel:
                        result.AddRange(analyticalModel.GetPanels() ?? new List<Panel>());
                        break;

                    case AdjacencyCluster adjacencyCluster:
                        result.AddRange(adjacencyCluster.GetPanels() ?? new List<Panel>());
                        break;

                    case Panel panel:
                        result.Add(panel);
                        break;
                }
            }

            return result
                .Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid())
                .ToList();
        }
    }
}
