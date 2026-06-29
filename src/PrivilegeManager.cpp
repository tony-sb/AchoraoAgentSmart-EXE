#include "PrivilegeManager.h"
#include "Logger.h"
#include <stdexcept>
#include <windows.h>
#include <wbemidl.h>
#include <comdef.h>
#include <vector>

#pragma comment(lib, "wbemuuid.lib")

bool PrivilegeManager::IsElevated() {
    HANDLE hToken = nullptr;
    TOKEN_ELEVATION elevation{};
    DWORD dwSize = sizeof(TOKEN_ELEVATION);

    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &hToken))
        return false;

    bool elevated = false;
    if (GetTokenInformation(hToken, TokenElevation, &elevation, dwSize, &dwSize)) {
        elevated = elevation.TokenIsElevated != 0;
    }
    CloseHandle(hToken);
    return elevated;
}

bool PrivilegeManager::IsSystemDisk(int deviceNumber) {
    if (deviceNumber == 0) return true;

    std::vector<int> systemDisks;
    HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(hr)) return false;

    IWbemLocator* pLoc = nullptr;
    IWbemServices* pSvc = nullptr;
    bool found = false;

    hr = CoCreateInstance(CLSID_WbemLocator, nullptr, CLSCTX_INPROC_SERVER,
                          IID_IWbemLocator, reinterpret_cast<void**>(&pLoc));
    if (SUCCEEDED(hr)) {
        hr = pLoc->ConnectServer(
            _bstr_t(L"ROOT\\Microsoft\\Windows\\Storage"),
            nullptr, nullptr, 0L, 0L, nullptr, 0, &pSvc);
        if (SUCCEEDED(hr)) {
            IEnumWbemClassObject* pEnum = nullptr;
            hr = pSvc->ExecQuery(
                _bstr_t(L"WQL"),
                _bstr_t(L"SELECT Number FROM MSFT_Disk WHERE IsSystem=TRUE"),
                WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY,
                nullptr, &pEnum);
            if (SUCCEEDED(hr) && pEnum) {
                IWbemClassObject* pObj = nullptr;
                ULONG ret = 0;
                while (pEnum->Next(WBEM_INFINITE, 1, &pObj, &ret) == S_OK && ret > 0) {
                    VARIANT vt;
                    if (SUCCEEDED(pObj->Get(L"Number", 0, &vt, nullptr, nullptr))) {
                        if (vt.vt == VT_I4 && vt.lVal == deviceNumber) {
                            found = true;
                        }
                        VariantClear(&vt);
                    }
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
    return found;
}

void PrivilegeManager::AssertSafeTarget(int deviceNumber) {
    if (!IsElevated()) {
        Logger::Instance().Error("PrivilegeManager",
            "Process not running as Administrator. Aborting.");
        throw std::runtime_error(
            "This program must be run as Administrator. "
            "Please restart with elevated privileges.");
    }
    if (IsSystemDisk(deviceNumber)) {
        Logger::Instance().Error("PrivilegeManager",
            "Target disk " + std::to_string(deviceNumber)
            + " is the system disk. Refusing to operate on it.");
        throw std::runtime_error(
            "Target disk " + std::to_string(deviceNumber)
            + " is the system disk. Cannot sanitize the OS drive.");
    }
    Logger::Instance().Info("PrivilegeManager",
        "Safety checks passed for disk " + std::to_string(deviceNumber));
}
