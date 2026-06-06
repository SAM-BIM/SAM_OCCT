// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    public class OcctDiagnosticTests
    {
        [Fact]
        public void Constructor_WithValues_ExposesProperties()
        {
            // Arrange & Act
            OcctDiagnostic diagnostic = new OcctDiagnostic(OcctDiagnosticSeverity.Error, "SAM_OCCT_X", "message", 7);

            // Assert
            Assert.Equal(OcctDiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal("SAM_OCCT_X", diagnostic.Code);
            Assert.Equal("message", diagnostic.Message);
            Assert.Equal(7, diagnostic.SourceIndex);
        }

        [Fact]
        public void ToString_WithSourceIndex_FormatsCodeIndexMessage()
        {
            // Arrange
            OcctDiagnostic diagnostic = new OcctDiagnostic(OcctDiagnosticSeverity.Warning, "C1", "msg", 4);

            // Act
            string text = diagnostic.ToString();

            // Assert
            Assert.Equal("C1 [4]: msg", text);
        }

        [Fact]
        public void ToString_WithoutSourceIndex_OmitsIndexBracket()
        {
            // Arrange
            OcctDiagnostic diagnostic = new OcctDiagnostic(OcctDiagnosticSeverity.Info, "C2", "msg");

            // Act
            string text = diagnostic.ToString();

            // Assert
            Assert.Equal("C2: msg", text);
        }
    }
}
