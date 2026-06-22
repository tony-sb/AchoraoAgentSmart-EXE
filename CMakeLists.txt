cmake_minimum_required(VERSION 3.20)
project(AchoraoAgent LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_executable(AchoraoAgent 
    src/main.cpp
    src/DiskInfo.cpp
    src/DiskInfo.h
    src/HttpClient.cpp
    src/HttpClient.h
)

# Enlazar las APIs nativas de Windows necesarias
target_link_libraries(AchoraoAgent PRIVATE 
    wbemuuid 
    winhttp 
    bcrypt
)