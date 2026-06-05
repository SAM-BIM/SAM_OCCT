cmake_minimum_required(VERSION 3.20)

project(SAM.Occt.Native LANGUAGES CXX)

find_package(OpenCASCADE REQUIRED)

add_library(SAM.Occt.Native SHARED
    src/CellComplexBuilder.cpp
)

target_compile_definitions(SAM.Occt.Native PRIVATE SAM_OCCT_NATIVE_EXPORTS)
target_include_directories(SAM.Occt.Native PUBLIC include)
target_compile_features(SAM.Occt.Native PRIVATE cxx_std_17)

target_include_directories(SAM.Occt.Native PRIVATE ${OpenCASCADE_INCLUDE_DIR})
target_link_libraries(SAM.Occt.Native PRIVATE ${OpenCASCADE_LIBRARIES})
