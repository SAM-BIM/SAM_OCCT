// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

namespace SAM.Core.OCCT
{
    public class OcctBuildOptions
    {
        public double Tolerance { get; set; } = global::SAM.Core.Tolerance.Distance;

        public double FuzzyTolerance { get; set; } = global::SAM.Core.Tolerance.MacroDistance;

        public bool RunParallel { get; set; } = true;

        public bool AvoidInternalShapes { get; set; } = true;

        public bool ValidateInput { get; set; } = true;

        /// <summary>
        /// When true, shell operations route through the persistent native
        /// topology handle and attach it to the result, which then owns it and
        /// must be disposed. Default false keeps the legacy decode-and-free
        /// native path.
        /// </summary>
        public bool RetainTopology { get; set; } = false;

        /// <summary>
        /// Tolerance for the native sew-and-heal step (issue #37). Coincident /
        /// near-touching face edges within this distance are joined into shared
        /// topology before the volume is built. A value of zero (the default)
        /// means "use <see cref="Tolerance"/>".
        /// </summary>
        public double SewingTolerance { get; set; } = 0.0;

        /// <summary>
        /// When true, a face soup is first run through the native sew-and-heal
        /// step (issue #37) and only then through BOPAlgo_MakerVolume, so
        /// triangulated / near-touching faces close into a watertight shell that
        /// MakerVolume's fuzzy-tolerance guesswork alone would leave open. Default
        /// false keeps the direct MakerVolume path (which still falls back to one
        /// sew-then-rebuild retry on a hard close failure).
        /// </summary>
        public bool SewBeforeBuild { get; set; } = false;

        /// <summary>
        /// BOP glue mode for the cell-complex build (issue #37 follow-on). When
        /// not <see cref="OcctGlueMode.Off"/> (the default), the builder asks the
        /// boolean kernel to treat coincident faces as shared and skip their
        /// pairwise intersection - a throughput win on cell complexes with many
        /// coincident shared walls. Because glue corrupts merely-near-coincident
        /// faces, it is applied ONLY when a watertightness pre-check reports the
        /// input as clean; otherwise the build silently degrades to the glue-off
        /// path with a diagnostic. Also degrades when the native library predates
        /// the glue ABI (v3).
        /// </summary>
        public OcctGlueMode GlueMode { get; set; } = OcctGlueMode.Off;

        public OcctBuildOptions()
        {
        }

        public OcctBuildOptions(OcctBuildOptions occtBuildOptions)
        {
            if (occtBuildOptions == null)
            {
                return;
            }

            Tolerance = occtBuildOptions.Tolerance;
            FuzzyTolerance = occtBuildOptions.FuzzyTolerance;
            RunParallel = occtBuildOptions.RunParallel;
            AvoidInternalShapes = occtBuildOptions.AvoidInternalShapes;
            ValidateInput = occtBuildOptions.ValidateInput;
            RetainTopology = occtBuildOptions.RetainTopology;
            SewingTolerance = occtBuildOptions.SewingTolerance;
            SewBeforeBuild = occtBuildOptions.SewBeforeBuild;
            GlueMode = occtBuildOptions.GlueMode;
        }

        /// <summary>
        /// The effective sew-and-heal tolerance: <see cref="SewingTolerance"/>
        /// when set to a positive value, otherwise <see cref="Tolerance"/>.
        /// </summary>
        public double EffectiveSewingTolerance
        {
            get { return SewingTolerance > 0 ? SewingTolerance : Tolerance; }
        }
    }
}
