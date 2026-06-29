cmake_minimum_required(VERSION 3.20)
project(AchoraoNativeEngine LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(AchoraoEngine SHARED
    src/AchoraoEngine.cpp
    src/AchoraoEngine.h
    src/Agent.cpp
    src/Agent.h
    src/Logger.cpp
    src/Logger.h
    src/PrivilegeManager.cpp
    src/PrivilegeManager.h
    src/StorageModule.cpp
    src/StorageModule.h
    src/EraseEngine.cpp
    src/EraseEngine.h
    src/MemoryModule.cpp
    src/MemoryModule.h
    src/GpuModule.cpp
    src/GpuModule.h
    src/DiagnosticEngine.cpp
    src/DiagnosticEngine.h
    src/DiskInfo.cpp
    src/DiskInfo.h
    src/Models.h
)

target_include_directories(AchoraoEngine PRIVATE
    third_party/json/single_include
)

target_link_libraries(AchoraoEngine PRIVATE
    wbemuuid
    winhttp
    bcrypt
)

target_compile_definitions(AchoraoEngine PRIVATE
    AchoraoEngine_EXPORTS
    _HAS_STD_BYTE=0
)

# Static link MSVC runtime for zero-dependency DLL
if(MSVC)
    set_target_properties(AchoraoEngine PROPERTIES MSVC_RUNTIME_LIBRARY "MultiThreaded")
endif()

set_target_properties(AchoraoEngine PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_SOURCE_DIR}/publish"
    LIBRARY_OUTPUT_DIRECTORY "${CMAKE_SOURCE_DIR}/publish"
    ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_SOURCE_DIR}/publish"
)
