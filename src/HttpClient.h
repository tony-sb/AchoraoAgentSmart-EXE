#pragma once
#include <string>

class HttpClient {
public:
    // Realiza una petición HTTPS POST nativa sincrónica usando WinHTTP
    static bool PostJson(const std::string& fullUrl, const std::string& jsonPayload);
};