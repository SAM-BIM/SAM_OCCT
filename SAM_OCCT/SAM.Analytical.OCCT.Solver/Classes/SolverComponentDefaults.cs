// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Analytical.OCCT.Solver
{
    /// <summary>Grasshopper defaults for solver controls that intentionally differ from the core API defaults.</summary>
    internal static class SolverComponentDefaults
    {
        public const double BucketBetweenLevels = 0.21;

        /// <summary>
        /// Version-gated fallback for an ABSENT voluntary <c>bucketBetweenLevels_</c> input. A voluntary GH
        /// input exists neither on an old saved component nor on a fresh placement, so this fallback IS the
        /// effective GH default. A component saved before the input existed
        /// (<paramref name="componentVersion"/> &lt; <paramref name="introducedInVersion"/>) keeps the core
        /// default 0 - its documents' solved geometry is frozen; a component placed at or after the
        /// introducing version uses <see cref="BucketBetweenLevels"/>. Unparseable/unknown versions fall to 0
        /// (never silently change a saved document).
        /// </summary>
        public static double BucketBetweenLevelsFallback(string componentVersion, string introducedInVersion)
        {
            if (!System.Version.TryParse(componentVersion, out System.Version version) ||
                !System.Version.TryParse(introducedInVersion, out System.Version introduced))
            {
                return 0.0;
            }

            return version >= introduced ? BucketBetweenLevels : 0.0;
        }
    }
}
