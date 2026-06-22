#pragma once
#include <string>
#include <cstdint>

struct DiskMetrics {
    std::string serialNumber;
    std::string diskName;
    std::string type;         // SSD / HDD
    std::string capacity;     // e.g., "512 GB"
    std::string busInterface; // NVMe / SATA / USB
    int healthScore;
    std::string grade;        // A, B, C, D
    int hours;
    int wear;
    int temp;
    int sectors;
    double writtenTB;
    std::string generatedAt;
    std::string hash;
};

// Ejecuta las consultas WMI reales y calcula las métricas de salud
DiskMetrics FetchHardwareTelemetry();