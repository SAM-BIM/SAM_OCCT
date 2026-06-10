// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020-2026 Michal Dengusiak & Jakub Ziolkowski and contributors

// Runtime probe for the delay-loaded Data Exchange toolkits (issue #20).
// Kept in its own translation unit so windows.h never meets the OCCT headers.

#include "OcctNativeCore.h"

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#endif

#include <string>

namespace sam_occt
{
    bool data_exchange_runtime_available(const char* dll_name)
    {
#ifdef _WIN32
        if (dll_name == nullptr || dll_name[0] == '\0')
        {
            return false;
        }

        // Already in the process (a previous probe pinned it, or the host
        // loaded it) - the delay-load helper resolves by module name, so this
        // is sufficient.
        if (GetModuleHandleA(dll_name) != nullptr)
        {
            return true;
        }

        // Look next to SAM.Occt.Native.dll first. A bare LoadLibrary searches
        // the application directory and PATH - which in a host like Rhino do
        // NOT include the deployment folder (%APPDATA%\SAM or build/) where
        // the OCCT runtime sits beside this module. Static imports (the core
        // TK* toolkits) resolve relative to this module automatically; the
        // delay-loaded toolkits need the same treatment by hand.
        HMODULE self = nullptr;
        if (GetModuleHandleExA(
                GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                reinterpret_cast<LPCSTR>(&data_exchange_runtime_available),
                &self) != 0)
        {
            char buffer[1024];
            const DWORD length = GetModuleFileNameA(self, buffer, static_cast<DWORD>(sizeof(buffer)));
            if (length > 0 && length < sizeof(buffer))
            {
                std::string full_path(buffer, length);
                const std::size_t separator = full_path.find_last_of("\\/");
                if (separator != std::string::npos)
                {
                    full_path.erase(separator + 1);
                    full_path += dll_name;

                    // LOAD_WITH_ALTERED_SEARCH_PATH so the toolkit's own static
                    // imports (TKXSBase, the XCAF/CAF stack) also resolve from
                    // the same directory. Deliberately left loaded: the
                    // delay-load helper then finds the module by name instead
                    // of repeating the (failing) default search.
                    if (LoadLibraryExA(full_path.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH) != nullptr)
                    {
                        return true;
                    }
                }
            }
        }

        // Fall back to the regular search order (application directory, PATH)
        // for deployments that place the OCCT runtime there instead.
        return LoadLibraryA(dll_name) != nullptr;
#else
        (void)dll_name;
        return true;
#endif
    }
}
