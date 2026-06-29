#pragma once
#include "Models.h"

class StorageModule {
public:
    static StorageInfo Detect(int deviceNumber);
    static EraseMethod Classify(const StorageInfo& info);
};
