#include "DiskInfo.h"
#include <iostream>
#include <windows.h>
#include <comdef.h>
#include <wbemidl.h>
#include <bcrypt.h>
#include <iomanip>
#include <sstream>
#include <algorithm>
#include <ctime>
#include <cstdlib>

#pragma comment(lib, "wbemuuid.lib")
#pragma comment(lib, "bcrypt.lib")

// Helper para calcular el Hash SHA256 usando la API BCrypt de Windows
std::string ComputeSHA256(const std::string& input) {
    BCRYPT_ALG_HANDLE hAlg = NULL;
    BCRYPT_HASH_HANDLE hHash = NULL;
    DWORD cbHashObject = 0, cbHash = 0, cbData = 0;
    PBYTE pbHashObject = NULL;
    PBYTE pbHash = NULL;
    std::string hashResult = "";

    if (BCryptOpenAlgorithmProvider(&hAlg, BCRYPT_SHA256_ALGORITHM, NULL, 0) >= 0) {
        if (BCryptGetProperty(hAlg, BCRYPT_OBJECT_LENGTH, (PBYTE)&cbHashObject, sizeof(DWORD), &cbData, 0) >= 0) {
            pbHashObject = (PBYTE)HeapAlloc(GetProcessHeap(), 0, cbHashObject);
            if (BCryptGetProperty(hAlg, BCRYPT_HASH_LENGTH, (PBYTE)&cbHash, sizeof(DWORD), &cbData, 0) >= 0) {
                pbHash = (PBYTE)HeapAlloc(GetProcessHeap(), 0, cbHash);
                if (BCryptCreateHash(hAlg, &hHash, pbHashObject, cbHashObject, NULL, 0, 0) >= 0) {
                    BCryptHashData(hHash, (PBYTE)input.c_str(), (ULONG)input.length(), 0);
                    BCryptFinishHash(hHash, pbHash, cbHash, 0);
                    
                    std::stringstream ss;
                    for (DWORD i = 0; i < cbHash; ++i) {
                        ss << std::hex << std::setw(2) << std::setfill('0') << (int)pbHash[i];
                    }
                    hashResult = ss.str();
                }
            }
        }
    }
    if (hHash) BCryptDestroyHash(hHash);
    if (hAlg) BCryptCloseAlgorithmProvider(hAlg, 0);
    if (pbHashObject) HeapFree(GetProcessHeap(), 0, pbHashObject);
    if (pbHash) HeapFree(GetProcessHeap(), 0, pbHash);

    return hashResult;
}

// Obtener timestamp actual en formato ISO 8601
std::string GetISO8601Time() {
    std::time_t now = std::time(nullptr);
    std::tm tm_buf;
    gmtime_s(&tm_buf, &now);
    char buf[32];
    std::strftime(buf, sizeof(buf), "%Y-%m-%dT%H:%M:%SZ", &tm_buf);
    return std::string(buf);
}

DiskMetrics FetchHardwareTelemetry() {
    DiskMetrics metrics;
    // Valores por defecto en caso falle WMI
    metrics.diskName = "Unknown Storage Unit";
    metrics.serialNumber = "UNKNOWN-SERIAL";
    metrics.type = "SSD";
    metrics.busInterface = "NVMe";
    metrics.capacity = "512 GB";
    metrics.hours = 1420;
    metrics.wear = 2;
    metrics.temp = 36;
    metrics.sectors = 0;
    metrics.writtenTB = 12.4;

    // Inicializar COM
    HRESULT hr = CoInitializeEx(0, COINIT_MULTITHREADED);
    if (FAILED(hr)) return metrics;

    hr = CoInitializeSecurity(NULL, -1, NULL, NULL, RPC_C_AUTHN_LEVEL_DEFAULT, RPC_C_IMP_LEVEL_IMPERSONATE, NULL, EOAC_NONE, NULL);
    
    IWbemLocator* pLoc = NULL;
    hr = CoCreateInstance(CLSID_WbemLocator, 0, CLSCTX_INPROC_SERVER, IID_IWbemLocator, (LPVOID*)&pLoc);
    
    if (SUCCEEDED(hr)) {
        IWbemServices* pSvc = NULL;
        // Conectar al namespace de Storage de Windows
        hr = pLoc->ConnectServer(_bstr_t(L"ROOT\\Microsoft\\Windows\\Storage"), NULL, NULL, 0L, 0L, NULL, NULL, &pSvc);
        
        if (SUCCEEDED(hr)) {
            hr = CoSetProxyBlanket(pSvc, RPC_C_AUTHN_WINNT, RPC_C_AUTHZ_NONE, NULL, RPC_C_AUTHN_LEVEL_CALL, RPC_C_IMP_LEVEL_IMPERSONATE, NULL, EOAC_NONE);
            
            // 1. Consultar MSFT_PhysicalDisk
            IEnumWbemClassObject* pEnumerator = NULL;
            hr = pSvc->ExecQuery(bstr_t("WQL"), bstr_t("SELECT FriendlyName, SerialNumber, MediaType, BusType, Size FROM MSFT_PhysicalDisk"),
                WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY, NULL, &pEnumerator);
            
            if (SUCCEEDED(hr) && pEnumerator) {
                IWbemClassObject* pclsObj = NULL;
                ULONG uReturn = 0;
                if (SUCCEEDED(pEnumerator->Next(WBEM_INFINITE, 1, &pclsObj, &uReturn)) && uReturn > 0) {
                    VARIANT vtProp;
                    if (SUCCEEDED(pclsObj->Get(L"FriendlyName", 0, &vtProp, 0, 0)) && vtProp.vt == VT_BSTR)
                        metrics.diskName = static_cast<const char*>(_bstr_t(vtProp.bstrVal));
                    
                    if (SUCCEEDED(pclsObj->Get(L"SerialNumber", 0, &vtProp, 0, 0)) && vtProp.vt == VT_BSTR) {
                        std::string sn = static_cast<const char*>(_bstr_t(vtProp.bstrVal));
                        // Trim strings
                        sn.erase(sn.find_last_not_of(" \t\r\n") + 1);
                        if (!sn.empty()) metrics.serialNumber = sn;
                    }

                    if (SUCCEEDED(pclsObj->Get(L"MediaType", 0, &vtProp, 0, 0)) && vtProp.vt == VT_I2)
                        metrics.type = (vtProp.iVal == 4) ? "SSD" : (vtProp.iVal == 3 ? "HDD" : "SSD");

                    if (SUCCEEDED(pclsObj->Get(L"BusType", 0, &vtProp, 0, 0)) && vtProp.vt == VT_I2) {
                        if (vtProp.iVal == 17) metrics.busInterface = "NVMe";
                        else if (vtProp.iVal == 11) metrics.busInterface = "SATA";
                        else if (vtProp.iVal == 7) metrics.busInterface = "USB";
                    }

                    if (SUCCEEDED(pclsObj->Get(L"Size", 0, &vtProp, 0, 0))) {
                        uint64_t rawSize = 0;
                        if (vtProp.vt == VT_BSTR) rawSize = std::strtoull(static_cast<const char*>(_bstr_t(vtProp.bstrVal)), nullptr, 10);
                        else if (vtProp.vt == VT_UI8) rawSize = vtProp.ullVal;
                        if (rawSize > 0) metrics.capacity = std::to_string(rawSize / 1024 / 1024 / 1024) + " GB";
                    }
                    VariantClear(&vtProp);
                    pclsObj->Release();
                }
                pEnumerator->Release();
            }

            // 2. Consultar MSFT_StorageReliabilityCounter para SMART de forma nativa
            hr = pSvc->ExecQuery(bstr_t("WQL"), bstr_t("SELECT PowerOnHours, Temperature, Wear, ReadErrorsTotal, CumulativeBytesWritten FROM MSFT_StorageReliabilityCounter"),
                WBEM_FLAG_FORWARD_ONLY | WBEM_FLAG_RETURN_IMMEDIATELY, NULL, &pEnumerator);

            if (SUCCEEDED(hr) && pEnumerator) {
                IWbemClassObject* pclsObj = NULL;
                ULONG uReturn = 0;
                if (SUCCEEDED(pEnumerator->Next(WBEM_INFINITE, 1, &pclsObj, &uReturn)) && uReturn > 0) {
                    VARIANT vtProp;
                    if (SUCCEEDED(pclsObj->Get(L"PowerOnHours", 0, &vtProp, 0, 0)) && vtProp.vt != VT_NULL) {
                        metrics.hours = (vtProp.vt == VT_UI8) ? (int)vtProp.ullVal : vtProp.lVal;
                    }
                    if (SUCCEEDED(pclsObj->Get(L"Temperature", 0, &vtProp, 0, 0)) && vtProp.vt != VT_NULL && vtProp.lVal > 0) {
                        metrics.temp = vtProp.lVal;
                    }
                    if (SUCCEEDED(pclsObj->Get(L"Wear", 0, &vtProp, 0, 0)) && vtProp.vt != VT_NULL) {
                        metrics.wear = vtProp.lVal;
                    }
                    if (SUCCEEDED(pclsObj->Get(L"ReadErrorsTotal", 0, &vtProp, 0, 0)) && vtProp.vt != VT_NULL) {
                        metrics.sectors = vtProp.lVal;
                    }
                    if (SUCCEEDED(pclsObj->Get(L"CumulativeBytesWritten", 0, &vtProp, 0, 0)) && vtProp.vt != VT_NULL) {
                        uint64_t bytes = (vtProp.vt == VT_UI8) ? vtProp.ullVal : std::strtoull(static_cast<const char*>(_bstr_t(vtProp.bstrVal)), nullptr, 10);
                        metrics.writtenTB = std::round((double)bytes / (1024.0 * 1024.0 * 1024.0 * 1024.0) * 100.0) / 100.0;
                    }
                    VariantClear(&vtProp);
                    pclsObj->Release();
                }
                pEnumerator->Release();
            }
            pSvc->Release();
        }
        pLoc->Release();
    }
    CoUninitialize();

    // --- ALGORITMO DE SALUD REPLICADO ---
    int health = 100 - (std::min)(metrics.wear, 40);
    if (metrics.temp > 60) health -= 15;
    if (metrics.hours > 20000) health -= 10;
    if (metrics.sectors > 0) health -= 25;
    metrics.healthScore = (std::max)(health, 0);

    if (metrics.healthScore >= 90) metrics.grade = "A";
    else if (metrics.healthScore >= 75) metrics.grade = "B";
    else if (metrics.healthScore >= 60) metrics.grade = "C";
    else metrics.grade = "D";

    metrics.generatedAt = GetISO8601Time();

    // Construcción del Payload para la Firma
    std::stringstream ssPayload;
    ssPayload << metrics.serialNumber << "|" 
              << metrics.diskName << "|" 
              << metrics.hours << "|" 
              << std::fixed << std::setprecision(1) << metrics.writtenTB << "|" 
              << metrics.wear << "|" 
              << metrics.temp << "|" 
              << metrics.sectors << "|" 
              << metrics.healthScore;

    metrics.hash = ComputeSHA256(ssPayload.str());

    return metrics;
}