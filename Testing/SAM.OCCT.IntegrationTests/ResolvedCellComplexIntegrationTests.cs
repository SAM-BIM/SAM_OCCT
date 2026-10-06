// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Analytical;
using SAM.Analytical.OCCT.Solver;
using SAM.Core;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

using AnalyticalOcctCreate = SAM.Analytical.OCCT.Create;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// Native-gated coverage for Phase P2: the solve carries its adopted cell complex on the report, and the
    /// public <see cref="AnalyticalOcctCreate.AdjacencyCluster(IEnumerable{Panel}, ResolvedCellComplex, out List{string}, double, double, double, double, IEnumerable{int})"/>
    /// overload consumes it directly (no rebuild), inheriting panel identity from the supplied panels.
    /// </summary>
    public class ResolvedCellComplexIntegrationTests
    {
        private static readonly string FixturesDirectory = Path.Combine(System.AppContext.BaseDirectory, "Fixtures");

        private static List<Panel> LoadPanels(string path)
        {
            List<IJSAMObject> objects = SAM.Core.Convert.ToSAM(path);
            List<Panel> result = new List<Panel>();
            foreach (IJSAMObject sAMObject in objects ?? new List<IJSAMObject>())
            {
                switch (sAMObject)
                {
                    case AnalyticalModel analyticalModel: result.AddRange(analyticalModel.GetPanels() ?? new List<Panel>()); break;
                    case AdjacencyCluster adjacencyCluster: result.AddRange(adjacencyCluster.GetPanels() ?? new List<Panel>()); break;
                    case Panel panel: result.Add(panel); break;
                }
            }

            return result.Where(x => x != null && x.PanelType != PanelType.Air && x.GetFace3D() != null && x.GetFace3D().IsValid()).ToList();
        }

        [SkippableFact]
        public void Solve3D_FlatFixture_ReportCarriesResolvedCellComplex()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, "whole-level-flat.sam");
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> panels = LoadPanels(path);
            panels.Solve3D(out List<Point3D> _, out List<string> _, out _, out Solve3DReport report);

            Assert.NotNull(report.ResolvedCellComplex);
            // Raw-first adopts flat at 22 cells / 0 naked: the complex reflects that decode exactly.
            Assert.Equal(22, report.ResolvedCellComplex.Cells.Count);
            Assert.True(report.ResolvedCellComplex.Faces.Count > 0);
            Assert.Empty(report.ResolvedCellComplex.NakedWires); // watertight adopted raw solve
            Assert.NotEqual(System.Guid.Empty, report.ResolvedCellComplex.SolveId);
            // Every unique face carries 1 (envelope) or 2 (shared) owner cells - never zero, never three-plus.
            Assert.All(report.ResolvedCellComplex.Faces, face => Assert.InRange(face.OwnerCellIndices.Count, 1, 2));
            // Flat ordinals align with owners (one ordinal per owner occurrence).
            Assert.All(report.ResolvedCellComplex.Faces, face => Assert.Equal(face.OwnerCellIndices.Count, face.FlatOrdinals.Count));
        }

        [SkippableFact]
        public void Solve3D_ManagedPath_ReportCarriesResolvedCellComplex()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, "whole-level-flat.sam");
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> panels = LoadPanels(path);
            // forceManagedPipeline exercises the FinalizeAndValidate capture path (flat closes managed at 22/0).
            panels.Solve3D(out List<Point3D> _, out List<string> _, out _, out Solve3DReport report, forceManagedPipeline: true);

            Assert.NotNull(report.ResolvedCellComplex);
            Assert.Equal(22, report.ResolvedCellComplex.Cells.Count);
            Assert.True(report.ResolvedCellComplex.Faces.Count > 0);
        }

        [SkippableFact]
        public void AdjacencyCluster_FromComplex_ConsumesDirectlyAndInheritsIdentity()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, "whole-level-flat.sam");
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> panels = LoadPanels(path);
            List<Panel> solved = panels.Solve3D(out List<Point3D> _, out List<string> _, out _, out Solve3DReport report);
            List<Panel> nonAirSolved = solved.Where(x => x != null && x.PanelType != PanelType.Air).ToList();
            Assert.NotNull(report.ResolvedCellComplex);

            // Consume the complex directly - no native rebuild.
            AdjacencyCluster cluster = AnalyticalOcctCreate.AdjacencyCluster(nonAirSolved, report.ResolvedCellComplex, out List<string> diagnostics);

            Assert.NotNull(cluster);
            Assert.Equal(22, cluster.GetSpaces().Count);
            Assert.All(cluster.GetSpaces(), space => Assert.NotEmpty(cluster.GetPanels(space)));

            // The consume path reported itself, and inherited identity from the supplied panels on the
            // unambiguous matches (never mis-attributed).
            Assert.Contains(diagnostics, d => d.Contains("SAM_OCCT_ANALYTICAL_COMPLEX_CONSUMED"));
            Assert.Contains(diagnostics, d => d.Contains("SAM_OCCT_ANALYTICAL_PANEL_IDENTITY"));

            // At least some panels carry a source panel's Guid (identity inherited), proving the attribution
            // wired through rather than defaulting everything.
            HashSet<System.Guid> sourceGuids = new HashSet<System.Guid>(nonAirSolved.Select(x => x.Guid));
            int inherited = cluster.GetPanels().Count(p => sourceGuids.Contains(p.Guid));
            Assert.True(inherited > 0, "Expected at least one panel to inherit a supplied panel's Guid.");
        }

        [SkippableFact]
        public void AdjacencyCluster_FromComplex_ExcludeCellIndices_OmitsThoseSpaces()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");
            string path = Path.Combine(FixturesDirectory, "whole-level-flat.sam");
            Skip.IfNot(File.Exists(path), "Fixture not found: " + path);

            List<Panel> panels = LoadPanels(path);
            panels.Solve3D(out List<Point3D> _, out List<string> _, out _, out Solve3DReport report);
            Assert.NotNull(report.ResolvedCellComplex);

            // Exclude two cells by INDEX: exactly two fewer spaces, and no relation references them.
            AdjacencyCluster cluster = AnalyticalOcctCreate.AdjacencyCluster(null, report.ResolvedCellComplex, out List<string> _, excludeCellIndices: new List<int> { 0, 5 });

            Assert.NotNull(cluster);
            Assert.Equal(20, cluster.GetSpaces().Count);
        }
    }
}
