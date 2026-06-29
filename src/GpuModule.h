#pragma once
#include "Models.h"
#include <vector>

class GpuModule {
public:
    static std::vector<GpuInfo> DetectAll();
};
