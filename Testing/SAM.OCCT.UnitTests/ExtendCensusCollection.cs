// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using Xunit;

namespace SAM.OCCT.UnitTests
{
    /// <summary>
    /// Test classes that run <c>Panel3DSnapSolver.Extend</c> while others assert the static extend census
    /// (<c>SnappedPanel.PlaneOpsExtendCount</c> and the other counters reset by <c>ResetExtendCensus</c>): they must not
    /// run in parallel, or one class's extends are counted in another's census.
    /// </summary>
    [CollectionDefinition(Name)]
    public class ExtendCensusCollection
    {
        public const string Name = "Extend census";
    }
}
