#include "Agent.h"
#include "Logger.h"

#include <fstream>
#include <utility>

Agent::Agent(std::string version)
    : version_(std::move(version)) {}

const std::string& Agent::Version() const noexcept {
    return version_;
}

nlohmann::json Agent::BuildPayload(const SanitizationReport& report) const {
    nlohmann::json payload = report;
    payload["agent"]["version"] = version_;
    return payload;
}

bool Agent::SaveEvidence(const SanitizationReport& report,
                         const std::string& filePath) const {
    const auto payload = BuildPayload(report).dump(2);
    std::ofstream output(filePath, std::ios::trunc);
    if (!output.is_open()) {
        Logger::Instance().Error("Agent", "Failed to create evidence payload: " + filePath);
        return false;
    }

    output << payload;
    if (!output.good()) {
        Logger::Instance().Error("Agent", "Failed to write evidence payload: " + filePath);
        return false;
    }

    Logger::Instance().Info("Agent", "Evidence payload saved to " + filePath);
    return true;
}
