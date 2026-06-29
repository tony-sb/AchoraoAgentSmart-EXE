#include "GpuModule.h"
#include "Logger.h"
#include <windows.h>
#include <wbemidl.h>
#include <comdef.h>

#pragma comment(lib, "wbemuuid.lib")

std::vector<GpuInfo> GpuModule::DetectAll() {
    Logger::Instance().Info("GpuModule", "Enumerating GPU adapters...");
    std::vector<GpuInfo> results;

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
                _bstr_t(L"SELECT Name, AdapterCompatibility, VideoProcessor, "
                        L"AdapterRAM, DriverVersion, VideoModeDescription, "
                        L"CurrentHorizontalResolution, CurrentVerticalResolution, "
                        L"InstalledDisplayDrivers, Status "
                        L"FROM Win32_VideoController"),
                WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
                nullptr, &pEnum);
            if (SUCCEEDED(hr) && pEnum) {
                IWbemClassObject* pObj = nullptr;
                ULONG ret = 0;
                while (pEnum->Next(WBEM_INFINITE, 1, &pObj, &ret) == S_OK && ret > 0) {
                    GpuInfo gpu;
                    VARIANT vt{};

                    if (SUCCEEDED(pObj->Get(L"Name", 0, &vt, nullptr, nullptr))) {
                        if (vt.vt == VT_BSTR) gpu.name = static_cast<const char*>(_bstr_t(vt.bstrVal));
                        VariantClear(&vt);
                    }
                    if (SUCCEEDED(pObj->Get(L"AdapterCompatibility", 0, &vt, nullptr, nullptr))) {
                        if (vt.vt == VT_BSTR) gpu.vendor = static_cast<const char*>(_bstr_t(vt.bstrVal));
                        VariantClear(&vt);
                    }
                    if (SUCCEEDED(pObj->Get(L"DriverVersion", 0, &vt, nullptr, nullptr))) {
                        if (vt.vt == VT_BSTR) gpu.driverVersion = static_cast<const char*>(_bstr_t(vt.bstrVal));
                        VariantClear(&vt);
                    }
                    if (SUCCEEDED(pObj->Get(L"AdapterRAM", 0, &vt, nullptr, nullptr))) {
                        if (vt.vt == VT_I4) gpu.dedicatedMemoryBytes = static_cast<uint64_t>(vt.lVal);
                        else if (vt.vt == VT_UI4) gpu.dedicatedMemoryBytes = vt.ulVal;
                        VariantClear(&vt);
                    }
                    if (SUCCEEDED(pObj->Get(L"Status", 0, &vt, nullptr, nullptr))) {
                        if (vt.vt == VT_BSTR) {
                            std::string s = static_cast<const char*>(_bstr_t(vt.bstrVal));
                            gpu.healthPercent = (s == "OK") ? 100 : 50;
                            gpu.hasError = (s != "OK");
                        }
                        VariantClear(&vt);
                    }

                    results.push_back(std::move(gpu));
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

    Logger::Instance().Info("GpuModule",
        "Found " + std::to_string(results.size()) + " GPU adapter(s).");
    return results;
}
