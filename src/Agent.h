#pragma once

#include "Models.h"
#include <nlohmann/json.hpp>
#include <string>

class Agent {
public:
    explicit Agent(std::string version = "1.0.0");

    const std::string& Version() const noexcept;
    nlohmann::json BuildPayload(const SanitizationReport& report) const;
    bool SaveEvidence(const SanitizationReport& report,
                      const std::string& filePath = "payload_evidence.json") const;

private:
    std::string version_;
};
