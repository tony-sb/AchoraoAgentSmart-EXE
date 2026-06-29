#include "MemoryModule.h"
#include "Logger.h"
#include <windows.h>
#include <wbemidl.h>
#include <comdef.h>
#include <sstream>
#include <cstdlib>

#pragma comment(lib, "wbemuuid.lib")

std::vector<MemoryModuleInfo> MemoryModule::DetectAll() {
    Logger::Instance().Info("MemoryModule", "Enumerating RAM modules...");
    std::vector<MemoryModuleInfo> results;

    HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(hr)) return results;

    IWbemLocator* pLoc = nullptr;
    IWbemServices* pSvc = nullptr;
    hr = CoCreateInstance(CLSID_WbemLocator, nullptr, CLSCTX_INPROC_SERVER,
                          IID_IWbemLocator, reinterpret_cast<void**>(&pLoc));
    if (SUCCEEDED(hr)) {
        hr = pLoc->ConnectServer(
            _bstr_t(L"ROOT\\CIMV2"), nullptr, nullptr, 0L, 0L, nullptr, nullptr, &pSvc);
        if (SUCCEEDED(hr)) {
            IEnumWbemClassObject* pEnum = nullptr;
            hr = pSvc->ExecQuery(
                _bstr_t(L"WQL"),
                _bstr_t(L"SELECT Capacity, Manufacturer, PartNumber, SerialNumber, "
                        L"Speed, FormFactor, MemoryType, ConfiguredVoltage "
                        L"FROM Win32_PhysicalMemory"),
                WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
                nullptr, &pEnum);
            if (SUCCEEDED(hr) && pEnum) {
                IWbemClassObject* pObj = nullptr;
                ULONG ret = 0;
                while (pEnum->Next(WBEM_INFINITE, 1, &pObj, &ret) == S_OK && ret > 0) {
                    MemoryModuleInfo mod;
                    mod.isPresent = true;
                    VARIANT vt{};

                    if (SUCCEEDED(pObj->Get(L"Capacity", 0, &vt, nullptr, nullptr))) {
                        uint64_t capBytes = 0;
                        if (vt.vt == VT_BSTR) capBytes = std::strtoull(static_cast<const char*>(_bstr_t(vt.bstrVal)), nullptr, 10);
                        else if (vt.vt == VT_UI8) capBytes = vt.ullVal;
                        if (capBytes > 0) {
                            mod.capacity = std::to_string(capBytes / (1024ULL * 1024ULL)) + " MB";
                        }
                        VariantClear(&vt);
                    }
                    if (SUCCEEDED(pObj->Get(L"Manufacturer", 0, &vt, nullptr, nullptr))) {
                        if (vt.vt == VT_BSTR) {
                            std::string m = static_cast<const char*>(_bstr_t(vt.bstrVal));
                            if (m != "Unknown" && m != "(Standard)")
                                mod.manufacturer = m;
                        }
                        VariantClear(&vt);
                    }
                    if (SUCCEEDED(pObj->Get(L"PartNumber", 0, &vt, nullptr, nullptr))) {
                        if (vt.vt == VT_BSTR) mod.partNumber = static_cast<const char*>(_bstr_t(vt.bstrVal));
                        VariantClear(&vt);
                    }
                    if (SUCCEEDED(pObj->Get(L"SerialNumber", 0, &vt, nullptr, nullptr))) {
                        if (vt.vt == VT_BSTR) mod.serialNumber = static_cast<const char*>(_bstr_t(vt.bstrVal));
                        VariantClear(&vt);
                    }
                    if (SUCCEEDED(pObj->Get(L"Speed", 0, &vt, nullptr, nullptr))) {
                        if (vt.vt == VT_I4) mod.speed = std::to_string(vt.lVal) + " MHz";
                        VariantClear(&vt);
                    }
                    if (SUCCEEDED(pObj->Get(L"FormFactor", 0, &vt, nullptr, nullptr))) {
                        if (vt.vt == VT_I2) {
                            switch (vt.iVal) {
                                case 8:  mod.formFactor = "DIMM";   break;
                                case 12: mod.formFactor = "SODIMM"; break;
                                case 9:  mod.formFactor = "RIMM";   break;
                                default: mod.formFactor = "Other(" + std::to_string(vt.iVal) + ")";
                            }
                        }
                        VariantClear(&vt);
                    }
                    if (SUCCEEDED(pObj->Get(L"MemoryType", 0, &vt, nullptr, nullptr))) {
                        if (vt.vt == VT_I2) {
                            switch (vt.iVal) {
                                case 26: mod.memoryType = "DDR4"; break;
                                case 34: mod.memoryType = "DDR5"; break;
                                case 20: mod.memoryType = "DDR3"; break;
                                case 21: mod.memoryType = "DDR2"; break;
                                default: mod.memoryType = "Type(" + std::to_string(vt.iVal) + ")";
                            }
                        }
                        VariantClear(&vt);
                    }
                    if (SUCCEEDED(pObj->Get(L"ConfiguredVoltage", 0, &vt, nullptr, nullptr))) {
                        if (vt.vt == VT_I4) mod.configuredVoltageMv = vt.lVal;
                        VariantClear(&vt);
                    }

                    results.push_back(std::move(mod));
                    pObj->Release();
                    ret = 0;
                }
                pEnum->Release();
            }
            pSvc->Release();
        }
        pLoc->Release();
    }
    CoUninitialize();

    Logger::Instance().Info("MemoryModule",
        "Found " + std::to_string(results.size()) + " RAM module(s).");
    return results;
}
