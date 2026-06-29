#include "Logger.h"
#include <iostream>
#include <chrono>
#include <iomanip>
#include <sstream>

Logger& Logger::Instance() {
    static Logger instance;
    return instance;
}

Logger::~Logger() {
    Close();
}

void Logger::Open(const std::string& filePath) {
    std::lock_guard<std::mutex> lock(mtx_);
    if (!filePath.empty()) {
        file_.open(filePath, std::ios::app);
        if (file_.is_open()) {
            console_ = true;
        }
    }
}

void Logger::Close() {
    std::lock_guard<std::mutex> lock(mtx_);
    if (file_.is_open()) {
        file_.close();
    }
}

void Logger::Log(LogLevel level, const std::string& module, const std::string& message) {
    std::lock_guard<std::mutex> lock(mtx_);
    auto line = Timestamp() + " [" + LevelPrefix(level) + "] [" + module + "] " + message;

    if (console_) {
        std::cout << line << std::endl;
    }
    if (file_.is_open()) {
        file_ << line << std::endl;
    }
}

std::string Logger::LevelPrefix(LogLevel level) const {
    switch (level) {
        case LogLevel::Debug:   return "DEBUG";
        case LogLevel::Info:    return "INFO";
        case LogLevel::Warning: return "WARN";
        case LogLevel::Error:   return "ERROR";
    }
    return "UNKN";
}

std::string Logger::Timestamp() const {
    auto now = std::chrono::system_clock::now();
    auto t = std::chrono::system_clock::to_time_t(now);
    std::tm tm{};
#ifdef _WIN32
    gmtime_s(&tm, &t);
#else
    gmtime_r(&t, &tm);
#endif
    auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
        now.time_since_epoch()) % 1000;
    std::ostringstream oss;
    oss << std::put_time(&tm, "%Y-%m-%dT%H:%M:%S")
        << '.' << std::setw(3) << std::setfill('0') << ms.count()
        << 'Z';
    return oss.str();
}
