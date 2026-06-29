#pragma once
#include "Models.h"
#include <string>

class DiagnosticEngine {
public:
    static SmartData ReadSmart(int deviceNumber);
    static VerifyResult VerifySanitization(int deviceNumber,
                                           const std::string& serial,
                                           int sampleBlocks = 64);
};
