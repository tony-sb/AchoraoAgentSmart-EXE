#pragma once
#include <string>
#include <vector>
#include <cstdint>
#include <nlohmann/json.hpp>

using json = nlohmann::json;

// ── Device / Storage ───────────────────────────────────────────────
struct StorageInfo {
    int    deviceNumber{0};
    std::string serialNumber;
    std::string model;
    std::string vendor;
    std::string capacity;         // "512 GB"
    uint64_t capacityBytes{0};
    std::string busInterface;     // "NVMe" | "SATA" | "USB"
    std::string mediaType;        // "HDD" | "SSD"
};

inline void to_json(json& j, const StorageInfo& s) {
    j = json{
        {"deviceNumber",   s.deviceNumber},
        {"serialNumber",   s.serialNumber},
        {"model",          s.model},
        {"vendor",         s.vendor},
        {"capacity",       s.capacity},
        {"capacityBytes",  s.capacityBytes},
        {"busInterface",   s.busInterface},
        {"mediaType",      s.mediaType}
    };
}

// ── SMART Attributes ────────────────────────────────────────────────
struct SmartData {
    int  temperature{0};
    int  powerOnHours{0};
    int  wearLevel{0};
    int  reallocatedSectors{0};
    int  pendingSectors{0};
    int  uncorrectableErrors{0};
    int  readErrorsTotal{0};
    double writtenTeraBytes{0.0};
    int  healthPercent{100};
};

inline void to_json(json& j, const SmartData& s) {
    j = json{
        {"temperature",        s.temperature},
        {"powerOnHours",       s.powerOnHours},
        {"wearLevel",          s.wearLevel},
        {"reallocatedSectors", s.reallocatedSectors},
        {"pendingSectors",     s.pendingSectors},
        {"uncorrectableErrors", s.uncorrectableErrors},
        {"readErrorsTotal",    s.readErrorsTotal},
        {"writtenTeraBytes",   s.writtenTeraBytes},
        {"healthPercent",      s.healthPercent}
    };
}

// ── Memory (RAM) ────────────────────────────────────────────────────
struct MemoryModuleInfo {
    std::string capacity;
    std::string manufacturer;
    std::string partNumber;
    std::string serialNumber;
    std::string speed;           // "3200 MHz"
    std::string formFactor;      // "DIMM" | "SODIMM"
    std::string memoryType;      // "DDR4" | "DDR5"
    int    configuredVoltageMv{0};
    bool   isPresent{false};
};

inline void to_json(json& j, const MemoryModuleInfo& m) {
    j = json{
        {"capacity",          m.capacity},
        {"manufacturer",      m.manufacturer},
        {"partNumber",        m.partNumber},
        {"serialNumber",      m.serialNumber},
        {"speed",             m.speed},
        {"formFactor",        m.formFactor},
        {"memoryType",        m.memoryType},
        {"configuredVoltageMv", m.configuredVoltageMv},
        {"isPresent",         m.isPresent}
    };
}

// ── GPU ─────────────────────────────────────────────────────────────
struct GpuInfo {
    std::string name;
    std::string vendor;
    std::string biosVersion;
    std::string driverVersion;
    int    temperatureCelsius{0};
    int    healthPercent{100};
    uint64_t dedicatedMemoryBytes{0};
    bool   hasError{false};
};

inline void to_json(json& j, const GpuInfo& g) {
    j = json{
        {"name",                g.name},
        {"vendor",              g.vendor},
        {"biosVersion",         g.biosVersion},
        {"driverVersion",       g.driverVersion},
        {"temperatureCelsius",  g.temperatureCelsius},
        {"healthPercent",       g.healthPercent},
        {"dedicatedMemoryBytes", g.dedicatedMemoryBytes},
        {"hasError",            g.hasError}
    };
}

// ── Sanitization Method Enum ────────────────────────────────────────
enum class EraseMethod {
    Overwrite,
    AtaSecureErase,
    NvmeSanitize,
    None
};

inline std::string EraseMethodToString(EraseMethod m) {
    switch (m) {
        case EraseMethod::Overwrite:      return "Overwrite";
        case EraseMethod::AtaSecureErase: return "ATASecurityErase";
        case EraseMethod::NvmeSanitize:   return "NVMeSanitize";
        case EraseMethod::None:           return "None";
    }
    return "Unknown";
}

// ── Sanitization Result ─────────────────────────────────────────────
struct SanitizeResult {
    bool     success{false};
    std::string method;
    std::string deviceSerial;
    std::string startTime;
    std::string endTime;
    int      durationSeconds{0};
    std::string errorMessage;
};

inline void to_json(json& j, const SanitizeResult& r) {
    j = json{
        {"success",        r.success},
        {"method",         r.method},
        {"deviceSerial",   r.deviceSerial},
        {"startTime",      r.startTime},
        {"endTime",        r.endTime},
        {"durationSeconds", r.durationSeconds},
        {"errorMessage",   r.errorMessage}
    };
}

// ── Block Verification Result ───────────────────────────────────────
struct VerifyResult {
    bool   passed{false};
    int    blocksSampled{0};
    int    blocksWithNonZero{0};
    std::string details;
};

inline void to_json(json& j, const VerifyResult& v) {
    j = json{
        {"passed",          v.passed},
        {"blocksSampled",   v.blocksSampled},
        {"blocksNonZero",   v.blocksWithNonZero},
        {"details",         v.details}
    };
}

// ── Complete Sanitization Report ────────────────────────────────────
struct SanitizationReport {
    StorageInfo  deviceInfo;
    std::vector<MemoryModuleInfo> memoryModules;
    std::vector<GpuInfo> gpuAdapters;
    SmartData    preSanitizeSmart;
    SmartData    postSanitizeSmart;
    SanitizeResult sanitizeResult;
    VerifyResult verifyResult;
    std::string  agentVersion{"1.0.0"};
    std::string  technicianId;
    std::string  workstation;
    std::string  generatedAt;
};

inline void to_json(json& j, const SanitizationReport& r) {
    j = json{
        {"agent", {
            {"version",      r.agentVersion},
            {"technicianId", r.technicianId},
            {"workstation",  r.workstation},
            {"generatedAt",  r.generatedAt}
        }},
        {"hardware", {
            {"storage", r.deviceInfo},
            {"memory",  r.memoryModules},
            {"gpu",     r.gpuAdapters}
        }},
        {"sanitization", r.sanitizeResult},
        {"diagnostics", {
            {"smart", {
                {"before", r.preSanitizeSmart},
                {"after",  r.postSanitizeSmart}
            }},
            {"verification", r.verifyResult}
        }}
    };
}
