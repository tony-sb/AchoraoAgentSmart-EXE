#include "StorageModule.h"
#include "Logger.h"
#include <windows.h>
#include <wbemidl.h>
#include <comdef.h>
#include <cstdint>
#include <sstream>
#include <algorithm>
#include <cstdlib>

#pragma comment(lib, "wbemuuid.lib")

static std::string Trim(const std::string& s) {
    size_t start = s.find_first_not_of(" \t\r\n");
    size_t end = s.find_last_not_of(" \t\r\n");
    return (start == std::string::npos) ? "" : s.substr(start, end - start + 1);
}

StorageInfo StorageModule::Detect(int deviceNumber) {
    Logger::Instance().Info("StorageModule",
        "Detecting storage device #" + std::to_string(deviceNumber));

    StorageInfo info;
    info.deviceNumber = deviceNumber;

    HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(hr)) return info;

    IWbemLocator* pLoc = nullptr;
    IWbemServices* pSvc = nullptr;
    hr = CoCreateInstance(CLSID_WbemLocator, nullptr, CLSCTX_INPROC_SERVER,
                          IID_IWbemLocator, reinterpret_cast<void**>(&pLoc));
    if (SUCCEEDED(hr)) {
        hr = pLoc->ConnectServer(
            _bstr_t(L"ROOT\\Microsoft\\Windows\\Storage"),
            nullptr, nullptr, 0L, 0L, nullptr, nullptr, &pSvc);
        if (SUCCEEDED(hr)) {
            std::wstring wql = L"SELECT Number, FriendlyName, SerialNumber, "
                               L"MediaType, BusType, Size, Model "
                               L"FROM MSFT_PhysicalDisk WHERE Number="
                               + std::to_wstring(deviceNumber);
            IEnumWbemClassObject* pEnum = nullptr;
            hr = pSvc->ExecQuery(
                _bstr_t(L"WQL"), _bstr_t(wql.c_str()),
                WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
                nullptr, &pEnum);
            if (SUCCEEDED(hr) && pEnum) {
                IWbemClassObject* pObj = nullptr;
                ULONG ret = 0;
                if (pEnum->Next(WBEM_INFINITE, 1, &pObj, &ret) == S_OK && ret > 0) {
                    VARIANT vt{};
                    if (SUCCEEDED(pObj->Get(L"FriendlyName", 0, &vt, nullptr, nullptr))) {
                        if (vt.vt == VT_BSTR) info.model = Trim(static_cast<const char*>(_bstr_t(vt.bstrVal)));
                        VariantClear(&vt);
                    }
                    if (SUCCEEDED(pObj->Get(L"SerialNumber", 0, &vt, nullptr, nullptr))) {
                        if (vt.vt == VT_BSTR) info.serialNumber = Trim(static_cast<const char*>(_bstr_t(vt.bstrVal)));
                        VariantClear(&vt);
                    }
                    if (SUCCEEDED(pObj->Get(L"Model", 0, &vt, nullptr, nullptr))) {
                        if (vt.vt == VT_BSTR) {
                            std::string m = Trim(static_cast<const char*>(_bstr_t(vt.bstrVal)));
                            if (!m.empty()) info.model = m;
                        }
                        VariantClear(&vt);
                    }
                    if (SUCCEEDED(pObj->Get(L"MediaType", 0, &vt, nullptr, nullptr))) {
                        if (vt.vt == VT_I2) {
                            info.mediaType = (vt.iVal == 3) ? "HDD" : "SSD";
                        }
                        VariantClear(&vt);
                    }
                    if (SUCCEEDED(pObj->Get(L"BusType", 0, &vt, nullptr, nullptr))) {
                        if (vt.vt == VT_I2) {
                            switch (vt.iVal) {
                                case 17: info.busInterface = "NVMe"; break;
                                case 11: info.busInterface = "SATA"; break;
                                case 7:  info.busInterface = "USB";  break;
                                case 5:  info.busInterface = "SATA"; break;
                                default: info.busInterface = "Unknown(" + std::to_string(vt.iVal) + ")";
                            }
                        }
                        VariantClear(&vt);
                    }
                    if (SUCCEEDED(pObj->Get(L"Size", 0, &vt, nullptr, nullptr))) {
                        uint64_t raw = 0;
                        if (vt.vt == VT_UI8) raw = vt.ullVal;
                        else if (vt.vt == VT_BSTR) raw = std::strtoull(static_cast<const char*>(_bstr_t(vt.bstrVal)), nullptr, 10);
                        if (raw > 0) {
                            info.capacityBytes = raw;
                            uint64_t gb = raw / (1024ULL * 1024ULL * 1024ULL);
                            info.capacity = std::to_string(gb) + " GB";
                        }
                        VariantClear(&vt);
                    }

                    pObj->Release();
                }
                pEnum->Release();
            }
            pSvc->Release();
        }
        pLoc->Release();
    }
    CoUninitialize();

    if (info.serialNumber.empty()) {
        Logger::Instance().Warn("StorageModule",
            "Device #" + std::to_string(deviceNumber) + " returned no serial.");
        info.serialNumber = "N/A";
    }

    Logger::Instance().Info("StorageModule",
        "Detected: " + info.model + " | " + info.mediaType + " | "
        + info.busInterface + " | " + info.capacity);
    return info;
}

EraseMethod StorageModule::Classify(const StorageInfo& info) {
    if (info.busInterface == "NVMe") {
        Logger::Instance().Info("StorageModule",
            "Classified as NVMe → using NVMe Sanitize");
        return EraseMethod::NvmeSanitize;
    }
    if (info.mediaType == "HDD") {
        Logger::Instance().Info("StorageModule",
            "Classified as HDD → using Overwrite (NIST Clear)");
        return EraseMethod::Overwrite;
    }
    Logger::Instance().Info("StorageModule",
        "Classified as SSD SATA → using ATA Secure Erase");
    return EraseMethod::AtaSecureErase;
}
