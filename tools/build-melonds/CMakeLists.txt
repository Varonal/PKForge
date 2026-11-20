cmake_minimum_required(VERSION 3.22.1)
project(pkforge_melonds LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

get_filename_component(MELONDS_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../external/melonDS" ABSOLUTE)
if(NOT EXISTS "${MELONDS_DIR}/src/NDS.h")
    message(FATAL_ERROR "melonDS submodule is missing. Run: git submodule update --init external/melonDS")
endif()

set(BUILD_QT_SDL OFF CACHE BOOL "" FORCE)
set(ENABLE_OGLRENDERER OFF CACHE BOOL "" FORCE)
set(ENABLE_GDBSTUB OFF CACHE BOOL "" FORCE)
set(ENABLE_LTO_RELEASE OFF CACHE BOOL "" FORCE)
set(ENABLE_LTO OFF CACHE BOOL "" FORCE)
set(ENABLE_JIT ON CACHE BOOL "" FORCE)

add_subdirectory("${MELONDS_DIR}" "${CMAKE_CURRENT_BINARY_DIR}/melonds" EXCLUDE_FROM_ALL)

add_library(pkforge_melonds SHARED
    melonds_platform.cpp
    pkforge_melonds.cpp)
target_include_directories(pkforge_melonds PRIVATE "${MELONDS_DIR}/src")
target_link_libraries(pkforge_melonds PRIVATE core android log)
target_link_options(pkforge_melonds PRIVATE
    "-Wl,--no-undefined"
    "-Wl,-z,max-page-size=16384"
    "-Wl,-z,common-page-size=16384")
set_target_properties(pkforge_melonds PROPERTIES OUTPUT_NAME "pkforge_melonds")
