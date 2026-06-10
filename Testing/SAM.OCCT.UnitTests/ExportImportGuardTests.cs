// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.IO;
using Xunit;

// Both SAM.Core.OCCT and SAM.Geometry.OCCT declare static Create/Export classes.
using GeometryCreate = SAM.Geometry.OCCT.Create;
using GeometryExport = SAM.Geometry.OCCT.Export;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Pure-managed guard tests for the STEP/IGES import/export API (issue #20).
    /// Every path here is rejected before the native library is touched, so they
    /// run on the OCCT-less CI agent.
    /// </summary>
    public class ExportImportGuardTests
    {
        [Fact]
        public void ExportToFile_NullShells_ReturnsFalseWithInputDiagnostic()
        {
            // Act
            bool successful = GeometryExport.ToFile((IEnumerable<Shell>)null, "out.stp", OcctExchangeFormat.Step, out OcctCellComplexResult result);

            // Assert
            Assert.False(successful);
            Assert.NotNull(result);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_EXPORT_INPUT");
        }

        [Fact]
        public void ExportToFile_EmptyShells_ReturnsFalseWithInputDiagnostic()
        {
            // Act
            bool successful = GeometryExport.ToFile(new List<Shell>(), "out.igs", OcctExchangeFormat.Iges, out OcctCellComplexResult result);

            // Assert
            Assert.False(successful);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_EXPORT_INPUT");
        }

        [Fact]
        public void ExportToFile_NullPathWithShells_ReturnsFalseWithInputDiagnostic()
        {
            // Arrange - a non-empty (but never-built) shell list; the null path
            // must be rejected before any native topology build.
            List<Shell> shells = new List<Shell> { new Shell(new List<Face3D>()) };

            // Act
            bool successful = GeometryExport.ToFile(shells, null, OcctExchangeFormat.Step, out OcctCellComplexResult result);

            // Assert
            Assert.False(successful);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_EXPORT_INPUT");
        }

        [Fact]
        public void ExportToFile_NullTopology_ReturnsFalseWithDisposedDiagnostic()
        {
            // Act
            bool successful = GeometryExport.ToFile((OcctTopology)null, "out.stp", OcctExchangeFormat.Step, out OcctCellComplexResult result);

            // Assert
            Assert.False(successful);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_EXPORT_TOPOLOGY_DISPOSED");
        }

        [Fact]
        public void ImportShells_NullPath_ReturnsNullWithInputDiagnostic()
        {
            // Act
            List<Shell> shells = GeometryCreate.Shells((string)null, OcctExchangeFormat.Step, out OcctCellComplexResult result);

            // Assert
            Assert.Null(shells);
            Assert.NotNull(result);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_IMPORT_INPUT");
        }

        [Fact]
        public void ImportTopology_EmptyPath_ReturnsNullWithInputDiagnostic()
        {
            // Act
            OcctTopology topology = GeometryCreate.Topology("   ", OcctExchangeFormat.Iges, out OcctCellComplexResult result);

            // Assert
            Assert.Null(topology);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_IMPORT_INPUT");
        }

        [Fact]
        public void ImportShells_MissingFile_ReturnsNullWithFileMissingDiagnostic()
        {
            // Arrange - a path guaranteed not to exist.
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".stp");

            // Act
            List<Shell> shells = GeometryCreate.Shells(path, OcctExchangeFormat.Step, out OcctCellComplexResult result);

            // Assert - graceful failure before reaching the native library.
            Assert.Null(shells);
            Assert.Contains(result.Diagnostics, x => x.Code == "SAM_OCCT_IMPORT_FILE_MISSING");
        }
    }
}
