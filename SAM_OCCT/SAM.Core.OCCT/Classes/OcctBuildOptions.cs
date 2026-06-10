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
        }
    }
}
