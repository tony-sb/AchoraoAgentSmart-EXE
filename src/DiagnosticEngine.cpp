#include "DiagnosticEngine.h"
#include "Logger.h"
#include <windows.h>
#include <winioctl.h>
#include <wbemidl.h>
#include <comdef.h>
#include <algorithm>
#include <cstdint>
#include <vector>
#include <random>
#include <sstream>
#include <cmath>
#include <cstdlib>

#pragma comment(lib, "wbemuuid.lib")

// ── SMART via WMI ────────────────────────────────────────────────────

SmartData DiagnosticEngine::ReadSmart(int deviceNumber) {
    Logger::Instance().Info("DiagnosticEngine",
        "Reading SMART data for disk " + std::to_string(deviceNumber));
    SmartData data;

    HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(hr)) return data;

    IWbemLocator* pLoc = nullptr;
    IWbemServices* pSvc = nullptr;
    hr = CoCreateInstance(CLSID_WbemLocator, nullptr, CLSCTX_INPROC_SERVER,
                          IID_IWbemLocator, reinterpret_cast<void**>(&pLoc));
    if (SUCCEEDED(hr)) {
        hr = pLoc->ConnectServer(
            _bstr_t(L"ROOT\\Microsoft\\Windows\\Storage"),
            nullptr, nullptr, 0L, 0L, nullptr, nullptr, &pSvc);
        if (SUCCEEDED(hr)) {
            IEnumWbemClassObject* pEnum = nullptr;
            // MSFT_StorageReliabilityCounter
            hr = pSvc->ExecQuery(
                _bstr_t(L"WQL"),
                _bstr_t(L"SELECT * FROM MSFT_StorageReliabilityCounter"),
                WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
                nullptr, &pEnum);
            if (SUCCEEDED(hr) && pEnum) {
                IWbemClassObject* pObj = nullptr;
                ULONG ret = 0;
                if (pEnum->Next(WBEM_INFINITE, 1, &pObj, &ret) == S_OK && ret > 0) {
                    VARIANT vt{};
                    if (SUCCEEDED(pObj->Get(L"Temperature", 0, &vt, nullptr, nullptr))
                        && vt.vt != VT_NULL && vt.lVal > 0)
                        data.temperature = vt.lVal;
                    VariantClear(&vt);

                    if (SUCCEEDED(pObj->Get(L"PowerOnHours", 0, &vt, nullptr, nullptr))
                        && vt.vt != VT_NULL) {
                        data.powerOnHours = (vt.vt == VT_UI8)
                            ? static_cast<int>(vt.ullVal) : vt.lVal;
                    }
                    VariantClear(&vt);

                    if (SUCCEEDED(pObj->Get(L"Wear", 0, &vt, nullptr, nullptr))
                        && vt.vt != VT_NULL)
                        data.wearLevel = vt.lVal;
                    VariantClear(&vt);

                    if (SUCCEEDED(pObj->Get(L"ReadErrorsTotal", 0, &vt, nullptr, nullptr))
                        && vt.vt != VT_NULL)
                        data.readErrorsTotal = vt.lVal;
                    VariantClear(&vt);

                    if (SUCCEEDED(pObj->Get(L"CumulativeBytesWritten", 0, &vt, nullptr, nullptr))
                        && vt.vt != VT_NULL) {
                        uint64_t bytes = (vt.vt == VT_UI8)
                            ? vt.ullVal
                            : std::strtoull(static_cast<const char*>(_bstr_t(vt.bstrVal)), nullptr, 10);
                        data.writtenTeraBytes = std::round(
                            static_cast<double>(bytes) / (1024.0 * 1024.0 * 1024.0 * 1024.0)
                            * 100.0) / 100.0;
                    }
                    VariantClear(&vt);

                    // MSFT_PhysicalDisk for ReallocatedSectors etc.
                    pObj->Release();
                }
                pEnum->Release();
            }

            // Also query MSFT_PhysicalDisk for sector info
            hr = pSvc->ExecQuery(
                _bstr_t(L"WQL"),
                _bstr_t(L"SELECT NumberOfMediaErrors, ReallocatedSectors "
                        L"FROM MSFT_PhysicalDisk"),
                WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
                nullptr, &pEnum);
            if (SUCCEEDED(hr) && pEnum) {
                IWbemClassObject* pObj = nullptr;
                ULONG ret = 0;
                if (pEnum->Next(WBEM_INFINITE, 1, &pObj, &ret) == S_OK && ret > 0) {
                    VARIANT vt{};
                    if (SUCCEEDED(pObj->Get(L"ReallocatedSectors", 0, &vt, nullptr, nullptr))
                        && vt.vt != VT_NULL)
                        data.reallocatedSectors = vt.lVal;
                    VariantClear(&vt);
                    if (SUCCEEDED(pObj->Get(L"NumberOfMediaErrors", 0, &vt, nullptr, nullptr))
                        && vt.vt != VT_NULL)
                        data.uncorrectableErrors = vt.lVal;
                    VariantClear(&vt);
                    pObj->Release();
                }
                pEnum->Release();
            }
            pSvc->Release();
        }
        pLoc->Release();
    }
    CoUninitialize();

    data.healthPercent = 100
        - (std::min)(data.wearLevel, 40)
        - (data.temperature > 60 ? 15 : 0)
        - (data.powerOnHours > 20000 ? 10 : 0)
        - (data.reallocatedSectors > 0 ? 25 : 0);
    if (data.healthPercent < 0) data.healthPercent = 0;

    Logger::Instance().Info("DiagnosticEngine",
        "SMART read: temp=" + std::to_string(data.temperature)
        + "C hours=" + std::to_string(data.powerOnHours)
        + " wear=" + std::to_string(data.wearLevel)
        + "% health=" + std::to_string(data.healthPercent) + "%");
    return data;
}

// ── Block Verification ───────────────────────────────────────────────

VerifyResult DiagnosticEngine::VerifySanitization(
    int deviceNumber, const std::string& serial, int sampleBlocks) {

    Logger::Instance().Info("DiagnosticEngine",
        "Verifying sanitization on disk " + std::to_string(deviceNumber)
        + " with " + std::to_string(sampleBlocks) + " sample blocks.");
    VerifyResult result;

    std::string path = "\\\\.\\PhysicalDrive" + std::to_string(deviceNumber);
    HANDLE hDisk = CreateFileA(path.c_str(),
        GENERIC_READ,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        nullptr, OPEN_EXISTING, 0, nullptr);

    if (hDisk == INVALID_HANDLE_VALUE) {
        result.details = "Failed to open drive: " + std::to_string(GetLastError());
        Logger::Instance().Error("DiagnosticEngine", result.details);
        return result;
    }

    DISK_GEOMETRY_EX geo{};
    DWORD bytesReturned = 0;
    if (!DeviceIoControl(hDisk, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX,
                         nullptr, 0, &geo, sizeof(geo),
                         &bytesReturned, nullptr)) {
        result.details = "Failed to get disk geometry: "
                         + std::to_string(GetLastError());
        CloseHandle(hDisk);
        Logger::Instance().Error("DiagnosticEngine", result.details);
        return result;
    }

    uint64_t diskSize = geo.DiskSize.QuadPart;
    uint64_t sectorSize = geo.Geometry.BytesPerSector;
    uint64_t totalSectors = diskSize / sectorSize;

    // Clamp sample size
    if (sampleBlocks <= 0) sampleBlocks = 64;
    if (sampleBlocks > static_cast<int>(totalSectors))
        sampleBlocks = static_cast<int>(totalSectors);

    std::random_device rd;
    std::mt19937_64 gen(rd());
    std::uniform_int_distribution<uint64_t> dist(0, totalSectors - 1);

    const int blockSize = 512;
    std::vector<uint8_t> buffer(blockSize);
    result.blocksSampled = 0;
    result.blocksWithNonZero = 0;

    LARGE_INTEGER offset{};
    for (int i = 0; i < sampleBlocks; ++i) {
        uint64_t sector = dist(gen);
        offset.QuadPart = sector * static_cast<long long>(sectorSize);
        if (!SetFilePointerEx(hDisk, offset, nullptr, FILE_BEGIN)) {
            continue;
        }
        DWORD readBytes = 0;
        if (!ReadFile(hDisk, buffer.data(), blockSize, &readBytes, nullptr)) {
            continue;
        }
        if (readBytes != blockSize) continue;

        result.blocksSampled++;

        bool allZero = true;
        bool allFF = true;
        for (int b = 0; b < blockSize; ++b) {
            if (buffer[b] != 0x00) allZero = false;
            if (buffer[b] != 0xFF) allFF = false;
            if (!allZero && !allFF) break;
        }

        if (!allZero && !allFF) {
            result.blocksWithNonZero++;
        }
    }

    CloseHandle(hDisk);

    result.passed = (result.blocksWithNonZero == 0);
    if (result.passed) {
        result.details = "All " + std::to_string(result.blocksSampled)
                        + " sampled blocks contain only 0x00 or 0xFF. "
                        "Sanitization verified.";
        Logger::Instance().Info("DiagnosticEngine", result.details);
    } else {
        result.details = std::to_string(result.blocksWithNonZero)
                        + " out of " + std::to_string(result.blocksSampled)
                        + " blocks contain non-zero/non-FF data. "
                        "Sanitization INCOMPLETE.";
        Logger::Instance().Error("DiagnosticEngine", result.details);
    }
    return result;
}
