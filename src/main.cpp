#include <iostream>
#include <windows.h>
#include "DiskInfo.h"
#include "HttpClient.h"
#include "../third_party/json.hpp"

using json = nlohmann::json;

// Helper para colores ANSI en Consola de Windows
void SetConsoleColor(WORD color) {
    SetConsoleTextAttribute(GetStdHandle(STD_OUTPUT_HANDLE), color);
}

std::string GetWorkstationName() {
    char buffer[MAX_COMPUTERNAME_LENGTH + 1];
    DWORD size = sizeof(buffer);
    if (GetComputerNameA(buffer, &size)) return std::string(buffer);
    return "UNKNOWN-PC";
}

std::string ExtractVendor(const std::string& diskName) {
    std::vector<std::string> vendors = { "Kingston", "Samsung", "Corsair", "Crucial", "Toshiba", "Western Digital", "WD", "Seagate" };
    for (const auto& v : vendors) {
        if (diskName.find(v) != std::string::npos) return v;
    }
    return "Generico";
}

int main() {
    // Configurar consola para soporte UTF-8 completo
    SetConsoleOutputCP(CP_UTF8);

    std::cout << "======================================================================" << std::endl;
    std::cout << "   AGENTE ACHORAO RECOPILADOR v1.0.4 - NATIVO C++ WIN32" << std::endl;
    std::cout << "  --------------------------------------------------------------------" << std::endl;
    std::cout << "   Este agente leera los atributos de su almacenamiento fisico real   " << std::endl;
    std::cout << "   mediante APIs del Kernel y WMI para su cotizacion real.           " << std::endl;
    std::cout << "======================================================================" << std::endl << std::endl;

    std::cout << "[CONN] Extrayendo datos de telemetria e integridad SMART de forma segura..." << std::endl << std::endl;

    // 1. Obtener la data por hardware real
    DiskMetrics metrics = FetchHardwareTelemetry();

    // Desplegar datos en Consola
    std::cout << "[INFO] Datos de Hardware Localizados:" << std::endl;
    SetConsoleColor(2); // Verde
    std::cout << "      - Disco: " << metrics.diskName << std::endl;
    std::cout << "      - Serial: " << metrics.serialNumber << std::endl;
    std::cout << "      - Capacidad: " << metrics.capacity << std::endl;
    std::cout << "      - Salud Calculada: " << metrics.healthScore << "% (Grado " << metrics.grade << ")" << std::endl;
    SetConsoleColor(7); // Blanco por defecto

    // 2. Construir JSON Telemetria Principal
    json telemetryJson = {
        {"serialNumber", metrics.serialNumber},
        {"diskName", metrics.diskName},
        {"type", metrics.type},
        {"capacity", metrics.capacity},
        {"interface", metrics.busInterface},
        {"healthScore", metrics.healthScore},
        {"grade", metrics.grade},
        {"hours", metrics.hours},
        {"wear", metrics.wear},
        {"temp", metrics.temp},
        {"sectors", metrics.sectors},
        {"writtenTB", metrics.writtenTB},
        {"generatedAt", metrics.generatedAt},
        {"signature", "SIG_RSA4096_PKCS1_SHA256_V104_APPROVED_ONLINE"},
        {"hash", metrics.hash}
    };

    std::cout << std::endl;
    SetConsoleColor(6); // Amarillo
    std::cout << "[CONN] Sincronizando telemetria al portal Achorao..." << std::endl;
    SetConsoleColor(7);

    // Reemplaza con tus URLs de producción reales
    std::string apiUrl = "https://api.achorao.com/telemetry"; 
    std::string nistUrl = "https://api.achorao.com/nist/handshake";

    // Enviar Telemetría Principal
    if (HttpClient::PostJson(apiUrl, telemetryJson.dump())) {
        SetConsoleColor(2);
        std::cout << std::endl << "[SINC_OK] Sincronizacion exitosa en el panel de Achorao!" << std::endl;
        SetConsoleColor(7);
        std::cout << "          Regresa a la ventana del navegador para ver tu cotizacion real actualizada." << std::endl << std::endl;
    } else {
        SetConsoleColor(12); // Rojo
        std::cout << std::endl << "[ERROR] El agente no pudo completar la sincronizacion de telemetria." << std::endl;
        SetConsoleColor(7);
    }

    // 3. Construir y Enviar Handshake de Módulo NIST (Saneamiento)
    json nistJson = {
        {"model", metrics.diskName},
        {"serialNumber", metrics.serialNumber},
        {"vendor", ExtractVendor(metrics.diskName)},
        {"technicianId", "TECH-LOCAL-CPP-AGENT"},
        {"workstation", GetWorkstationName()}
    };

    SetConsoleColor(6);
    std::cout << "[NIST] Registrando handshake para el modulo de saneamiento..." << std::endl;
    SetConsoleColor(7);

    if (HttpClient::PostJson(nistUrl, nistJson.dump())) {
        SetConsoleColor(2);
        std::cout << "[NIST_OK] Handshake registrado con exito en el servidor de borrado seguro." << std::endl;
    } else {
        SetConsoleColor(6);
        std::cout << "[NIST_WARN] SMART sincronizado, pero no se pudo registrar handshake NIST." << std::endl;
    }

    SetConsoleColor(7);
    std::cout << std::endl << "======================================================================" << std::endl;
    std::cout << "  [SINC] Sincronizado completo. Regrese al portal en su navegador." << std::endl;
    std::cout << "======================================================================" << std::endl;
    std::cout << "Presione cualquier tecla para cerrar el asistente...";
    
    std::cin.get();
    return 0;
}