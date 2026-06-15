// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core.OCCT;
using Xunit;

namespace SAM.OCCT.UnitTests
{
    public class OcctBuildOptionsTests
    {
        [Fact]
        public void Constructor_Default_SetsExpectedDefaults()
        {
            // Arrange & Act
            OcctBuildOptions options = new OcctBuildOptions();

            // Assert
            Assert.Equal(global::SAM.Core.Tolerance.Distance, options.Tolerance);
            Assert.Equal(global::SAM.Core.Tolerance.MacroDistance, options.FuzzyTolerance);
            Assert.True(options.RunParallel);
            Assert.True(options.AvoidInternalShapes);
            Assert.True(options.ValidateInput);

            // issue #37: sew-and-heal defaults are off / fall back to Tolerance.
            Assert.Equal(0.0, options.SewingTolerance);
            Assert.False(options.SewBeforeBuild);
            Assert.Equal(global::SAM.Core.Tolerance.Distance, options.EffectiveSewingTolerance);

            // issue #37 follow-on: BOP glue defaults off.
            Assert.Equal(OcctGlueMode.Off, options.GlueMode);
        }

        [Fact]
        public void CopyConstructor_WithSource_CopiesGlueMode()
        {
            // Arrange
            OcctBuildOptions source = new OcctBuildOptions { GlueMode = OcctGlueMode.Full };

            // Act
            OcctBuildOptions copy = new OcctBuildOptions(source);

            // Assert
            Assert.Equal(OcctGlueMode.Full, copy.GlueMode);
        }

        [Fact]
        public void EffectiveSewingTolerance_PositiveSewingTolerance_OverridesTolerance()
        {
            // Arrange & Act
            OcctBuildOptions options = new OcctBuildOptions { Tolerance = 1e-6, SewingTolerance = 5e-3 };

            // Assert - a positive SewingTolerance wins over Tolerance.
            Assert.Equal(5e-3, options.EffectiveSewingTolerance);
        }

        [Fact]
        public void CopyConstructor_WithSource_CopiesSewOptions()
        {
            // Arrange
            OcctBuildOptions source = new OcctBuildOptions { SewingTolerance = 0.004, SewBeforeBuild = true };

            // Act
            OcctBuildOptions copy = new OcctBuildOptions(source);

            // Assert
            Assert.Equal(source.SewingTolerance, copy.SewingTolerance);
            Assert.True(copy.SewBeforeBuild);
        }

        [Theory]
        [InlineData(true, true, true)]
        [InlineData(false, false, false)]
        [InlineData(true, false, true)]
        public void CopyConstructor_WithSource_CopiesAllFields(bool runParallel, bool avoidInternalShapes, bool validateInput)
        {
            // Arrange
            OcctBuildOptions source = new OcctBuildOptions
            {
                Tolerance = 0.123,
                FuzzyTolerance = 0.456,
                RunParallel = runParallel,
                AvoidInternalShapes = avoidInternalShapes,
                ValidateInput = validateInput
            };

            // Act
            OcctBuildOptions copy = new OcctBuildOptions(source);

            // Assert
            Assert.Equal(source.Tolerance, copy.Tolerance);
            Assert.Equal(source.FuzzyTolerance, copy.FuzzyTolerance);
            Assert.Equal(source.RunParallel, copy.RunParallel);
            Assert.Equal(source.AvoidInternalShapes, copy.AvoidInternalShapes);
            Assert.Equal(source.ValidateInput, copy.ValidateInput);
        }

        [Fact]
        public void CopyConstructor_WithNull_FallsBackToDefaults()
        {
            // Arrange & Act
            OcctBuildOptions copy = new OcctBuildOptions(null);

            // Assert - a null source must not throw and must leave defaults intact.
            Assert.Equal(global::SAM.Core.Tolerance.Distance, copy.Tolerance);
            Assert.True(copy.RunParallel);
            Assert.True(copy.AvoidInternalShapes);
            Assert.True(copy.ValidateInput);
        }
    }
}
