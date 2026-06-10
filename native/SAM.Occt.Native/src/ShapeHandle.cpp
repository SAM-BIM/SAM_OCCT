// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

// Persistent sam_occt_shape handle family (issue #14). The opaque Shape struct
// owns a live TopoDS_Shape so operations can chain natively without managed
// round-trips. Issue #20 (STEP/IGES via XCAF) can extend the struct with a
// document member without breaking the ABI - only void* crosses the boundary.

#include "sam_occt.h"

extern "C" {

int sam_occt_abi_version(void)
{
    return 2;
}

}
