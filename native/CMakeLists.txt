cmake_minimum_required(VERSION 3.20)
project(NebulaNative LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")
foreach(configuration DEBUG RELEASE RELWITHDEBINFO MINSIZEREL)
  set(CMAKE_RUNTIME_OUTPUT_DIRECTORY_${configuration} "${CMAKE_BINARY_DIR}/bin")
  set(CMAKE_LIBRARY_OUTPUT_DIRECTORY_${configuration} "${CMAKE_BINARY_DIR}/bin")
  set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY_${configuration} "${CMAKE_BINARY_DIR}/lib")
endforeach()

function(nebula_strict_warnings target)
  if(MSVC)
    target_compile_options(
      ${target}
      PRIVATE /W4 /WX /permissive- /EHsc /Zc:__cplusplus)
  else()
    target_compile_options(
      ${target}
      PRIVATE -Wall -Wextra -Wpedantic -Werror)
  endif()
endfunction()

add_library(NebulaNative SHARED src/nebula_native.cpp)
target_include_directories(NebulaNative PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/include")
target_compile_definitions(NebulaNative PRIVATE NEBULA_NATIVE_EXPORTS)
set_target_properties(
  NebulaNative
  PROPERTIES CXX_VISIBILITY_PRESET hidden VISIBILITY_INLINES_HIDDEN YES)
nebula_strict_warnings(NebulaNative)

add_executable(nebula_native_tests tests/native_tests.cpp)
target_link_libraries(nebula_native_tests PRIVATE NebulaNative)
nebula_strict_warnings(nebula_native_tests)

add_executable(nebula_native_benchmark benchmarks/native_benchmark.cpp)
target_link_libraries(nebula_native_benchmark PRIVATE NebulaNative)
nebula_strict_warnings(nebula_native_benchmark)

enable_testing()
add_test(NAME nebula_native_tests COMMAND nebula_native_tests)
