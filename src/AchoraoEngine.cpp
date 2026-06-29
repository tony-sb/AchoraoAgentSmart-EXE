#include "AchoraoEngine.h"
#include "Agent.h"
#include "StorageModule.h"
#include "EraseEngine.h"
#include "MemoryModule.h"
#include "GpuModule.h"
#include "DiagnosticEngine.h"
#include "PrivilegeManager.h"
#include "Logger.h"

#include <nlohmann/json.hpp>
#include <string>
#include <cstring>
#include <mutex>

using json = nlohmann::json;

namespace {

std::mutex g_mutex;
bool g_initialized = false;

char* AllocCString(const std::string& s) {
    auto* buf = static_cast<char*>(std::malloc(s.size() + 1));
    if (buf) {
        std::memcpy(buf, s.c_str(), s.size() + 1);
    }
    return buf;
}

} // anonymous namespace

bool Engine_Initialize(const char* logPath) {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (g_initialized) return true;

    Logger::Instance().Open(logPath ? logPath : "achorao_engine.log");
    Logger::Instance().Info("Engine", "Achorao Native Engine v1.0.0 initialized");
    g_initialized = true;
    return true;
}

void Engine_Shutdown() {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (g_initialized) {
        Logger::Instance().Info("Engine", "Shutting down");
        Logger::Instance().Close();
        g_initialized = false;
    }
}

const char* Engine_EnumerateStorage() {
    try {
        json arr = json::array();
        for (int i = 0; i < 16; ++i) {
            auto info = StorageModule::Detect(i);
            if ((!info.model.empty() && info.serialNumber != "N/A") ||
                info.capacityBytes > 0) {
                arr.push_back(json(info));
            }
        }
        return AllocCString(arr.dump());
    } catch (const std::exception& e) {
        return AllocCString(json{{"error", e.what()}}.dump());
    }
}

const char* Engine_ClassifyDevice(int deviceNumber) {
    try {
        auto info = StorageModule::Detect(deviceNumber);
        auto method = StorageModule::Classify(info);
        json j = {
            {"deviceNumber", deviceNumber},
            {"mediaType", info.mediaType},
            {"busInterface", info.busInterface},
            {"eraseMethod", EraseMethodToString(method)},
            {"model", info.model},
            {"serialNumber", info.serialNumber}
        };
        return AllocCString(j.dump());
    } catch (const std::exception& e) {
        return AllocCString(json{{"error", e.what()}}.dump());
    }
}

const char* Engine_ReadSmart(int deviceNumber) {
    try {
        auto data = DiagnosticEngine::ReadSmart(deviceNumber);
        return AllocCString(json(data).dump());
    } catch (const std::exception& e) {
        return AllocCString(json{{"error", e.what()}}.dump());
    }
}

const char* Engine_DetectMemory() {
    try {
        auto modules = MemoryModule::DetectAll();
        json arr = json::array();
        for (const auto& m : modules) {
            arr.push_back(json(m));
        }
        return AllocCString(arr.dump());
    } catch (const std::exception& e) {
        return AllocCString(json{{"error", e.what()}}.dump());
    }
}

const char* Engine_DetectGPU() {
    try {
        auto gpus = GpuModule::DetectAll();
        json arr = json::array();
        for (const auto& g : gpus) {
            arr.push_back(json(g));
        }
        return AllocCString(arr.dump());
    } catch (const std::exception& e) {
        return AllocCString(json{{"error", e.what()}}.dump());
    }
}

const char* Engine_ExecuteSanitize(int deviceNumber) {
    try {
        if (!PrivilegeManager::IsElevated()) {
            return AllocCString(json{
                {"success", false},
                {"error", "Process not running as Administrator"}
            }.dump());
        }
        PrivilegeManager::AssertSafeTarget(deviceNumber);
        EraseEngine engine(deviceNumber);
        auto result = engine.Run();
        return AllocCString(json(result).dump());
    } catch (const std::exception& e) {
        return AllocCString(json{
            {"success", false},
            {"error", e.what()}
        }.dump());
    }
}

const char* Engine_VerifySanitization(int deviceNumber) {
    try {
        auto result = DiagnosticEngine::VerifySanitization(deviceNumber, "", 64);
        return AllocCString(json(result).dump());
    } catch (const std::exception& e) {
        return AllocCString(json{
            {"passed", false},
            {"details", e.what()}
        }.dump());
    }
}

const char* Engine_BuildEvidence(int deviceNumber,
                                  const char* technicianId,
                                  const char* workstation) {
    try {
        SanitizationReport report;
        report.deviceInfo = StorageModule::Detect(deviceNumber);
        report.memoryModules = MemoryModule::DetectAll();
        report.gpuAdapters = GpuModule::DetectAll();
        report.preSanitizeSmart = DiagnosticEngine::ReadSmart(deviceNumber);
        report.postSanitizeSmart = DiagnosticEngine::ReadSmart(deviceNumber);
        report.technicianId = technicianId ? technicianId : "CPP-ENGINE";
        report.workstation = workstation ? workstation : "UNKNOWN";
        report.generatedAt = Logger::Instance().Timestamp();
        report.agentVersion = "1.0.0";

        Agent agent(report.agentVersion);
        auto payload = agent.BuildPayload(report);
        return AllocCString(payload.dump(2));
    } catch (const std::exception& e) {
        return AllocCString(json{{"error", e.what()}}.dump());
    }
}

void Engine_FreeString(const char* str) {
    if (str) {
        std::free(const_cast<char*>(str));
    }
}

bool Engine_IsSystemDisk(int deviceNumber) {
    return PrivilegeManager::IsSystemDisk(deviceNumber);
}
