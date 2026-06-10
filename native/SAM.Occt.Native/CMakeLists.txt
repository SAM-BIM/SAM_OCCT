cmake_minimum_required(VERSION 3.20)

project(SAM.Occt.Native LANGUAGES CXX)

set(SAM_OCCT_OUTPUT_DIRECTORY "${CMAKE_CURRENT_LIST_DIR}/../../build" CACHE PATH "Directory for SAM OCCT native build output.")
set(SAM_OCCT_OCCT_INCLUDE_DIR "" CACHE PATH "Manual OCCT include directory, for SDKs without OpenCASCADEConfig.cmake.")
set(SAM_OCCT_OCCT_LIBRARY_DIR "" CACHE PATH "Manual OCCT library directory, for SDKs without OpenCASCADEConfig.cmake.")

find_package(OpenCASCADE QUIET)

set(SAM_OCCT_OCCT_LIBRARIES "")
if(OpenCASCADE_FOUND)
    set(SAM_OCCT_OCCT_INCLUDE_DIRS ${OpenCASCADE_INCLUDE_DIR})
    set(SAM_OCCT_OCCT_LIBRARIES ${OpenCASCADE_LIBRARIES})
else()
    if(NOT SAM_OCCT_OCCT_INCLUDE_DIR OR NOT SAM_OCCT_OCCT_LIBRARY_DIR)
        message(FATAL_ERROR "OpenCASCADE was not found. Set OpenCASCADE_DIR or set SAM_OCCT_OCCT_INCLUDE_DIR and SAM_OCCT_OCCT_LIBRARY_DIR.")
    endif()

    set(SAM_OCCT_OCCT_INCLUDE_DIRS ${SAM_OCCT_OCCT_INCLUDE_DIR})
    set(SAM_OCCT_OCCT_MODULES
        TKOffset
        TKPrim
        TKMesh
        TKBool
        TKBO
        TKShHealing
        TKG3d
        TKG2d
        TKGeomBase
        TKGeomAlgo
        TKTopAlgo
        TKBRep
        TKMath
        TKernel
    )

    foreach(SAM_OCCT_OCCT_MODULE IN LISTS SAM_OCCT_OCCT_MODULES)
        find_library(${SAM_OCCT_OCCT_MODULE}_LIBRARY
            NAMES ${SAM_OCCT_OCCT_MODULE}
            PATHS ${SAM_OCCT_OCCT_LIBRARY_DIR}
            NO_DEFAULT_PATH)

        if(NOT ${SAM_OCCT_OCCT_MODULE}_LIBRARY)
            message(FATAL_ERROR "Could not find ${SAM_OCCT_OCCT_MODULE}.lib in ${SAM_OCCT_OCCT_LIBRARY_DIR}.")
        endif()

        list(APPEND SAM_OCCT_OCCT_LIBRARIES ${${SAM_OCCT_OCCT_MODULE}_LIBRARY})
    endforeach()
endif()

add_library(SAM.Occt.Native SHARED
    src/CellComplexBuilder.cpp
    src/ShapeHandle.cpp
)

target_compile_definitions(SAM.Occt.Native PRIVATE SAM_OCCT_NATIVE_EXPORTS)
target_include_directories(SAM.Occt.Native PUBLIC include)
target_compile_features(SAM.Occt.Native PRIVATE cxx_std_17)

target_include_directories(SAM.Occt.Native PRIVATE ${SAM_OCCT_OCCT_INCLUDE_DIRS})
target_link_libraries(SAM.Occt.Native PRIVATE ${SAM_OCCT_OCCT_LIBRARIES})

set_target_properties(SAM.Occt.Native PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${SAM_OCCT_OUTPUT_DIRECTORY}"
    LIBRARY_OUTPUT_DIRECTORY "${SAM_OCCT_OUTPUT_DIRECTORY}"
    ARCHIVE_OUTPUT_DIRECTORY "${SAM_OCCT_OUTPUT_DIRECTORY}"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${SAM_OCCT_OUTPUT_DIRECTORY}"
    LIBRARY_OUTPUT_DIRECTORY_DEBUG "${SAM_OCCT_OUTPUT_DIRECTORY}"
    ARCHIVE_OUTPUT_DIRECTORY_DEBUG "${SAM_OCCT_OUTPUT_DIRECTORY}"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${SAM_OCCT_OUTPUT_DIRECTORY}"
    LIBRARY_OUTPUT_DIRECTORY_RELEASE "${SAM_OCCT_OUTPUT_DIRECTORY}"
    ARCHIVE_OUTPUT_DIRECTORY_RELEASE "${SAM_OCCT_OUTPUT_DIRECTORY}"
)
