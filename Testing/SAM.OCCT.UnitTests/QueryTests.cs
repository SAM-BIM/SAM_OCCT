// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Xunit;

namespace SAM.OCCT.UnitTests
{
    public class QueryTests
    {
        [Fact]
        public void NativeLibraryName_WhenCalled_ReturnsSamOcctNative()
        {
            // Arrange & Act
            string name = global::SAM.Core.OCCT.Query.NativeLibraryName();

            // Assert
            Assert.Equal("SAM.Occt.Native", name);
        }
    }
}
