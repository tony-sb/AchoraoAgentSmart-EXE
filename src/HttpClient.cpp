#include "HttpClient.h"
#include <windows.h>
#include <winhttp.h>
#include <iostream>
#include <vector>

#pragma comment(lib, "winhttp.lib")

bool HttpClient::PostJson(const std::string& fullUrl, const std::string& jsonPayload) {
    bool success = false;
    HINTERNET hSession = NULL, hConnect = NULL, hRequest = NULL;
    URL_COMPONENTS urlComp = { 0 };
    
    // Parsear la URL
    wchar_t wHostName[256] = { 0 };
    wchar_t wUrlPath[2048] = { 0 };
    
    urlComp.dwStructSize = sizeof(urlComp);
    urlComp.lpszHostName = wHostName;
    urlComp.dwHostNameLength = sizeof(wHostName) / sizeof(wchar_t);
    urlComp.lpszUrlPath = wUrlPath;
    urlComp.dwUrlPathLength = sizeof(wUrlPath) / sizeof(wchar_t);
    
    std::wstring wUrl(fullUrl.begin(), fullUrl.end());
    if (!WinHttpCrackUrl(wUrl.c_str(), (DWORD)wUrl.length(), 0, &urlComp)) {
        return false;
    }

    hSession = WinHttpOpen(L"Achorao Native Agent v1.0.4/1.0", WINHTTP_ACCESS_TYPE_DEFAULT_PROXY, WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    if (hSession) {
        hConnect = WinHttpConnect(hSession, wHostName, urlComp.nPort, 0);
    }

    if (hConnect) {
        DWORD dwFlags = (urlComp.nScheme == INTERNET_SCHEME_HTTPS) ? WINHTTP_FLAG_SECURE : 0;
        hRequest = WinHttpOpenRequest(hConnect, L"POST", wUrlPath, NULL, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, dwFlags);
    }

    if (hRequest) {
        // Headers obligatorios
        wchar_t headers[] = L"Content-Type: application/json\r\n";
        
        success = WinHttpSendRequest(hRequest, headers, (DWORD)-1, (LPVOID)jsonPayload.c_str(), (DWORD)jsonPayload.length(), (DWORD)jsonPayload.length(), 0);
        if (success) {
            success = WinHttpReceiveResponse(hRequest, NULL);
        }
    }

    // Limpieza de Handlers nativos
    if (hRequest) WinHttpCloseHandle(hRequest);
    if (hConnect) WinHttpCloseHandle(hConnect);
    if (hSession) WinHttpCloseHandle(hSession);

    return success;
}