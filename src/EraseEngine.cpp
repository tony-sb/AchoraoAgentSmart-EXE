#include "EraseEngine.h"
#include "StorageModule.h"
#include "Logger.h"
#include "PrivilegeManager.h"
#include <windows.h>
#include <winioctl.h>
#include <ntddscsi.h>
#include <ntddstor.h>
#include <algorithm>
#include <cstring>
#include <vector>
#include <chrono>
#include <sstream>
#include <iomanip>
#include <thread>

#pragma comment(lib, "advapi32.lib")

// ── EraseEngine ──────────────────────────────────────────────────────

EraseEngine::EraseEngine(int deviceNumber)
    : deviceNumber_(deviceNumber) {
    deviceInfo_ = StorageModule::Detect(deviceNumber);
    selectedMethod_ = StorageModule::Classify(deviceInfo_);
    Logger::Instance().Info("EraseEngine",
        "Engine initialized for disk " + std::to_string(deviceNumber)
        + " → " + EraseMethodToString(selectedMethod_));
}

void EraseEngine::SetStrategy(std::unique_ptr<EraseStrategy> strategy) {
    strategy_ = std::move(strategy);
}

std::unique_ptr<EraseStrategy> EraseEngine::ResolveStrategy() const {
    switch (selectedMethod_) {
        case EraseMethod::Overwrite:
            return std::make_unique<HddOverwriteStrategy>();
        case EraseMethod::AtaSecureErase:
            return std::make_unique<AtaSecureEraseStrategy>();
        case EraseMethod::NvmeSanitize:
            return std::make_unique<NvmeSanitizeStrategy>();
        default:
            return nullptr;
    }
}

SanitizeResult EraseEngine::Run() {
    PrivilegeManager::AssertSafeTarget(deviceNumber_);

    if (!strategy_) {
        strategy_ = ResolveStrategy();
    }
    if (!strategy_) {
        SanitizeResult r;
        r.success = false;
        r.method = "None";
        r.errorMessage = "No suitable erase strategy found for disk "
                         + std::to_string(deviceNumber_);
        Logger::Instance().Error("EraseEngine", r.errorMessage);
        return r;
    }

    Logger::Instance().Info("EraseEngine",
        "Starting " + EraseMethodToString(strategy_->Method())
        + " on disk " + std::to_string(deviceNumber_));

    return strategy_->Execute(
        deviceNumber_,
        deviceInfo_.serialNumber,
        deviceInfo_.model);
}

// ── HDD Overwrite (NIST Clear) ───────────────────────────────────────

SanitizeResult HddOverwriteStrategy::Execute(int deviceNumber,
                                              const std::string& serial,
                                              const std::string& model) {
    SanitizeResult result;
    result.method = EraseMethodToString(Method());
    result.deviceSerial = serial;
    result.startTime = Logger::Instance().Timestamp();
    auto start = std::chrono::steady_clock::now();

    Logger::Instance().Info("Overwrite",
        "Beginning NIST Clear overwrite on disk " + std::to_string(deviceNumber));

    std::string path = "\\\\.\\PhysicalDrive" + std::to_string(deviceNumber);
    HANDLE hDisk = CreateFileA(path.c_str(),
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);

    if (hDisk == INVALID_HANDLE_VALUE) {
        result.errorMessage = "Failed to open physical drive: "
                              + std::to_string(GetLastError());
        Logger::Instance().Error("Overwrite", result.errorMessage);
        return result;
    }

    // Dismount all volumes on this disk so writes bypass the volume manager
    DWORD bytesReturned = 0;
    DeviceIoControl(hDisk, FSCTL_LOCK_VOLUME, nullptr, 0, nullptr, 0,
                    &bytesReturned, nullptr);
    DeviceIoControl(hDisk, FSCTL_DISMOUNT_VOLUME, nullptr, 0, nullptr, 0,
                    &bytesReturned, nullptr);

    DISK_GEOMETRY_EX geo{};
    if (!DeviceIoControl(hDisk, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX,
                         nullptr, 0, &geo, sizeof(geo),
                         &bytesReturned, nullptr)) {
        CloseHandle(hDisk);
        result.errorMessage = "Failed to get disk geometry: "
                              + std::to_string(GetLastError());
        Logger::Instance().Error("Overwrite", result.errorMessage);
        return result;
    }

    uint64_t diskSize = geo.DiskSize.QuadPart;
    uint64_t sectorSize = geo.Geometry.BytesPerSector;
    uint64_t writeBlock = 1024ULL * 1024ULL;
    if (writeBlock % sectorSize != 0)
        writeBlock = sectorSize;

    std::vector<uint8_t> zeroBlock(writeBlock, 0);
    uint64_t totalWritten = 0;
    int lastProgress = -1;

    LARGE_INTEGER offset{};
    offset.QuadPart = 0;
    SetFilePointerEx(hDisk, offset, nullptr, FILE_BEGIN);
    bool writeError = false;

    while (totalWritten < diskSize) {
        uint64_t toWrite = (std::min)(writeBlock, diskSize - totalWritten);
        DWORD written = 0;
        if (!WriteFile(hDisk, zeroBlock.data(), static_cast<DWORD>(toWrite),
                       &written, nullptr)) {
            result.errorMessage = "Write error at offset "
                                  + std::to_string(totalWritten)
                                  + ": " + std::to_string(GetLastError());
            Logger::Instance().Error("Overwrite", result.errorMessage);
            writeError = true;
            break;
        }
        if (written == 0) {
            result.errorMessage = "Write operation made no progress at offset "
                                  + std::to_string(totalWritten);
            Logger::Instance().Error("Overwrite", result.errorMessage);
            writeError = true;
            break;
        }
        totalWritten += written;

        int progress = diskSize == 0
            ? 100
            : static_cast<int>((totalWritten * 100ULL) / diskSize);
        if (progress >= lastProgress + 5 || progress == 100) {
            lastProgress = progress;
            Logger::Instance().Info("Overwrite",
                "Progress " + std::to_string(progress) + "% on disk "
                + std::to_string(deviceNumber));
        }
    }

    CloseHandle(hDisk);

    auto end = std::chrono::steady_clock::now();
    result.endTime = Logger::Instance().Timestamp();
    result.durationSeconds = static_cast<int>(
        std::chrono::duration_cast<std::chrono::seconds>(end - start).count());

    if (!writeError) {
        result.success = true;
        Logger::Instance().Info("Overwrite",
            "NIST Clear complete. " + std::to_string(totalWritten)
            + " bytes written to disk " + std::to_string(deviceNumber));
    }
    return result;
}

// ── ATA Secure Erase ─────────────────────────────────────────────────

SanitizeResult AtaSecureEraseStrategy::Execute(int deviceNumber,
                                                const std::string& serial,
                                                const std::string& model) {
    SanitizeResult result;
    result.method = EraseMethodToString(Method());
    result.deviceSerial = serial;
    result.startTime = Logger::Instance().Timestamp();
    auto start = std::chrono::steady_clock::now();

    Logger::Instance().Info("ATASecureErase",
        "Issuing ATA SECURITY ERASE UNIT on disk "
        + std::to_string(deviceNumber));

    std::string path = "\\\\.\\PhysicalDrive" + std::to_string(deviceNumber);
    HANDLE hDisk = CreateFileA(path.c_str(),
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);

    if (hDisk == INVALID_HANDLE_VALUE) {
        result.errorMessage = "Failed to open physical drive: "
                              + std::to_string(GetLastError());
        Logger::Instance().Error("ATASecureErase", result.errorMessage);
        return result;
    }

    // Step 1: SECURITY SET PASSWORD (Master password = all zeros)
    struct {
        ATA_PASS_THROUGH_EX apt;
        UCHAR dataBuf[512];
    } setPwd{};

    setPwd.apt.Length = sizeof(ATA_PASS_THROUGH_EX);
    setPwd.apt.AtaFlags = ATA_FLAGS_DATA_OUT;
    setPwd.apt.DataTransferLength = 512;
    setPwd.apt.TimeOutValue = 30;
    setPwd.apt.DataBufferOffset = sizeof(ATA_PASS_THROUGH_EX);
    setPwd.apt.CurrentTaskFile[0] = 0x00; // Features (identify)
    setPwd.apt.CurrentTaskFile[1] = 0x00;
    setPwd.apt.CurrentTaskFile[2] = 0x00;
    setPwd.apt.CurrentTaskFile[3] = 0x00;
    setPwd.apt.CurrentTaskFile[4] = 0x00;
    setPwd.apt.CurrentTaskFile[5] = 0x00;
    setPwd.apt.CurrentTaskFile[6] = 0x00;
    setPwd.apt.CurrentTaskFile[7] = 0xF1; // SECURITY SET PASSWORD

    // Security buffer: [0]=version, [1]=0, [2..3]=identifier, [4]=0 (master),
    // [8..39]=password (zeros = NULL)
    memset(setPwd.dataBuf, 0, 512);
    setPwd.dataBuf[0] = 0x00; // version
    setPwd.dataBuf[4] = 0x00; // 0=user password

    DWORD returned = 0;
    if (!DeviceIoControl(hDisk, IOCTL_ATA_PASS_THROUGH,
                         &setPwd, sizeof(setPwd), &setPwd, sizeof(setPwd),
                         &returned, nullptr)) {
        result.errorMessage = "SECURITY SET PASSWORD failed: "
                              + std::to_string(GetLastError());
        Logger::Instance().Error("ATASecureErase", result.errorMessage);
        CloseHandle(hDisk);
        return result;
    }

    // Step 2: SECURITY ERASE UNIT
    ATA_PASS_THROUGH_EX erase{};
    erase.Length = sizeof(ATA_PASS_THROUGH_EX);
    erase.AtaFlags = ATA_FLAGS_DATA_IN;
    erase.DataTransferLength = 512;
    erase.TimeOutValue = 120; // 2 minutes for most consumer SSDs
    erase.DataBufferOffset = sizeof(ATA_PASS_THROUGH_EX);
    erase.CurrentTaskFile[0] = 0x00;
    erase.CurrentTaskFile[1] = 0x00;
    erase.CurrentTaskFile[2] = 0x00;
    erase.CurrentTaskFile[3] = 0x00;
    erase.CurrentTaskFile[4] = 0x00;
    erase.CurrentTaskFile[5] = 0x00;
    erase.CurrentTaskFile[6] = 0x84; // LBA=0, master=1
    erase.CurrentTaskFile[7] = 0xF2; // SECURITY ERASE UNIT

    UCHAR buf[512]{};
    if (!DeviceIoControl(hDisk, IOCTL_ATA_PASS_THROUGH,
                         &erase, sizeof(erase),
                         &buf, sizeof(buf),
                         &returned, nullptr)) {
        result.errorMessage = "SECURITY ERASE UNIT failed: "
                              + std::to_string(GetLastError());
        Logger::Instance().Error("ATASecureErase", result.errorMessage);
        CloseHandle(hDisk);
        return result;
    }

    CloseHandle(hDisk);

    auto end = std::chrono::steady_clock::now();
    result.endTime = Logger::Instance().Timestamp();
    result.durationSeconds = static_cast<int>(
        std::chrono::duration_cast<std::chrono::seconds>(end - start).count());
    result.success = true;

    Logger::Instance().Info("ATASecureErase",
        "ATA Secure Erase completed successfully.");
    return result;
}

// ── NVMe Sanitize ────────────────────────────────────────────────────

SanitizeResult NvmeSanitizeStrategy::Execute(int deviceNumber,
                                               const std::string& serial,
                                               const std::string& model) {
    SanitizeResult result;
    result.method = EraseMethodToString(Method());
    result.deviceSerial = serial;
    result.startTime = Logger::Instance().Timestamp();
    auto start = std::chrono::steady_clock::now();

    Logger::Instance().Info("NVMeSanitize",
        "NVMe Sanitize selected for disk " + std::to_string(deviceNumber)
        + " (" + model + ").");

    result.errorMessage =
        "NVMe Sanitize command dispatch is not implemented in this build. "
        "Refusing unsafe overwrite fallback for NVMe media.";

    auto end = std::chrono::steady_clock::now();
    result.endTime = Logger::Instance().Timestamp();
    result.durationSeconds = static_cast<int>(
        std::chrono::duration_cast<std::chrono::seconds>(end - start).count());

    Logger::Instance().Error("NVMeSanitize", result.errorMessage);
    return result;
}
