cmake_minimum_required(VERSION 3.24)
project(WatermarkPystringStatic LANGUAGES CXX)

if(DEFINED PYSTRING_SOURCE_DIR)
  # CMake writes install(FILES ...) paths into cmake_install.cmake. Normalize
  # Windows drive paths first so a path such as C:\\Mac\\Home is emitted as
  # C:/Mac/Home instead of being parsed as the invalid escape sequence \M.
  file(TO_CMAKE_PATH "${PYSTRING_SOURCE_DIR}" _PYSTRING_SOURCE_DIR)
endif()

if(NOT DEFINED _PYSTRING_SOURCE_DIR OR NOT EXISTS "${_PYSTRING_SOURCE_DIR}/pystring.cpp")
  message(FATAL_ERROR "PYSTRING_SOURCE_DIR must point to the locked pystring 1.1.4 source tree.")
endif()

include(GNUInstallDirs)
add_library(pystring STATIC "${_PYSTRING_SOURCE_DIR}/pystring.cpp")
target_include_directories(pystring PUBLIC
  $<BUILD_INTERFACE:${_PYSTRING_SOURCE_DIR}>
  $<INSTALL_INTERFACE:${CMAKE_INSTALL_INCLUDEDIR}>)
set_target_properties(pystring PROPERTIES POSITION_INDEPENDENT_CODE ON)
install(TARGETS pystring ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR})
install(FILES "${_PYSTRING_SOURCE_DIR}/pystring.h" DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
