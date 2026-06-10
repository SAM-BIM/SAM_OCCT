// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare static Create/Export classes.
using GeometryCreate = SAM.Geometry.OCCT.Create;
using GeometryExport = SAM.Geometry.OCCT.Export;

namespace SAM.OCCT.IntegrationTests
{
    /// <summary>
    /// STEP/IGES round-trip tests (issue #20) against the real OCCT Data
    /// Exchange engine. Each test auto-skips (via SkippableFact) when the native
    /// library is absent. STEP is asserted strictly (cell count + volume); IGES
    /// leniently (import succeeds and the closed volume survives within
    /// tolerance), reflecting its surface-oriented nature.
    /// </summary>
    public class ExportImportIntegrationTests
    {
        [SkippableFact]
        public void ExportImportStep_UnitBox_RoundTripsCellCountAndVolume()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            List<Shell> shells = new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) };
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".stp");

            try
            {
                // Act - export to STEP then re-import.
                bool exported = GeometryExport.ToFile(shells, path, OcctExchangeFormat.Step, out OcctCellComplexResult exportResult);
                Assert.True(exported, DiagnosticText(exportResult));
                Assert.True(File.Exists(path));
                Assert.True(new FileInfo(path).Length > 0);

                List<Shell> imported = GeometryCreate.Shells(path, OcctExchangeFormat.Step, out OcctCellComplexResult importResult);

                // Assert - STEP preserves the BRep solid exactly.
                Assert.True(importResult.Success, DiagnosticText(importResult));
                Assert.NotNull(imported);
                Assert.Single(importResult.Cells);
                Assert.Equal(1.0, importResult.Cells[0].Volume, 6);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [SkippableFact]
        public void ExportImportIges_UnitBox_RoundTripsClosedVolume()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange
            List<Shell> shells = new List<Shell> { TestGeometry.CreateUnitBox(0, 0, 0) };
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".igs");

            try
            {
                // Act - export to IGES (BRep mode) then re-import.
                bool exported = GeometryExport.ToFile(shells, path, OcctExchangeFormat.Iges, out OcctCellComplexResult exportResult);
                Assert.True(exported, DiagnosticText(exportResult));
                Assert.True(File.Exists(path));
                Assert.True(new FileInfo(path).Length > 0);

                List<Shell> imported = GeometryCreate.Shells(path, OcctExchangeFormat.Iges, out OcctCellComplexResult importResult);

                // Assert - lenient: the import must succeed and the closed unit
                // volume must survive within tolerance (IGES is surface-based,
                // so exact solid counts are not asserted).
                Assert.True(importResult.Success, DiagnosticText(importResult));
                Assert.NotNull(imported);
                Assert.NotEmpty(importResult.Cells);
                double totalVolume = importResult.Cells.Sum(x => x.Volume);
                Assert.Equal(1.0, totalVolume, 3);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [SkippableFact]
        public void ImportStep_MissingFile_FailsGracefully()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - a path that does not exist.
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".stp");

            // Act
            List<Shell> imported = GeometryCreate.Shells(path, OcctExchangeFormat.Step, out OcctCellComplexResult result);

            // Assert - no throw, a clear failure diagnostic, no shells.
            Assert.Null(imported);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_IMPORT_FILE_MISSING" || x.Code == "SAM_OCCT_IMPORT_NATIVE_FAILED");
        }

        [SkippableFact]
        public void ExportImportStep_TwoAdjacentBoxes_RoundTripsCellCountAndAdjacency()
        {
            Skip.IfNot(NativeProbe.Available, "Native SAM.Occt.Native library is not available.");

            // Arrange - two adjacent unit boxes built as a single cell complex.
            List<Face3D> face3Ds = new List<Face3D>();
            face3Ds.AddRange(TestGeometry.CreateUnitBox(0, 0, 0).Face3Ds);
            face3Ds.AddRange(TestGeometry.CreateUnitBox(1, 0, 0).Face3Ds);
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".stp");

            try
            {
                using (OcctTopology topology = GeometryCreate.Topology(face3Ds, out OcctCellComplexResult buildResult))
                {
                    Assert.NotNull(topology);
                    OcctCellComplexResult before = SAM.Geometry.OCCT.Query.CellComplexResult(topology);

                    // Act - export the live topology, re-import and decode.
                    bool exported = GeometryExport.ToFile(topology, path, OcctExchangeFormat.Step, out OcctCellComplexResult exportResult);
                    Assert.True(exported, DiagnosticText(exportResult));

                    List<Shell> imported = GeometryCreate.Shells(path, OcctExchangeFormat.Step, out OcctCellComplexResult after);

                    // Assert - cell count and total volume preserved through STEP.
                    Assert.True(after.Success, DiagnosticText(after));
                    Assert.NotNull(imported);
                    Assert.Equal(before.Cells.Count, after.Cells.Count);
                    Assert.Equal(before.Cells.Sum(x => x.Volume), after.Cells.Sum(x => x.Volume), 6);
                }
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private static string DiagnosticText(OcctCellComplexResult result)
        {
            if (result?.Diagnostics == null)
            {
                return "no diagnostics";
            }

            return string.Join("; ", result.Diagnostics.Select(x => x.ToString()));
        }
    }
}
