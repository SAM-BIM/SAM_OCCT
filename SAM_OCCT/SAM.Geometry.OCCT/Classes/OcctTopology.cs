// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Runtime.InteropServices;

namespace SAM.Geometry.OCCT
{
    /// <summary>
    /// Owns a native OCCT topology handle (sam_occt_shape) wrapping a live
    /// TopoDS_Shape, so chained operations stay native instead of round-tripping
    /// through decoded point arrays.
    /// <para>
    /// Lifetime: every API returning an <see cref="OcctTopology"/> transfers
    /// ownership to the caller, who should Dispose it deterministically (the
    /// finalizer is only a backstop). Dispose is idempotent. Operations never
    /// consume their inputs - combining two topologies leaves both valid.
    /// </para>
    /// <para>
    /// Threading: create, operate and decode on a single thread. Only Dispose
    /// or finalization may safely occur on another thread. No managed copy of
    /// the geometry is kept; decode via Query.CellComplexResult when SAM
    /// geometry is needed. Topology keys are only comparable within one decoded
    /// result.
    /// </para>
    /// </summary>
    public sealed class OcctTopology : SafeHandle
    {
        // Used by the P/Invoke marshaler when materialising out parameters; the
        // runtime AddRef/Releases the handle around every native call, so a
        // Dispose racing an in-flight operation cannot free the native shape.
        private OcctTopology()
            : base(IntPtr.Zero, true)
        {
        }

        public override bool IsInvalid
        {
            get { return handle == IntPtr.Zero; }
        }

        /// <summary>
        /// Number of solids in the native shape; -1 when the handle is invalid,
        /// disposed or the native library is unavailable.
        /// </summary>
        public int SolidCount
        {
            get
            {
                if (IsInvalid || IsClosed)
                {
                    return -1;
                }

                try
                {
                    return Native.OcctNativeMethods.sam_occt_shape_solid_count(this);
                }
                catch
                {
                    return -1;
                }
            }
        }

        protected override bool ReleaseHandle()
        {
            try
            {
                Native.OcctNativeMethods.sam_occt_shape_release(handle);
            }
            catch
            {
                // Releasing on a process without the native library (or during
                // teardown) must never throw from the finalizer thread.
            }

            return true;
        }
    }
}
