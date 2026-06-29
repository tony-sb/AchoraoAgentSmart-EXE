#pragma once
#include "Models.h"
#include <vector>

class MemoryModule {
public:
    static std::vector<MemoryModuleInfo> DetectAll();
};
