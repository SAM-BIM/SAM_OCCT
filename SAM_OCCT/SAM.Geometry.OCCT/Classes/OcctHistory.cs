// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.OCCT.Native;
using System;
using System.Collections.Generic;

namespace SAM.Geometry.OCCT
{
    /// <summary>
    /// A pure managed snapshot of the native <c>BRepTools_History</c> a history-capturing op
    /// (<c>sam_occt_build_cell_complex</c>, <c>sam_occt_merge_coplanar</c>) recorded for its
    /// result, copied eagerly while the native result handle is alive
    /// (docs/P3_ABI_V4_NATIVE_HISTORY_DESIGN_REVIEW.md §G). Deliberately NOT a native handle
    /// type: there is no finalizer, nothing to dispose, and nothing to soak-leak.
    /// </summary>
    /// <remarks>
    /// Input faces are addressed by the caller's flattened-array face order (0-based). Output
    /// faces are addressed by FLAT ORDINAL - the cell-major, face-minor enumeration of the
    /// result, identical to walking the managed decode (a face shared by two cells has two
    /// ordinals and is listed under both). The op is strictly observational: capturing this
    /// history does not change the geometry the op produces.
    /// </remarks>
    public sealed class OcctHistory
    {
        private readonly bool[] deleted;
        private readonly int[][] modifiedOrdinals;
        private readonly int[][] generatedOrdinals;

        private OcctHistory(
            int inputCount,
            bool[] deleted,
            int[][] modifiedOrdinals,
            int[][] generatedOrdinals,
            double maxTolerance,
            double averageTolerance)
        {
            InputCount = inputCount;
            this.deleted = deleted;
            this.modifiedOrdinals = modifiedOrdinals;
            this.generatedOrdinals = generatedOrdinals;
            MaxTolerance = maxTolerance;
            AverageTolerance = averageTolerance;
        }

        /// <summary>
        /// Builds a snapshot from already-materialised per-input records - the direct constructor for
        /// tests and any caller that computes history another way. <paramref name="deletedInputs"/> lists
        /// the deleted input indices; <paramref name="modifiedOrdinals"/> / <paramref name="generatedOrdinals"/>
        /// give the output flat ordinals per input (index-aligned to [0, <paramref name="inputCount"/>),
        /// short lists padded with empties).
        /// </summary>
        public OcctHistory(
            int inputCount,
            IEnumerable<int> deletedInputs,
            IReadOnlyList<IReadOnlyList<int>> modifiedOrdinals,
            IReadOnlyList<IReadOnlyList<int>> generatedOrdinals = null,
            double maxTolerance = 0,
            double averageTolerance = 0)
        {
            InputCount = inputCount < 0 ? 0 : inputCount;

            deleted = new bool[InputCount];
            foreach (int index in deletedInputs ?? Array.Empty<int>())
            {
                if (index >= 0 && index < InputCount)
                {
                    deleted[index] = true;
                }
            }

            this.modifiedOrdinals = Materialise(modifiedOrdinals, InputCount);
            this.generatedOrdinals = Materialise(generatedOrdinals, InputCount);
            MaxTolerance = maxTolerance;
            AverageTolerance = averageTolerance;
        }

        private static int[][] Materialise(IReadOnlyList<IReadOnlyList<int>> source, int inputCount)
        {
            int[][] result = new int[inputCount][];
            for (int i = 0; i < inputCount; i++)
            {
                IReadOnlyList<int> row = source != null && i < source.Count ? source[i] : null;
                result[i] = row != null ? System.Linq.Enumerable.ToArray(row) : Array.Empty<int>();
            }

            return result;
        }

        /// <summary>Number of input faces the producing op saw (the caller's flattened face order).</summary>
        public int InputCount { get; }

        /// <summary>Max sub-shape tolerance the op left on its result; 0 when not captured.</summary>
        public double MaxTolerance { get; }

        /// <summary>Average sub-shape tolerance the op left on its result; 0 when not captured.</summary>
        public double AverageTolerance { get; }

        /// <summary>True for an input face the op deleted (no surviving output).</summary>
        public bool IsDeleted(int inputIndex)
        {
            return inputIndex >= 0 && inputIndex < deleted.Length && deleted[inputIndex];
        }

        /// <summary>The output flat ordinals <paramref name="inputIndex"/> was modified into (splits/merges).</summary>
        public IReadOnlyList<int> ModifiedOrdinals(int inputIndex)
        {
            return inputIndex >= 0 && inputIndex < modifiedOrdinals.Length
                ? modifiedOrdinals[inputIndex]
                : Array.Empty<int>();
        }

        /// <summary>The output flat ordinals <paramref name="inputIndex"/> generated (typically empty for BOP faces).</summary>
        public IReadOnlyList<int> GeneratedOrdinals(int inputIndex)
        {
            return inputIndex >= 0 && inputIndex < generatedOrdinals.Length
                ? generatedOrdinals[inputIndex]
                : Array.Empty<int>();
        }

        /// <summary>The input face indices the op deleted (excluded from any output mapping).</summary>
        public IReadOnlyList<int> DeletedInputs
        {
            get
            {
                List<int> result = new List<int>();
                for (int i = 0; i < deleted.Length; i++)
                {
                    if (deleted[i])
                    {
                        result.Add(i);
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// Copies the native history off <paramref name="resultHandle"/> into a managed snapshot,
        /// or returns null when the native build predates ABI v4, the op captured no history for
        /// this result, or the handle is invalid. Must be called while the handle is still alive
        /// (before <c>sam_occt_free_result</c>). Never throws: any native fault degrades to null so
        /// callers fall back to the geometric heuristic.
        /// </summary>
        internal static OcctHistory Capture(IntPtr resultHandle)
        {
            if (resultHandle == IntPtr.Zero || !OcctNativeMethods.SupportsHistory)
            {
                return null;
            }

            try
            {
                if (OcctNativeMethods.sam_occt_result_history_available(resultHandle) != 1)
                {
                    return null;
                }

                int inputCount = OcctNativeMethods.sam_occt_result_history_input_count(resultHandle);
                if (inputCount <= 0)
                {
                    return null;
                }

                bool[] deleted = new bool[inputCount];
                int[][] modified = new int[inputCount][];
                int[][] generated = new int[inputCount][];

                for (int i = 0; i < inputCount; i++)
                {
                    if (OcctNativeMethods.sam_occt_result_history_face(resultHandle, i, out int deletedFlag, out int modifiedCount, out int generatedCount) != 0)
                    {
                        return null;
                    }

                    deleted[i] = deletedFlag != 0;

                    int[] modifiedBuffer = modifiedCount > 0 ? new int[modifiedCount] : Array.Empty<int>();
                    int[] generatedBuffer = generatedCount > 0 ? new int[generatedCount] : Array.Empty<int>();

                    if (modifiedCount > 0 || generatedCount > 0)
                    {
                        if (OcctNativeMethods.sam_occt_result_history_entries(resultHandle, i, modifiedBuffer, modifiedBuffer.Length, generatedBuffer, generatedBuffer.Length) != 0)
                        {
                            return null;
                        }
                    }

                    modified[i] = modifiedBuffer;
                    generated[i] = generatedBuffer;
                }

                double maxTolerance = 0;
                double averageTolerance = 0;
                OcctNativeMethods.sam_occt_result_max_tolerance(resultHandle, out maxTolerance, out averageTolerance);

                return new OcctHistory(inputCount, deleted, modified, generated, maxTolerance, averageTolerance);
            }
            catch (EntryPointNotFoundException)
            {
                // Stale native library that reports ABI >= 4 for the base symbols but
                // lacks a v4 accessor: degrade to the geometric heuristic.
                return null;
            }
            catch (DllNotFoundException)
            {
                return null;
            }
        }
    }
}
